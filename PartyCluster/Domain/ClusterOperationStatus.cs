// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    public enum ClusterOperationStatus
    {
        /// <summary>
        /// Cluster is being created.
        /// </summary>
        Creating,

        /// <summary>
        /// Cluster is ready.
        /// </summary>
        Ready,

        /// <summary>
        /// Cluster is being deleted.
        /// </summary>
        Deleting,

        /// <summary>
        /// A create operation failed.
        /// </summary>
        CreateFailed,

        /// <summary>
        /// A delete operation failed.
        /// </summary>
        DeleteFailed,

        /// <summary>
        /// The cluster doesn't exist.
        /// </summary>
        ClusterNotFound
    }
}