// ModEntry.cs — UI mod entry point
// Tests: IMod lifecycle, event bus subscriptions, UI event publishing

using System;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace ModUI;

public class ModEntry : IMod
{
    public string Id => "com.spaceescape.ui";
    public string Name => "SpaceEscape UI Mod";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0, 0);

    private IModContext? _context;
    private int _startGameCount;
    private int _restartCount;
    private int _menuCount;

    public void Initialize(IModContext context)
    {
        _context = context;

        try
        {
            context.Services.AddService<IModEventBus>(context.EventBus);
        }
        catch (Exception ex)
        {
            context.Logger.Warning($"[UIMod] Could not register event bus: {ex.Message}");
        }

        // Subscribe to game events
        context.EventBus.Subscribe<PlayerDiedEvent>(_ => OnPlayerDied());
        context.EventBus.Subscribe<DistanceUpdatedEvent>(evt => OnDistanceUpdated(evt.Distance));
        context.EventBus.Subscribe<GameStateChanged>(evt => OnGameStateChanged(evt.Current));

        context.Logger.Info("[UIMod] Initialized");
    }

    public void OnEnabled() => _context?.Logger.Info("[UIMod] Enabled");
    public void OnDisabled() => _context?.Logger.Info("[UIMod] Disabled");

    private void OnPlayerDied()
    {
        _context?.Logger.Info("[UIMod] Player died — showing game over");
    }

    private void OnDistanceUpdated(float distance)
    {
        // Distance update received — UI processor will update the text
    }

    private void OnGameStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Playing: _startGameCount++; break;
            case GameState.GameOver: _restartCount++; break;
            case GameState.Menu: _menuCount++; break;
        }
    }

    public int StartGameCount => _startGameCount;
    public int RestartCount => _restartCount;
    public int MenuCount => _menuCount;
}
