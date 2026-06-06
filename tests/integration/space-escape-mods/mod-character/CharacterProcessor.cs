// CharacterProcessor.cs — Port of SpaceEscape's CharacterScript.Execute() state machine
// Tests: EntityProcessor across ALC, Input access, AnimationComponent access, event bus publishing

using System;
using System.Collections.Generic;
using Modulus.Modding.Api;
using SpaceEscape.Contracts;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Animations;
using Stride.Graphics;

namespace ModCharacter;

/// <summary>
/// EntityProcessor that drives CharacterComponent state machine.
/// Ported from SpaceEscape's CharacterScript (AsyncScript with Execute() coroutine).
/// Tests: Cross-ALC processor, Input access, AnimationComponent, event bus publishing.
/// </summary>
public class CharacterProcessor : EntityProcessor<CharacterComponent>
{
    private IModEventBus? _eventBus;
    private InputManager? _input;
    private bool _initialized;

    public override void Update(GameTime gameTime)
    {
        if (!_initialized)
        {
            // Resolve services lazily — they may not be available during construction
            _eventBus = Services.GetService<IModEventBus>();
            _input = Services.GetService<InputManager>();
            _initialized = true;
        }

        var deltaTime = (float)gameTime.Elapsed.TotalSeconds;

        foreach (var kvp in ComponentDatas)
        {
            var component = kvp.Key;
            UpdateCharacter(component, deltaTime);
        }
    }

    private void UpdateCharacter(CharacterComponent character, float deltaTime)
    {
        if (character.IsDead || !character.IsActive)
            return;

        // Gather input
        var inputState = GetInputState(character);

        // Process input → state transitions
        ProcessInput(character, inputState);

        // Update state machine
        UpdateState(character, deltaTime);
    }

    private InputState GetInputState(CharacterComponent character)
    {
        if (!character.CanProcessInput || _input == null)
            return InputState.None;

        // Check keyboard
        if (_input.IsKeyPressed(Keys.Left))
            return InputState.Left;
        if (_input.IsKeyPressed(Keys.Right))
            return InputState.Right;
        if (_input.IsKeyPressed(Keys.Down))
            return InputState.Down;

        // Check gestures (drag)
        foreach (var gestureEvent in _input.GestureEvents)
        {
            if (gestureEvent.Type == GestureType.Drag && gestureEvent.State == GestureState.Began)
            {
                return ProcessDragGesture((GestureEventDrag)gestureEvent);
            }
        }

        return InputState.None;
    }

    private InputState ProcessDragGesture(GestureEventDrag gestureEvent)
    {
        var graphicsDevice = Services.GetService<Stride.Graphics.GraphicsDevice>();
        if (graphicsDevice == null)
            return InputState.None;

        var screenRatio = (float)graphicsDevice.Presenter.BackBuffer.Height
                          / graphicsDevice.Presenter.BackBuffer.Width;
        var dragVector = (gestureEvent.CurrentPosition - gestureEvent.StartPosition)
                         * new Vector2(1f, -screenRatio);
        var dragDirection = Vector2.Normalize(dragVector);

        // Quadrant-based direction detection (same logic as original)
        if (dragDirection.X >= 0 && dragDirection.Y >= 0)
        {
            var xDeg = AngleBetween(dragDirection, Vector2.UnitX);
            var yDeg = AngleBetween(dragDirection, Vector2.UnitY);
            return xDeg <= yDeg ? InputState.Right : InputState.None;
        }
        if (dragDirection.X <= 0 && dragDirection.Y >= 0)
        {
            var xDeg = AngleBetween(dragDirection, -Vector2.UnitX);
            var yDeg = AngleBetween(dragDirection, Vector2.UnitY);
            return xDeg <= yDeg ? InputState.Left : InputState.None;
        }
        if (dragDirection.X <= 0 && dragDirection.Y <= 0)
        {
            var xDeg = AngleBetween(dragDirection, -Vector2.UnitX);
            var yDeg = AngleBetween(dragDirection, -Vector2.UnitY);
            return xDeg <= yDeg ? InputState.Left : InputState.Down;
        }
        // Quadrant 4
        {
            var xDeg = AngleBetween(dragDirection, Vector2.UnitX);
            var yDeg = AngleBetween(dragDirection, -Vector2.UnitY);
            return xDeg <= yDeg ? InputState.Right : InputState.Down;
        }
    }

    private static float AngleBetween(Vector2 a, Vector2 b)
    {
        float dot;
        Vector2.Dot(ref a, ref b, out dot);
        return (float)Math.Acos(Math.Clamp(dot, -1f, 1f));
    }

