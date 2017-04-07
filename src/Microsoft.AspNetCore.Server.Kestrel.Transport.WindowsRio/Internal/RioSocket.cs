// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public class RioSocket
    {
        private readonly static WaitCallback _sendCallback = (instance, context, wait, waitResult) =>
        {
            ((RioSocket) GCHandle.FromIntPtr(context).Target).OnSend();
        };

        private readonly static WorkCallback _commitCallback = (instance, context, work) =>
        {
            ((RioSocket) GCHandle.FromIntPtr(context).Target).OnCommit();
        };

        private readonly static WaitCallback _receiveCallback = (instance, context, wait, waitResult) =>
        {
            ((RioSocket) GCHandle.FromIntPtr(context).Target).OnRead();
        };

        private readonly BufferMapper _bufferMapper;

        private RioConnectedSocket _socket;

        private ManualResetGate _sendGate = new ManualResetGate();
        private Event _sendEvent;
        private Event _receiveEvent;
        private RioCompletionQueue _sendQueue;
        private RioCompletionQueue _receiveQueue;
        private RioRequestQueue _requestQueue;

        private GCHandle _handle;
        private WinThreadpoolWait _receiveWait;
        private WinThreadpoolWait _sendWait;
        private WinThreadpoolWork _sendCommit;

        private AutoResetGate<ReceiveResult> _receiveGate = new AutoResetGate<ReceiveResult>();

        private const int _maxOutstandingSends = 10;
        private int _outstandingSends;

        private int _pendingCommit = 0;

        public IPEndPoint RemoteEndPoint { get; }
        public IPEndPoint LocalEndPoint { get; }

        public RioSocket(RioConnectedSocket socket, BufferMapper bufferMapper)
        {
            _socket = socket;
            _bufferMapper = bufferMapper;

            RemoteEndPoint = RioFunctions.GetPeerIPEndPoint(_socket);
            LocalEndPoint = RioFunctions.GetSockIPEndPoint(_socket);

            _sendEvent = Event.Create();
            _sendQueue = _sendEvent.CreateCompletionQueue(_maxOutstandingSends);

            _receiveEvent = Event.Create();
            _receiveQueue = _receiveEvent.CreateCompletionQueue(_maxOutstandingSends);

            _requestQueue = RioFunctions.CreateRequestQueue(socket, _receiveQueue, _sendQueue, _maxOutstandingSends);


            _handle = GCHandle.Alloc(this, GCHandleType.Normal);
            var address = GCHandle.ToIntPtr(_handle);

            _sendWait = WinThreadpool.CreateThreadpoolWait(_sendCallback, address);
            _receiveWait = WinThreadpool.CreateThreadpoolWait(_receiveCallback, address);

            _sendCommit = WinThreadpool.CreateThreadpoolWork(_commitCallback, address);

            _sendGate.Open();
        }

        public bool NoDelay
        {
            set => RioFunctions.SetTcpNodelay(_socket, value);
        }

        public void Shutdown(SocketShutdown how)
        {
            RioFunctions.Shutdown(_socket, how);
        }

        public void Dispose()
        {
            _socket.Dispose();
        }

        public Task SendAsync(ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                var segment = _bufferMapper.GetSegmentFromBuffer(buffer.First);
                _requestQueue.Send(ref segment);
            }
            else
            {
                return SendMultiAsync(buffer);
            }

            if ((++_outstandingSends) == _maxOutstandingSends)
            {
                _sendGate.Close();
                return AwaitSendGate();
            }

            PostSend();
            return TaskCache.CompletedTask;
        }

        private async Task AwaitSendGate()
        {
            PostSend();
            await _sendGate;
        }

        public async Task SendMultiAsync(ReadableBuffer buffer)
        {
            RioBufferSegment segment;
            var enumerator = buffer.GetEnumerator();

            if (enumerator.MoveNext())
            {
                var current = enumerator.Current;

                while (enumerator.MoveNext())
                {
                    var next = enumerator.Current;

                    segment = _bufferMapper.GetSegmentFromBuffer(current);
                    current = next;

                    if ((++_outstandingSends) < _maxOutstandingSends)
                    {
                        _requestQueue.QueueSend(ref segment);
                    }
                    else
                    {
                        _sendGate.Close();
                        _requestQueue.Send(ref segment);
                        await AwaitSendGate();
                    }
                }

                segment = _bufferMapper.GetSegmentFromBuffer(current);
                _requestQueue.Send(ref segment);
                if ((++_outstandingSends) == _maxOutstandingSends)
                {
                    _sendGate.Close();
                    await AwaitSendGate();
                }
                else
                {
                    PostSend();
                }
            }
        }

        private void OnSend()
        {
            var totalTransferred = 0;
            var totalCount = 0;
            //var isEnd = false;

            RioRequestResults rioResults;
            ref var results = ref rioResults;
            while (true)
            {
                var count = _sendQueue.Dequeue(ref results);
                if (count == 0)
                {
                    break;
                }

                totalCount += (int) count;

                for (var i = 0; i < count; i++)
                {
                    var bytesTransferred = (int) results[i].BytesTransferred;
                    if (bytesTransferred > 0)
                    {
                        totalTransferred += bytesTransferred;
                    }
                    else
                    {
                        //isEnd = true;
                    }
                }
            }

            if ((_outstandingSends -= totalCount) < _maxOutstandingSends)
            {
                _sendGate.Open();
            }
        }

        private void OnCommit()
        {
            if (Interlocked.CompareExchange(ref _pendingCommit, 0, 1) == 1)
            {
                _requestQueue.FlushSends();
            }
        }

        public AutoResetGate<ReceiveResult> ReceiveAsync(Buffer<byte> buffer)
        {
            var receiveBufferSeg = _bufferMapper.GetSegmentFromBuffer(buffer);
            _requestQueue.Receive(ref receiveBufferSeg);

            PostReceive();

            return _receiveGate;
        }

        private void OnRead()
        {
            var totalTransferred = 0;
            var isEnd = false;

            RioRequestResults rioResults;
            ref var results = ref rioResults;
            while (true)
            {
                var count = _receiveQueue.Dequeue(ref results);
                if (count == 0)
                {
                    break;
                }

                for (var i = 0; i < count; i++)
                {
                    var bytesTransferred = (int)results[i].BytesTransferred;
                    if (bytesTransferred > 0)
                    {
                        totalTransferred += bytesTransferred;
                    }
                    else
                    {
                        isEnd = true;
                    }
                }
            }

            _receiveGate.Open(new ReceiveResult
            {
                BytesReceived = totalTransferred,
                IsEnd = isEnd
            });
        }

        private void PostSend()
        {

            if (Interlocked.CompareExchange(ref _pendingCommit, 1, 0) == 0)
            {
                WinThreadpool.SubmitThreadpoolWork(_sendCommit);
            }
            _sendQueue.Notify();
            WinThreadpool.SetThreadpoolWait(_sendWait, _sendEvent);
        }

        private void PostReceive()
        {
            _receiveQueue.Notify();
            WinThreadpool.SetThreadpoolWait(_receiveWait, _receiveEvent);
        }
    }
    public struct ReceiveResult
    {
        public int BytesReceived;
        public bool IsEnd;
    }
}
