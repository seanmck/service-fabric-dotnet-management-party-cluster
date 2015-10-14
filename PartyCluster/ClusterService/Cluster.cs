// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.Generic;

    internal struct Cluster
    {
        public ClusterStatus Status { get; set; }

        public int AppCount { get; set; }

        public int ServiceCount { get; set; }

        public string Name { get; set; }

        public string Address { get; set; }

        public IEnumerable<int> Ports { get; set; }

        public IList<ClusterUser> Users { get; set; }

        public TimeSpan Uptime { get; set; }
    }
}