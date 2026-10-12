# Run with Windows PowerShell (the game's .NET Framework runtime), not pwsh.
param(
    [Parameter(Mandatory = $true)][string]$ManagedPath,
    [Parameter(Mandatory = $true)][string]$ModPath
)
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $ManagedPath '0Harmony.dll')) | Out-Null
Add-Type -ReferencedAssemblies (Join-Path $ManagedPath '0Harmony.dll') -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

public static class ArdubsEnemyIntelligencePatchSmoke {
    public static void Run(string modPath, string managedPath) {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            string name = new AssemblyName(args.Name).Name + ".dll";
            foreach (var root in new[] { managedPath, Path.Combine(managedPath, "UnityModManager") }) {
                string path = Path.Combine(root, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        const string owner = "ArdubsEnemyIntelligence.CompatibilitySmoke";
        var harmony = new Harmony(owner);
        try {
            var mod = Assembly.Load(File.ReadAllBytes(modPath));
            Console.WriteLine("Mod assembly version: " + mod.GetName().Version);
            Console.WriteLine("Harmony version: " + typeof(Harmony).Assembly.GetName().Version);
            harmony.PatchAll(mod);
            int count = 0;
            foreach (var method in Harmony.GetAllPatchedMethods()) {
                var info = Harmony.GetPatchInfo(method);
                if (info != null && info.Owners.Contains(owner)) {
                    count++;
                    Console.WriteLine("Patched: " + method.DeclaringType.FullName + "." + method.Name);
                }
            }
            if (count != 47) throw new Exception("Expected all 47 targeting/body-blocking/destination-preview/spell-approach/touch-delivery/allegiance hooks; got " + count);
            Console.WriteLine("All 47 hooks registered successfully.");
        } finally {
            harmony.UnpatchAll(owner);
            foreach (var method in Harmony.GetAllPatchedMethods()) {
                var info = Harmony.GetPatchInfo(method);
                if (info != null && info.Owners.Contains(owner))
                    throw new Exception("Smoke-test patch cleanup failed.");
            }
            Console.WriteLine("All smoke-test patches removed.");
        }
    }
}
'@
try { [ArdubsEnemyIntelligencePatchSmoke]::Run($ModPath, $ManagedPath) }
catch { [Console]::Error.WriteLine($_.Exception.ToString()); throw }
