// Validates mod directory structure: mod.json exists, required directories/files
// present for each mod type, assets directory integrity, explicitOverrides check.

namespace Modulus.Mod.PackTool.Verification;

public static class ModStructureValidator
{
    public static ModManifestDto? Validate(string modDir, VerifyResult result)
    {
        // mod.json must exist
        var manifestPath = Path.Combine(modDir, "mod.json");
        if (!File.Exists(manifestPath))
        {
            result.Error("STRUCTURE_MANIFEST_MISSING", $"mod.json not found in: {modDir}");
            return null;
        }

        // Parse manifest
        ModManifestDto? manifest;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            manifest = ModManifestDto.FromStream(stream);
        }
        catch (Exception ex)
        {
            result.Error("STRUCTURE_MANIFEST_PARSE", $"Failed to parse mod.json: {ex.Message}");
            return null;
        }

        var modType = ManifestPatterns.ResolveType(manifest);

        // Standard/patch mods require at least one assembly
        if (modType is "standard" or "patch")
        {
            var assembliesDir = Path.Combine(modDir, "assemblies");
            var hasDll = false;

            if (Directory.Exists(assembliesDir))
            {
                hasDll = Directory.GetFiles(assembliesDir, "*.dll").Length > 0;
            }

            if (!hasDll)
            {
                // Also check root for single-DLL mods
                hasDll = Directory.GetFiles(modDir, "*.dll").Length > 0;
            }

            if (!hasDll)
            {
                result.Error("STRUCTURE_NO_ASSEMBLIES",
                    $"Standard/patch mod '{manifest.Id}' has no assemblies. " +
                    "Place at least one .dll in assemblies/ directory.");
            }
        }

        // If assets directory exists, warn about missing asset-guids.json
        var assetsDir = Path.Combine(modDir, "assets");
        var hasAssets = Directory.Exists(assetsDir);

        if (hasAssets)
        {
            var guidJsonPath = Path.Combine(modDir, "asset-guids.json");
            if (!File.Exists(guidJsonPath))
            {
                result.Warning("STRUCTURE_NO_GUID_JSON",
                    "assets/ directory exists but asset-guids.json is missing. " +
                    "Asset URLs may not resolve at runtime. Run 'gen-guids' to generate it.");
            }

            // Check for platform subdirectories
            var platformDirs = Directory.GetDirectories(assetsDir)
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => n!)
                .ToList();

            if (platformDirs.Count == 0)
            {
                result.Warning("STRUCTURE_NO_PLATFORM_DIRS",
                    "assets/ directory has no platform subdirectories. " +
                    "Expected: assets/windows-vulkan/, assets/windows-dx11/, assets/windows-dx12/");
            }
        }

        // Check for explicitOverrides entries — warn if they reference non-existent game assets
        // (This is informational only — we can't verify against the game DB here)
        foreach (var overridePath in manifest.ExplicitOverrides)
        {
            if (string.IsNullOrWhiteSpace(overridePath))
            {
                result.Warning("STRUCTURE_OVERRIDE_EMPTY",
                    "mod.json: explicitOverrides contains an empty entry.");
            }
        }

        return manifest;
    }
}
