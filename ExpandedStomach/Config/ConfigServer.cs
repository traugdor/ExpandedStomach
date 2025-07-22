using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace ExpandedStomach
{
    public class ConfigServer : IModConfig
    {
        public const string configName = "expandedstomachServer.json";

        public bool hardcoreDeath { get; set; } = false;

        public ConfigServer(ICoreAPI api, ConfigServer previousConfig = null)
        {
            if (previousConfig == null) return;

            hardcoreDeath = previousConfig.hardcoreDeath;
        }
    }
}
