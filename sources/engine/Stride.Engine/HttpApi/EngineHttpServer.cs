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
    /// Embedded HTTP server for engine API access.
    /// Listens on localhost and routes requests to engine handlers.
    /// </summary>
    public class EngineHttpServer : IDisposable
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi");

        private readonly HttpListener _listener;
        private readonly Dictionary<string, Func<HttpListenerRequest, Task<object>>> _routes = new();
        private readonly int _port;

        public EngineHttpServer(int port = 9876)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        /// <summary>
        /// Registers a route handler for a given method and path template.
        /// Path templates support {param} placeholders: "/api/v1/scene/entities/{id}"
        /// </summary>
        public void RegisterRoute(string method, string path, Func<HttpListenerRequest, Task<object>> handler)
        {
            _routes[$"{method} {path}"] = handler;
        }

        private string? MatchRoute(string method, string url, out Dictionary<string, string> pathParams)
        {
            pathParams = new();
            foreach (var (key, _) in _routes)
            {
                var parts = key.Split(' ', 2);
                if (parts[0] != method) continue;
                if (TryMatchTemplate(parts[1], url, pathParams))
                    return key;
            }
            return null;
        }

        private bool TryMatchTemplate(string template, string url, Dictionary<string, string> pathParams)
        {
            var tSegs = template.Split('/');
            var uSegs = url.Split('/');
            if (tSegs.Length != uSegs.Length) return false;
            for (int i = 0; i < tSegs.Length; i++)
            {
                if (tSegs[i].StartsWith('{') && tSegs[i].EndsWith('}'))
                    pathParams[tSegs[i][1..^1]] = uSegs[i];
                else if (tSegs[i] != uSegs[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Starts the HTTP listener and processes requests until cancellation.
        /// </summary>
        public async Task StartAsync(CancellationToken ct)
        {
            try
            {
                _listener.Start();
                Log.Info($"[HttpApi] Listening on http://localhost:{_port}/");

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();
                        _ = Task.Run(() => HandleRequestAsync(ctx));
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
            catch (Exception ex)
            {
                Log.Error($"[HttpApi] Failed to start: {ex.Message}");
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            var method = ctx.Request.HttpMethod;
            var url = ctx.Request.Url!.AbsolutePath;

            try
            {
                var match = MatchRoute(method, url, out var pathParams);
                if (match != null && _routes.TryGetValue(match, out var handler))
                {
                    // Store path params in request for handlers
                    ctx.Request.QueryString.Add("___pathParams", JsonSerializer.Serialize(pathParams));

                    var result = await handler(ctx.Request).WithTimeout(TimeSpan.FromSeconds(5));
                    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.StatusCode = 200;
                    using var writer = new StreamWriter(ctx.Response.OutputStream);
                    writer.Write(json);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    var json = JsonSerializer.Serialize(new { error = "Not found", path = url, method });
                    ctx.Response.ContentType = "application/json";
                    using var writer = new StreamWriter(ctx.Response.OutputStream);
                    writer.Write(json);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[HttpApi] {method} {url} failed: {ex}");
                try
                {
                    ctx.Response.StatusCode = 500;
                    var json = JsonSerializer.Serialize(new { error = ex.Message });
                    ctx.Response.ContentType = "application/json";
                    using var writer = new StreamWriter(ctx.Response.OutputStream);
                    writer.Write(json);
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
            try { _listener.Stop(); } catch { }
        }
    }
}
