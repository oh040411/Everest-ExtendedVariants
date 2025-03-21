﻿using Celeste;
using Celeste.Mod;
using ExtendedVariants.Module;
using ExtendedVariants.Variants;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;

namespace ExtendedVariants {
    public class ExtendedVariantTriggerManager {

        private Dictionary<ExtendedVariantsModule.Variant, int> overridenVariantsInRoom = new Dictionary<ExtendedVariantsModule.Variant, int>();
        private Dictionary<ExtendedVariantsModule.Variant, int> overridenVariantsInRoomRevertOnLeave = new Dictionary<ExtendedVariantsModule.Variant, int>();
        private Dictionary<ExtendedVariantsModule.Variant, int> oldVariantsInRoom = new Dictionary<ExtendedVariantsModule.Variant, int>();
        private Dictionary<ExtendedVariantsModule.Variant, int> variantValuesBeforeOverride = new Dictionary<ExtendedVariantsModule.Variant, int>();
        private string modColorGradeBeforeOverride = null;

        public void Load() {
            Everest.Events.Level.OnEnter += onLevelEnter;
            Everest.Events.Player.OnSpawn += onPlayerSpawn;
            Everest.Events.Level.OnTransitionTo += onLevelTransitionTo;
            Everest.Events.Level.OnExit += onLevelExit;
            IL.Celeste.ChangeRespawnTrigger.OnEnter += modRespawnTriggerOnEnter;
        }

        public void Unload() {
            Everest.Events.Level.OnEnter -= onLevelEnter;
            Everest.Events.Player.OnSpawn -= onPlayerSpawn;
            Everest.Events.Level.OnTransitionTo -= onLevelTransitionTo;
            Everest.Events.Level.OnExit -= onLevelExit;
            IL.Celeste.ChangeRespawnTrigger.OnEnter -= modRespawnTriggerOnEnter;
        }

        /// <summary>
        /// Restore extended variants values when entering a saved level.
        /// </summary>
        /// <param name="session">unused</param>
        /// <param name="fromSaveData">true if loaded from save data, false otherwise</param>
        private void onLevelEnter(Session session, bool fromSaveData) {
            // failsafe: if VariantsEnabledViaTrigger is null, initialize it. THIS SHOULD NEVER HAPPEN, but already happened in a case of a corrupted save.
            if ((ExtendedVariantsModule.Session?.VariantsEnabledViaTrigger ?? null) == null) {
                Logger.Log(LogLevel.Warn, "ExtendedVariantMode/ExtendedVariantTriggerManager", "Session was null. This should not happen. Initializing it to an empty session.");
                ExtendedVariantsModule.Instance._Session = new ExtendedVariantsSession();
            }
            if (fromSaveData) {
                foreach (ExtendedVariantsModule.Variant v in ExtendedVariantsModule.Session.VariantsEnabledViaTrigger.Keys) {
                    Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", $"Loading save: restoring {v} to {ExtendedVariantsModule.Session.VariantsEnabledViaTrigger[v]}");

                    if (v == ExtendedVariantsModule.Variant.ColorGrading) {
                        modColorGradeBeforeOverride = ExtendedVariantsModule.Settings.ModColorGrade;
                    }

                    int oldValue = setVariantValue(v, ExtendedVariantsModule.Session.VariantsEnabledViaTrigger[v], out _);
                    variantValuesBeforeOverride[v] = oldValue;
                }
            }
        }

        /// <summary>
        /// Handle respawn (reset variants that were set in the room).
        /// </summary>
        /// <param name="obj">unused</param>
        private void onPlayerSpawn(Player obj) {
            if (oldVariantsInRoom.Count != 0) {
                // reset all variants that got set in the room
                foreach (ExtendedVariantsModule.Variant v in oldVariantsInRoom.Keys) {
                    Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", $"Died in room: resetting {v} to {oldVariantsInRoom[v]}");
                    setVariantValue(v, oldVariantsInRoom[v], out _);

                    if (v == ExtendedVariantsModule.Variant.ColorGrading) {
                        ExtendedVariantsModule.Settings.ModColorGrade = modColorGradeBeforeOverride;
                    }
                }

                // clear values
                Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", "Room state reset");
                oldVariantsInRoom.Clear();
                overridenVariantsInRoom.Clear();
                overridenVariantsInRoomRevertOnLeave.Clear();
            }
        }

