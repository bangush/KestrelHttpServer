// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public class RioTransportContext
    {
        public RioTransportOptions Options { get; set; }

        public IApplicationLifetime AppLifetime { get; set; }

        public IRioTrace Log { get; set; }

        public IConnectionHandler ConnectionHandler { get; set; }
    }
}
