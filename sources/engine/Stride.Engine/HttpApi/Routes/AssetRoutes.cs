// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Net;
using System.Threading.Tasks;

namespace Stride.Engine.HttpApi.Routes
{
    public static class AssetRoutes
    {
        public static Task<object> ListAssets(HttpListenerRequest req, HttpApiSystem api)
        {
            return api.EnqueueOnGameThread(() =>
            {
                // Stub — full implementation requires asset database traversal
                return (object)new
                {
                    message = "Asset listing not yet implemented",
                    note = "This endpoint will list assets by type when the content database is accessible"
                };
            });
        }

        public static Task<object> ImportAsset(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new
            {
                error = "Not implemented",
                status = 501,
                message = "Asset import will be available in a future release"
            });
        }

        public static Task<object> BuildAsset(HttpListenerRequest req, HttpApiSystem api)
        {
            return Task.FromResult<object>(new
            {
                error = "Not implemented",
                status = 501,
                message = "Asset build will be available in a future release"
            });
        }
    }
}
