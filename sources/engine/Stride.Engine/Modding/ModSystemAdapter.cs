// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Modulus.Modding.Api;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Games;

namespace Stride.Engine.Modding;

/// <summary>
/// Internal adapter that bridges a mod's standalone <see cref="IModSystem"/>
/// to Stride's <see cref="EntityProcessor{T}"/> base class.
///
/// This allows mods to define ECS systems without referencing Stride types.
/// The engine creates a <see cref="ModProcessorAdapter{T}"/> for each registered
/// <see cref="IModSystem"/> and adds it to the scene's entity processors.
///
/// The generic parameter <typeparamref name="T"/> preserves the concrete mod system
/// type for diagnostics and introspection.
///
/// Mods never interact with this class directly.
/// </summary>
public sealed class ModProcessorAdapter<T> : EntityProcessor<ModComponentAdapter>
    where T : IModSystem
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModProcessorAdapter");

    private readonly T _modSystem;
    private readonly string _modId;

    /// <summary>
    /// The underlying mod system instance.
    /// </summary>
    public T ModSystem => _modSystem;

    /// <summary>
    /// The mod ID that owns this system.
    /// </summary>
    public string ModId => _modId;

    /// <summary>
    /// Execution priority — lower values run first.
    /// </summary>
    public int Priority => _modSystem.Priority;

    public ModProcessorAdapter(T modSystem, string modId)
    {
        _modSystem = modSystem ?? throw new ArgumentNullException(nameof(modSystem));
        _modId = modId ?? throw new ArgumentNullException(nameof(modId));
    }

    /// <inheritdoc />
    public override void Update(GameTime time)
    {
        try
        {
            var deltaTime = (float)time.Elapsed.TotalSeconds;
            _modSystem.Update(deltaTime);
        }
        catch (Exception ex)
        {
            Log.Error($"[ModProcessorAdapter] Update failed for mod '{_modId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Called once when the system is registered with the entity manager.
    /// </summary>
    internal void InvokeOnRegistered()
    {
        try
        {
            _modSystem.OnRegistered();
        }
        catch (Exception ex)
        {
            Log.Error($"[ModProcessorAdapter] OnRegistered failed for mod '{_modId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Called once when the system is being removed (mod unload/disable).
    /// </summary>
    internal void InvokeOnUnregistered()
    {
        try
        {
            _modSystem.OnUnregistered();
        }
        catch (Exception ex)
        {
            Log.Error($"[ModProcessorAdapter] OnUnregistered failed for mod '{_modId}': {ex.Message}");
        }
    }
}
