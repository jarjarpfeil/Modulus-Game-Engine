using System.Text.RegularExpressions;

namespace Modulus.Mod.PackTool.Verification;

public static class ModManifestValidator
{
    public static void Validate(ModManifestDto manifest, VerifyResult result)
    {
        // Id — required, kebab-case
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            result.Error("MANIFEST_ID_MISSING", "mod.json: 'id' field is required.");
        }
        else if (!ManifestPatterns.KebabCase.IsMatch(manifest.Id))
        {
            result.Error("MANIFEST_ID_FORMAT", $"mod.json: 'id' must be kebab-case (e.g., 'com.example.mymod'). Got: '{manifest.Id}'");
        }

        // Name — required
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            result.Error("MANIFEST_NAME_MISSING", "mod.json: 'name' field is required.");
        }

        // Version — required, strict 3-part semver
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            result.Error("MANIFEST_VERSION_MISSING", "mod.json: 'version' field is required.");
        }
        else if (!ManifestPatterns.SemverStrict.IsMatch(manifest.Version))
        {
            result.Error("MANIFEST_VERSION_FORMAT", $"mod.json: 'version' must be valid semver (e.g., '1.0.0'). Got: '{manifest.Version}'");
        }

        // ApiVersion — required, 2-or-3 part semver
        if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
        {
            result.Error("MANIFEST_APIVERSION_MISSING", "mod.json: 'apiVersion' field is required.");
        }
        else if (!ManifestPatterns.Semver.IsMatch(manifest.ApiVersion))
        {
            result.Error("MANIFEST_APIVERSION_FORMAT", $"mod.json: 'apiVersion' must be valid semver (e.g., '1.0'). Got: '{manifest.ApiVersion}'");
        }

        // Type — must be valid
        if (!string.IsNullOrWhiteSpace(manifest.Type) && !ManifestPatterns.ValidTypes.Contains(manifest.Type))
        {
            result.Error("MANIFEST_TYPE_INVALID", $"mod.json: 'type' must be one of: {string.Join(", ", ManifestPatterns.ValidTypes)}. Got: '{manifest.Type}'");
        }

        // Patch mods require at least one dependency
        if (manifest.Type == "patch" && manifest.Dependencies.Count == 0)
        {
            result.Error("MANIFEST_PATCH_NO_DEPS", "mod.json: 'patch' type mods must declare at least one dependency.");
        }

        // Dependencies — each must have non-blank id and minVersion
        foreach (var dep in manifest.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dep.Id))
            {
                result.Error("MANIFEST_DEP_ID_MISSING", "mod.json: dependency entry missing 'id' field.");
            }
            if (string.IsNullOrWhiteSpace(dep.MinVersion))
            {
                result.Error("MANIFEST_DEP_VERSION_MISSING", $"mod.json: dependency '{dep.Id}' missing 'minVersion' field.");
            }
        }

        // EntryPoint — if present, must be "Namespace.Type, Assembly" format
        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            var parts = manifest.EntryPoint.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                result.Warning("MANIFEST_ENTRYPOINT_FORMAT",
                    $"mod.json: 'entryPoint' should be 'Namespace.Type, Assembly' format. Got: '{manifest.EntryPoint}'");
            }
        }
    }
}
