// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System;

    public struct ClusterView
    {
        public string Name { get; set; }

        public int AppCount { get; set; }

        public int ServiceCount { get; set; }

        public int UserCount { get; set; }

        public TimeSpan Uptime { get; set; }
    }
}