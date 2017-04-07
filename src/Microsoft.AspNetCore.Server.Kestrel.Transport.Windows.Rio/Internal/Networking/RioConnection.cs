// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal
{
    public sealed class RioConnection : RioConnectionContext, ITimeoutControl
    {
        private const long RestartSendCorrelations = -2;

        private readonly long _connectionId;
        private RioSocket _socket;
        private RioRequestQueue _requestQueue;
        private CompletionPort _sendCommitPort;
        private bool _disposedValue;

        private long _previousSendCorrelation = RestartSendCorrelations;

        private const int MaxWritesPerSocket = 3;
        private readonly SemaphoreSlim _outgoingSends = new SemaphoreSlim(MaxWritesPerSocket);
        private readonly SemaphoreSlim _previousSendsComplete = new SemaphoreSlim(1);

        private Task _sendTask;

        private PreservedBuffer _sendingBuffer;
        private WritableBuffer _buffer;
        private IConnectionContext _connectionContext;

        internal RioConnection(RioThreadContext context, RioSocket socket, long connectionId, RioRequestQueue requestQueue, CompletionPort sendCommitPort) : base(context)
        {
            _socket = socket;
            _connectionId = connectionId;
            TimeoutControl = this;

            _connectionContext = this.ThreadContext.ListenerContext.TransportContext.ConnectionHandler.OnConnection(this);

            Input = _connectionContext.Input;
            Output = _connectionContext.Output;

            _requestQueue = requestQueue;
            _sendCommitPort = sendCommitPort;

            Receive();
            _sendTask = ProcessSends();

        }

        public IPipeWriter Input { get; set; }
        public IPipeReader Output { get; set; }

        private async Task ProcessSends()
        {
            while (true)
            {
                var result = await Output.ReadAsync();
                var buffer = result.Buffer;

                if (buffer.IsSingleSpan)
                {
                    await SendAsync(buffer.First, endOfMessage: true);
                }
                else
                {
                    var enumerator = buffer.GetEnumerator();

                    if (enumerator.MoveNext())
                    {
                        var current = enumerator.Current;

                        while (enumerator.MoveNext())
                        {
                            var next = enumerator.Current;

                            await SendAsync(current, endOfMessage: false);
                            current = next;
                        }

                        await PreviousSendingComplete();

                        _sendingBuffer = buffer.Preserve();

                        await SendAsync(current, endOfMessage: true);

                    }
                    
                }

                if (buffer.IsEmpty && result.IsCompleted)
                {
                    break;
                }

                Output.Advance(buffer.End);
            }

            Output.Complete();
        }

        private Task SendAsync(Buffer<byte> memory, bool endOfMessage)
        {
            if (!IsReadyToSend())
            {
                return SendAsyncAwaited(memory, endOfMessage);
            }

            var flushSends = endOfMessage || MaxOutstandingSendsReached;

            Send(GetSegmentFromMemory(memory), flushSends);

            if (flushSends && !endOfMessage)
            {
                return AwaitReadyToSend();
            }

            return TaskCache.CompletedTask;
        }

        private async Task SendAsyncAwaited(Buffer<byte> memory, bool endOfMessage)
        {
            await ReadyToSend();

            var flushSends = endOfMessage || MaxOutstandingSendsReached;

            Send(GetSegmentFromMemory(memory), flushSends);

            if (flushSends && !endOfMessage)
            {
                await ReadyToSend();
            }
        }

        private async Task AwaitReadyToSend()
        {
            await ReadyToSend();
        }

        private void Send(RioBufferSegment segment, bool flushSends)
        {
            if (!flushSends)
            {
                _requestQueue.QueueSend(ref segment);
            }
            else
            {
                _requestQueue.CommitSend(_sendCommitPort, ref segment, CompleteSendCorrelation());
            }
        }

        private Task PreviousSendingComplete() => _previousSendsComplete.WaitAsync();

        private Task ReadyToSend() => _outgoingSends.WaitAsync();

        private bool IsReadyToSend() => _outgoingSends.Wait(0);

        private bool MaxOutstandingSendsReached => _outgoingSends.CurrentCount == 0;

        private void CompleteSend() => _outgoingSends.Release();

        private void CompletePreviousSending()
        {
            _sendingBuffer.Dispose();
            _previousSendsComplete.Release();
        }

        private long CompleteSendCorrelation()
        {
            var sendCorrelation = _previousSendCorrelation;
            if (sendCorrelation == int.MinValue)
            {
                _previousSendCorrelation = RestartSendCorrelations;
                return RestartSendCorrelations;
            }

            _previousSendCorrelation = sendCorrelation - 1;
            return sendCorrelation - 1;
        }

        public void SendComplete(long sendCorrelation)
        {
            CompleteSend();

            if (sendCorrelation == _previousSendCorrelation)
            {
                CompletePreviousSending();
            }
        }

        private void Receive()
        {
            _buffer = Input.Alloc(2048);
            var receiveBufferSeg = GetSegmentFromMemory(_buffer.Buffer);

            _requestQueue.Receive(ref receiveBufferSeg);
        }

        public void ReceiveCommit(uint bytesTransferred)
        {
            if (bytesTransferred == 0)
            {
                _buffer.FlushAsync();
                Input.Complete();
            }
            else
            {
                _buffer.Advance((int)bytesTransferred);
                //_buffer.Commit();
            // TODO:
                _buffer.FlushAsync();

                Receive();
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                _disposedValue = true;

                //_rioThread.RemoveConnection(_connectionId);
                _socket.Dispose();
            }
        }

        ~RioConnection()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void SetTimeout(long milliseconds, TimeoutAction timeoutAction)
        {
        }

        public void ResetTimeout(long milliseconds, TimeoutAction timeoutAction)
        {
        }

        public void CancelTimeout()
        {
        }
    }
}
