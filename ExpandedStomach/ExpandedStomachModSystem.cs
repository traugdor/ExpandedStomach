using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;

using HarmonyLib;

namespace ExpandedStomach;

public class ExpandedStomachModSystem : ModSystem
{

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
        {
            api.Event.OnEntitySpawn += (entity) =>
            {
                if (entity is EntityPlayer && entity.GetBehavior<EntityBehaviorStomach>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorStomach(entity));
                }
            };
        }
        var harmony = new Harmony("expandedstomach");
        harmony.PatchAll();
        Mod.Logger.Notification("Expanded Stomach loaded and patched!");
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
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
