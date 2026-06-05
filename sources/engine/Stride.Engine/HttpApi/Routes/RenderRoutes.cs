// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Net;
using System.Threading.Tasks;

namespace Stride.Engine.HttpApi.Routes
{
    public static class RenderRoutes
    {
        private const int NotImpl = 501;

        public static Task<object> CompileShader(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new { error = "Not implemented", status = NotImpl, message = "Shader compilation pending Phase 4" });
        }

        public static Task<object> ListShaders(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new { error = "Not implemented", status = NotImpl, message = "Shader listing pending Phase 4" });
        }
    }
}
