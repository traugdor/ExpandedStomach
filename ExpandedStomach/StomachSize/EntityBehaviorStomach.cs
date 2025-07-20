
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
                entity.WatchedAttributes.MarkPathDirty("stomachSize");
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
                entity.WatchedAttributes.MarkPathDirty("fatMeter");
            }
        }

        public float MaxSatiety
        {
            get => entity.WatchedAttributes.TryGetFloat("maxSatiety") ?? 1500f;
            set
            {
                float tryInt = float.TryParse(value.ToString(), out float result) ? result : 1500f;
                entity.WatchedAttributes.SetFloat("maxSatiety", tryInt);
                entity.WatchedAttributes.MarkPathDirty("maxSatiety");
            }
        }

        public float CurrentSatiety //just an accessor for base game
        {
            get => entity.WatchedAttributes.GetTreeAttribute("hunger").GetFloat("currentsaturation");
        }

        public float ExpandedStomachMeter
        {
            get => entity.WatchedAttributes.TryGetFloat("expandedStomachMeter") ?? 0f;
            set
            {
                float tryF = float.TryParse(value.ToString(), out float result) ? result : 0f;
                entity.WatchedAttributes.SetFloat("expandedStomachMeter", tryF);
                entity.WatchedAttributes.MarkPathDirty("expandedStomachMeter");
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

        public float strain
        {
            get => entity.WatchedAttributes.TryGetFloat("strain") ?? 0f;
            set {
                entity.WatchedAttributes.SetFloat("strain", value);
                entity.WatchedAttributes.MarkPathDirty("strain");
            }
        }

        public float laststrain
        {
            get => entity.WatchedAttributes.TryGetFloat("laststrain") ?? 0f;
            set
            {
                entity.WatchedAttributes.SetFloat("laststrain", value);
                entity.WatchedAttributes.MarkPathDirty("laststrain");
            }
        }

        public float averagestrain
        {
            get => entity.WatchedAttributes.TryGetFloat("averagestrain") ?? 0f;
            set
            {
                entity.WatchedAttributes.SetFloat("averagestrain", value);
                entity.WatchedAttributes.MarkPathDirty("averagestrain");
            }
        }

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
            }
            else
            {
                if (!entity.WatchedAttributes.HasAttribute("maxSatiety"))
                {
                    //get the max satiety from the attributes
                    MaxSatiety = entity.WatchedAttributes.TryGetFloat("maxSatiety") ?? 1500f;
                }
            }
            if (entity.World.Side == EnumAppSide.Server)
            {
                serverListenerId = entity.World.RegisterGameTickListener(ServerTick2min, 120000, 2000); //2 min
                serverListenerSlowId = entity.World.RegisterGameTickListener(ServerTickSUPERSlow, 60000, 2000); //1 min
            }
            try// just in case
            {
                int tempdays = entity.WatchedAttributes.TryGetInt("days") ?? 0;
            }
            catch (Exception e)
            {
                entity.WatchedAttributes.SetInt("days", 0);
                entity.WatchedAttributes.MarkPathDirty("days");
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
            MovementPenalty = FatMeter * 0.4f;
        }

        private void UpdateWalkSpeed()
        {
            entity.Stats.Set("walkspeed", "fatPenalty", -MovementPenalty, true);
        }

        int days
        {
            get => entity.WatchedAttributes.TryGetInt("days") ?? 0;
            set
            {
                entity.WatchedAttributes.SetInt("days", value);
                entity.WatchedAttributes.MarkPathDirty("days");
            }
        }

        bool ExpandedStomachWasActive = false;

        public void ServerTickSUPERSlow(float deltaTime)
        {
            // roll the dice to see if player is fat
            // probability of getting fat is determined by strain value. The higher the value, the higher the chance of getting fat
            int today = (int)Math.Floor(entity.World.Calendar.TotalDays);
            if(today > days) // if a day has passed
            {
                averagestrain = ((float)days * averagestrain + strain) / ((float)today);
                days = today;
                CalculateFatandStomachSize();
                CalculateMovementSpeedPenalty();
                UpdateWalkSpeed();

                laststrain = strain; //reset strain amounts
                ExpandedStomachWasActive = false;
            }
        }

        private void CalculateFatandStomachSize()
        {
            // calculate both gains and losses.
            //calculate fat level, stomach size, etc
            Random rand = new Random();
            var player = entity as EntityPlayer;
            var serverPlayer = player?.Player as IServerPlayer;
            // determine if gain or loss

            // overeating = strain higher than previous day
            // stable = strain same as previous day it neither went up nor down, or it's lower but expanded stomach was active
            // dieting = strain lower than previous day

            bool overeating = strain > laststrain;
            bool stable1 = strain == laststrain;
            bool stable2 = strain < laststrain && ExpandedStomachWasActive;
            bool dieting = strain < laststrain && !ExpandedStomachWasActive;

            if(overeating)
            {
                StomachSize += 150;
                //roll to see if fat meter goes up
                if(rand.NextDouble() < 0.5) // 50% chance
                {
                    FatMeter += 0.01f  * (1 + averagestrain); //increase slowly but more if strain values are high
                }
            }
            else if (stable1 || stable2)
            {
                //not pushing limits, decrease stomach amount
                StomachSize -= 75;
            }
            else if (dieting)
            {
                //actively not eating
                StomachSize -= 150;
                if (rand.NextDouble() < 0.5) // 50% chance
                {
                    FatMeter -= 0.01f; // decrease 2x slower
                }
            }

            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                        "Your stomach size is now " + StomachSize.ToString() + " units." +
                        "\nYour fat level is now " + FatMeter.ToString() + " units." +
                        "\nstrain: " + strain.ToString() + "  averagestrain: " + averagestrain.ToString(),
                        EnumChatType.Notification);
        }

        float proximity = 0f;
        float buildrate = 0.01f;
        float decayrate = 0.005f;

        public void ServerTick2min(float deltaTime) // used to calculate expanded stomach size and if fat should rise
        {
            proximity = Math.Clamp(ExpandedStomachMeter / StomachSize, 0f, 1f);
            if (proximity > 0f) ExpandedStomachWasActive = true;
            if(proximity >= 0.9f) // if 90% of stomach is full
            {
                strain += buildrate * (proximity - 0.9f) / 0.1f; // increases faster the closer to the limit
            }
            if(CurrentSatiety < MaxSatiety) // if player is not overeating, assume they're on a diet
            {
                proximity = 0.5f;
                strain -= decayrate * (1f - proximity);
                // lower fat level?
            }
            strain = Math.Clamp(strain, 0f, 1f);
        }

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown, float saturationLossDelay = 10, float nutritionGainMultiplier = 1)
        {
            //update last time player ate
            ; //do nothing... we might even remove this entire function later
        }

        public override string PropertyName() => "expandedStomach";
    }
}
