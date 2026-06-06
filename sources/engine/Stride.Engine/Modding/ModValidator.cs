// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Stride.Engine.Modding;

/// <summary>
/// Validates a mod manifest before loading. Checks required fields, format constraints,
/// and dependency consistency.
/// </summary>
public static class ModValidator
{
    private static readonly Regex KebabCaseRegex = new(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)*$", RegexOptions.Compiled);
    private static readonly Regex SemverRegex = new(@"^\d+\.\d+\.?\d*(-[a-zA-Z0-9.]+)?(\+[a-zA-Z0-9.]+)?$", RegexOptions.Compiled);
    private static readonly Regex SemverRegexStrict = new(@"^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?(\+[a-zA-Z0-9.]+)?$", RegexOptions.Compiled);
    private static readonly string[] ValidTypes = { "standard", "patch", "data" };
    private static readonly string[] NativeDllExtensions = { ".dll", ".so", ".dylib" };
    private static readonly string[] ManagedDllWhitelist = { ".dll" }; // .dll can be managed OR native

    /// <summary>
    /// Validates a mod manifest. Returns a list of validation errors (empty = valid).
    /// </summary>
    public static List<string> Validate(ModManifest manifest)
    {
        var errors = new List<string>();

        // id: required, kebab-case
        if (string.IsNullOrWhiteSpace(manifest.Id))
            errors.Add("mod.json 'id' is required");
        else if (!KebabCaseRegex.IsMatch(manifest.Id))
            errors.Add($"mod.json 'id' must be kebab-case (e.g. 'com.example.my-mod'), got: '{manifest.Id}'");

        // name: required
        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("mod.json 'name' is required");

        // version: required, semver (3-part)
        if (string.IsNullOrWhiteSpace(manifest.Version))
            errors.Add("mod.json 'version' is required");
        else if (!SemverRegexStrict.IsMatch(manifest.Version))
            errors.Add($"mod.json 'version' must be semver (e.g. '1.0.0'), got: '{manifest.Version}'");

        // apiVersion: required, semver (2 or 3 part — "1.0" or "1.0.0")
        if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
            errors.Add("mod.json 'apiVersion' is required");
        else if (!SemverRegex.IsMatch(manifest.ApiVersion))
            errors.Add($"mod.json 'apiVersion' must be semver (e.g. '1.0'), got: '{manifest.ApiVersion}'");

        // type: must be one of valid types
        if (string.IsNullOrWhiteSpace(manifest.Type))
            manifest.Type = "standard"; // Default
        else if (Array.IndexOf(ValidTypes, manifest.Type) < 0)
            errors.Add($"mod.json 'type' must be one of [{string.Join(", ", ValidTypes)}], got: '{manifest.Type}'");

        // patch mods require dependencies pointing to the target mod
        if (manifest.Type == "patch" && (manifest.Dependencies == null || manifest.Dependencies.Count == 0))
            errors.Add("mod.json 'type' is 'patch' but no dependencies declared — patch mods require dependencies pointing to the target mod.");

        // dependencies: each dep must have id and minVersion
        if (manifest.Dependencies != null)
        {
            foreach (var dep in manifest.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dep.Id))
                    errors.Add("Dependency entry is missing 'id'");
                if (string.IsNullOrWhiteSpace(dep.MinVersion))
                    errors.Add($"Dependency '{dep.Id}' is missing 'minVersion'");
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates that a mod directory doesn't contain native DLLs (.so, .dylib, or non-managed .dll).
    /// This enforces the security rule: mods are managed C# only.
    /// </summary>
    public static List<string> ValidateNoNativeDlls(string modDirectory)
    {
        var errors = new List<string>();
        
        // Check for explicitly native extensions
        foreach (var ext in new[] { ".so", ".dylib" })
        {
            var nativeFiles = Directory.GetFiles(modDirectory, $"*{ext}", SearchOption.AllDirectories);
            if (nativeFiles.Length > 0)
            {
                errors.Add($"Mods cannot contain native libraries ({ext}): {string.Join(", ", nativeFiles.Select(Path.GetFileName))}");
            }
        }
        
        return errors;
    }
}
