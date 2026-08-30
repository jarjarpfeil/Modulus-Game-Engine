// Reads the Stride ObjectDatabase index file (plain text format) and emits
// asset-guids.json for runtime ContentIndexMap injection.
//
// Index file format (one entry per line):
//   <virtualUrl> <32-char-hex-objectId>
//   # comment lines start with #
//
// ObjectId is a 128-bit hash (same size as System.Guid). The runtime casts
// Guid -> ObjectId via Unsafe.As, so we just convert the hex bytes to a Guid.

using System.Text.Json;

namespace Modulus.Mod.PackTool.GuidGeneration;

public static class GuidGenerator
{
    /// <summary>
    /// Reads the index file from the given database path and writes asset-guids.json.
    /// </summary>
    /// <param name="dbPath">Path to the directory containing the "index" file.</param>
    /// <param name="outputPath">Where to write asset-guids.json.</param>
    /// <returns>Number of mappings written.</returns>
    public static int Generate(string dbPath, string outputPath)
    {
        var indexPath = Path.Combine(dbPath, "index");
        if (!File.Exists(indexPath))
        {
            Console.Error.WriteLine($"[ModPackTool] Index file not found: {indexPath}");
            return 0;
        }

        var mappings = new List<AssetGuidEntry>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (url, hexHash) in IndexFileParser.ParseEntries(indexPath))
        {
            // Skip duplicates (index file may contain repeated entries)
            if (!seenUrls.Add(url))
                continue;

            // Convert 32-char hex hash to 16 bytes, then to Guid.
            // ObjectId and Guid are both 16-byte structs; Unsafe.As reinterprets
            // the memory directly. new Guid(byte[]) preserves the byte layout.
            var bytes = Convert.FromHexString(hexHash);
            var guid = new Guid(bytes);

            mappings.Add(new AssetGuidEntry
            {
                VirtualPath = url,
                Guid = guid.ToString(),
            });
        }

        // Write asset-guids.json with exact format expected by ModContentManager:
        // { "Mappings": [ { "VirtualPath": "...", "Guid": "..." } ] }
        // (Engine uses default System.Text.Json — case-sensitive, PascalCase property names)
        var manifest = new AssetGuidManifest { Mappings = mappings };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir != null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        File.WriteAllText(outputPath, json);

        Console.WriteLine($"[ModPackTool] Generated {mappings.Count} GUID mappings -> {outputPath}");
        return mappings.Count;
    }

    // DTOs matching the exact JSON format used by ModContentManager
    private sealed class AssetGuidManifest
    {
        public List<AssetGuidEntry> Mappings { get; set; } = [];
    }

    private sealed class AssetGuidEntry
    {
        public string VirtualPath { get; set; } = "";
        public string Guid { get; set; } = "";
    }
}
