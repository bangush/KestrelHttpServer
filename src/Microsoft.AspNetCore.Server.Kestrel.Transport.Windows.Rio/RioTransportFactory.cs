// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio
{
    public class RioTransportFactory : ITransportFactory
    {
        private readonly RioTransportContext _baseTransportContext;

        public RioTransportFactory(
            IOptions<RioTransportOptions> options,
            IApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (applicationLifetime == null)
            {
                throw new ArgumentNullException(nameof(applicationLifetime));
            }
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            var logger  = loggerFactory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv");
            var trace = new RioTrace(logger);

            var threadCount = options.Value.ThreadCount;

            if (threadCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(threadCount),
                    threadCount,
                    "ThreadCount must be positive.");
            }

            RioFunctions.Initalize();

            _baseTransportContext = new RioTransportContext
            {
                Options = options.Value,
                AppLifetime = applicationLifetime,
                Log = trace
            };
        }

        public ITransport Create(IEndPointInformation endPointInformation, IConnectionHandler handler)
        {
            var transportContext = new RioTransportContext
            {
                Options = _baseTransportContext.Options,
                AppLifetime = _baseTransportContext.AppLifetime,
                Log = _baseTransportContext.Log,
                ConnectionHandler = handler
            };

            return new RioTransport(transportContext, endPointInformation);
        }
    }
}
