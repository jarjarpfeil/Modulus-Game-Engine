// Shared regex patterns for mod manifest validation.
// These mirror the regexes in Stride.Engine.Modding.ModValidator.

using System.Text.RegularExpressions;

namespace Modulus.Mod.PackTool;

public static class ManifestPatterns
{
    // Matches ModValidator.KebabCaseRegex: allows dot-separated segments,
    // each segment is lowercase alphanumerics with hyphens.
    public static readonly Regex KebabCase = new(
        @"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)*$",
        RegexOptions.Compiled);

    // 3-part semver with optional pre-release/build suffix (matches ModValidator.SemverRegexStrict)
    public static readonly Regex SemverStrict = new(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.Compiled);

    // 2-or-3-part semver (matches ModValidator.SemverRegex for apiVersion)
    public static readonly Regex Semver = new(
        @"^\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.Compiled);

    public static readonly HashSet<string> ValidTypes = ["standard", "patch", "data"];

    /// <summary>
    /// Resolves the effective mod type, defaulting to "standard" if blank.
    /// </summary>
    public static string ResolveType(ModManifestDto manifest)
    {
        return string.IsNullOrWhiteSpace(manifest.Type) ? "standard" : manifest.Type;
    }
}
