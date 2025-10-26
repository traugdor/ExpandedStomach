using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ExpandedStomach.HarmonyPatches
{
    [HarmonyPatch(typeof(EntityPlayer), "GetInfoText")]
    public static class Patch_GetInfoText
    {
        static ITreeAttribute stomach = null;

        static List<(float min, float max, string value)> ranges = new List<(float min, float max, string value)>
        {
            (0.0f, 0.1f, "flNormal"),
            (0.10f, 0.2f, "flSOverweight"),
            (0.2f, 0.35f, "flOverweight"),
            (0.35f, 0.75f, "flFat"),
            (0.75f, 1.0f, "flObese")
        };

        static void Prefix(EntityPlayer __instance, out StringBuilder __state)
        {
            __state = new StringBuilder();
            stomach = __instance.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if (stomach != null)
            {
                string fatLevelKey = "";
                float FatMeter = stomach.GetFloat("fatMeter");

                fatLevelKey = ranges.FirstOrDefault(x => FatMeter >= x.min && FatMeter <= x.max).value;

                __state.AppendLine(string.Format(Lang.Get("expandedstomach:fatlevel"), Lang.Get("expandedstomach:" + fatLevelKey)));
            }
        }

        static void PostFix(ref string __result, StringBuilder __state)
        {
            string oldResult = __result;
            __state.Append(oldResult);
            __result = __state.ToString();
        }
    }
}
