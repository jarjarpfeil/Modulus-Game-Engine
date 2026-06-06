// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Result of an API compatibility check between a mod and the engine.
/// </summary>
public enum CompatibilityResult
{
    /// <summary>Mod is fully compatible — load normally.</summary>
    Compatible,

    /// <summary>
    /// Minor/major mismatch but within acceptable range.
    /// Mod loads with a warning. This covers:
    /// - Minor version bumps (mod targets older minor, engine is newer)
    /// - Major version mismatches when <c>allowOutdatedMods</c> is enabled
    /// </summary>
    CompatibleWithWarning,

    /// <summary>Mod is incompatible and must not be loaded.</summary>
    Incompatible,
}

/// <summary>
/// Detailed compatibility report for a mod vs. the engine API version.
/// </summary>
public sealed class CompatibilityReport
{
    /// <summary>The compatibility verdict.</summary>
    public CompatibilityResult Result { get; init; }

    /// <summary>The mod's declared minimum API version.</summary>
    public Version ModApiVersion { get; init; } = new(1, 0);

    /// <summary>The engine's current API version.</summary>
    public Version EngineApiVersion { get; init; } = new(1, 0);

    /// <summary>Human-readable explanation of the compatibility status.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>List of specific issues found (warnings or errors).</summary>
    public List<string> Issues { get; init; } = [];

    /// <summary>Whether the mod should be loaded.</summary>
    public bool IsLoadable => Result != CompatibilityResult.Incompatible;
}

