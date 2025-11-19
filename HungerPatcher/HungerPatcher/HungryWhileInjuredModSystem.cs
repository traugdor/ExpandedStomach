using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System;
using Vintagestory.Server;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using Vintagestory.API.MathTools;

namespace HungryWhileInjured
{
    public class HungryWhileInjuredModSystem : ModSystem
    {
        private static bool patched = false;
        ICoreServerAPI theMod;
        private static ILogger ModLogger;

        public override void StartPre(ICoreAPI api)
        {
            api.RegisterEntityBehaviorClass("entitybehavior.hungrywhileinjured", typeof(EntityBehaviorHungryWhileInjured));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            theMod = api;
            ModLogger = Mod.Logger;
            Mod.Logger.Notification("Patching serverside...");

            if (!patched)
            {
                //prefix EntityBehaviorHunger.ReducedSaturation with harmony
                var h = new Harmony("HungerPatcher");
                Mod.Logger.Notification("Getting ReduceSaturation method...");
                MethodInfo ReduceSaturationMethod = typeof(EntityBehaviorHunger).GetMethod("ReduceSaturation", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Mod.Logger.Notification("Generating links to pre/post fix methods...");
                HarmonyMethod prefix = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ReduceSaturationPrefix), 
                    BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod postfix = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ReduceSaturationPostfix), 
                    BindingFlags.NonPublic | BindingFlags.Static));
                Mod.Logger.Notification("Patching...");
                h.Patch(ReduceSaturationMethod, prefix: prefix, postfix: postfix);
                Mod.Logger.Notification("Getting ApplyRegenAndHunger method...");
                MethodInfo ApplyRegenAndHungerMethod = typeof(EntityBehaviorHealth).GetMethod("ApplyRegenAndHunger", 
                    BindingFlags.NonPublic | BindingFlags.Instance );
                Mod.Logger.Notification("Generating link to patch method...");
                HarmonyMethod patch = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ApplyRegenAndHungerPatch),
                    BindingFlags.NonPublic | BindingFlags.Static));
                Mod.Logger.Notification("Patching...");
                h.Patch(ApplyRegenAndHungerMethod, transpiler: patch);
                Mod.Logger.Notification("Done!");
                patched = true;
            }
            else
            {
                Mod.Logger.Notification("Already patched!");
            }
        }

        private static bool ReduceSaturationPrefix(EntityBehaviorHunger __instance, float satLossMultiplier, out EntityBehaviorHunger __state)
        {
            __state = __instance;

            return true;
        }

        private static void ReduceSaturationPostfix(EntityBehaviorHunger __instance, EntityBehaviorHunger __state)
        {
            //get hungercounter from __state using reflection
            FieldInfo hungerCounterField = typeof(EntityBehaviorHunger).GetField("hungerCounter", BindingFlags.NonPublic | BindingFlags.Instance);
            float stateHC = (float)hungerCounterField.GetValue(__state);

            //set hungercounter to __instance using reflection
            hungerCounterField.SetValue(__instance, stateHC);
        }

        private static IEnumerable<CodeInstruction> ApplyRegenAndHungerPatch(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);

            int index = 0;

            //find call to GameMath.Clamp that stores into stloc.3
            for (int i = 0; i < codes.Count-1; i++) //stop before last since we're comparing i+1 as well
            {
                if (codes[i].opcode == OpCodes.Call 
                    && (MethodInfo)codes[i].operand == typeof(GameMath).GetMethod("Clamp", [typeof(float), typeof(float), typeof(float)])
                    && codes[i + 1].opcode == OpCodes.Stloc_3)
                {
                    index = i+1; // index is the index of the stloc.3. We are injecting after it.
                    break;
                }
            }
            if (index != 0 && index < codes.Count - 1)
            {
                index++;
                var inject = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0) //load __instance
                    , new CodeInstruction(OpCodes.Ldloc_3) //load local var 3
                    , new CodeInstruction(OpCodes.Ldloc, 6) //load local var 6 (hungerBehavior)
                    , new CodeInstruction(OpCodes.Call, typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ApplyNutritionToRegenBoost), BindingFlags.NonPublic | BindingFlags.Static)) //get boosted amount
                    , new CodeInstruction(OpCodes.Stloc_3) //store back into local var 3
                };
                codes.InsertRange(index, inject);
            }
            else
            {
                throw new Exception("Could not find location to inject code. Aborting Patch.");
            }
            return codes.AsEnumerable();
        }

        private static float ApplyNutritionToRegenBoost(EntityBehaviorHealth __instance, float healthRegenPerGameSecond, EntityBehaviorHunger hungerBehavior)
        {
            float boostedRegen = healthRegenPerGameSecond;
            
            if (hungerBehavior != null) 
            {
                float fruitBoost = hungerBehavior.FruitLevel / 1500f;
                float vegBoost = hungerBehavior.VegetableLevel / 1500f;
                float proteinBoost = hungerBehavior.ProteinLevel / 1500f;
                float grainBoost = hungerBehavior.GrainLevel / 1500f;
                float dairyBoost = hungerBehavior.DairyLevel / 1500f;

                float boostAmount = (fruitBoost + vegBoost + proteinBoost + grainBoost + dairyBoost) / 100f;
                boostedRegen = healthRegenPerGameSecond * (1f + boostAmount);
            }
            return boostedRegen;
        }
    }
}
