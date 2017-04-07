// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public struct Event
    {
#pragma warning disable 0169, 0649
        private IntPtr _handle;
#pragma warning restore 0169, 0649

        public static Event Create()
        {
            return RioFunctions.CreateEvent();
        }

        public RioCompletionQueue CreateCompletionQueue(uint queueSize)
        {
            return RioFunctions.CreateCompletionQueue(this, queueSize);
        }
    }
}
