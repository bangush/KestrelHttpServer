// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    /// <summary>
    /// Base class for listeners in Kestrel. Listens for incoming connections
    /// </summary>
    public class RioListener : RioListenerContext, IAsyncDisposable
    {
        private readonly Thread _thread;

        private readonly object _startSync = new object();
        private bool _initCompleted = false;
        private int _roundRobinIndex;

        public RioListener(RioTransportContext transportContext) : base(transportContext)
        {
            _thread = new Thread(Run)
            {
                Name = $"RIO Listen Thread",
                IsBackground = true
            };
        }

        private RioListenSocket ListenSocket;

        public IRioTrace Log => TransportContext.Log;

        public Task StartAsync(
            IEndPointInformation endPointInformation,
            List<RioCompletionThread> completionThreads)
        {
            EndPointInformation = endPointInformation;
            CompletionThreads = completionThreads;

            var tcs = new TaskCompletionSource<int>();
            _thread.Start(
                new RioThreadStart()
                {
                    State = this,
                    StartTcs = tcs
                });

            return tcs.Task;
        }

        private static void Run(object state)
        {
            var threadStart = ((RioThreadStart)state);
            var completionThread = (RioListener)threadStart.State;

            try
            {
                completionThread.Initalize();
            }
            catch (Exception ex)
            {
                threadStart.StartTcs.TrySetException(ex);
                return;
            }

            threadStart.StartTcs.TrySetResult(0);

            completionThread.Accept();
        }

        private void Initalize()
        {
            lock (_startSync)
            {
                if (_initCompleted)
                {
                    throw new InvalidOperationException();
                }

                ListenSocket = CreateListenSocket();
                ListenSocket.Listen(RioConstants.ListenBacklog);

                _initCompleted = true;
            }
        }

        /// <summary>
        /// Creates the socket used to listen for incoming connections
        /// </summary>
        private RioListenSocket CreateListenSocket()
        {
            switch (EndPointInformation.Type)
            {
                case ListenType.IPEndPoint:
                    var socket = RioListenSocket.Create();
                    try
                    {
                        socket.NoDelay(EndPointInformation.NoDelay);
                        socket.Bind(EndPointInformation.IPEndPoint);

                        // If requested port was "0", replace with assigned dynamic port.
                        EndPointInformation.IPEndPoint = socket.GetSockIPEndPoint();
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }

                    return socket;
                default:
                    throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Handles incoming connections
        /// </summary>
        private void Accept()
        {
            RioSocket acceptSocket = default(RioSocket);

            while (true)
            {
                try
                {
                    acceptSocket = ListenSocket.AcceptSocket();
                    DispatchConnection(acceptSocket);
                }
                catch (RioException ex)
                {
                    Log.LogError(0, ex, "Listener.OnConnection");
                    acceptSocket.Dispose();
                }
            }
        }

        protected void DispatchConnection(RioSocket socket)
        {
            var completionThreads = CompletionThreads;

            var index = ++_roundRobinIndex;
            var count = completionThreads.Count;
            if (index >= count)
            {
                index = 0;
                _roundRobinIndex = 0;
            }

            completionThreads[index].CompletionPort.AcceptSocket(socket);
        }

        public async Task DisposeAsync()
        {
            await Task.Delay(0);
            //// Ensure the event loop is still running.
            //// If the event loop isn't running and we try to wait on this Post
            //// to complete, then LibuvTransport will never be disposed and
            //// the exception that stopped the event loop will never be surfaced.
            //if (Thread.FatalError == null && ListenSocket != null)
            //{
            //    await Thread.PostAsync(state =>
            //    {
            //        var listener = (RioListener)state;
            //        listener.ListenSocket.Dispose();

            //        listener._closed = true;

            //    }, this).ConfigureAwait(false);
            //}

            //ListenSocket = null;
        }
    }
}
