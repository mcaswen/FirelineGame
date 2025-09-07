using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SetCommandTargetOnSpawn : ISystem
{
    public void OnCreate(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        // 只在未设置 CommandTarget 时查找一次
        if (!SystemAPI.TryGetSingletonRW<CommandTarget>(out var target) || target.ValueRO.targetEntity != Entity.Null)
            return;

        // 本机 NetworkId
        if (!SystemAPI.TryGetSingleton<NetworkId>(out var myId)) return;

        foreach (var (owner, entity) in SystemAPI
                     .Query<RefRO<GhostOwner>>()
                     .WithAll<CubeTag>()
                     .WithEntityAccess())
        {
            if (owner.ValueRO.NetworkId == myId.Value)
            {
                target.ValueRW.targetEntity = entity;
                break;
            }
        }
    }
}
