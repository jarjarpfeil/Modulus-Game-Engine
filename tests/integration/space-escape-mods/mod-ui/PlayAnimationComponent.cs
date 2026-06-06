// PlayAnimationComponent.cs — Port of SpaceEscape's PlayIdleAnimationScript
// Tests: Simple component + processor, animation access across ALC

using Stride.Core;
using Stride.Engine;
using Stride.Games;

namespace ModUI;

/// <summary>Component that plays an animation on Start. Ported from PlayIdleAnimationScript.</summary>
[DataContract("PlayAnimationComponent")]
public class PlayAnimationComponent : EntityComponent
{
    [DataMember(10)]
    public string AnimationName { get; set; } = string.Empty;
}

/// <summary>Processor that plays the animation when the component is first updated.</summary>
public class PlayAnimationProcessor : EntityProcessor<PlayAnimationComponent>
{
    public override void Update(GameTime gameTime)
    {
        foreach (var kvp in ComponentDatas)
        {
            var component = kvp.Key;
            if (string.IsNullOrEmpty(component.AnimationName))
                continue;

            var animComponent = component.Entity.Get<AnimationComponent>();
            if (animComponent != null)
            {
                try
                {
                    animComponent.Play(component.AnimationName);
                }
                catch
                {
                    // Animation not found — don't crash
                }
            }
        }
    }
}
