// Main verification orchestrator: runs all validators, performs GUID/URL collision
// detection against the game's asset database (if provided).

namespace Modulus.Mod.PackTool.Verification;

public static class ModVerifier
{
    /// <summary>
    /// Runs all verification checks on the mod directory.
    /// </summary>
    /// <param name="modDir">Path to the staged mod directory.</param>
    /// <param name="gameDbPath">Optional path to the game's data/db directory for collision checking.</param>
    /// <param name="strictVerification">If true, missing gameDbPath is an error instead of warning.</param>
    public static VerifyResult Verify(string modDir, string? gameDbPath, bool strictVerification)
    {
        var result = new VerifyResult();

        if (!Directory.Exists(modDir))
        {
            result.Error("DIR_MISSING", $"Mod directory not found: {modDir}");
            return result;
        }

        // Phase 1: Structure validation (also parses manifest)
        var manifest = ModStructureValidator.Validate(modDir, result);

        if (manifest != null)
        {
            // Phase 2: Manifest field validation
            ModManifestValidator.Validate(manifest, result);

            // Phase 3: Assembly validation
            ModAssemblyValidator.Validate(modDir, manifest, result);

            // Phase 4: Dependency validation
            ModDependencyValidator.Validate(manifest, result);

            // Phase 5: GUID/URL collision detection
            CheckAssetCollisions(modDir, manifest, gameDbPath, strictVerification, result);
        }

        return result;
    }

    /// <summary>
    /// Checks for URL and GUID collisions between mod assets and the game's asset database.
    /// URL collisions are primary (CompositeFileProviderService resolves by URL).
    /// GUID collisions are secondary (rare, usually accidental).
    /// </summary>
    private static void CheckAssetCollisions(
        string modDir, ModManifestDto manifest, string? gameDbPath,
        bool strictVerification, VerifyResult result)
    {
        // Collect mod asset URLs from all platform index files
        var modAssetUrls = CollectAssetUrls(modDir);

        if (modAssetUrls.Count == 0)
            return; // No assets to check

        if (string.IsNullOrWhiteSpace(gameDbPath) || !File.Exists(Path.Combine(gameDbPath, "index")))
        {
            if (strictVerification)
            {
                result.Error("COLLISION_NO_GAME_DB",
                    "Game asset database path not provided (use --game-db-path). " +
                    "Cannot check for asset collisions. Strict verification is enabled.");
            }
            else
            {
                result.Warning("COLLISION_NO_GAME_DB",
                    "Game asset database path not provided — URL/GUID collision check skipped. " +
                    "Use --game-db-path to enable collision detection.");
            }
            return;
        }

        // Load game asset URLs/ObjectIds
        var gameIndexPath = Path.Combine(gameDbPath, "index");
        var gameAssetUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // url -> objectId

        foreach (var (url, objectId) in IndexFileParser.ParseEntries(gameIndexPath))
        {
            gameAssetUrls[url] = objectId;
        }

        if (gameAssetUrls.Count == 0)
            return;

        // Tier 1: URL collision check (primary)
        var explicitOverrides = manifest.ExplicitOverrides
            .Select(o => o.Trim())
            .Where(o => !string.IsNullOrEmpty(o))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (url, objectId) in modAssetUrls)
        {
            if (gameAssetUrls.TryGetValue(url, out var _))
            {
                // URL collision detected
                if (explicitOverrides.Contains(url))
                {
                    result.Info("COLLISION_OVERRIDE_INTENTIONAL",
                        $"Asset '{url}' is intentionally patching core game asset '{url}'.");
                }
                else
                {
                    result.Error("COLLISION_URL_NOT_DECLARED",
                        $"URL collision: mod asset '{url}' overrides game asset '{url}'. " +
                        $"If intentional, add '{url}' to 'explicitOverrides' in mod.json.");
                }
            }
        }

        // Tier 2: GUID collision check (secondary — different URL, same ObjectId)
        var gameObjectIds = gameAssetUrls.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (url, objectId) in modAssetUrls)
        {
            if (gameObjectIds.Contains(objectId))
            {
                // Check if URL also matches (already handled above)
                if (!gameAssetUrls.ContainsKey(url))
                {
                    result.Warning("COLLISION_GUID_DIFFERENT_URL",
                        $"GUID collision: mod asset '{url}' has same ObjectId as a game asset " +
                        "(different URL). This may cause reference conflicts.");
                }
            }
        }

        // Check for explicitOverrides entries that don't match any game asset
        foreach (var overridePath in explicitOverrides)
        {
            if (!gameAssetUrls.ContainsKey(overridePath))
            {
                result.Warning("OVERRIDE_NOT_FOUND",
                    $"explicitOverrides entry '{overridePath}' not found in game asset database.");
            }
        }
    }

    /// <summary>
    /// Collects all asset URLs from all platform index files in the mod directory.
    /// </summary>
    private static Dictionary<string, string> CollectAssetUrls(string modDir)
    {
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var assetsDir = Path.Combine(modDir, "assets");
        if (!Directory.Exists(assetsDir))
            return urls;

        // Look for index files in all platform subdirectories
        foreach (var platformDir in Directory.GetDirectories(assetsDir))
        {
            var indexPath = Path.Combine(platformDir, "index");
            foreach (var (url, objectId) in IndexFileParser.ParseEntries(indexPath))
            {
                urls[url] = objectId; // Last platform wins (they should all be the same)
            }
        }

        // Also check for a flat index file (legacy layout)
        var flatIndexPath = Path.Combine(assetsDir, "index");
        foreach (var (url, objectId) in IndexFileParser.ParseEntries(flatIndexPath))
        {
            urls[url] = objectId;
        }

        return urls;
    }
}
