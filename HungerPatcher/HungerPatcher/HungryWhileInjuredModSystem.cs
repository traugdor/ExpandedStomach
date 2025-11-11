using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System;
using Vintagestory.Server;

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
            ModLogger = api.Logger;
            Mod.Logger.Notification("Patching serverside...");

            if (!patched)
            {
                //prefix EntityBehaviorHunger.ReducedSaturation with harmony
                var h = new Harmony("HungerPatcher");
                Mod.Logger.Notification("Getting ReduceSaturation method...");
                MethodInfo ReduceSaturationMethod = typeof(EntityBehaviorHunger).GetMethod("ReduceSaturation", BindingFlags.NonPublic | BindingFlags.Instance);
                Mod.Logger.Notification("Generating links to pre/post fix methods...");
                HarmonyMethod prefix = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ReduceSaturationPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                HarmonyMethod postfix = new HarmonyMethod(typeof(HungryWhileInjuredModSystem).GetMethod(nameof(ReduceSaturationPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                Mod.Logger.Notification("Patching...");
                h.Patch(ReduceSaturationMethod, prefix: prefix, postfix: postfix);
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
    }
}
