// ModReassemblyHostApp.cs — Full game with visible meshes and mod components

using Stride.Engine;
using Stride.Engine.Modding;
using Stride.Engine.Processors;
using Stride.Core.Mathematics;
using Stride.Core.Diagnostics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Graphics;
using SpaceEscape.Contracts;

namespace ModReassemblyHost;

public static class ModReassemblyHostApp
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModReassemblyHost");

    public static int Main()
    {
        using var game = new Game();

        var modHost = game.Services.GetService<ModHost>();
        if (modHost == null)
            throw new InvalidOperationException("ModHost not found");

        modHost.ModsDirectory = Path.Combine(AppContext.BaseDirectory, "mods");
        game.Services.AddService<ISpaceEscapeHost>(new SpaceEscapeHostService());

        // Create a minimal scene with a bootstrap script
        // The script runs on the first Update() when GraphicsDevice exists
        var bootstrapScene = new Scene();
        var bootstrapEntity = new Entity("Bootstrap");
        bootstrapEntity.Add(new SceneBootstrapScript());
        bootstrapScene.Entities.Add(bootstrapEntity);
        game.SceneSystem.SceneInstance = new SceneInstance(game.Services, bootstrapScene);

        game.Run();
        return 0;
    }
}

/// <summary>
/// SyncScript that runs once on first Update to load mods and create the visible scene.
/// Attached to a root entity created before game.Run().
/// </summary>
public class SceneBootstrapScript : SyncScript
{
    private bool _initialized;

    public override void Update()
    {
        if (_initialized) return;
        _initialized = true;

        var modHost = Services.GetService<ModHost>();
        var graphicsDevice = Game.GraphicsDevice;
        var logger = GlobalLogger.GetLogger("ModReassemblyHost");

        if (modHost == null || graphicsDevice == null)
        {
            logger.Error("ModHost or GraphicsDevice not available");
            return;
        }

        modHost.EnableStatePersistence();
        var loaded = modHost.LoadAllMods();
        logger.Info($"Loaded {loaded.Count} mods:");
        foreach (var pkg in loaded)
            logger.Info($"  {pkg.Manifest.Id} v{pkg.Manifest.Version} - State: {pkg.State}");

        CreateVisibleScene(modHost, graphicsDevice, logger);
        logger.Info("=== Mod Reassembly Running ===");
        logger.Info("Game window should show colored cubes with mod components attached.");
    }

    private void CreateVisibleScene(ModHost modHost, GraphicsDevice graphicsDevice, Logger logger)
    {
        var scene = Entity.Scene;

        // ── Camera ──
        var camera = new Entity("Camera");
        camera.Transform.Position = new Vector3(0, 4, 8);
        camera.Transform.Rotation = Quaternion.RotationX(-0.35f);
        camera.Add(new CameraComponent(0.1f, 1000f)
        {
            Projection = CameraProjectionMode.Perspective,
            VerticalFieldOfView = 55f,
        });
        scene.Entities.Add(camera);

        // ── Directional Light ──
        var light = new Entity("Light");
        light.Transform.Rotation = Quaternion.RotationX(-1.0f) * Quaternion.RotationY(0.5f);
        light.Add(new LightComponent { Type = new LightDirectional(), Intensity = 2.0f });
        scene.Entities.Add(light);

        // ── Ground (green cube, flat) ──
        AddColoredCube(scene, "Ground", new Vector3(0, -0.5f, 0), new Vector3(10, 1, 10),
            new Color4(0.3f, 0.7f, 0.3f, 1f), graphicsDevice);

        // ── Character (red cube, tall) ──
        var character = AddColoredCube(scene, "Character", new Vector3(0, 1, 0), new Vector3(1, 2, 1),
            new Color4(0.9f, 0.2f, 0.2f, 1f), graphicsDevice);
        TryAddModComponent(character, modHost, "com.spaceescape.character", "ModCharacter.CharacterComponent", logger);

        // ── Background wall (blue, wide) ──
        var background = AddColoredCube(scene, "Background", new Vector3(0, 2, -6), new Vector3(16, 6, 0.5f),
            new Color4(0.15f, 0.25f, 0.6f, 1f), graphicsDevice);
        TryAddModComponent(background, modHost, "com.spaceescape.background", "ModBackground.BackgroundInfoComponent", logger);

        // ── Obstacle 1 (orange) ──
        AddColoredCube(scene, "Obstacle1", new Vector3(-3, 1, -3), new Vector3(1.5f, 2, 1.5f),
            new Color4(1f, 0.5f, 0.1f, 1f), graphicsDevice);

        // ── Obstacle 2 (yellow) ──
        AddColoredCube(scene, "Obstacle2", new Vector3(3, 0.75f, -2), new Vector3(1.5f, 1.5f, 1.5f),
            new Color4(1f, 0.9f, 0.1f, 1f), graphicsDevice);

        // ── UI entity ──
        var ui = new Entity("UI");
        TryAddModComponent(ui, modHost, "com.spaceescape.ui", "ModUI.UIStateComponent", logger);
        scene.Entities.Add(ui);

        logger.Info($"Scene ready: {scene.Entities.Count} entities");
    }

