using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using HarmonyLib;
using Vintagestory.Server;

namespace ExpandedStomach;

public class ExpandedStomachModSystem : ModSystem
{
    public static ConfigServer sConfig;

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("expandedStomach", typeof(EntityBehaviorStomach));
        var harmony = new Harmony("expandedstomach");
        harmony.PatchAll();
        
        Mod.Logger.Notification("Expanded Stomach loaded and patched!");
    }

    public override void StartPre(ICoreAPI api)
    {
        switch (api.Side)
        {
            case EnumAppSide.Server:
                sConfig = ExpandedStomach.ModConfig.ReadConfig<ConfigServer>(api, ConfigServer.configName);
                api.World.Config.SetBool("ExpandedStomach.hardcoreDeath", sConfig.hardcoreDeath);
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
