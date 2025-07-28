using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace ExpandedStomach
{
    // Many thanks to Dana for showing me how to do this
    public class ConfigServer : IModConfig
    {
        public const string configName = "expandedstomachServer.json";

        [JsonProperty(Order = 1)]
        public string description => "Determine if the hardcore death mode is enabled.";
        [JsonProperty(Order = 2)]
        public bool hardcoreDeath { get; set; } = false;
        [JsonProperty(Order = 3)]
        public string sslmDescription => "Multiply the amount of stomach saturation lost per second. Minimum of 1.";
        [JsonProperty(Order = 4)]
        public float stomachSatLossMultiplier { get; set; } = 1f;
        [JsonProperty(Order = 5)]
        public string drawbackSeverityDescription => "Multiply the severity of the drawback effect.";
        [JsonProperty(Order = 6)]
        public float drawbackSeverity { get; set; } = 0.4f;
        [JsonProperty(Order = 7)]
        public string fatGainLossDescription => "Base rate muliplier for how fat is gained and lost.";
        [JsonProperty(Order = 8)]
        public float fatGainRate { get; set; } = 1f;
        [JsonProperty(Order = 9)]
        public float fatLossRate { get; set; } = 1f;
        [JsonProperty(Order = 10)]
        public string strainGainLossDescription => "Base rate muliplier for how strain on the stomach is measured.";
        [JsonProperty(Order = 11)]
        public float strainGainRate { get; set; } = 1f;
        [JsonProperty(Order = 12)]
        public float strainLossRate { get; set; } = 1f;
        [JsonProperty(Order = 13)]
        public string difficultyDescription => "Set the difficulty of the mod. Valid values are easy, normal, and hard.";
        [JsonProperty(Order = 14)]
        public string difficulty { get; set; } = "normal";
        [JsonProperty(Order = 15)]
        public string imDescription => "Toggle on/off the immersive messages that appear when using the mod. (Requires a restart)";
        [JsonProperty(Order = 16)]
        public bool immersiveMessages { get; set; } = true;
        [JsonProperty(Order = 17)]
        public bool debugMode { get; set; } = false;

        public ConfigServer(ICoreAPI api, ConfigServer previousConfig = null)
        {
            if (previousConfig == null) return;

            hardcoreDeath = previousConfig.hardcoreDeath;
            stomachSatLossMultiplier = previousConfig.stomachSatLossMultiplier;
            if (stomachSatLossMultiplier < 1f) stomachSatLossMultiplier = 1f;
            drawbackSeverity = previousConfig.drawbackSeverity;
            fatGainRate = previousConfig.fatGainRate;
            fatLossRate = previousConfig.fatLossRate;
            strainGainRate = previousConfig.strainGainRate;
            strainLossRate = previousConfig.strainLossRate;
            difficulty = previousConfig.difficulty;
            immersiveMessages = previousConfig.immersiveMessages;
            debugMode = previousConfig.debugMode;
        }
    }
}
