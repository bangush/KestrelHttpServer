﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public static class HttpApplicationConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseHttpApplication<TConnection>(this IConnectionBuilder builder) where TConnection : HttpConnection, new()
        {
            return builder.Use(next => new HttpApplication<TConnection>().ExecuteAsync);
        }
    }

    public class HttpApplication<TConnection> where TConnection : HttpConnection, new()
    {
        public Task ExecuteAsync(ConnectionContext connection)
        {
            var parser = new HttpParser<ParsingAdapter>();

            var httpConnection = new TConnection
            {
                Parser = parser,
                Reader = connection.Transport.Input,
                Writer = connection.Transport.Output
            };
            return httpConnection.ExecuteAsync();
        }
    }

    public class HttpConnection : IHttpHeadersHandler, IHttpRequestLineHandler
    {
        private State _state;

        public PipeReader Reader { get; set; }
        public PipeWriter Writer { get; set; }

        internal HttpParser<ParsingAdapter> Parser { get; set; }

        public virtual void OnHeader(Span<byte> name, Span<byte> value)
        {

        }

        public virtual void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {

        }

        public virtual ValueTask ProcessRequestAsync()
        {
            return default;
        }

        public virtual ValueTask OnReadCompletedAsync()
        {
            return default;
        }

        public async Task ExecuteAsync()
        {
            try
            {
                await ProcessRequestsAsync();

                Reader.Complete();
            }
            catch (Exception ex)
            {
                Reader.Complete(ex);
            }
            finally
            {
                Writer.Complete();
            }
        }

        private async Task ProcessRequestsAsync()
        {
            while (true)
            {
                var task = Reader.ReadAsync();

                if (!task.IsCompleted)
                {
                    // No more data in the input
                    await OnReadCompletedAsync();
                }

                var result = await task;
                if (!ParseHttpRequest(ref result))
                {
                    break;
                }

                if (_state == State.Body)
                {
                    await ProcessRequestAsync();

                    _state = State.StartLine;
                }
            }
        }

        // Should be `in` but ReadResult isn't readonly struct
        private bool ParseHttpRequest(ref ReadResult result)
        {
            var buffer = result.Buffer;
            var consumed = buffer.Start;
            var examined = buffer.End;

            if (!buffer.IsEmpty)
            {
                var parsingStartLine = _state == State.StartLine;
                if (parsingStartLine)
                {
                    if (Parser.ParseRequestLine(this, buffer, out consumed, out examined))
                    {
                        _state = State.Headers;
                    }
                }

                if (_state == State.Headers)
                {
                    if (Parser.ParseHeaders(this, parsingStartLine ? buffer.Slice(consumed) : buffer, out consumed, out examined, out int consumedBytes))
                    {
                        _state = State.Body;
                    }
                }

                if (_state != State.Body && result.IsCompleted)
                {
                    ThrowUnexpectedEndOfData();
                }
            }
            else if (result.IsCompleted)
            {
                return false;
            }

            Reader.AdvanceTo(consumed, examined);
            return true;
        }

        private static void ThrowUnexpectedEndOfData()
        {
            throw new InvalidOperationException("Unexpected end of data!");
        }

        private enum State
        {
            StartLine,
            Headers,
            Body
        }
    }

    public struct ParsingAdapter : IHttpRequestLineHandler, IHttpHeadersHandler
    {
        public HttpConnection RequestHandler;

        public ParsingAdapter(HttpConnection requestHandler)
            => RequestHandler = requestHandler;

        public static implicit operator ParsingAdapter(HttpConnection connection)
            => new ParsingAdapter(connection);

        public void OnHeader(Span<byte> name, Span<byte> value)
            => RequestHandler.OnHeader(name, value);

        public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
            => RequestHandler.OnStartLine(method, version, target, path, query, customMethod, pathEncoded);
    }
}
