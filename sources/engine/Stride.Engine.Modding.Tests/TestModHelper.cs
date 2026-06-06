// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.Reflection;
using Stride.Core;
using Stride.Engine.Modding;

namespace Stride.Engine.Modding.Tests;

/// <summary>
/// Helper for integration tests that load test mods through ModHost.
/// Copies pre-built test mod DLLs + mod.json into a temporary directory
/// so ModHost can discover and load them via ALC isolation.
/// </summary>
public static class TestModHelper
{
    /// <summary>
    /// Finds the solution root by walking up from the test assembly location
    /// until we find the tests/mods/ directory.
    /// </summary>
    private static string? _solutionRoot;

    public static string SolutionRoot
    {
        get
        {
            if (_solutionRoot != null) return _solutionRoot;

            // Walk up from the test assembly location
            var dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "tests", "mods")))
                {
                    _solutionRoot = dir;
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }

            // Fallback: try the known path
            _solutionRoot = @"D:\Modulus-Game-Engine";
            return _solutionRoot;
        }
    }

    /// <summary>
    /// Creates a temporary mod directory with mod.json and the compiled DLLs
    /// from the specified test mod project.
    /// </summary>
    /// <param name="testModDirName">Subdirectory name under tests/mods/ (e.g. "test-component-adder")</param>
    /// <returns>Path to the temporary mod directory ready for ModHost.LoadMod().</returns>
    public static string CreateTempModDirectory(string testModDirName)
    {
        var sourceModDir = Path.Combine(SolutionRoot, "tests", "mods", testModDirName);
        if (!Directory.Exists(sourceModDir))
            throw new DirectoryNotFoundException($"Test mod source not found: {sourceModDir}");

        // Create temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), "modulus_test_mods", testModDirName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Copy mod.json
        var modJsonSrc = Path.Combine(sourceModDir, "mod.json");
        if (!File.Exists(modJsonSrc))
            throw new FileNotFoundException($"mod.json not found in: {sourceModDir}");
        File.Copy(modJsonSrc, Path.Combine(tempDir, "mod.json"), overwrite: true);

        // Find and copy DLLs from the build output
        // Try with and without TFM subfolder (test mods set AppendTargetFrameworkToOutputPath=false)
        var buildOutputDir = Path.Combine(sourceModDir, "bin", "Debug", "net10.0");
        if (!Directory.Exists(buildOutputDir))
            buildOutputDir = Path.Combine(sourceModDir, "bin", "Debug");
        if (!Directory.Exists(buildOutputDir))
            throw new DirectoryNotFoundException(
                $"Test mod build output not found: {buildOutputDir}\n" +
                $"Make sure to build the test mod first: dotnet build tests/mods/{testModDirName}/");

        // Copy the main DLL and its dependencies (but not Stride engine DLLs — those resolve through default ALC)
        var assembliesDir = Path.Combine(tempDir, "assemblies");
        Directory.CreateDirectory(assembliesDir);

        foreach (var dll in Directory.GetFiles(buildOutputDir, "*.dll"))
        {
            var fileName = Path.GetFileName(dll);
            // Only copy test mod DLLs and non-Stride dependencies
            // Stride.* and Modulus.* DLLs resolve through the default ALC
            if (fileName.StartsWith("Test", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("test", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(dll, Path.Combine(assembliesDir, fileName), overwrite: true);
            }
        }

        return tempDir;
    }

    /// <summary>
    /// Cleans up a temporary mod directory.
    /// </summary>
    public static void CleanupTempModDirectory(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best effort — temp cleanup may fail on Windows due to file locks
        }
    }

    /// <summary>
    /// Creates a minimal ModHost with a service registry for testing.
    /// No game loop, no scene system — just the mod loading infrastructure.
    /// </summary>
    public static ModHost CreateTestModHost()
    {
        var services = new ServiceRegistry();
        var modHost = new ModHost(services);
        modHost.RegisterService();
        return modHost;
    }

    /// <summary>
    /// Creates a ModHost with a pre-registered service of type T.
    /// </summary>
    public static ModHost CreateTestModHostWithService<T>(T service) where T : class
    {
        var services = new ServiceRegistry();
        services.AddService<T>(service);
        var modHost = new ModHost(services);
        modHost.RegisterService();
        return modHost;
    }
}
