// SpaceEscape Inter-Mod Contracts
// These types are shared between mods via a shared assembly (not ALC-isolated).
// All mods reference this project, and the engine treats it as a shared dependency.

using System;
using System.IO;
using Modulus.Modding.Api;

namespace SpaceEscape.Contracts;

// ── Game State ──

public enum GameState { Menu, Playing, GameOver }

// ── Events (published/subscribed via IModEventBus) ──

/// <summary>Published when game state changes. Source: orchestrator or UI.</summary>
public record GameStateChanged(GameState Previous, GameState Current);

/// <summary>Published when player dies. Source: character mod. Consumed: orchestrator, UI.</summary>
public record PlayerDiedEvent(float FloorHeight);

/// <summary>Published when distance updates. Source: background mod. Consumed: UI mod.</summary>
public record DistanceUpdatedEvent(float Distance);

/// <summary>Published when player changes lane. Source: character mod. Consumed: orchestrator.</summary>
public record LaneChangedEvent(int NewLane);

/// <summary>Request to start the game. Source: UI mod. Consumed: character, background.</summary>
public record StartGameRequest;

/// <summary>Request to restart the game. Source: UI mod. Consumed: character, background.</summary>
public record RestartGameRequest;

/// <summary>Request to go to menu. Source: UI mod. Consumed: character, background.</summary>
public record GoToMenuRequest;

/// <summary>Published when collision detected between character and obstacle. Source: background mod. Consumed: orchestrator.</summary>
public record CollisionDetectedEvent;

/// <summary>Published when character falls into a hole. Source: background mod. Consumed: orchestrator.</summary>
public record HoleDetectedEvent(float FloorHeight);

/// <summary>Published by orchestrator to trigger game-over sequence. Consumed: UI mod.</summary>
public record GameOverTriggered(float FinalDistance);

/// <summary>Published by orchestrator to trigger main-menu sequence. Consumed: UI mod.</summary>
public record MainMenuTriggered;

// ── Service Interfaces ──

/// <summary>Service that the host provides to mods for game-level coordination.
/// Registered by the host project before mods are loaded.</summary>
public interface ISpaceEscapeHost
{
    GameState CurrentState { get; }
    void RequestStateChange(GameState newState);
}

// ── Shared Data Contracts (for serialization across ALC boundary) ──

/// <summary>Data for a hole in the background. Must match the DataContract alias
/// used in scene files so the serializer can resolve it across ALC boundaries.</summary>
[Stride.Core.DataContract("BackgroundElement")]
public class HoleData
{
    [Stride.Core.DataMember(10)]
    public Stride.Core.Mathematics.RectangleF Area { get; set; }

    [Stride.Core.DataMember(20)]
    public float Height { get; set; }
}
