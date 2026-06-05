// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
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
                return Task.FromResult<string?>(JsonSerializer.Serialize(new
                {
                    error = "Not implemented",
                    status = 501,
                    message = "Mod hot-reload pending Phase 4"
                }));

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
