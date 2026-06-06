// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Games;

namespace TestComponentAdder;

/// <summary>
/// Test ECS component that rotates an entity around an axis.
/// Proves that mods can define new EntityComponent types that are
/// serializable and participate in the ECS pipeline.
/// </summary>
[DefaultEntityComponentProcessor(typeof(RotatingProcessor), ExecutionMode = ExecutionMode.Runtime)]
[DataContract("RotatingComponent")]
[Display("Rotating Component")]
public class RotatingComponent : EntityComponent
{
    /// <summary>Rotation speed in degrees per second.</summary>
    [DataMember(10)]
    public float Speed { get; set; } = 45.0f;

    /// <summary>Axis of rotation.</summary>
    [DataMember(20)]
    public Vector3 Axis { get; set; } = Vector3.UnitY;

    /// <summary>Whether rotation is currently active.</summary>
    [DataMember(30)]
    public bool IsActive { get; set; } = true;

    /// <summary>Accumulated rotation angle (degrees) — for testing state persistence.</summary>
    [DataMemberIgnore]
    public float TotalRotation { get; set; }
}

/// <summary>
/// EntityProcessor that updates RotatingComponent instances each frame.
/// Proves that mods can register new ECS systems (processors).
/// </summary>
public class RotatingProcessor : EntityProcessor<RotatingComponent>
{
    public override void Update(GameTime time)
    {
        var deltaTime = (float)time.Elapsed.TotalSeconds;

        // ComponentDatas maps TComponent → TComponent (key == value)
        // Entity is accessed via component.Entity
        foreach (var entry in ComponentDatas)
        {
            var component = entry.Key;
            var entity = component.Entity;

            if (!component.IsActive)
                continue;

            var angleDeg = component.Speed * deltaTime;
            component.TotalRotation += angleDeg;

            var rotationDelta = Quaternion.RotationAxis(component.Axis, MathUtil.DegreesToRadians(angleDeg));
            entity.Transform.Rotation = rotationDelta * entity.Transform.Rotation;
        }
    }
}
