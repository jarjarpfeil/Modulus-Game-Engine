// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModCompatibilityTests : IDisposable
{
    private readonly Version _savedEngineVersion;
    private readonly bool _savedAllowOutdated;

    public ModCompatibilityTests()
    {
        // Save and restore static state for test isolation
        _savedEngineVersion = ModCompatibility.EngineApiVersion;
        _savedAllowOutdated = ModCompatibility.AllowOutdatedMods;
    }

    public void Dispose()
    {
        ModCompatibility.EngineApiVersion = _savedEngineVersion;
        ModCompatibility.AllowOutdatedMods = _savedAllowOutdated;
    }

    // ─────────────────────────────────────────────────
    //  Exact match
    // ─────────────────────────────────────────────────

    [Fact]
    public void ExactVersionMatch_IsCompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 0));

        Assert.Equal(CompatibilityResult.Compatible, report.Result);
        Assert.True(report.IsLoadable);
        Assert.Contains("Fully compatible", report.Message);
    }

    [Fact]
    public void ExactVersionMatch_ViaString()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility("1.0.0");

        Assert.Equal(CompatibilityResult.Compatible, report.Result);
    }

    // ─────────────────────────────────────────────────
    //  Patch version differences
    // ─────────────────────────────────────────────────

    [Fact]
    public void PatchBump_IsCompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 1);
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 0));

        Assert.Equal(CompatibilityResult.Compatible, report.Result);
    }

    [Fact]
    public void PatchDowngrade_IsCompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 5));

        Assert.Equal(CompatibilityResult.Compatible, report.Result);
    }

    // ─────────────────────────────────────────────────
    //  Minor version differences (same major)
    // ─────────────────────────────────────────────────

    [Fact]
    public void MinorBump_EngineNewer_IsCompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 2, 0);
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 0));

        Assert.Equal(CompatibilityResult.Compatible, report.Result);
        Assert.Contains("newer minor", report.Message);
    }

    [Fact]
    public void MinorDowngrade_EngineOlder_IsCompatibleWithWarning()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility(new Version(1, 2, 0));

        Assert.Equal(CompatibilityResult.CompatibleWithWarning, report.Result);
        Assert.True(report.IsLoadable);
        Assert.Contains("newer minor", report.Message);
    }

    // ─────────────────────────────────────────────────
    //  Major version differences
    // ─────────────────────────────────────────────────

    [Fact]
    public void MajorBump_DefaultPolicy_IsIncompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(2, 0, 0);
        ModCompatibility.AllowOutdatedMods = false;
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 0));

        Assert.Equal(CompatibilityResult.Incompatible, report.Result);
        Assert.False(report.IsLoadable);
        Assert.Contains("major", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MajorBump_WithAllowOutdated_IsCompatibleWithWarning()
    {
        ModCompatibility.EngineApiVersion = new Version(2, 0, 0);
        ModCompatibility.AllowOutdatedMods = true;
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 0));

        Assert.Equal(CompatibilityResult.CompatibleWithWarning, report.Result);
        Assert.True(report.IsLoadable);
    }

    [Fact]
    public void MajorBump_RejectFutureVersions_OverridesAllowOutdated()
    {
        ModCompatibility.EngineApiVersion = new Version(2, 0, 0);
        ModCompatibility.AllowOutdatedMods = true;
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 0), rejectFutureVersions: true);

        Assert.Equal(CompatibilityResult.Incompatible, report.Result);
        Assert.False(report.IsLoadable);
        Assert.Contains("rejects", report.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.Issues, i => i.Contains("rejectFutureVersions"));
    }

    [Fact]
    public void MajorDowngrade_DefaultPolicy_IsIncompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        ModCompatibility.AllowOutdatedMods = false;
        var report = ModCompatibility.CheckCompatibility(new Version(2, 0, 0));

        Assert.Equal(CompatibilityResult.Incompatible, report.Result);
        Assert.False(report.IsLoadable);
    }

    [Fact]
    public void MajorDowngrade_WithAllowOutdated_IsCompatibleWithWarning()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        ModCompatibility.AllowOutdatedMods = true;
        var report = ModCompatibility.CheckCompatibility(new Version(2, 0, 0));

        Assert.Equal(CompatibilityResult.CompatibleWithWarning, report.Result);
        Assert.True(report.IsLoadable);
    }

    // ─────────────────────────────────────────────────
    //  String parsing edge cases
    // ─────────────────────────────────────────────────

    [Fact]
    public void TwoPartVersion_NormalizedToThreePart()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility("1.0");

        Assert.Equal(CompatibilityResult.Compatible, report.Result);
    }

    [Fact]
    public void NullApiVersion_IsIncompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility((string?)null);

        Assert.Equal(CompatibilityResult.Incompatible, report.Result);
        Assert.Contains("missing", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyApiVersion_IsIncompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility("");

        Assert.Equal(CompatibilityResult.Incompatible, report.Result);
    }

    [Fact]
    public void InvalidApiVersion_IsIncompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        var report = ModCompatibility.CheckCompatibility("not-a-version");

        Assert.Equal(CompatibilityResult.Incompatible, report.Result);
        Assert.Contains("parse", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────
    //  Report details
    // ─────────────────────────────────────────────────

    [Fact]
    public void ReportContainsBothVersions()
    {
        ModCompatibility.EngineApiVersion = new Version(2, 3, 0);
        var report = ModCompatibility.CheckCompatibility(new Version(1, 5, 0));

        Assert.Equal(new Version(1, 5, 0), report.ModApiVersion);
        Assert.Equal(new Version(2, 3, 0), report.EngineApiVersion);
    }

    [Fact]
    public void ReportPopulatesIssues()
    {
        ModCompatibility.EngineApiVersion = new Version(2, 0, 0);
        ModCompatibility.AllowOutdatedMods = false;
        var report = ModCompatibility.CheckCompatibility(new Version(1, 0, 0));

        Assert.NotEmpty(report.Issues);
    }

    // ─────────────────────────────────────────────────
    //  v0.x handling (pre-release / early development)
    // ─────────────────────────────────────────────────

    [Fact]
    public void VersionZero_ModAgainstEngineV1_IsMajorMismatch()
    {
        ModCompatibility.EngineApiVersion = new Version(1, 0, 0);
        ModCompatibility.AllowOutdatedMods = false;
        var report = ModCompatibility.CheckCompatibility(new Version(0, 1, 0));

        // Major 0 vs major 1 = major mismatch
        Assert.Equal(CompatibilityResult.Incompatible, report.Result);
    }

    [Fact]
    public void VersionZero_ModAgainstEngineV0_SameMajorMinor_IsCompatible()
    {
        ModCompatibility.EngineApiVersion = new Version(0, 1, 0);
        var report = ModCompatibility.CheckCompatibility(new Version(0, 1, 0));

        Assert.Equal(CompatibilityResult.Compatible, report.Result);
    }
}
