// Section.cs — Port of SpaceEscape's Section.cs
// Tests: Entity hierarchy management, bounding box calculations, data structures across ALC

using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace ModBackground;

/// <summary>Represents a section of background containing entities, obstacles, and holes.</summary>
public class Section
{
    public float Length { get; private set; }
    public Entity RootEntity { get; private set; }
    public List<Obstacle> CollidableObstacles { get; private set; }
    public List<Hole> Holes { get; private set; }

    private float positionZ;

    public Section()
    {
        RootEntity = new Entity();
        CollidableObstacles = new List<Obstacle>();
        Holes = new List<Hole>();
    }

    public float PositionZ
    {
        get => positionZ;
        set
        {
            positionZ = value;
            RootEntity.Transform.Position.Z = value;
        }
    }

    public Section AddBackgroundEntity(Entity backgroundEntity)
    {
        RootEntity.AddChild(backgroundEntity);
        var model = backgroundEntity.Get<ModelComponent>();
        if (model != null)
        {
            var bb = model.Model.BoundingBox;
            Length += bb.Maximum.Z - bb.Minimum.Z;
        }
        return this;
    }

    public Section AddObstacleEntity(Entity obstacleEntity, bool useSubBoundingBoxes)
    {
        RootEntity.AddChild(obstacleEntity);
        var obstacle = new Obstacle { Entity = obstacleEntity };
        var modelComp = obstacleEntity.Get<ModelComponent>();

        if (useSubBoundingBoxes && modelComp != null)
        {
            foreach (var mesh in modelComp.Model.Meshes)
            {
                var bb = mesh.BoundingBox;
                var nodeIndex = mesh.NodeIndex;
                while (nodeIndex >= 0)
                {
                    var node = modelComp.Model.Skeleton.Nodes[nodeIndex];
                    var transform = node.Transform;
                    var matrix = Matrix.Transformation(
                        Vector3.Zero, Quaternion.Identity, transform.Scale,
                        Vector3.Zero, transform.Rotation, transform.Position);
                    Vector3.TransformNormal(ref bb.Minimum, ref matrix, out bb.Minimum);
                    Vector3.TransformNormal(ref bb.Maximum, ref matrix, out bb.Maximum);
                    nodeIndex = node.ParentIndex;
                }
                obstacle.BoundingBoxes.Add(bb);
            }
        }
        else if (modelComp != null)
        {
            obstacle.BoundingBoxes.Add(modelComp.Model.BoundingBox);
        }

        CollidableObstacles.Add(obstacle);
        return this;
    }

    public Section AddHoleRange(List<Hole>? holes)
    {
        if (holes != null)
            Holes.AddRange(holes);
        return this;
    }
}
