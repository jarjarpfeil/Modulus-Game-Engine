// ModEntry.cs — Background mod entry point
// Tests: IMod lifecycle, dependency on character mod, event bus subscriptions

using System;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace ModBackground;

public class ModEntry : IMod
{
    public string Id => "com.spaceescape.background";
    public string Name => "SpaceEscape Background Mod";
    public Version Version => new(1, 0, 0);
    public Version MinApiVersion => new(1, 0, 0);

    private IModContext? _context;
    private int _startCount;
    private int _resetCount;

    public void Initialize(IModContext context)
    {
        _context = context;

        // Register event bus as service for processors
        try
        {
            context.Services.AddService<IModEventBus>(context.EventBus);
        }
        catch (Exception ex)
        {
            context.Logger.Warning($"[BackgroundMod] Could not register event bus: {ex.Message}");
        }

        context.EventBus.Subscribe<StartGameRequest>(_ => OnGameStart());
        context.EventBus.Subscribe<RestartGameRequest>(_ => OnGameRestart());
        context.EventBus.Subscribe<GoToMenuRequest>(_ => OnGoToMenu());

        context.Logger.Info("[BackgroundMod] Initialized");
    }

    public void OnEnabled() => _context?.Logger.Info("[BackgroundMod] Enabled");
    public void OnDisabled() => _context?.Logger.Info("[BackgroundMod] Disabled");

    private void OnGameStart()
    {
        _startCount++;
        _context?.Logger.Info("[BackgroundMod] Game started — scrolling");
    }

    private void OnGameRestart()
    {
        _resetCount++;
        _context?.Logger.Info("[BackgroundMod] Game restarted — reset");
    }

    private void OnGoToMenu()
    {
        _context?.Logger.Info("[BackgroundMod] Return to menu");
    }

    public int StartCount => _startCount;
    public int ResetCount => _resetCount;
}
