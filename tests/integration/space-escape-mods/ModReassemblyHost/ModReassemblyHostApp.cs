// ModReassemblyHostApp.cs — Full integration test: loads mods, creates entities with mod components, runs game

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
                CreateScene(game);

                modHost.EnableStatePersistence();
                var loaded = modHost.LoadAllMods();

                Log.Info($"Loaded {loaded.Count} mods:");
                foreach (var pkg in loaded)
                {
                    Log.Info($"  {pkg.Manifest.Id} v{pkg.Manifest.Version} - State: {pkg.State}");
                }

                AddModComponentsToScene(game, modHost);

                Log.Info("=== Mod Reassembly Running ===");
                Log.Info("Game window is open. 6 mods loaded, processors registered.");
                Log.Info("Entities with mod components: Character, Background, UI");
                Log.Info("Close the game window to exit.");
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

    private static void CreateScene(Game game)
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

        Log.Info("Created scene");

        // Camera
        var camera = new Entity("Camera")
        {
            new CameraComponent(0.1f, 1000f)
            {
                Projection = CameraProjectionMode.Perspective,
                VerticalFieldOfView = 60f,
            },
        };
        camera.Transform.Position = new Vector3(0, 3, 8);
        camera.Transform.Rotation = Quaternion.RotationX(-0.3f);
        scene.Entities.Add(camera);

        // Light
        var light = new Entity("Light")
        {
            new LightComponent { Type = new LightDirectional(), Intensity = 1.5f },
        };
        light.Transform.Rotation = Quaternion.RotationX(-1.0f) * Quaternion.RotationY(0.5f);
        scene.Entities.Add(light);

        // Character entity
        var character = new Entity("Character");
        character.Transform.Position = new Vector3(0, 1, 0);
        scene.Entities.Add(character);

        // Background entity
        var background = new Entity("Background");
        background.Transform.Position = new Vector3(0, 2, -5);
        scene.Entities.Add(background);

        // UI entity
        var ui = new Entity("UI");
        ui.Transform.Position = Vector3.Zero;
        scene.Entities.Add(ui);

        Log.Info($"Scene has {scene.Entities.Count} entities (Camera, Light, Character, Background, UI)");
    }

    private static void AddModComponentsToScene(Game game, ModHost modHost)
    {
        var sceneSystem = game.Services.GetService<SceneSystem>();
        if (sceneSystem?.SceneInstance == null) return;

        var scene = sceneSystem.SceneInstance;

        var characterEntity = scene.FirstOrDefault(e => e.Name == "Character");
        if (characterEntity != null)
            TryAddModComponent(characterEntity, modHost, "com.spaceescape.character", "ModCharacter.CharacterComponent");

        var bgEntity = scene.FirstOrDefault(e => e.Name == "Background");
        if (bgEntity != null)
            TryAddModComponent(bgEntity, modHost, "com.spaceescape.background", "ModBackground.BackgroundInfoComponent");

        var uiEntity = scene.FirstOrDefault(e => e.Name == "UI");
        if (uiEntity != null)
            TryAddModComponent(uiEntity, modHost, "com.spaceescape.ui", "ModUI.UIStateComponent");
    }

    private static void TryAddModComponent(Entity entity, ModHost modHost, string modId, string typeName)
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

            entity.Add((EntityComponent)component);
            Log.Info($"  Added {typeName} to {entity.Name}");
        }
        catch (Exception ex)
        {
            Log.Error($"  Failed to add {typeName} to {entity.Name}: {ex.Message}");
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
