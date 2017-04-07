// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public struct RioRegisteredBuffer : IDisposable
    {
#pragma warning disable 0169, 0649
        private IntPtr _handle;
#pragma warning restore 0169, 0649

        public static RioRegisteredBuffer Create(IntPtr dataBuffer, uint dataLength)
        {
            return RioFunctions.RegisterBuffer(dataBuffer, dataLength);
        }

        public void Dispose()
        {
            RioFunctions.DeregisterBuffer(this);
        }
    }
}
