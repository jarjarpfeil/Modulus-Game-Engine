// BackgroundInfoComponent.cs — Port of SpaceEscape's BackgroundInfo (ScriptComponent → EntityComponent)
// Tests: ScriptComponent adapter, DataContract across ALC

using System.Collections.Generic;
using Stride.Core;
using Stride.Engine;

namespace ModBackground;

/// <summary>Component attached to background entities. Replaces BackgroundInfo ScriptComponent.</summary>
[DataContract("BackgroundInfoComponent")]
public class BackgroundInfoComponent : EntityComponent
{
    public BackgroundInfoComponent()
    {
        Holes = new List<Hole>();
    }

    [DataMember(10)]
    public int MaxNbObstacles { get; set; }

    [DataMember(20)]
    public List<Hole> Holes { get; private set; }
}
