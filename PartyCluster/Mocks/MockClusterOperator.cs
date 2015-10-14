// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Domain;

    public class MockClusterOperator : IClusterOperator
    {
        public MockClusterOperator()
        {
            this.CreateClusterAsyncFunc = domain => Task.FromResult(true);
            this.DeleteClusterAsyncFunc = domain => Task.FromResult(true);
            this.GetClusterPortsAsyncFunc = domain => Task.FromResult(new List<int>() {80, 8081, 405, 520});
            this.GetClusterStatusAsyncFunc = domain => Task.FromResult(ClusterOperationStatus.Ready);
        }

        public Func<string, Task> CreateClusterAsyncFunc { get; set; }

        public Func<string, Task> DeleteClusterAsyncFunc { get; set; }

        public Func<string, Task> GetClusterPortsAsyncFunc { get; set; }

        public Func<string, Task> GetClusterStatusAsyncFunc { get; set; }

        public Task CreateClusterAsync(string domain)
        {
            throw new NotImplementedException();
        }

        public Task DeleteClusterAsync(string domain)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<int>> GetClusterPortsAsync(string domain)
        {
            throw new NotImplementedException();
        }

        public Task<ClusterOperationStatus> GetClusterStatusAsync(string domain)
        {
            throw new NotImplementedException();
        }
    }
}