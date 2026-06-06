// Obstacle.cs — Port of SpaceEscape's Obstacle.cs
// Tests: Pure data class across ALC

using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace ModBackground;

/// <summary>Collidable obstacle data.</summary>
public class Obstacle
{
    public List<BoundingBox> BoundingBoxes { get; set; } = new();
    public Entity? Entity { get; set; }
}
