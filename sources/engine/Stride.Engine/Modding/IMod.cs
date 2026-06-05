// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;

namespace Stride.Engine.Modding;

/// <summary>
/// Entry point interface for mods. Every mod DLL that declares an entryPoint in mod.json
/// must implement this interface. The engine calls these methods during the mod lifecycle.
/// </summary>
public interface IMod
{
    /// <summary>Unique mod identifier (must match mod.json id).</summary>
    string Id { get; }

    /// <summary>Human-readable mod name.</summary>
    string Name { get; }

    /// <summary>Mod version.</summary>
    Version Version { get; }

    /// <summary>Minimum API version this mod requires.</summary>
    Version MinApiVersion { get; }

    /// <summary>
    /// Called once when the mod is first loaded. Use this to register components,
    /// subscribe to events, and set up initial state.
    /// </summary>
    void Initialize(IModContext context);

    /// <summary>Called when the mod is enabled (or re-enabled after disable).</summary>
    void OnEnabled();

    /// <summary>Called when the mod is disabled (by user or error).</summary>
    void OnDisabled();
}
