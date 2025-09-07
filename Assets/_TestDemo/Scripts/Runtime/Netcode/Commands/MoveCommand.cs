using Unity.NetCode;
using Unity.Mathematics;

public struct MoveCommand : ICommandData
{
    public NetworkTick Tick { get; set; }       
    public float2 Move;                  
}   