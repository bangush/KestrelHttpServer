// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public class RioCommitCompletionThread
    {
        private readonly Thread _thread;
        private CancellationToken _cancellationToken;
        private CompletionPort CompletionPort;

        private readonly TaskCompletionSource<int> _threadTcs = new TaskCompletionSource<int>();
        public bool NotCancelled => !_cancellationToken.IsCancellationRequested;

        public RioCommitCompletionThread(CompletionPort completionPort, int threadId, CancellationToken cancellationToken)
        {
            CompletionPort = completionPort;
            _cancellationToken = cancellationToken;
            _thread = new Thread(Run)
            {
                Name = $"RIO Commit Thread {threadId:00}",
                IsBackground = true
            };
        }

        public Task StartAsync()
        {
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
            var completionThread = (RioCommitCompletionThread)threadStart.State;

            threadStart.StartTcs.TrySetResult(0);

            completionThread.ProcessCompletions();
        }


        private void ProcessCompletions()
        {
            while (NotCancelled)
            {
                CompletionPort.Wait(out var eventType, out var dataIntPtr);

                if (eventType == CompletionEventType.SendCommit)
                {
                    ((RioRequestQueue)dataIntPtr).FlushSends();
                }
                else if (eventType == CompletionEventType.Shutdown)
                {
                    break;
                }
            }

            _threadTcs.TrySetResult(0);
        }
    }
}
