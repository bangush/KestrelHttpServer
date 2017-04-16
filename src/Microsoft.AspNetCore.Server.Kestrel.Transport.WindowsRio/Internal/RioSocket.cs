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
        private readonly static WaitCallback _dataCallback = (instance, context, wait, waitResult) =>
        {
            ((RioSocket)GCHandle.FromIntPtr(context).Target).OnData();
        };

        private readonly BufferMapper _bufferMapper;

        private RioConnectedSocket _socket;

        private ManualResetGate _sendGate = new ManualResetGate();

        private Event _dataEvent;

        private RioCompletionQueue _dataQueue;

        private RioRequestQueue _requestQueue;

        private GCHandle _handle;

        private WinThreadpoolWait _dataWait;

        private AutoResetGate<ReceiveResult> _receiveGate = new AutoResetGate<ReceiveResult>();
        private RioRequestResults _rioResults;

        private const int _maxOutstandingSends = 20;
        private const int _maxOutstandingReceives = 1;
        private int _outstandingSends;

        public IPEndPoint RemoteEndPoint { get; }
        public IPEndPoint LocalEndPoint { get; }

        public RioSocket(RioConnectedSocket socket, BufferMapper bufferMapper)
        {
            _socket = socket;
            _bufferMapper = bufferMapper;

            RemoteEndPoint = RioFunctions.GetPeerIPEndPoint(_socket);
            LocalEndPoint = RioFunctions.GetSockIPEndPoint(_socket);

            _dataEvent = Event.Create();
            _dataQueue = _dataEvent.CreateCompletionQueue(_maxOutstandingSends + _maxOutstandingReceives);

            _requestQueue = RioFunctions.CreateRequestQueue(socket, _dataQueue, _dataQueue, _maxOutstandingSends);


            _handle = GCHandle.Alloc(this, GCHandleType.Normal);
            var address = GCHandle.ToIntPtr(_handle);

            _dataWait = WinThreadpool.CreateThreadpoolWait(_dataCallback, address);

            _sendGate.Open();
            Post();
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

            return TaskCache.CompletedTask;
        }

        private async Task AwaitSendGate()
        {
            await _sendGate;
        }

        public async Task SendMultiAsync(ReadableBuffer buffer)
        {
            var enumerator = buffer.GetEnumerator();
            if (enumerator.MoveNext())
            {
                var current = enumerator.Current;

                RioBufferSegment segment;
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
            }
        }

        public AutoResetGate<ReceiveResult> ReceiveAsync(Buffer<byte> buffer)
        {
            var receiveBufferSeg = _bufferMapper.GetSegmentFromBuffer(buffer);
            _requestQueue.Receive(ref receiveBufferSeg);

            //Post();

            return _receiveGate;
        }

        private void OnData()
        {
            int totalSends = 0;
            bool isEnd = false;
            bool wasData;

            do
            {
                isEnd &= GetData(out var sends, out wasData);
                totalSends += sends;
            } while (wasData);

            if (totalSends > 0)
            {
                _requestQueue.FlushSends();
            }

            if (!isEnd)
            {
                _dataQueue.Notify();
                WinThreadpool.SetThreadpoolWait(_dataWait, _dataEvent);
            }
        }

        private bool GetData(out int outstandingSends, out bool wasData)
        {
            var totalReceiveTransferred = 0;
            var totalReceiveCount = 0;
            var totalSendTransferred = 0;
            var totalSendCount = 0;
            var isEnd = false;
            wasData = false;

            ref var results = ref _rioResults;
            while (true)
            {
                var count = _dataQueue.Dequeue(ref results);
                if (count == 0)
                {
                    break;
                }


                for (var i = 0; i < count; i++)
                {
                    ref var result = ref results[i];

                    if (result.RequestCorrelation > 0)
                    {
                        // Receive
                        totalReceiveCount++;
                        var bytesTransferred = (int) result.BytesTransferred;
                        if (bytesTransferred > 0)
                        {
                            totalReceiveTransferred += bytesTransferred;
                        }
                        else
                        {
                            isEnd = true;
                        }
                    }
                    else
                    {
                        totalSendCount++;
                        // Send
                        var bytesTransferred = (int) result.BytesTransferred;
                        if (bytesTransferred > 0)
                        {
                            totalSendTransferred += bytesTransferred;
                        }
                        else
                        {
                            //isEnd = true;
                        }
                    }
                }
            }

            outstandingSends = (_outstandingSends -= totalSendCount);

            if (totalSendCount > 0 && outstandingSends < _maxOutstandingSends)
            {
                wasData = true;
                _sendGate.Open();
            }

            if (totalReceiveCount > 0)
            {
                wasData = true;
                _receiveGate.Open(new ReceiveResult
                {
                    BytesReceived = totalReceiveTransferred,
                    IsEnd = isEnd
                });
            }

            return isEnd;
        }

        private void Post()
        {
            _dataQueue.Notify();
            WinThreadpool.SetThreadpoolWait(_dataWait, _dataEvent);
        }
    }
    public struct ReceiveResult
    {
        public int BytesReceived;
        public bool IsEnd;
    }
}
