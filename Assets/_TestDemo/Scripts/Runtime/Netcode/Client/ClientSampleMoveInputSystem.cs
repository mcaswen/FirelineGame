using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(GhostInputSystemGroup))] 
public partial struct ClientSampleMoveInputSystem : ISystem
{
    double _next;

    public void OnCreate(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<CommandTarget>(out var target) || target.targetEntity == Entity.Null)
            return;

        // 采样本地输入
        float2 move = new float2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (math.lengthsq(move) > 1f) move = math.normalize(move);

        // 当前服务器Tick
        var tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;

        var buffer = state.EntityManager.GetBuffer<MoveCommand>(target.targetEntity);
        buffer.AddCommandData(new MoveCommand { Tick = tick, Move = move });
#if !UNITY_EDITOR
        if (SystemAPI.Time.ElapsedTime > _next)
        {
            _next = SystemAPI.Time.ElapsedTime + 1.0;
            Debug.Log($"[Client] SEND cmd tick={tick.SerializedData} move={move} bufLen={buffer.Length}");
        }
#endif
    }
}
