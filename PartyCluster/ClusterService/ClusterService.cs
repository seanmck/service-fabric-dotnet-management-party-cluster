// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Domain;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services;

    public class ClusterService : StatefulService, IClusterService
    {
        internal const string ClusterDictionaryName = "clusterDictionary";
        private IClusterOperator clusterOperator;
        private IReliableStateManager reliableStateManager;

        public ClusterService()
        {
            this.Config = new ClusterConfig();
        }

        /// <summary>
        /// Poor-man's dependency injection for now until the API supports proper injection of IReliableStateManager.
        /// This constructor is used in unit tests to inject a different state manager.
        /// </summary>
        /// <param name="stateManager"></param>
        /// <param name="clusterOperator"></param>
        public ClusterService(IClusterOperator clusterOperator, IReliableStateManager stateManager)
            : this()
        {
            this.clusterOperator = clusterOperator;
            this.reliableStateManager = stateManager;
        }

        internal ClusterConfig Config { get; set; }

        public async Task<IEnumerable<ClusterView>> GetClusterList()
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.reliableStateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            return from cluster in clusterDictionary.CreateEnumerable(EnumerationMode.Ordered)
                   where cluster.Value.Status == ClusterStatus.Ready
                   select new ClusterView()
                   {
                       AppCount = cluster.Value.AppCount,
                       Name = "Party Cluster " + cluster.Key,
                       ServiceCount = cluster.Value.ServiceCount,
                       Uptime = DateTimeOffset.UtcNow - cluster.Value.CreatedOn.ToUniversalTime(),
                       UserCount = cluster.Value.Users.Count
                   };
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
                if ((DateTimeOffset.UtcNow - cluster.CreatedOn.ToUniversalTime()) > (this.Config.MaxClusterUptime - TimeSpan.FromMinutes(5)))
                {
                    throw new InvalidOperationException(); // need a better exception here
                }

                userPort = cluster.Ports.First(port => !cluster.Users.Select(x => x.Port).Contains(port));
                clusterAddress = cluster.Address;

                cluster.Users.Add(new ClusterUser() { Name = username, Port = userPort });

                await clusterDictionary.SetAsync(tx, clusterName, cluster);

                await tx.CommitAsync();
            }

            // send email to user with cluster info
        }

        /// <summary>
        /// Adds clusters by the given amount without going over the max threshold.
        /// </summary>
        /// <param name="targetCount"></param>
        /// <returns></returns>
        internal async Task IncreaseClustersBy(int amount)
        {
            Random random = new Random();

            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.reliableStateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction tx = this.reliableStateManager.CreateTransaction())
            {
                var activeClusters = clusterDictionary
                    .Where(x =>
                        x.Value.Status != ClusterStatus.Deleting &&
                        x.Value.Status != ClusterStatus.Remove);

                int limit = Math.Min(amount, this.Config.MaximumClusterCount - activeClusters.Count());
                for (int i = 0; i < limit; ++i)
                {
                    await clusterDictionary.AddAsync(tx, random.Next(), new Cluster());
                }

                await tx.CommitAsync();
            }
        }

        /// <summary>
        /// Decreases the number of active, empty clusters by the given amount without going below the min cluster threshold.
        /// </summary>
        /// <param name="amount"></param>
        /// <returns></returns>
        internal async Task DecreaseClustersBy(int amount)
        {
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.reliableStateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            using (ITransaction tx = this.reliableStateManager.CreateTransaction())
            {
                var activeClusters = clusterDictionary
                    .Where(x =>
                        x.Value.Status != ClusterStatus.Deleting &&
                        x.Value.Status != ClusterStatus.Remove);

                var removeList = activeClusters
                    .Where(x => x.Value.Users.Count == 0)
                    .Take(Math.Min(activeClusters.Count() - this.Config.MinimumClusterCount, amount));

                foreach (var item in removeList)
                {
                    Cluster value = item.Value;
                    value.Status = ClusterStatus.Remove;

                    await clusterDictionary.SetAsync(tx, item.Key, value);
                }

                await tx.CommitAsync();
            }
        }

        /// <summary>
        /// Determines how many clusters there should be based on user activity.
        /// </summary>
        /// <remarks>
        /// When the user count goes below the low percent threshold, decrease capacity by (high - low)%
        /// When the user count goes above the high percent threshold, increase capacity by (1 - high)%
        /// </remarks>
        /// <returns></returns>
        internal async Task<int> GetTargetClusterCapacityAsync()
        {
            // 1.a. check number users to see if we need to create more clusters or delete unused clusters.
            IReliableDictionary<int, Cluster> clusterDictionary =
                await this.reliableStateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterDictionaryName);

            int activeClusters = clusterDictionary
                    .Count(x => x.Value.Status != ClusterStatus.Deleting && x.Value.Status != ClusterStatus.Remove);

            double totalCapacity = activeClusters * this.Config.MaximumUsersPerCluster;

            double totalUsers = clusterDictionary
                    .Aggregate(0, (total, next) => total += next.Value.Users.Count);

            double percentFull = totalUsers / totalCapacity;

            if (percentFull >= this.Config.UserCapacityHighPercentThreshold)
            {
                return Math.Min(
                    this.Config.MaximumClusterCount,
                    activeClusters + (int)Math.Ceiling(activeClusters * (1 - this.Config.UserCapacityHighPercentThreshold)));
            }

            if (percentFull <= this.Config.UserCapacityLowPercentThreshold)
            {
                return Math.Max(
                    this.Config.MinimumClusterCount,
                    activeClusters - (int)Math.Floor(activeClusters * (this.Config.UserCapacityHighPercentThreshold - this.Config.UserCapacityLowPercentThreshold)));
            }

            return activeClusters;
        }

        /// <summary>
        /// Processes a cluster based on its current state.
        /// </summary>
        /// <returns></returns>
        internal async Task ProcessClusterStatusAsync(Cluster cluster)
        {
            switch (cluster.Status)
            {
                case ClusterStatus.New:
                    Random random = new Random();
                    cluster.Address = await this.clusterOperator.CreateClusterAsync(random.Next().ToString());
                    cluster.Status = ClusterStatus.Creating;
                    break;

                case ClusterStatus.Creating:
                    ClusterOperationStatus creatingStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.Address);
                    switch (creatingStatus)
                    {
                        case ClusterOperationStatus.Creating:
                            // still creating
                            break;
                        case ClusterOperationStatus.Ready:
                            cluster.Ports = await this.clusterOperator.GetClusterPortsAsync(cluster.Address);
                            cluster.CreatedOn = DateTimeOffset.UtcNow;
                            cluster.Status = ClusterStatus.Ready;
                            break;
                        case ClusterOperationStatus.CreateFailed:
                            cluster.Status = ClusterStatus.New;
                            break;
                        case ClusterOperationStatus.Deleting:
                            cluster.Status = ClusterStatus.Deleting;
                            break;
                    }
                    break;

                case ClusterStatus.Ready:
                    if (DateTimeOffset.UtcNow - cluster.CreatedOn.ToUniversalTime() >= this.Config.MaxClusterUptime)
                    {
                        await this.clusterOperator.DeleteClusterAsync(cluster.Address);
                        cluster.Status = ClusterStatus.Deleting;
                    }

                    ClusterOperationStatus readyStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.Address);
                    switch (readyStatus)
                    {
                        case ClusterOperationStatus.Deleting:
                            cluster.Status = ClusterStatus.Deleting;
                            break;
                    }

                    //TODO: update application and service count
                    break;

                case ClusterStatus.Remove:
                    ClusterOperationStatus removeStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.Address);
                    switch (removeStatus)
                    {
                        case ClusterOperationStatus.Creating:
                        case ClusterOperationStatus.Ready:
                            await this.clusterOperator.DeleteClusterAsync(cluster.Address);
                            cluster.Status = ClusterStatus.Deleting;
                            break;
                        case ClusterOperationStatus.Deleting:
                            cluster.Status = ClusterStatus.Deleting;
                            break;
                        case ClusterOperationStatus.CreateFailed:
                        case ClusterOperationStatus.DeleteFailed:
                            // this means the cluster was marked for removal but it is a failed state. Do something!
                            break;

                    }
                    break;

                case ClusterStatus.Deleting:
                    ClusterOperationStatus deleteStatus = await this.clusterOperator.GetClusterStatusAsync(cluster.Address);
                    switch (deleteStatus)
                    {
                        case ClusterOperationStatus.Creating:
                        case ClusterOperationStatus.Ready:
                            await this.clusterOperator.DeleteClusterAsync(cluster.Address);
                            break;
                        case ClusterOperationStatus.Deleting:
                            break; // still in progress
                        case ClusterOperationStatus.ClusterNotFound:
                            cluster.Status = ClusterStatus.Deleted;
                            break;
                        case ClusterOperationStatus.DeleteFailed:
                            //TODO: set status back to Remove so it can be retried?
                            break;
                    }
                    break;

            }
        }

        private string ClusterName(int number)
        {
            return String.Format("Party Cluster {0}", number);
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
            return new[] { new ServiceReplicaListener(parameters => new ServiceCommunicationListener<IClusterService>(parameters, this)) };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int target = await this.GetTargetClusterCapacityAsync();
                //await clusterManagementPipeline.UpdateClusterListAsync(target);
                await this.ProcessClusterStatusAsync(null);

                await Task.Delay(this.Config.RefreshInterval, cancellationToken);
            }
        }
    }
}