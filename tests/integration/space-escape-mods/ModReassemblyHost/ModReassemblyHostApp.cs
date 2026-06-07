// ModReassemblyHostApp.cs — Full integration test: loads mods, creates test scene, runs game with visible window

using Stride.Engine;
using Stride.Engine.Modding;
using Stride.Engine.Processors;
using Stride.Core.Mathematics;
using Stride.Core.Diagnostics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using SpaceEscape.Contracts;

namespace ModReassemblyHost;

public static class ModReassemblyHostApp
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModReassemblyHost");

    public static int Main()
    {
        using var game = new Game();

        var modHost = game.Services.GetService<Stride.Engine.Modding.ModHost>();
        if (modHost == null)
        {
            throw new InvalidOperationException("ModHost not found");
        }

        modHost.ModsDirectory = Path.Combine(AppContext.BaseDirectory, "mods");
        game.Services.AddService<ISpaceEscapeHost>(new SpaceEscapeHostService());

        int exitCode = 0;

        Game.GameStarted += (_, _) =>
        {
            try
            {
                modHost.EnableStatePersistence();
                var loaded = modHost.LoadAllMods();

                Log.Info($"Loaded {loaded.Count} mods:");
                foreach (var pkg in loaded)
                {
                    Log.Info($"  {pkg.Manifest.Id} v{pkg.Manifest.Version} - State: {pkg.State}");
                }

                // Create test scene with entities that use mod components
                CreateTestScene(game, modHost);

                Log.Info("=== Mod Reassembly Test Complete ===");
                Log.Info("All mods loaded and test scene created successfully.");
                Log.Info("The game window should be visible. Close it to exit.");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed: {ex}");
                exitCode = 1;
                game.Exit();
            }
        };

        game.Run();
        return exitCode;
    }

    private static void CreateTestScene(Game game, ModHost modHost)
    {
        var sceneSystem = game.Services.GetService<SceneSystem>();
        if (sceneSystem == null)
        {
            Log.Error("SceneSystem not available");
            return;
        }

        var scene = new Scene();
        var sceneInstance = new SceneInstance(game.Services, scene);
        sceneSystem.SceneInstance = sceneInstance;

        Log.Info("Created test scene");

        // ── Camera ──
        var camera = new Entity("Camera")
        {
            new CameraComponent(0.1f, 1000f)
            {
                Projection = CameraProjectionMode.Perspective,
                VerticalFieldOfView = 60f,
            },
        };
        camera.Transform.Position = new Vector3(0, 5, 10);
        camera.Transform.Rotation = Quaternion.RotationX(-0.2f);
        scene.Entities.Add(camera);
        Log.Info("  Added Camera");

        // ── Directional Light ──
        var light = new Entity("Light")
        {
            new LightComponent
            {
                Type = new LightDirectional(),
                Intensity = 1.0f,
            },
        };
        light.Transform.Rotation = Quaternion.RotationX(-0.8f) * Quaternion.RotationY(0.5f);
        scene.Entities.Add(light);
        Log.Info("  Added Light");

        // ── Character entity with mod component ──
        TryAddModComponent(scene, modHost, "com.spaceescape.character",
            "ModCharacter.CharacterComponent", "Character", new Vector3(0, 0, 0));

        // ── Background entity with mod component ──
        TryAddModComponent(scene, modHost, "com.spaceescape.background",
            "ModBackground.BackgroundInfoComponent", "Background", new Vector3(0, -2, 0));

        // ── UI entity with mod component ──
        TryAddModComponent(scene, modHost, "com.spaceescape.ui",
            "ModUI.UIStateComponent", "UI", Vector3.Zero);

        Log.Info($"Scene has {scene.Entities.Count} entities");
    }

    private static void TryAddModComponent(Scene scene, ModHost modHost,
        string modId, string typeName, string entityName, Vector3 position)
    {
        try
        {
            var mod = modHost.LoadedMods.Values.FirstOrDefault(m => m.Manifest.Id == modId);
            if (mod?.ModAssembly == null)
            {
                Log.Warning($"  Mod '{modId}' not found");
                return;
            }

            var componentType = mod.ModAssembly.GetType(typeName);
            if (componentType == null)
            {
                Log.Warning($"  Type '{typeName}' not found in mod '{modId}'");
                return;
            }

            var component = Activator.CreateInstance(componentType);
            if (component == null) return;

            var entity = new Entity(entityName);
            entity.Transform.Position = position;
            entity.Add((EntityComponent)component);
            scene.Entities.Add(entity);

            Log.Info($"  Added {entityName} with {typeName}");
        }
        catch (Exception ex)
        {
            Log.Error($"  Failed to add {entityName}: {ex.Message}");
        }
    }
}

internal class SpaceEscapeHostService : ISpaceEscapeHost
{
    public GameState CurrentState { get; private set; } = GameState.Menu;

    public void RequestStateChange(GameState newState)
    {
        var previous = CurrentState;
        CurrentState = newState;
        GlobalLogger.GetLogger("ModReassemblyHost").Info($"Game state: {previous} -> {newState}");
    }
}
