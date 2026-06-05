// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stride.Engine.Modding;

/// <summary>
/// Parsed representation of a mod.json manifest file.
/// </summary>
public sealed class ModManifest
{
    /// <summary>Unique mod identifier (kebab-case, e.g. "com.example.my-mod").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable mod name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Mod version (semver).</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>Minimum API version required.</summary>
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = "1.0";

    /// <summary>Mod author.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>Mod description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Fully-qualified entry point type (e.g. "MyMod.Main, MyMod").</summary>
    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    /// <summary>Mod dependencies.</summary>
    [JsonPropertyName("dependencies")]
    public List<ModDependency> Dependencies { get; set; } = [];

    /// <summary>If true, reject on major API version mismatch instead of warning.</summary>
    [JsonPropertyName("rejectFutureVersions")]
    public bool RejectFutureVersions { get; set; }

    /// <summary>Custom components declared by this mod.</summary>
    [JsonPropertyName("components")]
    public List<ModComponentDeclaration> Components { get; set; } = [];

    /// <summary>Custom systems declared by this mod.</summary>
    [JsonPropertyName("systems")]
    public List<ModSystemDeclaration> Systems { get; set; } = [];

    /// <summary>Asset paths included in the mod.</summary>
    [JsonPropertyName("assets")]
    public List<string> Assets { get; set; } = [];

    /// <summary>Custom shaders included in the mod.</summary>
    [JsonPropertyName("shaders")]
    public List<ModShaderDeclaration>? Shaders { get; set; }

    /// <summary>Load order priority (lower = loads first, default 100).</summary>
    [JsonPropertyName("loadOrder")]
    public int LoadOrder { get; set; } = 100;

    /// <summary>Mod tags for categorization.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>Mod type: "standard", "patch", or "data".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "standard";

    /// <summary>
    /// Parses a mod.json manifest from a stream.
    /// </summary>
    public static ModManifest FromStream(Stream stream)
    {
        return JsonSerializer.Deserialize<ModManifest>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        }) ?? throw new InvalidDataException("Failed to parse mod.json — null result.");
    }

    /// <summary>
    /// Parses a mod.json manifest from a JSON string.
    /// </summary>
    public static ModManifest FromJson(string json)
    {
        return JsonSerializer.Deserialize<ModManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        }) ?? throw new InvalidDataException("Failed to parse mod.json — null result.");
    }
}

/// <summary>
/// A dependency declared in mod.json.
/// </summary>
public sealed class ModDependency
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("minVersion")]
    public string MinVersion { get; set; } = "1.0.0";

    [JsonPropertyName("optional")]
    public bool Optional { get; set; }
}

/// <summary>
/// A component declared in mod.json.
/// </summary>
public sealed class ModComponentDeclaration
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("processor")]
    public string? Processor { get; set; }
}

/// <summary>
/// A system declared in mod.json.
/// </summary>
public sealed class ModSystemDeclaration
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}

/// <summary>
/// A shader declared in mod.json.
/// </summary>
public sealed class ModShaderDeclaration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
