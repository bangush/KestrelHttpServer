// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public class RioConnectionContext : IConnectionInformation
    {
        public RioConnectionContext()
        {
        }

        public RioConnectionContext(RioThreadContext context)
        {
            ThreadContext = context;
        }

        public RioThreadContext ThreadContext { get; set; }

        public IPEndPoint RemoteEndPoint { get; set; }
        public IPEndPoint LocalEndPoint => ThreadContext.LocalEndPoint;

        public PipeFactory PipeFactory => ThreadContext.PipeFactory;

        public IScheduler InputWriterScheduler => SyncScheduler.Shared;
        public IScheduler OutputReaderScheduler => SyncScheduler.Shared;

        public ITimeoutControl TimeoutControl { get; set; }

        protected RioBufferSegment GetSegmentFromMemory(Buffer<byte> memory) => ThreadContext.GetSegmentFromMemory(memory);
    }
}