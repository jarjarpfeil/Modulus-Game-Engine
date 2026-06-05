// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Stride.Core.Diagnostics;
using Stride.Games;

namespace Stride.Engine.HttpApi.Routes
{
    /// <summary>
    /// HTTP API routes for asset management (/api/v1/asset/*).
    /// </summary>
    public static class AssetRoutes
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("HttpApi.AssetRoutes");
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        // Known Stride asset extensions
        private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".sdscene", ".sdmat", ".sdtex", ".sdm3d", ".sdskel", ".sdanim",
            ".sdsheet", ".sdfnt", ".sdgamesettings", ".sdgfxcomp", ".sdpkg",
            ".sdsprite", ".sdsl", ".sdbundle"
        };

        /// <summary>
        /// Dispatches asset route requests.
        /// </summary>
        public static Task<string?> HandleRequest(string method, string url, string? body, string? queryString, HttpApiSystem api)
        {
            return url switch
            {
                "/api/v1/asset/list" when method == "GET" => Task.FromResult<string?>(HandleList(queryString, api)),
                "/api/v1/asset/import" when method == "POST" => Task.FromResult<string?>(HandleImport(body, api)),
                "/api/v1/asset/build" when method == "POST" => Task.FromResult<string?>(HandleBuild(body, api)),
                _ => Task.FromResult<string?>(JsonSerializer.Serialize(new { error = "Unknown asset route", path = url }))
            };
        }

        private static string HandleList(string? queryString, HttpApiSystem api)
        {
            var game = api.Game;
            if (game == null)
                return JsonSerializer.Serialize(new { error = "Game not initialized" });

            // Parse type filter from query string
            string? typeFilter = null;
            if (!string.IsNullOrEmpty(queryString))
            {
                var parts = queryString.Split('&');
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && kv[0] == "type")
                        typeFilter = Uri.UnescapeDataString(kv[1]);
                }
            }

            var contentDir = GetContentDirectory(game);
            if (contentDir == null || !Directory.Exists(contentDir))
                return JsonSerializer.Serialize(new { error = "Content directory not found", assets = Array.Empty<object>() });

            var assets = new List<object>();
            foreach (var file in Directory.EnumerateFiles(contentDir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!AssetExtensions.Contains(ext))
                    continue;

                var relativePath = Path.GetRelativePath(contentDir, file);

                if (!string.IsNullOrEmpty(typeFilter) &&
                    !ext.Equals($".{typeFilter}", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals($"sd{typeFilter}", StringComparison.OrdinalIgnoreCase))
                    continue;

                assets.Add(new
                {
                    path = relativePath.Replace('\\', '/'),
                    name = Path.GetFileNameWithoutExtension(file),
                    type = ext.TrimStart('.'),
                    size = new FileInfo(file).Length,
                    lastModified = File.GetLastWriteTimeUtc(file)
                });
            }

            return JsonSerializer.Serialize(new
            {
                assets,
                count = assets.Count,
                contentDir,
                filter = typeFilter
            }, JsonOpts);
        }

        private static string HandleImport(string? body, HttpApiSystem api)
        {
            var game = api.Game;
            if (game == null)
                return JsonSerializer.Serialize(new { error = "Game not initialized" });

            try
            {
                if (string.IsNullOrWhiteSpace(body))
                    return JsonSerializer.Serialize(new { error = "Request body must contain 'sourcePath' and optional 'targetPath'" });

                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("sourcePath", out var srcProp))
                    return JsonSerializer.Serialize(new { error = "Missing 'sourcePath' in request body" });

                var sourcePath = srcProp.GetString();
                if (sourcePath == null || !File.Exists(sourcePath))
                    return JsonSerializer.Serialize(new { error = $"Source file not found: {sourcePath}" });

                var contentDir = GetContentDirectory(game);
                if (contentDir == null)
                    return JsonSerializer.Serialize(new { error = "Content directory not found" });

                string targetPath;
                if (root.TryGetProperty("targetPath", out var tgtProp) && tgtProp.GetString() != null)
                    targetPath = Path.Combine(contentDir, tgtProp.GetString()!);
                else
                    targetPath = Path.Combine(contentDir, Path.GetFileName(sourcePath));

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(sourcePath, targetPath, overwrite: true);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    sourcePath,
                    targetPath = Path.GetRelativePath(contentDir, targetPath).Replace('\\', '/'),
                    message = "Asset imported successfully"
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[AssetRoutes] Import failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string HandleBuild(string? body, HttpApiSystem api)
        {
            var game = api.Game;
            if (game == null)
                return JsonSerializer.Serialize(new { error = "Game not initialized" });

            try
            {
                string? assetPath = null;
                string platform = "Windows";

                if (!string.IsNullOrWhiteSpace(body))
                {
                    var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("path", out var pathProp))
                        assetPath = pathProp.GetString();
                    if (root.TryGetProperty("platform", out var platProp))
                        platform = platProp.GetString() ?? "Windows";
                }

                var gameDir = Environment.CurrentDirectory;
                var args = $"--platform {platform}";
                if (!string.IsNullOrEmpty(assetPath))
                    args += $" --build \"{assetPath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "AssetCompiler.exe",
                    Arguments = args,
                    WorkingDirectory = gameDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(startInfo);
                if (process == null)
                    return JsonSerializer.Serialize(new { error = "Failed to start AssetCompiler.exe" });

                if (!process.WaitForExit(30_000))
                {
                    process.Kill();
                    return JsonSerializer.Serialize(new { error = "AssetCompiler timed out after 30 seconds", exitCode = -1 });
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();

                return JsonSerializer.Serialize(new
                {
                    success = process.ExitCode == 0,
                    exitCode = process.ExitCode,
                    platform,
                    output = stdout.Length > 2000 ? stdout[..2000] + "...(truncated)" : stdout,
                    errors = stderr.Length > 2000 ? stderr[..2000] + "...(truncated)" : stderr
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "AssetCompiler.exe not found on PATH",
                    hint = "Run 'dotnet build' first, or install Stride Game Studio"
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[AssetRoutes] Build failed: {ex.Message}");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private static string? GetContentDirectory(GameBase game)
        {
            var gameDir = Environment.CurrentDirectory;
            var candidates = new[] { "content", "Content", "assets", "Assets" };
            foreach (var c in candidates)
            {
                var path = Path.Combine(gameDir, c);
                if (Directory.Exists(path))
                    return path;
            }
            return null;
        }
    }
}
