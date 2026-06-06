// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

/// <summary>
/// Integration tests for the Component Adder test mod.
/// Proves that mods can define new EntityComponent types and EntityProcessors
/// that are loaded through ALC isolation.
/// </summary>
public class ComponentAdderModTests
{
    [Fact]
    public void ComponentAdder_LoadMod_Succeeds()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-component-adder");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            // Load the mod
            var package = modHost.LoadMod(tempDir);

            Assert.NotNull(package);
            Assert.Equal("com.modulus.test.component-adder", package.Manifest.Id);
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
    public void ComponentAdder_LoadMod_AssemblyLoaded()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-component-adder");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);

            // Verify the mod assembly was loaded into a separate ALC
            Assert.NotNull(package.LoadContext);
            Assert.NotNull(package.ModAssembly);
            Assert.Contains("TestComponentAdder", package.ModAssembly.GetName().Name);
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void ComponentAdder_LoadMod_ManifestParsed()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-component-adder");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);

            // Verify manifest was parsed correctly
            Assert.Equal("Test Component Adder", package.Manifest.Name);
            Assert.Equal("1.0.0", package.Manifest.Version);
            Assert.Equal("1.0", package.Manifest.ApiVersion);
            Assert.Single(package.Manifest.Components);
            Assert.Equal("TestComponentAdder.RotatingComponent", package.Manifest.Components[0].Type);
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void ComponentAdder_UnloadMod_CleansUp()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-component-adder");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);
            var alcWeakRef = new WeakReference(package.LoadContext);

            // Unload the mod
            modHost.UnloadMod("com.modulus.test.component-adder");

            // Verify it's removed from loaded mods
            Assert.Empty(modHost.LoadedMods);

            // Verify the ALC was unloaded (may need GC)
            package = null;
            for (int i = 0; i < 10; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (!alcWeakRef.IsAlive) break;
            }
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void ComponentAdder_DisableAndEnable_Works()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-component-adder");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);
            Assert.True(package.IsEnabled);

            // Disable
            modHost.DisableMod("com.modulus.test.component-adder");
            Assert.False(package.IsEnabled);
            Assert.Equal(ModState.Disabled, package.State);

            // Re-enable
            modHost.EnableMod("com.modulus.test.component-adder");
            Assert.True(package.IsEnabled);
            Assert.Equal(ModState.Loaded, package.State);
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }

    [Fact]
    public void ComponentAdder_DiscoverMods_FindsMod()
    {
        var modDir = TestModHelper.CreateTempModDirectory("test-component-adder");
        try
        {
            // DiscoverMods scans subdirectories of modsDirectory for mod.json
            // So we need a parent directory containing our mod as a subdirectory
            var parentDir = Path.Combine(Path.GetTempPath(), "modulus_test_discovery", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parentDir);
            var modSubDir = Path.Combine(parentDir, "com.modulus.test.component-adder");
            // Move the temp mod dir into the parent
            Directory.Move(modDir, modSubDir);
            // modDir is now invalid, don't clean it up
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = parentDir;

            var discovered = modHost.DiscoverMods();

            Assert.Single(discovered);
            Assert.Equal("com.modulus.test.component-adder", discovered[0].Manifest.Id);

            // Cleanup
            Directory.Delete(parentDir, recursive: true);
        }
        catch
        {
            TestModHelper.CleanupTempModDirectory(modDir);
            throw;
        }
    }

    [Fact]
    public void ComponentAdder_TypeRegistered_WithSerializer()
    {
        var tempDir = TestModHelper.CreateTempModDirectory("test-component-adder");
        try
        {
            var modHost = TestModHelper.CreateTestModHost();
            modHost.ModsDirectory = tempDir;

            var package = modHost.LoadMod(tempDir);

            // Verify the RotatingComponent type can be found in the loaded assembly
            var componentType = package.ModAssembly!.GetType("TestComponentAdder.RotatingComponent");
            Assert.NotNull(componentType);

            // Verify it's an EntityComponent
            Assert.True(typeof(EntityComponent).IsAssignableFrom(componentType));

            // Verify the processor type exists too
            var processorType = package.ModAssembly.GetType("TestComponentAdder.RotatingProcessor");
            Assert.NotNull(processorType);
            Assert.True(typeof(EntityProcessor).IsAssignableFrom(processorType));
        }
        finally
        {
            TestModHelper.CleanupTempModDirectory(tempDir);
        }
    }
}
