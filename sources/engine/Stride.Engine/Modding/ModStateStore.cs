// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;

namespace Stride.Engine.Modding;

/// <summary>Result of loading mod state from disk.</summary>
public sealed class ModStateData
{
    public byte[] Data { get; }
    public Version Version { get; }

    public ModStateData(byte[] data, Version version)
    {
        Data = data;
        Version = version;
    }
}

/// <summary>
/// Manages persistent storage of mod state on disk.
/// Location: {baseDir}/mod-states/{modId}/state.dat
/// </summary>
public sealed class ModStateStore
{
    private readonly string _baseDir;

    public ModStateStore(string baseDir)
    {
        _baseDir = baseDir ?? throw new ArgumentNullException(nameof(baseDir));
    }

    private string GetModStateDir(string modId) => Path.Combine(_baseDir, "mod-states", modId);
    private string GetModStatePath(string modId) => Path.Combine(GetModStateDir(modId), "state.dat");
    private string GetModVersionPath(string modId) => Path.Combine(GetModStateDir(modId), "version.txt");

    public bool HasState(string modId) => File.Exists(GetModStatePath(modId));

    public void SaveState(string modId, byte[] data, Version version)
    {
        var dir = GetModStateDir(modId);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(GetModStatePath(modId), data);
        File.WriteAllText(GetModVersionPath(modId), version.ToString());
    }

    public ModStateData? LoadState(string modId)
    {
        var statePath = GetModStatePath(modId);
        if (!File.Exists(statePath)) return null;

        var data = File.ReadAllBytes(statePath);
        Version version = new(1, 0, 0);
        var versionPath = GetModVersionPath(modId);
        if (File.Exists(versionPath))
        {
            var versionText = File.ReadAllText(versionPath).Trim();
            Version.TryParse(versionText, out var parsed);
            if (parsed != null) version = parsed;
        }
        return new ModStateData(data, version);
    }
}
