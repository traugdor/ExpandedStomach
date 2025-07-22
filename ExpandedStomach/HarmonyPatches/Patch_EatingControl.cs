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
    [HarmonyPatch]
    public static class YeahBoiScrapeThatBowl
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BlockMeal), "tryFinishEatMeal");
        }

        static bool Prefix(
            BlockMeal __instance,
            float secondsUsed, 
            ItemSlot slot, 
            EntityAgent byEntity, 
            bool handleAllServingsConsumed)
        {
            //get watched attribute and check if timeLastEat is defined
            if (!byEntity.WatchedAttributes.HasAttribute("timeLastEat"))
            {
                byEntity.WatchedAttributes.SetFloat("timeLastEat", 0f);
                byEntity.WatchedAttributes.MarkPathDirty("timeLastEat");
            }
            ICoreAPI api = Traverse.Create(__instance).Field("api").GetValue<ICoreAPI>();
            FoodNutritionProperties[]? multiProps = __instance.GetContentNutritionProperties(byEntity.World, slot, byEntity);

            if (byEntity.World.Side == EnumAppSide.Client || multiProps == null || secondsUsed < 1.45) return false;

            if (slot.Itemstack is not ItemStack foodSourceStack || (byEntity as EntityPlayer)?.Player is not IPlayer player) return false;
            slot.MarkDirty();

            float servingsLeft = __instance.GetQuantityServings(byEntity.World, foodSourceStack);
            ItemStack[] stacks = __instance.GetNonEmptyContents(api.World, foodSourceStack);

            if (stacks.Length == 0)
            {
                servingsLeft = 0;
            }
            else
            {
                string? recipeCode = __instance.GetRecipeCode(api.World, foodSourceStack);
                servingsLeft = __instance.Consume(byEntity.World, player, slot, stacks, servingsLeft, recipeCode == null || recipeCode == "");
            }

            // get remaining sat from servingsleft and total meal sat and fill expandable stomach
            float mealbaseSat = 0f;
            float mealremSat = 0f;
            foreach(var prop in multiProps)
            {
                mealbaseSat += prop.Satiety;
            }
            mealremSat = servingsLeft * mealbaseSat;

            // get expandable stomach properties
            ITreeAttribute stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            int stomachsize = stomach.GetInt("stomachSize");
            float stomachsat = stomach.GetFloat("expandedStomachMeter");

            //check last time we ate because this fires twice for some reason I don't understand why
            float timeLastEat = byEntity.WatchedAttributes.GetFloat("timeLastEat");
            float eatWindow = api.World.ElapsedMilliseconds - timeLastEat;
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

            if (servingsLeft <= 0)
            {
                if (handleAllServingsConsumed)
                {
                    if (__instance.Attributes["eatenBlock"].Exists)
                    {
                        Block block = byEntity.World.GetBlock(new AssetLocation(__instance.Attributes["eatenBlock"].AsString()));

                        if (slot.Empty || slot.StackSize == 1)
                        {
                            slot.Itemstack = new ItemStack(block);
                        }
                        else
                        {
                            if (!player.InventoryManager.TryGiveItemstack(new ItemStack(block), true))
                            {
                                byEntity.World.SpawnItemEntity(new ItemStack(block), byEntity.SidedPos.XYZ);
                            }
                        }
                    }
                    else
                    {
                        slot.TakeOut(1);
                        slot.MarkDirty();
                    }
                }
            }
            else
            {
                if (slot.Empty || slot.StackSize == 1)
                {
                    (foodSourceStack.Collectible as BlockMeal)?.SetQuantityServings(byEntity.World, foodSourceStack, servingsLeft);
                    slot.Itemstack = foodSourceStack;
                }
                else
                {
                    ItemStack? splitStack = slot.TakeOut(1);
                    (foodSourceStack.Collectible as BlockMeal)?.SetQuantityServings(byEntity.World, splitStack, servingsLeft);

                    ItemStack originalStack = slot.Itemstack;
                    slot.Itemstack = splitStack;

                    if (!player.InventoryManager.TryGiveItemstack(originalStack, true))
                    {
                        byEntity.World.SpawnItemEntity(originalStack, byEntity.SidedPos.XYZ);
                    }
                }
            }

            return true;
        }
    }
    //----------------------------------------------------------------------------
    
    //----------------------------------------------------------------------------

    // Patch for regular items (meat, bread, berries, etc.)
    [HarmonyPatch]
    public static class Patch_CollectibleObject_tryEatStop
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CollectibleObject), "tryEatStop");
        }

        static bool Prefix(
            CollectibleObject __instance,
            float secondsUsed, 
            ItemSlot slot, 
            EntityAgent byEntity)
        {
            ICoreAPI api = Traverse.Create(__instance).Field("api").GetValue<ICoreAPI>();
            FoodNutritionProperties nutriProps = __instance.GetNutritionProperties(byEntity.World, slot.Itemstack, byEntity);

            if (byEntity.World is IServerWorldAccessor && nutriProps != null && secondsUsed >= 0.95f)
            {
                //get watched attribute and check if timeLastEat is defined
                if (!byEntity.WatchedAttributes.HasAttribute("timeLastEat"))
                {
                    byEntity.WatchedAttributes.SetFloat("timeLastEat", 0f);
                    byEntity.WatchedAttributes.MarkPathDirty("timeLastEat");
                }
                TransitionState state = __instance.UpdateAndGetTransitionState(api.World, slot, EnumTransitionType.Perish);
                float spoilState = state != null ? state.TransitionLevel : 0;

                float satLossMul = GlobalConstants.FoodSpoilageSatLossMul(spoilState, slot.Itemstack, byEntity);
                float healthLossMul = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, slot.Itemstack, byEntity);

                byEntity.ReceiveSaturation(nutriProps.Satiety * satLossMul, nutriProps.FoodCategory);

                // fill stomach
                //eat as much as we can and toss the rest
                float mealSat = nutriProps.Satiety * satLossMul;
                //calculate stomach size left

                Helpers.GetNutrientsFromFoodType(nutriProps.FoodCategory, mealSat, byEntity);

                IPlayer player = null;
                if (byEntity is EntityPlayer) player = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);

                slot.TakeOut(1);

                if (nutriProps.EatenStack != null)
                {
                    if (slot.Empty)
                    {
                        slot.Itemstack = nutriProps.EatenStack.ResolvedItemstack.Clone();
                    }
                    else
                    {
                        if (player == null || !player.InventoryManager.TryGiveItemstack(nutriProps.EatenStack.ResolvedItemstack.Clone(), true))
                        {
                            byEntity.World.SpawnItemEntity(nutriProps.EatenStack.ResolvedItemstack.Clone(), byEntity.SidedPos.XYZ);
                        }
                    }
                }

                float healthChange = nutriProps.Health * healthLossMul;

                float intox = byEntity.WatchedAttributes.GetFloat("intoxication");
                byEntity.WatchedAttributes.SetFloat("intoxication", Math.Min(1.1f, intox + nutriProps.Intoxication));

                if (healthChange != 0)
                {
                    byEntity.ReceiveDamage(new DamageSource()
                    {
                        Source = EnumDamageSource.Internal,
                        Type = healthChange > 0 ? EnumDamageType.Heal : EnumDamageType.Poison
                    }, Math.Abs(healthChange));
                }

                slot.MarkDirty();
                player.InventoryManager.BroadcastHotbarSlot();
                return false; //deny default behavior
            }
            return false; //deny default behavior
        }
    }
    //----------------------------------------------------------------------------


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
                return codes.AsEnumerable();
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

                if (stomachsize - stomachsat < saturationConsumed)
                {
                    saturationConsumed = stomachsize - stomachsat;
                    stomachsat = stomachsize;
                    stomach.SetFloat("expandedStomachMeter", stomachsat);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                }
                else
                {
                    stomachsat += saturationConsumed;
                    stomach.SetFloat("expandedStomachMeter", stomachsat);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");
                }
                switch (foodCat)
                {
                    case EnumFoodCategory.Fruit:
                        fruitsat = Math.Min(maxsat, fruitsat + saturationConsumed * 0.1f);
                        hunger.SetFloat("fruitLevel", fruitsat);
                        break;
                    case EnumFoodCategory.Vegetable:
                        vegetablesat = Math.Min(maxsat, vegetablesat + saturationConsumed * 0.1f);
                        hunger.SetFloat("vegetableLevel", vegetablesat);
                        break;
                    case EnumFoodCategory.Protein:
                        proteinsat = Math.Min(maxsat, proteinsat + saturationConsumed * 0.1f);
                        hunger.SetFloat("proteinLevel", proteinsat);
                        break;
                    case EnumFoodCategory.Grain:
                        grainsat = Math.Min(maxsat, grainsat + saturationConsumed * 0.1f);
                        hunger.SetFloat("grainLevel", grainsat);
                        break;
                    case EnumFoodCategory.Dairy:
                        dairysat = Math.Min(maxsat, dairysat + saturationConsumed * 0.1f);
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
    }

}
