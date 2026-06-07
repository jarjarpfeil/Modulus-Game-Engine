// ModReassemblyHostApp.cs — Self-contained game with mod loading and visible graphics

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
                // Create scene FIRST so EntityProcessors can register
                CreateVisibleScene(game, modHost);

                modHost.EnableStatePersistence();
                var loaded = modHost.LoadAllMods();

                Log.Info($"Loaded {loaded.Count} mods:");
                foreach (var pkg in loaded)
                {
                    Log.Info($"  {pkg.Manifest.Id} v{pkg.Manifest.Version} - State: {pkg.State}");
                }

                Log.Info("=== Mod Reassembly Running ===");
                Log.Info("Game is running with mods loaded and visible graphics.");
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

    private static void CreateVisibleScene(Game game, ModHost modHost)
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
        camera.Transform.Position = new Vector3(0, 3, 8);
        camera.Transform.Rotation = Quaternion.RotationX(-0.3f);
        scene.Entities.Add(camera);
        Log.Info("  Added Camera");

        // ── Directional Light ──
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
        Log.Info("  Added Light");

        // ── Character entity with mod component ──
        var character = new Entity("Character");
        character.Transform.Position = new Vector3(0, 1, 0);
        character.Transform.Scale = new Vector3(1, 2, 1);
        TryAddModComponent(character, modHost, "com.spaceescape.character", "ModCharacter.CharacterComponent");
        scene.Entities.Add(character);
        Log.Info("  Added Character with mod component");

        // ── Background entity with mod component ──
        var background = new Entity("Background");
        background.Transform.Position = new Vector3(0, 2, -5);
        background.Transform.Scale = new Vector3(20, 10, 1);
        TryAddModComponent(background, modHost, "com.spaceescape.background", "ModBackground.BackgroundInfoComponent");
        scene.Entities.Add(background);
        Log.Info("  Added Background with mod component");

        // ── UI entity with mod component ──
        var ui = new Entity("UI");
        ui.Transform.Position = Vector3.Zero;
        TryAddModComponent(ui, modHost, "com.spaceescape.ui", "ModUI.UIStateComponent");
        scene.Entities.Add(ui);
        Log.Info("  Added UI with mod component");

        Log.Info($"Scene has {scene.Entities.Count} entities");
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
                Log.Warning($"  Type '{typeName}' not found");
                return;
            }

            var component = Activator.CreateInstance(componentType);
            if (component == null) return;

            entity.Add((EntityComponent)component);
            Log.Info($"  Added {typeName}");
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
