using HarmonyLib;
using Vintagestory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace ExpandedStomach;

public class ExpandedStomachModSystem : ModSystem
{
    public static ConfigServer sConfig;
    public static ICoreAPI Api;
    public static ILogger Logger;
    private static bool patched = false;

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("expandedStomach", typeof(EntityBehaviorStomach));
        Api = api;
        Logger = Mod.Logger;
        if (!patched)
        {
            var harmony = new Harmony("expandedstomach");
            harmony.PatchAll();
            patched = true;
        }
        
        Mod.Logger.Notification("Expanded Stomach loaded and patched!");
    }

    public override void StartPre(ICoreAPI api)
    {
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
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("expandedstomach:hello"));
    }

    private void OnPlayerNowPlaying(IServerPlayer thePlayer)
    {
        var entity = thePlayer.Entity;
        if(entity == null) return;


    }


}
