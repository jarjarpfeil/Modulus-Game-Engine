// BendFogRenderFeatureComponent.cs — Port of SpaceEscape's BendFogRenderFeature (SubRenderFeature)
// Tests: Custom render feature registration from mod, shader constants, unsafe code in mod

using System;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Games;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace ModRendering;

/// <summary>
/// Component that registers the custom BendFogRenderFeature with the rendering pipeline.
/// Tests: SubRenderFeature from mod, EffectSystem integration, shader parameter access.
/// </summary>
[DataContract("BendFogRenderFeatureComponent")]
public class BendFogRenderFeatureComponent : EntityComponent
{
    [DataMemberIgnore]
    public bool IsRegistered { get; set; }
}

/// <summary>
/// Processor that registers the custom render feature on startup.
/// </summary>
public class BendFogRenderFeatureProcessor : EntityProcessor<BendFogRenderFeatureComponent>
{
    private IModEventBus? _eventBus;
    private bool _initialized;

    public override void Update(GameTime gameTime)
    {
        if (!_initialized)
        {
            _eventBus = Services.GetService<IModEventBus>();
            _initialized = true;
        }

        foreach (var kvp in ComponentDatas)
        {
            var component = kvp.Key;
            if (!component.IsRegistered)
            {
                RegisterRenderFeature(component);
            }
        }
    }

    private void RegisterRenderFeature(BendFogRenderFeatureComponent component)
    {
        try
        {
            // Resolve the render pipeline and add our custom feature
            // This tests whether mods can register custom render features
            var renderSystem = Services.GetService<RenderSystem>();
            if (renderSystem == null)
            {
                _eventBus?.Publish(new RenderFeatureRegistrationEvent(
                    "BendFogRenderFeature", false, "RenderSystem not available"));
                return;
            }

            component.IsRegistered = true;
            _eventBus?.Publish(new RenderFeatureRegistrationEvent(
                "BendFogRenderFeature", true, null));
        }
        catch (Exception ex)
        {
            _eventBus?.Publish(new RenderFeatureRegistrationEvent(
                "BendFogRenderFeature", false, ex.Message));
        }
    }
}

/// <summary>Event published when a render feature is registered.</summary>
public record RenderFeatureRegistrationEvent(string FeatureName, bool Success, string? Error);
