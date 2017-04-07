// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio
{
    public class RioTransport : ITransport
    {
        private readonly CancellationTokenSource _cts;
        private readonly IEndPointInformation _endPointInformation;

        private CompletionPort _sendCompletionPort;

        private IAsyncDisposable _listener;

        // For testing
        public RioTransport(RioTransportContext context, IEndPointInformation endPointInformation)
        {
            _sendCompletionPort = CompletionPort.Create();

            TransportContext = context;

            _cts = new CancellationTokenSource();
            _endPointInformation = endPointInformation;
        }

        public RioTransportContext TransportContext { get; }

        public List<RioCompletionThread> ConnectionThreads { get; } = new List<RioCompletionThread>();
        public List<RioCommitCompletionThread> CommitThreads { get; } = new List<RioCommitCompletionThread>();

        public IApplicationLifetime AppLifetime => TransportContext.AppLifetime;
        public IRioTrace Log => TransportContext.Log;
        public RioTransportOptions TransportOptions => TransportContext.Options;

        public async Task StopAsync()
        {
            try
            {
                _cts.Cancel();

                await Task.WhenAll(ConnectionThreads.Select(thread => thread.StopAsync(TimeSpan.FromSeconds(2.5))).ToArray())
                    .ConfigureAwait(false);
            }
            catch (AggregateException aggEx)
            {
                // An uncaught exception was likely thrown from the libuv event loop.
                // The original error that crashed one loop may have caused secondary errors in others.
                // Make sure that the stack trace of the original error is logged.
                foreach (var ex in aggEx.InnerExceptions)
                {
                    Log.LogCritical("Failed to gracefully close Kestrel.", ex);
                }

                throw;
            }

            ConnectionThreads.Clear();
#if DEBUG
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#endif
        }

        public async Task BindAsync()
        {
            try
            {
                var listener = new RioListener(TransportContext);
                _listener = listener;

                // TODO: Move thread management to LibuvTransportFactory
                // TODO: Split endpoint management from thread management
                for (var index = 0; index < TransportOptions.ThreadCount; index++)
                {
                    ConnectionThreads.Add(new RioCompletionThread(_sendCompletionPort, listener, index, _cts.Token));
                }

                for (var index = 0; index < TransportOptions.ThreadCount; index++)
                {
                    CommitThreads.Add(new RioCommitCompletionThread(_sendCompletionPort, index, _cts.Token));
                }

                await listener.StartAsync(_endPointInformation, ConnectionThreads).ConfigureAwait(false);

                foreach (var thread in CommitThreads)
                {
                    await thread.StartAsync().ConfigureAwait(false);
                }

                foreach (var thread in ConnectionThreads)
                {
                    await thread.StartAsync().ConfigureAwait(false);
                }
            }
            catch (RioException ex) //when (ex.StatusCode == LibuvConstants.EADDRINUSE)
            {
                await UnbindAsync().ConfigureAwait(false);
                throw new AddressInUseException(ex.Message, ex);
            }
            catch
            {
                await UnbindAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task UnbindAsync()
        {
            if (!await WaitAsync(_listener.DisposeAsync(), TimeSpan.FromSeconds(2.5)).ConfigureAwait(false))
            {
                Log.LogError(0, null, "Disposing listeners failed");
            }
        }

        private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
        {
            return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
        }
    }
}
