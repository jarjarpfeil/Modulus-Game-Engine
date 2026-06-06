// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Stride.Core.Diagnostics;
using Stride.Engine.Modding;

namespace Stride.Engine.HttpApi.Routes
{
    /// <summary>
    /// HTTP API routes for mod management (/api/v1/mod/*).
    /// </summary>
    public static class ModRoutes
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi.ModRoutes");
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        // Shared ModHost instance — set by the editor or game system on startup
        internal static ModHost? ActiveModHost { get; set; }

        /// <summary>
        /// Sets the active ModHost for all mod route handlers.
        /// </summary>
        public static void SetModHost(ModHost? host)
        {
            ActiveModHost = host;
        }

        /// <summary>
        /// Dispatches a mod route request to the appropriate handler.
        /// Called from HttpApiSystem.HandleGameRequest.
        /// </summary>
        public static Task<string?> HandleRequest(string method, string url, string? body, HttpApiSystem? api = null)
        {
            // /api/v1/mod/list
            if (method == "GET" && url == "/api/v1/mod/list")
                return Task.FromResult<string?>(HandleList());

            // /api/v1/mod/install
            if (method == "POST" && url.StartsWith("/api/v1/mod/install"))
                return Task.FromResult(HandleInstall(body));

            // /api/v1/mod/enable/{id}
            if (method == "POST" && url.StartsWith("/api/v1/mod/enable/"))
                return Task.FromResult(HandleEnable(url));

            // /api/v1/mod/disable/{id}
            if (method == "POST" && url.StartsWith("/api/v1/mod/disable/"))
                return Task.FromResult(HandleDisable(url));

            // /api/v1/mod/reload/{id}
            if (method == "POST" && url.StartsWith("/api/v1/mod/reload/"))
                return Task.FromResult<string?>(HandleReload(url));

            // /api/v1/mod/unload/{id}
            if (method == "POST" && url.StartsWith("/api/v1/mod/unload/"))
                return Task.FromResult(HandleUnload(url));

            // /api/v1/mod/discover
            if (method == "GET" && url == "/api/v1/mod/discover")
                return Task.FromResult<string?>(HandleDiscover());

            // /api/v1/mod/load/{id}
            if (method == "POST" && url.StartsWith("/api/v1/mod/load/"))
                return Task.FromResult(HandleLoad(url));

            // /api/v1/mod/event-bus/status
            if (method == "GET" && url == "/api/v1/mod/event-bus/status")
                return Task.FromResult<string?>(HandleEventBusStatus());

            // /api/v1/mod/test/run
            if (method == "POST" && url == "/api/v1/mod/test/run")
                return Task.FromResult<string?>(HandleTestRun(body, api));

            return Task.FromResult<string?>(null); // Not a mod route
        }

        // ─────────────────────────────────────────────
        //  Handler implementations
        // ─────────────────────────────────────────────

        private static string HandleList()
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            var mods = host.LoadedMods.Values.Select(m => new
            {
                id = m.Manifest.Id,
                name = m.Manifest.Name,
                version = m.Manifest.Version,
                type = m.Manifest.Type,
                enabled = m.IsEnabled,
                state = m.State.ToString(),
                error = m.ErrorReason
            }).ToList();

