// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Net;
using System.Threading.Tasks;

namespace Stride.Engine.HttpApi.Routes
{
    public static class ModRoutes
    {
        private const int NotImpl = 501;

        public static Task<object> Install(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new { error = "Not implemented", status = NotImpl, message = "Mod install pending Phase 3" });
        }

        public static Task<object> Enable(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new { error = "Not implemented", status = NotImpl, message = "Mod enable pending Phase 3" });
        }

        public static Task<object> Disable(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new { error = "Not implemented", status = NotImpl, message = "Mod disable pending Phase 3" });
        }

        public static Task<object> Reload(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new { error = "Not implemented", status = NotImpl, message = "Mod reload pending Phase 4" });
        }

        public static Task<object> List(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new { error = "Not implemented", status = NotImpl, message = "Mod list pending Phase 4" });
        }
    }
}
