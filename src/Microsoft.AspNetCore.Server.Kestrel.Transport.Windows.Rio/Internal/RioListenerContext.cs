// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public class RioListenerContext
    {
        public RioListenerContext(RioTransportContext transportContext)
        {
            TransportContext = transportContext;
        }

        public RioTransportContext TransportContext { get; set; }

        public IEndPointInformation EndPointInformation { get; set; }

        public IPEndPoint LocalEndPoint { get; set; }

        public List<RioCompletionThread> CompletionThreads { get; set; }

    }
}
