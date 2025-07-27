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
using System.Runtime.CompilerServices;


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

        public void SetFatMeter(float value)
        {
            FatMeter = value;
            CalculateMovementSpeedPenalty();
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

        public float ExpandedStomachCapToday
        {
            get => StomachAttributes.GetFloat("expandedStomachCapToday", 0);
            set
            {
                StomachAttributes.SetFloat("expandedStomachCapToday", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public float ExpandedStomachCapAverage
        {
            get => StomachAttributes.GetFloat("expandedStomachCapAverage", 0);
            set
            {
                StomachAttributes.SetFloat("expandedStomachCapAverage", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public float SatConsumedToday
        {
            get => StomachAttributes.GetFloat("satConsumedToday", 0);
            set
            {
                StomachAttributes.SetFloat("satConsumedToday", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public bool OopsWeDied
        {
            get => StomachAttributes.GetBool("OopsWeDied", false);
            set
            {
                StomachAttributes.SetBool("OopsWeDied", value);
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
                tryFloat = GameMath.Clamp(tryFloat, 0f, 0.4f);
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

        bool debugmode = false;

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
            if(entity.Api.World.Config.GetString("ExpandedStomach.difficulty") == "hard")
                OopsWeDied = true;
        }

        private void CalculateMovementSpeedPenalty()
        {
            //cap to 50% movement penalty
            MovementPenalty = FatMeter * entity.Api.World.Config.GetFloat("ExpandedStomach.drawbackSeverity");
        }

        private void UpdateWalkSpeed()
        {
            entity.Stats.Set("walkspeed", "fatPenalty", -MovementPenalty, false);
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
            debugmode = entity.World.Config.GetBool("ExpandedStomach.debugMode");
        }

        public void ServerTickSUPERSlow(float deltaTime)
        {
            // roll the dice to see if player is fat
            // probability of getting fat is determined by strain value. The higher the value, the higher the chance of getting fat
            int today = (int)Math.Floor(entity.World.Calendar.TotalDays);
            if (today > days) // if a day has passed
            {
                if(OopsWeDied) OopsWeDied = false;
                averagestrain = (averagestrain * 6 + strain) / 7;
                ExpandedStomachCapAverage = (ExpandedStomachCapAverage * 6 + ExpandedStomachCapToday) / 7;
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
            bool dieting = strain < laststrain && !ExpandedStomachWasActive;
            float fatlossChance = 1-strain;

            string smessage;
            int newstomachsize = GameMath.Max((int)ExpandedStomachCapAverage * 2, 500); //auto caps to 500 if too low
            bool stomachsizechanged = newstomachsize.isDifferent(StomachSize);

            if (newstomachsize > StomachSize)
            {
                smessage = Lang.Get("expandedstomach:stomachwillgrow");
            }
            else
            {
                smessage = Lang.Get("expandedstomach:stomachwillshrink");
            }
            StomachSize = newstomachsize;
            if (entity.Api.World.Config.GetString("ExpandedStomach.difficulty") == "easy" || debugmode == true)
            {
                smessage += " (" + StomachSize.ToString() + " units)";
            }

            float oldFatMeter = FatMeter;

            if (overeating)
            {
                //roll to see if fat meter goes up
                if (rand.NextDouble() < strain) // probability scaled by strain
                {
                    FatMeter += 0.0025f * (1 + averagestrain); // reduced from 0.01f to 0.0025f for slower fat gain
                }
            }
            else if (dieting)
            {
                if (rand.NextDouble() < fatlossChance) // 50% chance
                {
                    FatMeter -= 0.002f; // reduced from 0.01f to 0.002f for slower fat loss
                }
            }

            bool FatMeterChanged = FatMeter.isDifferent(oldFatMeter);

            switch (entity.Api.World.Config.GetString("ExpandedStomach.difficulty"))
            {
                case "easy":
                case "normal":
                    smessage += "\nYour fat level is now " + FatMeter.ToString() + " units.";
                    if (FatMeterChanged) serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                        smessage,
                        EnumChatType.Notification);
                    break;
                case "hard":
                    if (debugmode == true)
                    {
                        smessage += "\nYour fat level is now " + FatMeter.ToString() + " units.";
                        if (FatMeterChanged) serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                            smessage,
                            EnumChatType.Notification);
                    }
                    break;
            }
        }

        float proximity = 0f;
        float buildrate = 0.04f;  // increased from 0.01f to 0.04f to build strain faster
        float decayrate = 0.01f;  // increased from 0.005f to 0.01f to decay strain faster

        public void ServerTick2min(float deltaTime) // used to calculate expanded stomach size and if fat should rise
        {
            float buildratemult = entity.Api.World.Config.GetFloat("ExpandedStomach.strainGainRate");
            float decayratemult = entity.Api.World.Config.GetFloat("ExpandedStomach.strainLossRate");

            float newbuildrate = buildrate * buildratemult;
            float newdecayrate = decayrate * decayratemult;

            if (ExpandedStomachMeter > ExpandedStomachCapToday) ExpandedStomachCapToday = ExpandedStomachMeter;
            proximity = Math.Clamp(ExpandedStomachMeter / StomachSize, 0f, 1f);
            if (proximity > 0f) ExpandedStomachWasActive = true;
            if (proximity >= 0.5f) // if 50% of stomach is full
            {
                strain += newbuildrate * (proximity - 0.5f) / 0.1f; // increases faster the closer to the limit
            }
            if (CurrentSatiety < 1000) // if player is not overeating, assume they're on a diet
            {
                proximity = 0.5f;
                strain -= newdecayrate * (1f - proximity);
                // lower fat level?
            }
            strain = Math.Clamp(strain, 0f, 1f);
        }

        DateTime lastrecievedsaturation; // put a cooldown on the messages

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown, float saturationLossDelay = 10, float nutritionGainMultiplier = 1)
        {
            //update last time player ate
            float percentfull = ExpandedStomachMeter / StomachSize;
            if (percentfull <= 0 ) return;
            if (DateTime.Now > lastrecievedsaturation + TimeSpan.FromSeconds(1) && !OopsWeDied)
            {
                lastrecievedsaturation = DateTime.Now;
                //get stomach sat and size and calculate percentage
                
                if (entity.Api.World.Config.GetBool("ExpandedStomach.immersiveMessages") && saturation >= 0)
                {
                    bool messageset = false;
                    var player = entity as EntityPlayer;
                    var serverPlayer = player?.Player as IServerPlayer;
                    //serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                    //    "Stomach Sat/Size: " + ExpandedStomachMeter + "/" + StomachSize,
                    //    EnumChatType.Notification);
                    string message = "";
                    if (percentfull.between(0.25f, 0.5f))
                    {
                        //get message from language file
                        message = Lang.Get("expandedstomach:stomachover25");
                        messageset = true;
                    }
                    else if (percentfull.between(0.5f, 0.75f))
                    {
                        message = Lang.Get("expandedstomach:stomachover50");
                        messageset = true;
                    }
                    else if (percentfull.between(0.75f, 1f))
                    {
                        message = Lang.Get("expandedstomach:stomachover75");
                        messageset = true;
                    }
                    else if (percentfull >= 1f)
                    {
                        message = Lang.Get("expandedstomach:stomachover100");
                        messageset = true;
                    }
                    if (messageset) serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "     \"" + message + "\"", EnumChatType.Notification);
                }
            }
            else
            {
                lastrecievedsaturation = DateTime.Now;
            }
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
public static class ExtensionMethods
{
    public static bool between(this float value, float a, float b)
    {
        return value >= a && value < b;
    }

    public static bool isDifferent(this int value, int a)
    {
        return a != value;
    }

    public static bool isDifferent(this float value, float a)
    {
        return a != value;
    }
}