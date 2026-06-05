// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Stride.Core.IO;

/// <summary>
/// Multi-source file provider that fans out content resolution across
/// multiple registered file providers. Used by the modding system to
/// merge game content with mod content.
/// </summary>
/// <remarks>
/// Resolution order: providers are checked in registration order (last added = highest priority).
/// Mod providers should be added AFTER the game provider so mods can override game assets.
/// </remarks>
public class CompositeFileProviderService : IDatabaseFileProviderService
{
    private readonly List<DatabaseFileProvider> _providers = [];

    /// <summary>
    /// The primary file provider (the first one registered, typically the game's provider).
    /// </summary>
    public DatabaseFileProvider FileProvider
    {
        get
        {
            if (_providers.Count == 0)
                throw new InvalidOperationException("No file providers registered with CompositeFileProviderService");
            return _providers[^1]; // Last added = highest priority
        }
    }

    /// <summary>
    /// Registers a file provider. Later registrations have higher priority.
    /// </summary>
    public void AddProvider(DatabaseFileProvider provider)
    {
        _providers.Add(provider);
    }

    /// <summary>
    /// Removes a file provider.
    /// </summary>
    public void RemoveProvider(DatabaseFileProvider provider)
    {
        _providers.Remove(provider);
    }

    /// <summary>
    /// Returns all registered providers in priority order (lowest first, highest last).
    /// </summary>
    public IReadOnlyList<DatabaseFileProvider> GetProviders() => _providers;

    /// <summary>
    /// Checks if a file exists in any of the registered providers.
    /// Mod providers are checked first (higher priority).
    /// </summary>
    public bool FileExists(string url)
    {
        // Check in reverse (highest priority first)
        for (int i = _providers.Count - 1; i >= 0; i--)
        {
            if (_providers[i].FileExists(url))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Opens a stream from the first provider that contains the file.
    /// Mod providers are checked first (higher priority).
    /// </summary>
    public Stream? OpenStream(string url)
    {
        for (int i = _providers.Count - 1; i >= 0; i--)
        {
            if (_providers[i].FileExists(url))
            {
                return _providers[i].OpenStream(url, VirtualFileMode.Open, VirtualFileAccess.Read);
            }
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
        _providers.Clear();
    }
}
