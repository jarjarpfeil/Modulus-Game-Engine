// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Diagnostics;

namespace Modulus.Modding.Api;

/// <summary>
/// Context provided to mods during initialization. Gives access to engine services
/// without exposing internal Stride types directly.
///
/// For engine types not directly exposed here (ECS, ContentManager, Scene),
/// use <see cref="Services"/> to resolve them:
/// <code>
/// var sceneSystem = context.Services.GetService&lt;SceneSystem&gt;();
/// var contentManager = context.Services.GetService&lt;IContentManager&gt;();
/// </code>
///
/// This is part of the stable <c>Modulus.Modding.Api</c> ABI.
/// </summary>
public interface IModContext
{
    /// <summary>Access to the engine's service registry. Use this to resolve engine subsystems.</summary>
    IServiceRegistry Services { get; }

    /// <summary>Logger scoped to the mod.</summary>
    ILogger Logger { get; }

    /// <summary>Inter-mod event bus.</summary>
    IModEventBus EventBus { get; }

    /// <summary>Path to the mod's own directory on disk.</summary>
    string ModDirectory { get; }

    /// <summary>The mod's unique identifier (matches mod.json id).</summary>
    string ModId { get; }
}
