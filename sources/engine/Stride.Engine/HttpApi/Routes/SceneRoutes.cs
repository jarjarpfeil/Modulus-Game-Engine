// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stride.Engine.HttpApi.Routes
{
    public static class SceneRoutes
    {
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        public static async Task<string?> HandleRequest(string method, string url, string? body, HttpApiSystem api)
        {
            object? result = null;

            if (method == "GET" && url == "/api/v1/scene/entities")
                result = await GetEntities(api);
            else if (method == "GET" && url.StartsWith("/api/v1/scene/entities/"))
                result = await GetEntity(api, url.Split('/').Last());
            else if (method == "POST" && url == "/api/v1/scene/entities")
                result = await CreateEntity(api, body);
            else if (method == "DELETE" && url.StartsWith("/api/v1/scene/entities/"))
                result = await DeleteEntity(api, url.Split('/').Last());
            else if (method == "GET" && url == "/api/v1/scene/scenes")
                result = await GetScenes(api);
            else
                return JsonSerializer.Serialize(new { error = "Not found", path = url, method }, _jsonOpts);

            return JsonSerializer.Serialize(result, _jsonOpts);
        }

        private static Task<object> GetEntities(HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var sceneSystem = api.Game?.Services.GetService<SceneSystem>();
                var scene = sceneSystem?.SceneInstance;
                if (scene == null)
                    return (object)new { error = "No scene loaded" };

                var entities = scene.Select(e => new
                {
                    id = e.Id.ToString(),
                    name = e.Name,
                    componentCount = e.Components.Count,
                    hasParent = e.GetParent() != null,
                    parentId = e.GetParent()?.Id.ToString(),
                    position = new { x = e.Transform.Position.X, y = e.Transform.Position.Y, z = e.Transform.Position.Z }
                }).ToList();

                return (object)new { entities, count = entities.Count };
            });
        }

        private static Task<object> GetEntity(HttpApiSystem api, string idStr)
        {
            return api.EnqueueOnGameThread(() =>
            {
                if (!Guid.TryParse(idStr, out var id))
                    return (object)new { error = "Invalid entity ID" };

                var sceneSystem = api.Game?.Services.GetService<SceneSystem>();
                var scene = sceneSystem?.SceneInstance;
                if (scene == null)
                    return (object)new { error = "No scene loaded" };

                var entity = scene.FirstOrDefault(e => e.Id == id);
                if (entity == null)
                    return (object)new { error = "Entity not found" };

                var components = entity.Components.Select(c => new
                {
                    type = c.GetType().Name,
                    fullType = c.GetType().FullName
                }).ToList();

                return (object)new
                {
                    id = entity.Id.ToString(),
                    name = entity.Name,
                    position = new { x = entity.Transform.Position.X, y = entity.Transform.Position.Y, z = entity.Transform.Position.Z },
                    rotation = new { x = entity.Transform.Rotation.X, y = entity.Transform.Rotation.Y, z = entity.Transform.Rotation.Z, w = entity.Transform.Rotation.W },
                    scale = new { x = entity.Transform.Scale.X, y = entity.Transform.Scale.Y, z = entity.Transform.Scale.Z },
                    components,
                    children = entity.GetChildren().Select(c => new { id = c.Id.ToString(), name = c.Name }).ToList()
                };
            });
        }

        private static Task<object> CreateEntity(HttpApiSystem api, string? body)
        {
            return api.EnqueueOnGameThread(() =>
            {
                string? name = null;
                string? parentIdStr = null;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        var doc = JsonSerializer.Deserialize<JsonElement>(body);
                        if (doc.TryGetProperty("name", out var nameProp))
                            name = nameProp.GetString();
                        if (doc.TryGetProperty("parentId", out var parentIdProp))
                            parentIdStr = parentIdProp.GetString();
                    }
                    catch { }
                }

                name ??= "New Entity";

                var sceneSystem = api.Game?.Services.GetService<SceneSystem>();
                var scene = sceneSystem?.SceneInstance;
                if (scene == null)
                    return (object)new { error = "No scene loaded" };

                var entity = new Entity(name);

                if (parentIdStr != null && Guid.TryParse(parentIdStr, out var parentId))
                {
                    var parent = scene.FirstOrDefault(e => e.Id == parentId);
                    if (parent != null)
                        entity.SetParent(parent);
                }

                scene.Add(entity);

                return (object)new { id = entity.Id.ToString(), name = entity.Name, message = "Entity created" };
            });
        }

        private static Task<object> DeleteEntity(HttpApiSystem api, string idStr)
        {
            return api.EnqueueOnGameThread(() =>
            {
                if (!Guid.TryParse(idStr, out var id))
                    return (object)new { error = "Invalid entity ID" };

                var sceneSystem = api.Game?.Services.GetService<SceneSystem>();
                var scene = sceneSystem?.SceneInstance;
                if (scene == null)
                    return (object)new { error = "No scene loaded" };

                var entity = scene.FirstOrDefault(e => e.Id == id);
                if (entity == null)
                    return (object)new { error = "Entity not found" };

                scene.Remove(entity);
                return (object)new { message = "Entity deleted", id = idStr };
            });
        }

        private static Task<object> GetScenes(HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var sceneSystem = api.Game?.Services.GetService<SceneSystem>();
                var scene = sceneSystem?.SceneInstance;

                return (object)new
                {
                    currentScene = scene != null ? new { entityCount = scene.Count } : null,
                    initialSceneUrl = sceneSystem?.InitialSceneUrl
                };
            });
        }
    }
}
