// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services;

    public class WebService : StatelessService
    {
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            // TODO: If your service needs to handle user requests, return the list of ServiceInstanceListeners from here.
            return base.CreateServiceInstanceListeners();
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following with your own logic.

            int iterations = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Working-{0}", iterations++);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}