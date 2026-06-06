// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Modulus.Modding.Api;
using Stride.Core;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Default implementation of <see cref="IModContext"/>, created by <see cref="ModHost"/> when initializing a mod.
/// Tracks the mod ID so event subscriptions can be ownership-tracked for cleanup.
/// </summary>
internal sealed class ModContext : IModContext
{
    public IServiceRegistry Services { get; }
    public ILogger Logger { get; }
    public IModEventBus EventBus { get; }
    public string ModDirectory { get; }
    public string ModId { get; }

    public ModContext(IServiceRegistry services, ILogger logger, IModEventBus eventBus, string modDirectory, string modId)
    {
        Services = services;
        Logger = logger;
        EventBus = new ModEventBusProxy(eventBus, modId);
        ModDirectory = modDirectory;
        ModId = modId;
    }
}

/// <summary>
/// Proxy that automatically tags event subscriptions with the owning mod ID.
/// This ensures UnsubscribeAll(modId) during unload properly cleans up all handlers.
/// </summary>
internal sealed class ModEventBusProxy : IModEventBus
{
    private readonly IModEventBus _inner;
    private readonly string _modId;

    public ModEventBusProxy(IModEventBus inner, string modId)
    {
        _inner = inner;
        _modId = modId;
    }

    public void Subscribe<T>(Action<T> handler)
    {
        if (_inner is ModEventBus bus)
            bus.Subscribe(handler, _modId);
        else
            _inner.Subscribe(handler);
    }

    public void Unsubscribe<T>(Action<T> handler) => _inner.Unsubscribe(handler);
    public void Publish<T>(T evt) => _inner.Publish(evt);
    public void UnsubscribeAll(string modId) => _inner.UnsubscribeAll(modId);
}
