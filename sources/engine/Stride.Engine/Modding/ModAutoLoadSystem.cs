// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Lights;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace Stride.Engine.Modding;

/// <summary>
/// Lightweight game system that auto-discovers and loads mods on the first frame.
/// Added automatically by <see cref="Game.Initialize"/> — no game code required.
///
/// After loading mods, if no active scene content exists, creates a minimal fallback
/// scene so the game window always renders something visible.
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

        // Only load if the mods directory exists
        if (!System.IO.Directory.Exists(modHost.ModsDirectory))
        {
            Log.Info($"[ModAutoLoad] Mods directory '{modHost.ModsDirectory}' does not exist — no mods to load");
            Enabled = false;
            return;
        }

        try
        {
            var loaded = modHost.LoadAllMods();
            Log.Info($"[ModAutoLoad] Loaded {loaded.Count} mod(s)");

            // Log scene catalog summary
            var selectable = modHost.SceneManager.GetPlayerSelectableScenes();
            if (selectable.Count > 0)
            {
                Log.Info($"[ModAutoLoad] {selectable.Count} player-selectable scene(s) available:");
                foreach (var entry in selectable)
                    Log.Info($"  [{entry.ModId}] {entry.DisplayName}");
            }

            // Attempt to load mod-provided "replace" scenes
            var allScenes = modHost.SceneManager.GetAllModScenes();
            bool modSceneLoaded = false;
            foreach (var entry in allScenes)
            {
                if (entry.Behavior == Modulus.Modding.Api.ModSceneLoadBehavior.Replace)
                {
                    Log.Info($"[ModAutoLoad] Loading mod scene: {entry.DisplayName} ({entry.SceneUrl})");
                    var scene = modHost.SceneManager.LoadScene(entry.ModId, entry.SceneUrl);
                    modSceneLoaded = scene != null;
                    break; // Only load the first replace scene
                }
            }

            // Always ensure the active scene has visible geometry.
            // Mod scenes are compiled without GPU resources (no GraphicsDevice at compile time),
            // so they contain only data-only components (camera, light, transforms).
            // EnsureFallbackScene adds visible geometry (ground plane, marker cube) if missing.
            EnsureFallbackScene();
        }
        catch (Exception ex)
        {
            Log.Error($"[ModAutoLoad] Failed to load mods: {ex.Message}");
        }

        // One-shot — disable after running
        Enabled = false;
    }

    /// <summary>
    /// Creates a minimal fallback scene if the active scene has no renderable content.
    /// Adds a camera, directional light, and a visible ground plane.
    /// </summary>
    private void EnsureFallbackScene()
    {
        var sceneSystem = Services.GetService<SceneSystem>();
        if (sceneSystem == null) return;

        var sceneInstance = sceneSystem.SceneInstance;
        if (sceneInstance == null)
        {
            // No scene instance at all — create one
            var scene = new Scene();
            PopulateFallbackScene(scene);
            sceneSystem.SceneInstance = new SceneInstance(Services, scene);
            Log.Info("[ModAutoLoad] Created fallback scene (no mod scene loaded)");
            return;
        }

        // Check if the existing scene has useful content
        var rootScene = sceneInstance.RootScene;
        if (rootScene == null)
        {
            rootScene = new Scene();
            sceneInstance.RootScene = rootScene;
        }

        // Check for camera and renderable content
        bool hasCamera = false;
        bool hasRenderable = false;
        foreach (var entity in rootScene.Entities)
        {
            if (entity.Get<CameraComponent>() != null)
                hasCamera = true;
            if (entity.Get<ModelComponent>() != null)
                hasRenderable = true;
        }

        if (!hasCamera)
        {
            PopulateFallbackScene(rootScene);
            Log.Info("[ModAutoLoad] Added camera + light + geometry to empty scene");
        }
        else if (!hasRenderable)
        {
            AddVisibleGeometry(rootScene);
            Log.Info("[ModAutoLoad] Added visible geometry to mod scene (had camera but no renderables)");
        }
    }

    private void PopulateFallbackScene(Scene scene)
    {
        var graphicsDevice = (Services.GetService<IGraphicsDeviceService>())?.GraphicsDevice;
        if (graphicsDevice == null) return;

        // Find the default camera slot from the GraphicsCompositor
        var sceneSystem = Services.GetService<SceneSystem>();
        var compositor = sceneSystem?.GraphicsCompositor;
        var cameraSlot = compositor?.Cameras?.Count > 0 ? compositor.Cameras[0] : null;

        // ── Camera ──
        var cameraComponent = new CameraComponent
        {
            Projection = CameraProjectionMode.Perspective,
            VerticalFieldOfView = 55f,
            NearClipPlane = 0.1f,
            FarClipPlane = 1000f,
        };
        // Assign to the compositor's camera slot so the renderer picks it up
        if (cameraSlot != null)
            cameraComponent.Slot = cameraSlot.ToSlotId();

        var cameraEntity = new Entity("Camera") { cameraComponent };
        cameraEntity.Transform.Position = new Vector3(0, 8, -12);
        // Look toward origin from above
        cameraEntity.Transform.Rotation = Quaternion.RotationX(0.55f);
        scene.Entities.Add(cameraEntity);

        // ── Directional Light ──
        var lightEntity = new Entity("Light")
        {
            new LightComponent
            {
                Type = new LightDirectional(),
                Intensity = 2.0f,
            }
        };
        lightEntity.Transform.Rotation = Quaternion.RotationX(-1.0f) * Quaternion.RotationY(0.5f);
        scene.Entities.Add(lightEntity);

        // ── Ground (flat cube, visible indicator that the engine is running) ──
        var ground = CreateColoredCube(graphicsDevice,
            "Ground",
            new Vector3(0, -0.5f, 0),
            new Vector3(50, 1, 50),
            new Color4(0.25f, 0.6f, 0.3f, 1f));
        scene.Entities.Add(ground);

        // ── A visible cube above the ground ──
        var marker = CreateColoredCube(graphicsDevice,
            "Marker",
            new Vector3(0, 1.5f, 0),
            new Vector3(2, 3, 2),
            new Color4(0.9f, 0.2f, 0.2f, 1f));
        scene.Entities.Add(marker);
    }

    /// <summary>
    /// Adds visible geometry (ground plane + marker cube) to an existing scene that already
    /// has a camera and light. Used when a mod scene is loaded but contains no renderable
    /// mesh data (compiled scenes can only include data-only components without a GPU).
    /// </summary>
    private void AddVisibleGeometry(Scene scene)
    {
        var graphicsDevice = (Services.GetService<IGraphicsDeviceService>())?.GraphicsDevice;
        if (graphicsDevice == null) return;

        var ground = CreateColoredCube(graphicsDevice,
            "Ground",
            new Vector3(0, -0.5f, 0),
            new Vector3(50, 1, 50),
            new Color4(0.25f, 0.6f, 0.3f, 1f));
        scene.Entities.Add(ground);

        var marker = CreateColoredCube(graphicsDevice,
            "Marker",
            new Vector3(0, 1.5f, 0),
            new Vector3(2, 3, 2),
            new Color4(0.9f, 0.2f, 0.2f, 1f));
        scene.Entities.Add(marker);
    }

    private static Entity CreateColoredCube(GraphicsDevice graphicsDevice, string name,
        Vector3 position, Vector3 scale, Color4 color)
    {
        var material = CreateSimpleMaterial(graphicsDevice, color);

        var model = new Model();
        model.Meshes.Add(CreateCubeMesh(graphicsDevice));
        model.Materials.Add(new MaterialInstance(material));

        var entity = new Entity(name)
        {
            new ModelComponent(model),
        };
        entity.Transform.Position = position;
        entity.Transform.Scale = scale;
        return entity;
    }

    private static Mesh CreateCubeMesh(GraphicsDevice graphicsDevice)
    {
        var vertices = new VertexPositionNormalTexture[]
        {
            // Front face
            new(new Vector3(-0.5f, -0.5f,  0.5f), Vector3.UnitZ, Vector2.Zero),
            new(new Vector3( 0.5f, -0.5f,  0.5f), Vector3.UnitZ, Vector2.UnitX),
            new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.UnitZ, Vector2.One),
            new(new Vector3(-0.5f,  0.5f,  0.5f), Vector3.UnitZ, Vector2.UnitY),
            // Back face
            new(new Vector3( 0.5f, -0.5f, -0.5f), -Vector3.UnitZ, Vector2.Zero),
            new(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitZ, Vector2.UnitX),
            new(new Vector3(-0.5f,  0.5f, -0.5f), -Vector3.UnitZ, Vector2.One),
            new(new Vector3( 0.5f,  0.5f, -0.5f), -Vector3.UnitZ, Vector2.UnitY),
            // Top face
            new(new Vector3(-0.5f,  0.5f,  0.5f), Vector3.UnitY, Vector2.Zero),
            new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.UnitY, Vector2.UnitX),
            new(new Vector3( 0.5f,  0.5f, -0.5f), Vector3.UnitY, Vector2.One),
            new(new Vector3(-0.5f,  0.5f, -0.5f), Vector3.UnitY, Vector2.UnitY),
            // Bottom face
            new(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitY, Vector2.Zero),
            new(new Vector3( 0.5f, -0.5f, -0.5f), -Vector3.UnitY, Vector2.UnitX),
            new(new Vector3( 0.5f, -0.5f,  0.5f), -Vector3.UnitY, Vector2.One),
            new(new Vector3(-0.5f, -0.5f,  0.5f), -Vector3.UnitY, Vector2.UnitY),
            // Right face
            new(new Vector3( 0.5f, -0.5f,  0.5f), Vector3.UnitX, Vector2.Zero),
            new(new Vector3( 0.5f, -0.5f, -0.5f), Vector3.UnitX, Vector2.UnitX),
            new(new Vector3( 0.5f,  0.5f, -0.5f), Vector3.UnitX, Vector2.One),
            new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.UnitX, Vector2.UnitY),
            // Left face
            new(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitX, Vector2.Zero),
            new(new Vector3(-0.5f, -0.5f,  0.5f), -Vector3.UnitX, Vector2.UnitX),
            new(new Vector3(-0.5f,  0.5f,  0.5f), -Vector3.UnitX, Vector2.One),
            new(new Vector3(-0.5f,  0.5f, -0.5f), -Vector3.UnitX, Vector2.UnitY),
        };

        var indices = new ushort[]
        {
            0,1,2, 0,2,3,       // front
            4,5,6, 4,6,7,       // back
            8,9,10, 8,10,11,    // top
            12,13,14, 12,14,15, // bottom
            16,17,18, 16,18,19, // right
            20,21,22, 20,22,23, // left
        };

        var vbo = Stride.Graphics.Buffer.Vertex.New(graphicsDevice, vertices);
        var ibo = Stride.Graphics.Buffer.Index.New(graphicsDevice, indices);

        return new Mesh
        {
            Draw = new MeshDraw
            {
                StartLocation = 0,
                PrimitiveType = PrimitiveType.TriangleList,
                VertexBuffers = new[] { new VertexBufferBinding(vbo, VertexPositionNormalTexture.Layout, vertices.Length) },
                IndexBuffer = new IndexBufferBinding(ibo, false, indices.Length),
            }
        };
    }

    private static Material CreateSimpleMaterial(GraphicsDevice graphicsDevice, Color4 color)
    {
        var descriptor = new MaterialDescriptor
        {
            Attributes = new MaterialAttributes
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor { Value = color }),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            }
        };
        return Material.New(graphicsDevice, descriptor);
    }
}
