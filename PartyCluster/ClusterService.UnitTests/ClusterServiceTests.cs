// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace ClusterService.UnitTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Mocks;

    [TestClass]
    public class ClusterServiceTests
    {
        [TestMethod]
        public void JoinClusterSuccessful()
        {
            MockReliableStateManager stateManager = new MockReliableStateManager();
            ClusterService target = new ClusterService(null, stateManager);
        }
    }
}