/// <summary>
/// Checks API version compatibility between mods and the engine.
///
/// Implements the "always try, unless explicitly unsafe" rule from the Modulus plan:
///
/// | Scenario                     | Behavior                                      |
/// |------------------------------|-----------------------------------------------|
/// | Exact match                  | Compatible                                    |
/// | Patch bump (1.0.0 → 1.0.1)  | Compatible (bug fixes)                        |
/// | Minor bump (1.0.0 → 1.1.0)  | Compatible (new features, old mods still work) |
/// | Minor downgrade (1.1.0 → 1.0)| Compatible with warning                      |
/// | Major bump (1.x → 2.x)      | Incompatible by default; warning with override |
/// | Major downgrade (2.x → 1.x) | Incompatible by default; warning with override |
/// | Same major, future minor     | Compatible with warning                       |
///
/// The <c>rejectFutureVersions</c> manifest flag forces major mismatches to hard-fail
/// even when <c>allowOutdatedMods</c> is enabled.
/// </summary>
public static class ModCompatibility
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModCompatibility");

    /// <summary>
    /// The engine's current API version. This should be set once during engine initialization
    /// and represents the stable ABI version of <c>Modulus.Modding.Api</c>.
    /// </summary>
    public static Version EngineApiVersion { get; set; } = new(1, 0);

    /// <summary>
    /// Global flag: when true, major version mismatches produce a warning instead of a rejection.
    /// Can be set via CLI flag <c>--allow-outdated-mods</c> or config.
    /// </summary>
    public static bool AllowOutdatedMods { get; set; }

    /// <summary>
    /// Checks whether a mod is compatible with the current engine API version.
    /// </summary>
    /// <param name="modApiVersion">The mod's declared minimum API version (from mod.json apiVersion).</param>
    /// <param name="rejectFutureVersions">
    /// If true, the mod has declared it should NOT be loaded on major API version mismatch
    /// even when <see cref="AllowOutdatedMods"/> is true.
    /// </param>
    /// <returns>A detailed compatibility report.</returns>
    public static CompatibilityReport CheckCompatibility(Version modApiVersion, bool rejectFutureVersions = false)
    {
        var engineVersion = EngineApiVersion;
        var issues = new List<string>();

        // Exact or patch-level match
        if (modApiVersion.Major == engineVersion.Major
            && modApiVersion.Minor == engineVersion.Minor)
        {
            if (modApiVersion.Build != engineVersion.Build && modApiVersion.Build >= 0 && engineVersion.Build >= 0)
            {
                issues.Add($"Patch version difference: mod targets {modApiVersion}, engine is {engineVersion} (no impact).");
            }

            return new CompatibilityReport
            {
                Result = CompatibilityResult.Compatible,
                ModApiVersion = modApiVersion,
                EngineApiVersion = engineVersion,
                Message = "Fully compatible.",
                Issues = issues,
            };
        }

        // Same major, minor version bump (engine is newer)
        if (modApiVersion.Major == engineVersion.Major
            && modApiVersion.Minor < engineVersion.Minor)
        {
            issues.Add($"Mod targets API {modApiVersion}, engine is {engineVersion}. Minor version is newer — mod should still work.");

            return new CompatibilityReport
            {
                Result = CompatibilityResult.Compatible,
                ModApiVersion = modApiVersion,
                EngineApiVersion = engineVersion,
                Message = "Compatible — engine has newer minor version. Mod should still work.",
                Issues = issues,
            };
        }

        // Same major, minor version downgrade (engine is older)
        if (modApiVersion.Major == engineVersion.Major
            && modApiVersion.Minor > engineVersion.Minor)
        {
            issues.Add($"Mod targets API {modApiVersion} but engine is {engineVersion}. Mod uses newer features that may not exist.");

            return new CompatibilityReport
            {
                Result = CompatibilityResult.CompatibleWithWarning,
                ModApiVersion = modApiVersion,
                EngineApiVersion = engineVersion,
                Message = "Compatible with warning — mod targets a newer minor API version. Some features may be unavailable.",
                Issues = issues,
            };
        }

        // Major version mismatch (different major numbers)
        bool majorMismatch = modApiVersion.Major != engineVersion.Major;
        bool isDowngrade = modApiVersion.Major > engineVersion.Major;

        if (majorMismatch)
        {
            string direction = isDowngrade ? "downgrade" : "upgrade";
            issues.Add($"Major API version {direction}: mod targets {modApiVersion}, engine is {engineVersion}.");

            if (rejectFutureVersions)
            {
                issues.Add("Mod has 'rejectFutureVersions: true' — refusing to load on major mismatch.");

                return new CompatibilityReport
                {
                    Result = CompatibilityResult.Incompatible,
                    ModApiVersion = modApiVersion,
                    EngineApiVersion = engineVersion,
                    Message = $"Incompatible — mod explicitly rejects major version {direction}. Mod targets API {modApiVersion}, engine is {engineVersion}.",
                    Issues = issues,
                };
            }

            if (AllowOutdatedMods)
            {
                issues.Add("--allow-outdated-mods is enabled — loading with warning.");

                return new CompatibilityReport
                {
                    Result = CompatibilityResult.CompatibleWithWarning,
                    ModApiVersion = modApiVersion,
                    EngineApiVersion = engineVersion,
                    Message = $"Loading with warning — major API version {direction}. Mod targets {modApiVersion}, engine is {engineVersion}. Stability not guaranteed.",
                    Issues = issues,
                };
            }

            return new CompatibilityReport
            {
                Result = CompatibilityResult.Incompatible,
                ModApiVersion = modApiVersion,
                EngineApiVersion = engineVersion,
                Message = $"Incompatible — major API version {direction}. Mod targets {modApiVersion}, engine is {engineVersion}. Use --allow-outdated-mods to override.",
                Issues = issues,
            };
        }

        // Should not reach here, but be safe
        return new CompatibilityReport
        {
            Result = CompatibilityResult.Compatible,
            ModApiVersion = modApiVersion,
            EngineApiVersion = engineVersion,
            Message = "Compatible.",
            Issues = issues,
        };
    }

    /// <summary>
    /// Parses the apiVersion string from a mod manifest and checks compatibility.
    /// </summary>
    public static CompatibilityReport CheckCompatibility(string? modApiVersionString, bool rejectFutureVersions = false)
    {
        if (string.IsNullOrWhiteSpace(modApiVersionString))
        {
            return new CompatibilityReport
            {
                Result = CompatibilityResult.Incompatible,
                ModApiVersion = new Version(0, 0),
                EngineApiVersion = EngineApiVersion,
                Message = "Incompatible — mod.json 'apiVersion' is missing or empty.",
                Issues = ["Missing 'apiVersion' field in mod.json."],
            };
        }

        // Normalize: allow "1.0" → "1.0.0"
        var normalized = modApiVersionString.Trim();
        if (Regex.IsMatch(normalized, @"^\d+\.\d+$"))
            normalized += ".0";

        if (!Version.TryParse(normalized, out var modVersion))
        {
            return new CompatibilityReport
            {
                Result = CompatibilityResult.Incompatible,
                ModApiVersion = new Version(0, 0),
                EngineApiVersion = EngineApiVersion,
                Message = $"Incompatible — could not parse apiVersion '{modApiVersionString}'.",
                Issues = [$"Invalid apiVersion format: '{modApiVersionString}'. Expected semver (e.g. '1.0' or '1.0.0')."],
            };
        }

        return CheckCompatibility(modVersion, rejectFutureVersions);
    }
}
