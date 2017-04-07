// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    [Flags]
    public enum CompletionEventType : ulong
    {
        None =         0,
        RioData =      1 << 0,
        SocketAccept = 1 << 1,
        SendCommit =   1 << 2,
        Shutdown =     1 << 3,
    }
}
