// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;

    internal class ClusterManagementPipeline
    {
        private readonly string clusterDictionaryName;
        private readonly IReliableStateManager stateManager;
        private readonly ClusterConfig config;

        public ClusterManagementPipeline(IReliableStateManager stateManager, string clusterDictionaryName, ClusterConfig config)
        {
            this.clusterDictionaryName = clusterDictionaryName;
            this.stateManager = stateManager;
            this.config = config;
        }

        public async Task VerifyClusterCountAsync()
        {
            throw new NotImplementedException();

            IReliableDictionary<string, Cluster> clusterDictionary =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Cluster>>(this.clusterDictionaryName);

            // 1. Check number of clusters in the dictionary
            long clusterCount = await clusterDictionary.GetCountAsync();

            if (clusterCount < this.config.MinimumClusterCount)
            {
                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    for (int i = 0; i < this.config.MinimumClusterCount - clusterCount; ++i)
                    {
                        await clusterDictionary.AddAsync(tx, "", new Cluster());
                    }

                    await tx.CommitAsync();
                }
            }
        }

        public Task CheckUserCountAsync()
        {
            // 1.a. check number users to see if we need to create more clusters or delete unused clusters.

            throw new NotImplementedException();
        }

        public Task UpdateClusterStatusAsync()
        {
            // 2. Check status of each item in the dictionary
            // state machine:
            //
            // new: Create cluster
            // creating: Check status
            // ready: Get ports, check uptime.
            // deleting: Delete cluster
            // removed: Check status

            // 3. Update metadata fields of each item in the dictionary
            throw new NotImplementedException();
        }
    }
}