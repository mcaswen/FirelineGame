using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class CubeAuthoring : MonoBehaviour
{
    [Range(0.1f, 20f)]
    public float moveSpeed = 4f;
}

public struct CubeTag : IComponentData {}
public struct MoveSpeed : IComponentData { public float Value; }

public class CubeAuthoringBaker : Baker<CubeAuthoring>
{
    public override void Bake(CubeAuthoring authoring)
    {
        var e = GetEntity(TransformUsageFlags.Dynamic);

        AddComponent<CubeTag>(e);
        AddComponent(e, new MoveSpeed { Value = authoring.moveSpeed });

        AddBuffer<MoveCommand>(e); //给玩家实体加命令缓冲
    }
}