            return JsonSerializer.Serialize(new { mods }, JsonOpts);
        }

        private static string HandleInstall(string? body)
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                if (string.IsNullOrWhiteSpace(body))
                    return JsonSerializer.Serialize(new { error = "Request body must contain 'path' to .modpkg file" });

                var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("path", out var pathElement))
                    return JsonSerializer.Serialize(new { error = "Request body must contain 'path' to .modpkg file" });

                var modPkgPath = pathElement.GetString();
                if (modPkgPath == null || !System.IO.File.Exists(modPkgPath))
                    return JsonSerializer.Serialize(new { error = $"File not found: {modPkgPath}" });

                var package = host.InstallMod(modPkgPath);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    id = package.Manifest.Id,
                    name = package.Manifest.Name,
                    version = package.Manifest.Version
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Install failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleEnable(string url)
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                var modId = ExtractModId(url);
                if (modId == null)
                    return JsonSerializer.Serialize(new { error = "Missing mod ID in URL" });

                host.EnableMod(modId);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    id = modId,
                    message = $"Mod '{modId}' enabled"
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Enable failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleDisable(string url)
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                var modId = ExtractModId(url);
                if (modId == null)
                    return JsonSerializer.Serialize(new { error = "Missing mod ID in URL" });

                host.DisableMod(modId);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    id = modId,
                    message = $"Mod '{modId}' disabled"
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Disable failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleReload(string url)
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                var modId = ExtractModId(url);
                if (modId == null)
                    return JsonSerializer.Serialize(new { error = "Missing mod ID in URL" });

                // Unload then reload
                host.UnloadMod(modId);
                var package = host.LoadMod(Path.Combine(host.ModsDirectory, modId));

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    id = modId,
                    message = $"Mod '{modId}' reloaded",
                    state = package.State.ToString()
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Reload failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleUnload(string url)
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                var modId = ExtractModId(url);
                if (modId == null)
                    return JsonSerializer.Serialize(new { error = "Missing mod ID in URL" });

                host.UnloadMod(modId);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    id = modId,
                    message = $"Mod '{modId}' unloaded"
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Unload failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleDiscover()
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                var discovered = host.DiscoverMods();
                var mods = discovered.Select(m => new
                {
                    id = m.Manifest.Id,
                    name = m.Manifest.Name,
                    version = m.Manifest.Version,
                    type = m.Manifest.Type,
                    directory = m.ModDirectory,
                    loadOrder = m.Manifest.LoadOrder,
                    dependencies = m.Manifest.Dependencies.Select(d => new { d.Id, d.MinVersion, d.Optional }).ToList()
                }).ToList();

                return JsonSerializer.Serialize(new { mods, count = mods.Count }, JsonOpts);
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Discover failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleLoad(string url)
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                var modId = ExtractModId(url);
                if (modId == null)
                    return JsonSerializer.Serialize(new { error = "Missing mod ID in URL" });

                var modDir = Path.Combine(host.ModsDirectory, modId);
                if (!Directory.Exists(modDir))
                    return JsonSerializer.Serialize(new { error = $"Mod directory not found: {modDir}" });

                var package = host.LoadMod(modDir);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    id = package.Manifest.Id,
                    name = package.Manifest.Name,
                    version = package.Manifest.Version,
                    state = package.State.ToString()
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Load failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleEventBusStatus()
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                var eventBus = host.EventBus;
                var subscriptionCount = (eventBus as ModEventBus)?.SubscriptionCount ?? 0;

                return JsonSerializer.Serialize(new
                {
                    subscriptionCount,
                    loadedMods = host.LoadedMods.Count
                }, JsonOpts);
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Event bus status failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleTestRun(string? body, HttpApiSystem? api)
        {
            var host = ActiveModHost;
            if (host == null)
                return JsonSerializer.Serialize(new { error = "ModHost not initialized" });

            try
            {
                // Parse test configuration from body
                string testType = "all";
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("test", out var testElement))
                        testType = testElement.GetString() ?? "all";
                }

                var results = new Dictionary<string, object>();

                switch (testType)
                {
                    case "all":
                        results["discover"] = host.DiscoverMods().Count;
                        results["loaded"] = host.LoadedMods.Count;
                        results["eventBus"] = (host.EventBus as ModEventBus)?.SubscriptionCount ?? 0;
                        break;

                    case "discover":
                        var discovered = host.DiscoverMods();
                        results["count"] = discovered.Count;
                        results["mods"] = discovered.Select(m => m.Manifest.Id).ToList();
                        break;

                    case "event-bus":
                        results["subscriptionCount"] = (host.EventBus as ModEventBus)?.SubscriptionCount ?? 0;
                        break;

                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown test type: {testType}" });
                }

                return JsonSerializer.Serialize(new { success = true, test = testType, results }, JsonOpts);
            }
            catch (Exception ex)
            {
                Log.Error($"[ModRoutes] Test run failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Extracts mod ID from URLs like /api/v1/mod/enable/{id} or /api/v1/mod/disable/{id}.
        /// </summary>
        private static string? ExtractModId(string url)
        {
            var parts = url.TrimEnd('/').Split('/');
            return parts.Length >= 2 ? parts[^1] : null;
        }
    }
}
