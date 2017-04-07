// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public struct RioSocket : IDisposable
    {
#pragma warning disable 0169, 0649
        private IntPtr _handle;
#pragma warning restore 0169, 0649

        public bool IsInvalid => _handle == (IntPtr)(-1);

        public IPEndPoint GetPeerIPEndPoint()
        {
            return RioFunctions.GetPeerIPEndPoint(this);
        }

        public IPEndPoint GetSockIPEndPoint()
        {
            return RioFunctions.GetSockIPEndPoint(this);
        }

        public void Dispose()
        {
            RioFunctions.CloseSocket(this);
        }

        public static explicit operator IntPtr(RioSocket socket)
        {
            return socket._handle;
        }

        public static explicit operator RioSocket(IntPtr socket)
        {
            return new RioSocket() { _handle = socket };
        }
    }
}
