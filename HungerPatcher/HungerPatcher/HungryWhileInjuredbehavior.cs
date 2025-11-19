using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common.Entities;

namespace HungryWhileInjured
{
    public class EntityBehaviorHungryWhileInjured : EntityBehavior
    {

        public EntityBehaviorHungryWhileInjured(Entity entity) : base(entity) 
        {
            // :) using this to attach future things to the mod if needed. idk maybe I don't need it, but it's here anyway.
        }

        public override string PropertyName()
        {
            return "entitybehavior.hungrywhileinjured";
        }
    }
}
