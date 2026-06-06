// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Modulus.Modding.Api;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

// ═══════════════════════════════════════════════════════
//  Test implementations of mod interfaces
// ═══════════════════════════════════════════════════════

internal class TestModComponent : IModComponent
{
    public int AttachCount { get; private set; }
    public int DetachCount { get; private set; }
    public int UpdateCount { get; private set; }
    public float LastDeltaTime { get; private set; }
    public bool ThrowOnAttach { get; set; }
    public bool ThrowOnUpdate { get; set; }

    public void OnAttach()
    {
        if (ThrowOnAttach) throw new InvalidOperationException("Attach failed");
        AttachCount++;
    }

    public void OnDetach()
    {
        DetachCount++;
    }

    public void Update(float deltaTime)
    {
        if (ThrowOnUpdate) throw new InvalidOperationException("Update failed");
        UpdateCount++;
        LastDeltaTime = deltaTime;
    }
}

internal class TestModSystem : IModSystem
{
    public int Priority { get; set; } = 100;
    public int UpdateCount { get; private set; }
    public float LastDeltaTime { get; private set; }
    public int RegisteredCount { get; private set; }
    public int UnregisteredCount { get; private set; }
    public bool ThrowOnUpdate { get; set; }
    public bool ThrowOnRegister { get; set; }

    public void Update(float deltaTime)
    {
        if (ThrowOnUpdate) throw new InvalidOperationException("Update failed");
        UpdateCount++;
        LastDeltaTime = deltaTime;
    }

    public void OnRegistered()
    {
        if (ThrowOnRegister) throw new InvalidOperationException("Register failed");
        RegisteredCount++;
    }

    public void OnUnregistered()
    {
        UnregisteredCount++;
    }
}

// ═══════════════════════════════════════════════════════
//  Tests
// ═══════════════════════════════════════════════════════

public class ModComponentAdapterTests
{
    [Fact]
    public void AdapterStoresComponentReference()
    {
        var modComponent = new TestModComponent();
        var adapter = new ModComponentAdapter(modComponent);

        Assert.Same(modComponent, adapter.ModComponent);
    }

    [Fact]
    public void AdapterSetsOwnerModId()
    {
        var adapter = new ModComponentAdapter(new TestModComponent());
        adapter.OwnerModId = "com.example.test-mod";

        Assert.Equal("com.example.test-mod", adapter.OwnerModId);
    }

    [Fact]
    public void InvokeOnAttach_ForwardsToComponent()
    {
        var modComponent = new TestModComponent();
        var adapter = new ModComponentAdapter(modComponent);

        adapter.InvokeOnAttach();

        Assert.Equal(1, modComponent.AttachCount);
    }

    [Fact]
    public void InvokeOnDetach_ForwardsToComponent()
    {
        var modComponent = new TestModComponent();
        var adapter = new ModComponentAdapter(modComponent);

        adapter.InvokeOnDetach();

        Assert.Equal(1, modComponent.DetachCount);
    }

    [Fact]
    public void InvokeUpdate_ForwardsToComponent()
    {
        var modComponent = new TestModComponent();
        var adapter = new ModComponentAdapter(modComponent);

        adapter.InvokeUpdate(0.016f);

        Assert.Equal(1, modComponent.UpdateCount);
        Assert.Equal(0.016f, modComponent.LastDeltaTime, 3);
    }

    [Fact]
    public void InvokeUpdate_MultipleCalls_Forwards()
    {
        var modComponent = new TestModComponent();
        var adapter = new ModComponentAdapter(modComponent);

        adapter.InvokeUpdate(0.016f);
        adapter.InvokeUpdate(0.032f);
        adapter.InvokeUpdate(0.048f);

        Assert.Equal(3, modComponent.UpdateCount);
    }

    [Fact]
    public void InvokeOnAttach_DoesNotThrow_OnException()
    {
        var modComponent = new TestModComponent { ThrowOnAttach = true };
        var adapter = new ModComponentAdapter(modComponent);

        // Should NOT throw — adapter catches exceptions
        adapter.InvokeOnAttach();
    }

    [Fact]
    public void InvokeUpdate_DoesNotThrow_OnException()
    {
        var modComponent = new TestModComponent { ThrowOnUpdate = true };
        var adapter = new ModComponentAdapter(modComponent);

        // Should NOT throw — adapter catches exceptions
        adapter.InvokeUpdate(0.016f);
    }

    [Fact]
    public void ParameterlessConstructor_DoesNotThrow()
    {
        // Required for serialization / Activator.CreateInstance
        var adapter = new ModComponentAdapter();
        Assert.Null(adapter.ModComponent);
    }

    [Fact]
    public void NullConstructor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ModComponentAdapter(null!));
    }

    [Fact]
    public void InvokeOnAttach_NullComponent_DoesNotThrow()
    {
        // Parameterless-constructed adapter — InvokeOnAttach should be safe
        var adapter = new ModComponentAdapter();
        adapter.InvokeOnAttach(); // no-op, no crash
    }
}

public class ModProcessorAdapterTests
{
    [Fact]
    public void AdapterStoresSystemAndModId()
    {
        var system = new TestModSystem();
        var adapter = new ModProcessorAdapter<TestModSystem>(system, "com.example.test");

        Assert.Same(system, adapter.ModSystem);
        Assert.Equal("com.example.test", adapter.ModId);
    }

    [Fact]
    public void Priority_ForwardsFromModSystem()
    {
        var system = new TestModSystem { Priority = 42 };
        var adapter = new ModProcessorAdapter<TestModSystem>(system, "test");

        Assert.Equal(42, adapter.Priority);
    }

