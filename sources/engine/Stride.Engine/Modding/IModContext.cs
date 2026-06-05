// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Diagnostics;

namespace Stride.Engine.Modding;

/// <summary>
/// Context provided to mods during initialization. Gives access to engine services
/// without exposing internal Stride types directly.
/// </summary>
public interface IModContext
{
    /// <summary>Access to the engine's service registry.</summary>
    IServiceRegistry Services { get; }

    /// <summary>Logger scoped to the mod.</summary>
    ILogger Logger { get; }

    /// <summary>Inter-mod event bus.</summary>
    IModEventBus EventBus { get; }

    /// <summary>Path to the mod's own directory.</summary>
    string ModDirectory { get; }
}

/// <summary>
/// Default implementation of IModContext, created by ModHost when initializing a mod.
/// </summary>
internal sealed class ModContext : IModContext
{
    public IServiceRegistry Services { get; }
    public ILogger Logger { get; }
    public IModEventBus EventBus { get; }
    public string ModDirectory { get; }

    public ModContext(IServiceRegistry services, ILogger logger, IModEventBus eventBus, string modDirectory)
    {
        Services = services;
        Logger = logger;
        EventBus = eventBus;
        ModDirectory = modDirectory;
    }
}
