// Hole.cs — Port of SpaceEscape's Hole.cs
// Tests: DataContract serialization across ALC boundary with alias "BackgroundElement"

using Stride.Core;
using Stride.Core.Mathematics;

namespace ModBackground;

/// <summary>Data for a hole in the background. DataContract alias must match scene files.</summary>
[DataContract("BackgroundElement")]
public class Hole
{
    [DataMember(10)]
    public RectangleF Area { get; set; }

    [DataMember(20)]
    public float Height { get; set; }
}
