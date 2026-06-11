// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Modulus.Modding.Api;
using Stride.Core;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModSceneManagerTests
{
    [Fact]
    public void RegisterModScenes_ParsesAllBehaviors()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/Menu", Behavior = "menu" },
            new ModSceneDeclaration { Path = "assets/Replace", Behavior = "replace" },
            new ModSceneDeclaration { Path = "assets/Additive", Behavior = "additive" },
            new ModSceneDeclaration { Path = "assets/Background", Behavior = "background" },
            new ModSceneDeclaration { Path = "assets/Default" }, // no behavior
        ]);

        var package = CreatePackage(manifest, "com.example.scenes");

        sceneManager.RegisterModScenes(package);

        var all = sceneManager.GetAllModScenes();
        Assert.Equal(5, all.Count);

        // Verify behaviors
        Assert.Equal(ModSceneLoadBehavior.MenuSelect, all[0].Behavior);
        Assert.Equal(ModSceneLoadBehavior.Replace, all[1].Behavior);
        Assert.Equal(ModSceneLoadBehavior.Additive, all[2].Behavior);
        Assert.Equal(ModSceneLoadBehavior.Background, all[3].Behavior);
        Assert.Equal(ModSceneLoadBehavior.MenuSelect, all[4].Behavior); // default
    }

    [Fact]
    public void GetPlayerSelectableScenes_ReturnsOnlyMenuSelect()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/Normal" },
            new ModSceneDeclaration { Path = "assets/MenuExplicit", Behavior = "menu" },
            new ModSceneDeclaration { Path = "assets/Replace", Behavior = "replace" },
            new ModSceneDeclaration { Path = "assets/Additive", Behavior = "additive" },
        ]);

        var package = CreatePackage(manifest, "com.example.select");
        sceneManager.RegisterModScenes(package);

        var selectable = sceneManager.GetPlayerSelectableScenes();
        Assert.Equal(2, selectable.Count);
        Assert.All(selectable, e => Assert.True(e.IsPlayerSelectable));
        Assert.Contains(selectable, e => e.SceneUrl == "assets/Normal");
        Assert.Contains(selectable, e => e.SceneUrl == "assets/MenuExplicit");

        // Verify total count
        Assert.Equal(4, sceneManager.GetAllModScenes().Count);
    }

    [Fact]
    public void GetModScenes_ReturnsScenesForSpecificMod()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest1 = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/Mod1Scene" }
        ], "mod-one");

        var manifest2 = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/Mod2Scene1" },
            new ModSceneDeclaration { Path = "assets/Mod2Scene2" },
        ], "mod-two");

        sceneManager.RegisterModScenes(CreatePackage(manifest1, "mod-one"));
        sceneManager.RegisterModScenes(CreatePackage(manifest2, "mod-two"));

        Assert.Equal(1, sceneManager.GetModScenes("mod-one").Count);
        Assert.Equal(2, sceneManager.GetModScenes("mod-two").Count);
        Assert.Empty(sceneManager.GetModScenes("nonexistent"));
    }

    [Fact]
    public void FindEntry_FindsSceneByModAndPath()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/Dungeon", Name = "Dungeon", Behavior = "replace" }
        ], "com.example.dungeon-mod");

        sceneManager.RegisterModScenes(CreatePackage(manifest, "com.example.dungeon-mod"));

        var entry = sceneManager.FindEntry("com.example.dungeon-mod", "assets/Dungeon");
        Assert.NotNull(entry);
        Assert.Equal("Dungeon", entry!.DisplayName);
        Assert.Equal(ModSceneLoadBehavior.Replace, entry.Behavior);

        // Not found — wrong mod
        Assert.Null(sceneManager.FindEntry("other-mod", "assets/Dungeon"));

        // Not found — wrong path
        Assert.Null(sceneManager.FindEntry("com.example.dungeon-mod", "assets/Nonexistent"));
    }

    [Fact]
    public void UnregisterModScenes_RemovesEntries()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/Scene1" },
            new ModSceneDeclaration { Path = "assets/Scene2" },
        ], "com.example.to-unload");

        sceneManager.RegisterModScenes(CreatePackage(manifest, "com.example.to-unload"));
        Assert.Equal(2, sceneManager.GetAllModScenes().Count);

        sceneManager.UnregisterModScenes("com.example.to-unload");
        Assert.Empty(sceneManager.GetAllModScenes());
        Assert.Empty(sceneManager.GetModScenes("com.example.to-unload"));
    }

    [Fact]
    public void RegisterModScenes_ReplacesOnReload()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest1 = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/Old" },
        ], "com.example.reload");

        sceneManager.RegisterModScenes(CreatePackage(manifest1, "com.example.reload"));
        Assert.Single(sceneManager.GetModScenes("com.example.reload"));
        Assert.Equal("assets/Old", sceneManager.GetModScenes("com.example.reload")[0].SceneUrl);

        // Reload with different scenes
        var manifest2 = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/New1" },
            new ModSceneDeclaration { Path = "assets/New2", Behavior = "replace" },
        ], "com.example.reload");

        sceneManager.RegisterModScenes(CreatePackage(manifest2, "com.example.reload"));
        Assert.Equal(2, sceneManager.GetModScenes("com.example.reload").Count);
        Assert.Equal("assets/New1", sceneManager.GetModScenes("com.example.reload")[0].SceneUrl);
    }

    [Fact]
    public void TotalEntryCount_TracksAcrossMods()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        Assert.Equal(0, sceneManager.TotalEntryCount);
        Assert.Equal(0, sceneManager.ModSceneCount);

        var manifest = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/A" },
            new ModSceneDeclaration { Path = "assets/B" },
        ], "mod-a");

        sceneManager.RegisterModScenes(CreatePackage(manifest, "mod-a"));
        Assert.Equal(1, sceneManager.ModSceneCount);
        Assert.Equal(2, sceneManager.TotalEntryCount);
    }

    [Fact]
    public void RegisterModScenes_SkipsEmptyPath()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest = CreateTestManifest([
            new ModSceneDeclaration { Path = "" },
            new ModSceneDeclaration { Path = "assets/Valid" },
        ], "com.example.empty-path");

        sceneManager.RegisterModScenes(CreatePackage(manifest, "com.example.empty-path"));

        // Empty path should be skipped, only Valid should register
        Assert.Single(sceneManager.GetAllModScenes());
        Assert.Equal("assets/Valid", sceneManager.GetAllModScenes()[0].SceneUrl);
    }

    [Fact]
    public void RegisterModScenes_NoScenesDoesNothing()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest = new ModManifest
        {
            Id = "com.example.empty",
            Name = "Empty",
            Version = "1.0.0",
            ApiVersion = "1.0",
            // Scenes = default [] — empty
        };

        sceneManager.RegisterModScenes(CreatePackage(manifest, "com.example.empty"));
        Assert.Equal(0, sceneManager.TotalEntryCount);
        Assert.Equal(0, sceneManager.ModSceneCount);
    }

    [Fact]
    public void DisplayName_FallsBackToFileName()
    {
        var modHost = TestModHelper.CreateTestModHost();
        var sceneManager = modHost.SceneManager;

        var manifest = CreateTestManifest([
            new ModSceneDeclaration { Path = "assets/MyCoolLevel", Name = "Cool Level" },
            new ModSceneDeclaration { Path = "assets/NonameLevel" }, // no name
        ], "com.example.names");

        sceneManager.RegisterModScenes(CreatePackage(manifest, "com.example.names"));

        var entries = sceneManager.GetAllModScenes();
        Assert.Equal("Cool Level", entries[0].DisplayName);
        Assert.Equal("NonameLevel", entries[1].DisplayName); // falls back to file name
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static ModManifest CreateTestManifest(List<ModSceneDeclaration> scenes, string? overrideId = null)
    {
        var manifest = new ModManifest
        {
            Id = overrideId ?? "com.example.test",
            Name = "Test Mod",
            Version = "1.0.0",
            ApiVersion = "1.0",
        };
        manifest.Scenes.AddRange(scenes);
        return manifest;
    }

    private static ModPackage CreatePackage(ModManifest manifest, string modId)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "modulus_test_scenes", modId, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return new ModPackage(manifest, tempDir);
    }
}
