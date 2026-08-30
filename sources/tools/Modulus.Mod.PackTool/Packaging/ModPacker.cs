// Creates .modpkg ZIP archives from staged mod directories.

using System.IO.Compression;

namespace Modulus.Mod.PackTool.Packaging;

public static class ModPacker
{
    /// <summary>
    /// Creates a .modpkg ZIP archive from the staged mod directory.
    /// Uses CompressionLevel.Optimal (matching ModPackageManager.CreatePackage convention).
    /// </summary>
    /// <param name="sourceDir">Path to the staged mod directory.</param>
    /// <param name="outputPath">Path where the .modpkg file will be created.</param>
    public static void Pack(string sourceDir, string outputPath)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir != null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Delete existing output if present
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        // Create ZIP archive
        ZipFile.CreateFromDirectory(sourceDir, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        var size = new FileInfo(outputPath).Length;
        var sizeKb = size / 1024.0;
        Console.WriteLine($"[ModPackTool] Created package: {outputPath} ({sizeKb:F1} KB)");
    }
}
