using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace HungryWhileInjured
{
    public class HungryWhileInjuredModSystem : ModSystem
    {
        private static bool patched = false;
        private static ILogger ModLogger;
        private static bool es = false;

        private static readonly AccessTools.FieldRef<EntityBehaviorHunger, float> HungerCounterRef =
            AccessTools.FieldRefAccess<EntityBehaviorHunger, float>("hungerCounter");

        public override void StartPre(ICoreAPI api)
        {
            api.RegisterEntityBehaviorClass("entitybehavior.hungrywhileinjured", typeof(EntityBehaviorHungryWhileInjured));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ModLogger = Mod.Logger;

            if (api.ModLoader.IsModEnabled("expandedstomach"))
            {
                es = true;
            }

            if (!patched)
            {
                var h = new Harmony("HungerPatcher");

                MethodInfo reduceSaturation = typeof(EntityBehaviorHunger).GetMethod("ReduceSaturation",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (reduceSaturation == null)
                {
                    Mod.Logger.Error("HungryWhileInjured: could not find EntityBehaviorHunger.ReduceSaturation — hunger counter patch skipped.");
                }
                else
                {
                    HarmonyMethod prefix  = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ReduceSaturationPrefix),  BindingFlags.NonPublic | BindingFlags.Static));
                    HarmonyMethod postfix = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ReduceSaturationPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                    h.Patch(reduceSaturation, prefix: prefix, postfix: postfix);
                }

                MethodInfo applyRegenAndHunger = typeof(EntityBehaviorHealth).GetMethod("ApplyRegenAndHunger",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (applyRegenAndHunger == null)
                {
                    Mod.Logger.Error("HungryWhileInjured: could not find EntityBehaviorHealth.ApplyRegenAndHunger — nutrition regen boost patch skipped.");
                }
                else
                {
                    HarmonyMethod patch = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ApplyRegenAndHungerPatch), BindingFlags.NonPublic | BindingFlags.Static));
                    h.Patch(applyRegenAndHunger, transpiler: patch);
                }

                patched = true;
                Mod.Logger.Notification("HungryWhileInjured patched!");
            }
            else
            {
                Mod.Logger.Notification("HungryWhileInjured already patched.");
            }
        }

        /// <summary>
        /// Snapshots the hunger counter before <c>ReduceSaturation</c> runs so that the
        /// postfix can restore it, preventing health regen from resetting the counter and
        /// blocking normal hunger drain ticks.
        /// </summary>
        private static bool ReduceSaturationPrefix(EntityBehaviorHunger __instance, out float __state)
        {
            __state = HungerCounterRef(__instance);
            return true;
        }

        /// <summary>
        /// Restores the hunger counter to the value captured in the prefix.
        /// </summary>
        private static void ReduceSaturationPostfix(EntityBehaviorHunger __instance, float __state)
        {
            HungerCounterRef(__instance) = __state;
        }

        private static IEnumerable<CodeInstruction> ApplyRegenAndHungerPatch(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);

            int index = 0;

            // Find the call to GameMath.Clamp followed by stloc.3, which stores the final regen amount.
            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].opcode == OpCodes.Call
                    && (MethodInfo)codes[i].operand == typeof(GameMath).GetMethod("Clamp", [typeof(float), typeof(float), typeof(float)])
                    && codes[i + 1].opcode == OpCodes.Stloc_3)
                {
                    index = i + 1;
                    break;
                }
            }
            if (index != 0 && index < codes.Count - 1)
            {
                index++;
                var inject = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldloc_3),
                    new CodeInstruction(OpCodes.Ldloc, 6),
                    new CodeInstruction(OpCodes.Call, typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ApplyNutritionToRegenBoost), BindingFlags.NonPublic | BindingFlags.Static)),
                    new CodeInstruction(OpCodes.Stloc_3)
                };
                codes.InsertRange(index, inject);
            }
            else
            {
                throw new Exception("HungryWhileInjured: could not find injection point in ApplyRegenAndHunger — aborting transpiler patch.");
            }
            return codes.AsEnumerable();
        }

        /// <summary>
        /// Scales the health regen amount upward based on the player's current nutrition levels
        /// and, if Expanded Stomach is installed, their current stomach size.
        /// </summary>
        private static float ApplyNutritionToRegenBoost(EntityBehaviorHealth __instance, float healthRegenPerGameSecond, EntityBehaviorHunger hungerBehavior)
        {
            if (hungerBehavior == null)
                return healthRegenPerGameSecond;

            float fruitBoost    = hungerBehavior.FruitLevel      / 1500f;
            float vegBoost      = hungerBehavior.VegetableLevel  / 1500f;
            float proteinBoost  = hungerBehavior.ProteinLevel    / 1500f;
            float grainBoost    = hungerBehavior.GrainLevel      / 1500f;
            float dairyBoost    = hungerBehavior.DairyLevel      / 1500f;

            float boostAmount = (fruitBoost + vegBoost + proteinBoost + grainBoost + dairyBoost) / 100f;

            if (es)
            {
                var entity = hungerBehavior.entity as EntityPlayer;
                var stomachBehavior = entity?.GetBehavior("expandedStomach");
                if (stomachBehavior != null)
                {
                    float stomachSize = stomachBehavior.GetType().GetProperty("StomachSize")?.GetValue(stomachBehavior) as float? ?? 0f;
                    boostAmount += stomachSize / 5000f;
                }
            }

            return healthRegenPerGameSecond * (1f + boostAmount);
        }
    }
}
