// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModEventBusTests
{
    [Fact]
    public void Publish_DeliversToSubscribers()
    {
        var bus = new ModEventBus();
        string? received = null;

        bus.Subscribe<string>(msg => received = msg);
        bus.Publish("hello");

        Assert.Equal("hello", received);
    }

    [Fact]
    public void Publish_MultipleSubscribers_AllReceive()
    {
        var bus = new ModEventBus();
        int count = 0;

        bus.Subscribe<string>(_ => count++);
        bus.Subscribe<string>(_ => count++);
        bus.Publish("test");

        Assert.Equal(2, count);
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        var bus = new ModEventBus();
        int count = 0;
        Action<string> handler = _ => count++;

        bus.Subscribe(handler);
        bus.Publish("first");
        Assert.Equal(1, count);

        bus.Unsubscribe(handler);
        bus.Publish("second");
        Assert.Equal(1, count); // unchanged
    }

    [Fact]
    public void Publish_DifferentTypes_Independent()
    {
        var bus = new ModEventBus();
        string? strResult = null;
        int? intResult = null;

        bus.Subscribe<string>(msg => strResult = msg);
        bus.Subscribe<int>(n => intResult = n);

        bus.Publish("hello");
        Assert.Equal("hello", strResult);
        Assert.Null(intResult);

        bus.Publish(42);
        Assert.Equal(42, intResult);
    }

    [Fact]
    public void UnsubscribeAll_RemovesAllForMod()
    {
        var bus = new ModEventBus();
        int countA = 0;
        int countB = 0;

        // Use the modId overload so we can track ownership
        bus.Subscribe<string>(_ => countA++, "test-mod");
        bus.Subscribe<int>(_ => countB++, "test-mod");

        bus.UnsubscribeAll("test-mod");

        bus.Publish("test");
        bus.Publish(42);

        Assert.Equal(0, countA);
        Assert.Equal(0, countB);
    }

    [Fact]
    public void UnsubscribeAll_OnlyAffectsTargetMod()
    {
        var bus = new ModEventBus();
        int modACount = 0;
        int modBCount = 0;

        // Use the Subscribe<T>(handler, modId) overload directly
        bus.Subscribe<string>(_ => modACount++, "mod-a");
        bus.Subscribe<string>(_ => modBCount++, "mod-b");

        bus.UnsubscribeAll("mod-a");

        bus.Publish("test");

        Assert.Equal(0, modACount); // mod-a unsubscribed
        Assert.Equal(1, modBCount); // mod-b still subscribed
    }

    [Fact]
    public void Publish_HandlerException_DoesNotBreakOtherHandlers()
    {
        var bus = new ModEventBus();
        int secondCalled = 0;

        bus.Subscribe<string>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<string>(_ => secondCalled++);

        // Should not throw
        bus.Publish("test");

        Assert.Equal(1, secondCalled);
    }

    [Fact]
    public void SubscriptionCount_TracksAccurately()
    {
        var bus = new ModEventBus();
        Assert.Equal(0, bus.SubscriptionCount);

        Action<string> h1 = _ => { };
        Action<string> h2 = _ => { };

        bus.Subscribe(h1);
        Assert.Equal(1, bus.SubscriptionCount);

        bus.Subscribe(h2);
        Assert.Equal(2, bus.SubscriptionCount);

        bus.Unsubscribe(h1);
        Assert.Equal(1, bus.SubscriptionCount);

        bus.Unsubscribe(h2);
        Assert.Equal(0, bus.SubscriptionCount);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var bus = new ModEventBus();
        var exception = Record.Exception(() => bus.Publish("nobody listening"));
        Assert.Null(exception);
    }

    [Fact]
    public void Publish_PreservesTypeInformation()
    {
        var bus = new ModEventBus();
        TestEvent? received = null;

        bus.Subscribe<TestEvent>(evt => received = evt);

        var sent = new TestEvent { Value = 42, Name = "test" };
        bus.Publish(sent);

        Assert.NotNull(received);
        Assert.Same(sent, received); // reference equality
        Assert.Equal(42, received.Value);
        Assert.Equal("test", received.Name);
    }

    private class TestEvent
    {
        public int Value { get; set; }
        public string Name { get; set; } = "";
    }
}
