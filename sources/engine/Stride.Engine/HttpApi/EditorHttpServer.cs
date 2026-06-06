// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
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
        
        // Rate limiting: max concurrent requests and per-IP tracking
        private readonly SemaphoreSlim _concurrentRequestLimit = new(initialCount: 20, maxCount: 20);
        private readonly Dictionary<string, Queue<DateTime>> _requestTimestamps = new();
        private readonly object _rateLimitLock = new();
        private const int MaxRequestsPerSecond = 50;

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

        /// <summary>
        /// Gets the current request handler (for saving/restoring when game starts/stops).
        /// </summary>
        public Func<string, string, string?, Task<string?>>? GetRequestHandler()
        {
            return _requestHandler;
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
            var clientIp = ctx.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
            string? body = null;

            // Rate limiting check
            if (!IsRequestAllowed(clientIp))
            {
                ctx.Response.StatusCode = 429; // Too Many Requests
                var rateLimitJson = JsonSerializer.Serialize(new { error = "Rate limit exceeded", retryAfterMs = 1000 });
                ctx.Response.ContentType = "application/json";
                using var writer = new StreamWriter(ctx.Response.OutputStream);
                writer.Write(rateLimitJson);
                try { ctx.Response.Close(); } catch { }
                return;
            }

            // Semaphore to limit concurrent requests
            if (!await _concurrentRequestLimit.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                ctx.Response.StatusCode = 503; // Service Unavailable
                var busyJson = JsonSerializer.Serialize(new { error = "Server busy, try again later" });
                ctx.Response.ContentType = "application/json";
                using var writer = new StreamWriter(ctx.Response.OutputStream);
                writer.Write(busyJson);
                try { ctx.Response.Close(); } catch { }
                return;
            }

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
                _concurrentRequestLimit.Release();
                try { ctx.Response.Close(); } catch { }
            }
        }

        /// <summary>
        /// Checks if a request from the given IP is allowed (not exceeding rate limits).
        /// </summary>
        private bool IsRequestAllowed(string clientIp)
        {
            lock (_rateLimitLock)
            {
                var now = DateTime.UtcNow;
                
                if (!_requestTimestamps.TryGetValue(clientIp, out var timestamps))
                {
                    timestamps = new Queue<DateTime>();
                    _requestTimestamps[clientIp] = timestamps;
                }
                
                // Remove timestamps older than 1 second
                while (timestamps.Count > 0 && (now - timestamps.Peek()).TotalSeconds > 1.0)
                    timestamps.Dequeue();
                
                // Check if under limit
                if (timestamps.Count >= MaxRequestsPerSecond)
                    return false;
                
                timestamps.Enqueue(now);
                return true;
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