        /// <summary>
        /// Handle screen transitions (make variants set within the room permanent).
        /// </summary>
        /// <param name="level">unused</param>
        /// <param name="next">unused</param>
        /// <param name="direction">unused</param>
        private void onLevelTransitionTo(Level level, LevelData next, Vector2 direction) {
            commitVariantChanges();
        }

        /// <summary>
        /// Edits the OnEnter method in ChangeRespawnTrigger, so that the variants set are made permanent when the respawn point is changed.
        /// </summary>
        /// <param name="il">Object allowing CIL patching</param>
        private void modRespawnTriggerOnEnter(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            // simply jump into the "if" controlling whether the respawn should be changed or not
            // (yet again, this is brtrue.s in XNA and brfalse.s in FNA. Thanks compiler.)
            if (cursor.TryGotoNext(MoveType.After, instr => (instr.OpCode == OpCodes.Brtrue_S || instr.OpCode == OpCodes.Brfalse_S))) {
                // and call our method in there
                Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", $"Inserting call to CommitVariantChanges at index {cursor.Index} in CIL code for OnEnter in ChangeRespawnTrigger");
                cursor.EmitDelegate<Action>(commitVariantChanges);
            }
        }

        /// <summary>
        /// Make the changes in variant settings permanent (even if the player dies).
        /// </summary>
        private void commitVariantChanges() {
            if (overridenVariantsInRoom.Count != 0) {
                // "commit" variants set in the room to save slot
                foreach (ExtendedVariantsModule.Variant v in overridenVariantsInRoom.Keys) {
                    Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", $"Committing variant change {v} to {overridenVariantsInRoom[v]}");
                    ExtendedVariantsModule.Session.VariantsEnabledViaTrigger[v] = overridenVariantsInRoom[v];
                }

                // clear values
                Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", "Room state reset");
                oldVariantsInRoom.Clear();
                overridenVariantsInRoom.Clear();
                overridenVariantsInRoomRevertOnLeave.Clear();
            }
        }

        private void onLevelExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow) {
            ResetVariantsOnLevelExit();
        }

        internal void ResetVariantsOnLevelExit() {
            if (variantValuesBeforeOverride.Count != 0) {
                // reset all variants that got set during the session
                foreach (ExtendedVariantsModule.Variant v in variantValuesBeforeOverride.Keys) {
                    Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", $"Ending session: resetting {v} to {variantValuesBeforeOverride[v]}");
                    setVariantValue(v, variantValuesBeforeOverride[v], out _);

                    if (v == ExtendedVariantsModule.Variant.ColorGrading) {
                        ExtendedVariantsModule.Settings.ModColorGrade = modColorGradeBeforeOverride;
                    }
                }
            }

            // exiting level: clear state
            Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", "Room and session state reset");
            overridenVariantsInRoom.Clear();
            overridenVariantsInRoomRevertOnLeave.Clear();
            oldVariantsInRoom.Clear();
            variantValuesBeforeOverride.Clear();
            modColorGradeBeforeOverride = null;

            ExtendedVariantsModule.Instance.SaveSettings();
        }

        public int OnEnteredInTrigger(ExtendedVariantsModule.Variant variantChange, int newValue, bool revertOnLeave) {
            return OnEnteredInTrigger(variantChange, newValue, revertOnLeave, isFade: false);
        }

        public int OnEnteredInTrigger(ExtendedVariantsModule.Variant variantChange, int newValue, bool revertOnLeave, bool isFade) {
            return OnEnteredInTrigger(variantChange, newValue, revertOnLeave, isFade, revertOnDeath: true);
        }

