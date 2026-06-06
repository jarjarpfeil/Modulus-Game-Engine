// ModEntry.cs — Character mod entry point
// Tests: IMod lifecycle, IModSerializable state persistence, event bus subscriptions

using System;
using System.IO;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace ModCharacter;

public class ModEntry : IMod, IModSerializable
{
    public string Id => "com.spaceescape.character";
    public string Name => "SpaceEscape Character Mod";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0, 0);

    private IModContext? _context;
    private int _totalLaneChanges;
    private int _totalDeaths;
    private int _totalActivations;

    public void Initialize(IModContext context)
    {
        _context = context;

        // Register the event bus as a service so processors can access it
        // This is a test point: can mod-registered services be resolved by processors?
        try
        {
            context.Services.AddService<IModEventBus>(context.EventBus);
        }
        catch (Exception ex)
        {
            context.Logger.Warning($"[CharacterMod] Could not register event bus as service: {ex.Message}");
        }

        // Subscribe to game flow events from other mods
        context.EventBus.Subscribe<StartGameRequest>(_ => OnGameStart());
        context.EventBus.Subscribe<RestartGameRequest>(_ => OnGameRestart());
        context.EventBus.Subscribe<GoToMenuRequest>(_ => OnGoToMenu());

        // Subscribe to collision events from background mod
        context.EventBus.Subscribe<CollisionDetectedEvent>(_ => OnCollision());
        context.EventBus.Subscribe<HoleDetectedEvent>(evt => OnHoleDetected(evt.FloorHeight));

        context.Logger.Info("[CharacterMod] Initialized — listening for game events");
    }

    public void OnEnabled()
    {
        _context?.Logger.Info("[CharacterMod] Enabled");
    }

    public void OnDisabled()
    {
        _context?.Logger.Info("[CharacterMod] Disabled");
    }

    private void OnGameStart()
    {
        _totalActivations++;
        _context?.Logger.Info("[CharacterMod] Game started");
        // Find all character entities and activate them
        // (In a full implementation, we'd iterate entities with CharacterComponent)
    }

    private void OnGameRestart()
    {
        _context?.Logger.Info("[CharacterMod] Game restarted");
    }

    private void OnGoToMenu()
    {
        _context?.Logger.Info("[CharacterMod] Return to menu");
    }

    private void OnCollision()
    {
        _totalDeaths++;
        _context?.Logger.Info("[CharacterMod] Collision detected — character died");
    }

    private void OnHoleDetected(float floorHeight)
    {
        _totalDeaths++;
        _context?.Logger.Info($"[CharacterMod] Fell into hole at height {floorHeight}");
    }

    // ── IModSerializable — state persistence across unload/reload ──

    public void Save(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(_totalLaneChanges);
        writer.Write(_totalDeaths);
        writer.Write(_totalActivations);
    }

    public void Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        _totalLaneChanges = reader.ReadInt32();
        _totalDeaths = reader.ReadInt32();
        _totalActivations = reader.ReadInt32();
    }

    // ── Test Accessors ──

    public int TotalLaneChanges => _totalLaneChanges;
    public int TotalDeaths => _totalDeaths;
    public int TotalActivations => _totalActivations;
}
