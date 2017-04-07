// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public struct RioListenSocket : IDisposable
    {
#pragma warning disable 0169, 0649
        private IntPtr _handle;
#pragma warning restore 0169, 0649

        public static RioListenSocket Create()
        {
            return RioFunctions.CreateListenSocket();
        }

        public bool IsNull => _handle == IntPtr.Zero;

        public void Bind(IPEndPoint endPoint)
        {
            RioFunctions.BindSocket(this, endPoint);
        }

        public void Listen(int listenBacklog)
        {
            RioFunctions.Listen(this, listenBacklog);
        }

        public RioSocket AcceptSocket()
        {
            return RioFunctions.AcceptSocket(this);
        }

        public void NoDelay(bool enable)
        {
            RioFunctions.SetTcpNodelay(this, enable);
        }

        public IPEndPoint GetSockIPEndPoint()
        {
            return RioFunctions.GetSockIPEndPoint(this);
        }

        public void Dispose()
        {
            RioFunctions.CloseSocket(this);
        }
    }
}
