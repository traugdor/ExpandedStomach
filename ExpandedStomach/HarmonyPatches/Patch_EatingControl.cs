using ExpandedStomach;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ExpandedStomach.HarmonyPatches
{
    public static class HarmonyPatchesVars
    {
        public static bool BrainFreezeInstalled = false;
        public static MethodInfo BrainFreezeMethod = null;
    }

    [HarmonyPatch]
    public static class YeahBoiScrapeThatBowl
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BlockMeal), "tryFinishEatMeal");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);

            var originalcallIndex = -1; //set to -1 for now. Check to see if modified later.
            var stlocIndex = -1; //set to -1 for now. Check to see if modified later.
            var ldlocIndex = -1; //set to -1 for now. Check to see if modified later.

            for (int i = 0; i < codes.Count - 3; i++) {//doing -3 because we need to check next TWO instructions
                if ((codes[i].opcode == OpCodes.Callvirt && codes[i].operand.ToString().Contains("Consume")) 
                    && codes[i+1].opcode == OpCodes.Stloc_3
                    && codes[i+2].opcode == OpCodes.Ldloc_3)
                {
                    originalcallIndex = i;
                    stlocIndex = i + 1;
                    ldlocIndex = i + 2;
                    break;
                }
            }

            if(originalcallIndex == -1)
            {
                throw new Exception("Could not find call to Consume. Aborting patch.");
            }

            //build call to EatMealIntoExpandedStomach
            //public static float EatMealIntoExpandedStomach(BlockMeal __instance, ItemSlot slot, float servingsLeft, EntityAgent byEntity)
            var injection = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0), // __instance
                new CodeInstruction(OpCodes.Ldarg_2), // slot
                new CodeInstruction(OpCodes.Ldloc_3), // servingsLeft
                new CodeInstruction(OpCodes.Ldarg_3), // byEntity
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Helpers), "EatMealIntoExpandedStomach")),
                new CodeInstruction(OpCodes.Stloc_3) // store method return value in local variable 3
            };

            codes.InsertRange(stlocIndex + 1, injection); //inject after Stloc_3

            return codes.AsEnumerable();
        }
    }
    //----------------------------------------------------------------------------

    //----------------------------------------------------------------------------

    // Patch for regular items (meat, bread, berries, etc.)
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
    [HarmonyPriority(100)]
    public static class Patch_CollectibleObject_tryEatStop
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
            var codes = new List<CodeInstruction>(instructions);

            //find where the call to RecieveSaturation is
            int originalCallIndex = -1; //set to -1 for now. Check to see if modified later.
            int ldNullIndex = -1; //set to -1 for now. Check to see if modified later.
            //we store these values because we want to inject code between them.

            //let's find the call to RecieveSaturation
            for (int i = 0; i < codes.Count - 2; i++) // subtract 2 because we need to check the next instruction as well.
            {
                if ((codes[i].opcode == OpCodes.Callvirt && //if it's a call to a virtual method
                    codes[i].operand.ToString().Contains("ReceiveSaturation")) && //and it's a call to ReceiveSaturation
                    codes[i + 1].opcode == OpCodes.Ldnull) // and the very next instruction is Ldnull
                {
                    // then we found it!
                    originalCallIndex = i;
                    ldNullIndex = i + 1;
                    break;
                }
            }
            
            //if we didn't find it, abort with exception. We want the mod to crash and fail.
            if (originalCallIndex == -1 || ldNullIndex == -1)
            {
                throw new Exception("Could not find call to ReceiveSaturation. Aborting patch.");
            }

            var foodCat = AccessTools.Field(typeof(FoodNutritionProperties), "FoodCategory"); //save foodCat -- Meow!
            var satiety = AccessTools.Field(typeof(FoodNutritionProperties), "Satiety");

            //now it's time to inject the call to GetNutrientsFromFoodType
            var toInject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0), //get this aka __instance
                new CodeInstruction(OpCodes.Ldloc_0), //get the foodprops (local variable 0) ... again
                new CodeInstruction(OpCodes.Ldarg_3), //load byEntity (argument 3) ... again
                new CodeInstruction(OpCodes.Ldarg_2), //load itemStack (argument 2)
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Helpers), "EatFoodIntoExpandedStomach")),
            };

            codes.InsertRange(originalCallIndex+3, toInject); //insert the code before receivesaturation

            return codes.AsEnumerable();
        } // <--- See Patch_EatingControl.cs>
    }
    //----------------------------------------------------------------------------
    [HarmonyPatch(typeof(Vintagestory.GameContent.EntityBehaviorHunger), "set_Saturation")]
    public static class Patch_EntityBehaviorHunger_set_Saturation
    {
        public static bool Prefix(EntityBehaviorHunger __instance, ref float value)
        {
            var hunger = __instance.entity.WatchedAttributes.GetTreeAttribute("hunger");
            var ExpandedStomach = __instance.entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if(ExpandedStomach != null && hunger != null)
            {
                float currentSaturation = hunger.GetFloat("currentsaturation");
                float currentStomachSat = ExpandedStomach.GetFloat("expandedStomachMeter");
                if (currentSaturation > value)
                {
                    if (currentStomachSat > 0)
                    {
                        float difference = currentSaturation - value;
                        currentStomachSat -= difference;
                        if (currentStomachSat < 0)
                        {
                            value += currentStomachSat;
                            currentStomachSat = 0;
                        }
                        else
                        {
                            value = currentSaturation; // currentsaturation remains unchanged
                        }
                    }
                    ExpandedStomach.SetFloat("expandedStomachMeter", currentStomachSat);
                    __instance.entity.WatchedAttributes.MarkPathDirty("expandedStomach");
                }
            }

            return true; //allow method to proceed with new value
        }
    }

    [HarmonyPatch(typeof(Vintagestory.GameContent.EntityBehaviorBodyTemperature), "OnGameTick")]
    public static class Patch_EntityBehaviorBodyTemperature_get_CurBodyTemperature
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);

            //Step 1: find call to getfloat
            int originalCallIndex = -1; //set to -1 for now. Check to see if modified later.

            for (int i = 2; i < codes.Count; i++)
            {
                if (
                    codes[i - 2].opcode == OpCodes.Ldloc_S && codes[i - 2].operand is LocalBuilder lb1 && lb1.LocalIndex == 11 &&
                    codes[i - 1].opcode == OpCodes.Sub &&
                    codes[i].opcode == OpCodes.Stloc_S && codes[i].operand is LocalBuilder lb2 && lb2.LocalIndex == 12
                )
                {
                    // Inject after i (the stloc.s 12)
                    originalCallIndex = i + 1;
                    break;
                }
            }



            //if we didn't find it, abort with exception. We want the mod to crash and fail.
            if (originalCallIndex == -1)
            {
                throw new Exception("Could not find location to inject code. Aborting body temperature patch.");
            }

            //insert code to custom method
            var toInject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldloc_S, 12),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Helpers), "ModifyHereTemperature")),
                new CodeInstruction(OpCodes.Stloc_S, 12)
            };

            codes.InsertRange(originalCallIndex, toInject);

            return codes.AsEnumerable();
        }
    }

    [HarmonyPatch(typeof(Vintagestory.GameContent.EntityBehaviorHunger), "ReduceSaturation")] // Change to actual method name if different
    [HarmonyPriority(Priority.Last)]
    public static class Patch_EntityBehaviorHunger_ReduceSaturation
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Step 1: Find `if (prevSaturation > 0)` pattern
            int originalIfStartIndex = -1;
            int bleInstructionIndex = -1;

            for (int i = 0; i < codes.Count - 2; i++)
            {
                if ((codes[i].opcode == OpCodes.Ldloc_1) &&
                    codes[i + 1].opcode == OpCodes.Ldc_R4 && (float)codes[i + 1].operand == 0f &&
                    (codes[i + 2].opcode == OpCodes.Ble_Un_S))
                {
                    originalIfStartIndex = i;
                    bleInstructionIndex = i + 2;
                    break;
                }
            }

            if (originalIfStartIndex == -1)
            {
                ExpandedStomachModSystem.Logger.Warning("ExpandedStomach: Could not find original saturation check. Patch skipped.");
                throw new Exception("ExpandedStomach: Could not find original saturation check.");
            }

            // Define label to jump to after our logic (i.e. skip original code)
            Label skipOriginalIfBlock = il.DefineLabel();

            // Assign that label to the instruction *after* the original if block's branch
            int insertionPoint = originalIfStartIndex;

            var targetlabel = (Label)codes[bleInstructionIndex].operand;

            // Insert our call to CustomStomachLogic and conditional branch
            var injected = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0), // __instance
                new CodeInstruction(OpCodes.Ldarg_1), // satLossMultiplier
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_EntityBehaviorHunger_ReduceSaturation), nameof(CustomStomachLogic))),
                new CodeInstruction(OpCodes.Brtrue_S, targetlabel) // If true, skip original hunger logic
            };

            codes.InsertRange(insertionPoint, injected);

            foreach (var instr in codes.AsEnumerable())
            {
                ExpandedStomachModSystem.Logger.Debug($"IL: {instr}");
            }

            return codes.AsEnumerable();
        }

        // This runs before the original hunger depletion logic
        public static bool CustomStomachLogic(EntityBehaviorHunger __instance, float satLossMultiplier)
        {
            var stomach = __instance.entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if (stomach == null) { 
                return false; 
            }

            float prevStomachSat = stomach.GetFloat("expandedStomachMeter");
            if (prevStomachSat <= 0)
            {
                return false;
            }

            float satLoss = satLossMultiplier * 10f;
            satLoss *= (1 + (prevStomachSat / 11000f));
            prevStomachSat = Math.Max(0f, prevStomachSat - satLoss);

            stomach.SetFloat("expandedStomachMeter", prevStomachSat);
            __instance.entity.WatchedAttributes.MarkPathDirty("expandedStomach");

            var sprintCounterField = typeof(EntityBehaviorHunger).GetField("sprintCounter", BindingFlags.Instance | BindingFlags.NonPublic);
            sprintCounterField?.SetValue(__instance, 0);

            return true;
        }
    }

    //----------------------------------------------------------------------------
    public static class Helpers
    {
        public static void GetNutrientsFromMeal(FoodNutritionProperties[] foodprops, float servingsConsumed, EntityAgent byEntity)
        {
            foreach (var foodprop in foodprops)
            {
                float saturation = foodprop.Satiety * servingsConsumed;
                GetNutrientsFromFoodType(foodprop.FoodCategory, saturation, byEntity);
            }
        }

        public static void EatFoodIntoExpandedStomach(CollectibleObject __instance, FoodNutritionProperties foodprops, EntityAgent byEntity, ItemSlot itemSlot = null)
        {
            if(HarmonyPatchesVars.BrainFreezeInstalled && byEntity is EntityPlayer player && itemSlot != null)
            {
                HarmonyPatchesVars.BrainFreezeMethod.Invoke(null, new object[] { player, itemSlot });
            }
            float saturation = foodprops.Satiety;
            //calculate saturation we can absorb based on stomach size - capacity
            ITreeAttribute stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            int stomachsize = stomach.GetInt("stomachSize");
            float stomachcapacity = stomach.GetFloat("expandedStomachMeter");
            float saturationAvailable = (float)stomachsize - stomachcapacity;

            var curSat = byEntity.WatchedAttributes.GetTreeAttribute("hunger").GetFloat("currentsaturation");
            var maxsat = byEntity.WatchedAttributes.GetTreeAttribute("hunger").GetFloat("maxsaturation");
            
            if(curSat >= maxsat)
            {
                //allow to eat into expanded stomach
                if (saturationAvailable > saturation)
                {
                    //we ate it all. add saturation to stomachcap and write it back
                    stomachcapacity += saturation;
                    stomach.SetFloat("expandedStomachMeter", stomachcapacity);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                }
                else
                {
                    //we didn't eat it all. stomachcap = stomachsize
                    saturation = ((float)stomachsize - stomachcapacity);
                    stomachcapacity = (float)stomachsize;
                    stomach.SetFloat("expandedStomachMeter", stomachcapacity);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                }
                GetNutrientsFromFoodType(foodprops.FoodCategory, saturation, byEntity);
            }
            byEntity.ReceiveSaturation(0, foodprops.FoodCategory);
        }

        public static float EatMealIntoExpandedStomach(BlockMeal __instance, ItemSlot slot, float servingsLeft, EntityAgent byEntity)
        {
            ICoreAPI api = Traverse.Create(__instance).Field("api").GetValue<ICoreAPI>();
            FoodNutritionProperties[]? multiProps = __instance.GetContentNutritionProperties(byEntity.World, slot, byEntity);

            // get remaining sat from servingsleft and total meal sat and fill expandable stomach
            float mealbaseSat = 0f;
            float mealremSat = 0f;
            foreach (var prop in multiProps)
            {
                mealbaseSat += prop.Satiety;
            }
            mealremSat = servingsLeft * mealbaseSat;

            // get expandable stomach properties
            ITreeAttribute stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            ITreeAttribute hunger = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
            int stomachsize = stomach.GetInt("stomachSize");
            float stomachsat = stomach.GetFloat("expandedStomachMeter");
            float currentsaturation = hunger.GetFloat("currentsaturation");
            float maxsaturation = hunger.GetFloat("maxsaturation");

            //patch weird hunger issue
            if (currentsaturation.between(maxsaturation - 0.1f, maxsaturation))
            {
                currentsaturation = maxsaturation;
                hunger.SetFloat("currentsaturation", currentsaturation);
                byEntity.WatchedAttributes.MarkPathDirty("hunger");
            }

            //check last time we ate because this fires twice for some reason I don't understand why
            float timeLastEat = byEntity.WatchedAttributes.GetFloat("timeLastEat");
            float eatWindow = api.World.ElapsedMilliseconds - timeLastEat;
            if(currentsaturation == maxsaturation)
            {
                if (eatWindow < 15000 && eatWindow > 1000) //if it's between 1s and 15s after last eat
                {
                    timeLastEat = api.World.ElapsedMilliseconds;
                    byEntity.WatchedAttributes.SetFloat("timeLastEat", timeLastEat);
                    byEntity.WatchedAttributes.MarkPathDirty("timeLastEat");
                    // fill stomach
                    if (stomachsize - stomachsat >= mealremSat)
                    {
                        //we ate it all :)
                        Helpers.GetNutrientsFromMeal(multiProps, servingsLeft, byEntity);
                        servingsLeft = 0;
                        stomachsat += mealremSat;
                        stomach.SetFloat("expandedStomachMeter", stomachsat);
                        byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                    }
                    else
                    {
                        //we were a wimp and couldn't eat it all
                        servingsLeft -= (stomachsize - stomachsat) / mealbaseSat;
                        Helpers.GetNutrientsFromMeal(multiProps, servingsLeft, byEntity);
                        stomachsat = stomachsize;
                        stomach.SetFloat("expandedStomachMeter", stomachsat);
                        byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                    }
                }
                else
                {
                    timeLastEat = api.World.ElapsedMilliseconds;
                    byEntity.WatchedAttributes.SetFloat("timeLastEat", timeLastEat);
                    byEntity.WatchedAttributes.MarkPathDirty("timeLastEat");
                }
                byEntity.ReceiveSaturation(0, multiProps[0].FoodCategory);
            }

            return servingsLeft;
        }

        public static void GetNutrientsFromFoodType(EnumFoodCategory foodCat, float saturationConsumed, EntityAgent byEntity)
        {
            // get expandable stomach properties
            ITreeAttribute stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            int stomachsize = stomach.GetInt("stomachSize");
            float stomachsat = stomach.GetFloat("expandedStomachMeter");

            ITreeAttribute hunger = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
            float currentsat = hunger.GetFloat("currentsaturation");
            float maxsat = hunger.GetFloat("maxsaturation");
            if (currentsat >= maxsat * 0.999f)
            {
                //only absorb 1/4 of nutrition if possible. sat/10 is calculation
                // Math.Min(maxsat, nutlevel + sat/10
                float fruitsat = hunger.GetFloat("fruitLevel");
                float vegetablesat = hunger.GetFloat("vegetableLevel");
                float proteinsat = hunger.GetFloat("proteinLevel");
                float grainsat = hunger.GetFloat("grainLevel");
                float dairysat = hunger.GetFloat("dairyLevel");
                float satMult = 1f;
                //don't need to recalculate the saturation consumed
                switch (byEntity.getDifficulty())
                {
                    case "easy":
                        satMult = 0.5f;
                        break;
                    case "normal":
                        satMult = 0.25f;
                        break;
                    case "hard":
                        satMult = 0.15f;
                        break;
                }
                switch (foodCat)
                {
                    case EnumFoodCategory.Fruit:
                        fruitsat = Math.Min(maxsat, fruitsat + saturationConsumed * satMult);
                        hunger.SetFloat("fruitLevel", fruitsat);
                        break;
                    case EnumFoodCategory.Vegetable:
                        vegetablesat = Math.Min(maxsat, vegetablesat + saturationConsumed * satMult);
                        hunger.SetFloat("vegetableLevel", vegetablesat);
                        break;
                    case EnumFoodCategory.Protein:
                        proteinsat = Math.Min(maxsat, proteinsat + saturationConsumed * satMult);
                        hunger.SetFloat("proteinLevel", proteinsat);
                        break;
                    case EnumFoodCategory.Grain:
                        grainsat = Math.Min(maxsat, grainsat + saturationConsumed * satMult);
                        hunger.SetFloat("grainLevel", grainsat);
                        break;
                    case EnumFoodCategory.Dairy:
                        dairysat = Math.Min(maxsat, dairysat + saturationConsumed * satMult);
                        hunger.SetFloat("dairyLevel", dairysat);
                        break;
                    default:
                        ; // do nothing. We couldn't update any saturation values because no category was specified
                        break;
                }
                byEntity.WatchedAttributes.SetAttribute("hunger", hunger);
                byEntity.WatchedAttributes.MarkPathDirty("hunger");
            }
        }

        public static float ModifyHereTemperature(EntityBehaviorBodyTemperature __instance, float hereTemp)
        {
            float bodyTempOffset = 0f;
            float fatlevel = 0f;
            ITreeAttribute stomachtree = __instance.entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if(stomachtree != null)
                fatlevel = stomachtree.GetFloat("fatMeter");
            bodyTempOffset = (fatlevel) * 10f; //full fat is +20 degrees C and minus 40% movement speed by default
            return hereTemp + bodyTempOffset;
        }
    }

}
