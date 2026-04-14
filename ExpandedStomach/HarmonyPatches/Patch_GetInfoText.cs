using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ExpandedStomach.HarmonyPatches
{
    public static class ClientPatcher
    {
        public static void ApplyClientPatches(Harmony harmony)
        {
            MethodInfo getInfoText = AccessTools.Method(typeof(EntityPlayer), "GetInfoText");
            if (getInfoText == null)
            {
                ExpandedStomachModSystem.Logger.Error("Patch_GetInfoText: could not find EntityPlayer.GetInfoText — patch skipped.");
                return;
            }
            MethodInfo getInfoTextPre  = AccessTools.Method(typeof(Patch_GetInfoText), nameof(Patch_GetInfoText.Prefix));
            MethodInfo getInfoTextPost = AccessTools.Method(typeof(Patch_GetInfoText), nameof(Patch_GetInfoText.Postfix));
            harmony.Patch(getInfoText, prefix: new HarmonyMethod(getInfoTextPre), postfix: new HarmonyMethod(getInfoTextPost));
        }
    }

    public static class Patch_GetInfoText
    {
        private static readonly List<(float min, float max, string value)> ranges = new List<(float min, float max, string value)>
        {
            (0.0f,  0.1f,  "flNormal"),
            (0.10f, 0.2f,  "flSOverweight"),
            (0.2f,  0.35f, "flOverweight"),
            (0.35f, 0.75f, "flFat"),
            (0.75f, 1.0f,  "flObese")
        };

        /// <summary>
        /// Reads the player's fat level before GetInfoText runs and stores the formatted
        /// fat-level description line in <paramref name="__state"/> for the postfix to append.
        /// </summary>
        public static bool Prefix(EntityPlayer __instance, ref StringBuilder __state)
        {
            __state = new StringBuilder();
            ITreeAttribute stomach = __instance.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if (stomach != null)
            {
                float fatMeter = stomach.GetFloat("fatMeter");
                var range = ranges.FirstOrDefault(x => fatMeter >= x.min && fatMeter <= x.max);
                if (range.value != null)
                    __state.AppendLine(string.Format(Lang.Get("expandedstomach:fatlevel"), Lang.Get("expandedstomach:" + range.value)));
            }
            return true;
        }

        /// <summary>
        /// Appends the fat-level line built in the prefix to the final info text result string.
        /// </summary>
        public static void Postfix(ref string __result, StringBuilder __state)
        {
            __result = new StringBuilder(__result).AppendLine(__state.ToString()).ToString();
        }
    }
}
