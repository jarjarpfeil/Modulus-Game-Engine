// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Modulus.Modding.Api;

/// <summary>
/// Standalone interface for mod components. Mods implement this to define custom
/// ECS components without referencing Stride types directly.
///
/// The engine internally wraps <see cref="IModComponent"/> instances in an adapter
/// class (<c>EntityComponent</c>) so they can participate in the normal Stride ECS
/// pipeline. Mods never see the adapter — they interact only with this interface.
///
/// This abstraction is what enables cross-game compatibility: the same
/// <see cref="IModComponent"/> implementation works in any game built on Modulus,
/// regardless of which Stride version or game-specific components are present.
///
/// This is part of the stable <c>Modulus.Modding.Api</c> ABI.
/// </summary>
public interface IModComponent
{
    /// <summary>
    /// Called when this component is first attached to an entity.
    /// Use this for initialization that depends on the entity context.
    /// </summary>
    void OnAttach() { }

    /// <summary>
    /// Called when this component is removed from an entity or the entity is destroyed.
    /// Clean up any resources or subscriptions here.
    /// </summary>
    void OnDetach() { }

    /// <summary>
    /// Called each frame while the component is active.
    /// </summary>
    /// <param name="deltaTime">Seconds elapsed since last frame.</param>
    void Update(float deltaTime) { }
}
