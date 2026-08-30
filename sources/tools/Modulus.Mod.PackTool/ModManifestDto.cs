// Lightweight DTO for parsing mod.json — matches the exact JSON field names
// used by Stride.Engine.Modding.ModManifest (System.Text.Json with PropertyNameCaseInsensitive).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modulus.Mod.PackTool;

public sealed class ModManifestDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = "1.0";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    [JsonPropertyName("dependencies")]
    public List<ModDependencyDto> Dependencies { get; set; } = [];

    [JsonPropertyName("components")]
    public List<ModComponentDto> Components { get; set; } = [];

    [JsonPropertyName("systems")]
    public List<ModSystemDto> Systems { get; set; } = [];

    [JsonPropertyName("assets")]
    public List<string> Assets { get; set; } = [];

    [JsonPropertyName("scenes")]
    public List<ModSceneDto> Scenes { get; set; } = [];

    [JsonPropertyName("loadOrder")]
    public int LoadOrder { get; set; } = 100;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("type")]
    public string Type { get; set; } = "standard";

    [JsonPropertyName("requiresNativeCode")]
    public bool RequiresNativeCode { get; set; }

    [JsonPropertyName("explicitOverrides")]
    public List<string> ExplicitOverrides { get; set; } = [];

    public static ModManifestDto FromStream(Stream stream)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        var result = JsonSerializer.Deserialize<ModManifestDto>(stream, options)
            ?? throw new InvalidDataException("mod.json deserialized to null.");
        return result;
    }
}

public sealed class ModDependencyDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("minVersion")]
    public string MinVersion { get; set; } = "1.0.0";
    [JsonPropertyName("optional")]
    public bool Optional { get; set; }
}

public sealed class ModComponentDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    [JsonPropertyName("processor")]
    public string? Processor { get; set; }
}

public sealed class ModSystemDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}

public sealed class ModSceneDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("behavior")]
    public string? Behavior { get; set; }
}
