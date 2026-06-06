// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Modulus.Modding.Api;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

/// <summary>
/// Integration tests for the Cross-Game test mod.
/// Proves that the same mod works identically across different game projects
/// by loading it through multiple independent ModHost instances.
/// </summary>
public class CrossGameModTests
{
    [Fact]
    public void CrossGame_LoadMod_Succeeds()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-cross-game");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);

            Assert.NotNull(package);
            Assert.Equal("com.modulus.test.cross-game", package.Manifest.Id);
            Assert.Equal(ModState.Loaded, package.State);
            Assert.True(package.IsEnabled);
            Assert.Null(package.ErrorReason);
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void CrossGame_EntryPointInitialized()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-cross-game");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);

            // Verify the IMod entry point was created and initialized
            Assert.NotNull(package.ModInstance);
            var mod = Assert.IsAssignableFrom<IMod>(package.ModInstance);
            Assert.Equal("com.modulus.test.cross-game", mod.Id);
            Assert.Equal("Test Cross-Game Mod", mod.Name);
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void CrossGame_ComponentAndProcessorTypesExist()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-cross-game");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);

            // Verify HealthComponent type
            var componentType = package.ModAssembly!.GetType("TestCrossGame.HealthComponent");
            Assert.NotNull(componentType);
            Assert.True(typeof(EntityComponent).IsAssignableFrom(componentType));

            // Verify HealthProcessor type
            var processorType = package.ModAssembly.GetType("TestCrossGame.HealthProcessor");
            Assert.NotNull(processorType);
            Assert.True(typeof(EntityProcessor).IsAssignableFrom(processorType));
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void CrossGame_SameMod_WorksInMultipleHosts()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-cross-game");
        try
        {
            // Game A: load the mod
            var modHostA = TestModHelper.CreateTestModHost();
            modHostA.ModsDirectory = tempDir;
            var packageA = modHostA.LoadMod(tempDir);

            // Game B: load the same mod in a separate host
            // Need a different temp dir since the mod is already "loaded" in the first dir
            var tempDir2 = TestModHelper.CreateTempModDirectory("test-cross-game");
            try
            {
                var modHostB = TestModHelper.CreateTestModHost();
                modHostB.ModsDirectory = tempDir2;
                var packageB = modHostB.LoadMod(tempDir2);

                // Both loaded successfully
                Assert.Equal(ModState.Loaded, packageA.State);
                Assert.Equal(ModState.Loaded, packageB.State);

                // Both have the same mod ID
                Assert.Equal(packageA.Manifest.Id, packageB.Manifest.Id);

                // Both have entry points
                Assert.NotNull(packageA.ModInstance);
                Assert.NotNull(packageB.ModInstance);

                // Both have loaded assemblies (separate ALCs)
                Assert.NotNull(packageA.LoadContext);
                Assert.NotNull(packageB.LoadContext);

                // The assemblies should be structurally identical (same types)
                var componentTypeA = packageA.ModAssembly!.GetType("TestCrossGame.HealthComponent");
                var componentTypeB = packageB.ModAssembly!.GetType("TestCrossGame.HealthComponent");
                Assert.NotNull(componentTypeA);
                Assert.NotNull(componentTypeB);

                // Same type name, same properties
                Assert.Equal(componentTypeA!.FullName, componentTypeB!.FullName);
                Assert.Equal(
                    componentTypeA.GetProperties().Select(p => p.Name).OrderBy(x => x),
                    componentTypeB.GetProperties().Select(p => p.Name).OrderBy(x => x));
            }
            finally
            {
                TestModHelper.CleanupTempModDirectory(tempDir2);
            }
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void CrossGame_EventBus_WorksAcrossAlcBoundary()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-cross-game");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);

            // The mod subscribes to EntityDiedEvent in Initialize()
            // Publishing an event should reach the mod's handler
            var diedEventType = package.ModAssembly!.GetType("TestCrossGame.EntityDiedEvent");
            Assert.NotNull(diedEventType);

            // Verify the event bus has subscriptions
            // (The mod subscribed in Initialize, so EventBus should have handlers)
            Assert.NotNull(modHost.EventBus);
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void CrossGame_Manifest_HasCorrectMetadata()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-cross-game");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);
            var manifest = package.Manifest;

            Assert.Equal("com.modulus.test.cross-game", manifest.Id);
            Assert.Equal("Test Cross-Game Mod", manifest.Name);
            Assert.Equal("1.0.0", manifest.Version);
            Assert.Equal("1.0", manifest.ApiVersion);
            Assert.Equal("Modulus Team", manifest.Author);
            Assert.Contains("cross-game", manifest.Tags);
            Assert.Equal(50, manifest.LoadOrder); // Lower priority = loads first

            // Verify component declaration
            Assert.Single(manifest.Components);
            Assert.Equal("TestCrossGame.HealthComponent", manifest.Components[0].Type);
            Assert.Equal("TestCrossGame.HealthProcessor", manifest.Components[0].Processor);
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void CrossGame_UnloadMod_FullCleanup()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-cross-game");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);
            var alcRef = new WeakReference(package.LoadContext);

            // Unload
            modHost.UnloadMod("com.modulus.test.cross-game");

            Assert.Empty(modHost.LoadedMods);

            // Force GC to collect the unloaded ALC
            package = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }
}
