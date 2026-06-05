// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Net;
using System.Threading.Tasks;
using Stride.Graphics;

namespace Stride.Engine.HttpApi.Routes
{
    public static class EditorRoutes
    {
        public static Task<object> GetStatus(HttpListenerRequest req, HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                var game = api.Game;
                var graphicsDevice = game?.Services.GetService<GraphicsDevice>();

                return (object)new
                {
                    isRunning = game != null,
                    isExiting = game?.IsExiting ?? false,
                    windowTitle = game?.Window?.Title ?? "Modulus Engine",
                    graphics = graphicsDevice != null ? new
                    {
                        renderer = graphicsDevice.RendererName,
                        platform = GraphicsDevice.Platform.ToString()
                    } : null
                };
            });
        }

        public static Task<object> Launch(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new
            {
                message = "Editor is already running (integrated mode)",
                status = "ok"
            });
        }

        public static Task<object> Screenshot(HttpListenerRequest req, HttpApiSystem api)
        {
            // Stub — actual screenshot requires GPU readback in Draw phase
            return Task.FromResult<object>(new
            {
                error = "Not implemented",
                status = 501,
                message = "Screenshot capture will be implemented with GPU readback support"
            });
        }
    }
}
