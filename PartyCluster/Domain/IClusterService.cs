// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services;

    public interface IClusterService : IService
    {
        Task<IEnumerable<ClusterView>> GetClusterList();
        Task JoinClusterAsync(string username, string clusterName);
    }
}