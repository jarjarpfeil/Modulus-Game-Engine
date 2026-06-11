// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Modulus.Modding.Api;

/// <summary>
/// Declares how a mod's scene integrates with the host game.
/// Set in mod.json under scenes[].behavior, or defaults to <see cref="MenuSelect"/>.
///
/// This is part of the stable <c>Modulus.Modding.Api</c> ABI.
/// </summary>
public enum ModSceneLoadBehavior
{
    /// <summary>
    /// No explicit behavior declared — the engine presents this scene as a
    /// player-selectable option (e.g., a scene selection menu).
    /// This is the default when a mod ships scenes without setting behavior.
    /// </summary>
    MenuSelect = 0,

    /// <summary>
    /// Scene replaces the game's main scene entirely (total conversion mod).
    /// When loaded, the current scene is unloaded first.
    /// </summary>
    Replace = 1,

    /// <summary>
    /// Scene is loaded additively on top of the current scene.
    /// Entities from this scene coexist with the game's existing entities.
    /// </summary>
    Additive = 2,

    /// <summary>
    /// Scene replaces background/environment elements but keeps existing
    /// gameplay entities intact.
    /// </summary>
    Background = 3,
}
