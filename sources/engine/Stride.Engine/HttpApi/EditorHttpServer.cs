// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// </summary>
    public class EditorHttpServer : IDisposable
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi");

        private readonly HttpListener _listener;
        private readonly int _port;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<LogEntry> _recentLogs = new();
        private const int MaxLogEntries = 500;
        private bool _logHooked = false;
        private readonly object _logLock = new();

        // Editor state (set after editor initializes)
        private string? _currentProjectPath;
        private string? _editorStatus = "starting";
        private Func<Task<string?>>? _getEditorInfo;

        private class LogEntry
        {
            public string Level { get; set; } = "";
            public string Module { get; set; } = "";
            public string Message { get; set; } = "";
            public string Timestamp { get; set; } = "";
        }

        public EditorHttpServer(int port = 9876)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        /// <summary>
        /// Set the current project path (call when project opens).
        /// </summary>
        public void SetCurrentProject(string? projectPath)
        {
            _currentProjectPath = projectPath;
            _editorStatus = projectPath != null ? "project-loaded" : "ready";
        }

        /// <summary>
        /// Set a function that returns editor info JSON.
        /// </summary>
        public void SetEditorInfoProvider(Func<Task<string?>> provider)
        {
            _getEditorInfo = provider;
        }

        private void EnsureLogHook()
        {
            if (_logHooked) return;
            lock (_logLock)
            {
                if (_logHooked) return;
                GlobalLogger.GlobalMessageLogged += (logMessage) =>
                {
                    var entry = new LogEntry
                    {
                        Level = logMessage.Type.ToString(),
                        Module = logMessage.Module ?? "",
                        Message = logMessage.Text ?? "",
                        Timestamp = DateTime.UtcNow.ToString("O")
                    };
                    _recentLogs.Enqueue(entry);
                    while (_recentLogs.Count > MaxLogEntries)
                        _recentLogs.TryDequeue(out _);
                };
                _logHooked = true;
            }
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
                if (ctx.Request.HasEntityBody)
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    body = await reader.ReadToEndAsync(ct);
                }

                var response = await RouteRequest(method, url, body);
                ctx.Response.ContentType = "application/json";
                ctx.Response.StatusCode = 200;
                using var writer = new StreamWriter(ctx.Response.OutputStream);
                writer.Write(response);
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

        private async Task<string> RouteRequest(string method, string url, string? body)
        {
            // Status
            if (method == "GET" && url == "/api/v1/status")
            {
                return JsonSerializer.Serialize(new
                {
                    status = "running",
                    engine = "Modulus Engine",
                    editor = _editorStatus,
                    currentProject = _currentProjectPath,
                    time = DateTime.UtcNow
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Editor status
            if (method == "GET" && url == "/api/v1/editor/status")
            {
                var editorInfo = _getEditorInfo != null ? await _getEditorInfo() : null;
                return JsonSerializer.Serialize(new
                {
                    status = _editorStatus,
                    currentProject = _currentProjectPath,
                    editorInfo = editorInfo
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Debug logs
            if (method == "GET" && url.StartsWith("/api/v1/debug/logs"))
            {
                EnsureLogHook();
                var count = 50;
                var q = System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost" + url).Query);
                if (q["count"] != null && int.TryParse(q["count"], out var c))
                    count = Math.Clamp(c, 1, MaxLogEntries);

                var logs = _recentLogs.TakeLast(count).Select(l => new
                {
                    level = l.Level,
                    module = l.Module,
                    message = l.Message,
                    timestamp = l.Timestamp
                }).ToList();

                return JsonSerializer.Serialize(new { logs, total = _recentLogs.Count }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Debug console
            if (method == "GET" && url.StartsWith("/api/v1/debug/console"))
            {
                EnsureLogHook();
                var count = 100;
                var q = System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost" + url).Query);
                if (q["count"] != null && int.TryParse(q["count"], out var c))
                    count = Math.Clamp(c, 1, MaxLogEntries);

                var console = _recentLogs
                    .Where(l => l.Level != "Verbose")
                    .TakeLast(count)
                    .Select(l => $"[{l.Level}] [{l.Module}] {l.Message}")
                    .ToList();

                return JsonSerializer.Serialize(new { console, total = console.Count }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Mod stubs (501)
            if (url.StartsWith("/api/v1/mod/"))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Not implemented",
                    status = 501,
                    message = "Mod system pending Phase 3-6"
                });
            }

            // Render stubs (501)
            if (url.StartsWith("/api/v1/render/"))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Not implemented",
                    status = 501,
                    message = "Render API pending Phase 4"
                });
            }

            // Asset stubs
            if (url.StartsWith("/api/v1/asset/"))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Not implemented",
                    status = 501,
                    message = "Asset API pending Phase 3"
                });
            }

            // 404
            return JsonSerializer.Serialize(new { error = "Not found", path = url, method });
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
        }
    }
}
