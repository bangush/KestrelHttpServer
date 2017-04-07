// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public class RioException : IOException
    {
        public RioException(string message) : base(message)
        {
        }

        public enum ActionType
        {
            Send,
            Receive,
            Notify,
            CompletionStatus,
            ResizeCompletionQueue,
            ResizeRequestQueue,
            GetPeerIPEndPoint,
            CloseSocket
        }
    }
}
