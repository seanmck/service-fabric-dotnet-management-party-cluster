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


    [TestClass]
    public class ClusterServiceTests
    {
        /// <summary>
        /// First time around there are no clusters. This tests that the minimum number of clusters is created initially.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestInitState()
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
            Random random = new Random(7);
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
                for (int i = 0; i < readyClusters; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Status = ClusterStatus.Ready
                    });
                }

                for (int i = 0; i < deletingClusterCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Status = ClusterStatus.Deleting
                    });
                }
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
            Random random = new Random(7);
            ClusterConfig config = new ClusterConfig();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager)
            {
                Config = config
            };

            var dictionary = await stateManager.GetOrAddAsync<IReliableDictionary<int, Cluster>>(ClusterService.ClusterDictionaryName);

            using (ITransaction tx = stateManager.CreateTransaction())
            {
                for (int i = 0; i < config.MinimumClusterCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Status = ClusterStatus.Ready
                    });
                }
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
            Random random = new Random(7);
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

                for (int i = 0; i < readyCount + config.MinimumClusterCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Status = ClusterStatus.Ready
                    });
                }

                for (int i = 0; i < deletingCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Status = ClusterStatus.Deleting
                    });
                }

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
            Random random = new Random(7);
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
                for (int i = 0; i < withUsers; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Users = new List<ClusterUser>() { new ClusterUser() },
                        Status = ClusterStatus.Ready
                    });
                }

                for (int i = 0; i < withoutUsers; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Status = ClusterStatus.Ready
                    });
                }

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
            Random random = new Random(7);
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
                for (int i = 0; i < clusterCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Ceiling((double)config.MaximumUsersPerCluster * config.UserCapacityHighPercentThreshold)))
                    });
                }

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
            Random random = new Random(7);
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
                for (int i = 0; i < clusterCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Ceiling((double)config.MaximumUsersPerCluster * config.UserCapacityHighPercentThreshold)))
                    });
                }

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
            Random random = new Random(7);
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
                for (int i = 0; i < clusterCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Floor((double)config.MaximumUsersPerCluster * config.UserCapacityLowPercentThreshold)))
                    });
                }

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
            Random random = new Random(7);
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
                for (int i = 0; i < clusterCount; ++i)
                {
                    await dictionary.AddAsync(tx, random.Next(), new Cluster()
                    {
                        Users = new List<ClusterUser>(Enumerable.Repeat(new ClusterUser(), (int)Math.Floor((double)config.MaximumUsersPerCluster * config.UserCapacityLowPercentThreshold)))
                    });
                }

                await tx.CommitAsync();
            }

            int expected = config.MinimumClusterCount;
            int actual = await target.GetTargetClusterCapacityAsync();

            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void JoinClusterSuccessful()
        {
            throw new NotImplementedException();
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager);
        }
    }
}