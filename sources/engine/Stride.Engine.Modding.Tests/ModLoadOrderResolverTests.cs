// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModLoadOrderResolverTests
{
    private static ModPackage CreateTestPackage(string id, string version = "1.0.0",
        string apiVersion = "1.0", int loadOrder = 100, List<ModDependency>? dependencies = null)
    {
        var manifest = new ModManifest
        {
            Id = id,
            Name = $"Test Mod {id}",
            Version = version,
            ApiVersion = apiVersion,
            Type = "standard",
            LoadOrder = loadOrder,
            Dependencies = dependencies ?? [],
        };
        return new ModPackage(manifest, $"/fake/mods/{id}");
    }

    // ──────────────────────────────────────────────
    //  Basic topological sort
    // ──────────────────────────────────────────────

    [Fact]
    public void NoDependencies_LoadOrderPreserved()
    {
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-c", loadOrder: 300),
            CreateTestPackage("mod-a", loadOrder: 100),
            CreateTestPackage("mod-b", loadOrder: 200),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(3, result.OrderedMods.Count);
        Assert.Equal("mod-a", result.OrderedMods[0].Manifest.Id);
        Assert.Equal("mod-b", result.OrderedMods[1].Manifest.Id);
        Assert.Equal("mod-c", result.OrderedMods[2].Manifest.Id);
    }

    [Fact]
    public void LinearDependency_ChainRespected()
    {
        // A depends on B, B depends on C → load order: C, B, A
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-b", dependencies:
            [
                new ModDependency { Id = "mod-c", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-c"),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(3, result.OrderedMods.Count);
        var ids = result.OrderedMods.Select(m => m.Manifest.Id).ToList();
        Assert.True(ids.IndexOf("mod-c") < ids.IndexOf("mod-b"), "C should load before B");
        Assert.True(ids.IndexOf("mod-b") < ids.IndexOf("mod-a"), "B should load before A");
    }

    [Fact]
    public void DiamondDependency_ResolvesCorrectly()
    {
        // A depends on B and C, B depends on D, C depends on D
        // D → B, C → A (D first, then B and C in any order, then A)
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", loadOrder: 400, dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "1.0.0" },
                new ModDependency { Id = "mod-c", MinVersion = "1.0.0" },
            ]),
            CreateTestPackage("mod-b", loadOrder: 200, dependencies:
            [
                new ModDependency { Id = "mod-d", MinVersion = "1.0.0" },
            ]),
            CreateTestPackage("mod-c", loadOrder: 300, dependencies:
            [
                new ModDependency { Id = "mod-d", MinVersion = "1.0.0" },
            ]),
            CreateTestPackage("mod-d", loadOrder: 100),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(4, result.OrderedMods.Count);
        var ids = result.OrderedMods.Select(m => m.Manifest.Id).ToList();
        // D must be first
        Assert.Equal("mod-d", ids[0]);
        // A must be last
        Assert.Equal("mod-a", ids[3]);
        // B and C in between (order determined by loadOrder)
        Assert.Equal("mod-b", ids[1]);
        Assert.Equal("mod-c", ids[2]);
    }

    // ──────────────────────────────────────────────
    //  Load order tie-breaking
    // ──────────────────────────────────────────────

    [Fact]
    public void SameDependencyLevel_TieBreaksByLoadOrder()
    {
        // All independent — sorted by loadOrder
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-z", loadOrder: 50),
            CreateTestPackage("mod-m", loadOrder: 300),
            CreateTestPackage("mod-a", loadOrder: 10),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal("mod-a", result.OrderedMods[0].Manifest.Id);
        Assert.Equal("mod-z", result.OrderedMods[1].Manifest.Id);
        Assert.Equal("mod-m", result.OrderedMods[2].Manifest.Id);
    }

    [Fact]
    public void SameLoadOrder_DeterministicById()
    {
        // Same loadOrder — should still be deterministic
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-b", loadOrder: 100),
            CreateTestPackage("mod-a", loadOrder: 100),
            CreateTestPackage("mod-c", loadOrder: 100),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(3, result.OrderedMods.Count);
        // Should be deterministic (SortedSet orders by tuple: (loadOrder, id))
        Assert.Equal("mod-a", result.OrderedMods[0].Manifest.Id);
        Assert.Equal("mod-b", result.OrderedMods[1].Manifest.Id);
        Assert.Equal("mod-c", result.OrderedMods[2].Manifest.Id);
    }

    // ──────────────────────────────────────────────
    //  Missing dependencies
    // ──────────────────────────────────────────────

    [Fact]
    public void MissingRequiredDependency_ModExcludedWithError()
    {
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "nonexistent", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-b"),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal("mod-a", result.Errors[0].ModId);
        Assert.Contains("nonexistent", result.Errors[0].Message);
        Assert.Equal(ModLoadOrderResolver.ModLoadErrorType.MissingDependency, result.Errors[0].ErrorType);
        // mod-b still loads fine
        Assert.Single(result.OrderedMods);
        Assert.Equal("mod-b", result.OrderedMods[0].Manifest.Id);
    }

    [Fact]
    public void MissingOptionalDependency_ModLoadsWithWarning()
    {
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "nonexistent", MinVersion = "1.0.0", Optional = true }
            ]),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Single(result.OrderedMods);
        Assert.Single(result.Warnings);
        Assert.Contains("nonexistent", result.Warnings[0]);
    }

    // ──────────────────────────────────────────────
    //  Version conflicts
    // ──────────────────────────────────────────────

    [Fact]
    public void VersionTooOld_ModExcludedWithError()
    {
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", version: "1.0.0", dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "2.0.0" }
            ]),
            CreateTestPackage("mod-b", version: "1.5.0"),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal("mod-a", result.Errors[0].ModId);
        Assert.Contains("1.5.0", result.Errors[0].Message);
        Assert.Contains("2.0.0", result.Errors[0].Message);
        Assert.Equal(ModLoadOrderResolver.ModLoadErrorType.VersionConflict, result.Errors[0].ErrorType);
    }

    [Fact]
    public void VersionSatisfied_ModLoads()
    {
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-b", version: "1.5.0"),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(2, result.OrderedMods.Count);
    }

    [Fact]
    public void OptionalVersionMismatch_ModLoadsWithWarning()
    {
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "2.0.0", Optional = true }
            ]),
            CreateTestPackage("mod-b", version: "1.0.0"),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(2, result.OrderedMods.Count); // Both load — mod-b is independent
        Assert.Single(result.Warnings);
    }

    // ──────────────────────────────────────────────
    //  Cycle detection
    // ──────────────────────────────────────────────

    [Fact]
    public void TwoNodeCycle_DetectedAndExcluded()
    {
        // A → B → A
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-b", dependencies:
            [
                new ModDependency { Id = "mod-a", MinVersion = "1.0.0" }
            ]),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.False(result.Success);
        Assert.True(result.Errors.Count >= 1);
        Assert.Contains(result.Errors, e =>
            e.ErrorType == ModLoadOrderResolver.ModLoadErrorType.CircularDependency);
        Assert.Contains("Circular dependency", result.Errors[0].Message);
    }

    [Fact]
    public void ThreeNodeCycle_DetectedAndExcluded()
    {
        // A → B → C → A
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-b", dependencies:
            [
                new ModDependency { Id = "mod-c", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-c", dependencies:
            [
                new ModDependency { Id = "mod-a", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-d"), // independent, should still load
        };

        var result = resolver.ResolveLoadOrder(mods);

        // mod-d should still load despite the cycle among a,b,c
        Assert.Contains(result.OrderedMods, m => m.Manifest.Id == "mod-d");
        // Cycle mods should not load
        Assert.DoesNotContain(result.OrderedMods, m => m.Manifest.Id == "mod-a");
        Assert.DoesNotContain(result.OrderedMods, m => m.Manifest.Id == "mod-b");
        Assert.DoesNotContain(result.OrderedMods, m => m.Manifest.Id == "mod-c");
    }

    [Fact]
    public void SelfDependency_DetectedAsCycle()
    {
        // A → A (self-loop)
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "mod-a", MinVersion = "1.0.0" }
            ]),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e =>
            e.ErrorType == ModLoadOrderResolver.ModLoadErrorType.CircularDependency);
    }

    // ──────────────────────────────────────────────
    //  Cascade failures
    // ──────────────────────────────────────────────

    [Fact]
    public void DependencyOfFailedMod_AlsoExcluded()
    {
        // A depends on nonexistent, B depends on A → both fail
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "nonexistent", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-b", dependencies:
            [
                new ModDependency { Id = "mod-a", MinVersion = "1.0.0" }
            ]),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.False(result.Success);
        Assert.Empty(result.OrderedMods);
        Assert.True(result.Errors.Count >= 2);
    }

    // ──────────────────────────────────────────────
    //  Version parsing
    // ──────────────────────────────────────────────

    [Fact]
    public void IsVersionSatisfied_ExactMatch()
    {
        Assert.True(ModLoadOrderResolver.IsVersionSatisfied("1.0.0", "1.0.0"));
    }

    [Fact]
    public void IsVersionSatisfied_HigherPatch()
    {
        Assert.True(ModLoadOrderResolver.IsVersionSatisfied("1.0.5", "1.0.0"));
    }

    [Fact]
    public void IsVersionSatisfied_HigherMinor()
    {
        Assert.True(ModLoadOrderResolver.IsVersionSatisfied("1.2.0", "1.0.0"));
    }

    [Fact]
    public void IsVersionSatisfied_HigherMajor()
    {
        Assert.True(ModLoadOrderResolver.IsVersionSatisfied("2.0.0", "1.0.0"));
    }

    [Fact]
    public void IsVersionSatisfied_TooLow()
    {
        Assert.False(ModLoadOrderResolver.IsVersionSatisfied("1.0.0", "2.0.0"));
    }

    [Fact]
    public void IsVersionSatisfied_TwoPartVersion()
    {
        Assert.True(ModLoadOrderResolver.IsVersionSatisfied("1.0", "1.0.0"));
        Assert.True(ModLoadOrderResolver.IsVersionSatisfied("1.0.0", "1.0"));
    }

    [Fact]
    public void IsVersionSatisfied_PreReleaseStripped()
    {
        Assert.True(ModLoadOrderResolver.IsVersionSatisfied("1.0.0-beta.1", "1.0.0"));
    }

    // ──────────────────────────────────────────────
    //  Duplicate mod IDs
    // ──────────────────────────────────────────────

    [Fact]
    public void DuplicateModIds_FirstWins()
    {
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", version: "1.0.0", loadOrder: 100),
            CreateTestPackage("mod-a", version: "2.0.0", loadOrder: 200),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Single(result.OrderedMods);
        Assert.Equal("1.0.0", result.OrderedMods[0].Manifest.Version);
    }

    // ──────────────────────────────────────────────
    //  Complex graph
    // ──────────────────────────────────────────────

    [Fact]
    public void ComplexGraph_ResolvesCorrectly()
    {
        // A→B, A→C, B→D, C→D, D→E, F independent
        // Expected: E, D, B, C, A, F (roughly — F could be anywhere)
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", loadOrder: 500, dependencies:
            [
                new ModDependency { Id = "mod-b", MinVersion = "1.0.0" },
                new ModDependency { Id = "mod-c", MinVersion = "1.0.0" },
            ]),
            CreateTestPackage("mod-b", loadOrder: 300, dependencies:
            [
                new ModDependency { Id = "mod-d", MinVersion = "1.0.0" },
            ]),
            CreateTestPackage("mod-c", loadOrder: 400, dependencies:
            [
                new ModDependency { Id = "mod-d", MinVersion = "1.0.0" },
            ]),
            CreateTestPackage("mod-d", loadOrder: 200, dependencies:
            [
                new ModDependency { Id = "mod-e", MinVersion = "1.0.0" },
            ]),
            CreateTestPackage("mod-e", loadOrder: 100),
            CreateTestPackage("mod-f", loadOrder: 50),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(6, result.OrderedMods.Count);
        var ids = result.OrderedMods.Select(m => m.Manifest.Id).ToList();

        // E before D
        Assert.True(ids.IndexOf("mod-e") < ids.IndexOf("mod-d"));
        // D before B and C
        Assert.True(ids.IndexOf("mod-d") < ids.IndexOf("mod-b"));
        Assert.True(ids.IndexOf("mod-d") < ids.IndexOf("mod-c"));
        // B and C before A
        Assert.True(ids.IndexOf("mod-b") < ids.IndexOf("mod-a"));
        Assert.True(ids.IndexOf("mod-c") < ids.IndexOf("mod-a"));
        // F can be anywhere (independent)
        Assert.Contains("mod-f", ids);
    }

    [Fact]
    public void EmptyModList_ReturnsEmptyResult()
    {
        var resolver = new ModLoadOrderResolver();
        var result = resolver.ResolveLoadOrder([]);

        Assert.True(result.Success);
        Assert.Empty(result.OrderedMods);
        Assert.Empty(result.Errors);
    }

    // ──────────────────────────────────────────────
    //  Cross-mod version conflicts
    // ──────────────────────────────────────────────

    [Fact]
    public void CrossModVersionConflict_DemandsHigherVersionFails()
    {
        // Mod A requires libX >= 2.0, Mod B requires libX >= 1.0, libX = 1.5
        // → Mod A fails (version conflict), Mod B loads fine
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "lib-x", MinVersion = "2.0.0" }
            ]),
            CreateTestPackage("mod-b", dependencies:
            [
                new ModDependency { Id = "lib-x", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("lib-x", version: "1.5.0"),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.False(result.Success);
        // mod-a fails because lib-x 1.5 < 2.0
        Assert.Contains(result.Errors, e => e.ModId == "mod-a" && e.ErrorType == ModLoadOrderResolver.ModLoadErrorType.VersionConflict);
        // mod-b loads fine (1.5 >= 1.0)
        Assert.Contains(result.OrderedMods, m => m.Manifest.Id == "mod-b");
        // lib-x loads fine (independent)
        Assert.Contains(result.OrderedMods, m => m.Manifest.Id == "lib-x");
        // mod-a does NOT load
        Assert.DoesNotContain(result.OrderedMods, m => m.Manifest.Id == "mod-a");
    }

    [Fact]
    public void CrossModVersionConflict_BothSatisfied_LoadsAll()
    {
        // Both mods require lib-x >= 1.0, lib-x = 2.0 → all good
        var resolver = new ModLoadOrderResolver();
        var mods = new List<ModPackage>
        {
            CreateTestPackage("mod-a", dependencies:
            [
                new ModDependency { Id = "lib-x", MinVersion = "1.0.0" }
            ]),
            CreateTestPackage("mod-b", dependencies:
            [
                new ModDependency { Id = "lib-x", MinVersion = "2.0.0" }
            ]),
            CreateTestPackage("lib-x", version: "2.5.0"),
        };

        var result = resolver.ResolveLoadOrder(mods);

        Assert.True(result.Success);
        Assert.Equal(3, result.OrderedMods.Count);
    }
}
