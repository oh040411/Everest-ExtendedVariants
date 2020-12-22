﻿using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ExtendedVariants.Variants {
    class BoostMultiplier : AbstractExtendedVariant {
        private static ILHook playerOrigWallJumpHook;

        public override int GetDefaultValue() {
            return 10;
        }

        public override int GetValue() {
            return Settings.BoostMultiplier;
        }

        public override void SetValue(int value) {
            Settings.BoostMultiplier = value;
        }

        public override void Load() {
            // hooking methods returning Vector2 is cursed. so let's hook its usages instead.
            IL.Celeste.Player.LaunchedBoostCheck += hookLiftBoostUsages;
            IL.Celeste.Player.Jump += hookLiftBoostUsages;
            IL.Celeste.Player.SuperJump += hookLiftBoostUsages;
            IL.Celeste.Player.SuperWallJump += hookLiftBoostUsages;

            playerOrigWallJumpHook = new ILHook(typeof(Player).GetMethod("orig_WallJump", BindingFlags.NonPublic | BindingFlags.Instance), hookLiftBoostUsages);
        }

        public override void Unload() {
            IL.Celeste.Player.LaunchedBoostCheck -= hookLiftBoostUsages;
            IL.Celeste.Player.Jump -= hookLiftBoostUsages;
            IL.Celeste.Player.SuperJump -= hookLiftBoostUsages;
            IL.Celeste.Player.SuperWallJump -= hookLiftBoostUsages;

            playerOrigWallJumpHook?.Dispose();
            playerOrigWallJumpHook = null;
        }

        private void hookLiftBoostUsages(ILContext il) {
            ILCursor cursor = new ILCursor(il);
            while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<Player>("get_LiftBoost"))) {
                Logger.Log("ExtendedVariantMode/BoostMultiplier", $"Modding lift boost at {cursor.Index} in IL for {il.Method.FullName}");

                // turn LiftBoost into LiftBoost * (Settings.BoostMultiplier / 10f)
                cursor.EmitDelegate<Func<float>>(() => Settings.BoostMultiplier / 10f);
                cursor.Emit(OpCodes.Call, typeof(Vector2).GetMethod("op_Multiply", new Type[] { typeof(Vector2), typeof(float) }));
            }
        }
    }
}
