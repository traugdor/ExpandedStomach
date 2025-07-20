using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using ExpandedStomach;

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
            int stomachsize = byEntity.WatchedAttributes.GetInt("stomachSize");
            float stomachsat = byEntity.WatchedAttributes.GetFloat("expandedStomachMeter");

            //check last time we ate because this fires twice for some reason I don't understand why
            float timeLastEat = byEntity.WatchedAttributes.GetFloat("timeLastEat");
            if (api.World.ElapsedMilliseconds - timeLastEat < 15000 && api.World.ElapsedMilliseconds - timeLastEat > 1500) //if it hasn't been 15 seconds since we last ate
            {
                timeLastEat = api.World.ElapsedMilliseconds;
                byEntity.WatchedAttributes.SetFloat("timeLastEat", timeLastEat);
                byEntity.WatchedAttributes.MarkPathDirty("timeLastEat");
                // fill stomach
                if (stomachsize - stomachsat >= mealremSat)
                {
                    //we ate it all :)
                    servingsLeft = 0;
                    stomachsat += mealremSat;
                    byEntity.WatchedAttributes.SetFloat("expandedStomachMeter", stomachsat);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomachMeter");
                }
                else
                {
                    //we were a wimp and couldn't eat it all
                    servingsLeft -= (stomachsize - stomachsat) / mealbaseSat;
                    stomachsat = stomachsize;
                    byEntity.WatchedAttributes.SetFloat("expandedStomachMeter", stomachsat);
                    byEntity.WatchedAttributes.MarkPathDirty("expandedStomachMeter");
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

    [HarmonyPatch]
    public static class ShutYourPieHole
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BlockPie), "OnBlockInteractStart");
        }

        static bool Prefix(
            BlockPie __instance,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (byPlayer?.Entity == null || world.Side != EnumAppSide.Server) return true;

            var behavior = byPlayer.Entity.GetBehavior<EntityBehaviorStomach>();
            float currentSat = behavior.CurrentSatiety;
            float maxSat = 1500f;

            var hunger = byPlayer.Entity.GetBehavior<EntityBehaviorHunger>();
            if (hunger != null) maxSat = hunger.MaxSaturation;
            if (behavior != null) maxSat = behavior.MaxSatiety;

            if (currentSat >= maxSat + 500f)
            {
                if (byPlayer is IServerPlayer serverPlayer)
                {
                    serverPlayer.SendMessage(
                        GlobalConstants.GeneralChatGroup,
                        "I couldn't possibly eat another bite.",
                        EnumChatType.Notification
                    );
                }
                return false; // Cancel pie interaction
            }
            return false; // Allow default pie eating
        }
    }
    //----------------------------------------------------------------------------

    // Patch for regular items (meat, bread, etc.)
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
                TransitionState state = __instance.UpdateAndGetTransitionState(api.World, slot, EnumTransitionType.Perish);
                float spoilState = state != null ? state.TransitionLevel : 0;

                float satLossMul = GlobalConstants.FoodSpoilageSatLossMul(spoilState, slot.Itemstack, byEntity);
                float healthLossMul = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, slot.Itemstack, byEntity);

                byEntity.ReceiveSaturation(nutriProps.Satiety * satLossMul, nutriProps.FoodCategory);

                // get expandable stomach properties
                int stomachsize = byEntity.WatchedAttributes.GetInt("stomachSize");
                float stomachsat = byEntity.WatchedAttributes.GetFloat("expandedStomachMeter");

                //check last time we ate because this fires twice for some reason I don't understand why
                float timeLastEat = byEntity.WatchedAttributes.GetFloat("timeLastEat");
                if (api.World.ElapsedMilliseconds - timeLastEat > 1500)
                {
                    timeLastEat = api.World.ElapsedMilliseconds;
                    byEntity.WatchedAttributes.SetFloat("timeLastEat", timeLastEat);
                    byEntity.WatchedAttributes.MarkPathDirty("timeLastEat");
                    // fill stomach
                    //eat as much as we can and toss the rest
                    float mealSat = nutriProps.Satiety * satLossMul;
                    //calculate stomach size left
                    if(stomachsize - stomachsat < mealSat)
                    {
                        stomachsat = stomachsize;
                        byEntity.WatchedAttributes.SetFloat("expandedStomachMeter", stomachsat);
                        byEntity.WatchedAttributes.MarkPathDirty("expandedStomachMeter");
                    }
                    else
                    {
                        stomachsat += mealSat;
                        byEntity.WatchedAttributes.SetFloat("expandedStomachMeter", stomachsat);
                        byEntity.WatchedAttributes.MarkPathDirty("expandedStomachMeter");
                    }

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
                else
                {
                    timeLastEat = api.World.ElapsedMilliseconds;
                    byEntity.WatchedAttributes.SetFloat("timeLastEat", timeLastEat);
                    byEntity.WatchedAttributes.MarkPathDirty("timeLastEat");
                }
            }
            return false; //deny default behavior
        }
    }
    //----------------------------------------------------------------------------

    
    [HarmonyPatch(typeof(EntityBehaviorHunger), "ReduceSaturation")]
    public static class Patch_EntityBehaviorHunger_ReduceSaturation
    {
        public static bool Prefix(EntityBehaviorHunger __instance, float satLossMultiplier)
        {
            bool isondelay = false;

            Traverse t = Traverse.Create(__instance);

            // Get the values
            float hungerCounter = t.Field("hungerCounter").GetValue<float>();
            int sprintCounter = t.Field("sprintCounter").GetValue<int>();

            satLossMultiplier *= GlobalConstants.HungerSpeedModifier;
            t.Field("satLossMultiplier").SetValue(satLossMultiplier);

            if (__instance.SaturationLossDelayFruit > 0)
            {
                __instance.SaturationLossDelayFruit -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                __instance.FruitLevel = Math.Max(0, __instance.FruitLevel - Math.Max(0.5f, 0.001f * __instance.FruitLevel) * satLossMultiplier * 0.25f);
            }

            if (__instance.SaturationLossDelayVegetable > 0)
            {
                __instance.SaturationLossDelayVegetable -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                __instance.VegetableLevel = Math.Max(0, __instance.VegetableLevel - Math.Max(0.5f, 0.001f * __instance.VegetableLevel) * satLossMultiplier * 0.25f);
            }

            if (__instance.SaturationLossDelayProtein > 0)
            {
                __instance.SaturationLossDelayProtein -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                __instance.ProteinLevel = Math.Max(0, __instance.ProteinLevel - Math.Max(0.5f, 0.001f * __instance.ProteinLevel) * satLossMultiplier * 0.25f);
            }

            if (__instance.SaturationLossDelayGrain > 0)
            {
                __instance.SaturationLossDelayGrain -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                __instance.GrainLevel = Math.Max(0, __instance.GrainLevel - Math.Max(0.5f, 0.001f * __instance.GrainLevel) * satLossMultiplier * 0.25f);
            }

            if (__instance.SaturationLossDelayDairy > 0)
            {
                __instance.SaturationLossDelayDairy -= 10 * satLossMultiplier;
                isondelay = true;
            }
            else
            {
                __instance.DairyLevel = Math.Max(0, __instance.DairyLevel - Math.Max(0.5f, 0.001f * __instance.DairyLevel) * satLossMultiplier * 0.25f / 2);
            }

            __instance.UpdateNutrientHealthBoost();

            if (isondelay)
            {
                hungerCounter -= 10;
                t.Field("hungerCounter").SetValue(hungerCounter);
                return false; //return early to avoid draining hunger; deny original method
            }

            float prevSaturation = __instance.Saturation;
            float prevStomachSat = __instance.entity.WatchedAttributes.GetFloat("expandedStomachMeter");
            float maxStomachSat = __instance.entity.WatchedAttributes.GetInt("stomachSize");
            float satLoss = satLossMultiplier * 10;

            if (prevStomachSat > 0)
            {
                satLoss = satLoss * (1 + (prevStomachSat / 11000f)); // caps at 1.5x and lowers as stomach empties
                prevStomachSat = Math.Max(0, prevStomachSat - satLoss);
                __instance.entity.WatchedAttributes.SetFloat("expandedStomachMeter", prevStomachSat);
                __instance.entity.WatchedAttributes.MarkPathDirty("expandedStomachMeter");
                sprintCounter = 0;
                t.Field("sprintCounter").SetValue(sprintCounter);
            }
            else if (prevSaturation > 0)
            {
                __instance.Saturation = Math.Max(0, prevSaturation - satLoss);
                sprintCounter = 0;
                t.Field("sprintCounter").SetValue(sprintCounter);
            }


            return false; //deny original method
        }
    }

    //----------------------------------------------------------------------------

}
