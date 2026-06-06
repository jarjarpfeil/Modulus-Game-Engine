// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
using Stride.Core.Assets.Editor.ViewModel;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.ViewModels;
using Stride.Engine.Modding;

namespace Stride.Modding.Editor;

/// <summary>
/// ViewModel for the Mod Manager panel in Game Studio.
/// At editor time, mods are discovered (not loaded) — this ViewModel tracks
/// manifests and user preferences (enable/disable) via a sidecar settings file.
/// Actual mod loading happens at runtime only.
/// </summary>
public sealed class ModManagerViewModel : ViewModelBase
{
    private readonly SessionViewModel _session;
    private readonly string _projectModSettingsPath;
    private ModInfoViewModel? _selectedMod;
    private string _statusMessage = "Ready";
    private EditorMetadataLoader? _metadataLoader;

    public ModManagerViewModel(SessionViewModel session) : base(session.ServiceProvider)
    {
        _session = session;

        // Settings file lives next to the project
        var projectDir = session.CurrentProject?.PackagePath?.GetFullDirectory()
                         ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModulusEngine");
        _projectModSettingsPath = Path.Combine(projectDir, "mod-settings.json");

        InstallModCommand = new AnonymousCommand(ServiceProvider, InstallMod);
        UninstallModCommand = new AnonymousCommand(ServiceProvider, UninstallMod, () => SelectedMod != null);
        EnableModCommand = new AnonymousCommand(ServiceProvider, () => SetModEnabled(true), () => SelectedMod is { IsEnabled: false });
        DisableModCommand = new AnonymousCommand(ServiceProvider, () => SetModEnabled(false), () => SelectedMod is { IsEnabled: true });
        ReloadModCommand = new AnonymousCommand(ServiceProvider, ReloadMod, () => SelectedMod != null);
        RefreshCommand = new AnonymousCommand(ServiceProvider, RefreshModList);

        RefreshModList();
    }

    /// <summary>All discovered mods.</summary>
    public ObservableCollection<ModInfoViewModel> Mods { get; } = [];

    /// <summary>Mod console log messages.</summary>
    public ObservableCollection<string> ModConsoleMessages { get; } = [];

