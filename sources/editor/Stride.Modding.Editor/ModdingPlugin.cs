// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModel;
using Stride.Core.Diagnostics;
using Stride.Core.Packages;
using Stride.Core.Presentation.View;
using Stride.Editor;

namespace Stride.Modding.Editor;

/// <summary>
/// Game Studio plugin that registers the Mod Manager panel and mod-related
/// property template providers. Loaded automatically via ModuleInitializer.
/// </summary>
public sealed class ModdingPlugin : StrideAssetsPlugin
{
    private ModManagerViewModel? _modManagerViewModel;
    private Action<ModManagerViewModel?>? _modManagerCallback;

    /// <summary>
    /// Static accessor for the current plugin instance.
    /// </summary>
    public static ModdingPlugin? Instance { get; private set; }

    /// <summary>
    /// The current ModManagerViewModel, or null if no session is loaded.
    /// </summary>
    public ModManagerViewModel? ModManager => _modManagerViewModel;

    /// <summary>
    /// Sets a callback to be invoked when the ModManager is created or disposed.
    /// Called by GameStudioViewModel to sync the ModManager property.
    /// </summary>
    public void SetModManagerCallback(Action<ModManagerViewModel?> callback)
    {
        _modManagerCallback = callback;
        // If ModManager is already created, invoke immediately
        if (_modManagerViewModel != null)
            callback(_modManagerViewModel);
    }

    protected override void Initialize(ILogger logger)
    {
        Instance = this;
        logger.Info("[Modding] ModdingPlugin initialized");
    }

    public override void InitializeSession(SessionViewModel session)
    {
        _modManagerViewModel = new ModManagerViewModel(session);
        _modManagerCallback?.Invoke(_modManagerViewModel);

        // Register Modulus.Modding.Api as a suggested package so mod types
        // appear in the "Add Reference" dialog and asset browser
        session.SuggestedPackages.Add(new PackageName(
            typeof(Modulus.Modding.Api.IMod).Assembly.GetName().Name,
            new PackageVersion("1.0.0")));

        // Register the Stride.Engine assembly too, since mod components
        // inherit from EntityComponent
        session.SuggestedPackages.Add(new PackageName(
            typeof(Stride.Engine.EntityComponent).Assembly.GetName().Name,
            new PackageVersion("1.0.0")));
    }

    // Note: SessionDisposed is not overridden to avoid access modifier conflicts
    // with the WPF compilation pipeline. Cleanup happens when the next session
    // is initialized (overwrites the old ViewModel).

    public override void RegisterAssetPreviewViewTypes(IDictionary<Type, Type> assetPreviewViewTypes)
    {
        // No custom asset preview views for mods in v1
    }

    /// <inheritdoc />
    public override void RegisterTemplateProviders(ICollection<ITemplateProvider> templateProviders)
    {
        // Register template providers for mod component types.
        // The metadata ALC makes mod types visible to the property grid's default
        // reflection-based display. Custom DataTemplates can be added here for
        // richer mod component editing in a future version.
    }
}
