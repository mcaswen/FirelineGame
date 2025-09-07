using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public class CustomBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
#if UNITY_EDITOR
        AutoConnectPort = 7979;     // 编辑器：PlayMode
#else
        AutoConnectPort = 0;       // Player：StartClientConnectSystem
#endif
        if (HasArg("-dedicated")|| HasArg("-serverui"))
        {
            CreateServerWorld(defaultWorldName);
        }
        else
        {
            CreateClientWorld(defaultWorldName);
        }
        
        return true;
    }

    bool HasArg(string flag)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

}