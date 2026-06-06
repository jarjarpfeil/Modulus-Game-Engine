// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Engine.HttpApi.Routes;
using Stride.Games;

namespace Stride.Engine.HttpApi
{
    /// <summary>
    /// Game system that registers HTTP API routes with the shared EditorHttpServer.
    /// When the game starts, it adds game-specific routes (scene CRUD, etc.)
    /// to the existing editor HTTP server.
    /// </summary>
    public class HttpApiSystem : GameSystemBase
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi");

        private Func<string, string, string?, Task<string?>>? _previousHandler;
        private Func<string, string, string?, Task<string?>>? _editorHandler;

        /// <summary>
        /// Queue for ECS reads/writes — drained during Update().
        /// </summary>
        private readonly ConcurrentQueue<WorkItem> _updateQueue = new();

        private class WorkItem
        {
            public Action Action { get; set; } = null!;
            public TaskCompletionSource<object>? Completion { get; set; }
        }

        public HttpApiSystem(IServiceRegistry registry)
            : base(registry)
        {
        }

        /// <summary>
        /// Enqueue work on the game thread (Update phase). Used by all route handlers.
        /// </summary>
        public Task<object> EnqueueOnGameThread(Func<object> work)
        {
            var tcs = new TaskCompletionSource<object>();
            _updateQueue.Enqueue(new WorkItem
            {
                Action = () =>
                {
                    try
                    {
                        tcs.SetResult(work());
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                Completion = tcs
            });
            return tcs.Task;
        }

        private async Task<string?> HandleGameRequest(string method, string url, string? body)
        {
            // Scene routes (game runtime only)
            if (url.StartsWith("/api/v1/scene/"))
            {
                return await SceneRoutes.HandleRequest(method, url, body, this);
            }

            // Mod routes
            if (url.StartsWith("/api/v1/mod/"))
            {
                return await ModRoutes.HandleRequest(method, url, body, this);
            }

            // Asset routes
            if (url.StartsWith("/api/v1/asset/"))
            {
                var result = await AssetRoutes.HandleRequest(method, url, body, null, this);
                if (result != null) return result;
            }

            // Editor routes
            if (url.StartsWith("/api/v1/editor/"))
            {
                var result = await EditorRoutes.HandleRequest(method, url, body, this);
                if (result != null) return result;
            }

            // Status with game info
            if (method == "GET" && url == "/api/v1/status")
            {
                var sceneSystem = Game?.Services.GetService<SceneSystem>();
                var graphicsDevice = Game?.Services.GetService<Graphics.GraphicsDevice>();
                var modHost = Game?.Services.GetService<Modding.ModHost>();

                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "running",
                    engine = "Modulus Engine",
                    mode = "game-runtime",
                    entityCount = sceneSystem?.SceneInstance?.Count ?? 0,
                    graphics = graphicsDevice != null ? new
                    {
                        renderer = graphicsDevice.RendererName,
                        platform = Graphics.GraphicsDevice.Platform.ToString()
                    } : null,
                    mods = modHost != null ? new
                    {
                        loaded = modHost.LoadedMods.Count,
                        modsDirectory = modHost.ModsDirectory
                    } : null,
                    time = DateTime.UtcNow
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }

            // Delegate to previous handler (editor) for all other routes
            if (_previousHandler != null)
            {
                return await _previousHandler(method, url, body);
            }

            return System.Text.Json.JsonSerializer.Serialize(new { error = "No handler available" });
        }

        public override void Initialize()
        {
            base.Initialize();

            // Wire ModHost into ModRoutes so mod API endpoints work
            var modHost = Game?.Services.GetService<Modding.ModHost>();
            if (modHost != null)
            {
                ModRoutes.SetModHost(modHost);
                Log.Info("[HttpApi] ModHost connected to mod routes");
            }

            // Register with the shared EditorHttpServer
            var server = EditorHttpServer.Instance;
            if (server != null)
            {
                // Save the editor handler before replacing it
                _editorHandler = server.GetRequestHandler();
                _previousHandler = _editorHandler;
                server.SetRequestHandler(HandleGameRequest);
                Log.Info("[HttpApi] Game runtime registered with shared HTTP server");
            }
            else
            {
                Log.Warning("[HttpApi] No EditorHttpServer instance found — game routes not available");
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // Drain the update queue — process ECS work on the game thread
            // Limit items per frame to prevent hitching (max 50ms worth of work)
            var frameStart = DateTime.UtcNow;
            const int maxItemsPerFrame = 100;
            int processed = 0;
            
            while (_updateQueue.TryDequeue(out var item) && processed < maxItemsPerFrame)
            {
                try
                {
                    item.Action();
                    processed++;
                    
                    // Check if we've spent too long on HTTP work this frame
                    if ((DateTime.UtcNow - frameStart).TotalMilliseconds > 50)
                    {
                        Log.Debug($"[HttpApi] Frame budget exhausted after {processed} items — deferring remaining work");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[HttpApi] Update work item failed: {ex.Message}");
                    item.Completion?.TrySetException(ex);
                }
            }
        }

        protected override void Destroy()
        {
            // Restore the editor handler when game stops
            var server = EditorHttpServer.Instance;
            if (server != null && _editorHandler != null)
            {
                server.SetRequestHandler(_editorHandler);
                Log.Info("[HttpApi] Game runtime unregistered — editor handler restored");
            }
            base.Destroy();
        }
    }
}
