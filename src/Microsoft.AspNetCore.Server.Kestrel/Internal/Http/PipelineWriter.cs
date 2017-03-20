// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class PipelineWriter
    {
        private const int _maxULongByteLength = 20;

        [ThreadStatic]
        private static byte[] _numericBytesScratch;

        WritableBuffer _buffer;
        Memory<byte> _memory;
        int _outstandingBytes;

        public void Initalize(WritableBuffer buffer)
        {
            _buffer = buffer;
            _memory = buffer.Memory;
        }

        public void Commit()
        {
            var outstandingBytes = _outstandingBytes;
            if (outstandingBytes > 0)
            {
                _buffer.Advance(outstandingBytes);
                _outstandingBytes = 0;
            }

            _buffer.Commit();
        }


        public WritableBufferAwaitable FlushAsync()
        {
            Commit();

            return _buffer.FlushAsync();
        }

        public void Write(ArraySegment<byte> source)
        {
            WriteFast(new ReadOnlySpan<byte>(source.Array, source.Offset, source.Count));
        }

        public void WriteFast(ReadOnlySpan<byte> source)
        {
            var outstandingBytes = _outstandingBytes;
            var dest = _memory.Span;
            var sourceLength = source.Length;
            var destLength = dest.Length - outstandingBytes;

            if (sourceLength <= destLength)
            {
                ref byte pSource = ref source.DangerousGetPinnableReference();
                ref byte pDest = ref dest.DangerousGetPinnableReference();
                Unsafe.CopyBlockUnaligned(ref pDest, ref Unsafe.AddByteOffset(ref pSource, (IntPtr)outstandingBytes), (uint)sourceLength);
                _outstandingBytes += sourceLength;
            }
            else if (destLength == 0)
            {
                WriteAlloc(source);
            }
            else
            {
                WriteMultiBuffer(source);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteAlloc(ReadOnlySpan<byte> source)
        {
            Alloc(_outstandingBytes);

            // Get the new span
            _memory = _buffer.Memory;
            var dest = _memory.Span;

            var sourceLength = source.Length;
            if (sourceLength <= dest.Length)
            {
                ref byte pSource = ref source.DangerousGetPinnableReference();
                ref byte pDest = ref dest.DangerousGetPinnableReference();
                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)sourceLength);
                _outstandingBytes = sourceLength;
            }
            else
            {
                WriteMultiBuffer(source);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteMultiBuffer(ReadOnlySpan<byte> source)
        {
            var outstandingBytes = _outstandingBytes;
            var remaining = source.Length;
            var offset = 0;

            ref byte pSource = ref source.DangerousGetPinnableReference();
            while (remaining > 0)
            {
                var writable = Math.Min(remaining, _memory.Length - outstandingBytes);

                if (writable == 0)
                {
                    Alloc(outstandingBytes);
                    outstandingBytes = 0;

                    _memory = _buffer.Memory;
                    continue;
                }

                remaining -= writable;
                ref byte pDest = ref _memory.Span.DangerousGetPinnableReference();
                Unsafe.CopyBlockUnaligned(ref Unsafe.AddByteOffset(ref pDest, (IntPtr)offset), ref Unsafe.AddByteOffset(ref pSource, (IntPtr)outstandingBytes), (uint)writable);

                offset += writable;
                outstandingBytes += writable;
            }

            _outstandingBytes = outstandingBytes;
        }

        public unsafe void WriteAscii(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            var outstandingBytes = _outstandingBytes;
            var dest = _memory.Span;
            var sourceLength = data.Length;
            var destLength = dest.Length - outstandingBytes;

            if (sourceLength <= destLength)
            {
                fixed (char* input = data)
                fixed (byte* output = &dest.DangerousGetPinnableReference())
                {
                    EncodeAsciiCharsToBytes(input, output, data.Length);
                }

                _outstandingBytes += sourceLength;
            }
            else if (destLength == 0)
            {
                WriteAsciiAlloc(data);
            }
            else
            {
                WriteAsciiMultiBuffer(data);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe void WriteAsciiAlloc(string data)
        {
            Alloc(_outstandingBytes);

            // Get the new span
            _memory = _buffer.Memory;
            var dest = _memory.Span;

            var sourceLength = data.Length;
            if (sourceLength <= dest.Length)
            {
                fixed (char* input = data)
                fixed (byte* output = &dest.DangerousGetPinnableReference())
                {
                    EncodeAsciiCharsToBytes(input, output, data.Length);
                }

                _outstandingBytes += sourceLength;
            }
            else
            {
                WriteAsciiMultiBuffer(data);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe void WriteAsciiMultiBuffer(string data)
        {
            var outstandingBytes = _outstandingBytes;
            var remaining = data.Length;

            fixed (char* input = data)
            {
                var inputSlice = input;

                while (remaining > 0)
                {
                    var writable = Math.Min(remaining, _memory.Length - outstandingBytes);

                    if (writable == 0)
                    {
                        Alloc(outstandingBytes);
                        outstandingBytes = 0;

                        _memory = _buffer.Memory;
                        continue;
                    }

                    fixed (byte* output = &_memory.Span.DangerousGetPinnableReference())
                    {
                        EncodeAsciiCharsToBytes(inputSlice, output + outstandingBytes, writable);
                    }

                    inputSlice += writable;
                    remaining -= writable;

                    outstandingBytes += writable;
                }
            }

            _outstandingBytes = outstandingBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteNumeric(ulong number)
        {
            if (_memory.Length - _outstandingBytes > 0)
            {
                var simpleWrite = TryNumericWriteSimple(number);

                if (!simpleWrite)
                {
                    WriteNumericMultiWrite(number);
                }
            }
            else
            {
                WriteNumericAlloc(number);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe void WriteNumericAlloc(ulong number)
        {
            Alloc(_outstandingBytes);

            var simpleWrite = TryNumericWriteSimple(number);

            if (!simpleWrite)
            {
                WriteNumericMultiWrite(number);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteNumericMultiWrite(ulong number)
        {
            const byte AsciiDigitStart = (byte)'0';

            var value = number;
            var position = _maxULongByteLength;
            var byteBuffer = NumericBytesScratch;
            do
            {
                // Consider using Math.DivRem() if available
                var quotient = value / 10;
                byteBuffer[--position] = (byte)(AsciiDigitStart + (value - quotient * 10)); // 0x30 = '0'
                value = quotient;
            }
            while (value != 0);

            var length = _maxULongByteLength - position;
            WriteFast(new ReadOnlySpan<byte>(byteBuffer, position, length));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe bool TryNumericWriteSimple(ulong number)
        {
            const byte AsciiDigitStart = (byte)'0';

            // Fast path, try copying to the available memory directly
            var bytesLeftInBlock = _memory.Length - _outstandingBytes;
            var simpleWrite = true;
            fixed (byte* output = &_memory.Span.DangerousGetPinnableReference())
            {
                var start = output + _outstandingBytes;
                if (number < 10 && bytesLeftInBlock >= 1)
                {
                    *(start) = (byte) (((uint) number) + AsciiDigitStart);
                    _outstandingBytes++;
                }
                else if (number < 100 && bytesLeftInBlock >= 2)
                {
                    var val = (uint) number;
                    var tens = (byte) ((val * 205u) >> 11); // div10, valid to 1028

                    *(start) = (byte) (tens + AsciiDigitStart);
                    *(start + 1) = (byte) (val - (tens * 10) + AsciiDigitStart);
                    _outstandingBytes += 2;
                }
                else if (number < 1000 && bytesLeftInBlock >= 3)
                {
                    var val = (uint) number;
                    var digit0 = (byte) ((val * 41u) >> 12); // div100, valid to 1098
                    var digits01 = (byte) ((val * 205u) >> 11); // div10, valid to 1028

                    *(start) = (byte) (digit0 + AsciiDigitStart);
                    *(start + 1) = (byte) (digits01 - (digit0 * 10) + AsciiDigitStart);
                    *(start + 2) = (byte) (val - (digits01 * 10) + AsciiDigitStart);
                    _outstandingBytes += 3;
                }
                else
                {
                    simpleWrite = false;
                }
            }

            return simpleWrite;
        }

        private unsafe static void EncodeAsciiCharsToBytes(char* input, byte* output, int length)
        {
            // Note: Not BIGENDIAN or check for non-ascii
            const int Shift16Shift24 = (1 << 16) | (1 << 24);
            const int Shift8Identity = (1 << 8) | (1);

            // Encode as bytes upto the first non-ASCII byte and return count encoded
            int i = 0;
            // Use Intrinsic switch
            if (IntPtr.Size == 8) // 64 bit
            {
                if (length < 4) goto trailing;

                int unaligned = (int)(((ulong)input) & 0x7) >> 1;
                // Unaligned chars
                for (; i < unaligned; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }

                // Aligned
                int ulongDoubleCount = (length - i) & ~0x7;
                for (; i < ulongDoubleCount; i += 8)
                {
                    ulong inputUlong0 = *(ulong*)(input + i);
                    ulong inputUlong1 = *(ulong*)(input + i + 4);
                    // Pack 16 ASCII chars into 16 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong0 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong0 * Shift8Identity) >> 24) & 0xffff0000);
                    *(uint*)(output + i + 4) =
                        ((uint)((inputUlong1 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong1 * Shift8Identity) >> 24) & 0xffff0000);
                }
                if (length - 4 > i)
                {
                    ulong inputUlong = *(ulong*)(input + i);
                    // Pack 8 ASCII chars into 8 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong * Shift8Identity) >> 24) & 0xffff0000);
                    i += 4;
                }

                trailing:
                for (; i < length; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }
            }
            else // 32 bit
            {
                // Unaligned chars
                if ((unchecked((int)input) & 0x2) != 0)
                {
                    char ch = *input;
                    i = 1;
                    *(output) = (byte)ch; // Cast convert
                }

                // Aligned
                int uintCount = (length - i) & ~0x3;
                for (; i < uintCount; i += 4)
                {
                    uint inputUint0 = *(uint*)(input + i);
                    uint inputUint1 = *(uint*)(input + i + 2);
                    // Pack 4 ASCII chars into 4 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint0 | (inputUint0 >> 8));
                    *(ushort*)(output + i + 2) = (ushort)(inputUint1 | (inputUint1 >> 8));
                }
                if (length - 1 > i)
                {
                    uint inputUint = *(uint*)(input + i);
                    // Pack 2 ASCII chars into 2 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint | (inputUint >> 8));
                    i += 2;
                }

                if (i < length)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                    i = length;
                }
            }
        }

        private void Alloc(int outstandingBytes)
        {
            if (outstandingBytes > 0)
            {
                _outstandingBytes = 0;
                _buffer.Advance(outstandingBytes);
            }

            _buffer.Ensure();
        }

        private static byte[] NumericBytesScratch => _numericBytesScratch ?? CreateNumericBytesScratch();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte[] CreateNumericBytesScratch()
        {
            var bytes = new byte[_maxULongByteLength];
            _numericBytesScratch = bytes;
            return bytes;
        }
    }
}