    private static Entity AddColoredCube(Scene scene, string name, Vector3 position, Vector3 scale,
        Color4 color, GraphicsDevice graphicsDevice)
    {
        var model = new Model();
        model.Meshes.Add(CreateCubeMesh(graphicsDevice));
        model.Materials.Add(new MaterialInstance(CreateMaterial(graphicsDevice, color)));

        var entity = new Entity(name);
        entity.Transform.Position = position;
        entity.Transform.Scale = scale;
        entity.Add(new ModelComponent(model));
        scene.Entities.Add(entity);
        return entity;
    }

    private static Mesh CreateCubeMesh(GraphicsDevice graphicsDevice)
    {
        var vertices = new VertexPositionNormalTexture[]
        {
            new(new Vector3(-0.5f, -0.5f,  0.5f), Vector3.UnitZ, Vector2.Zero),
            new(new Vector3( 0.5f, -0.5f,  0.5f), Vector3.UnitZ, Vector2.UnitX),
            new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.UnitZ, Vector2.One),
            new(new Vector3(-0.5f,  0.5f,  0.5f), Vector3.UnitZ, Vector2.UnitY),
            new(new Vector3( 0.5f, -0.5f, -0.5f), -Vector3.UnitZ, Vector2.Zero),
            new(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitZ, Vector2.UnitX),
            new(new Vector3(-0.5f,  0.5f, -0.5f), -Vector3.UnitZ, Vector2.One),
            new(new Vector3( 0.5f,  0.5f, -0.5f), -Vector3.UnitZ, Vector2.UnitY),
            new(new Vector3(-0.5f,  0.5f,  0.5f), Vector3.UnitY, Vector2.Zero),
            new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.UnitY, Vector2.UnitX),
            new(new Vector3( 0.5f,  0.5f, -0.5f), Vector3.UnitY, Vector2.One),
            new(new Vector3(-0.5f,  0.5f, -0.5f), Vector3.UnitY, Vector2.UnitY),
            new(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitY, Vector2.Zero),
            new(new Vector3( 0.5f, -0.5f, -0.5f), -Vector3.UnitY, Vector2.UnitX),
            new(new Vector3( 0.5f, -0.5f,  0.5f), -Vector3.UnitY, Vector2.One),
            new(new Vector3(-0.5f, -0.5f,  0.5f), -Vector3.UnitY, Vector2.UnitY),
            new(new Vector3( 0.5f, -0.5f,  0.5f), Vector3.UnitX, Vector2.Zero),
            new(new Vector3( 0.5f, -0.5f, -0.5f), Vector3.UnitX, Vector2.UnitX),
            new(new Vector3( 0.5f,  0.5f, -0.5f), Vector3.UnitX, Vector2.One),
            new(new Vector3( 0.5f,  0.5f,  0.5f), Vector3.UnitX, Vector2.UnitY),
            new(new Vector3(-0.5f, -0.5f, -0.5f), -Vector3.UnitX, Vector2.Zero),
            new(new Vector3(-0.5f, -0.5f,  0.5f), -Vector3.UnitX, Vector2.UnitX),
            new(new Vector3(-0.5f,  0.5f,  0.5f), -Vector3.UnitX, Vector2.One),
            new(new Vector3(-0.5f,  0.5f, -0.5f), -Vector3.UnitX, Vector2.UnitY),
        };

        var indices = new ushort[]
        {
            0,1,2, 0,2,3, 4,5,6, 4,6,7, 8,9,10, 8,10,11,
            12,13,14, 12,14,15, 16,17,18, 16,18,19, 20,21,22, 20,22,23,
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

    private static Material CreateMaterial(GraphicsDevice graphicsDevice, Color4 color)
    {
        var diffuseColor = new ComputeColor { Value = color };
        var descriptor = new MaterialDescriptor
        {
            Attributes = new MaterialAttributes
            {
                Diffuse = new MaterialDiffuseMapFeature(diffuseColor),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            }
        };
        return Material.New(graphicsDevice, descriptor);
    }

    private static void TryAddModComponent(Entity entity, ModHost modHost, string modId, string typeName, Logger logger)
    {
        try
        {
            var mod = modHost.LoadedMods.Values.FirstOrDefault(m => m.Manifest.Id == modId);
            if (mod?.ModAssembly == null) { logger.Warning($"  Mod '{modId}' not found"); return; }

            var componentType = mod.ModAssembly.GetType(typeName);
            if (componentType == null) { logger.Warning($"  Type '{typeName}' not found"); return; }

            var component = Activator.CreateInstance(componentType);
            if (component == null) return;

            entity.Add((EntityComponent)component);
            logger.Info($"  Added {typeName} to {entity.Name}");
        }
        catch (Exception ex) { logger.Error($"  Failed to add {typeName}: {ex.Message}"); }
    }
}

internal class SpaceEscapeHostService : ISpaceEscapeHost
{
    public GameState CurrentState { get; private set; } = GameState.Menu;
    public void RequestStateChange(GameState newState)
    {
        CurrentState = newState;
        GlobalLogger.GetLogger("ModReassemblyHost").Info($"Game state: {newState}");
    }
}
