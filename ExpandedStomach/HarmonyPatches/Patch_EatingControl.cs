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

        static DateTime lastEat = DateTime.MinValue;

        static bool Prefix(
            BlockMeal __instance,
            float secondsUsed, 
            ItemSlot slot, 
            EntityAgent byEntity, 
            bool handleAllServingsConsumed)
        {
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

            lastEat = DateTime.Now;

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
            if (DateTime.Now - lastEat > TimeSpan.FromSeconds(1.5))
            {
                // fill stomach
                if (stomachsize - stomachsat >= mealremSat)
                {
                    //we ate it all :)
                    servingsLeft = 0;
                    stomachsat += mealremSat;
                    byEntity.WatchedAttributes.SetFloat("expandedStomachMeter", stomachsat);
                }
                else
                {
                    //we were a wimp and couldn't eat it all
                    servingsLeft -= (stomachsize - stomachsat) / mealbaseSat;
                    stomachsat = stomachsize;
                    byEntity.WatchedAttributes.SetFloat("expandedStomachMeter", stomachsat);
                }
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
    [HarmonyPatch(typeof(CollectibleObject), "OnHeldInteractStart")]
    public static class Patch_Collectible_OnHeldInteractStart
    {
        static bool Prefix(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            return SharedEatingHandlers.Handle(slot, byEntity, ref handling);
        }
    }
    //----------------------------------------------------------------------------

    // Start Helper Methods
    

    // Shared logic for satiety-based blocking
    public static class SharedEatingHandlers
    {
        public static float ConsumeMealOrPie(ItemSlot slot, float totalSatiety, float consumedSatiety, BlockSelection blockSel)
        {
            var stack = slot.Itemstack;
            var collectible = stack.Collectible;

            float percentEaten = consumedSatiety / totalSatiety;
            
            if (percentEaten >= 0.99f)
            {
                //assume fully consumed
                slot.TakeOut(1); //consume one item from stack
                slot.MarkDirty(); //update client
            }
            else
            {
                //assume partially consumed
                //check if item is a meal or a pie
                if(collectible is BlockMeal meal)
                {
                    //if it's a meal, consume as much as you can and leave the rest in the bowl
                    string pH = slot.Itemstack.Attributes.GetAsString("test", "");
                }
            }
            return 0f;
        }

        public static bool Handle(ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling, object source = null)
        {
            var foodProps = slot?.Itemstack?.Collectible?.NutritionProps;
            if (foodProps == null || foodProps.Satiety <= 0)
            {
                return true;
            }

            var nutrition = byEntity.GetBehavior<EntityBehaviorHunger>();
            float currentSat = nutrition?.Saturation ?? 0f;
            float baseMax = nutrition?.MaxSaturation ?? 1500f;
            float expandedMax = baseMax + 500f;

            float foodSat = 0f;
            if (source != null)
            {
                var getSatiety = AccessTools.Method(source.GetType(), "GetSatiety", new[] { typeof(IWorldAccessor), typeof(ItemStack) });
                if (getSatiety != null)
                {
                    foodSat = (float)getSatiety.Invoke(source, new object[] { byEntity.World, slot.Itemstack });
                }
            }

            if (currentSat >= baseMax && currentSat + foodSat > expandedMax)
            {
                if (byEntity is EntityPlayer player && player.Player is IServerPlayer serverPlayer)
                {
                    serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                        "You couldn't possibly eat another bite.",
                        EnumChatType.Notification);
                }

                handling = EnumHandHandling.PreventDefault;
                return false;
            }

            return true;
        }
    }
}
