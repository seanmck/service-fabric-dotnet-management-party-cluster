// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService.UnitTests
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;
    using System.Linq;
    using Microsoft.ServiceFabric.Data;
    using System.Collections.Generic;
    using Domain;

    [TestClass]
    public class ClusterServiceTests
    {
        private Random random = new Random(7);
        private object locker = new object();

        /// <summary>
        /// The cluster list should filter out any clusters that are not ready.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestGetClusterList()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            int readyClusters = 10;
            int deletingCluster = 4;
            int newClusters = 2;
            int removeClusters = 1;

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, readyClusters, ClusterStatus.Ready);
                await AddClusters(tx, dictionary, deletingCluster, ClusterStatus.Deleting);
                await AddClusters(tx, dictionary, newClusters, ClusterStatus.New);
                await AddClusters(tx, dictionary, removeClusters, ClusterStatus.Remove);
                await tx.CommitAsync();
            }

            IEnumerable<ClusterView> actual = await target.GetClusterList();

            Assert.AreEqual(readyClusters, actual.Count());
        }

        /// <summary>
        /// First time around there are no clusters. This tests that the minimum number of clusters is created initially.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestIncreaseClusters()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            await target.IncreaseClustersBy(config.MinimumClusterCount);

            var result = await stateManager.TryGetAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(config.MinimumClusterCount, await result.Value.GetCountAsync());
            Assert.IsTrue(result.Value.All(x => x.Value.Status == ClusterStatus.New));
        }

        /// <summary>.
        /// Only add clusters up to the limit.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestIncreaseClustersMax()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            int readyClusters = 10;
            int deletingClusterCount = 20;
            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, readyClusters, ClusterStatus.Ready);
                await AddClusters(tx, dictionary, deletingClusterCount, ClusterStatus.Deleting);
                await tx.CommitAsync();
            }

            await target.IncreaseClustersBy(config.MaximumClusterCount + 1);

            Assert.AreEqual(config.MaximumClusterCount + deletingClusterCount, await dictionary.GetCountAsync());
            Assert.AreEqual(config.MaximumClusterCount - readyClusters, dictionary.Count(x => x.Value.Status == ClusterStatus.New));
            Assert.AreEqual(readyClusters, dictionary.Count(x => x.Value.Status == ClusterStatus.Ready));
        }

        /// <summary>
        /// Only decrease down to the minimum.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestDecreaseClustersMin()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, config.MinimumClusterCount, ClusterStatus.Ready);
                await tx.CommitAsync();
            }

            await target.DecreaseClustersBy(1);

            Assert.AreEqual(config.MinimumClusterCount, await dictionary.GetCountAsync());
            Assert.IsTrue(dictionary.All(x => x.Value.Status == ClusterStatus.Ready));
        }

        /// <summary>
        /// UpdateClusterListAsync should not flag to remove clusters that are already being deleted.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestDecreaseClusterAlreadyDeleting()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int readyCount = 5;
            int deletingCount = 10;
            int decreaseBy = 10;

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, readyCount + config.MinimumClusterCount, ClusterStatus.Ready);
                await AddClusters(tx, dictionary, deletingCount, ClusterStatus.Deleting);

                await tx.CommitAsync();
            }

            await target.DecreaseClustersBy(decreaseBy);

            Assert.AreEqual(readyCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Remove));
            Assert.AreEqual(config.MinimumClusterCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Ready));
            Assert.AreEqual(deletingCount, dictionary.Count(x => x.Value.Status == ClusterStatus.Deleting));
        }
        
        /// <summary>
        /// UpdateClusterListAsync should not flag to remove clusters that still have users in them 
        /// when given a target count below the current count.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestDecreaseClustersNonEmpty()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int withUsers = config.MinimumClusterCount + 5;
            int withoutUsers = 10;
            int reduceBy = 11;

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, withUsers, () => new Cluster()
                {
                    Users = new List<ClusterUser>() { new ClusterUser() },
                    Status = ClusterStatus.Ready
                });

                await AddClusters(tx, dictionary, withoutUsers, ClusterStatus.Ready);

                await tx.CommitAsync();
            }

            await target.DecreaseClustersBy(reduceBy);

            Assert.AreEqual(withUsers, dictionary.Select(x => x.Value).Count(x => x.Status == ClusterStatus.Ready));
            Assert.AreEqual(withoutUsers, dictionary.Select(x => x.Value).Count(x => x.Status == ClusterStatus.Remove));
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityHighPercentThreshold value, 
        /// increase the number of clusters by (100 - UserCapacityHighPercentThreshold)%
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestTargetClusterCapacityIncrease()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MinimumClusterCount;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, clusterCount, () => new Cluster()
                {
                    Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Ceiling((double)config.MaximumUsersPerCluster * config.UserCapacityHighPercentThreshold)))
                });

                await AddClusters(tx, dictionary, 5, ClusterStatus.Deleting);
                
                await tx.CommitAsync();
            }

            int expected = clusterCount + (int)Math.Ceiling(clusterCount * (1 - config.UserCapacityHighPercentThreshold));
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityHighPercentThreshold value, 
        /// increase the number of clusters without going over MaximumClusterCount. 
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestTargetClusterCapacityIncreaseAtMaxCount()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MaximumClusterCount - 1;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, clusterCount, () =>  new Cluster()
                {
                    Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Ceiling((double)config.MaximumUsersPerCluster * config.UserCapacityHighPercentThreshold)))
                });
                
                await tx.CommitAsync();
            }
            
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(config.MaximumClusterCount, actual);
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityLowPercentThreshold value, 
        /// decrease the number of clusters by high-low% capacity
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestTargetClusterCapacityDecrease()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MaximumClusterCount;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, clusterCount, () => new Cluster()
                {
                    Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Floor((double)config.MaximumUsersPerCluster * config.UserCapacityLowPercentThreshold)))
                });

                await AddClusters(tx, dictionary, 5, ClusterStatus.Deleting);

                await tx.CommitAsync();
            }

            int expected = clusterCount - (int)Math.Floor(clusterCount * (config.UserCapacityHighPercentThreshold - config.UserCapacityLowPercentThreshold));
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// When the number of users in all the clusters reaches the UserCapacityLowPercentThreshold value, 
        /// decrease the number of clusters without going below the min threshold.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestTargetClusterCapacityDecreaseAtMinCount()
        {
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            int clusterCount = config.MinimumClusterCount + 1;
            using (ITransaction tx = stateManager.CreateTransaction())
            {
                await AddClusters(tx, dictionary, clusterCount, () => new Cluster()
                {
                    Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Floor((double)config.MaximumUsersPerCluster * config.UserCapacityLowPercentThreshold)))
                });
                
                await tx.CommitAsync();
            }

            int expected = config.MinimumClusterCount;
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// A new cluster should initiate a create cluster operation and switch its status to "creating" if successful.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestProcessNewCluster()
        {
            bool calledActual = false;
            string nameTemplate = "Test:{0}";
            string nameActual = null;

            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                CreateClusterAsyncFunc = name =>
                {
                    nameActual = name;
                    calledActual = true;
                    return Task.FromResult(String.Format(nameTemplate, name));
                }
            };

            ClusterService target = new ClusterService(clusterOperator, stateManager);

            Cluster cluster = new Cluster()
            {
                Status = ClusterStatus.New
            };

            await target.ProcessClusterStatusAsync(cluster);

            Assert.IsTrue(calledActual);
            Assert.AreEqual(ClusterStatus.Creating, cluster.Status);
            Assert.AreEqual(String.Format(nameTemplate, nameActual), cluster.Address);
        }
        
        /// <summary>
        /// A creating cluster should set its status to ready and populate fields when the cluster creation has completed.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestProcessCreatingClusterSuccess()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                GetClusterStatusAsyncFunc = name => Task.FromResult(ClusterOperationStatus.Ready)
            };

            ClusterService target = new ClusterService(clusterOperator, stateManager);
            Cluster cluster = new Cluster()
            {
                Status = ClusterStatus.Creating
            };

            await target.ProcessClusterStatusAsync(cluster);
            
            Assert.AreEqual(ClusterStatus.Ready, cluster.Status);
            Assert.IsTrue(cluster.CreatedOn.ToUniversalTime() <= DateTimeOffset.UtcNow);
            cluster.Ports.SequenceEqual(await clusterOperator.GetClusterPortsAsync(""));
        }

        /// <summary>
        /// A creating cluster should revert to "new" status if creation failed so that it can be retried.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestProcessCreatingClusterFailed()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                GetClusterStatusAsyncFunc = name => Task.FromResult(ClusterOperationStatus.CreateFailed)
            };

            ClusterService target = new ClusterService(clusterOperator, stateManager);
            Cluster cluster = new Cluster()
            {
                Status = ClusterStatus.Creating
            };

            await target.ProcessClusterStatusAsync(cluster);

            Assert.AreEqual(ClusterStatus.New, cluster.Status);
            Assert.AreEqual(0, cluster.Ports.Count());
            Assert.AreEqual(0, cluster.Users.Count());
        }

        /// <summary>
        /// A cluster marked for removal should initiate a delete cluster operation and switch its status to "deleting" if successful.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestProcessRemove()
        {
            bool calledActual = false;
            string nameActual = null;

            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                DeleteClusterAsyncFunc = name =>
                {
                    nameActual = name;
                    calledActual = true;
                    return Task.FromResult(true);
                }
            };

            ClusterService target = new ClusterService(clusterOperator, stateManager);

            Cluster cluster = new Cluster()
            {
                Status = ClusterStatus.Remove
            };

            await target.ProcessClusterStatusAsync(cluster);

            Assert.IsTrue(calledActual);
            Assert.AreEqual(ClusterStatus.Deleting, cluster.Status);
        }

        /// <summary>
        /// When deleting is complete, set the status to deleted.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestProcessDeletingSuccessful()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                GetClusterStatusAsyncFunc = domain => Task.FromResult(ClusterOperationStatus.ClusterNotFound)
            };

            ClusterService target = new ClusterService(clusterOperator, stateManager);

            Cluster cluster = new Cluster()
            {
                Status = ClusterStatus.Deleting
            };

            await target.ProcessClusterStatusAsync(cluster);
            
            Assert.AreEqual(ClusterStatus.Deleted, cluster.Status);
        }

        /// <summary>
        /// A cluster should be removed when its time limit has elapsed.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestProcessRemoveTimeLimit()
        {
            bool calledActual = false;

            ClusterConfig config = new ClusterConfig()
            {
                MaxClusterUptime = TimeSpan.FromHours(2)
            };
             
            MockReliableStateManager stateManager = new MockReliableStateManager();
            MockClusterOperator clusterOperator = new MockClusterOperator()
            {
                DeleteClusterAsyncFunc = name =>
                {
                    calledActual = true;
                    return Task.FromResult(true);
                }
            };

            ClusterService target = new ClusterService(clusterOperator, stateManager)
            {
                Config = config
            };

            Cluster cluster = new Cluster()
            {
                Status = ClusterStatus.Ready,
                CreatedOn = DateTimeOffset.UtcNow - config.MaxClusterUptime
            };

            await target.ProcessClusterStatusAsync(cluster);

            Assert.IsTrue(calledActual);
            Assert.AreEqual(ClusterStatus.Deleting, cluster.Status);
        }

        [TestMethod]
        public void JoinClusterSuccessful()
        {
            throw new NotImplementedException();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager);
        }

        private int GetRandom()
        {
            lock (locker)
            {
                return random.Next();
            }
        }

        private async Task AddClusters(ITransaction tx, IReliableDictionary<int, Cluster> dictionary, int count, Func<Cluster> newCluster)
        {
            for (int i = 0; i < count; ++i)
            {
                await dictionary.AddAsync(tx, GetRandom(), newCluster());
            }
        }

        private Task AddClusters(ITransaction tx, IReliableDictionary<int, Cluster> dictionary, int count, ClusterStatus status)
        {
            return this.AddClusters(tx, dictionary, count, () => new Cluster() { Status = status });
        }

    }
}