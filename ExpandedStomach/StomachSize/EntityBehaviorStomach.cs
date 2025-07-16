using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedStomach
{
    public class EntityBehaviorStomach : EntityBehavior
    {
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
                result = GameMath.Clamp(tryFloat, 0, 1);
                entity.WatchedAttributes.SetFloat("fatMeter", result);
            }
        }

        public int MaxSatiety
        {
            get => entity.WatchedAttributes.TryGetInt("maxSatiety") ?? 1500;
            set
            {
                int tryInt = int.TryParse(value.ToString(), out int result) ? result : 0;
                entity.WatchedAttributes.SetInt("maxSatiety", tryInt);
            }
        }

        private bool _canSprint;
        public bool CanSprint
        {
            get => _canSprint;
            set => _canSprint = value;
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
        const float eatingWindow = 10f;
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
        }

        public void OnRespawn()
        {
            // halve stomach and body fat and recalculate movement penalties
            StomachSize = StomachSize / 2;
            FatMeter = 0.5f * FatMeter;
            CheckIfCanSprint();
            CalculateMovementSpeedPenalty();
            UpdateWalkSpeed();
        }

        private void CheckIfCanSprint()
        {
            // if fat is 50% you can no longer sprint
            if(FatMeter >= 0.5f)
            {
                CanSprint = false;
                NeedsSprintToMove = false;
            }
            // if fat is 95% you can no longer move. You need to sprint to move
            else if(FatMeter >= 0.95f)
            {
                CanSprint = false;
                NeedsSprintToMove = true;
            }
            // if fat is less than 50% you can sprint; Movement penalties will still apply
            else
            {
                CanSprint = true;
                NeedsSprintToMove = false;
            }
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

        public override void OnGameTick(float deltaTime)
        {
            if (!entity.Alive || entity is not EntityPlayer playerEntity) return;
            if (playerEntity.Player?.WorldData?.CurrentGameMode is EnumGameMode.Creative or EnumGameMode.Spectator or EnumGameMode.Guest) return;

            if(float.IsNaN(deltaTime) || deltaTime < 0)
            {
                deltaTime = 0f;
            }

            //get player satiety
            
            

            base.OnGameTick(deltaTime);
        }

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown,
                                                       float saturationLossDelay = 10f, float nutritionGainMultiplier = 1f)
        {
            float maxSatiety = 1500f;
            var nutritionBehavior = entity.GetBehavior<EntityBehaviorHunger>();
            if (nutritionBehavior != null)
            {
                maxSatiety = nutritionBehavior.MaxSaturation;
            }
            float currentSatiety = entity.WatchedAttributes.GetFloat("saturation", 0f);
            //note the last time the player ate
            if (currentSatiety >= maxSatiety)
            {
                lastFullTime = entity.World.ElapsedMilliseconds / 1000f;
            }
        }

        public override string PropertyName() => "expandedStomach";
    }
}
