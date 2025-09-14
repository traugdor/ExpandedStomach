using ExpandedStomach.HarmonyPatches;
using ExpandedStomach.Hud;
using HarmonyLib;
using System;
using System.Reflection;
using System.Text;
using Vintagestory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace ExpandedStomach;

public class ExpandedStomachModSystem : ModSystem
{
    public static ConfigServer sConfig;
    public static ICoreAPI Api;
    public static ILogger Logger;
    private static bool patched = false;

    public static ICoreAPI       coreapi;
    public static ICoreServerAPI serverapi;
    public static ICoreClientAPI clientapi;

    public static bool IsHODLoaded { get; private set; }
    public static  bool IsVigorLoaded { get; private set; }
    public HudESBar hudESBar;

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
                api.World.Config.SetBool("ExpandedStomach.bar", sConfig.bar);
                api.World.Config.SetBool("ExpandedStomach.audoHideHungerBar", sConfig.audoHideHungerBar);
                api.World.Config.SetFloat("ExpandedStomach.barVerticalOffset", sConfig.barVerticalOffset);
                break;
        }
    }

    public static void forceOverwriteConfigFromFile()
    {
        sConfig = ExpandedStomach.ModConfig.ReadConfig<ConfigServer>(serverapi, ConfigServer.configName);
        serverapi.World.Config.SetBool("ExpandedStomach.hardcoreDeath", sConfig.hardcoreDeath);
        serverapi.World.Config.SetFloat("ExpandedStomach.stomachSatLossMultiplier", sConfig.stomachSatLossMultiplier);
        serverapi.World.Config.SetFloat("ExpandedStomach.drawbackSeverity", sConfig.drawbackSeverity);
        serverapi.World.Config.SetFloat("ExpandedStomach.strainGainRate", sConfig.strainGainRate);
        serverapi.World.Config.SetFloat("ExpandedStomach.strainLossRate", sConfig.strainLossRate);
        serverapi.World.Config.SetFloat("ExpandedStomach.fatGainRate", sConfig.fatGainRate);
        serverapi.World.Config.SetFloat("ExpandedStomach.fatLossRate", sConfig.fatLossRate);
        serverapi.World.Config.SetString("ExpandedStomach.difficulty", sConfig.difficulty);
        serverapi.World.Config.SetBool("ExpandedStomach.immersiveMessages", sConfig.immersiveMessages);
        serverapi.World.Config.SetBool("ExpandedStomach.debugMode", sConfig.debugMode);
        serverapi.World.Config.SetBool("ExpandedStomach.bar", sConfig.bar);
        serverapi.World.Config.SetBool("ExpandedStomach.audoHideHungerBar", sConfig.audoHideHungerBar);
        serverapi.World.Config.SetFloat("ExpandedStomach.barVerticalOffset", sConfig.barVerticalOffset);
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
        api.Event.PlayerLeave += (IServerPlayer player) => { 
            
        };
        Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("expandedstomach:hello"));
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        if (!patched)
        {
            // Detect Brainfreeze
            if (api.ModLoader.IsModEnabled("brainfreeze"))
            {
                HarmonyPatchesVars.BrainFreezeInstalled = true;
                api.Logger.Notification("Brainfreeze detected. Removing TryEatStopTranspiler...");
                var type = Type.GetType(
                    "BrainFreeze.Code.HarmonyPatches.FrozenInteractions.Consumption.AddTemperaturePenalty, BrainFreeze"
                );
                if (type == null)
                {
                    api.Logger.Error("Could not find AddTemperaturePenalty type in Brainfreeze.");
                    return;
                }

                HarmonyPatchesVars.BrainFreezeMethod = type.GetMethod(
                    "ApplyPenalty",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                );

                var harmonyUnpatcher = new Harmony("expandedstomach.brainfreeze.compatibility.unpatcher");

                MethodInfo target = AccessTools.Method(typeof(CollectibleObject), "tryEatStop");
                if (target == null)
                {
                    api.Logger.Error("Could not find method CollectibleObject.tryEatStop.");
                    return;
                }

                harmonyUnpatcher.Unpatch(target, HarmonyPatchType.Transpiler, "brainfreeze");

                api.Logger.Notification("TryEatStopTranspiler successfully patched.");
            }
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
        IsHODLoaded = api.ModLoader.IsModEnabled("hydrateordiedrate");
        IsVigorLoaded = api.ModLoader.IsModEnabled("vigor");
        if (IsHODLoaded) Mod.Logger.Notification("Hydrate or Die Rate detected. Adjusting bar position.");
        if (IsVigorLoaded) Mod.Logger.Notification("Vigor detected. Adjusting bar position.");
        hudESBar = new HudESBar(api);
        Mod.Logger.Notification("Waking up the client.... " + Lang.Get("expandedstomach:hello"));
    }

    private void OnPlayerNowPlaying(IServerPlayer thePlayer)
    {
        var entity = thePlayer.Entity;
        if(entity == null) return;
        var stomachbehavior = entity.GetBehavior<EntityBehaviorStomach>();
        if (stomachbehavior != null)
        {
            stomachbehavior.CalculateMovementSpeedPenalty();
        }
    }

    public override void Dispose()
    {
        Harmony harmony = new Harmony("expandedstomach");
        harmony.UnpatchAll("expandedstomach");
        patched = false;
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
            .BeginSubCommand("debug")
                .WithDescription("Debug commands for Expanded Stomach mod.")
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
