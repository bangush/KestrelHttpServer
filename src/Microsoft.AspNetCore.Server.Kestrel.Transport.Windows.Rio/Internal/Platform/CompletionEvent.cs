// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CompletionEvent
    {
        public CompletionEventType EventType;
        public IntPtr DataIntPtr;
        public IntPtr Internal;
        public uint DataUInt;
    }
}
