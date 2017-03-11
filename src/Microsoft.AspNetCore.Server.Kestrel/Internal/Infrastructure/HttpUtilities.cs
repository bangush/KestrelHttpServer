// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure
{
    public static class HttpUtilities
    {
        public const string Http10Version = "HTTP/1.0";
        public const string Http11Version = "HTTP/1.1";

        public const string HttpUriScheme = "http://";
        public const string HttpsUriScheme = "https://";

        // readonly primitive statics can be Jit'd to consts https://github.com/dotnet/coreclr/issues/1079

        private readonly static ulong _httpSchemeLong = GetAsciiStringAsLong(HttpUriScheme + "\0");
        private readonly static ulong _httpsSchemeLong = GetAsciiStringAsLong(HttpsUriScheme);
        private readonly static ulong _httpConnectMethodLong = GetAsciiStringAsLong("CONNECT ");
        private readonly static ulong _httpDeleteMethodLong = GetAsciiStringAsLong("DELETE \0");
        private const uint _httpGetMethodInt = 542393671; // retun of GetAsciiStringAsInt("GET "); const results in better codegen
        private readonly static ulong _httpHeadMethodLong = GetAsciiStringAsLong("HEAD \0\0\0");
        private readonly static ulong _httpPatchMethodLong = GetAsciiStringAsLong("PATCH \0\0");
        private readonly static ulong _httpPostMethodLong = GetAsciiStringAsLong("POST \0\0\0");
        private readonly static ulong _httpPutMethodLong = GetAsciiStringAsLong("PUT \0\0\0\0");
        private readonly static ulong _httpOptionsMethodLong = GetAsciiStringAsLong("OPTIONS ");
        private readonly static ulong _httpTraceMethodLong = GetAsciiStringAsLong("TRACE \0\0");

        private const ulong _http10VersionLong = 3471766442030158920; // GetAsciiStringAsLong("HTTP/1.0"); const results in better codegen
        private const ulong _http11VersionLong = 3543824036068086856; // GetAsciiStringAsLong("HTTP/1.1"); const results in better codegen

        private readonly static ulong _mask8Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff });
        private readonly static ulong _mask7Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 });
        private readonly static ulong _mask6Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00 });
        private readonly static ulong _mask5Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00 });
        private readonly static ulong _mask4Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00 });

        private readonly static Tuple<ulong, ulong, HttpMethod, int>[] _knownMethods = new Tuple<ulong, ulong, HttpMethod, int>[8];

        private readonly static string[] _methodNames = new string[9];

        static HttpUtilities()
        {
            _knownMethods[0] = Tuple.Create(_mask4Chars, _httpPutMethodLong, HttpMethod.Put, 3);
            _knownMethods[1] = Tuple.Create(_mask5Chars, _httpPostMethodLong, HttpMethod.Post, 4);
            _knownMethods[2] = Tuple.Create(_mask5Chars, _httpHeadMethodLong, HttpMethod.Head, 4);
            _knownMethods[3] = Tuple.Create(_mask6Chars, _httpTraceMethodLong, HttpMethod.Trace, 5);
            _knownMethods[4] = Tuple.Create(_mask6Chars, _httpPatchMethodLong, HttpMethod.Patch, 5);
            _knownMethods[5] = Tuple.Create(_mask7Chars, _httpDeleteMethodLong, HttpMethod.Delete, 6);
            _knownMethods[6] = Tuple.Create(_mask8Chars, _httpConnectMethodLong, HttpMethod.Connect, 7);
            _knownMethods[7] = Tuple.Create(_mask8Chars, _httpOptionsMethodLong, HttpMethod.Options, 7);
            _methodNames[(byte)HttpMethod.Get] = HttpMethods.Get;
            _methodNames[(byte)HttpMethod.Put] = HttpMethods.Put;
            _methodNames[(byte)HttpMethod.Delete] = HttpMethods.Delete;
            _methodNames[(byte)HttpMethod.Post] = HttpMethods.Post;
            _methodNames[(byte)HttpMethod.Head] = HttpMethods.Head;
            _methodNames[(byte)HttpMethod.Trace] = HttpMethods.Trace;
            _methodNames[(byte)HttpMethod.Patch] = HttpMethods.Patch;
            _methodNames[(byte)HttpMethod.Connect] = HttpMethods.Connect;
            _methodNames[(byte)HttpMethod.Options] = HttpMethods.Options;
        }

        private unsafe static ulong GetAsciiStringAsLong(string str)
        {
            Debug.Assert(str.Length == 8, "String must be exactly 8 (ASCII) characters long.");

            var bytes = Encoding.ASCII.GetBytes(str);

            fixed (byte* ptr = &bytes[0])
            {
                return *(ulong*)ptr;
            }
        }

        private unsafe static uint GetAsciiStringAsInt(string str)
        {
            Debug.Assert(str.Length == 4, "String must be exactly 4 (ASCII) characters long.");

            var bytes = Encoding.ASCII.GetBytes(str);

            fixed (byte* ptr = &bytes[0])
            {
                return *(uint*)ptr;
            }
        }

        private unsafe static ulong GetMaskAsLong(byte[] bytes)
        {
            Debug.Assert(bytes.Length == 8, "Mask must be exactly 8 bytes long.");

            fixed (byte* ptr = bytes)
            {
                return *(ulong*)ptr;
            }
        }

        public unsafe static string GetAsciiStringNonNullCharacters(this Span<byte> span)
        {
            var length = span.Length;
            if (length == 0)
            {
                return string.Empty;
            }

            var asciiString = new string('\0', length);

            fixed (char* output = asciiString)
            fixed (byte* buffer = &span.DangerousGetPinnableReference())
            {
                // This version if AsciiUtilities returns null if there are any null (0 byte) characters
                // in the string
                if (!AsciiUtilities.TryGetAsciiString(buffer, output, length))
                {
                    throw new InvalidOperationException();
                }
            }
            return asciiString;
        }

        public static string GetAsciiStringEscaped(this Span<byte> span, int maxChars)
        {
            var sb = new StringBuilder();

            int i;
            int length = Math.Min(span.Length, maxChars);
            for (i = 0; i < length; i++)
            {
                var ch = span[i];
                if (ch < 0x20 || ch >= 0x7F)
                {
                    sb.Append($"\\x{ch:X2}");
                }
                else
                {
                    sb.Append((char)ch);
                }
            }

            if (span.Length > maxChars)
            {
                sb.Append("...");
            }
            return sb.ToString();
        }

        public static string GetRequestLineAsciiStringEscaped(this Span<byte> target, string method, string httpVersion, int maxChars)
        {
            // Reconstruct line for detailed exception
            var sb = new StringBuilder();

            int i = 0;

            if (method.Length < maxChars)
            {
                i += method.Length;
                sb.Append(method);
            }
            else
            {
                i += maxChars;
                sb.Append(method.Substring(0, maxChars));
            }

            if (i < maxChars)
            {
                i++;
                sb.Append(" ");
            }

            var methodLength = i;

            var length = Math.Min(target.Length, maxChars - methodLength);
            for (i = 0; i < length; i++)
            {
                var ch = target[i];
                if (ch < 0x20 || ch >= 0x7F)
                {
                    sb.Append($"\\x{ch:X2}");
                }
                else
                {
                    sb.Append((char)ch);
                }
            }

            i += methodLength;

            if (i < maxChars)
            {
                sb.Append(" ");
                i++;
            }

            if (i <= maxChars - httpVersion.Length)
            {
                sb.Append(httpVersion);
                i += httpVersion.Length;
            }
            else
            {
                length = maxChars - i;
                i += length;
                sb.Append(httpVersion.Substring(0, length));
            }

            if (i < maxChars)
            {
                i++;
                sb.Append(@"\x0D"); // CR
            }

            if (i < maxChars)
            {
                i++;
                sb.Append(@"\x0A"); // LF
            }

            if (method.Length + httpVersion.Length + target.Length + 1 + 1 + 2 > maxChars)
            {
                sb.Append("...");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Checks that up to 8 bytes from <paramref name="span"/> correspond to a known HTTP method.
        /// </summary>
        /// <remarks>
        /// A "known HTTP method" can be an HTTP method name defined in the HTTP/1.1 RFC.
        /// Since all of those fit in at most 8 bytes, they can be optimally looked up by reading those bytes as a long. Once
        /// in that format, it can be checked against the known method.
        /// The Known Methods (CONNECT, DELETE, GET, HEAD, PATCH, POST, PUT, OPTIONS, TRACE) are all less than 8 bytes
        /// and will be compared with the required space. A mask is used if the Known method is less than 8 bytes.
        /// To optimize performance the GET method will be checked first.
        /// </remarks>
        /// <returns><c>true</c> if the input matches a known string, <c>false</c> otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool GetKnownMethod(this Span<byte> span, out HttpMethod method, out int length)
        {
            fixed (byte* data = &span.DangerousGetPinnableReference())
            {
                method = GetKnownMethod(data, span.Length, out length);
                return method != HttpMethod.Custom;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe static HttpMethod GetKnownMethod(byte* data, int length, out int methodLength)
        {
            methodLength = 0;
            if (length < sizeof(uint))
            {
                return HttpMethod.Custom;
            }
            else if (*(uint*)data == _httpGetMethodInt)
            {
                methodLength = 3;
                return HttpMethod.Get;
            }
            else if (length < sizeof(ulong))
            {
                return HttpMethod.Custom;
            }
            else
            {
                var value = *(ulong*)data;
                foreach (var x in _knownMethods)
                {
                    if ((value & x.Item1) == x.Item2)
                    {
                        methodLength = x.Item4;
                        return x.Item3;
                    }
                }
            }

            return HttpMethod.Custom;
        }

        /// <summary>
        /// Checks 9 bytes from <paramref name="span"/>  correspond to a known HTTP version.
        /// </summary>
        /// <remarks>
        /// A "known HTTP version" Is is either HTTP/1.0 or HTTP/1.1.
        /// Since those fit in 8 bytes, they can be optimally looked up by reading those bytes as a long. Once
        /// in that format, it can be checked against the known versions.
        /// The Known versions will be checked with the required '\r'.
        /// To optimize performance the HTTP/1.1 will be checked first.
        /// </remarks>
        /// <returns><c>true</c> if the input matches a known string, <c>false</c> otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool GetKnownVersion(this Span<byte> span, out HttpVersion knownVersion, out byte length)
        {
            fixed (byte* data = &span.DangerousGetPinnableReference())
            {
                knownVersion = GetKnownVersion(data, span.Length);
                if (knownVersion != HttpVersion.Unknown)
                {
                    length = sizeof(ulong);
                    return true;
                }

                length = 0;
                return false;
            }
        }

        /// <summary>
        /// Checks 9 bytes from <paramref name="location"/>  correspond to a known HTTP version.
        /// </summary>
        /// <remarks>
        /// A "known HTTP version" Is is either HTTP/1.0 or HTTP/1.1.
        /// Since those fit in 8 bytes, they can be optimally looked up by reading those bytes as a long. Once
        /// in that format, it can be checked against the known versions.
        /// The Known versions will be checked with the required '\r'.
        /// To optimize performance the HTTP/1.1 will be checked first.
        /// </remarks>
        /// <returns><c>true</c> if the input matches a known string, <c>false</c> otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe static HttpVersion GetKnownVersion(byte* location, int length)
        {
            HttpVersion knownVersion;
            var version = *(ulong*)location;
            if (length < sizeof(ulong) + 1 || location[sizeof(ulong)] != (byte)'\r')
            {
                knownVersion = HttpVersion.Unknown;
            }
            else if (version == _http11VersionLong)
            {
                knownVersion = HttpVersion.Http11;
            }
            else if (version == _http10VersionLong)
            {
                knownVersion = HttpVersion.Http10;
            }
            else
            {
                knownVersion = HttpVersion.Unknown;
            }

            return knownVersion;
        }

        /// <summary>
        /// Checks 8 bytes from <paramref name="span"/> that correspond to 'http://' or 'https://'
        /// </summary>
        /// <param name="span">The span</param>
        /// <param name="knownScheme">A reference to the known scheme, if the input matches any</param>
        /// <returns>True when memory starts with known http or https schema</returns>
        public static bool GetKnownHttpScheme(this Span<byte> span, out HttpScheme knownScheme)
        {
            if (span.TryRead<ulong>(out var value))
            {
                if ((value & _mask7Chars) == _httpSchemeLong)
                {
                    knownScheme = HttpScheme.Http;
                    return true;
                }

                if (value == _httpsSchemeLong)
                {
                    knownScheme = HttpScheme.Https;
                    return true;
                }
            }

            knownScheme = HttpScheme.Unknown;
            return false;
        }

        /// <summary>
        /// Checks 8 bytes from <paramref name="span"/> that correspond to 'http://' or 'https://'
        /// </summary>
        /// <param name="span">The span</param>
        /// <returns>True when memory starts with known http or https schema</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool HasKnownHttpScheme(this Span<byte> span)
        {
            if (span.TryRead<ulong>(out var value) &&
                ((value & _mask7Chars) == _httpSchemeLong) || 
                 (value == _httpsSchemeLong))
            {
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string VersionToString(HttpVersion httpVersion)
        {
            Debug.Assert(httpVersion == HttpVersion.Http11 || httpVersion == HttpVersion.Http10);

            if (httpVersion == HttpVersion.Http11)
            {
                return Http11Version;
            }
            return Http10Version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string MethodToString(HttpMethod method)
        {
            // Pattern to avoid addtional range check since we are prechecking
            // https://github.com/dotnet/coreclr/pull/9773
            var methodNames = _methodNames;
            var methodIndex = (uint)method;
            if (methodIndex < methodNames.Length)
            {
                return methodNames[methodIndex];
            }
            return null;
        }

        public static string SchemeToString(HttpScheme scheme)
        {
            switch (scheme)
            {
                case HttpScheme.Http:
                    return HttpUriScheme;
                case HttpScheme.Https:
                    return HttpsUriScheme;
                default:
                    return null;
            }
        }
    }
}
