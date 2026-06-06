// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Games;

namespace TestCrossGame;

/// <summary>
/// Health component for cross-game testing.
/// Proves that the same component type works across different game projects.
/// </summary>
[DefaultEntityComponentProcessor(typeof(HealthProcessor), ExecutionMode = ExecutionMode.Runtime)]
[DataContract("HealthComponent")]
[Display("Health Component")]
public class HealthComponent : EntityComponent
{
    /// <summary>Maximum health points.</summary>
    [DataMember(10)]
    public float MaxHealth { get; set; } = 100f;

    /// <summary>Current health points.</summary>
    [DataMember(20)]
    public float CurrentHealth { get; set; } = 100f;

    /// <summary>Whether this entity is dead.</summary>
    [DataMember(30)]
    public bool IsDead { get; set; }

    /// <summary>Number of death events fired — for test verification.</summary>
    [DataMemberIgnore]
    public int DeathEventCount { get; set; }

    /// <summary>Apply damage to this entity.</summary>
    public void ApplyDamage(float amount)
    {
        if (IsDead) return;

        CurrentHealth = Math.Max(0, CurrentHealth - amount);
        if (CurrentHealth <= 0)
        {
            IsDead = true;
            DeathEventCount++;
        }
    }

    /// <summary>Heal this entity.</summary>
    public void Heal(float amount)
    {
        if (IsDead) return;
        CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
    }
}

/// <summary>
/// Processor that monitors health and triggers death events.
/// Proves that the same system works across different game projects.
/// </summary>
public class HealthProcessor : EntityProcessor<HealthComponent>
{
    public override void Update(GameTime time)
    {
        // ComponentDatas maps TComponent → TComponent (key == value)
        foreach (var entry in ComponentDatas)
        {
            var health = entry.Key;

            // Just verify the component is accessible — no complex logic needed for the test
            // The key proof is that this processor runs correctly in multiple game contexts
            if (health.IsDead)
            {
                // Death state is tracked — could trigger events here in a real game
            }
        }
    }
}
