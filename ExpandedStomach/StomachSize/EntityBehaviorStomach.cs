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
using System.Threading.Tasks;


namespace ExpandedStomach
{
    public class EntityBehaviorStomach : EntityBehavior
    {

        long serverListenerId;
        long serverListenerSlowId;
        long satietyWatchDogId;
        long stuffedListenerId = 0;

        public bool starvation = false;
        public DateTime StarvationLastEatTime = DateTime.Now;
        public DateTime StarvationCurrentTime = DateTime.Now;
        public float StarvationBankedDamage = 0f;
        public bool stuffed = false;
        public int overStuffedTimeDelay;
        public float overStuffedThreshold;

        public float fatToBeLostToHunger = 0f;
        public float totalFatLostToHunger = 0f;
        public float hungerStavedByFatLoss = 0f;
        public bool currentlyFendingOffHungerWithFatLoss = false;
        public float fatLostToHungerMultiplier = 0f;

        private static readonly Random rand = new Random();

        // Stomach size bounds
        private  const int MinStomachSize =  500;
        internal const int MaxStomachSize = 5500;

        // Movement penalty
        private const float MaxMovementPenalty      = 0.4f;
        private const float StuffedPenaltyFloor     = 0.1f;
        private const float StuffedPenaltyScale     = 0.9f;

        // Tick intervals (milliseconds)
        private const int   TickInterval2Min        = 120000;
        private const int   TickInterval1Min        = 60000;
        private const int   TickIntervalWatchdog    = 100;
        private const int   TickJitter              = 2000;
        private const int   StuffedRecheckInterval  = 2000;

        // Hunger / strain thresholds
        private const float DietingSatietyThreshold  = 1000f;
        private const float StrainProximityThreshold = 0.5f;

        // Strain rates
        private const float BaseStrainBuildRate     = 0.04f;
        private const float BaseStrainDecayRate     = 0.01f;

        // Stomach growth
        private const int   StandardMonthDays       = 9;
        private const int   MaxDailyStomachChange   = 100;

        // Fat system
        private const float FatGainPerDay           = 0.0025f;
        private const float FatLossRateEasy         = 0.004f;
        private const float FatLossRateNormal       = 0.002f;
        private const float DifficultyRollBias      = 0.25f;

        // Milestone thresholds (used for both fat meter and stomach fill messages)
        private const float Tier1Threshold          = 0.25f;
        private const float Tier2Threshold          = 0.5f;
        private const float Tier3Threshold          = 0.75f;

        // Message cooldown
        private static readonly TimeSpan MessageCooldown = TimeSpan.FromSeconds(1);

        public override void OnEntityRevive()
        {
            base.OnEntityRevive();
        }

