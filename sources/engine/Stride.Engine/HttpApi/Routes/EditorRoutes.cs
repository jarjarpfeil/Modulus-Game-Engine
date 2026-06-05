// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Stride.Core.Diagnostics;
using Stride.Graphics;

namespace Stride.Engine.HttpApi.Routes
{
    /// <summary>
    /// HTTP API routes for editor operations (/api/v1/editor/*).
    /// </summary>
    public static class EditorRoutes
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi.EditorRoutes");

        /// <summary>
        /// Dispatches editor route requests.
        /// </summary>
        public static Task<string?> HandleRequest(string method, string url, string? body, HttpApiSystem api)
        {
            return url switch
            {
                "/api/v1/editor/status" when method == "GET" => Task.FromResult<string?>(HandleStatus(api)),
                "/api/v1/editor/launch" when method == "POST" => Task.FromResult<string?>(HandleLaunch()),
                "/api/v1/editor/screenshot" when method == "POST" => Task.FromResult<string?>(HandleScreenshot(api)),
                _ => Task.FromResult<string?>(JsonSerializer.Serialize(new { error = "Unknown editor route", path = url }))
            };
        }

        private static string HandleStatus(HttpApiSystem api)
        {
            var game = api.Game;
            var graphicsDevice = game?.Services.GetService<GraphicsDevice>();

            return JsonSerializer.Serialize(new
            {
                isRunning = game != null,
                isExiting = game?.IsExiting ?? false,
                windowTitle = game?.Window?.Title ?? "Modulus Engine",
                graphics = graphicsDevice != null ? new
                {
                    renderer = graphicsDevice.RendererName,
                    platform = GraphicsDevice.Platform.ToString()
                } : null
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string HandleLaunch()
        {
            return JsonSerializer.Serialize(new
            {
                message = "Editor is already running (integrated mode)",
                status = "ok"
            });
        }

        /// <summary>
        /// Screenshot capture. GPU readback requires platform-specific texture staging
        /// which varies between Vulkan, Direct3D, and OpenGL backends. This endpoint
        /// returns the viewport dimensions and graphics info so agents can use the
        /// editor's built-in screenshot mechanism instead.
        ///
        /// Full GPU readback is planned for a future iteration when the rendering
        /// pipeline is more mature.
        /// </summary>
        private static string HandleScreenshot(HttpApiSystem api)
        {
            var game = api.Game;
            if (game == null)
                return JsonSerializer.Serialize(new { error = "Game not initialized" });

            var graphicsDevice = game.Services.GetService<GraphicsDevice>();
            if (graphicsDevice == null)
                return JsonSerializer.Serialize(new { error = "Graphics device not available" });

            var presenter = graphicsDevice.Presenter;
            var backBuffer = presenter?.BackBuffer;

            return JsonSerializer.Serialize(new
            {
                status = "partial",
                message = "GPU screenshot capture requires platform-specific backbuffer readback. Use editor screenshot tools (Windows Graphics Capture) for now.",
                viewport = backBuffer != null ? new
                {
                    width = backBuffer.Width,
                    height = backBuffer.Height,
                    format = backBuffer.Format.ToString()
                } : null,
                renderer = graphicsDevice.RendererName,
                platform = GraphicsDevice.Platform.ToString(),
                hint = "For automated screenshots, use the GameStudio's EditorApiHandler or the Windows Graphics Capture API"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
