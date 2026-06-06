// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModValidatorTests
{
    private static ModManifest CreateValidManifest()
    {
        return new ModManifest
        {
            Id = "com.example.valid-mod",
            Name = "Valid Mod",
            Version = "1.0.0",
            ApiVersion = "1.0",
            Type = "standard"
        };
    }

    [Fact]
    public void ValidManifest_ReturnsNoErrors()
    {
        var manifest = CreateValidManifest();
        var errors = ModValidator.Validate(manifest);
        Assert.Empty(errors);
    }

    [Fact]
    public void MissingId_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Id = "";
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'id'"));
    }

    [Fact]
    public void InvalidIdFormat_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Id = "Not Kebab Case!";
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("kebab-case"));
    }

    [Fact]
    public void ValidKebabCaseIds_Accepted()
    {
        var manifest = CreateValidManifest();

        manifest.Id = "com.example.my-mod";
        Assert.Empty(ModValidator.Validate(manifest));

        manifest.Id = "my-mod";
        Assert.Empty(ModValidator.Validate(manifest));

        manifest.Id = "a.b.c";
        Assert.Empty(ModValidator.Validate(manifest));
    }

    [Fact]
    public void MissingName_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Name = "";
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'name'"));
    }

    [Fact]
    public void MissingVersion_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Version = "";
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'version'"));
    }

    [Fact]
    public void InvalidSemver_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Version = "not-a-version";
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("semver"));
    }

    [Fact]
    public void ValidSemver_Accepted()
    {
        var manifest = CreateValidManifest();

        manifest.Version = "1.0.0";
        Assert.Empty(ModValidator.Validate(manifest));

        manifest.Version = "0.1.0-beta.1";
        Assert.Empty(ModValidator.Validate(manifest));

        manifest.Version = "2.0.0+build.123";
        Assert.Empty(ModValidator.Validate(manifest));
    }

    [Fact]
    public void InvalidType_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Type = "invalid";
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'type'"));
    }

    [Fact]
    public void EmptyType_DefaultsToStandard()
    {
        var manifest = CreateValidManifest();
        manifest.Type = "";
        var errors = ModValidator.Validate(manifest);
        Assert.Equal("standard", manifest.Type);
        Assert.DoesNotContain(errors, e => e.Contains("'type'"));
    }

    [Fact]
    public void ValidTypes_Accepted()
    {
        var manifest = CreateValidManifest();

        manifest.Type = "standard";
        Assert.Empty(ModValidator.Validate(manifest));

        manifest.Type = "patch";
        // Patch mods require dependencies
        manifest.Dependencies = [new ModDependency { Id = "com.example.target", MinVersion = "1.0.0" }];
        Assert.Empty(ModValidator.Validate(manifest));

        manifest.Type = "data";
        manifest.Dependencies = [];
        Assert.Empty(ModValidator.Validate(manifest));
    }

    [Fact]
    public void DependencyMissingId_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Dependencies.Add(new ModDependency { Id = "", MinVersion = "1.0.0" });
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'id'"));
    }

    [Fact]
    public void DependencyMissingMinVersion_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Dependencies.Add(new ModDependency { Id = "com.example.dep", MinVersion = "" });
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'minVersion'"));
    }

    [Fact]
    public void MissingApiVersion_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.ApiVersion = "";
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'apiVersion'"));
    }

    [Fact]
    public void MultipleErrors_ReturnedTogether()
    {
        var manifest = new ModManifest
        {
            Id = "",
            Name = "",
            Version = "",
            ApiVersion = "",
        };
        var errors = ModValidator.Validate(manifest);
        Assert.True(errors.Count >= 3, $"Expected at least 3 errors, got {errors.Count}: {string.Join("; ", errors)}");
    }

    [Fact]
    public void PatchModWithoutDependencies_ReturnsError()
    {
        var manifest = CreateValidManifest();
        manifest.Type = "patch";
        manifest.Dependencies = [];
        var errors = ModValidator.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("'patch'") && e.Contains("dependencies"));
    }

    [Fact]
    public void PatchModWithDependencies_NoError()
    {
        var manifest = CreateValidManifest();
        manifest.Type = "patch";
        manifest.Dependencies =
        [
            new ModDependency { Id = "com.example.target", MinVersion = "1.0.0" }
        ];
        var errors = ModValidator.Validate(manifest);
        Assert.DoesNotContain(errors, e => e.Contains("'patch'"));
    }
}
