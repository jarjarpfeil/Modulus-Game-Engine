// ModReassemblyHostApp.cs — Mod reassembly host with scene selection
// Loads mods and lets the user pick which scene to test them in

using Stride.Engine;
using Stride.Engine.Modding;
using Stride.Engine.Processors;
using Stride.Core.Mathematics;
using Stride.Core.Diagnostics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Input;
using SpaceEscape.Contracts;

namespace ModReassemblyHost;

public static class ModReassemblyHostApp
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModReassemblyHost");
    private static ModHost? s_modHost;
    private static int s_currentSceneIndex;
    private static readonly string[] s_sceneNames = { "Empty Scene", "Character Test", "Background Test", "Full Test" };

    public static int Main()
    {
        using var game = new Game();

        var modHost = game.Services.GetService<Stride.Engine.Modding.ModHost>();
        if (modHost == null)
        {
            Log.Error("ModHost not found — engine initialization failed");
            return 1;
        }

        s_modHost = modHost;
        modHost.ModsDirectory = Path.Combine(AppContext.BaseDirectory, "mods");
        game.Services.AddService<ISpaceEscapeHost>(new SpaceEscapeHostService());

        // Discover mods
        var discovered = modHost.DiscoverMods();
        Log.Info($"Discovered {discovered.Count} mods:");
        foreach (var pkg in discovered)
        {
            Log.Info($"  {pkg.Manifest.Id} v{pkg.Manifest.Version}");
        }

        // Load mods
        modHost.EnableStatePersistence();
        var loaded = modHost.LoadAllMods();
        Log.Info($"Loaded {loaded.Count} mods");

        // Subscribe to game started
        Game.GameStarted += (_, _) =>
        {
            LoadScene(game, s_currentSceneIndex);
            Log.Info("=== Controls ===");
            Log.Info("  F1-F4: Switch scenes");
            Log.Info("  Escape: Exit");
        };

        // Handle input for scene switching
        var input = game.Services.GetService<InputManager>();
        if (input != null)
        {
            // Scene switching handled in update loop
        }

        game.Run();
        return 0;
    }

    private static void LoadScene(Game game, int sceneIndex)
    {
        var sceneSystem = game.Services.GetService<SceneSystem>();
        if (sceneSystem == null) return;

        s_currentSceneIndex = sceneIndex;
        Scene scene;

        switch (sceneIndex)
        {
            case 0:
                scene = CreateEmptyScene();
                break;
            case 1:
                scene = CreateCharacterTestScene();
                break;
            case 2:
                scene = CreateBackgroundTestScene();
                break;
            case 3:
            default:
                scene = CreateFullTestScene();
                break;
        }

        var sceneInstance = new SceneInstance(game.Services, scene);
        sceneSystem.SceneInstance = sceneInstance;

        Log.Info($"Loaded scene: {s_sceneNames[sceneIndex]} ({scene.Entities.Count} entities)");
    }

    private static Scene CreateBaseScene()
    {
        var scene = new Scene();

        // Camera
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

        // Light
        var light = new Entity("Light")
        {
            new LightComponent
            {
                Type = new LightDirectional(),
                Intensity = 1.5f,
            },
        };
        light.Transform.Rotation = Quaternion.RotationX(-1.0f) * Quaternion.RotationY(0.5f);
        scene.Entities.Add(light);

        return scene;
    }

    private static Scene CreateEmptyScene()
    {
        return CreateBaseScene();
    }

    private static Scene CreateCharacterTestScene()
    {
        var scene = CreateBaseScene();

        // Character entity with mod component
        var character = new Entity("Character");
        character.Transform.Position = new Vector3(0, 1, 0);
        TryAddModComponent(character, "com.spaceescape.character", "ModCharacter.CharacterComponent");
        scene.Entities.Add(character);
        Log.Info("  Added Character with CharacterComponent");

        return scene;
    }

    private static Scene CreateBackgroundTestScene()
    {
        var scene = CreateBaseScene();

        // Background entity with mod component
        var background = new Entity("Background");
        background.Transform.Position = new Vector3(0, 0, -5);
        TryAddModComponent(background, "com.spaceescape.background", "ModBackground.BackgroundInfoComponent");
        scene.Entities.Add(background);
        Log.Info("  Added Background with BackgroundInfoComponent");

        return scene;
    }

    private static Scene CreateFullTestScene()
    {
        var scene = CreateBaseScene();

        // Character
        var character = new Entity("Character");
        character.Transform.Position = new Vector3(0, 1, 0);
        TryAddModComponent(character, "com.spaceescape.character", "ModCharacter.CharacterComponent");
        scene.Entities.Add(character);

        // Background
        var background = new Entity("Background");
        background.Transform.Position = new Vector3(0, 0, -5);
        TryAddModComponent(background, "com.spaceescape.background", "ModBackground.BackgroundInfoComponent");
        scene.Entities.Add(background);

        // UI
        var ui = new Entity("UI");
        ui.Transform.Position = Vector3.Zero;
        TryAddModComponent(ui, "com.spaceescape.ui", "ModUI.UIStateComponent");
        scene.Entities.Add(ui);

        Log.Info("  Added Character + Background + UI with mod components");

        return scene;
    }

    private static void TryAddModComponent(Entity entity, string modId, string typeName)
    {
        if (s_modHost == null) return;

        try
        {
            var mod = s_modHost.LoadedMods.Values.FirstOrDefault(m => m.Manifest.Id == modId);
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
        }
        catch (Exception ex)
        {
            Log.Error($"  Failed to add {typeName}: {ex.Message}");
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
