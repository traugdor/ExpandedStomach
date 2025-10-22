using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedStomach
{
    internal class EntityBehaviorStomachClient : EntityBehavior // this is the client side. It only exposes things the client NEEDS to know
    {
        public ITreeAttribute StomachAttributes
        {
            get => entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
        }

        public float satietyBeforeEating
        {
            get => StomachAttributes.GetFloat("satietyBeforeEating", 0f);
        }

        public int StomachSize
        {
            get => StomachAttributes.GetInt("stomachSize", 500);
        }

        public float FatMeter
        {
            get => StomachAttributes.GetFloat("fatMeter", 0f);
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
        }

        public float ExpandedStomachCapToday
        {
            get => StomachAttributes.GetFloat("expandedStomachCapToday", 0);
        }

        public float ExpandedStomachCapAverage
        {
            get => StomachAttributes.GetFloat("expandedStomachCapAverage", 0);
        }

        public float SatConsumedToday
        {
            get => StomachAttributes.GetFloat("satConsumedToday", 0);
        }

        public bool OopsWeDied
        {
            get => StomachAttributes.GetBool("OopsWeDied", false);
        }

        public float strain
        {
            get => StomachAttributes.GetFloat("strain", 0);
        }

        public float laststrain
        {
            get => StomachAttributes.GetFloat("laststrain", 0);
        }

        public float averagestrain
        {
            get => StomachAttributes.GetFloat("averagestrain", 0);
        }

        public int days
        {
            get => entity.WatchedAttributes.TryGetInt("days") ?? 0;
        }

        public int dayCountOffset
        {
            get => entity.WatchedAttributes.TryGetInt("dayCountOffset") ?? 0;
        }

        public EntityBehaviorStomachClient(Entity entity) : base(entity)
        {
            
        }

        private List<(float min, float max, string value)> ranges = new List<(float min, float max, string value)>
        {
            (0.0f, 0.1f, "flNormal"),
            (0.10f, 0.2f, "flSOverweight"),
            (0.2f, 0.35f, "flOverweight"),
            (0.35f, 0.75f, "flFat"),
            (0.75f, 1.0f, "flObese")
        };

        public override void GetInfoText(StringBuilder infotext)
        {
            string fatLevelKey = "";

            fatLevelKey = ranges.FirstOrDefault(x => FatMeter >= x.min && FatMeter <= x.max).value;

            infotext.AppendLine(string.Format(Lang.Get("expandedstomach:fatlevel"), Lang.Get("expandedstomach:" + fatLevelKey)));
        }

        public override string PropertyName() => "expandedStomachClient";
    }
}
