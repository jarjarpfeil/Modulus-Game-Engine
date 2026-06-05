using System.ComponentModel;
using ModulusEngine.MCPServer.Bridge;
using ModelContextProtocol.Server;

namespace ModulusEngine.MCPServer.Tools;

/// <summary>
/// MCP tools for querying engine status and scene information.
/// </summary>
[McpServerToolType]
public static class StatusTools
{
    [McpServerTool, Description("Get the current engine status including renderer info, entity count, and runtime details.")]
    public static async Task<string> GetEngineStatus(EngineBridge engine)
    {
        return await engine.GetAsync("/api/v1/status");
    }
}

/// <summary>
/// MCP tools for scene entity manipulation.
/// </summary>
[McpServerToolType]
public static class SceneTools
{
    [McpServerTool, Description("List all entities in the current scene with their IDs, names, positions, and component counts.")]
    public static async Task<string> GetEntities(EngineBridge engine)
    {
        return await engine.GetAsync("/api/v1/scene/entities");
    }

    [McpServerTool, Description("Get detailed information about a specific entity including its components, transform, and children.")]
    public static async Task<string> GetEntity(
        EngineBridge engine,
        [Description("The GUID of the entity to inspect")] string entityId)
    {
        return await engine.GetAsync($"/api/v1/scene/entities/{entityId}");
    }

    [McpServerTool, Description("Create a new entity in the current scene. Returns the new entity's ID.")]
    public static async Task<string> CreateEntity(
        EngineBridge engine,
        [Description("Name for the new entity")] string name,
        [Description("Optional parent entity GUID")] string? parentId = null)
    {
        var body = new Dictionary<string, string> { ["name"] = name };
        if (parentId != null)
            body["parentId"] = parentId;
        return await engine.PostAsync("/api/v1/scene/entities", body);
    }

    [McpServerTool, Description("Delete an entity from the current scene by its GUID.")]
    public static async Task<string> DeleteEntity(
        EngineBridge engine,
        [Description("The GUID of the entity to delete")] string entityId)
    {
        return await engine.DeleteAsync($"/api/v1/scene/entities/{entityId}");
    }

    [McpServerTool, Description("Get information about the current scene and its initial scene URL.")]
    public static async Task<string> GetScenes(EngineBridge engine)
    {
        return await engine.GetAsync("/api/v1/scene/scenes");
    }
}

/// <summary>
/// MCP tools for editor interaction.
/// </summary>
[McpServerToolType]
public static class EditorTools
{
    [McpServerTool, Description("Get the editor status including window info and graphics device details.")]
    public static async Task<string> GetEditorStatus(EngineBridge engine)
    {
        return await engine.GetAsync("/api/v1/editor/status");
    }
}

/// <summary>
/// MCP tools for debugging and log access.
/// </summary>
[McpServerToolType]
public static class DebugTools
{
    [McpServerTool, Description("Get recent engine log entries. Returns structured log data with levels, modules, and messages.")]
    public static async Task<string> GetLogs(
        EngineBridge engine,
        [Description("Number of log entries to retrieve (default 50)")] int count = 50)
    {
        return await engine.GetAsync($"/api/v1/debug/logs?count={count}");
    }

    [McpServerTool, Description("Get console output (excludes verbose messages). Useful for debugging engine issues.")]
    public static async Task<string> GetConsole(
        EngineBridge engine,
        [Description("Number of console lines to retrieve (default 100)")] int count = 100)
    {
        return await engine.GetAsync($"/api/v1/debug/console?count={count}");
    }
}
