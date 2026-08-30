using System;
using System.IO;
using Stride.Core.Storage;

var bundlePath = args.Length > 0 ? args[0] : @"D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-assets\assets\bundles\default.50bfde97cf23adde754598792d52e4a2.bundle";
var outputPath = args.Length > 1 ? args[1] : @"D:\TestGame\MyGame2\Bin\Windows\Debug\mods\mod-assets\assets\index";

Console.WriteLine($"Reading bundle: {bundlePath}");

// Read header-only description
using var stream = File.OpenRead(bundlePath);
var bundle = BundleOdbBackend.ReadBundleDescription(stream);
Console.WriteLine($"Dependencies: {bundle.Dependencies.Count}");
Console.WriteLine($"IncrementalBundles: {bundle.IncrementalBundles.Count}");
Console.WriteLine($"Objects: {bundle.Objects.Count}");
Console.WriteLine($"Assets (embedded): {bundle.Assets.Count}");
Console.WriteLine("--- Embedded Assets (URL -> OID) ---");
foreach (var asset in bundle.Assets)
    Console.WriteLine($"{asset.Key} {asset.Value}");

// Look for separate content index map files
var bundlesDir = Path.GetDirectoryName(bundlePath)!;
var assetsDir = Path.GetDirectoryName(bundlesDir)!;
Console.WriteLine($"\nSearching for content index files in: {assetsDir}");
foreach (var f in Directory.GetFiles(assetsDir, "*.contentindexmap", SearchOption.TopDirectoryOnly))
    Console.WriteLine($"  Found index: {Path.GetFileName(f)} ({new FileInfo(f).Length} bytes)");
foreach (var f in Directory.GetFiles(bundlesDir, "*.contentindexmap", SearchOption.TopDirectoryOnly))
    Console.WriteLine($"  Found index: {Path.GetFileName(f)} ({new FileInfo(f).Length} bytes)");

// Dump all objects from header
Console.WriteLine($"\n--- Objects ({bundle.Objects.Count}) ---");
foreach (var obj in bundle.Objects)
{
    Console.WriteLine($"{obj.Key} compressed={obj.Value.IsCompressed} start={obj.Value.StartOffset} end={obj.Value.EndOffset} uncomp={obj.Value.SizeNotCompressed} incBundleIdx={obj.Value.IncrementalBundleIndex}");
}

// Write index file (prefer ContentIndexMap, fall back to embedded Assets)
using var writer = new StreamWriter(outputPath);
if (bundle.Assets.Count > 0)
{
    foreach (var asset in bundle.Assets)
        writer.WriteLine($"{asset.Key} {asset.Value}");
    Console.WriteLine($"\nWrote {bundle.Assets.Count} entries to: {outputPath}");
}
else
{
    Console.WriteLine("\nNo embedded assets found. Bundle has no asset index map.");
    Console.WriteLine("The asset-to-object mapping is likely stored in a separate .contentindexmap file or in the ObjectDatabase.");
}
