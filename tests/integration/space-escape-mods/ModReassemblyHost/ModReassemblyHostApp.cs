// ModReassemblyHostApp.cs — Minimal Stride host that loads all SpaceEscape mods
// Tests: Full end-to-end mod loading, dependency resolution, game functionality

using Stride.Engine;
using Stride.Engine.Modding;
using SpaceEscape.Contracts;

namespace ModReassemblyHost;

public static class ModReassemblyHostApp
{
    public static void Main()
    {
        using var game = new Game();

        // Game() constructor already creates and registers ModHost (line 246-247 of Game.cs)
        var modHost = game.Services.GetService<Stride.Engine.Modding.ModHost>();
        if (modHost == null)
        {
            throw new InvalidOperationException("ModHost not found — Game() constructor should have created it");
        }

        modHost.ModsDirectory = Path.Combine(AppContext.BaseDirectory, "mods");

        // Register the host service for mods to use
        game.Services.AddService<ISpaceEscapeHost>(new SpaceEscapeHostService());

        // Load all mods after game initialization completes
        Game.GameStarted += (_, _) =>
        {
            modHost.EnableStatePersistence();

            var loaded = modHost.LoadAllMods();

            var logger = Stride.Core.Diagnostics.GlobalLogger.GetLogger("ModReassemblyHost");
            logger.Info($"Loaded {loaded.Count} mods:");
            foreach (var pkg in loaded)
            {
                logger.Info($"  {pkg.Manifest.Id} v{pkg.Manifest.Version} - State: {pkg.State}");
            }
        };

        game.Run();
    }
}

/// <summary>Simple implementation of ISpaceEscapeHost for mods to use.</summary>
internal class SpaceEscapeHostService : ISpaceEscapeHost
{
    public GameState CurrentState { get; private set; } = GameState.Menu;

    public void RequestStateChange(GameState newState)
    {
        var previous = CurrentState;
        CurrentState = newState;
        var logger = Stride.Core.Diagnostics.GlobalLogger.GetLogger("ModReassemblyHost");
        logger.Info($"Game state: {previous} -> {newState}");
    }
}
