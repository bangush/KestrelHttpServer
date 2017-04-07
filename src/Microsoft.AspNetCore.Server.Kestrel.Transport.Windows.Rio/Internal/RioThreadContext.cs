// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public abstract class RioThreadContext
    {
        public RioThreadContext()
        {
        }

        public RioThreadContext(RioListenerContext context)
        {
            ListenerContext = context;
        }

        public RioListenerContext ListenerContext { get; set; }

        public IPEndPoint LocalEndPoint => ListenerContext.LocalEndPoint;

        public PipeFactory PipeFactory { get; protected set; }

        public ITimeoutControl TimeoutControl { get; set; }

        public abstract RioBufferSegment GetSegmentFromMemory(Buffer<byte> memory);
    }
}
