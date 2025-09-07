using Unity.Entities;
using UnityEngine;

public class PlayerPrefabRegistry : MonoBehaviour
{
    public GameObject PlayerCubePrefab; 
}

public struct PlayerCubeGhostPrefab : IComponentData
{
    public Entity Value;
}

public class PlayerPrefabRegistryBaker : Baker<PlayerPrefabRegistry>
{
    public override void Bake(PlayerPrefabRegistry authoring)
    {
        var e = GetEntity(TransformUsageFlags.None);
        AddComponent(e, new PlayerCubeGhostPrefab
        {
            Value = GetEntity(authoring.PlayerCubePrefab, TransformUsageFlags.Dynamic)
        });

    }
}