    [Fact]
    public void DefaultPriority_Is100()
    {
        var system = new TestModSystem();
        var adapter = new ModProcessorAdapter<TestModSystem>(system, "test");

        Assert.Equal(100, adapter.Priority);
    }

    [Fact]
    public void InvokeOnRegistered_ForwardsToSystem()
    {
        var system = new TestModSystem();
        var adapter = new ModProcessorAdapter<TestModSystem>(system, "test");

        adapter.InvokeOnRegistered();

        Assert.Equal(1, system.RegisteredCount);
    }

    [Fact]
    public void InvokeOnUnregistered_ForwardsToSystem()
    {
        var system = new TestModSystem();
        var adapter = new ModProcessorAdapter<TestModSystem>(system, "test");

        adapter.InvokeOnUnregistered();

        Assert.Equal(1, system.UnregisteredCount);
    }

    [Fact]
    public void InvokeOnRegistered_DoesNotThrow_OnException()
    {
        var system = new TestModSystem { ThrowOnRegister = true };
        var adapter = new ModProcessorAdapter<TestModSystem>(system, "test");

        // Should NOT throw
        adapter.InvokeOnRegistered();
    }

    [Fact]
    public void NullSystem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ModProcessorAdapter<TestModSystem>(null!, "test"));
    }

    [Fact]
    public void NullModId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ModProcessorAdapter<TestModSystem>(new TestModSystem(), null!));
    }
}

// ═══════════════════════════════════════════════════════
//  Standalone interface tests (cross-game compatibility)
// ═══════════════════════════════════════════════════════

public class ModInterfaceTests
{
    [Fact]
    public void IModComponent_DefaultImplementations_DoNotThrow()
    {
        // A mod that uses default interface methods (no overrides)
        IModComponent minimal = new MinimalComponent();

        // These should all be no-ops (default implementations)
        minimal.OnAttach();
        minimal.OnDetach();
        minimal.Update(0.016f);
    }

    [Fact]
    public void IModSystem_DefaultImplementations_DoNotThrow()
    {
        // A mod system that only implements Update
        IModSystem minimal = new MinimalSystem();

        Assert.Equal(100, minimal.Priority); // default
        minimal.OnRegistered();  // no-op
        minimal.OnUnregistered(); // no-op
    }

    [Fact]
    public void SameComponent_WorksInMultipleContexts()
    {
        // This simulates cross-game compatibility: the same IModComponent
        // implementation can be used in any game built on Modulus.
        var component = new TestModComponent();

        // Game A wraps it
        var adapterA = new ModComponentAdapter(component);
        adapterA.OwnerModId = "game-a";

        // Game B wraps the same type
        var component2 = new TestModComponent();
        var adapterB = new ModComponentAdapter(component2);
        adapterB.OwnerModId = "game-b";

        // Both work independently
        adapterA.InvokeOnAttach();
        adapterB.InvokeOnAttach();

        Assert.Equal(1, component.AttachCount);
        Assert.Equal(1, component2.AttachCount);
    }

    [Fact]
    public void CrossGameMod_ComponentsAreIndependent()
    {
        // Simulates the same mod DLL providing health components to two different games.
        // Each game gets its own adapter wrapping the same IModComponent type.
        var gameAHealth = new HealthModComponent { MaxHealth = 100 };
        var gameBHealth = new HealthModComponent { MaxHealth = 200 };

        var adapterA = new ModComponentAdapter(gameAHealth);
        adapterA.OwnerModId = "skyrim-like";

        var adapterB = new ModComponentAdapter(gameBHealth);
        adapterB.OwnerModId = "fallout-like";

        // Both initialize independently
        adapterA.InvokeOnAttach();
        adapterB.InvokeOnAttach();

        // Game A takes damage
        gameAHealth.CurrentHealth -= 30;
        Assert.Equal(70, gameAHealth.CurrentHealth);

        // Game B is unaffected
        Assert.Equal(200, gameBHealth.CurrentHealth);

        // Both update independently
        adapterA.InvokeUpdate(0.016f);
        adapterB.InvokeUpdate(0.032f);
    }

    [Fact]
    public void CrossGameMod_SystemsWorkIndependently()
    {
        // The same IModSystem type can serve different game contexts
        var systemA = new HealthRegenSystem { Priority = 50 };
        var systemB = new HealthRegenSystem { Priority = 200 };

        var adapterA = new ModProcessorAdapter<HealthRegenSystem>(systemA, "game-a");
        var adapterB = new ModProcessorAdapter<HealthRegenSystem>(systemB, "game-b");

        // Priorities are independent
        Assert.Equal(50, adapterA.Priority);
        Assert.Equal(200, adapterB.Priority);

        // Both update independently
        adapterA.InvokeOnRegistered();
        adapterB.InvokeOnRegistered();
        Assert.Equal(1, systemA.RegisteredCount);
        Assert.Equal(1, systemB.RegisteredCount);
    }

    // Cross-game test types — these represent what a universal mod DLL would contain
    private class HealthModComponent : IModComponent
    {
        public int MaxHealth { get; set; }
        public int CurrentHealth { get; set; }
        public bool IsDead => CurrentHealth <= 0;
        public int UpdateCount { get; private set; }

        public void OnAttach() { CurrentHealth = MaxHealth; }
        public void Update(float deltaTime) { UpdateCount++; }
    }

    private class HealthRegenSystem : IModSystem
    {
        public int Priority { get; set; } = 100;
        public int RegisteredCount { get; private set; }
        public void Update(float deltaTime) { }
        public void OnRegistered() { RegisteredCount++; }
    }

    // Minimal implementations that rely on default interface methods
    private class MinimalComponent : IModComponent { }
    private class MinimalSystem : IModSystem
    {
        public void Update(float deltaTime) { }
    }
}
