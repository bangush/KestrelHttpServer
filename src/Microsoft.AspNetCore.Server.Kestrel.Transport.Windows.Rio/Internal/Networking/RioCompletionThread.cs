// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public class RioCompletionThread : RioThreadContext
    {
        private const int MaxSocketsPerThread = 256000;
        private const int MaxReadsPerSocket = 1;
        public const int MaxWritesPerSocket = 2;
        public const int MaxOutsandingCompletionsPerThread = (MaxReadsPerSocket + MaxWritesPerSocket) * MaxSocketsPerThread;

        private readonly Thread _thread;
        private CancellationToken _cancellationToken;

        private readonly object _startSync = new object();
        private bool _initCompleted = false;

        private readonly TaskCompletionSource<int> _threadTcs = new TaskCompletionSource<int>();

        private CompletionPort _completionPort;
        private RioCompletionQueue _completionQueue;

        private Dictionary<long, RioConnection> _connections;
        private List<BufferMapping> _bufferIdMappings;

        public CompletionPort CompletionPort => _completionPort;
        public RioCompletionQueue CompletionQueue => _completionQueue;

        private CompletionPort _sendCommitcompletionPort;

        public bool NotCancelled => !_cancellationToken.IsCancellationRequested;

        public long _connectionsCreated;

        bool _activatedNotify;

        public RioCompletionThread(CompletionPort sendCommitcompletionPort, RioListenerContext context, int threadId, CancellationToken cancellationToken) : base(context)
        {
            _cancellationToken = cancellationToken;

            _thread = new Thread(Run)
            {
                Name = $"RIO Completion Thread {threadId:00}",
                IsBackground = true
            };

            _sendCommitcompletionPort = sendCommitcompletionPort;
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

        public async Task StopAsync(TimeSpan timeout)
        {
            await Task.Delay(0);
            lock (_startSync)
            {
                if (!_initCompleted)
                {
                    return;
                }
            }

            if (!_threadTcs.Task.IsCompleted)
            {
                DisposeConnections();
            }
        }

        private void DisposeConnections()
        {
            //try
            //{
            //    // Close and wait for all connections
            //    if (!await ConnectionManager.WalkConnectionsAndCloseAsync(_shutdownTimeout).ConfigureAwait(false))
            //    {
            //        _log.NotAllConnectionsClosedGracefully();

            //        if (!await ConnectionManager.WalkConnectionsAndAbortAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false))
            //        {
            //            _log.NotAllConnectionsAborted();
            //        }
            //    }

            //    var result = await WaitAsync(PostAsync(state =>
            //    {
            //        var listener = state;
            //        listener.WriteReqPool.Dispose();
            //    },
            //    this), _shutdownTimeout).ConfigureAwait(false);

            //    if (!result)
            //    {
            //        _log.LogError(0, null, "Disposing write requests failed");
            //    }
            //}
            //finally
            //{
            //    PipelineFactory.Dispose();
            //}
        }

        private static void Run(object state)
        {
            var threadStart = ((RioThreadStart)state);
            var completionThread = (RioCompletionThread)threadStart.State;

            completionThread.Initalize();

            threadStart.StartTcs.TrySetResult(0);

            completionThread.ProcessCompletions();
        }

        private void Initalize()
        {
            lock (_startSync)
            {
                var completionPort = CompletionPort.Create();

                _completionPort = completionPort;
                _completionQueue = completionPort.CreateIocpQueue(MaxOutsandingCompletionsPerThread);

                _connections = new Dictionary<long, RioConnection>();
                _bufferIdMappings = new List<BufferMapping>();

                var memoryPool = new MemoryPool();
                memoryPool.RegisterSlabAllocationCallback((slab) => OnSlabAllocated(slab));
                memoryPool.RegisterSlabDeallocationCallback((slab) => OnSlabDeallocated(slab));
                PipeFactory = new PipeFactory(memoryPool);

                _initCompleted = true;
            }
        }

        public void AddConnection(long key, RioConnection value)
        {
            lock (_connections)
            {
                _connections.Add(key, value);
            }
        }

        public void RemoveConnection(long key)
        {
            lock (_connections)
            {
                _connections.Remove(key);
            }
        }

        private void ResetNotify()
        {
            _activatedNotify = false;
        }

        private bool SetNotify()
        {
            if (!_activatedNotify)
            {
                _activatedNotify = true;
                CompletionQueue.Notify();

                return true;
            }

            return false;
        }


        private void ProcessCompletions()
        {
            const uint MaxEvents = 25;

            var events = new CompletionEvent[MaxEvents];
            ref var startEvent = ref events[0];

            RioRequestResults results;

            CompletionQueue.Notify();
            while (NotCancelled)
            {
                CompletionPort.WaitMultiple(ref startEvent, MaxEvents, out var eventCount);

                var eventTypes = CompletionEventType.None;
                for (var i = 0; i < eventCount; i++)
                {
                    var eventType = events[i].EventType;
                    eventTypes |= eventType;

                    if (eventType == CompletionEventType.SocketAccept)
                    {
                        SetupSocket((RioSocket)events[i].DataIntPtr);
                    }
                }

                if ((eventTypes & CompletionEventType.RioData) != 0)
                {
                    ResetNotify();

                    while (true)
                    {
                        var count = CompletionQueue.Dequeue(ref results);
                        if (count == 0)
                        {
                            if (SetNotify())
                            {
                                continue;
                            }
                            break;
                        }

                        Commit(ref results, count);
                    }
                }


                if ((eventTypes & CompletionEventType.Shutdown) != 0)
                {
                    break;
                }
            }

            _threadTcs.TrySetResult(0);
        }

        private void SetupSocket(RioSocket socket)
        {
            var connectionId = ++_connectionsCreated;
            var requestQueue = CompletionQueue.CreateRequestQueue(socket, connectionId);

            var connection = new RioConnection(this, socket, connectionId, requestQueue, _sendCommitcompletionPort);

            connection.ThreadContext = this;
            connection.RemoteEndPoint = socket.GetPeerIPEndPoint();


            AddConnection(connectionId, connection);
        }

        private unsafe void Commit(ref RioRequestResults results, uint count)
        {
            for (var i = 0; i < count; i++)
            {
                var result = results[i];

                RioConnection connection;
                bool found;
                lock (_connections)
                {
                    found = _connections.TryGetValue(result.ConnectionCorrelation, out connection);
                }

                if (found)
                {
                    if (result.RequestCorrelation >= 0)
                    {
                        connection.ReceiveCommit(result.BytesTransferred);
                    }
                    else
                    {
                        connection.SendComplete(result.RequestCorrelation);
                    }
                }
            }
        }

        private RioRegisteredBuffer GetRegisteredBuffer(IntPtr address, out long startAddress)
        {
            var buffer = default(RioRegisteredBuffer);
            startAddress = 0;

            lock (_bufferIdMappings)
            {
                var addressLong = address.ToInt64();

                // Can binary search if it's too slow
                var length = _bufferIdMappings.Count;

                for (var i = 0; i < length; i++)
                {
                    var mapping = _bufferIdMappings[i];
                    if (addressLong >= mapping.Start && addressLong <= mapping.End)
                    {
                        buffer = mapping.Buffer;
                        startAddress = mapping.Start;
                        break;
                    }
                }
            }

            return buffer;
        }

        public override unsafe RioBufferSegment GetSegmentFromMemory(Buffer<byte> memory)
        {
            // It's ok to unpin the handle here because the memory is from the pool
            // we created, which is already pinned.
            var pin = memory.Pin();
            var spanPtr = (IntPtr)pin.PinnedPointer;
            pin.Free();

            long startAddress;
            long spanAddress = spanPtr.ToInt64();
            var bufferId = GetRegisteredBuffer(spanPtr, out startAddress);

            checked
            {
                var offset = (uint)(spanAddress - startAddress);
                return new RioBufferSegment(bufferId, offset, (uint)memory.Length);
            }
        }

        private void OnSlabAllocated(MemoryPoolSlab slab)
        {
            lock (_bufferIdMappings)
            {
                var memoryPtr = slab.NativePointer;
                var buffer = RioRegisteredBuffer.Create(memoryPtr, (uint)slab.Length);
                var addressLong = memoryPtr.ToInt64();

                _bufferIdMappings.Add(new BufferMapping
                {
                    Buffer = buffer,
                    Start = addressLong,
                    End = addressLong + slab.Length
                });
            }
        }

        private void OnSlabDeallocated(MemoryPoolSlab slab)
        {
            var memoryPtr = slab.NativePointer;
            var addressLong = memoryPtr.ToInt64();

            lock (_bufferIdMappings)
            {
                for (int i = _bufferIdMappings.Count - 1; i >= 0; i--)
                {
                    var bufferMapping = _bufferIdMappings[i];
                    if (addressLong == bufferMapping.Start)
                    {
                        _bufferIdMappings.RemoveAt(i);
                        bufferMapping.Buffer.Dispose();
                        break;
                    }
                }
            }
        }

        private readonly static WaitCallback _runAction = (object action) => ((Action)action)();

        public void Schedule(Action action)
        {
            ThreadPool.QueueUserWorkItem(_runAction, action);
        }

        private struct BufferMapping
        {
            public RioRegisteredBuffer Buffer;
            public long Start;
            public long End;

            public override string ToString()
            {
                return $"{Buffer} ({Start}) - ({End})";
            }
        }
    }
}
