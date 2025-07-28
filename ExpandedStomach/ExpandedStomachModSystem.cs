using System;
using HarmonyLib;
using Vintagestory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.Server;
using System.Text;

namespace ExpandedStomach;

public class ExpandedStomachModSystem : ModSystem
{
    public static ConfigServer sConfig;
    public static ICoreAPI Api;
    public static ILogger Logger;
    private static bool patched = false;

    private static ICoreAPI       coreapi;
    private static ICoreServerAPI serverapi;
    private static ICoreClientAPI clientapi;

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        Api = api;
        Logger = Mod.Logger;
        coreapi = api;
    }

    public override void StartPre(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("expandedStomach", typeof(EntityBehaviorStomach));
        switch (api.Side)
        {
            case EnumAppSide.Server:
                sConfig = ExpandedStomach.ModConfig.ReadConfig<ConfigServer>(api, ConfigServer.configName);
                api.World.Config.SetBool("ExpandedStomach.hardcoreDeath", sConfig.hardcoreDeath);
                api.World.Config.SetFloat("ExpandedStomach.stomachSatLossMultiplier", sConfig.stomachSatLossMultiplier);
                api.World.Config.SetFloat("ExpandedStomach.drawbackSeverity", sConfig.drawbackSeverity);
                api.World.Config.SetFloat("ExpandedStomach.strainGainRate", sConfig.strainGainRate);
                api.World.Config.SetFloat("ExpandedStomach.strainLossRate", sConfig.strainLossRate);
                api.World.Config.SetFloat("ExpandedStomach.fatGainRate", sConfig.fatGainRate);
                api.World.Config.SetFloat("ExpandedStomach.fatLossRate", sConfig.fatLossRate);
                api.World.Config.SetString("ExpandedStomach.difficulty", sConfig.difficulty);
                api.World.Config.SetBool("ExpandedStomach.immersiveMessages", sConfig.immersiveMessages);
                api.World.Config.SetBool("ExpandedStomach.debugMode", sConfig.debugMode);
                break;
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.PlayerNowPlaying += (IServerPlayer player) =>
        {
            var entity = player.Entity;
            if (entity != null && entity.GetBehavior<EntityBehaviorStomach>() == null)
            {
                entity.AddBehavior(new EntityBehaviorStomach(entity));
            }
        };
        Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("expandedstomach:hello"));
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        if (!patched)
        {
            var harmony = new Harmony("expandedstomach");
            harmony.PatchAll();
            patched = true;
        }
        serverapi = api;
        RegisterCommands(api);
        Mod.Logger.Notification("Expanded Stomach loaded and patched!");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientapi = api;
        Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("expandedstomach:hello"));
    }

    private void OnPlayerNowPlaying(IServerPlayer thePlayer)
    {
        var entity = thePlayer.Entity;
        if(entity == null) return;
    }

    public void RegisterCommands(ICoreServerAPI api)
    {
        CommandHandlers ch = new CommandHandlers(api, clientapi, coreapi);
        string[] codes = Vintagestory.API.Server.Privilege.AllCodes();
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands
            .Create("ES")
            .RequiresPrivilege(Privilege.root) //only run if you are server OP
            .RequiresPlayer()
            .WithDescription("Expanded Stomach root command. Use `/help es` for more information")
            .HandleWith(ch.ESMain)
            .BeginSubCommand("debug")
                .WithDescription("Debug commands for Expanded Stomach mod.")
                .HandleWith(ch.ESDebug)
                .BeginSubCommand("printInfo")
                    .WithDescription("Prints info about a player's stomach to the console.")
                    .WithArgs(new ICommandArgumentParser[] { parsers.OptionalWord("player") })
                    .HandleWith(ch.PrintInfo)
                .EndSubCommand()
                .BeginSubCommand("setFatLevel")
                    .WithDescription("Sets the fat level of a player to a percentage of its maximum.")
                    .WithArgs(new ICommandArgumentParser[] { parsers.OptionalWord("player"), parsers.OptionalFloat("level") })
                    .HandleWith(ch.SetFatLevel)
                .EndSubCommand()
                .BeginSubCommand("setStomachLevel")
                    .WithDescription("Sets the stomach level of a player to a percentage of its maximum.")
                    .WithArgs(new ICommandArgumentParser[]{ parsers.OptionalWord("player"),parsers.OptionalFloat("level")})
                    .HandleWith(ch.SetStomachLevel)
                .EndSubCommand()
                .BeginSubCommand("setStomachSize")
                    .WithDescription("Sets the stomach size of a player (500-5500).")
                    .WithArgs(new ICommandArgumentParser[]{ parsers.OptionalWord("player"),parsers.OptionalInt("size")})
                    .HandleWith(ch.SetStomachSize)
                .EndSubCommand()
                .BeginSubCommand("printConfig")
                    .WithDescription("Prints the config file to the console.")
                    .HandleWith(ch.PrintConfig)
                .EndSubCommand()
                .BeginSubCommand("setConfig")
                    .WithDescription("Sets a config value.")
                    .WithArgs(new ICommandArgumentParser[]{ parsers.OptionalWord("key"),parsers.OptionalWord("value")})
                    .HandleWith(ch.SetConfig)
                .EndSubCommand()
            .EndSubCommand();
    }
}
