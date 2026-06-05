// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Text.Json;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModManifestTests
{
    [Fact]
    public void FromJson_ParsesAllFields()
    {
        var json = """
        {
            "id": "com.example.test-mod",
            "name": "Test Mod",
            "version": "2.1.0",
            "apiVersion": "1.0",
            "author": "Test Author",
            "description": "A test mod",
            "entryPoint": "TestMod.Main, TestMod",
            "dependencies": [
                { "id": "com.example.other", "minVersion": "1.0.0" },
                { "id": "com.example.optional", "minVersion": "2.0.0", "optional": true }
            ],
            "rejectFutureVersions": true,
            "components": [
                { "type": "TestMod.MyComponent", "processor": "TestMod.MyProcessor" }
            ],
            "systems": [
                { "type": "TestMod.MySystem", "priority": 50 }
            ],
            "assets": ["models/cube.sdmodel", "textures/wood.png"],
            "shaders": [
                { "name": "CustomPBR", "path": "shaders/custom-pbr.sdbundle" }
            ],
            "loadOrder": 200,
            "tags": ["gameplay", "health"],
            "type": "standard"
        }
        """;

        var manifest = ModManifest.FromJson(json);

        Assert.Equal("com.example.test-mod", manifest.Id);
        Assert.Equal("Test Mod", manifest.Name);
        Assert.Equal("2.1.0", manifest.Version);
        Assert.Equal("1.0", manifest.ApiVersion);
        Assert.Equal("Test Author", manifest.Author);
        Assert.Equal("A test mod", manifest.Description);
        Assert.Equal("TestMod.Main, TestMod", manifest.EntryPoint);
        Assert.Equal(2, manifest.Dependencies.Count);
        Assert.False(manifest.Dependencies[0].Optional);
        Assert.True(manifest.Dependencies[1].Optional);
        Assert.True(manifest.RejectFutureVersions);
        Assert.Single(manifest.Components);
        Assert.Equal("TestMod.MyComponent", manifest.Components[0].Type);
        Assert.Equal("TestMod.MyProcessor", manifest.Components[0].Processor);
        Assert.Single(manifest.Systems);
        Assert.Equal(50, manifest.Systems[0].Priority);
        Assert.Equal(2, manifest.Assets.Count);
        Assert.NotNull(manifest.Shaders);
        Assert.Single(manifest.Shaders);
        Assert.Equal(200, manifest.LoadOrder);
        Assert.Contains("gameplay", manifest.Tags);
        Assert.Equal("standard", manifest.Type);
    }

    [Fact]
    public void FromJson_HandlesMinimalManifest()
    {
        var json = """
        {
            "id": "com.example.minimal",
            "name": "Minimal",
            "version": "1.0.0",
            "apiVersion": "1.0"
        }
        """;

        var manifest = ModManifest.FromJson(json);

        Assert.Equal("com.example.minimal", manifest.Id);
        Assert.Equal("Minimal", manifest.Name);
        Assert.Empty(manifest.Dependencies);
        Assert.Empty(manifest.Components);
        Assert.Empty(manifest.Assets);
        Assert.Equal(100, manifest.LoadOrder); // default
        Assert.Equal("standard", manifest.Type); // default
    }

    [Fact]
    public void FromJson_AllowsTrailingCommas()
    {
        var json = """
        {
            "id": "com.example.trailing",
            "name": "Trailing Commas",
            "version": "1.0.0",
            "apiVersion": "1.0",
            "tags": ["test",],
        }
        """;

        var manifest = ModManifest.FromJson(json);
        Assert.Single(manifest.Tags);
        Assert.Equal("test", manifest.Tags[0]);
    }

    [Fact]
    public void FromJson_AllowsComments()
    {
        var json = """
        {
            // This is a comment
            "id": "com.example.commented",
            "name": "Commented",
            "version": "1.0.0",
            "apiVersion": "1.0"
        }
        """;

        var manifest = ModManifest.FromJson(json);
        Assert.Equal("com.example.commented", manifest.Id);
    }

    [Fact]
    public void FromJson_WithNullResult_ThrowsInvalidDataException()
    {
        // "null" JSON should throw
        Assert.Throws<InvalidDataException>(() => ModManifest.FromJson("null"));
    }
}
