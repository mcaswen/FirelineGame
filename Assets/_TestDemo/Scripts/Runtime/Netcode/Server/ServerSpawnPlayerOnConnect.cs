using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct ServerSpawnPlayerOnConnect : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerCubeGhostPrefab>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var prefab = SystemAPI.GetSingleton<PlayerCubeGhostPrefab>().Value;

        // 给每个“已进入InGame但还没生成玩家”的连接生成一个玩家
        foreach (var (id, entity) in SystemAPI
                     .Query<RefRO<NetworkId>>()
                     .WithAll<NetworkStreamInGame>()
                     .WithNone<PlayerSpawnedTag>()   // 自定义标记，防重复生成
                     .WithEntityAccess())
        {
            var player = ecb.Instantiate(prefab);

            // 区分两个客户端
            float x = (id.ValueRO.Value % 2 == 0) ? -3f : 3f;
            ecb.SetComponent(player, LocalTransform.FromPositionRotationScale(new float3(x, 0.5f, 0), quaternion.identity, 1));

            // 使该客户端成为 Owner（其本地会变成 Predicted）
            ecb.AddComponent(player, new GhostOwner { NetworkId = id.ValueRO.Value });

            // 标记这条连接已生成玩家
            ecb.AddComponent<PlayerSpawnedTag>(entity);
        }

        ecb.Playback(state.EntityManager);
    }
}

public struct PlayerSpawnedTag : IComponentData { }
