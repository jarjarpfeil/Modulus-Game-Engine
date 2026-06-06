// ObstacleInfoComponent.cs — Port of SpaceEscape's ObstacleInfo (ScriptComponent → EntityComponent)
// Tests: ScriptComponent adapter

using Stride.Core;
using Stride.Engine;

namespace ModBackground;

/// <summary>Component attached to obstacle entities. Replaces ObstacleInfo ScriptComponent.</summary>
[DataContract("ObstacleInfoComponent")]
public class ObstacleInfoComponent : EntityComponent
{
    [DataMember(10)]
    public bool UseSubMeshBoundingBoxes { get; set; }
}
