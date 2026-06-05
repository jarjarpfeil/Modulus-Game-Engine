// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Core;

namespace Stride.Engine.Modding;

/// <summary>
/// Generic wrapper that preserves unknown component data when a mod is uninstalled.
/// Deserializer encounters unknown type -> maps to OrphanComponent, preserves raw data.
/// </summary>
[DataContract("OrphanComponent")]
public sealed class OrphanComponent : EntityComponent
{
    public string OriginalTypeName { get; set; } = "";
    public string ModId { get; set; } = "";
    public byte[] RawData { get; set; } = [];

    public string DisplayName
    {
        get
        {
            var shortName = OriginalTypeName;
            var lastDot = OriginalTypeName.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < OriginalTypeName.Length - 1)
                shortName = OriginalTypeName[(lastDot + 1)..];
            return $"Missing: {shortName} (from {ModId})";
        }
    }

    public bool CanRehydrate => !string.IsNullOrEmpty(OriginalTypeName) && RawData.Length > 0;
}
