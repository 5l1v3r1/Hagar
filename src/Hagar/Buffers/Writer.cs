using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Hagar.Session;
using Hagar.Utilities;

namespace Hagar.Buffers
{
    public ref struct Writer<TBufferWriter> where TBufferWriter : IBufferWriter<byte>
    {
        private TBufferWriter output;
        private Span<byte> currentSpan;
        private int bufferPos;
        private int previousBuffersSize;

        public Writer(TBufferWriter output, SerializerSession session)
        {
            this.output = output;
            this.Session = session;
            this.currentSpan = output.GetSpan();
            this.bufferPos = default;
            this.previousBuffersSize = default;
        }

        public SerializerSession Session { get; }

        public TBufferWriter Output => this.output;
        
        public int Position => this.previousBuffersSize + this.bufferPos;
        
        public Span<byte> WritableSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.currentSpan.Slice(bufferPos);
        }

        /// <summary>
        /// Advance the write position in the current span.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AdvanceSpan(int length) => this.bufferPos += length;

        /// <summary>
        /// Commit the currently written buffers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Commit()
        {
            this.output.Advance(this.bufferPos);
            this.previousBuffersSize += this.bufferPos;
            this.currentSpan = default;
            this.bufferPos = default;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureContiguous(int length)
        {
            // The current buffer is adequate.
            if (this.bufferPos + length < this.currentSpan.Length) return;

            // The current buffer is inadequate, allocate another.
            this.Allocate(length);
#if DEBUG
            // Throw if the allocation does not satisfy the request.
            if (this.currentSpan.Length < length) ThrowTooLarge(length);
            
            void ThrowTooLarge(int l) => throw new InvalidOperationException($"Requested buffer length {l} cannot be satisfied by the writer.");
#endif
        }

        public void Allocate(int length)
        {
            // Commit the bytes which have been written.
            this.output.Advance(this.bufferPos);

            // Request a new buffer with at least the requested number of available bytes.
            this.currentSpan = this.output.GetSpan(length);

            // Update internal state for the new buffer.
            this.previousBuffersSize += this.bufferPos;
            this.bufferPos = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ReadOnlySpan<byte> value)
        {
            // Fast path, try copying to the current buffer.
            if (value.Length <= this.currentSpan.Length - this.bufferPos)
            {
                value.CopyTo(this.WritableSpan);
                this.bufferPos += value.Length;
            }
            else
            {
                WriteMultiSegment(in value);
            }
        }

        private void WriteMultiSegment(in ReadOnlySpan<byte> source)
        {
            var input = source;
            while (true)
            {
                // Write as much as possible/necessary into the current segment.
                var writeSize = Math.Min(this.currentSpan.Length - this.bufferPos, input.Length);
                input.Slice(0, writeSize).CopyTo(this.WritableSpan);
                this.bufferPos += writeSize;

                input = input.Slice(writeSize);

                if (input.Length == 0) return;
                
                // The current segment is full but there is more to write.
                this.Allocate(input.Length);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(byte value)
        {
            this.EnsureContiguous(1);
            currentSpan[this.bufferPos++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(sbyte value)
        {
            this.EnsureContiguous(1);
            currentSpan[this.bufferPos++] = (byte)value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(short value)
        {
            const int width = 2;
            this.EnsureContiguous(width);
            BinaryPrimitives.WriteInt16LittleEndian(this.WritableSpan, value);
            this.bufferPos += width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int value)
        {
            const int width = 4;
            this.EnsureContiguous(width);
            BinaryPrimitives.WriteInt32LittleEndian(this.WritableSpan, value);
            this.bufferPos += width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(long value)
        {
            const int width = 8;
            this.EnsureContiguous(width);
            BinaryPrimitives.WriteInt64LittleEndian(this.WritableSpan, value);
            this.bufferPos += width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(uint value)
        {
            const int width = 4;
            this.EnsureContiguous(width);
            BinaryPrimitives.WriteUInt32LittleEndian(this.WritableSpan, value);
            this.bufferPos += width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ushort value)
        {
            const int width = 2;
            this.EnsureContiguous(width);
            BinaryPrimitives.WriteUInt16LittleEndian(this.WritableSpan, value);
            this.bufferPos += width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ulong value)
        {
            const int width = 8;
            this.EnsureContiguous(width);
            BinaryPrimitives.WriteUInt64LittleEndian(this.WritableSpan, value);
            this.bufferPos += width;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt(uint value)
        {
            var numBytes = PrefixVarIntHelpers.CountRequiredBytes(value);

            var span = this.currentSpan;
            var index = this.bufferPos;

            // Check that the current buffer is adequate.
            // JIT: performing this check here should allow for bounds check elision below.
            if (index + numBytes >= span.Length)
            {
                this.WriteVarIntSlow(value, numBytes);
                return;
            }
            
            switch (numBytes)
            {
                case 1:
                    span[index] = (byte)value;
                    break;
                case 2:
                    span[index] = (byte)(value >> 8 | 0b10000000);
                    span[index + 1] = (byte)value;
                    break;
                case 3:
                    span[index] = (byte)(value >> 16 | 0b11000000);
                    span[index + 1] = (byte)(value >> 8);
                    span[index + 2] = (byte)value;
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32BigEndian(span, value | 0b11100000);
                    break;
                case 5:
                    span[index] = 0b11110000;
                    BinaryPrimitives.WriteUInt32BigEndian(span.Slice(index + 1), value);
                    break;
            }

            this.bufferPos += numBytes;
        }

        public void WriteVarIntSlow(uint value, int numBytes)
        {
            var span = this.currentSpan;
            var index = this.bufferPos;

            // The current buffer is adequate.
            if (index + numBytes >= span.Length) this.Allocate(numBytes);

            // The current buffer is inadequate, allocate another.
            switch (numBytes)
            {
                case 1:
                    span[index] = (byte)value;
                    break;
                case 2:
                    span[index] = (byte)(value >> 8 | 0b10000000);
                    span[index + 1] = (byte)value;
                    break;
                case 3:
                    span[index] = (byte)(value >> 16 | 0b11000000);
                    span[index + 1] = (byte)(value >> 8);
                    span[index + 2] = (byte)value;
                    break;
                case 4:
                    span[index] = (byte)(value >> 24 | 0b11100000);
                    span[index + 1] = (byte)(value >> 16);
                    span[index + 2] = (byte)(value >> 8);
                    span[index + 3] = (byte)value;
                    break;
                case 5:
                    span[index] = 0b11110000;
                    BinaryPrimitives.WriteUInt32BigEndian(span.Slice(index + 1), value);
                    break;
            }

            this.bufferPos += numBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt(ulong value)
        {
            var numBytes = PrefixVarIntHelpers.CountRequiredBytes(value);
            this.EnsureContiguous(numBytes + sizeof(uint));
            var shunt = PrefixVarIntHelpers.WriteShuntForNineByteValues(value);
            BinaryPrimitives.WriteUInt64BigEndian(this.currentSpan.Slice(this.bufferPos + shunt), value << ((8 + shunt - numBytes) * 8));

            this.currentSpan[this.bufferPos] |= PrefixVarIntHelpers.GetPrefix(numBytes);
            this.bufferPos += numBytes;
        }
    }
}