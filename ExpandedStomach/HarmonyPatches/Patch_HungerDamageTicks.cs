using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ExpandedStomach.HarmonyPatches
{
    public static class Patch_HungerDamageTicks
    {
        public static void ApplyCorePatches(Harmony harmony)
        {
            MethodInfo EntityRD = (MethodInfo)IForgotToBringFoodWithMe.TargetMethod();
            MethodInfo EntityRDPrefix = AccessTools.Method(typeof(IForgotToBringFoodWithMe), nameof(IForgotToBringFoodWithMe.Prefix));
            harmony.Patch(EntityRD, prefix: new HarmonyMethod(EntityRDPrefix));
        }
    }

    public static class IForgotToBringFoodWithMe
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Entity), "ReceiveDamage");
        }

        public static bool Prefix(Entity __instance, DamageSource damageSource, ref float damage)
        {
            if(damageSource.Type == EnumDamageType.Hunger && __instance is EntityPlayer player)
            {
                //magic happens
                EntityBehaviorStomach stomach = player.GetBehavior<EntityBehaviorStomach>();
                if(stomach != null)
                {
                    if (!stomach.currentlyFendingOffHungerWithFatLoss 
                        && stomach.fatToBeLostToHunger == 0)
                    {
                        float currentfat = stomach.FatMeter;
                        stomach.fatToBeLostToHunger = currentfat * 0.1f;
                        stomach.totalFatLostToHunger = 0;
                        stomach.hungerStavedByFatLoss = 0;
                        stomach.currentlyFendingOffHungerWithFatLoss = true;
                    }
                    if (stomach.currentlyFendingOffHungerWithFatLoss) //if system is active
                    {
                        float fatToBeLost = 0.0005f * stomach.fatLostToHungerMultiplier;
                        //TODO: adjust fatToBeLost based on mod difficulty level in config because I hate myself
                        //
                        // -- END TODO
                        float fatThatCanBeLost = stomach.fatToBeLostToHunger - stomach.totalFatLostToHunger;
                        if (fatThatCanBeLost < fatToBeLost)
                        {
                            stomach.FatMeter -= fatToBeLost;
                            stomach.hungerStavedByFatLoss += damage;
                            if (stomach.hungerStavedByFatLoss > 20f)
                                stomach.hungerStavedByFatLoss = 20f;
                            damage = stomach.hungerStavedByFatLoss;
                            stomach.currentlyFendingOffHungerWithFatLoss = false;
                        }
                        else
                        {
                            stomach.totalFatLostToHunger += fatToBeLost;
                            stomach.hungerStavedByFatLoss += damage;
                            if(stomach.hungerStavedByFatLoss > 20f)
                                stomach.hungerStavedByFatLoss = 20f;
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
