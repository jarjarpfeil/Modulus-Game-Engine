// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Serialization.Contents;
using Stride.Games;
using Stride.Rendering.Compositing;

namespace Stride.Engine.Modding;

/// <summary>
/// Lightweight game system that auto-discovers and loads mods on the first frame.
/// Added automatically by <see cref="Game.Initialize"/> — no game code required.
///
/// The mod scene is used as-is. We do NOT merge host geometry into it: many mod
/// scenes are "shells" (camera + scripts + UI) whose visible content is spawned at
/// runtime by SyncScripts, so they legitimately have no static
/// <see cref="ModelComponent"/>/<see cref="BackgroundComponent"/>. Merging host
/// geometry into such a shell clobbers it. Host games that want a geometry fallback
/// should opt in via an explicit API, not by default.
///
/// After the scene loads, this system only ensures the mod scene's camera is bound
/// to the active compositor slot (so the replacement scene actually renders through
/// the host's compositor).
///
/// One-shot: disables itself after the first frame.
/// </summary>
public class ModAutoLoadSystem : GameSystemBase
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModAutoLoad");

    private bool _loaded;

    public ModAutoLoadSystem(IServiceRegistry registry) : base(registry)
    {
        Enabled = true;
        Visible = false;
    }

    public override void Update(GameTime gameTime)
    {
        if (_loaded) return;
        _loaded = true;

        var modHost = Services.GetService<ModHost>();
        if (modHost == null)
        {
            Log.Warning("[ModAutoLoad] ModHost not found — mods will not be loaded");
            Enabled = false;
            return;
        }

        var selectable = modHost.SceneManager.GetPlayerSelectableScenes();
        if (selectable.Count == 0)
        {
            Log.Info("[ModAutoLoad] No mod scenes — default game scene will render");
            Enabled = false;
            return;
        }

        Log.Info($"[ModAutoLoad] {selectable.Count} player-selectable scene(s):");
        foreach (var entry in selectable)
            Log.Info($"  [{entry.ModId}] {entry.DisplayName}");

        var sceneSystem = Services.GetService<SceneSystem>();
        if (sceneSystem?.SceneInstance?.RootScene == null) return;
        var rootScene = sceneSystem.SceneInstance.RootScene;

        // The mod scene is used as-is (see class remarks). We do NOT merge host
        // geometry into it: many mod scenes are "shells" whose visible content is
        // spawned at runtime by SyncScripts. Merging host geometry here would
        // clobber the shell with the host's blank-template scene (a previous
        // regression did exactly this and masked whether the mod scene worked).
        if (!string.IsNullOrEmpty(ModSceneManager.OriginalSceneUrl))
        {
            Log.Info($"[ModAutoLoad] Using mod scene as-is (host original scene '{ModSceneManager.OriginalSceneUrl}' not merged into replacement)");
        }

        // Ensure camera is assigned to the compositor's camera slot
        var compositor = sceneSystem.GraphicsCompositor;
        var cameraSlot = compositor?.Cameras?.Count > 0 ? compositor.Cameras[0] : null;
        foreach (var entity in rootScene.Entities)
        {
            var cameraComponent = entity.Get<CameraComponent>();
            if (cameraComponent != null && cameraSlot != null)
            {
                Log.Info($"[ModAutoLoad] Assigning camera '{entity.Name}' to compositor slot '{cameraSlot.Name}'");
                cameraComponent.Slot = cameraSlot.ToSlotId();
                cameraSlot.Camera = cameraComponent;
                cameraComponent.Slot.AttachedCompositor = compositor;
                break;
            }
        }

        Log.Info($"[ModAutoLoad] Scene ready: {rootScene.Entities.Count} entities");

        // Force SceneInstance to process the new entities before the next draw
        sceneSystem.SceneInstance.Update(new GameTime());

        Enabled = false;
    }
}
