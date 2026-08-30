// Validates mod assemblies: no Stride.*.dll contamination (type identity failure),
// no Modulus.Modding.Api.dll bundled (should resolve from engine).

namespace Modulus.Mod.PackTool.Verification;

public static class ModAssemblyValidator
{
    public static void Validate(string modDir, ModManifestDto manifest, VerifyResult result)
    {
        var modType = ManifestPatterns.ResolveType(manifest);
        if (modType == "data")
            return; // Data mods have no assemblies

        // Check assemblies/ directory and root for DLLs
        var assembliesDir = Path.Combine(modDir, "assemblies");
        var searchDirs = new List<string>();
        if (Directory.Exists(assembliesDir))
            searchDirs.Add(assembliesDir);
        searchDirs.Add(modDir);

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
            {
                var filename = Path.GetFileNameWithoutExtension(dll);

                // Stride.*.dll in assemblies → critical type identity failure
                if (filename.StartsWith("Stride.", StringComparison.OrdinalIgnoreCase))
                {
                    result.Error("ASSEMBLY_STRIDE_CONTAMINATION",
                        $"Found '{Path.GetFileName(dll)}' in assemblies. " +
                        "Stride.*.dll must NOT be bundled in the mod — they cause type identity " +
                        "failures at runtime. The SDK's ExcludeEngineAssembliesFromOutput target " +
                        "should have prevented this. Check your .csproj for missing PrivateAssets=\"all\".");
                }

                // Modulus.Engine*.dll bundled → should resolve from engine
                if (filename.StartsWith("Modulus.Engine", StringComparison.OrdinalIgnoreCase))
                {
                    result.Error("ASSEMBLY_ENGINE_CONTAMINATION",
                        $"Found '{Path.GetFileName(dll)}' in assemblies. " +
                        "Modulus.Engine.dll must NOT be bundled — it should resolve from the game's " +
                        "installation at runtime.");
                }

                // Modulus.Modding.Api.dll bundled → warning (should resolve from engine)
                if (filename.Equals("Modulus.Modding.Api", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warning("ASSEMBLY_API_BUNDLED",
                        $"Found '{Path.GetFileName(dll)}' in assemblies. " +
                        "Modulus.Modding.Api.dll should resolve from the engine at runtime, " +
                        "not be bundled with the mod. This may cause type mismatch errors.");
                }
            }
        }
    }
}
