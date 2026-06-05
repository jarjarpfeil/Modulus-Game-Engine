// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Stride.Core.Diagnostics;

namespace Stride.Engine.HttpApi.Routes
{
    public static class DebugRoutes
    {
        private static readonly ConcurrentQueue<LogEntry> _recentLogs = new();
        private const int MaxLogEntries = 500;
        private static bool _hooked = false;
        private static readonly object _hookLock = new();

        private class LogEntry
        {
            public string Level { get; set; } = "";
            public string Module { get; set; } = "";
            public string Message { get; set; } = "";
            public string Timestamp { get; set; } = "";
        }

        private static void EnsureLogHook()
        {
            if (_hooked) return;
            lock (_hookLock)
            {
                if (_hooked) return;
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
                _hooked = true;
            }
        }

        public static Task<object> GetLogs(HttpListenerRequest req, HttpApiSystem api)
        {
            EnsureLogHook();

            var countParam = req.QueryString["count"];
            int count = 50;
            if (countParam != null && int.TryParse(countParam, out var c))
                count = Math.Clamp(c, 1, MaxLogEntries);

            var logs = _recentLogs.TakeLast(count).Select(l => new
            {
                level = l.Level,
                module = l.Module,
                message = l.Message,
                timestamp = l.Timestamp
            }).ToList();

            return Task.FromResult<object>(new { logs, total = _recentLogs.Count });
        }

        public static Task<object> GetConsole(HttpListenerRequest req, HttpApiSystem api)
        {
            EnsureLogHook();

            var countParam = req.QueryString["count"];
            int count = 100;
            if (countParam != null && int.TryParse(countParam, out var c))
                count = Math.Clamp(c, 1, MaxLogEntries);

            var console = _recentLogs
                .Where(l => l.Level != "Verbose")
                .TakeLast(count)
                .Select(l => $"[{l.Level}] [{l.Module}] {l.Message}")
                .ToList();

            return Task.FromResult<object>(new { console, total = console.Count });
        }
    }
}
