using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ExpandedStomach.HarmonyPatches
{
    public static class Patch_HungerDamageTicks
    {
        public static void ApplyCorePatches(Harmony harmony)
        {
            MethodInfo entityReceiveDamage = AccessTools.Method(typeof(Entity), "ReceiveDamage");
            if (entityReceiveDamage == null)
            {
                ExpandedStomachModSystem.Logger.Error("Patch_HungerDamageTicks: could not find Entity.ReceiveDamage — patch skipped.");
                return;
            }
            MethodInfo prefix = AccessTools.Method(typeof(IForgotToBringFoodWithMe), nameof(IForgotToBringFoodWithMe.Prefix));
            harmony.Patch(entityReceiveDamage, prefix: new HarmonyMethod(prefix));
        }
    }

    public static class IForgotToBringFoodWithMe
    {
        /// <summary>
        /// Intercepts hunger damage on player entities to burn stored fat instead of dealing
        /// damage directly. A fat-burning window is opened on the first hunger tick and absorbs
        /// subsequent ticks until the allocated fat budget (10 % of current fat) is exhausted,
        /// at which point the accumulated staved damage is passed through as a single hit.
        /// </summary>
        public static bool Prefix(Entity __instance, DamageSource damageSource, ref float damage)
        {
            if (damageSource.Type == EnumDamageType.Hunger && __instance is EntityPlayer player)
            {
                EntityBehaviorStomach stomach = player.GetBehavior<EntityBehaviorStomach>();
                if (stomach != null)
                {
                    // Initialise a new fat-burning window when hunger damage arrives and one isn't active.
                    if (!stomach.currentlyFendingOffHungerWithFatLoss
                        && stomach.fatToBeLostToHunger == 0)
                    {
                        float currentfat = stomach.FatMeter;
                        stomach.fatToBeLostToHunger    = currentfat * 0.1f;
                        stomach.totalFatLostToHunger   = 0;
                        stomach.hungerStavedByFatLoss  = 0;
                        stomach.currentlyFendingOffHungerWithFatLoss = true;
                    }
                    if (stomach.currentlyFendingOffHungerWithFatLoss)
                    {
                        float fatToBeLost = 0.0005f * stomach.fatLostToHungerMultiplier;
                        //TODO: adjust fatToBeLost based on mod difficulty level in config because I hate myself
                        //
                        // -- END TODO
                        float fatThatCanBeLost = stomach.fatToBeLostToHunger - stomach.totalFatLostToHunger;
                        if (fatThatCanBeLost < fatToBeLost)
                        {
                            // Fat budget exhausted: let residual accumulated damage through.
                            stomach.FatMeter -= fatToBeLost;
                            stomach.hungerStavedByFatLoss += damage;
                            stomach.hungerStavedByFatLoss  = Math.Min(stomach.hungerStavedByFatLoss, 20f);
                            damage = stomach.hungerStavedByFatLoss;
                            stomach.currentlyFendingOffHungerWithFatLoss = false;
                        }
                        else
                        {
                            // Fat budget still available: absorb all damage this tick.
                            stomach.totalFatLostToHunger  += fatToBeLost;
                            stomach.hungerStavedByFatLoss += damage;
                            stomach.hungerStavedByFatLoss  = Math.Min(stomach.hungerStavedByFatLoss, 20f);
                            damage = 0;
                            stomach.FatMeter -= fatToBeLost;
                        }
                    }
                }
            }

            return true;
        }
    }
}
