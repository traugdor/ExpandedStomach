using ExpandedStomach;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace ExpandedStomach.HarmonyPatches
{
    public static class HarmonyPatchesVars
    {
        public static bool BrainFreezeInstalled = false;
        public static MethodInfo BrainFreezeMethod = null;
    }

    public static class ServerPatcher
    {
        public static void ApplyServerPatches(Harmony harmony)
        {
            // tryFinishEatMeal
            MethodInfo tryFEM = (MethodInfo)YeahBoiScrapeThatBowl.TargetMethod();
            MethodInfo tryFEMTranspiler = AccessTools.Method(typeof(YeahBoiScrapeThatBowl), nameof(YeahBoiScrapeThatBowl.Transpiler));
            harmony.Patch(tryFEM, transpiler: new HarmonyMethod(tryFEMTranspiler));
            // liquid tryEatStop
            MethodInfo tryeatstopLiquid = (MethodInfo)DrinkUpMyFriend.TargetMethod();
            MethodInfo tryeatstopLiquidPrefix = AccessTools.Method(typeof(DrinkUpMyFriend), nameof(DrinkUpMyFriend.Prefix));
            MethodInfo tryeatstopLiquidTranspiler = AccessTools.Method(typeof(DrinkUpMyFriend), nameof(DrinkUpMyFriend.Transpiler));
            harmony.Patch(tryeatstopLiquid, prefix: new HarmonyMethod(tryeatstopLiquidPrefix), transpiler: new HarmonyMethod(tryeatstopLiquidTranspiler));
            // food tryEatStop
            MethodInfo tryeatstopCO = AccessTools.Method(typeof(CollectibleObject), "tryEatStop");
            MethodInfo tryeatstopCOPrefix = AccessTools.Method(typeof(OmNomNomNomFooooood), nameof(OmNomNomNomFooooood.Prefix));
            var tesCOP = new HarmonyMethod(tryeatstopCOPrefix)
            {
                priority = Priority.Last
            };
            MethodInfo tryeatstopCOTranspiler = AccessTools.Method(typeof(OmNomNomNomFooooood), nameof(OmNomNomNomFooooood.Transpiler));
            var tesCOT = new HarmonyMethod(tryeatstopCOTranspiler)
            {
                priority = Priority.Last
            };
            harmony.Patch(tryeatstopCO, prefix: tesCOP, transpiler: tesCOT);
            // EBHunger set_Saturation
            MethodInfo setSat = AccessTools.Method(typeof(EntityBehaviorHunger), "set_Saturation");
            MethodInfo setSatPrefix = AccessTools.Method(typeof(Patch_EntityBehaviorHunger_set_Saturation), nameof(Patch_EntityBehaviorHunger_set_Saturation.Prefix));
            harmony.Patch(setSat, prefix: new HarmonyMethod(setSatPrefix));
            // EBBTemperature OnGameTick
            MethodInfo EBBTonGameTick = AccessTools.Method(typeof(EntityBehaviorBodyTemperature), "OnGameTick");
            MethodInfo EBBTonGameTickTranspiler = AccessTools.Method(typeof(Patch_EntityBehaviorBodyTemperature_OnGameTick), nameof(Patch_EntityBehaviorBodyTemperature_OnGameTick.Transpiler));
            harmony.Patch(EBBTonGameTick, transpiler: new HarmonyMethod(EBBTonGameTickTranspiler));
            // EBHunger ReduceSaturation
            MethodInfo EBHRedSat = AccessTools.Method(typeof(EntityBehaviorHunger), "ReduceSaturation");
            MethodInfo EBHRedSatT = AccessTools.Method(typeof(Patch_EntityBehaviorHunger_ReduceSaturation), nameof(Patch_EntityBehaviorHunger_ReduceSaturation.Transpiler));
            var EBHRedSatTHM = new HarmonyMethod(EBHRedSatT)
            {
                priority = Priority.Last
            };
            harmony.Patch(EBHRedSat, transpiler: EBHRedSatTHM);
            // DONE!
        }
    }

    # region BlockMeal_TryFinishEatMeal
    public static class YeahBoiScrapeThatBowl
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BlockMeal), "tryFinishEatMeal");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
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
    #endregion
    
    #region BlockLiquidContainerBase_TryEatStop
    public static class DrinkUpMyFriend
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BlockLiquidContainerBase), "tryEatStop");
        }

        public static bool Prefix(BlockLiquidContainerBase __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity)
        {
            var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            var hunger = byEntity.WatchedAttributes.GetTreeAttribute("hunger");

            float satietyBeforeEating = hunger.GetFloat("currentsaturation");
            stomach.SetFloat("satietyBeforeEating", satietyBeforeEating);
            byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");

            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            //hijack call to ReceiveSaturation and substitute it with code for eating food item
            //because drinking consumes the full amount available.
            var codes = new List<CodeInstruction>(instructions);

            var RecieveSaturationIndex = -1; //set to -1 for now. Check to see if modified later.
            var ldLoc1Index = -1; //set to -1 for now. Check to see if modified later.

            //let's find the call to RecieveSaturation
            for (int i = 0; i < codes.Count - 2; i++) // subtract 2 because we need to check the next instruction as well.
            {
                if ((codes[i].opcode == OpCodes.Callvirt && //if it's a call to a virtual method
                    codes[i].operand.ToString().Contains("ReceiveSaturation")) && //and it's a call to ReceiveSaturation
                    codes[i + 1].opcode == OpCodes.Ldloc_1) // and the very next instruction is Ldloc_1
                {
                    // then we found it!
                    RecieveSaturationIndex = i;
                    ldLoc1Index = i + 1;
                    break;
                }
            }

            //if we didn't find it, abort with exception. We want the mod to crash and fail.
            if (RecieveSaturationIndex == -1 || ldLoc1Index == -1)
            {
                throw new Exception("Could not find call to ReceiveSaturation. Aborting patch.");
            }

            var toInject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0), //get this aka __instance
                new CodeInstruction(OpCodes.Ldloc_1), //get the foodprops (local variable 0) ... again
                new CodeInstruction(OpCodes.Ldarg_3), //load byEntity (argument 3) ... again
                new CodeInstruction(OpCodes.Ldarg_2), //load itemStack (argument 2)
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Helpers), "EatFoodIntoExpandedStomach")),
            };

            codes.InsertRange(RecieveSaturationIndex + 3, toInject); //insert the code before receivesaturation

            return codes.AsEnumerable();
        }

    }
    #endregion

    #region CollectibleObject_TryEatStop
    // Patch for regular items (meat, bread, berries, etc.)
    public static class OmNomNomNomFooooood
    {
        public static bool Prefix(CollectibleObject __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity)
        {
            var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            var hunger = byEntity.WatchedAttributes.GetTreeAttribute("hunger");

            float satietyBeforeEating = hunger.GetFloat("currentsaturation");
            stomach.SetFloat("satietyBeforeEating", satietyBeforeEating);
            byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");

            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) 
        {
            var codes = new List<CodeInstruction>(instructions);

            //find where the call to RecieveSaturation is
            int originalCallIndex = -1; //set to -1 for now. Check to see if modified later.
            int brainFreezeIndex = -1; //set to -1 for now. Check to see if modified later.
            //we store these values because we want to inject code between them.

            //let's find the call to RecieveSaturation
            for (int i = 0; i < codes.Count - 1; i++)
            {
                if ((codes[i].opcode == OpCodes.Callvirt && //if it's a call to a virtual method
                    codes[i].operand.ToString().Contains("ReceiveSaturation")) ) //and it's a call to ReceiveSaturation
                {
                    // then we found it!
                    originalCallIndex = i;
                    break;
                }
            }
            
            //if we didn't find it, abort with exception. We want the mod to crash and fail.
            if (originalCallIndex == -1)
            {
                throw new Exception("Could not find call to ReceiveSaturation. Aborting patch.");
            }

            //check if BrainFreeze is loaded
            if(HarmonyPatchesVars.BrainFreezeInstalled == true)
            {
                //find call to brainfreeze method
                var methodToFind = HarmonyPatchesVars.BrainFreezeMethod;

                for (int i = 0; i < codes.Count - 1; i++)
                {
                    if ((codes[i].opcode == OpCodes.Call && //if it's a call to a virtual method
                        codes[i].operand == methodToFind)) //and it's a call to BrainFreeze method
                    {
                        // then we found it!
                        brainFreezeIndex = i;
                        break;
                    }
                }
                if(brainFreezeIndex == -1)
                {
                    throw new Exception("Could not find call to BrainFreeze method. Aborting patch.");
                }
            }
            if(brainFreezeIndex != -1)
            {
                originalCallIndex = brainFreezeIndex;
            }

            //now it's time to inject the call to GetNutrientsFromFoodType
            var toInject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0), //get this aka __instance
                new CodeInstruction(OpCodes.Ldloc_0), //get the foodprops (local variable 0) ... again
                new CodeInstruction(OpCodes.Ldarg_3), //load byEntity (argument 3) ... again
                new CodeInstruction(OpCodes.Ldarg_2), //load itemStack (argument 2)
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Helpers), "EatFoodIntoExpandedStomach")),
            };

            codes.InsertRange(originalCallIndex + 1, toInject); //insert the code after receivesaturation

            return codes.AsEnumerable();
        } // <--- See Patch_EatingControl.cs>
    }
    #endregion

    #region EntityBehaviorHunger_set_Saturation
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
    #endregion

    #region EntityBehaviorBodyTemperature_OnGameTick
    public static class Patch_EntityBehaviorBodyTemperature_OnGameTick
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);

            //Step 1: find call to getfloat
            int originalCallIndex = -1; //set to -1 for now. Check to see if modified later.

            for (int i = 2; i < codes.Count; i++)
            {
                if (
                    codes[i - 2].opcode == OpCodes.Ldloc_S && codes[i - 2].operand is LocalBuilder lb1 && lb1.LocalIndex == 12 &&
                    codes[i - 1].opcode == OpCodes.Sub &&
                    codes[i].opcode == OpCodes.Stloc_S && codes[i].operand is LocalBuilder lb2 && lb2.LocalIndex == 13
                )
                {
                    // Inject after i (the stloc.s 13)
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
                new CodeInstruction(OpCodes.Ldloc_S, 13),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Helpers), "ModifyHereTemperature")),
                new CodeInstruction(OpCodes.Stloc_S, 13)
            };

            codes.InsertRange(originalCallIndex, toInject);

            return codes.AsEnumerable();
        }
    }
    #endregion

    #region EntityBehaviorHunger_ReduceSaturation
    public static class Patch_EntityBehaviorHunger_ReduceSaturation
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
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

            return codes.AsEnumerable();
        }

        // This runs before the original hunger depletion logic
        public static bool CustomStomachLogic(EntityBehaviorHunger __instance, float satLossMultiplier)
        {
            var stomach = __instance.entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if (stomach == null) { 
                ExpandedStomachModSystem.Logger.Error("ExpandedStomach: Could not find stomach attribute. No saturation loss.");
                return false; 
            }

            float prevStomachSat = stomach.GetFloat("expandedStomachMeter");
            if (prevStomachSat <= 0)
            {
                return false;
            }

            float satLoss = satLossMultiplier * 10f * ExpandedStomachModSystem.serverapi.World.Config.GetFloat("ExpandedStomach.stomachSatLossMultiplier");
            var config = ExpandedStomachModSystem.sConfig;
            satLoss *= (1 + stomach.GetFloat("fatMeter") * config.drawbackSeverity); // increase saturation loss by fat level percentage
            satLoss *= (1 + (prevStomachSat / 11000f));
            prevStomachSat = Math.Max(0f, prevStomachSat - satLoss);

            stomach.SetFloat("expandedStomachMeter", prevStomachSat);
            __instance.entity.WatchedAttributes.MarkPathDirty("expandedStomach");

            var sprintCounterField = typeof(EntityBehaviorHunger).GetField("sprintCounter", BindingFlags.Instance | BindingFlags.NonPublic);
            sprintCounterField?.SetValue(__instance, 0);

            //ExpandedStomachModSystem.Logger.Debug($"ExpandedStomach: {satLoss} saturation was lost.");

            return true;
        }
    }
    #endregion

    #region Helpers
    public static class Helpers
    {
        #region GetNutrientsFromMeal
        public static void GetNutrientsFromMeal(FoodNutritionProperties[] foodprops, float servingsConsumed, EntityAgent byEntity)
        {
            foreach (var foodprop in foodprops)
            {
                float saturation = foodprop.Satiety * servingsConsumed;
                GetNutrientsFromFoodType(foodprop.FoodCategory, saturation, byEntity);
            }
            if (ExpandedStomachModSystem.EFACAactive)
            {

            }
        }
        #endregion

        #region EatFoodIntoExpandedStomach
        public static void EatFoodIntoExpandedStomach(CollectibleObject __instance, FoodNutritionProperties foodprops, EntityAgent byEntity, ItemSlot itemSlot = null)
        {
            float saturation = foodprops.Satiety;
            FoodNutritionProperties[] addprops = null;
            MethodInfo? method = typeof(CollectibleObject)
                .GetMethod("UpdateAndGetTransitionState",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            TransitionState tState = (TransitionState)method.Invoke(__instance, new object?[] {byEntity.World, itemSlot, EnumTransitionType.Perish});
            float spoilState = tState?.TransitionLevel ?? 0f;
            float spoilStateMult = 1-spoilState;
            //debugging
            //addprops = GetAdditiveNutritionProperties(__instance, itemSlot, spoilState);
            //enddebugging
            if (ExpandedStomachModSystem.EFACAactive)
            {
                addprops = GetAdditiveNutritionProperties(__instance, itemSlot, spoilState);
            }
            if (addprops != null)
            {
                foreach(FoodNutritionProperties addprop in addprops)
                {
                    saturation += addprop.Satiety * spoilStateMult;
                }
            }
            //get difference between how much we could hold and how much we want to eat
            float diff = 0f;
            var hunger = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
            var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            var satdiff = hunger.GetFloat("maxsaturation") - stomach.GetFloat("satietyBeforeEating");
            diff = saturation - satdiff;
            if (diff < 0f) { diff = 0f; }
            //calculate saturation we can absorb based on stomach size - capacity
            int stomachsize = stomach.GetInt("stomachSize");
            float stomachcapacity = stomach.GetFloat("expandedStomachMeter");
            float saturationAvailable = (float)stomachsize - stomachcapacity;
            
            if(diff > 0f)
            {
                //allow to eat into expanded stomach
                if (saturationAvailable > diff)
                {
                    //we ate it all. add saturation to stomachcap and write it back
                    stomachcapacity += diff;
                    stomach.SetFloat("expandedStomachMeter", stomachcapacity);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                }
                else
                {
                    //we didn't eat it all. stomachcap = stomachsize
                    diff = ((float)stomachsize - stomachcapacity);
                    stomachcapacity = (float)stomachsize;
                    stomach.SetFloat("expandedStomachMeter", stomachcapacity);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                }
                GetNutrientsFromFoodType(foodprops.FoodCategory, diff, byEntity);
            }
            byEntity.ReceiveSaturation(0, foodprops.FoodCategory);
        }
        #endregion

        #region EatMealIntoExpandedStomach
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
        #endregion

        #region GetAdditiveNutritionProperties
        private static FoodNutritionProperties[] GetAdditiveNutritionProperties(CollectibleObject __instance, ItemSlot slot, float spoilState)
        {
            // get additional nutrition properties
            float SatMult = __instance.Attributes?["satMult"].AsFloat(1f) ?? 1f;
            FloatArrayAttribute additiveNutrients = slot.Itemstack.Attributes["expandedSats"] as FloatArrayAttribute;
            float[] exSats = additiveNutrients?.value;
            if (exSats == null || exSats.Length < 6) return null;
            List<FoodNutritionProperties> props = [];
            for (int i = 1; i <= 5; i++)
            {
                if (exSats[i] != 0)
                    props.Add(new() { FoodCategory = (EnumFoodCategory)(i-1), Satiety = exSats[i] * SatMult});
            }
            if (exSats[0] != 0 && props.Count > 0) props[0].Health = exSats[0] * SatMult;
            return props.ToArray();
        }
        #endregion

        #region GetNutrientsFromFoodType
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
        #endregion

        #region ModifyHereTemperature
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
        #endregion
    }
    #endregion

}
