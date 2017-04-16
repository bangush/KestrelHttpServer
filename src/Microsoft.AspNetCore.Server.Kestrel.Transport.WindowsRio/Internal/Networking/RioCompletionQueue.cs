// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public struct RioCompletionQueue
    {
#pragma warning disable 0169, 0649
        private IntPtr _handle;
#pragma warning restore 0169, 0649

        public bool IsNull => _handle == IntPtr.Zero;

        public void Notify()
        {
            RioFunctions.Notify(this);
        }

        public uint Dequeue(ref RioRequestResults results)
        {
            return RioFunctions.DequeueCompletions(this, ref results);
        }

        //public RioRequestQueue CreateRequestQueue(RioConnectedSocket socket, long connectionId)
        //{
        //    return RioFunctions.CreateRequestQueue(this, socket, connectionId);
        //}

        //public static explicit operator IntPtr(RioCompletionQueue queue)
        //{
        //    return queue._handle;
        //}

        //public static explicit operator RioCompletionQueue(IntPtr queue)
        //{
        //    return new RioCompletionQueue() { _handle = queue };
        //}
    }
}
