// Validates mod dependencies: checks that all declared dependencies have valid
// ids and versions, and detects circular dependencies within the mod itself.
// (Full cross-mod dependency resolution happens at runtime via ModLoadOrderResolver.)

namespace Modulus.Mod.PackTool.Verification;

public static class ModDependencyValidator
{
    public static void Validate(ModManifestDto manifest, VerifyResult result)
    {
        var depIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dep in manifest.Dependencies)
        {
            // Check dependency ID format
            if (string.IsNullOrWhiteSpace(dep.Id))
            {
                result.Error("DEP_ID_MISSING", "mod.json: dependency entry missing 'id'.");
                continue;
            }

            if (!ManifestPatterns.KebabCase.IsMatch(dep.Id))
            {
                result.Error("DEP_ID_FORMAT",
                    $"mod.json: dependency id '{dep.Id}' is not valid kebab-case.");
                continue;
            }

            // Check for self-dependency
            if (dep.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Error("DEP_SELF_REFERENCE",
                    $"mod.json: mod '{manifest.Id}' declares a dependency on itself.");
                continue;
            }

            // Check for duplicate dependencies
            if (!depIds.Add(dep.Id))
            {
                result.Warning("DEP_DUPLICATE",
                    $"mod.json: dependency '{dep.Id}' declared more than once.");
            }

            // Check version format (lenient — matches ModLoadOrderResolver's permissive semantics)
            if (string.IsNullOrWhiteSpace(dep.MinVersion))
            {
                result.Error("DEP_VERSION_MISSING",
                    $"mod.json: dependency '{dep.Id}' missing 'minVersion'.");
            }
            else
            {
                // Try to parse as a version string (System.Version strips pre-release suffix)
                var versionStr = dep.MinVersion.Split('-')[0]; // strip pre-release
                if (!Version.TryParse(versionStr, out _))
                {
                    result.Warning("DEP_VERSION_FORMAT",
                        $"mod.json: dependency '{dep.Id}' has unparseable version '{dep.MinVersion}'. " +
                        "Expected format like '1.0.0' or '1.0'.");
                }
            }
        }

        // Note: Circular dependency detection across multiple mods requires all mods to be
        // loaded. At build time, we can only check self-dependency (done above).
        // Full cycle detection is handled at runtime by ModLoadOrderResolver.ResolveLoadOrder().
    }
}
