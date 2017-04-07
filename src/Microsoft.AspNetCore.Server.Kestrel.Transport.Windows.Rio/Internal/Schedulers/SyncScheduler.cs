// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public sealed class SyncScheduler : IScheduler
    {
        public static SyncScheduler Shared => RioConstants.SyncScheduler;

        public void Schedule(Action action) => action();
    }
}
