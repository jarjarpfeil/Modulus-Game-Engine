// BackgroundProcessor.cs — Port of SpaceEscape's BackgroundScript (SyncScript → EntityProcessor)
// Tests: Scene access via services, entity hierarchy, collision detection, event bus publishing

using System;
using System.Collections.Generic;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;

namespace ModBackground;

/// <summary>
/// Processor that manages scrolling background sections, collision detection, and hole detection.
/// Ported from SpaceEscape's BackgroundScript (SyncScript).
/// </summary>
public class BackgroundProcessor : EntityProcessor<BackgroundInfoComponent>
{
    private IModEventBus? _eventBus;
    private SceneSystem? _sceneSystem;
    private bool _initialized;

    private const float GameSpeed = 30f;
    private const float RemoveBlockPosition = -16f;
    private const float AddBlockPosition = 280f;

    private readonly List<Section> _levelBlocks = new();
    private float _runningDistance;
    private bool _isScrolling;
    private Vector4 _skyplaneUVRegion = new(0f, 0f, 1f, 1f);

    public int CollisionCount { get; private set; }
    public int HoleCount { get; private set; }

    public override void Update(GameTime gameTime)
    {
        if (!_initialized)
        {
            _eventBus = Services.GetService<IModEventBus>();
            _sceneSystem = Services.GetService<SceneSystem>();
            _initialized = true;
        }

        if (!_isScrolling || _levelBlocks.Count == 0)
            return;

        var elapsedTime = (float)gameTime.Elapsed.TotalSeconds;

        // Remove blocks too far behind
        while (_levelBlocks.Count > 0)
        {
            var first = _levelBlocks[0];
            if (first.PositionZ + first.Length * 0.5f < RemoveBlockPosition)
                RemoveLevelBlock(first);
            else break;
        }

        // Add new blocks when needed
        if (_levelBlocks.Count > 0)
        {
            var last = _levelBlocks[_levelBlocks.Count - 1];
            if (last.PositionZ - last.Length * 0.5f < AddBlockPosition)
                AddLevelBlock(CreateSafeBlock());
        }

        // Move all blocks
        foreach (var block in _levelBlocks)
        {
            var moveDist = GameSpeed * elapsedTime;
            block.PositionZ -= moveDist;
            _runningDistance += moveDist / 100f;
        }

        _eventBus?.Publish(new DistanceUpdatedEvent(_runningDistance));

        _skyplaneUVRegion.X -= 0.0005f;
        if (_skyplaneUVRegion.X < -1f)
            _skyplaneUVRegion.X = 0f;
    }

    public bool DetectCollisions(BoundingBox agentBB)
    {
        foreach (var block in _levelBlocks)
        {
            foreach (var obstacle in block.CollidableObstacles)
            {
                if (obstacle.Entity == null) continue;
                var objTrans = obstacle.Entity.Transform;
                objTrans.UpdateWorldMatrix();
                var objWorldPos = objTrans.WorldMatrix.TranslationVector;

                foreach (var bb in obstacle.BoundingBoxes)
                {
                    var minVec = objWorldPos + bb.Minimum;
                    var maxVec = objWorldPos + bb.Maximum;
                    var testBB = new BoundingBox(minVec, maxVec);

                    if (CollisionHelper.BoxContainsBox(ref testBB, ref agentBB) != ContainmentType.Disjoint)
                    {
                        CollisionCount++;
                        _eventBus?.Publish(new CollisionDetectedEvent());
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public bool DetectHoles(Vector3 agentWorldPos, out float height)
    {
        height = 0f;
        foreach (var block in _levelBlocks)
        {
            block.RootEntity.Transform.UpdateWorldMatrix();
            var blockPosZ = block.RootEntity.Transform.Position.Z;
            foreach (var hole in block.Holes)
            {
                var testArea = hole.Area;
                testArea.Y += blockPosZ;
                height = hole.Height;
                var agentVec2 = new Vector2(-agentWorldPos.X, agentWorldPos.Z);
                if (RectContains(testArea, agentVec2))
                {
                    HoleCount++;
                    _eventBus?.Publish(new HoleDetectedEvent(height));
                    return true;
                }
            }
        }
        return false;
    }

    public void StartScrolling() => _isScrolling = true;
    public void StopScrolling() => _isScrolling = false;

    public void Reset()
    {
        _runningDistance = 0f;
        _isScrolling = false;
        for (var i = _levelBlocks.Count - 1; i >= 0; i--)
        {
            var block = _levelBlocks[i];
            var entity = block.RootEntity;
            entity.Transform.Parent?.Children.Remove(entity.Transform);
            _levelBlocks.RemoveAt(i);
        }
    }

    private Entity? GetBackgroundEntity()
    {
        // Get the first entity that has this processor's component
        foreach (var kvp in ComponentDatas)
            return kvp.Key.Entity;
        return null;
    }

    private void AddLevelBlock(Section newSection)
    {
        var count = _levelBlocks.Count;
        _levelBlocks.Add(newSection);

        var bgEntity = GetBackgroundEntity();
        if (bgEntity == null) return;

        if (count == 0)
        {
            EntityTransformExtensions.AddChild(bgEntity, newSection.RootEntity);
            return;
        }

        var prev = _levelBlocks[count - 1];
        var originDist = 0.5f * (prev.Length + newSection.Length);
        newSection.PositionZ = prev.PositionZ + originDist;
        EntityTransformExtensions.AddChild(bgEntity, newSection.RootEntity);
    }

    private void RemoveLevelBlock(Section block)
    {
        _levelBlocks.Remove(block);
        var entity = block.RootEntity;
        entity.Transform.Parent?.Children.Remove(entity.Transform);
    }

    private static Section CreateSafeBlock()
    {
        // Simplified safe block for testing — no obstacles
        var section = new Section();
        return section;
    }

    private static bool RectContains(RectangleF rect, Vector2 point)
    {
        return rect.X <= point.X && rect.X + rect.Width >= point.X
               && rect.Y >= point.Y && rect.Y - rect.Height <= point.Y;
    }
}