    private void ProcessInput(CharacterComponent character, InputState input)
    {
        switch (input)
        {
            case InputState.Left:
                if (character.CurrentLane > 0 && character.State == CharacterState.Run)
                {
                    character.State = CharacterState.ChangeLaneLeft;
                    OnEnterLaneChange(character, isLeft: true);
                }
                break;
            case InputState.Right:
                if (character.CurrentLane < 2 && character.State == CharacterState.Run)
                {
                    character.State = CharacterState.ChangeLaneRight;
                    OnEnterLaneChange(character, isLeft: false);
                }
                break;
            case InputState.Down:
                if (character.State == CharacterState.Run)
                {
                    character.State = CharacterState.Slide;
                    OnEnterSlide(character);
                }
                break;
        }
    }

    private void UpdateState(CharacterComponent character, float deltaTime)
    {
        switch (character.State)
        {
            case CharacterState.ChangeLaneLeft:
            case CharacterState.ChangeLaneRight:
                UpdateLaneChange(character);
                break;
            case CharacterState.Slide:
                UpdateSlide(character);
                break;
        }
    }

    private void OnEnterLaneChange(CharacterComponent character, bool isLeft)
    {
        character.LaneChangeStartX = character.GetLaneWorldX(character.CurrentLane);

        if (isLeft)
            character.CurrentLane--;
        else
            character.CurrentLane++;

        character.LaneChangeTargetX = character.GetLaneWorldX(character.CurrentLane);

        // Play dodge animation
        PlayAnimation(character, isLeft ? "DodgeLeft" : "DodgeRight");

        // Publish event
        _eventBus?.Publish(new LaneChangedEvent(character.CurrentLane));
    }

    private void OnEnterSlide(CharacterComponent character)
    {
        PlayAnimation(character, "Slide");
        character.ActiveBoundingBox = character.BoundingBoxes.GetValueOrDefault(
            BoundingBoxKey.Slide, character.ActiveBoundingBox);
    }

    private void UpdateLaneChange(CharacterComponent character)
    {
        if (character.PlayingAnimation.Clip == null)
        {
            character.State = CharacterState.Run;
            return;
        }

        var t = (float)(character.PlayingAnimation.CurrentTime.TotalSeconds
                        / character.PlayingAnimation.Clip.Duration.TotalSeconds);
        t = Math.Clamp(t, 0f, 1f);

        var newX = MathUtil.Lerp(character.LaneChangeStartX, character.LaneChangeTargetX, t);
        character.Entity.Transform.Position.X = newX;

        if (t >= 1.0f)
        {
            character.State = CharacterState.Run;
            PlayAnimation(character, "Active");
            character.ActiveBoundingBox = character.BoundingBoxes.GetValueOrDefault(
                BoundingBoxKey.Normal, character.ActiveBoundingBox);
        }
    }

    private void UpdateSlide(CharacterComponent character)
    {
        if (character.PlayingAnimation.Clip == null ||
            character.PlayingAnimation.CurrentTime.TotalSeconds >= character.PlayingAnimation.Clip.Duration.TotalSeconds)
        {
            character.State = CharacterState.Run;
            PlayAnimation(character, "Active");
            character.ActiveBoundingBox = character.BoundingBoxes.GetValueOrDefault(
                BoundingBoxKey.Normal, character.ActiveBoundingBox);
        }
    }

    private void PlayAnimation(CharacterComponent character, string animationName)
    {
        var animComponent = character.Entity.Get<AnimationComponent>();
        if (animComponent == null)
            return;

        try
        {
            animComponent.Play(animationName);
            if (animComponent.PlayingAnimations.Count > 0)
                character.PlayingAnimation = animComponent.PlayingAnimations[0];
        }
        catch (Exception)
        {
            // Animation not found — this is a test point: mod tries to play animation
            // that may not exist in the host. Should not crash.
        }
    }

    /// <summary>
    /// Called by the mod system when the character dies (from external source).
    /// </summary>
    public void OnCharacterDied(CharacterComponent character, float floorHeight)
    {
        character.IsDead = true;
        character.State = CharacterState.Die;
        character.Entity.Transform.Position.Y = floorHeight;
        character.ShadowTransparency = 0;
        PlayAnimation(character, "Crash");
        _eventBus?.Publish(new PlayerDiedEvent(floorHeight));
    }

    /// <summary>
    /// Called by the mod system to activate the character.
    /// </summary>
    public void OnActivate(CharacterComponent character)
    {
        character.IsActive = true;
    }

    /// <summary>
    /// Called by the mod system to reset the character.
    /// </summary>
    public void OnReset(CharacterComponent character)
    {
        character.IsActive = false;
        character.IsDead = false;
        character.State = CharacterState.Run;
        character.CurrentLane = 1;
        character.ShadowTransparency = 1f;
        character.Entity.Transform.Position.Y = character.LaneHeight;
        character.Entity.Transform.Position.X = character.GetLaneWorldX(character.CurrentLane);
    }

    private enum InputState { None, Left, Right, Down }
}
