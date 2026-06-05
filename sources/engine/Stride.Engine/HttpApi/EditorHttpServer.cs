// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
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
    /// For game runtime, use HttpApiSystem instead (marshals to game thread).
    /// </summary>
    public class EditorHttpServer : IDisposable
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi");

        private readonly HttpListener _listener;
        private readonly int _port;
        private readonly CancellationTokenSource _cts = new();
        private Func<string, string?, Task<string?>>? _requestHandler;

        public EditorHttpServer(int port = 9876)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        /// <summary>
        /// Sets the request handler. Signature: (method+path, body) -> response JSON.
        /// </summary>
        public void SetHandler(Func<string, string?, Task<string?>> handler)
        {
            _requestHandler = handler;
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                Log.Info($"[HttpApi] Editor HTTP server listening on http://localhost:{_port}/");
                _ = Task.Run(() => AcceptLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                Log.Warning($"[HttpApi] Failed to start editor HTTP server: {ex.Message}");
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
                // Read request body if present
                if (ctx.Request.HasEntityBody)
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    body = await reader.ReadToEndAsync(ct);
                }

                var key = $"{method} {url}";
                string? response;

                if (_requestHandler != null)
                {
                    response = await _requestHandler(key, body);
                }
                else
                {
                    response = JsonSerializer.Serialize(new { status = "ok", message = "Editor HTTP server running", time = DateTime.UtcNow });
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
        }
    }
}
