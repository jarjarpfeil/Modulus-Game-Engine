// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Engine.Modding;

/// <summary>
/// Result of a mod reload operation.
/// </summary>
public enum ModReloadResult
{
    /// <summary>Mod was successfully reloaded.</summary>
    Success,

    /// <summary>Reload not possible — engine restart required.</summary>
    RequiresRestart,

    /// <summary>Reload failed — mod has been temporarily disabled.</summary>
    TemporarilyDisabled,
}
