// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Domain;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services;

    public class ClusterService : StatefulService, IClusterService
    {
        private const string ClusterDictionaryName = "clusterDictionary";
        private readonly IClusterOperator clusterOperator;
        private ClusterConfig config = new ClusterConfig();
        private IReliableStateManager reliableStateManager;

        public ClusterService()
        {
        }

        /// <summary>
        /// Poor-man's dependency injection for now until the API supports proper injection of IReliableStateManager.
        /// This constructor is used in unit tests to inject a different state manager.
        /// </summary>
        /// <param name="stateManager"></param>
        /// <param name="clusterOperator"></param>
        public ClusterService(IClusterOperator clusterOperator, IReliableStateManager stateManager)
        {
            this.clusterOperator = clusterOperator;
        }

        public async Task<IEnumerable<ClusterView>> GetClusterList()
        {
            IReliableDictionary<string, Cluster> clusterDictionary =
                await this.reliableStateManager.GetOrAddAsync<IReliableDictionary<string, Cluster>>(ClusterDictionaryName);

            return clusterDictionary.CreateEnumerable(EnumerationMode.Ordered).Select(
                item =>
                    new ClusterView()
                    {
                        AppCount = item.Value.AppCount,
                        Name = item.Key,
                        ServiceCount = item.Value.ServiceCount,
                        Uptime = item.Value.Uptime,
                        UserCount = item.Value.Users.Count
                    });
        }

        public async Task JoinClusterAsync(string username, string clusterName)
        {
            if (String.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentNullException("username");
            }

            if (String.IsNullOrWhiteSpace(clusterName))
            {
                throw new ArgumentNullException("clusterName");
            }

            IReliableDictionary<string, Cluster> clusterDictionary =
                await this.reliableStateManager.GetOrAddAsync<IReliableDictionary<string, Cluster>>(ClusterDictionaryName);

            int userPort;
            string clusterAddress;

            using (ITransaction tx = this.reliableStateManager.CreateTransaction())
            {
                ConditionalResult<Cluster> result = await clusterDictionary.TryGetValueAsync(tx, clusterName, LockMode.Update);

                if (!result.HasValue)
                {
                    throw new KeyNotFoundException();
                }

                Cluster cluster = result.Value;

                // make sure the cluster is ready
                if (cluster.Status != ClusterStatus.Ready)
                {
                    throw new InvalidOperationException(); // need a better exception here
                }

                // make sure the cluster isn't about to be deleted.
                if (cluster.Uptime > this.config.MaxClusterUptime - TimeSpan.FromMinutes(5))
                {
                    throw new InvalidOperationException(); // need a better exception here
                }

                userPort = cluster.Ports.First(port => !cluster.Users.Select(x => x.Port).Contains(port));
                clusterAddress = cluster.Address;

                cluster.Users.Add(new ClusterUser() {Name = username, Port = userPort});

                await clusterDictionary.SetAsync(tx, clusterName, cluster);

                await tx.CommitAsync();
            }

            // send email to user with cluster info
        }

        /// <summary>
        /// Poor-man's dependency injection for now until the API supports proper injection of IReliableStateManager.
        /// </summary>
        /// <returns></returns>
        protected override IReliableStateManager CreateReliableStateManager()
        {
            if (this.reliableStateManager == null)
            {
                this.reliableStateManager = base.CreateReliableStateManager();
            }
            return this.reliableStateManager;
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            yield return new ServiceReplicaListener(parameters => new ServiceCommunicationListener<IClusterService>(parameters, this));
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            ClusterManagementPipeline clusterManagementPipeline = new ClusterManagementPipeline(this.reliableStateManager, ClusterDictionaryName, this.config);

            while (!cancellationToken.IsCancellationRequested)
            {
                await clusterManagementPipeline.VerifyClusterCountAsync();
                await clusterManagementPipeline.CheckUserCountAsync();
                await clusterManagementPipeline.UpdateClusterStatusAsync();

                await Task.Delay(this.config.RefreshInterval, cancellationToken);
            }
        }
    }
}