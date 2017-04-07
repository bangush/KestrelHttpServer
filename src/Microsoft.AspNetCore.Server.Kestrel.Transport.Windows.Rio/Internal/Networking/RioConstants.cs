// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    internal static class RioConstants
    {
        public const int ListenBacklog = 128;

        public readonly static WaitCallback RunAction = (object action) => Unsafe.As<Action>(action)();

        public readonly static SyncScheduler SyncScheduler = new SyncScheduler();
    }
}
