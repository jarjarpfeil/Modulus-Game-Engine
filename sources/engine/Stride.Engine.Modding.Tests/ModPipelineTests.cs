// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Modulus.Mod.PackTool;
using Modulus.Mod.PackTool.GuidGeneration;
using Modulus.Mod.PackTool.Packaging;
using Modulus.Mod.PackTool.Verification;
using Xunit;

namespace Stride.Engine.Modding.Tests;

/// <summary>
/// Tests for the Modulus.Mod.PackTool — verification, GUID generation, and packaging.
/// These tests create temporary mod directories, run the PackTool against them,
/// and assert the expected behavior.
/// </summary>
public class ModPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public ModPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ModulusPackToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// Helper: creates a minimal staged mod directory with mod.json + assemblies.
    /// </summary>
    private string CreateTestModDir(string modId = "com.test.validmod", bool withAssets = false)
    {
        var modDir = Path.Combine(_tempDir, modId);
        Directory.CreateDirectory(modDir);

        // mod.json
        var manifest = $$"""
        {
          "id": "{{modId}}",
          "name": "Test Mod",
          "version": "1.0.0",
          "apiVersion": "1.0",
          "type": "standard",
          "entryPoint": null,
          "explicitOverrides": []
        }
        """;
        File.WriteAllText(Path.Combine(modDir, "mod.json"), manifest);

        // assemblies/
        var assembliesDir = Path.Combine(modDir, "assemblies");
        Directory.CreateDirectory(assembliesDir);
        File.WriteAllBytes(Path.Combine(assembliesDir, "TestMod.dll"), [0x4D, 0x5A, 0x90, 0x00]); // minimal PE header

        // assets/ with a fake index file (if requested)
        if (withAssets)
        {
            var assetsDir = Path.Combine(modDir, "assets", "windows-vulkan");
            Directory.CreateDirectory(assetsDir);
            File.WriteAllLines(Path.Combine(assetsDir, "index"), [
                "assets/TestScene.sdscene abc123def456789012345678901234ab",
            ]);
        }

        return modDir;
    }

    // ──────────────────────────────────────────────────────────────────
    // Verification tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_ValidMod_ReturnsZero()
    {
        var modDir = CreateTestModDir();
        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.False(result.HasErrors);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Verify_MissingManifest_Fails()
    {
        var modDir = Path.Combine(_tempDir, "empty-mod");
        Directory.CreateDirectory(modDir);
        // No mod.json

        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.True(result.HasErrors);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void Verify_StrideDllsInAssemblies_Fails()
    {
        var modDir = CreateTestModDir();
        // Add a Stride.Engine.dll to assemblies/
        File.WriteAllBytes(Path.Combine(modDir, "assemblies", "Stride.Engine.dll"), [0x00]);

        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "ASSEMBLY_STRIDE_CONTAMINATION");
    }

    [Fact]
    public void Verify_ModulusEngineDllInAssemblies_Fails()
    {
        var modDir = CreateTestModDir();
        File.WriteAllBytes(Path.Combine(modDir, "assemblies", "Modulus.Engine.dll"), [0x00]);

        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "ASSEMBLY_ENGINE_CONTAMINATION");
    }

    [Fact]
    public void Verify_InvalidIdNotKebabCase_Fails()
    {
        var modDir = CreateTestModDir(modId: "Invalid ID With Spaces");
        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "MANIFEST_ID_FORMAT");
    }

    [Fact]
    public void Verify_InvalidVersionNotSemver_Fails()
    {
        var modDir = Path.Combine(_tempDir, "badversion");
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(modDir, "assemblies"));
        File.WriteAllBytes(Path.Combine(modDir, "assemblies", "TestMod.dll"), [0x00]);
        File.WriteAllText(Path.Combine(modDir, "mod.json"), """
        {
          "id": "com.test.badversion",
          "name": "Test",
          "version": "not-a-version",
          "apiVersion": "1.0",
          "type": "standard"
        }
        """);

        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "MANIFEST_VERSION_FORMAT");
    }

    [Fact]
    public void Verify_PatchModWithoutDeps_Fails()
    {
        var modDir = Path.Combine(_tempDir, "patchnodeps");
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(modDir, "assemblies"));
        File.WriteAllBytes(Path.Combine(modDir, "assemblies", "TestMod.dll"), [0x00]);
        File.WriteAllText(Path.Combine(modDir, "mod.json"), """
        {
          "id": "com.test.patchnodeps",
          "name": "Test",
          "version": "1.0.0",
          "apiVersion": "1.0",
          "type": "patch"
        }
        """);

        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "MANIFEST_PATCH_NO_DEPS");
    }

    [Fact]
    public void Verify_DataOnlyModNoAssemblies_Passes()
    {
        var modDir = Path.Combine(_tempDir, "datamod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "mod.json"), """
        {
          "id": "com.test.datamod",
          "name": "Data Mod",
          "version": "1.0.0",
          "apiVersion": "1.0",
          "type": "data"
        }
        """);

        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: false);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Verify_StrictWithoutGameDb_Fails()
    {
        var modDir = CreateTestModDir(withAssets: true);
        var result = ModVerifier.Verify(modDir, gameDbPath: null, strictVerification: true);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "COLLISION_NO_GAME_DB");
    }

    // ──────────────────────────────────────────────────────────────────
    // GUID generation tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GenGuids_WithValidIndex_ProducesValidJson()
    {
        var dbDir = Path.Combine(_tempDir, "test-db");
        Directory.CreateDirectory(dbDir);
        File.WriteAllLines(Path.Combine(dbDir, "index"), [
            "assets/Scene.sdscene abc123def456789012345678901234ab",
            "assets/Material.sdmat def456789012345678901234ab123cd",
        ]);

        var outputPath = Path.Combine(_tempDir, "asset-guids.json");
        var count = GuidGenerator.Generate(dbDir, outputPath);

        Assert.Equal(2, count);
        Assert.True(File.Exists(outputPath));

        // Verify the JSON is parseable and has the correct format
        var json = File.ReadAllText(outputPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("Mappings", out var mappings));
        Assert.Equal(2, mappings.GetArrayLength());
    }

    [Fact]
    public void GenGuids_NoIndexFile_ReturnsZero()
    {
        var dbDir = Path.Combine(_tempDir, "empty-db");
        Directory.CreateDirectory(dbDir);
        // No index file

        var outputPath = Path.Combine(_tempDir, "no-guids.json");
        var count = GuidGenerator.Generate(dbDir, outputPath);

        Assert.Equal(0, count);
    }

    [Fact]
    public void GenGuids_DuplicateEntriesInIndex_Deduplicates()
    {
        var dbDir = Path.Combine(_tempDir, "dup-db");
        Directory.CreateDirectory(dbDir);
        File.WriteAllLines(Path.Combine(dbDir, "index"), [
            "assets/Scene.sdscene abc123def456789012345678901234ab",
            "assets/Scene.sdscene abc123def456789012345678901234ab", // duplicate
            "assets/Material.sdmat def456789012345678901234ab123cd",
        ]);

        var outputPath = Path.Combine(_tempDir, "dedup-guids.json");
        var count = GuidGenerator.Generate(dbDir, outputPath);

        Assert.Equal(2, count); // 3 entries, 1 duplicate → 2 unique
    }

    // ──────────────────────────────────────────────────────────────────
    // Collision detection tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_UrlCollisionWithoutExplicitOverride_Fails()
    {
        // Create mod with assets
        var modDir = CreateTestModDir(withAssets: true);

        // Create game DB with a colliding URL
        var gameDbDir = Path.Combine(_tempDir, "game-db");
        Directory.CreateDirectory(gameDbDir);
        File.WriteAllLines(Path.Combine(gameDbDir, "index"), [
            "assets/TestScene.sdscene different ObjectId entirely 0001",
            "assets/Other.sdmat 00000000000000000000000000000001",
        ]);

        var result = ModVerifier.Verify(modDir, gameDbPath: gameDbDir, strictVerification: false);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages,
            m => m.Code == "COLLISION_URL_NOT_DECLARED" &&
                 m.Message.Contains("TestScene"));
    }

    [Fact]
    public void Verify_UrlCollisionWithExplicitOverride_Passes()
    {
        var modDir = Path.Combine(_tempDir, "override-mod");
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(modDir, "assemblies"));
        File.WriteAllBytes(Path.Combine(modDir, "assemblies", "TestMod.dll"), [0x00]);

        // Assets with colliding URL
        var assetsDir = Path.Combine(modDir, "assets", "windows-vulkan");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllLines(Path.Combine(assetsDir, "index"), [
            "assets/TestScene.sdscene abc123def456789012345678901234ab",
        ]);

        // mod.json with explicitOverrides listing the collision URL
        File.WriteAllText(Path.Combine(modDir, "mod.json"), """
        {
          "id": "com.test.override",
          "name": "Override Mod",
          "version": "1.0.0",
          "apiVersion": "1.0",
          "type": "standard",
          "explicitOverrides": ["assets/TestScene.sdscene"]
        }
        """);

        // Game DB with the same URL
        var gameDbDir = Path.Combine(_tempDir, "game-db-override");
        Directory.CreateDirectory(gameDbDir);
        File.WriteAllLines(Path.Combine(gameDbDir, "index"), [
            "assets/TestScene.sdscene 00000000000000000000000000000001",
        ]);

        var result = ModVerifier.Verify(modDir, gameDbPath: gameDbDir, strictVerification: false);
        Assert.False(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "COLLISION_OVERRIDE_INTENTIONAL");
    }

    // ──────────────────────────────────────────────────────────────────
    // Packaging tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Pack_ValidModDir_CreatesZip()
    {
        var modDir = CreateTestModDir();
        var outputPath = Path.Combine(_tempDir, "test.modpkg");

        ModPacker.Pack(modDir, outputPath);

        Assert.True(File.Exists(outputPath));
        // Verify it's a valid ZIP
        using var archive = ZipFile.OpenRead(outputPath);
        Assert.Contains(archive.Entries, e => e.FullName == "mod.json");
    }

    [Fact]
    public void Pack_NonExistentSourceDir_Throws()
    {
        var outputPath = Path.Combine(_tempDir, "should-not-exist.modpkg");
        Assert.Throws<DirectoryNotFoundException>(() => ModPacker.Pack("/nonexistent/path", outputPath));
    }
}