        public int OnEnteredInTrigger(ExtendedVariantsModule.Variant variantChange, int newValue, bool revertOnLeave, bool isFade, bool revertOnDeath) {
            // change the variant value
            if (variantChange == ExtendedVariantsModule.Variant.ColorGrading && modColorGradeBeforeOverride == null) {
                modColorGradeBeforeOverride = ExtendedVariantsModule.Settings.ModColorGrade;
            }

            int oldValue = setVariantValue(variantChange, newValue, out int actualNewValue);

            if (!variantValuesBeforeOverride.ContainsKey(variantChange)) {
                variantValuesBeforeOverride[variantChange] = oldValue;
            }

            if (revertOnDeath) {
                // store the fact that the variant was changed within the room
                // so that it can be reverted if we die, or saved if we save & quit later
                // fade triggers get a special tag, because it can very quickly flood logs (1 line per frame) and needs to be turned on only when necessary.
                Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager" + (isFade ? "-fade" : ""), $"Triggered ExtendedVariantTrigger: changed {variantChange} from {oldValue} to {newValue} (revertOnLeave = {revertOnLeave}) => variant set to {actualNewValue}");

                if (!oldVariantsInRoom.ContainsKey(variantChange)) {
                    oldVariantsInRoom[variantChange] = oldValue;
                }
                if (revertOnLeave) {
                    overridenVariantsInRoomRevertOnLeave[variantChange] = actualNewValue;
                } else {
                    overridenVariantsInRoom[variantChange] = actualNewValue;
                }
            } else {
                Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", $"Triggered ExtendedVariantTrigger: changed and committed {variantChange} from {oldValue} to {newValue} => variant set to {actualNewValue}");

                // remove the variant from the room state if it was in there...
                oldVariantsInRoom.Remove(variantChange);
                overridenVariantsInRoom.Remove(variantChange);
                overridenVariantsInRoomRevertOnLeave.Remove(variantChange);

                // ... and save it straight into session.
                ExtendedVariantsModule.Session.VariantsEnabledViaTrigger[variantChange] = actualNewValue;
            }

            return oldValue;
        }

        public void OnExitedRevertOnLeaveTrigger(ExtendedVariantsModule.Variant variantChange, int oldValueToRevertOnLeave) {
            setVariantValue(variantChange, oldValueToRevertOnLeave, out _);

            if (variantChange == ExtendedVariantsModule.Variant.ColorGrading) {
                ExtendedVariantsModule.Settings.ModColorGrade = modColorGradeBeforeOverride;
            }

            overridenVariantsInRoomRevertOnLeave[variantChange] = oldValueToRevertOnLeave;
            Logger.Log("ExtendedVariantMode/ExtendedVariantTriggerManager", $"Left ExtendedVariantTrigger: reverted {variantChange} to {oldValueToRevertOnLeave}");
        }

        public int GetExpectedVariantValue(ExtendedVariantsModule.Variant variant) {
            if (overridenVariantsInRoomRevertOnLeave.ContainsKey(variant)) {
                // variant was replaced in current room in "revert on leave" mode: we expect this value to be set.
                return overridenVariantsInRoomRevertOnLeave[variant];
            }
            if (overridenVariantsInRoom.ContainsKey(variant)) {
                // variant was replaced in current room: we expect this value to be set.
                return overridenVariantsInRoom[variant];
            }
            if (ExtendedVariantsModule.Session.VariantsEnabledViaTrigger.ContainsKey(variant)) {
                // variant was replaced in a previous room: we expect this value to be set.
                return ExtendedVariantsModule.Session.VariantsEnabledViaTrigger[variant];
            }
            // no variant trigger has been used: we expect the default value.
            return ExtendedVariantTrigger.GetDefaultValueForVariant(variant);
        }

