// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Stride.Engine.Modding;

/// <summary>
/// Tracks all resources owned by a single mod. Used by ModLifecycleManager
/// to ensure complete cleanup during unload — no leaked references that would
/// prevent ALC collection.
/// </summary>
public sealed class ModScope
{
    /// <summary>The mod ID this scope belongs to.</summary>
    public string ModId { get; }

    /// <summary>Entities created by this mod.</summary>
    public HashSet<Guid> OwnedEntities { get; } = [];

    /// <summary>Event subscriptions owned by this mod (eventType -> handler delegates).</summary>
    public List<(Type EventType, Delegate Handler)> OwnedSubscriptions { get; } = [];

    /// <summary>EntityProcessor instances registered by this mod.</summary>
    public List<object> OwnedProcessors { get; } = [];

    /// <summary>Component instances created by this mod.</summary>
    public List<object> OwnedComponents { get; } = [];

    /// <summary>Cached MethodInfo/PropertyInfo references to mod types (for nulling).</summary>
    public List<object> CachedReflectionMembers { get; } = [];

    /// <summary>Static references held by engine code to mod types.</summary>
    public List<Action> StaticReferenceCleanup { get; } = [];

    public ModScope(string modId)
    {
        ModId = modId ?? throw new ArgumentNullException(nameof(modId));
    }
}
