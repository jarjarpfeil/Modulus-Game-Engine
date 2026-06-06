// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Modulus.Modding.Api;

/// <summary>
/// Standalone interface for mod systems. Mods implement this to define custom
/// ECS systems (processors) without referencing Stride types directly.
///
/// The engine internally wraps <see cref="IModSystem"/> instances in an adapter
/// class (<c>EntityProcessor</c>) so they participate in the normal Stride ECS
/// update loop. Mods never see the adapter.
///
/// Systems run every frame in priority order (lower priority number = runs first).
///
/// This is part of the stable <c>Modulus.Modding.Api</c> ABI.
/// </summary>
public interface IModSystem
{
    /// <summary>
    /// Execution priority. Lower values run first. Default is 100.
    /// Use this to control ordering between mod systems.
    /// </summary>
    int Priority => 100;

    /// <summary>
    /// Called each frame. Contains the mod's gameplay logic.
    /// </summary>
    /// <param name="deltaTime">Seconds elapsed since last frame.</param>
    void Update(float deltaTime);

    /// <summary>
    /// Called once when the system is registered with the engine.
    /// Use this for one-time setup (subscribing to events, etc.).
    /// </summary>
    void OnRegistered() { }

    /// <summary>
    /// Called once when the system is unregistered (mod unload/disable).
    /// Clean up any resources here.
    /// </summary>
    void OnUnregistered() { }
}
