// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;

    internal class ClusterConfig
    {
        public ClusterConfig()
        {
            this.RefreshInterval = TimeSpan.FromSeconds(1);
            this.MinimumClusterCount = 10;
            this.MinimumClusterCount = 100;
            this.MaximumUsersPerCluster = 20;
            this.MaxClusterUptime = TimeSpan.FromHours(2);
        }

        public TimeSpan RefreshInterval { get; set; }

        public int MinimumClusterCount { get; set; }

        public int MaximumClusterCount { get; set; }

        public int MaximumUsersPerCluster { get; set; }

        public TimeSpan MaxClusterUptime { get; set; }
    }
}