using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System;

namespace HungerPatcher
{
    public class HungerPatcherModSystem : ModSystem
    {
        private static bool patched = false;
        ICoreServerAPI theMod;
        private static ILogger ModLogger;

        public override void StartPre(ICoreAPI api)
        {
            api.RegisterEntityBehaviorClass("hungerpatcher", typeof(EntityBehaviorHungerPatcher));
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
                Mod.Logger.Notification("Generating link to prefix method...");
                HarmonyMethod prefix = new HarmonyMethod(typeof(HungerPatcherModSystem).GetMethod(nameof(ReduceSaturationPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                Mod.Logger.Notification("Patching...");
                h.Patch(ReduceSaturationMethod, prefix);
                Mod.Logger.Notification("Done!");
                patched = true;
            }
            else
            {
                Mod.Logger.Notification("Already patched!");
            }
        }

        private static bool ReduceSaturationPrefix(EntityBehaviorHunger __instance, float satLossMultiplier)
        {
            FieldInfo hungerCounterField = typeof(EntityBehaviorHunger).GetField("hungerCounter", BindingFlags.NonPublic | BindingFlags.Instance);
            //ModLogger.Notification("hungerCounter: " + (float)hungerCounterField?.GetValue(__instance));
            float hungerCounter = (float)hungerCounterField?.GetValue(__instance);
            //ModLogger.Notification("Value of hungerCounter is " + hungerCounter);
            float oldHunger = hungerCounter + 10f; // restore previous value
            float satlossDelayFruit = __instance.SaturationLossDelayFruit;
            float satlossDelayVegetable = __instance.SaturationLossDelayVegetable;
            float satlossDelayProtein = __instance.SaturationLossDelayProtein;
            float satlossDelayGrain = __instance.SaturationLossDelayGrain;
            float satlossDelayDairy = __instance.SaturationLossDelayDairy;
            var hpbehavior = __instance.entity.GetBehavior("hungerpatcher") as EntityBehaviorHungerPatcher;
            DateTime dt = DateTime.Now;
            if (hpbehavior != null) dt = hpbehavior.dt;
            
            if ((satlossDelayFruit > 0f
             || satlossDelayVegetable > 0f
             || satlossDelayProtein > 0f
             || satlossDelayGrain > 0f
             || satlossDelayDairy > 0f)
             && hungerCounter - 10f < 0f
             && DateTime.Now - dt < TimeSpan.FromSeconds(10)) //if hungercounter is negative, isondelay is true and last update was too soon.
            {
                dt = System.DateTime.Now;
                hungerCounterField?.SetValue(__instance, oldHunger); // add 10s so when 10 is subtracted it won't go negative
                //ModLogger.Notification("Value was bumped by 10f to prevent it from going negative " + oldHunger);
            }

            return true;
        }
    }
}
