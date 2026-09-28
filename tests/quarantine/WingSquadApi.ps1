param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\Release\netstandard2.1\WingCommand.dll'),
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option'
)

# Verify the actual shipped public pilot API without starting Unity or the game.
$ErrorActionPreference = 'Stop'
$probeSource = @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
public static class WingSquadIdentityProbe {
    public static void Run(string assemblyPath, string game) {
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            foreach (string directory in new[] { Path.Combine(game, "NuclearOption_Data", "Managed"), Path.Combine(game, "BepInEx", "core") }) {
                string file = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(file)) return context.LoadFromAssemblyPath(file);
            }
            return null;
        };
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        if (assembly.GetName().Version < new Version(0, 9, 2, 3)) throw new Exception("Wing Command 0.9.2.3 or newer is required.");
        Type api = assembly.GetType("WingCommand.Interop.WingSquad", true);
        if ((int)api.GetProperty("ApiVersion").GetValue(null) != 1) throw new Exception("Unsupported squad API version.");
        foreach (string expected in new[] {
            "CreatePilot:System.Object[](System.Int32)",
            "Portrait:UnityEngine.Sprite(System.String,System.String)",
            "SpawnWing:Aircraft[](Aircraft,FactionHQ,System.Int32,System.Int32,System.Int32,System.String)",
            "SetTarget:System.Boolean(Aircraft[],Aircraft)",
            "ReleaseWing:System.Void(Aircraft[],System.Boolean)",
            "Chatter:System.Void(System.String,System.String,System.String)",
            "SurvivorStatus:System.Int32(PersistentID)",
            "RecoverSurvivor:System.Boolean(PersistentID)"
        }) {
            string name = expected.Substring(0, expected.IndexOf(':'));
            MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new Exception("Missing API method: " + name);
            string actual = name + ":" + method.ReturnType.FullName + "(" +
                string.Join(",", Array.ConvertAll(method.GetParameters(), p => p.ParameterType.FullName)) + ")";
            if (actual != expected) throw new Exception("API signature changed: " + actual);
        }
        MethodInfo generate = api.GetMethod("CreatePilot");
        string first = null;
        for (int seed = 1; seed <= 32; seed++) {
            object[] a = (object[])generate.Invoke(null, new object[] { seed });
            object[] b = (object[])generate.Invoke(null, new object[] { seed });
            if (a.Length != 4 || (int)a[3] < 0 || (int)a[3] > 3) throw new Exception("Invalid pilot wire shape.");
            for (int i = 0; i < 4; i++) if (!a[i].Equals(b[i])) throw new Exception("Seed was not deterministic.");
            if (string.IsNullOrWhiteSpace((string)a[0]) || string.IsNullOrWhiteSpace((string)a[1]) || string.IsNullOrWhiteSpace((string)a[2])) throw new Exception("Empty identity.");
            string identity = a[0] + "|" + a[1];
            if (seed == 1) first = identity;
            else if (seed == 32 && first == identity) throw new Exception("Different seeds did not vary identities.");
        }
        System.Console.WriteLine("WingSquad public pilot generator: 32 deterministic seeds and API shape verified.");
    }
}
'@
Add-Type -TypeDefinition $probeSource
[WingSquadIdentityProbe]::Run($AssemblyPath, $GameDirectory)
