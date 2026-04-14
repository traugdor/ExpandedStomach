using Vintagestory.API.Common.Entities;

namespace HungryWhileInjured
{
    public class EntityBehaviorHungryWhileInjured : EntityBehavior
    {
        public EntityBehaviorHungryWhileInjured(Entity entity) : base(entity)
        {
            // Placeholder — reserved for future per-entity state if needed.
        }

        public override string PropertyName()
        {
            return "entitybehavior.hungrywhileinjured";
        }
    }
}
