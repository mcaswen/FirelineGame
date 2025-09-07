using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct StartServerListenSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
#if UNITY_EDITOR
        // 编辑器里由 PlayMode Tools 负责监听，避免重复
        state.Enabled = false;
        return;
#else
        if (!(HasArg("-server") || HasArg("-serverui"))) { state.Enabled = false; return; }
#endif
        if (!SystemAPI.QueryBuilder().WithAll<NetworkStreamRequestListen>().Build().IsEmpty)
        {
            state.Enabled = false;
            return;
        }

        var ep = NetworkEndpoint.AnyIpv4.WithPort(7979);
        var e  = state.EntityManager.CreateEntity();
        state.EntityManager.SetName(e, "ServerListenRequest (Custom)");
        state.EntityManager.AddComponentData(e, new NetworkStreamRequestListen { Endpoint = ep });
    }

    static bool HasArg(string flag)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