        public ITreeAttribute StomachAttributes
        {
            get => entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            set
            {
                entity.WatchedAttributes.SetAttribute("expandedStomach", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public float satietyBeforeEating
        {
            get => StomachAttributes.GetFloat("satietyBeforeEating", 0f);
            set
            {
                StomachAttributes.SetFloat("satietyBeforeEating", value);
                entity.WatchedAttributes.MarkPathDirty("expandedStomach");
            }
        }

        public int StomachSize
        {
            get => StomachAttributes.GetInt("stomachSize", MinStomachSize);
            set
            {
                int result = GameMath.Clamp(value, MinStomachSize, MaxStomachSize);
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

        public void SatietyWatchDog(float dt)
        {
            if (ExpandedStomachMeter > 0 && CurrentSatiety < MaxSatiety)
            {
                float diff = MaxSatiety - CurrentSatiety;
                if (ExpandedStomachMeter > diff)
                {
                    ExpandedStomachMeter -= diff;
                    entity.WatchedAttributes.GetTreeAttribute("hunger").SetFloat("currentsaturation", MaxSatiety);
                    entity.WatchedAttributes.MarkPathDirty("hunger");
                }
                else
                {
                    diff -= ExpandedStomachMeter;
                    ExpandedStomachMeter = 0;
                    entity.WatchedAttributes.GetTreeAttribute("hunger").SetFloat("currentsaturation", CurrentSatiety + diff);
                    entity.WatchedAttributes.MarkPathDirty("hunger");
                }
            }
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
                tryFloat = GameMath.Clamp(tryFloat, 0f, MaxMovementPenalty);
                if (_movementPenalty != tryFloat)
                {
                    _movementPenalty = tryFloat;
                    UpdateWalkSpeed();
                }
            }
        }

        public float strain
        {
            get => StomachAttributes.GetFloat("strain", 0);
            set
            {
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
            if (entity.Api.World.Config.GetString("ExpandedStomach.difficulty") == "hard")
                OopsWeDied = true;
        }

        internal void CalculateMovementSpeedPenalty()
        {
            //cap to 50% movement penalty
            float penalty = FatMeter * entity.Api.World.Config.GetFloat("ExpandedStomach.drawbackSeverity");
            if (stuffed)
            {
                penalty = StuffedPenaltyFloor + StuffedPenaltyScale * penalty;
            }
            MovementPenalty = penalty;
        }

        private void UpdateWalkSpeed()
        {
            entity.Stats.Set("walkspeed", "fatPenalty", -MovementPenalty, false);
        }

        public EntityBehaviorStomach(Entity entity) : base(entity)
        {
            if (entity.World.Side == EnumAppSide.Server)
            {
                serverListenerId     = entity.World.RegisterGameTickListener(ServerTick2min,      TickInterval2Min,    TickJitter);
                serverListenerSlowId = entity.World.RegisterGameTickListener(ServerTickSUPERSlow, TickInterval1Min,    TickJitter);
                satietyWatchDogId    = entity.World.RegisterGameTickListener(SatietyWatchDog,     TickIntervalWatchdog, 0);
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
                satietyBeforeEating = 0;
            }
            if (!entity.WatchedAttributes.HasAttribute("dayCountOffset"))
            {
                dayCountOffset = (int)Math.Floor(entity.World.Calendar.TotalDays);
                days = dayCountOffset;
            }
            CalculateMovementSpeedPenalty();
            debugmode = entity.World.Config.GetBool("ExpandedStomach.debugMode");
            overStuffedTimeDelay = entity.World.Config.GetInt("ExpandedStomach.overStuffedTimeDelay");
            fatLostToHungerMultiplier = entity.World.Config.GetFloat("ExpandedStomach.fatLostToHungerMultiplier");
        }

        public void ServerTickSUPERSlow(float deltaTime)
        {
            // roll the dice to see if player is fat
            // probability of getting fat is determined by strain value. The higher the value, the higher the chance of getting fat
            int today = (int)Math.Floor(entity.World.Calendar.TotalDays);
            if (today > days) // if a day has passed
            {
                if (OopsWeDied) OopsWeDied = false;
                averagestrain = (averagestrain * 6 + strain) / 7;
                ExpandedStomachCapAverage = (ExpandedStomachCapAverage * 6 + ExpandedStomachCapToday) / 7;
                ExpandedStomachCapToday = 0;
                days = today;
                CalculateFatandStomachSize();
                CalculateMovementSpeedPenalty();

                laststrain = strain; //reset strain amounts
                ExpandedStomachWasActive = false;
            }
        }

        private void CalculateFatandStomachSize()
        {
            var player = entity as EntityPlayer;
            var serverPlayer = player?.Player as IServerPlayer;

            bool overeating = strain == 1 || strain > laststrain;
            bool maintaining = strain <= laststrain && ExpandedStomachWasActive;
            bool dieting = strain <= laststrain && !ExpandedStomachWasActive;
            float fatlossChance = 1 - strain;

            string smessage = "";
            bool immersiveMessages = entity.Api.World.Config.GetBool("ExpandedStomach.immersiveMessages");
            int increasedifference = GameMath.Clamp((int)ExpandedStomachCapAverage * 2 - StomachSize, -MaxDailyStomachChange, MaxDailyStomachChange);
            // standard length of a month = 9 days
            int daysPerMonth = entity.World.Calendar.DaysPerMonth;
            if (daysPerMonth > StandardMonthDays) increasedifference = increasedifference * StandardMonthDays / daysPerMonth;
            string difficulty = entity.Api.World.Config.GetString("ExpandedStomach.difficulty");
            switch (difficulty)
            {
                case "easy":
                    if (increasedifference > 0)
                        increasedifference *= 2;
                    if (increasedifference < 0)
                        increasedifference /= 2;
                    break;
                case "hard":
                    increasedifference /= 2;
                    break;

            }
            int newstomachsize = GameMath.Max(StomachSize + increasedifference, MinStomachSize); //auto caps to MinStomachSize if too low
            bool stomachsizechanged = newstomachsize.isDifferent(StomachSize); //why is this here???

            if (newstomachsize > StomachSize)
            {
                smessage = Lang.Get("expandedstomach:stomachwillgrow");
            }
            else if (newstomachsize < StomachSize && newstomachsize > MinStomachSize)
            {
                smessage = Lang.Get("expandedstomach:stomachwillshrink");
            }
            StomachSize = newstomachsize;
            if (newstomachsize > MaxStomachSize) smessage = Lang.Get("expandedstomach:stomachatmax");
            if (difficulty == "easy" || debugmode == true)
            {
                smessage += "\nStomach size is " + StomachSize.ToString() + " units.";
            }

            float oldFatMeter = FatMeter;
            // need to recalculate based on multipliers from config
            float fatgainMultiplier = entity.Api.World.Config.GetFloat("ExpandedStomach.fatGainRate");
            float fatlossMultiplier = entity.Api.World.Config.GetFloat("ExpandedStomach.fatLossRate");

            if (overeating)
            {
                //roll to see if fat meter goes up
                switch (difficulty)
                {
                    case "easy":
                        if (rand.NextDouble() + DifficultyRollBias < strain) // fat increases are skewed to make them less common
                        {
                            FatMeter += (FatGainPerDay * (1 + averagestrain) * fatgainMultiplier);
                        }
                        break;
                    case "normal":
                        if (rand.NextDouble() < strain)
                        {
                            FatMeter += (FatGainPerDay * (1 + averagestrain) * fatgainMultiplier);
                        }
                        break;
                    case "hard":
                        if (rand.NextDouble() - DifficultyRollBias < strain) // fat increases are skewed to make them MORE common
                        {
                            FatMeter += (FatGainPerDay * (1 + averagestrain) * fatgainMultiplier);
                        }
                        break;
                }
            }
            else if (dieting)
            {
                switch (difficulty)
                {
                    case "easy":
                        if (rand.NextDouble() - DifficultyRollBias < fatlossChance) // fat decreases are skewed to make them more common
                        {
                            FatMeter -= FatLossRateEasy * fatlossMultiplier;
                        }
                        break;
                    case "normal":
                        if (rand.NextDouble() < fatlossChance)
                        {
                            FatMeter -= FatLossRateNormal * fatlossMultiplier;
                        }
                        break;
                    case "hard":
                        if (rand.NextDouble() + DifficultyRollBias < fatlossChance) // fat decreases are skewed to make them less common
                        {
                            FatMeter -= FatLossRateNormal * fatlossMultiplier;
                        }
                        break;
                }
            }

            bool FatMeterChanged = FatMeter.isDifferent(oldFatMeter);

            switch (entity.Api.World.Config.GetString("ExpandedStomach.difficulty"))
            {
                case "easy":
                    if (immersiveMessages)
                    {
                        if (FatMeterChanged)
                        {
                            if (smessage != "") smessage += "\n\n";
                            if (FatMeter >= Tier1Threshold && oldFatMeter < Tier1Threshold) smessage += Lang.Get("expandedstomach:bodyfatplus25");
                            if (FatMeter >= Tier2Threshold && oldFatMeter < Tier2Threshold) smessage += Lang.Get("expandedstomach:bodyfatplus50");
                            if (FatMeter >= Tier3Threshold && oldFatMeter < Tier3Threshold) smessage += Lang.Get("expandedstomach:bodyfatplus75");
                            if (FatMeter >= 1 && oldFatMeter < 1) smessage += Lang.Get("expandedstomach:bodyfatworst");
                            if (FatMeter <= 0 && oldFatMeter > 0) smessage += Lang.Get("expandedstomach:bodyfatperfect");
                            if (FatMeter <= Tier1Threshold && oldFatMeter > Tier1Threshold) smessage += Lang.Get("expandedstomach:bodyfatminus25");
                            if (FatMeter <= Tier2Threshold && oldFatMeter > Tier2Threshold) smessage += Lang.Get("expandedstomach:bodyfatminus50");
                            if (FatMeter <= Tier3Threshold && oldFatMeter > Tier3Threshold) smessage += Lang.Get("expandedstomach:bodyfatminus75");
                        }
                    }
                    smessage += "\nYour fat level is now " + (FatMeter * 100).ToString() + "%.";
                    serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                        smessage.Clean(),
                        EnumChatType.Notification);
                    break;
                case "normal":
                    if (immersiveMessages)
                    {
                        if (FatMeterChanged)
                        {
                            if (smessage != "") smessage += "\n\n";
                            if (FatMeter >= Tier1Threshold && oldFatMeter < Tier1Threshold) smessage += Lang.Get("expandedstomach:bodyfatplus25");
                            if (FatMeter >= Tier2Threshold && oldFatMeter < Tier2Threshold) smessage += Lang.Get("expandedstomach:bodyfatplus50");
                            if (FatMeter >= Tier3Threshold && oldFatMeter < Tier3Threshold) smessage += Lang.Get("expandedstomach:bodyfatplus75");
                            if (FatMeter >= 1 && oldFatMeter < 1) smessage += Lang.Get("expandedstomach:bodyfatworst");
                            if (FatMeter <= 0 && oldFatMeter > 0) smessage += Lang.Get("expandedstomach:bodyfatperfect");
                            if (FatMeter <= Tier1Threshold && oldFatMeter > Tier1Threshold) smessage += Lang.Get("expandedstomach:bodyfatminus25");
                            if (FatMeter <= Tier2Threshold && oldFatMeter > Tier2Threshold) smessage += Lang.Get("expandedstomach:bodyfatminus50");
                            if (FatMeter <= Tier3Threshold && oldFatMeter > Tier3Threshold) smessage += Lang.Get("expandedstomach:bodyfatminus75");
                        }
                        if (!string.IsNullOrEmpty(smessage.Clean().Trim()))
                        {
                            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                            smessage.Clean(),
                            EnumChatType.Notification);
                        }
                    }
                    if (debugmode == true)
                    {
                        smessage += "\nYour fat level is now " + (FatMeter * 100).ToString() + "%.";
                        serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                            smessage.Clean(),
                            EnumChatType.Notification);
                    }
                    break;
                case "hard":
                    if (debugmode == true)
                    {
                        smessage += "\nYour fat level is now " + (FatMeter * 100).ToString() + "%.";
                        serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                            smessage.Clean(),
                            EnumChatType.Notification);
                    }
                    break;
            }
        }

        float proximity = 0f;

        public void ServerTick2min(float deltaTime) // used to calculate expanded stomach size and if fat should rise
        {
            float buildratemult = entity.Api.World.Config.GetFloat("ExpandedStomach.strainGainRate");
            float decayratemult = entity.Api.World.Config.GetFloat("ExpandedStomach.strainLossRate");

            float newbuildrate = BaseStrainBuildRate * buildratemult;
            float newdecayrate = BaseStrainDecayRate * decayratemult;

            if (ExpandedStomachMeter > ExpandedStomachCapToday) ExpandedStomachCapToday = ExpandedStomachMeter;
            proximity = Math.Clamp(ExpandedStomachMeter / StomachSize, 0f, 1f);
            if (proximity > 0f) ExpandedStomachWasActive = true;
            if (proximity >= StrainProximityThreshold) // if 50% of stomach is full
            {
                strain += newbuildrate * (proximity - StrainProximityThreshold) / 0.1f; // increases faster the closer to the limit
            }
            if (proximity < StrainProximityThreshold && proximity > 0f) // if 50% of stomach is empty, assume maintenance mode. Freeze fat levels?
            {
                float newstrain = strain - newdecayrate * StrainProximityThreshold;
                newstrain = Math.Clamp(newstrain, StrainProximityThreshold, 1f);
                if (newstrain < strain) strain = newstrain;
            }
            if (proximity == 0f && CurrentSatiety >= DietingSatietyThreshold) // if stomach is empty but not dieting, assume maintenance mode. Freeze fat levels?
            {
                float newstrain = strain - newdecayrate * StrainProximityThreshold;
                newstrain = Math.Clamp(newstrain, StrainProximityThreshold, 1f);
                if (newstrain < strain) strain = newstrain;
            }
            if (CurrentSatiety < DietingSatietyThreshold) // if player is not overeating, assume they're on a diet
            {
                strain -= newdecayrate; // strain decreases by full amount
                // lower fat level?
            }
            strain = Math.Clamp(strain, 0f, 1f);
        }

        DateTime lastrecievedsaturation; // put a cooldown on the messages
        bool to25 = false;
        bool to50 = false;
        bool to75 = false;
        bool to100 = false;
        bool tomax = false;

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown, float saturationLossDelay = 10, float nutritionGainMultiplier = 1)
        {
            //check if stomach is full and they overate a little...or a lot
            if (CurrentSatiety == MaxSatiety && ExpandedStomachMeter > 0) // + some threshold from the config?)
            {
                // reset fat to be lost
                fatToBeLostToHunger = 0;
                currentlyFendingOffHungerWithFatLoss = false;
            }
            //update last time player ate
            float percentfull = ExpandedStomachMeter / StomachSize;
            overStuffedThreshold = entity.Api.World.Config.GetFloat("ExpandedStomach.overStuffedThreshold");
            bool shouldDisplayMessages = shouldMessagesDisplay(entity.Api.World.Config.GetBool("ExpandedStomach.immersiveMessages"),
                                                               entity.Api.World.Config.GetBool("ExpandedStomach.bar"));
            if (percentfull <= 0) return;
            if (percentfull >= overStuffedThreshold)
                TriggerStuffedStatus(true);
            if (DateTime.Now > lastrecievedsaturation + MessageCooldown && !OopsWeDied)
            {
                lastrecievedsaturation = DateTime.Now;
                //get stomach sat and size and calculate percentage

                if (shouldDisplayMessages && saturation >= 0)
                {
                    bool messageset = false;
                    var player = entity as EntityPlayer;
                    var serverPlayer = player?.Player as IServerPlayer;
                    //serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                    //    "Stomach Sat/Size: " + ExpandedStomachMeter + "/" + StomachSize,
                    //    EnumChatType.Notification);
                    string message = "";
                    if (percentfull.between(0f, Tier1Threshold) && !to25)
                    {
                        to25 = true;
                        to50 = false;
                        to75 = false;
                        to100 = false;
                        tomax = false;
                        message = Lang.Get("expandedstomach:stomachfilling");
                        messageset = true;
                    }
                    else if (percentfull.between(Tier1Threshold, Tier2Threshold) && !to50)
                    {
                        to25 = false;
                        to50 = true;
                        to75 = false;
                        to100 = false;
                        tomax = false;
                        message = Lang.Get("expandedstomach:stomachover25");
                        messageset = true;
                    }
                    else if (percentfull.between(Tier2Threshold, Tier3Threshold) && !to75)
                    {
                        to25 = false;
                        to50 = false;
                        to75 = true;
                        to100 = false;
                        tomax = false;
                        message = Lang.Get("expandedstomach:stomachover50");
                        messageset = true;
                    }
                    else if (percentfull.between(Tier3Threshold, 1f) && !to100)
                    {
                        to25 = false;
                        to50 = false;
                        to75 = false;
                        to100 = true;
                        tomax = false;
                        message = Lang.Get("expandedstomach:stomachover75");
                        messageset = true;
                    }
                    else if (percentfull >= 1f)
                    {
                        to25 = false;
                        to50 = false;
                        to75 = false;
                        to100 = false;
                        tomax = true;
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

        private void TriggerStuffedStatus(bool isStuffed = false)
        {
            if (stuffedListenerId == 0 && isStuffed)
                stuffedListenerId = entity.World.RegisterGameTickListener(StartCancelOverStuffedTimeDelay, overStuffedTimeDelay, 200); //1 min
            stuffed = isStuffed;
            CalculateMovementSpeedPenalty();
        }

        private void StartCancelOverStuffedTimeDelay(float obj)
        {
            if (ExpandedStomachMeter / StomachSize < overStuffedThreshold)
            {
                stuffed = false;
                CalculateMovementSpeedPenalty();
                entity.World.UnregisterGameTickListener(stuffedListenerId);
                stuffedListenerId = 0;
            }
            else
            {
                entity.World.UnregisterGameTickListener(stuffedListenerId);
                stuffedListenerId = entity.World.RegisterGameTickListener(MonitorStomachForOverStuffed, StuffedRecheckInterval, 0);
            }
        }

        private void MonitorStomachForOverStuffed(float obj)
        {
            if (ExpandedStomachMeter / StomachSize < overStuffedThreshold)
            {
                stuffed = false;
                CalculateMovementSpeedPenalty();
                entity.World.UnregisterGameTickListener(stuffedListenerId);
                stuffedListenerId = 0;
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
            if (stuffedListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(stuffedListenerId);
            }
            if(satietyWatchDogId != 0) 
                entity.World.UnregisterGameTickListener(satietyWatchDogId);
        }

        public override string PropertyName() => "expandedStomach";

        public bool shouldMessagesDisplay(bool messages, bool barhud)
        {
            return (messages && !barhud);
        }
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

    public static string getDifficulty(this EntityAgent EnAgent)
    {
        return EnAgent.Api.World.Config.GetString("ExpandedStomach.difficulty");
    }

    public static string Clean(this string value)
    {
        return value.Replace("\n", "").Replace("\r", "");
    }
}