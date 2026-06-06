// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Modulus.Modding.Api;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Internal adapter that bridges a mod's standalone <see cref="IModComponent"/>
/// to Stride's <see cref="EntityComponent"/> base class.
///
/// This allows mods to define components without referencing Stride types.
/// The engine creates a <see cref="ModComponentAdapter"/> for each
/// <see cref="IModComponent"/> instance and attaches it to the ECS entity.
///
/// Mods never interact with this class directly.
/// </summary>
[DataContract("ModComponentAdapter")]
[Display("Mod Component")]
public sealed class ModComponentAdapter : EntityComponent
{
    private readonly IModComponent _modComponent;

    /// <summary>
    /// The underlying mod component instance.
    /// </summary>
    [DataMemberIgnore]
    public IModComponent ModComponent => _modComponent;

    /// <summary>
    /// The mod ID that owns this component, for lifecycle tracking.
    /// </summary>
    [DataMemberIgnore]
    public string? OwnerModId { get; internal set; }

    public ModComponentAdapter()
    {
        // Parameterless constructor for serialization/Activator.CreateInstance.
        // _modComponent will be null — guard all calls with null checks.
        // This is required for Stride's serialization system which calls Activator.CreateInstance.
        _modComponent = null!;
    }

    public ModComponentAdapter(IModComponent modComponent)
    {
        _modComponent = modComponent ?? throw new System.ArgumentNullException(nameof(modComponent));
    }

    /// <summary>
    /// Whether this adapter has a valid mod component instance.
    /// Returns false for deserialized instances that haven't been hydrated yet.
    /// </summary>
    [DataMemberIgnore]
    public bool HasModComponent => _modComponent != null;

    /// <summary>
    /// Forward lifecycle call to the underlying mod component.
    /// Called by the engine when the component is added to an entity.
    /// </summary>
    internal void InvokeOnAttach()
    {
        try
        {
            _modComponent?.OnAttach();
        }
        catch (System.Exception ex)
        {
            var logger = GlobalLogger.GetLogger("ModComponentAdapter");
            logger.Error($"[ModComponentAdapter] OnAttach failed for component owned by '{OwnerModId ?? "unknown"}': {ex.Message}");
        }
    }

    /// <summary>
    /// Forward lifecycle call to the underlying mod component.
    /// Called by the engine when the component is removed or entity destroyed.
    /// </summary>
    internal void InvokeOnDetach()
    {
        try
        {
            _modComponent?.OnDetach();
        }
        catch (System.Exception ex)
        {
            var logger = GlobalLogger.GetLogger("ModComponentAdapter");
            logger.Error($"[ModComponentAdapter] OnDetach failed for component owned by '{OwnerModId ?? "unknown"}': {ex.Message}");
        }
    }

    /// <summary>
    /// Forward per-frame update to the underlying mod component.
    /// </summary>
    internal void InvokeUpdate(float deltaTime)
    {
        try
        {
            _modComponent?.Update(deltaTime);
        }
        catch (System.Exception ex)
        {
            var logger = GlobalLogger.GetLogger("ModComponentAdapter");
            logger.Error($"[ModComponentAdapter] Update failed for component owned by '{OwnerModId ?? "unknown"}': {ex.Message}");
        }
    }
}
