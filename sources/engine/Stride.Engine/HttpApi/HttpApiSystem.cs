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
    /// Game system that hosts the embedded HTTP API server.
    /// All HTTP requests are marshaled to the game thread via queues.
    /// </summary>
    public class HttpApiSystem : GameSystemBase
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi");

        private readonly EngineHttpServer _server;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Queue for ECS reads/writes — drained during Update().
        /// </summary>
        private readonly ConcurrentQueue<WorkItem> _updateQueue = new();

        /// <summary>
        /// Queue for GPU commands — drained during Draw().
        /// </summary>
        private readonly ConcurrentQueue<WorkItem> _drawQueue = new();

        private class WorkItem
        {
            public Action Action { get; set; } = null!;
            public TaskCompletionSource<object>? Completion { get; set; }
        }

        public HttpApiSystem(IServiceRegistry registry)
            : base(registry)
        {
            _server = new EngineHttpServer(9876);
            RegisterRoutes();
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

        /// <summary>
        /// Enqueue work on the game thread (Update phase) with async result.
        /// </summary>
        public Task<object> EnqueueOnGameThread(Func<Task<object>> work)
        {
            var tcs = new TaskCompletionSource<object>();
            _updateQueue.Enqueue(new WorkItem
            {
                Action = () =>
                {
                    try
                    {
                        var task = work();
                        task.ContinueWith(t =>
                        {
                            if (t.IsFaulted) tcs.SetException(t.Exception!);
                            else tcs.SetResult(t.Result);
                        });
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

        private void RegisterRoutes()
        {
            // Status
            _server.RegisterRoute("GET", "/api/v1/status", req =>
                StatusRoutes.GetStatus(req, this));

            // Scene routes
            _server.RegisterRoute("GET", "/api/v1/scene/entities", req =>
                SceneRoutes.GetEntities(req, this));
            _server.RegisterRoute("GET", "/api/v1/scene/entities/{id}", req =>
                SceneRoutes.GetEntity(req, this));
            _server.RegisterRoute("POST", "/api/v1/scene/entities", req =>
                SceneRoutes.CreateEntity(req, this));
            _server.RegisterRoute("DELETE", "/api/v1/scene/entities/{id}", req =>
                SceneRoutes.DeleteEntity(req, this));
            _server.RegisterRoute("GET", "/api/v1/scene/scenes", req =>
                SceneRoutes.GetScenes(req, this));

            // Asset routes (stubs)
            _server.RegisterRoute("GET", "/api/v1/asset/list", req =>
                AssetRoutes.ListAssets(req, this));
            _server.RegisterRoute("POST", "/api/v1/asset/import", req =>
                AssetRoutes.ImportAsset(req, this));
            _server.RegisterRoute("POST", "/api/v1/asset/build", req =>
                AssetRoutes.BuildAsset(req, this));

            // Editor routes
            _server.RegisterRoute("GET", "/api/v1/editor/status", req =>
                EditorRoutes.GetStatus(req, this));
            _server.RegisterRoute("POST", "/api/v1/editor/launch", req =>
                EditorRoutes.Launch(req, this));
            _server.RegisterRoute("POST", "/api/v1/editor/screenshot", req =>
                EditorRoutes.Screenshot(req, this));

            // Mod routes (501 stubs)
            _server.RegisterRoute("POST", "/api/v1/mod/install", req =>
                ModRoutes.Install(req, this));
            _server.RegisterRoute("POST", "/api/v1/mod/enable/{id}", req =>
                ModRoutes.Enable(req, this));
            _server.RegisterRoute("POST", "/api/v1/mod/disable/{id}", req =>
                ModRoutes.Disable(req, this));
            _server.RegisterRoute("POST", "/api/v1/mod/reload/{id}", req =>
                ModRoutes.Reload(req, this));
            _server.RegisterRoute("GET", "/api/v1/mod/list", req =>
                ModRoutes.List(req, this));

            // Render routes (501 stubs)
            _server.RegisterRoute("POST", "/api/v1/render/shader/compile", req =>
                RenderRoutes.CompileShader(req, this));
            _server.RegisterRoute("GET", "/api/v1/render/shaders", req =>
                RenderRoutes.ListShaders(req, this));

            // Debug routes
            _server.RegisterRoute("GET", "/api/v1/debug/logs", req =>
                DebugRoutes.GetLogs(req, this));
            _server.RegisterRoute("GET", "/api/v1/debug/console", req =>
                DebugRoutes.GetConsole(req, this));
        }

        public override void Initialize()
        {
            base.Initialize();
            _ = _server.StartAsync(_cts.Token);
            Log.Info("[HttpApi] System initialized, server starting on port 9876");
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // Drain the update queue — process ECS work on the game thread
            while (_updateQueue.TryDequeue(out var item))
            {
                try
                {
                    item.Action();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[HttpApi] Update work item failed: {ex.Message}");
                    item.Completion?.TrySetException(ex);
                }
            }
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            // Drain the draw queue — process GPU work on the render thread
            while (_drawQueue.TryDequeue(out var item))
            {
                try
                {
                    item.Action();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[HttpApi] Draw work item failed: {ex.Message}");
                    item.Completion?.TrySetException(ex);
                }
            }
        }

        protected override void Destroy()
        {
            _cts.Cancel();
            _server.Dispose();
            base.Destroy();
        }
    }
}
