using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedStomach
{
    internal class CommandHandlers
    {
        private ICoreServerAPI serverAPI;
        private ICoreAPI API;
        private ICoreClientAPI clientAPI;
        internal CommandHandlers(ICoreServerAPI serverapi, ICoreClientAPI clientapi, ICoreAPI api)
        {
            serverAPI = serverapi;
            API = api;
            clientAPI = clientapi;
        }

        internal TextCommandResult ESMain(TextCommandCallingArgs args)
        {
            if (args.RawArgs.Length <= 1)
            {
                return TextCommandResult.Error("You must specify a subcommand.");
            }
            return TextCommandResult.Success();
        }

        internal TextCommandResult ESDebug(TextCommandCallingArgs args)
        {
            return TextCommandResult.Error("Please enter a valid subcommand. Type /help es for a list of valid subcommands.");
        }

        internal TextCommandResult PrintConfig(TextCommandCallingArgs args)
        {
            var config = ModConfig.ReadConfig<ConfigServer>(API, ConfigServer.configName);
            string output = "";
            foreach (var prop in config.GetType().GetProperties())
            {
                output += $"{prop.Name}: {prop.GetValue(config)}\n";
            }
            return TextCommandResult.Success(output);
        }

        internal TextCommandResult SetStomachLevel(TextCommandCallingArgs args)
        {
            try
            {
                string playername = args.Parsers[0].IsMissing ? "" : args.Parsers[0].GetValue().ToString();
                float level = args.Parsers[1].IsMissing ? 0f : float.Parse(args.Parsers[1].GetValue().ToString());

                var allplayers = serverAPI.World.AllOnlinePlayers;
                bool playerFound = false;
                IPlayer thePlayer = null;
                foreach (var player in allplayers)
                {
                    if (player.PlayerName == playername)
                    {
                        thePlayer = player;
                        playerFound = true;
                        break;
                    }
                }
                if (!playerFound) return TextCommandResult.Error("Player not found.");
                var stomach = thePlayer.Entity.GetBehavior<EntityBehaviorStomach>();
                stomach.ExpandedStomachMeter = stomach.StomachSize * level;
                return TextCommandResult.Success("Player stomach level set to " +
                    stomach.ExpandedStomachMeter.ToString() + "/" + stomach.StomachSize.ToString());
            }
            catch (Exception e)
            {
                return TextCommandResult.Error("You did not specify a valid level. Must be a float between 0 and 1.");
            }
        }

        internal TextCommandResult SetFatLevel(TextCommandCallingArgs args)
        {
            try
            {
                string playername = args.Parsers[0].IsMissing ? "" : args.Parsers[0].GetValue().ToString();
                float level = args.Parsers[1].IsMissing ? 0f : float.Parse(args.Parsers[1].GetValue().ToString());

                var allplayers = serverAPI.World.AllOnlinePlayers;
                bool playerFound = false;
                IPlayer thePlayer = null;
                foreach (var player in allplayers)
                {
                    if (player.PlayerName == playername)
                    {
                        thePlayer = player;
                        playerFound = true;
                        break;
                    }
                }
                if (!playerFound) return TextCommandResult.Error("Player not found.");
                var stomach = thePlayer.Entity.GetBehavior<EntityBehaviorStomach>();
                stomach.SetFatMeter(level);
                return TextCommandResult.Success("Player fat level set to " + (level * 100).ToString() + "%");
            }
            catch (Exception e)
            {
                return TextCommandResult.Error("You did not specify a valid level. Must be a float between 0 and 1.");
            }
        }

        internal TextCommandResult SetStomachSize(TextCommandCallingArgs args)
        {
            try
            {
                string playername = args.Parsers[0].IsMissing ? "" : args.Parsers[0].GetValue().ToString();
                int size = GameMath.Clamp(args.Parsers[1].IsMissing ? 0 : int.Parse(args.Parsers[1].GetValue().ToString()), 500, 5500);

                var allplayers = serverAPI.World.AllOnlinePlayers;
                bool playerFound = false;
                IPlayer thePlayer = null;
                foreach (var player in allplayers)
                {
                    if (player.PlayerName == playername)
                    {
                        thePlayer = player;
                        playerFound = true;
                        break;
                    }
                }
                if (!playerFound) return TextCommandResult.Error("Player not found.");
                var stomach = thePlayer.Entity.GetBehavior<EntityBehaviorStomach>();
                stomach.StomachSize = size;
                return TextCommandResult.Success("Player Stomach Size set to " + size.ToString());
            }
            catch (Exception e)
            {
                return TextCommandResult.Error("You did not specify a valid level. Must be a integer between 500 and 5500.");
            }
        }

        internal TextCommandResult PrintInfo(TextCommandCallingArgs args)
        {
            string playername = args.Parsers[0].IsMissing ? "" : args.Parsers[0].GetValue().ToString();
            var allplayers = serverAPI.World.AllOnlinePlayers;
            bool playerFound = false;
            IPlayer thePlayer = null;
            foreach (var player in allplayers)
            {
                if (player.PlayerName == playername)
                {
                    thePlayer = player;
                    playerFound = true;
                    break;
                }
            }
            if (!playerFound) return TextCommandResult.Error("Player not found.");
            var stomach = thePlayer.Entity.GetBehavior<EntityBehaviorStomach>();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Stomach Level/Size: " + stomach.ExpandedStomachMeter.ToString() + "/" + stomach.StomachSize.ToString());
            sb.AppendLine("Stomach Cap info: Today's cap: " + stomach.ExpandedStomachCapToday.ToString() + "   Average Cap: " + stomach.ExpandedStomachCapAverage.ToString());
            sb.AppendLine("Fat Level: " + (stomach.FatMeter * 100).ToString() + "%");
            sb.AppendLine("Strain Values: Current: " + stomach.strain.ToString() + "   Average: " + stomach.averagestrain.ToString() + "   Last: " + stomach.laststrain.ToString());
            return TextCommandResult.Success(sb.ToString());
        }

        internal TextCommandResult SetConfig(TextCommandCallingArgs args)
        {
            string key = args.Parsers[0].IsMissing ? "" : args.Parsers[0].GetValue().ToString();
            string value = args.Parsers[1].IsMissing ? "" : args.Parsers[1].GetValue().ToString();
            var config = ModConfig.ReadConfig<ConfigServer>(API, ConfigServer.configName);

            var configProperty = config.GetType().GetProperty(key);

            if (configProperty == null)
            {
                return TextCommandResult.Error($"Invalid config key: {key}");
            } else
            {
                config.GetType().GetProperty(key).SetValue(config, Convert.ChangeType(value, configProperty.PropertyType));
            }

            ModConfig.WriteConfig<ConfigServer>(API, ConfigServer.configName, config);
            ExpandedStomachModSystem.forceOverwriteConfigFromFile();
            return TextCommandResult.Success($"Config {key} set to {value}");
        }
    }
}
