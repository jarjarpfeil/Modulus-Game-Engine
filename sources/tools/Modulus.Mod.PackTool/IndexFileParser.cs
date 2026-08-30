// Shared parsing helpers for the Stride ObjectDatabase index file format.
// Index file format (plain text, one entry per line):
//   <virtualUrl> <32-char-hex-objectId>
//   # comment lines start with #

using System.Text.RegularExpressions;

namespace Modulus.Mod.PackTool;

public static class IndexFileParser
{
    private static readonly Regex IndexEntryRegex = new(@"^(.*?)\s+([0-9a-fA-F]{32})\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Parses an index file and yields (url, objectIdHex) tuples.
    /// Skips blank lines and comments.
    /// </summary>
    public static IEnumerable<(string Url, string ObjectId)> ParseEntries(string indexPath)
    {
        if (!File.Exists(indexPath))
            yield break;

        foreach (var line in File.ReadAllLines(indexPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var match = IndexEntryRegex.Match(trimmed);
            if (!match.Success)
                continue;

            yield return (match.Groups[1].Value.Trim(), match.Groups[2].Value);
        }
    }
}
