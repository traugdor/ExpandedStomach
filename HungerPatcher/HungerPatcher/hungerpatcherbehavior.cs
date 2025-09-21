using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common.Entities;

namespace HungerPatcher
{
    public class EntityBehaviorHungerPatcher : EntityBehavior
    {
        public DateTime dt = DateTime.Now;

        public EntityBehaviorHungerPatcher(Entity entity) : base(entity) 
        {
            
        }

        public override string PropertyName()
        {
            return "hungerpatcher";
        }
    }
}
