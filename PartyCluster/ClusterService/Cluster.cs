// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.Generic;

    internal class Cluster
    {
        public Cluster()
        {
            this.Status = ClusterStatus.New;
            this.AppCount = 0;
            this.ServiceCount = 0;
            this.Address = String.Empty;
            this.Ports = new List<int>();
            this.Users = new List<ClusterUser>();
            this.CreatedOn = DateTimeOffset.MaxValue;
        }

        public ClusterStatus Status { get; set; }

        public int AppCount { get; set; }

        public int ServiceCount { get; set; }
        
        public string Address { get; set; }

        public IEnumerable<int> Ports { get; set; }

        public IList<ClusterUser> Users { get; set; }

        public DateTimeOffset CreatedOn { get; set; }
    }
}