    /// <summary>Currently selected mod in the list.</summary>
    public ModInfoViewModel? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (SetValue(ref _selectedMod, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Status bar message.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetValue(ref _statusMessage, value);
    }

    /// <summary>Loads mod assemblies for metadata-only reflection (property grid).</summary>
    public EditorMetadataLoader MetadataLoader => _metadataLoader ??= new EditorMetadataLoader();

    // Commands
    public ICommandBase InstallModCommand { get; }
    public ICommandBase UninstallModCommand { get; }
    public ICommandBase EnableModCommand { get; }
    public ICommandBase DisableModCommand { get; }
    public ICommandBase ReloadModCommand { get; }
    public ICommandBase RefreshCommand { get; }

    /// <summary>Scans the project's mods/ directory and reloads the list.</summary>
    public void RefreshModList()
    {
        Mods.Clear();

        var modsDirectory = GetModsDirectory();
        var settings = LoadSettings();

        if (!Directory.Exists(modsDirectory))
        {
            StatusMessage = $"No mods/ directory found at {modsDirectory}";
            LogToConsole($"[Editor] {StatusMessage}");
            return;
        }

        try
        {
            var discovered = ModDiscovery.Discover(modsDirectory);
            foreach (var package in discovered)
            {
                var info = new ModInfoViewModel(ServiceProvider, package.Manifest, modsDirectory);
                // Restore persisted enable/disable state
                if (settings.TryGetValue(info.Id, out var enabled))
                    info.IsEnabled = enabled;
                Mods.Add(info);
            }

            StatusMessage = $"Found {Mods.Count} mod(s)";
            LogToConsole($"[Editor] Discovered {Mods.Count} mod(s) in {modsDirectory}");

            // Load metadata for property grid integration
            foreach (var mod in Mods.Where(m => m.IsEnabled))
            {
                MetadataLoader.LoadModMetadata(mod.Manifest, Path.Combine(modsDirectory, mod.Id));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning mods: {ex.Message}";
            LogToConsole($"[Error] {StatusMessage}");
        }
    }

    /// <summary>Opens a file dialog to install a .modpkg file.</summary>
    private void InstallMod()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Install Mod Package",
            Filter = "Mod Package (*.modpkg)|*.modpkg|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var modsDirectory = GetModsDirectory();
            Directory.CreateDirectory(modsDirectory);

            var packageManager = new ModPackageManager(modsDirectory);
            var installedId = packageManager.Install(dialog.FileName);
            if (!string.IsNullOrEmpty(installedId))
            {
                StatusMessage = $"Installed mod: {installedId}";
                LogToConsole($"[Editor] Installed mod '{installedId}' from {Path.GetFileName(dialog.FileName)}");
                RefreshModList();
            }
            else
            {
                StatusMessage = "Failed to install mod package — invalid format?";
                LogToConsole($"[Error] Failed to install: {Path.GetFileName(dialog.FileName)}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Install failed: {ex.Message}";
            LogToConsole($"[Error] Install failed: {ex.Message}");
        }
    }

    /// <summary>Uninstalls the selected mod.</summary>
    private void UninstallMod()
    {
        if (SelectedMod == null) return;

        try
        {
            var modsDirectory = GetModsDirectory();
            var packageManager = new ModPackageManager(modsDirectory);
            var result = packageManager.Uninstall(SelectedMod.Id);
            if (result)
            {
                StatusMessage = $"Uninstalled mod: {SelectedMod.Name}";
                LogToConsole($"[Editor] Uninstalled mod '{SelectedMod.Name}' ({SelectedMod.Id})");
                MetadataLoader.UnloadModMetadata(SelectedMod.Id);
                RefreshModList();
            }
            else
            {
                StatusMessage = "Failed to uninstall mod";
                LogToConsole($"[Error] Failed to uninstall mod '{SelectedMod.Id}'");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Uninstall failed: {ex.Message}";
            LogToConsole($"[Error] Uninstall failed: {ex.Message}");
        }
    }

    /// <summary>Sets the enabled state of the selected mod and persists.</summary>
    private void SetModEnabled(bool enabled)
    {
        if (SelectedMod == null) return;

        SelectedMod.IsEnabled = enabled;
        SaveSettings();

        if (enabled)
        {
            var modsDirectory = GetModsDirectory();
            MetadataLoader.LoadModMetadata(SelectedMod.Manifest, Path.Combine(modsDirectory, SelectedMod.Id));
            StatusMessage = $"Enabled mod: {SelectedMod.Name}";
            LogToConsole($"[Editor] Enabled mod '{SelectedMod.Name}'");
        }
        else
        {
            MetadataLoader.UnloadModMetadata(SelectedMod.Id);
            StatusMessage = $"Disabled mod: {SelectedMod.Name}";
            LogToConsole($"[Editor] Disabled mod '{SelectedMod.Name}'");
        }

        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Marks the selected mod for reload on next game start.
    /// At editor time, mods are NOT loaded at runtime — this toggles the mod
    /// off and on so the runtime ModHost will pick up the change.
    /// </summary>
    private void ReloadMod()
    {
        if (SelectedMod == null) return;

        // At editor time, "reload" means: mark the mod for reload on next game start.
        // The actual ALC unload/reload happens at runtime via ModHost.
        // For editor iteration, we re-scan the mod directory to pick up any changes.
        var modsDirectory = GetModsDirectory();
        var modDir = Path.Combine(modsDirectory, SelectedMod.Id);

        if (Directory.Exists(modDir))
        {
            // Re-read manifest in case it changed
            var manifestPath = Path.Combine(modDir, "mod.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    using var stream = File.OpenRead(manifestPath);
                    var manifest = ModManifest.FromStream(stream);
                    // Update the displayed info
                    SelectedMod.Manifest = manifest;
                    LogToConsole($"[Editor] Reloaded manifest for '{SelectedMod.Name}' v{manifest.Version}");
                }
                catch (Exception ex)
                {
                    LogToConsole($"[Error] Failed to reload manifest: {ex.Message}");
                }
            }

            // Reload metadata
            if (SelectedMod.IsEnabled)
            {
                MetadataLoader.UnloadModMetadata(SelectedMod.Id);
                MetadataLoader.LoadModMetadata(SelectedMod.Manifest, modDir);
            }
        }

        StatusMessage = $"Reloaded mod: {SelectedMod.Name} (changes apply on next game start)";
        LogToConsole($"[Editor] Mod '{SelectedMod.Name}' marked for reload — changes will apply when the game starts");
    }

    private string GetModsDirectory()
    {
        var projectDir = _session.CurrentProject?.PackagePath?.GetFullDirectory() ?? Directory.GetCurrentDirectory();
        return Path.Combine(projectDir, "mods");
    }

    private Dictionary<string, bool> LoadSettings()
    {
        try
        {
            if (File.Exists(_projectModSettingsPath))
            {
                var json = File.ReadAllText(_projectModSettingsPath);
                return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? [];
            }
        }
        catch
        {
            // Settings file corrupted — start fresh
        }
        return [];
    }

    private void SaveSettings()
    {
        try
        {
            var settings = Mods.ToDictionary(m => m.Id, m => m.IsEnabled);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_projectModSettingsPath, json);
        }
        catch
        {
            // Non-fatal — settings persistence is a convenience
        }
    }

    /// <summary>Adds a message to the mod console log.</summary>
    private void LogToConsole(string message)
    {
        ModConsoleMessages.Add($"{DateTime.Now:HH:mm:ss} {message}");
        // Keep console from growing unbounded
        while (ModConsoleMessages.Count > 500)
            ModConsoleMessages.RemoveAt(0);
    }
}
