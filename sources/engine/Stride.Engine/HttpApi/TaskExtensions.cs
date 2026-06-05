// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Stride.Engine.HttpApi
{
    /// <summary>
    /// Extension methods for <see cref="Task"/> with timeout support.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Awaits a task with a timeout. If the task doesn't complete within the timeout,
        /// returns the fallback value instead.
        /// </summary>
        public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout, Func<T> fallback)
        {
            var delay = Task.Delay(timeout);
            if (await Task.WhenAny(task, delay) == task)
                return await task;
            return fallback();
        }

        /// <summary>
        /// Awaits a task with a timeout. If the task doesn't complete within the timeout,
        /// returns a default error response.
        /// </summary>
        public static Task<object> WithTimeout(this Task<object> task, TimeSpan timeout)
        {
            return task.WithTimeout(timeout, () => (object)new { error = "Engine busy, try again" });
        }
    }
}
