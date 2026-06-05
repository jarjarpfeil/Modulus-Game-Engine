// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Net;
using System.Threading.Tasks;
using Stride.Graphics;

namespace Stride.Engine.HttpApi.Routes
{
    public static class StatusRoutes
    {
        public static Task<object> GetStatus(HttpListenerRequest req, HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var game = api.Game;
                var sceneSystem = game?.Services.GetService<SceneSystem>();
                var graphicsDevice = game?.Services.GetService<GraphicsDevice>();

                return (object)new
                {
                    status = "running",
                    engine = "Modulus Engine (Stride fork)",
                    version = typeof(Game).Assembly.GetName().Version?.ToString() ?? "unknown",
                    platform = Environment.OSVersion.ToString(),
                    dotnet = Environment.Version.ToString(),
                    scene = sceneSystem?.SceneInstance != null ? "loaded" : "none",
                    entityCount = sceneSystem?.SceneInstance?.Count ?? 0,
                    graphics = graphicsDevice != null ? new
                    {
                        renderer = graphicsDevice.RendererName,
                        platform = GraphicsDevice.Platform.ToString()
                    } : null
                };
            });
        }
    }
}
