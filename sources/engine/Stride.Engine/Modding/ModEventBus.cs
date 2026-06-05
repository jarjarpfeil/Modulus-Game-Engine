// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Implementation of IModEventBus. Manages typed event subscriptions with per-mod tracking
/// so all subscriptions are automatically cleaned up when a mod is unloaded.
/// </summary>
public sealed class ModEventBus : IModEventBus
{
    private static readonly Logger Log = GlobalLogger.GetLogger("ModEventBus");
    private readonly object _lock = new();

    // eventType -> list of (modId, handler)
    private readonly Dictionary<Type, List<ModSubscription>> _subscriptions = [];

    /// <inheritdoc/>
    public void Subscribe<T>(Action<T> handler)
    {
        // Use the modId overload with empty string for unowned subscriptions
        Subscribe(handler, modId: null);
    }

    /// <summary>
    /// Subscribes to events of type T with explicit mod ownership tracking.
    /// Called by ModHost when a mod subscribes.
    /// </summary>
    public void Subscribe<T>(Action<T> handler, string? modId)
    {
        var eventType = typeof(T);
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(eventType, out var list))
            {
                list = [];
                _subscriptions[eventType] = list;
            }
            list.Add(new ModSubscription(modId, handler));
        }
    }

    /// <inheritdoc/>
    public void Unsubscribe<T>(Action<T> handler)
    {
        var eventType = typeof(T);
        lock (_lock)
        {
            if (_subscriptions.TryGetValue(eventType, out var list))
            {
                list.RemoveAll(s => Equals(s.Handler, handler));
            }
        }
    }

    /// <inheritdoc/>
    public void Publish<T>(T evt)
    {
        var eventType = typeof(T);
        List<ModSubscription> snapshot;
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(eventType, out var list) || list.Count == 0)
                return;
            snapshot = [.. list];
        }

        foreach (var subscription in snapshot)
        {
            try
            {
                ((Action<T>)subscription.Handler)(evt);
            }
            catch (Exception ex)
            {
                Log.Error($"[ModEventBus] Handler from mod '{subscription.ModId ?? "unknown"}' threw on event {eventType.Name}: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void UnsubscribeAll(string modId)
    {
        lock (_lock)
        {
            var keysToRemove = new List<Type>();
            foreach (var (eventType, list) in _subscriptions)
            {
                list.RemoveAll(s => s.ModId == modId);
                if (list.Count == 0)
                    keysToRemove.Add(eventType);
            }
            foreach (var key in keysToRemove)
                _subscriptions.Remove(key);
        }
        Log.Info($"[ModEventBus] Unsubscribed all handlers for mod '{modId}'");
    }

    /// <summary>
    /// Returns the count of active subscriptions (for diagnostics).
    /// </summary>
    public int SubscriptionCount
    {
        get
        {
            lock (_lock)
                return _subscriptions.Values.Sum(l => l.Count);
        }
    }

    private sealed class ModSubscription
    {
        public string? ModId { get; }
        public Delegate Handler { get; }

        public ModSubscription(string? modId, Delegate handler)
        {
            ModId = modId;
            Handler = handler;
        }
    }
}
