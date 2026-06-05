// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stride.Engine.HttpApi.Routes
{
    public static class SceneRoutes
    {
        public static Task<object> GetEntities(HttpListenerRequest req, HttpApiSystem api)
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

        public static Task<object> GetEntity(HttpListenerRequest req, HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var idStr = GetPathParam(req, "id");
                if (idStr == null || !Guid.TryParse(idStr, out var id))
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

        public static Task<object> CreateEntity(HttpListenerRequest req, HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var body = ReadBody(req);
                var name = body?.TryGetProperty("name", out var nameProp) == true ? nameProp.GetString() : "New Entity";

                var sceneSystem = api.Game?.Services.GetService<SceneSystem>();
                var scene = sceneSystem?.SceneInstance;
                if (scene == null)
                    return (object)new { error = "No scene loaded" };

                var entity = new Entity(name ?? "New Entity");

                // Optional parent
                if (body?.TryGetProperty("parentId", out var parentIdProp) == true
                    && Guid.TryParse(parentIdProp.GetString(), out var parentId))
                {
                    var parent = scene.FirstOrDefault(e => e.Id == parentId);
                    if (parent != null)
                    {
                        entity.SetParent(parent);
                    }
                }

                scene.Add(entity);

                return (object)new
                {
                    id = entity.Id.ToString(),
                    name = entity.Name,
                    message = "Entity created"
                };
            });
        }

        public static Task<object> DeleteEntity(HttpListenerRequest req, HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var idStr = GetPathParam(req, "id");
                if (idStr == null || !Guid.TryParse(idStr, out var id))
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

        public static Task<object> GetScenes(HttpListenerRequest req, HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var sceneSystem = api.Game?.Services.GetService<SceneSystem>();
                var scene = sceneSystem?.SceneInstance;

                return (object)new
                {
                    currentScene = scene != null ? new
                    {
                        entityCount = scene.Count
                    } : null,
                    initialSceneUrl = sceneSystem?.InitialSceneUrl
                };
            });
        }

        private static string? GetPathParam(HttpListenerRequest req, string name)
        {
            try
            {
                var paramsJson = req.QueryString["___pathParams"];
                if (paramsJson != null)
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(paramsJson);
                    if (dict?.TryGetValue(name, out var value) == true)
                        return value;
                }
            }
            catch { }
            return null;
        }

        private static JsonElement? ReadBody(HttpListenerRequest req)
        {
            try
            {
                using var reader = new StreamReader(req.InputStream);
                var body = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(body)) return null;
                return JsonSerializer.Deserialize<JsonElement>(body);
            }
            catch { return null; }
        }
    }
}
