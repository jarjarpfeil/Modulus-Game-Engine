// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Engine.Modding;

/// <summary>
/// Security configuration for the mod loading system.
/// Register as a service via <see cref="IServiceRegistry"/> to control native mod loading.
/// </summary>
public sealed class ModSecurityConfig
{
    /// <summary>
    /// When true, mods with <see cref="ModManifest.RequiresNativeCode"/> set to true
    /// are allowed to load. When false (default), such mods are rejected with
    /// <see cref="ModState.Errored"/>.
    ///
    /// Native mods bypass ALC isolation and cannot be hot-swapped. Enabling this
    /// reduces crash safety and hot-reload guarantees.
    /// </summary>
    public bool EnableNativeModLoading { get; set; }
}
