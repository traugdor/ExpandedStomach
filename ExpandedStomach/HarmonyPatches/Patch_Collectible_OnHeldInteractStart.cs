using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using ExpandedStomach;

namespace ExpandedStomach.HarmonyPatches
{
    [HarmonyPatch]
    public static class Patch_Collectible_OnHeldInteractStart
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CollectibleObject), "OnHeldInteractStart");
        }

        static bool Prefix(
            CollectibleObject __instance,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            // Example: intercept food use
            var foodProps = slot?.Itemstack?.Collectible?.NutritionProps;
            if (foodProps == null || foodProps.Satiety <= 0)
            {
                // Not a food item — let it proceed untouched
                return true;
            }
            if (foodProps != null && foodProps.Satiety > 0)
            {
                var currentSat = byEntity.WatchedAttributes.GetFloat("saturation", 0f);
                var behavior = byEntity.GetBehavior<EntityBehaviorStomach>();
                float maxSat = 1500f;
                if(behavior != null)
                {
                    maxSat = behavior.MaxSatiety;
                }

                if (currentSat >= maxSat + 500f)
                {
                    if (byEntity is EntityPlayer ep && ep.Player is IServerPlayer serverPlayer)
                    {
                        serverPlayer.SendMessage(
                            GlobalConstants.GeneralChatGroup,
                            "You couldn't possibly eat another bite.",
                            EnumChatType.Notification
                        );
                    }

                    handling = EnumHandHandling.PreventDefault;
                    return false; // Block the eating action
                }

                // Otherwise, allow eating
            }

            return true;
        }
        static bool HandleEating(object __instance, ItemSlot slot, EntityAgent byEntity, EntityBehaviorHunger nutrition, ref EnumHandHandling handling)
        {
            float currentSat = byEntity.WatchedAttributes.GetFloat("saturation", 0f);
            float baseMax = nutrition.MaxSaturation;
            float expandedMax = baseMax + 500f; // Your extra stomach capacity

            // Use reflection to call ItemFood.GetSatiety
            var getSatiety = AccessTools.Method(__instance.GetType(), "GetSatiety", new[] { typeof(IWorldAccessor), typeof(ItemStack) });
            float foodSat = 0f;

            if (getSatiety != null)
            {
                foodSat = (float)getSatiety.Invoke(__instance, new object[] { byEntity.World, slot.Itemstack });
            }

            // Block eating if it would exceed expanded max satiety
            if (currentSat >= baseMax && currentSat + foodSat > expandedMax)
            {
                if (byEntity is EntityPlayer player && player.Player is IServerPlayer serverPlayer)
                {
                    serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                        "You couldn't possibly eat another bite.",
                        EnumChatType.Notification
                    );
                }

                handling = EnumHandHandling.PreventDefault;
                return false; // Cancel eating
            }

            return true; // Allow eating
        }
    }
}
