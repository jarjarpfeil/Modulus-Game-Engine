// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Stride.Core.Assets.Editor.ViewModel;
using Stride.Core.Diagnostics;
using Stride.Engine.Modding;

namespace Stride.GameStudio.HttpApi
{
    /// <summary>
    /// Handles HTTP API requests for the Game Studio editor.
    /// Provides access to session, packages, assets, and editor state.
    /// </summary>
    public static class EditorApiHandler
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi");
        private static readonly ConcurrentQueue<LogEntry> _recentLogs = new();
        private const int MaxLogEntries = 500;
        private static bool _logHooked = false;
        private static readonly object _logLock = new();
        private static string? _currentProjectPath;

        private class LogEntry
        {
            public string Level { get; set; } = "";
            public string Module { get; set; } = "";
            public string Message { get; set; } = "";
            public string Timestamp { get; set; } = "";
        }

        /// <summary>
        /// Set the current project path (call when project opens).
        /// </summary>
        public static void SetCurrentProject(string? path)
        {
            _currentProjectPath = path;
        }

        private static void EnsureLogHook()
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

        /// <summary>
        /// Handle an API request. Returns JSON response.
        /// </summary>
        public static async Task<string?> HandleRequest(string method, string url, string? body)
        {
            try
            {
                // Status
                if (method == "GET" && url == "/api/v1/status")
                {
                    return GetStatus();
                }

                // Editor status
                if (method == "GET" && url == "/api/v1/editor/status")
                {
                    return GetEditorStatus();
                }

                // Session info
                if (method == "GET" && url == "/api/v1/session/info")
                {
                    return GetSessionInfo();
                }

                // List packages
                if (method == "GET" && url == "/api/v1/session/packages")
                {
                    return ListPackages();
                }

                // List assets
                if (method == "GET" && url.StartsWith("/api/v1/assets"))
                {
                    return ListAssets();
                }

                // Open project (stub — needs UI thread)
                if (method == "POST" && url == "/api/v1/project/open")
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = "Not implemented",
                        message = "Opening projects requires UI thread access. Use command line argument instead."
                    });
                }

                // Scene stubs
                if (url.StartsWith("/api/v1/scene/"))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = "Not implemented",
                        message = "Scene manipulation requires active scene editor. Use Game runtime API instead."
                    });
                }

                // Mod routes — mod discovery/validation works in editor;
                // enable/disable/unload require game runtime
                if (url.StartsWith("/api/v1/mod/"))
                {
                    return HandleModRoute(method, url, body);
                }

                // Render stubs
                if (url.StartsWith("/api/v1/render/"))
                {
                    return JsonSerializer.Serialize(new { error = "Not implemented", status = 501, message = "Render API pending Phase 4" });
                }

                // Debug logs
                if (method == "GET" && url.StartsWith("/api/v1/debug/logs"))
                {
                    return GetLogs(url);
                }

                // Debug console
                if (method == "GET" && url.StartsWith("/api/v1/debug/console"))
                {
                    return GetConsole(url);
                }

                // 404
                return JsonSerializer.Serialize(new { error = "Not found", path = url, method });
            }
            catch (Exception ex)
            {
                Log.Error($"[HttpApi] Error handling {method} {url}: {ex}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleModRoute(string method, string url, string? body)
        {
            // Mod discovery — works in editor (file scan, no game runtime needed)
            if (method == "GET" && url == "/api/v1/mod/discover")
            {
                var modsDir = Path.Combine(Directory.GetCurrentDirectory(), "mods");
                try
                {
                    var discovered = ModDiscovery.Discover(modsDir);
                    var mods = discovered.Select(m => new
                    {
                        id = m.Manifest.Id,
                        name = m.Manifest.Name,
                        version = m.Manifest.Version,
                        type = m.Manifest.Type
                    }).ToList();
                    return JsonSerializer.Serialize(new { mods, count = mods.Count, modsDirectory = modsDir },
                        new JsonSerializerOptions { WriteIndented = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            }

            // Mod install — works in editor (file extraction)
            if (method == "POST" && url == "/api/v1/mod/install")
            {
                if (string.IsNullOrWhiteSpace(body))
                    return JsonSerializer.Serialize(new { error = "Request body required with 'path' field" });

                try
                {
                    var doc = JsonSerializer.Deserialize<JsonElement>(body);
                    var path = doc.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        return JsonSerializer.Serialize(new { error = "Valid 'path' to .modpkg file required" });

                    var modsDir = Path.Combine(Directory.GetCurrentDirectory(), "mods");
                    Directory.CreateDirectory(modsDir);
                    var pkg = ModPackage.FromModPkg(path, Path.Combine(modsDir, ".temp_extract"));

                    // Validate
                    var errors = ModValidator.Validate(pkg.Manifest);
                    if (errors.Count > 0)
                        return JsonSerializer.Serialize(new { error = "Validation failed", validationErrors = errors });

                    // Extract to mods/{id}/
                    var targetDir = Path.Combine(modsDir, pkg.Manifest.Id);
                    if (Directory.Exists(targetDir))
                        Directory.Delete(targetDir, recursive: true);
                    System.IO.Compression.ZipFile.ExtractToDirectory(path, targetDir, overwriteFiles: true);

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = $"Mod '{pkg.Manifest.Id}' v{pkg.Manifest.Version} installed. Full activation requires game runtime.",
                        mod = new { id = pkg.Manifest.Id, name = pkg.Manifest.Name, version = pkg.Manifest.Version, type = pkg.Manifest.Type }
                    }, new JsonSerializerOptions { WriteIndented = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            }

            // Mod validate — validate a mod.json without installing
            if (method == "POST" && url == "/api/v1/mod/validate")
            {
                if (string.IsNullOrWhiteSpace(body))
                    return JsonSerializer.Serialize(new { error = "Request body required with mod.json content" });
                try
                {
                    var manifest = ModManifest.FromJson(body);
                    var errors = ModValidator.Validate(manifest);
                    return JsonSerializer.Serialize(new
                    {
                        valid = errors.Count == 0,
                        errors,
                        mod = new { id = manifest.Id, name = manifest.Name, version = manifest.Version }
                    }, new JsonSerializerOptions { WriteIndented = true });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            }

            // Operations requiring game runtime
            return JsonSerializer.Serialize(new
            {
                error = "Game runtime required",
                message = "Mod listing, enable/disable, and unload require a running game. Launch a game project first."
            });
        }

        private static string GetStatus()
        {
            var session = SessionViewModel.Instance;
            return JsonSerializer.Serialize(new
            {
                status = "running",
                engine = "Modulus Engine",
                editor = session != null ? "project-loaded" : "ready",
                currentProject = _currentProjectPath ?? session?.SolutionPath?.FullPath,
                time = DateTime.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string GetEditorStatus()
        {
            var session = SessionViewModel.Instance;
            var packages = session?.AllPackages?.Select(p => new
            {
                name = p.Name,
                isLoaded = p.IsLoaded,
                isEditable = p.IsEditable
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                status = session != null ? "project-loaded" : "ready",
                currentProject = _currentProjectPath ?? session?.SolutionPath?.FullPath,
                packageCount = packages?.Count ?? 0,
                packages = packages
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string GetSessionInfo()
        {
            var session = SessionViewModel.Instance;
            if (session == null)
                return JsonSerializer.Serialize(new { error = "No session loaded" });

            return JsonSerializer.Serialize(new
            {
                solutionPath = session.SolutionPath?.FullPath,
                sessionFilePath = session.SessionFilePath?.FullPath,
                isEditorInitialized = session.IsEditorInitialized,
                packageCount = session.AllPackages?.Count() ?? 0,
                localPackages = session.LocalPackages?.Select(p => p.Name).ToList(),
                storePackages = session.StorePackages?.Select(p => p.Name).ToList()
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string ListPackages()
        {
            var session = SessionViewModel.Instance;
            if (session == null)
                return JsonSerializer.Serialize(new { error = "No session loaded" });

            var packages = session.AllPackages?.Select(p => new
            {
                name = p.Name,
                isLoaded = p.IsLoaded,
                isEditable = p.IsEditable,
                category = session.PackageCategories
                    .FirstOrDefault(c => c.Value.Content.Contains(p)).Key
            }).ToList();

            return JsonSerializer.Serialize(new { packages }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string ListAssets()
        {
            var session = SessionViewModel.Instance;
            if (session == null)
                return JsonSerializer.Serialize(new { error = "No session loaded" });

            return JsonSerializer.Serialize(new
            {
                message = "Asset listing requires deeper editor integration",
                packageCount = session.AllPackages?.Count() ?? 0
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string GetLogs(string url)
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

        private static string GetConsole(string url)
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
    }
}
