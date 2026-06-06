// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;

namespace Modulus.Modding.Api;

/// <summary>
/// Interface for mod state persistence. Mods implement this to define
/// save/load behavior. State is stored per-mod in a standard location.
///
/// Storage location: <c>%APPDATA%/ModulusEngine/mod-states/{modId}/state.dat</c>
///
/// This is part of the stable <c>Modulus.Modding.Api</c> ABI.
/// </summary>
public interface IModSerializable
{
    /// <summary>
    /// Save mod state to the given stream. The stream is writable and positioned at the start.
    /// </summary>
    void Save(Stream stream);

    /// <summary>
    /// Load mod state from the given stream. The stream is readable and positioned at the start.
    /// </summary>
    void Load(Stream stream);
}