        public int GetCurrentVariantValue(ExtendedVariantsModule.Variant variant) {
            switch (variant) {
                case ExtendedVariantsModule.Variant.ChaserCount: return ExtendedVariantsModule.Settings.ChaserCount;
                case ExtendedVariantsModule.Variant.AffectExistingChasers: return ExtendedVariantsModule.Settings.AffectExistingChasers ? 1 : 0;
                case ExtendedVariantsModule.Variant.RefillJumpsOnDashRefill: return ExtendedVariantsModule.Settings.RefillJumpsOnDashRefill ? 1 : 0;
                case ExtendedVariantsModule.Variant.HiccupStrength: return ExtendedVariantsModule.Settings.HiccupStrength;
                case ExtendedVariantsModule.Variant.SnowballDelay: return ExtendedVariantsModule.Settings.SnowballDelay;
                case ExtendedVariantsModule.Variant.BadelineLag: return ExtendedVariantsModule.Settings.BadelineLag;
                case ExtendedVariantsModule.Variant.DelayBetweenBadelines: return ExtendedVariantsModule.Settings.DelayBetweenBadelines;
                case ExtendedVariantsModule.Variant.OshiroCount: return ExtendedVariantsModule.Settings.OshiroCount;
                case ExtendedVariantsModule.Variant.ReverseOshiroCount: return ExtendedVariantsModule.Settings.ReverseOshiroCount;
                case ExtendedVariantsModule.Variant.DisableOshiroSlowdown: return ExtendedVariantsModule.Settings.DisableOshiroSlowdown ? 1 : 0;
                case ExtendedVariantsModule.Variant.DisableSeekerSlowdown: return ExtendedVariantsModule.Settings.DisableSeekerSlowdown ? 1 : 0;
                case ExtendedVariantsModule.Variant.BadelineAttackPattern: return ExtendedVariantsModule.Settings.BadelineAttackPattern;
                case ExtendedVariantsModule.Variant.ChangePatternsOfExistingBosses: return ExtendedVariantsModule.Settings.ChangePatternsOfExistingBosses ? 1 : 0;
                case ExtendedVariantsModule.Variant.FirstBadelineSpawnRandom: return ExtendedVariantsModule.Settings.FirstBadelineSpawnRandom ? 1 : 0;
                case ExtendedVariantsModule.Variant.BadelineBossCount: return ExtendedVariantsModule.Settings.BadelineBossCount;
                case ExtendedVariantsModule.Variant.BadelineBossNodeCount: return ExtendedVariantsModule.Settings.BadelineBossNodeCount;
                case ExtendedVariantsModule.Variant.RisingLavaSpeed: return ExtendedVariantsModule.Settings.RisingLavaSpeed;
                case ExtendedVariantsModule.Variant.AllowThrowingTheoOffscreen: return ExtendedVariantsModule.Settings.AllowThrowingTheoOffscreen ? 1 : 0;
                case ExtendedVariantsModule.Variant.AllowLeavingTheoBehind: return ExtendedVariantsModule.Settings.AllowLeavingTheoBehind ? 1 : 0;
                case ExtendedVariantsModule.Variant.DisableSuperBoosts: return ExtendedVariantsModule.Settings.DisableSuperBoosts ? 1 : 0;
                case ExtendedVariantsModule.Variant.DontRefillStaminaOnGround: return ExtendedVariantsModule.Settings.DontRefillStaminaOnGround ? 1 : 0;
                default: return ExtendedVariantsModule.Instance.VariantHandlers[variant].GetValue();
            }
        }

