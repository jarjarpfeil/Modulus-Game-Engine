// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stride.Core.Diagnostics;

namespace Stride.Engine.HttpApi
{
    /// <summary>
    /// Standalone HTTP server for editor and game runtime.
    /// Runs on a background thread, independent of the game loop.
    /// Supports dynamic handler registration so both editor and game
    /// can register their routes on the same server.
    /// </summary>
    public class EditorHttpServer : IDisposable
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi");

        private readonly HttpListener _listener;
        private readonly int _port;
        private readonly CancellationTokenSource _cts = new();
        private Func<string, string, string?, Task<string?>>? _requestHandler;

        /// <summary>
        /// Shared instance — editor creates it, game runtime reuses it.
        /// </summary>
        public static EditorHttpServer? Instance { get; private set; }

        public EditorHttpServer(int port = 9876)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            Instance = this;
        }

        /// <summary>
        /// Set the request handler function.
        /// Signature: (method, url, body) -> JSON response.
        /// </summary>
        public void SetRequestHandler(Func<string, string, string?, Task<string?>> handler)
        {
            _requestHandler = handler;
        }

        public void Start()
        {
            if (_listener.IsListening) return; // Already started

            try
            {
                _listener.Start();
                Log.Info($"[HttpApi] HTTP server listening on http://localhost:{_port}/");
                _ = Task.Run(() => AcceptLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                Log.Warning($"[HttpApi] Failed to start HTTP server: {ex.Message}");
            }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(ctx, ct));
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[HttpApi] Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var method = ctx.Request.HttpMethod;
            var url = ctx.Request.Url!.AbsolutePath;
            string? body = null;

            try
            {
                if (ctx.Request.HasEntityBody)
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    body = await reader.ReadToEndAsync(ct);
                }

                string? response;
                if (_requestHandler != null)
                {
                    response = await _requestHandler(method, url, body);
                }
                else
                {
                    response = JsonSerializer.Serialize(new
                    {
                        status = "ok",
                        message = "Modulus Engine HTTP server running (no handler set)",
                        time = DateTime.UtcNow
                    });
                }

                ctx.Response.ContentType = "application/json";
                ctx.Response.StatusCode = 200;
                using var writer = new StreamWriter(ctx.Response.OutputStream);
                writer.Write(response ?? "{}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[HttpApi] {method} {url} error: {ex.Message}");
                try
                {
                    ctx.Response.StatusCode = 500;
                    var errJson = JsonSerializer.Serialize(new { error = ex.Message });
                    ctx.Response.ContentType = "application/json";
                    using var writer = new StreamWriter(ctx.Response.OutputStream);
                    writer.Write(errJson);
                }
                catch { }
            }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            if (Instance == this) Instance = null;
        }
    }
}
