
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ExpandedStomach
{
    public class EntityBehaviorStomach : EntityBehavior
    {

        long serverListenerId;
        long serverListenerSlowId;
        public int StomachSize
        {
            get => entity.WatchedAttributes.TryGetInt("stomachSize") ?? 500;
            set
            {
                int tryInt = int.TryParse(value.ToString(), out int result) ? result : 0;
                result = GameMath.Clamp(tryInt, 500, 5500);
                entity.WatchedAttributes.SetInt("stomachSize", result);
            }
        }

        public float FatMeter
        {
            get => entity.WatchedAttributes.TryGetFloat("fatMeter") ?? 0;
            set
            {
                float tryFloat = float.TryParse(value.ToString(), out float result) ? result : 0;
                result = GameMath.Clamp(tryFloat, 0f, 1f);
                entity.WatchedAttributes.SetFloat("fatMeter", result);
            }
        }

        public float MaxSatiety
        {
            get => entity.WatchedAttributes.TryGetFloat("maxSatiety") ?? 1500f;
            set
            {
                float tryInt = float.TryParse(value.ToString(), out float result) ? result : 1500f;
                entity.WatchedAttributes.SetFloat("maxSatiety", tryInt);
            }
        }

        public float CurrentSatiety
        {
            get => entity.WatchedAttributes.TryGetFloat("currentSatiety") ?? 1500f;
            set
            {
                float tryF = float.TryParse(value.ToString(), out float result) ? result : 1500f;
                entity.WatchedAttributes.SetFloat("currentSatiety", tryF);
            }
        }

        public float ExpandedStomachMeter
        {
            get => entity.WatchedAttributes.TryGetFloat("expandedStomachMeter") ?? 0f;
            set
            {
                float tryF = float.TryParse(value.ToString(), out float result) ? result : 0f;
                entity.WatchedAttributes.SetFloat("expandedStomachMeter", tryF);
            }
        }

        private bool _needsSprintToMove;
        public bool NeedsSprintToMove
        {
            get => _needsSprintToMove;
            set => _needsSprintToMove = value;
        }

        private float _movementPenalty;
        public float MovementPenalty
        {
            get => _movementPenalty;
            set
            {
                float tryFloat = float.IsNaN(value) ? 0f : value;
                tryFloat = GameMath.Clamp(tryFloat, 0f, 0.5f);
                if(_movementPenalty != tryFloat)
                {
                    _movementPenalty = tryFloat;
                    UpdateWalkSpeed();
                }
            }
        }

        float lastFullTime = -1;
        
        public EntityBehaviorStomach(Entity entity) : base(entity)
        {
            if (!entity.WatchedAttributes.HasAttribute("stomachSize"))
            {
                StomachSize = 500;
            }
            if (!entity.WatchedAttributes.HasAttribute("fatMeter"))
            {
                FatMeter = 0;
            }
            if (!entity.WatchedAttributes.HasAttribute("expandedStomachMeter"))
            {
                ExpandedStomachMeter = 0;
            }
            var nutritionBehavior = entity.GetBehavior<EntityBehaviorHunger>();
            if (nutritionBehavior != null)
            {
                MaxSatiety = nutritionBehavior.MaxSaturation;
                CurrentSatiety = nutritionBehavior.Saturation;
            }
            else
            {
                if (!entity.WatchedAttributes.HasAttribute("maxSatiety"))
                {
                    //get the max satiety from the attributes
                    MaxSatiety = entity.WatchedAttributes.TryGetFloat("maxSatiety") ?? 1500f;
                }
                if (!entity.WatchedAttributes.HasAttribute("currentSatiety"))
                {
                    //get the current satiety from the attributes
                    CurrentSatiety = entity.WatchedAttributes.TryGetFloat("currentSatiety") ?? 1500f;
                }
            }
            if (entity.World.Side == EnumAppSide.Server)
            {
                serverListenerId = entity.World.RegisterGameTickListener(ServerTick2min, 120000, 2000); //2 min
                serverListenerSlowId = entity.World.RegisterGameTickListener(ServerTickSUPERSlow, 2880000, 2000); //48 min
            }
        }

        public void OnRespawn()
        {
            // halve stomach size
            StomachSize = StomachSize / 2;
        }

        private void CalculateMovementSpeedPenalty()
        {
            //cap to 50% movement penalty
            MovementPenalty = FatMeter * 0.5f;
        }

        private void UpdateWalkSpeed()
        {
            entity.Stats.Set("walkspeed", "fatPenalty", -MovementPenalty, true);
        }
        
        float strain = 0f;

        public void ServerTickSUPERSlow(float deltaTime)
        {
            //time to check if player is fat
            if (strain >= 0.75f) //if player is spending a lot of time overeating
            {
                FatMeter += 0.1f; //caps at 1
            } 
            else
            {
                FatMeter -= 0.05f; //caps at 0
            }
            FatMeter = Math.Clamp(strain, 0f, 1f);

            CalculateMovementSpeedPenalty();
            UpdateWalkSpeed();
        }

        float proximity = 0f;
        float buildrate = 0.01f;
        float decayrate = 0.005f;

        public void ServerTick2min(float deltaTime) // used to calculate expanded stomach size and if fat should rise
        {
            proximity = Math.Clamp(ExpandedStomachMeter / StomachSize, 0f, 1f);
            if(proximity >= 0.9f) // if 90% of stomach is full
            {
                strain += buildrate * (proximity - 0.9f) / 0.1f;
            }
            if(CurrentSatiety < MaxSatiety) // if player is not overeating, assume they're on a diet
            {
                proximity = 0.5f;
                strain -= decayrate * (1f - proximity);
            }
            strain = Math.Clamp(strain, 0f, 1f); //clamp values so they have a chance to lose fat if they decide to stop overeating
            if(ExpandedStomachMeter >= StomachSize * 0.9f) // if 90% of stomach is full
            {
                StomachSize += 50; //50 x 24 = 1200
            }
            if(ExpandedStomachMeter < 0.1f * StomachSize) // if 10% of stomach is empty
            {
                StomachSize -= 12; //12 x 24 = 288
            }

            var player = entity as EntityPlayer;
            var serverPlayer = player?.Player as IServerPlayer;
            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                        "Your stomach size is now " + StomachSize.ToString() + " units.\n" + 
                        "Your fat level is now " + (FatMeter * 100).ToString() + "%.",
                        EnumChatType.Notification);
        }

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown, float saturationLossDelay = 10, float nutritionGainMultiplier = 1)
        {
            //update last time player ate
            lastFullTime = entity.World.ElapsedMilliseconds / 1000f;
        }

        public bool HandlePlayerEating(float saturation, float overflowsat, float cooldown)
        {
            float maxSatiety = 1500f;
            float currentSatiety = 0f;
            var nutritionBehavior = entity.GetBehavior<EntityBehaviorHunger>();
            if (nutritionBehavior != null)
            {
                maxSatiety = nutritionBehavior.MaxSaturation;
                currentSatiety = nutritionBehavior.Saturation;
            }
            //note the last time the player ate
            if (currentSatiety >= maxSatiety)
            {
                lastFullTime = entity.World.ElapsedMilliseconds / 1000f;
            }
            var player = entity as EntityPlayer;
            var serverPlayer = player?.Player as IServerPlayer;
            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                        "Food received.\nCurrent satiety: " + currentSatiety + "/" + maxSatiety + "\nLast full: " + lastFullTime + "s\nFat Meter: " + FatMeter
                        + "\nSaturation of incoming food: " + saturation + "\nStomach Size: " + StomachSize,
                        EnumChatType.Notification);
            return true;
        }

        public override string PropertyName() => "expandedStomach";
    }
}
