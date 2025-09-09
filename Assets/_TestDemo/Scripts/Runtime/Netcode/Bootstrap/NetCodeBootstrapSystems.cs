using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct StartServerListenSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // 7979 端口上监听
        var ep = NetworkEndpoint.AnyIpv4.WithPort(7979);
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new NetworkStreamRequestListen { Endpoint = ep });
    }
}

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct StartClientConnectSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // 连接到本机 7979
        var ep = NetworkEndpoint.LoopbackIpv4.WithPort(7979);
        var e = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(e, new NetworkStreamRequestConnect { Endpoint = ep });
    }
}

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
public partial struct AutoGoInGameSystem : ISystem
{
    [BurstCompile] public void OnCreate(ref SystemState state) { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (id, entity) in SystemAPI
                 .Query<RefRO<NetworkId>>()
                 .WithNone<NetworkStreamInGame>()
                 .WithEntityAccess())
        {
            ecb.AddComponent<NetworkStreamInGame>(entity);
        }

        ecb.Playback(state.EntityManager);
    }
}

