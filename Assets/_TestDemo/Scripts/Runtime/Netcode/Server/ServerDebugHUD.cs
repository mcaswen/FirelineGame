using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

public class ServerDebugHUD : MonoBehaviour
{
    World _serverWorld;
    EntityManager _em;
    EntityQuery _qConn, _qInGame, _qCubes;

    void Start()
    {
        // 可选：只有带 -server 才显示 HUD（避免线上误开）
        if (!HasArg("-serverui"))
        {
            enabled = false;
            return;
        }

        // 找到 Server World

        foreach (var w in World.All)
            Debug.Log($"World: {w.Name}, Flags={w.Flags}");


        foreach (var w in World.All)
        {
            if ((w.Flags & WorldFlags.Game) != 0 && w.Name.Contains("Default"))
            {
                _serverWorld = w;
                break;
            }
        }

        if (_serverWorld == null)
        {
            enabled = false;
            Debug.LogWarning("[ServerDebugHUD] No Server World found!");
            return;
        }

        _em = _serverWorld.EntityManager;

        _qConn   = _em.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
        _qInGame = _em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
        _qCubes  = _em.CreateEntityQuery(ComponentType.ReadOnly<CubeTag>(), ComponentType.ReadOnly<LocalTransform>());

        Debug.Log("[ServerDebugHUD] HUD started on Server World: " + _serverWorld.Name);
    }

    void OnGUI()
    {
        if (_serverWorld == null) return;

        int conns  = _qConn.CalculateEntityCount();
        int inGame = _qInGame.CalculateEntityCount();

        GUILayout.BeginArea(new Rect(10, 10, 520, 400), GUI.skin.box);
        GUILayout.Label($"[ServerHUD] World={_serverWorld.Name}");
        GUILayout.Label($"Connections={conns}   InGame={inGame}");

        using (var cubes = _qCubes.ToEntityArray(Allocator.Temp))
        using (var transforms = _qCubes.ToComponentDataArray<LocalTransform>(Allocator.Temp))
        {
            for (int i = 0; i < cubes.Length; i++)
            {
                var p = transforms[i].Position;
                GUILayout.Label($"Cube[{i}] pos=({p.x:F2}, {p.y:F2}, {p.z:F2})");
            }
        }
        GUILayout.EndArea();
    }

    static bool HasArg(string flag)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