        /// <summary>
        /// Sets a variant value.
        /// </summary>
        /// <param name="variantChange">The variant to change</param>
        /// <param name="newValue">The new value</param>
        /// <returns>The old value for this variant</returns>
        private int setVariantValue(ExtendedVariantsModule.Variant variantChange, int newValue, out int actualNewValue) {
            int oldValue;

            switch (variantChange) {
                case ExtendedVariantsModule.Variant.ChaserCount:
                    oldValue = ExtendedVariantsModule.Settings.ChaserCount;
                    ExtendedVariantsModule.Settings.ChaserCount = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.AffectExistingChasers:
                    oldValue = ExtendedVariantsModule.Settings.AffectExistingChasers ? 1 : 0;
                    ExtendedVariantsModule.Settings.AffectExistingChasers = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.RefillJumpsOnDashRefill:
                    oldValue = ExtendedVariantsModule.Settings.RefillJumpsOnDashRefill ? 1 : 0;
                    ExtendedVariantsModule.Settings.RefillJumpsOnDashRefill = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.HiccupStrength:
                    oldValue = ExtendedVariantsModule.Settings.HiccupStrength;
                    ExtendedVariantsModule.Settings.HiccupStrength = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.SnowballDelay:
                    oldValue = ExtendedVariantsModule.Settings.SnowballDelay;
                    ExtendedVariantsModule.Settings.SnowballDelay = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.BadelineLag:
                    oldValue = ExtendedVariantsModule.Settings.BadelineLag;
                    ExtendedVariantsModule.Settings.BadelineLag = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.DelayBetweenBadelines:
                    oldValue = ExtendedVariantsModule.Settings.DelayBetweenBadelines;
                    ExtendedVariantsModule.Settings.DelayBetweenBadelines = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.OshiroCount:
                    oldValue = ExtendedVariantsModule.Settings.OshiroCount;
                    ExtendedVariantsModule.Settings.OshiroCount = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.ReverseOshiroCount:
                    oldValue = ExtendedVariantsModule.Settings.ReverseOshiroCount;
                    ExtendedVariantsModule.Settings.ReverseOshiroCount = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.DisableOshiroSlowdown:
                    oldValue = ExtendedVariantsModule.Settings.DisableOshiroSlowdown ? 1 : 0;
                    ExtendedVariantsModule.Settings.DisableOshiroSlowdown = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.DisableSeekerSlowdown:
                    oldValue = ExtendedVariantsModule.Settings.DisableSeekerSlowdown ? 1 : 0;
                    ExtendedVariantsModule.Settings.DisableSeekerSlowdown = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.BadelineAttackPattern:
                    oldValue = ExtendedVariantsModule.Settings.BadelineAttackPattern;
                    ExtendedVariantsModule.Settings.BadelineAttackPattern = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.ChangePatternsOfExistingBosses:
                    oldValue = ExtendedVariantsModule.Settings.ChangePatternsOfExistingBosses ? 1 : 0;
                    ExtendedVariantsModule.Settings.ChangePatternsOfExistingBosses = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.FirstBadelineSpawnRandom:
                    oldValue = ExtendedVariantsModule.Settings.FirstBadelineSpawnRandom ? 1 : 0;
                    ExtendedVariantsModule.Settings.FirstBadelineSpawnRandom = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.BadelineBossCount:
                    oldValue = ExtendedVariantsModule.Settings.BadelineBossCount;
                    ExtendedVariantsModule.Settings.BadelineBossCount = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.BadelineBossNodeCount:
                    oldValue = ExtendedVariantsModule.Settings.BadelineBossNodeCount;
                    ExtendedVariantsModule.Settings.BadelineBossNodeCount = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.RisingLavaSpeed:
                    oldValue = ExtendedVariantsModule.Settings.RisingLavaSpeed;
                    ExtendedVariantsModule.Settings.RisingLavaSpeed = newValue;
                    actualNewValue = newValue;
                    break;
                case ExtendedVariantsModule.Variant.AllowThrowingTheoOffscreen:
                    oldValue = ExtendedVariantsModule.Settings.AllowThrowingTheoOffscreen ? 1 : 0;
                    ExtendedVariantsModule.Settings.AllowThrowingTheoOffscreen = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.AllowLeavingTheoBehind:
                    oldValue = ExtendedVariantsModule.Settings.AllowLeavingTheoBehind ? 1 : 0;
                    ExtendedVariantsModule.Settings.AllowLeavingTheoBehind = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.DisableSuperBoosts:
                    oldValue = ExtendedVariantsModule.Settings.DisableSuperBoosts ? 1 : 0;
                    ExtendedVariantsModule.Settings.DisableSuperBoosts = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                case ExtendedVariantsModule.Variant.DontRefillStaminaOnGround:
                    oldValue = ExtendedVariantsModule.Settings.DontRefillStaminaOnGround ? 1 : 0;
                    ExtendedVariantsModule.Settings.DontRefillStaminaOnGround = (newValue != 0);
                    actualNewValue = (newValue != 0 ? 1 : 0);
                    break;
                default:
                    AbstractExtendedVariant variant = ExtendedVariantsModule.Instance.VariantHandlers[variantChange];
                    oldValue = variant.GetValue();
                    variant.SetValue(newValue);
                    actualNewValue = variant.GetValue();
                    break;
            }

            ExtendedVariantsModule.Instance.Randomizer.RefreshEnabledVariantsDisplayList();

            return oldValue;
        }
    }
}
