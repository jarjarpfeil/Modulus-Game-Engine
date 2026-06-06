// CharacterComponent.cs — Port of SpaceEscape's CharacterScript (AsyncScript → ECS component)
// Tests: DataContract serialization across ALC, entity component lifecycle, animation access

using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Games;
using Stride.Animations;

namespace ModCharacter;

public enum CharacterState
{
    Run,
    ChangeLaneLeft,
    ChangeLaneRight,
    Slide,
    Die,
}

public enum BoundingBoxKey
{
    Normal,
    Slide,
}

/// <summary>
/// Player character component. Ported from SpaceEscape's CharacterScript (AsyncScript).
/// This is a pure data component — all logic is in CharacterProcessor.
/// Tests: DataContract across ALC, DataMember ordering, IgnoreDataMember for transient state.
/// </summary>
[DataContract("CharacterComponent")]
[Display("Character Component")]
[DefaultEntityComponentProcessor(typeof(CharacterProcessor), ExecutionMode = ExecutionMode.Runtime)]
public class CharacterComponent : EntityComponent
{
    // ── Persistent State ──

    /// <summary>Current lane: 0=left, 1=center, 2=right</summary>
    [DataMember(10)]
    public int CurrentLane { get; set; } = 1;

    /// <summary>Whether the character is actively responding to input.</summary>
    [DataMember(20)]
    public bool IsActive { get; set; }

    /// <summary>Whether the character is dead.</summary>
    [DataMember(30)]
    public bool IsDead { get; set; }

    /// <summary>Y position of the lane (reset anchor).</summary>
    [DataMember(40)]
    public float LaneHeight { get; set; }

    /// <summary>Distance between lanes in world units.</summary>
    [DataMember(50)]
    public float LaneLength { get; set; } = 5.0f;

    /// <summary>Transparency of the shadow sprite (0=invisible, 1=opaque).</summary>
    [DataMember(60)]
    public float ShadowTransparency { get; set; } = 1.0f;

    // ── Transient State (not serialized) ──

    /// <summary>Current character state (Run, ChangeLane, Slide, Die).</summary>
    [DataMemberIgnore]
    public CharacterState State { get; set; } = CharacterState.Run;

    /// <summary>Start X position when begins a lane change.</summary>
    [DataMemberIgnore]
    public float LaneChangeStartX { get; set; }

    /// <summary>Target X position for the current lane change.</summary>
    [DataMemberIgnore]
    public float LaneChangeTargetX { get; set; }

    /// <summary>Currently playing animation (for progress tracking).</summary>
    [DataMemberIgnore]
    public PlayingAnimation PlayingAnimation { get; set; }

    /// <summary>Pre-computed bounding boxes for collision detection.</summary>
    [DataMemberIgnore]
    public Dictionary<BoundingBoxKey, BoundingBox> BoundingBoxes { get; set; } = new();

    /// <summary>Current active bounding box (Normal or Slide).</summary>
    [DataMemberIgnore]
    public BoundingBox ActiveBoundingBox { get; set; }

    /// <summary>Reference to the shadow SpriteComponent entity (child).</summary>
    [DataMemberIgnore]
    public Entity? ShadowEntity { get; set; }

    /// <summary>Entity this component is attached to (for transform access convenience).</summary>
    [DataMemberIgnore]
    public Entity CharacterEntity => Entity;

    /// <summary>Returns whether the character can accept input.</summary>
    public bool CanProcessInput => IsActive && !IsDead && (State == CharacterState.Run || State == CharacterState.Slide);

    /// <summary>Gets the world X position for a given lane index.</summary>
    public float GetLaneWorldX(int lane) => (1 - lane) * LaneLength;

    /// <summary>Calculates the current world-space bounding box.</summary>
    public BoundingBox CalculateWorldBoundingBox()
    {
        var pos = Entity.Transform.Position;
        return new BoundingBox(
            pos + ActiveBoundingBox.Minimum,
            pos + ActiveBoundingBox.Maximum
        );
    }
}
