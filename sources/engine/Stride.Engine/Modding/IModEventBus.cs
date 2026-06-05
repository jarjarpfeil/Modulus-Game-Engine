// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;

namespace Stride.Engine.Modding;

/// <summary>
/// Inter-mod communication bus. Allows mods to publish and subscribe to typed events.
/// Event subscriptions are scoped to the mod's lifecycle — unsubscribed automatically on mod unload.
/// </summary>
public interface IModEventBus
{
    /// <summary>
    /// Subscribes to events of type T. The handler will be called when Publish&lt;T&gt; is invoked.
    /// </summary>
    void Subscribe<T>(Action<T> handler);

    /// <summary>
    /// Unsubscribes a previously registered handler for events of type T.
    /// </summary>
    void Unsubscribe<T>(Action<T> handler);

    /// <summary>
    /// Publishes an event to all subscribed handlers of type T.
    /// </summary>
    void Publish<T>(T evt);

    /// <summary>
    /// Removes all event subscriptions for a specific mod. Called during mod unload.
    /// </summary>
    void UnsubscribeAll(string modId);
}
