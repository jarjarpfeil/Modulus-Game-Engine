// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.ViewModels;
using Stride.Engine.Modding;

namespace Stride.Modding.Editor;

/// <summary>
/// ViewModel representing a single discovered mod in the Mod Manager list.
/// Wraps a ModManifest with user-facing display properties.
/// </summary>
public sealed class ModInfoViewModel : ViewModelBase
{
    private bool _isEnabled = true;

    public ModInfoViewModel(IViewModelServiceProvider serviceProvider, ModManifest manifest, string modsDirectory)
        : base(serviceProvider)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        ModPath = Path.Combine(modsDirectory, manifest.Id);
    }

    /// <summary>The underlying mod manifest.</summary>
    public ModManifest Manifest { get; internal set; }

    /// <summary>Path to the mod directory on disk.</summary>
    public string ModPath { get; }

    // Convenience properties for XAML binding
    public string Id => Manifest.Id;
    public string Name => Manifest.Name;
    public string Version => Manifest.Version;
    public string? Author => Manifest.Author;
    public string? Description => Manifest.Description;
    public string ApiVersion => Manifest.ApiVersion;

    /// <summary>List of dependency mod IDs.</summary>
    public IReadOnlyList<string> Dependencies => Manifest.Dependencies.Select(d => d.Id).ToList();

    /// <summary>Whether the user has enabled this mod.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetValue(ref _isEnabled, value);
    }

    /// <summary>Formatted dependency list for display.</summary>
    public string DependenciesText
    {
        get
        {
            var deps = Dependencies;
            return deps.Count == 0 ? "None" : string.Join(", ", deps);
        }
    }

    /// <summary>Summary text for the details panel.</summary>
    public string Summary => $"{Name} v{Version}" +
                              (Author != null ? $" by {Author}" : "") +
                              (Description != null ? $"\n{Description}" : "");
}
