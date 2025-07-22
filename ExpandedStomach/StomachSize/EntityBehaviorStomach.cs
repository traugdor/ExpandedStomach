using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods.NoObf;
using Vintagestory.API.Config;
using Vintagestory.API.Server;


namespace ExpandedStomach
{
    public class EntityBehaviorStomach : EntityBehavior
    {

        long serverListenerId;
        long serverListenerSlowId;

        private static readonly Random rand = new Random();

        public ITreeAttribute StomachAttributes
        {
            get => entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            set
            {
                entity.WatchedAttributes.SetAttribute("expandedStomach", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public int StomachSize
        {
            get => StomachAttributes.GetInt("stomachSize", 500);
            set
            {
                int result = GameMath.Clamp(value, 500, 5500);
                StomachAttributes.SetInt("stomachSize", result);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public float FatMeter
        {
            get => StomachAttributes.GetFloat("fatMeter", 0f);
            set
            {
                float result = GameMath.Clamp(value, 0f, 1f);
                StomachAttributes.SetFloat("fatMeter", result);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public float MaxSatiety //just an accessor for base game
        {
            get => entity.WatchedAttributes.GetTreeAttribute("hunger").GetFloat("maxsaturation");
        }

        public float CurrentSatiety //just an accessor for base game
        {
            get => entity.WatchedAttributes.GetTreeAttribute("hunger").GetFloat("currentsaturation");
        }

        public float ExpandedStomachMeter
        {
            get => StomachAttributes.GetFloat("expandedStomachMeter", 0);
            set
            {
                StomachAttributes.SetFloat("expandedStomachMeter", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
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
            get => StomachAttributes.GetFloat("strain", 0);
            set {
                StomachAttributes.SetFloat("strain", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public float laststrain
        {
            get => StomachAttributes.GetFloat("laststrain", 0);
            set
            {
                StomachAttributes.SetFloat("laststrain", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public float averagestrain
        {
            get => StomachAttributes.GetFloat("averagestrain", 0);
            set
            {
                StomachAttributes.SetFloat("averagestrain", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
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

        int dayCountOffset
        {
            get => entity.WatchedAttributes.TryGetInt("dayCountOffset") ?? 0;
            set
            {
                entity.WatchedAttributes.SetInt("dayCountOffset", value);
                entity.WatchedAttributes.MarkPathDirty("dayCountOffset");
            }
        }

        bool ExpandedStomachWasActive = false;

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);
            // halve stomach size if enabled
            if (entity.Api.World.Config.GetBool("ExpandedStomach.hardcoreDeath") == true)
            {
                StomachSize /= 2;
            }
            ExpandedStomachMeter = 0;
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

        public EntityBehaviorStomach(Entity entity) : base(entity)
        {
            if (entity.World.Side == EnumAppSide.Server)
            {
                serverListenerId = entity.World.RegisterGameTickListener(ServerTick2min, 120000, 2000); //2 min
                serverListenerSlowId = entity.World.RegisterGameTickListener(ServerTickSUPERSlow, 60000, 2000); //1 min
            }

            //create tree attribute and set all values if it doesn't exist
            if (!entity.WatchedAttributes.HasAttribute("expandedStomach"))
            {
                entity.WatchedAttributes.SetAttribute("expandedStomach", new TreeAttribute());
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
                //set default values
                StomachSize = 1;
                FatMeter = 0;
                ExpandedStomachMeter = 0;
                strain = 0;
                laststrain = 0;
                averagestrain = 0;
                CalculateMovementSpeedPenalty();
            }
            if (!entity.WatchedAttributes.HasAttribute("dayCountOffset"))
            {
                dayCountOffset = (int)Math.Floor(entity.World.Calendar.TotalDays);
                days = dayCountOffset;
            }
        }

        public void ServerTickSUPERSlow(float deltaTime)
        {
            // roll the dice to see if player is fat
            // probability of getting fat is determined by strain value. The higher the value, the higher the chance of getting fat
            int today = (int)Math.Floor(entity.World.Calendar.TotalDays);
            if(today > days) // if a day has passed
            {
                averagestrain = (((float)(days-dayCountOffset) * averagestrain) + strain) / (float)(today-dayCountOffset);
                days = today;
                CalculateFatandStomachSize();
                CalculateMovementSpeedPenalty();

                laststrain = strain; //reset strain amounts
                ExpandedStomachWasActive = false;
            }
        }

        private void CalculateFatandStomachSize()
        {
            // calculate both gains and losses.
            //calculate fat level, stomach size, etc
            
            var player = entity as EntityPlayer;
            var serverPlayer = player?.Player as IServerPlayer;
            // determine if gain or loss

            // overeating = strain higher than previous day
            // stable = strain same as previous day it neither went up nor down, or it's lower but expanded stomach was active
            // dieting = strain lower than previous day

            bool overeating = strain > averagestrain;
            bool stable1 = strain == laststrain;
            bool stable2 = strain < laststrain && ExpandedStomachWasActive;
            bool dieting = strain < laststrain && !ExpandedStomachWasActive;

            if(overeating)
            {
                StomachSize += 50;
                //roll to see if fat meter goes up
                if(rand.NextDouble() < strain) // 50% chance
                {
                    FatMeter += 0.01f  * (1 + averagestrain); //increase slowly but more if strain values are high
                }
            }
            else if (stable1 || stable2)
            {
                //not pushing limits, decrease stomach amount
                StomachSize -= 25;
            }
            else if (dieting)
            {
                //actively not eating
                StomachSize -= 50;
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
            var player = entity as EntityPlayer;
            var serverPlayer = player?.Player as IServerPlayer;
            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                "Stomach Sat/Size: " + ExpandedStomachMeter + "/" + StomachSize,
                EnumChatType.Notification);
        }

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown, float saturationLossDelay = 10, float nutritionGainMultiplier = 1)
        {
            //update last time player ate
            ; //do nothing... we might even remove this entire function later
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            
            if (serverListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(serverListenerId);
                entity.World.UnregisterGameTickListener(serverListenerSlowId);
            }
        }

        public override string PropertyName() => "expandedStomach";
    }
}
