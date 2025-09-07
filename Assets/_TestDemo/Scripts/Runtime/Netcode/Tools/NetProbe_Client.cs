using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct NetProbe_Client : ISystem
{
    double _next;
#if !UNITY_EDITOR
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.Time.ElapsedTime < _next) return;
        _next = SystemAPI.Time.ElapsedTime + 1.0;

        bool hasConn = !SystemAPI.QueryBuilder().WithAll<NetworkStreamConnection>().Build().IsEmpty;
        bool hasId = SystemAPI.HasSingleton<NetworkId>();
        bool inGame = !SystemAPI.QueryBuilder().WithAll<NetworkId, NetworkStreamInGame>().Build().IsEmpty;
        Debug.Log($"[Client] hasConn={hasConn} hasId={hasId} inGame={inGame}");
    }
#endif
}
