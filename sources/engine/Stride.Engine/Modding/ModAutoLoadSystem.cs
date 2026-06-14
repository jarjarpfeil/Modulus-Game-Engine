// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Linq;
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
/// For Replace-behavior mod scenes that lack renderable geometry, merges the original
/// game scene's entities so the window always shows something visible.
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

        // For Replace-behavior scenes, merge the original game scene's entities.
        // Mod scenes compiled without GPU resources typically only have Camera+Light.
        // The original scene's pre-compiled geometry (Ground, Sphere, Skybox, etc.)
        // provides visible content. Runtime Material.New() doesn't work because
        // the EffectSystem can't compile shaders on the fly.
        var originalUrl = ModSceneManager.OriginalSceneUrl;
        if (!string.IsNullOrEmpty(originalUrl))
        {
            var contentManager = Services.GetService<ContentManager>();
            if (contentManager != null && contentManager.Exists(originalUrl))
            {
                var originalScene = contentManager.Load<Scene>(originalUrl);
                if (originalScene != null)
                {
                    Log.Info($"[ModAutoLoad] Merging original scene '{originalUrl}' ({originalScene.Entities.Count} entities) into mod scene");

                    // Remove mod scene's camera to avoid duplicate-camera conflicts
                    // (the original scene brings its own camera)
                    var modCamera = rootScene.Entities.FirstOrDefault(e => e.Get<CameraComponent>() != null);
                    if (modCamera != null)
                    {
                        rootScene.Entities.Remove(modCamera);
                        Log.Info("[ModAutoLoad] Removed mod scene camera (using original scene camera instead)");
                    }

                    // Move entities from original scene to mod scene
                    foreach (var entity in originalScene.Entities.ToList())
                    {
                        originalScene.Entities.Remove(entity);
                        rootScene.Entities.Add(entity);
                    }
                    foreach (var child in originalScene.Children.ToList())
                    {
                        originalScene.Children.Remove(child);
                        rootScene.Children.Add(child);
                    }
                }
            }
            else
            {
                Log.Warning($"[ModAutoLoad] Original scene '{originalUrl}' not found in content database");
            }
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
