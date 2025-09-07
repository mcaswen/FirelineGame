using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct StartClientConnectSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
#if UNITY_EDITOR
        state.Enabled = false; // 这里 PlayMode Tools/Bootstrap 负责连接
        return;
#endif
        if (AlreadyConnectedOrConnecting(ref state))
        {
            return;
        }

        var ep = NetworkEndpoint.LoopbackIpv4.WithPort(7979);
        var e = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(e, new NetworkStreamRequestConnect { Endpoint = ep });
    }

    private bool AlreadyConnectedOrConnecting(ref SystemState state)
    {
        if (SystemAPI.HasSingleton<NetworkId>()) return true; // 已连接
        if (!SystemAPI.QueryBuilder().WithAll<NetworkStreamRequestConnect>().Build().IsEmpty) return true; // 已有请求
        if (!SystemAPI.QueryBuilder().WithAll<NetworkStreamConnection>().Build().IsEmpty) return true;     // 连接中
        return false;
    }
}
