// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public struct CompletionPort
    {
#pragma warning disable 0169, 0649
        private IntPtr _handle;
#pragma warning restore 0169, 0649

        public static CompletionPort Create()
        {
            return RioFunctions.CreateCompletionPort();
        }

        public bool IsNull => _handle == IntPtr.Zero;

        public void Wait(out CompletionEventType eventType, out IntPtr dataIntPtr)
        {
            uint dataUInt;
            RioFunctions.Wait(this, out dataUInt, out eventType, out dataIntPtr);
        }

        public void WaitMultiple(ref CompletionEvent entries, uint maxCount, out uint count)
        {
            RioFunctions.WaitForNextCompletions(this, ref entries, maxCount, out count);
        }

        public RioCompletionQueue CreateIocpQueue(uint queueSize)
        {
            return RioFunctions.CreateIocpCompletionQueue(this, queueSize);
        }

        public void AcceptSocket(RioSocket socket)
        {
            RioFunctions.PostQueuedCompletionStatus(this, 0, CompletionEventType.SocketAccept, (IntPtr)socket);
        }

        public static CompletionPort Create(uint maxConcurrentThreads)
        {
            return RioFunctions.CreateCompletionPort(maxConcurrentThreads);
        }
    }
}
