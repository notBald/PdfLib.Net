using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PdfLib.Util
{
    /// <summary>
    /// For reading induvidual bits out of a stream.
    /// </summary>
    /// <remarks>
    /// BitStream supports reading chunks of 24 bits at a time.
    /// The reason 24 bits is the limit is simply because the
    /// buffer is 32 bits long, and a minimum of 8 bits are read
    /// a time. I.e. if there's 1 bit in the buffer, and you need
    /// 32 bits, a total of 33 bits needs to be buffered.
    /// </remarks>
    public class BitStream
    {
        #region Variables and properties

        Stream _str;

        /// <summary>
        /// A buffer containing up to 32 bits
        /// </summary>
        internal uint bit_buffer = 0;

        /// <summary>
        /// Number of bits in the buffer
        /// </summary>
        internal int n_buf_bits = 0;

        /// <summary>
        /// Maximum number of bits this reader can handle in one go
        /// </summary>
        public const int MAX = 24;

        /// <summary>
        /// Number of bits read from the stream. (Can be negative of position is manipulated with reseting)
        /// </summary>
        public long BitsRead { get { return _str.Position * 8 - n_buf_bits; } }

        /// <summary>
        /// Sets the position of the underlying stream and clears the buffer
        /// </summary>
        public long StreamPosition
        {
            get
            {
                return _str.Position;
            }
            set
            {
                _str.Position = value;
                n_buf_bits = 0;
                bit_buffer = 0;
            }
        }

        #endregion

        #region Init

        public BitStream(byte[] data)
            : this(new MemoryStream(data)) 
        { }

        public BitStream(Stream str)
        {
            _str = str;
        }

        public void Reset()
        {
            _str.Position = 0;
            n_buf_bits = 0;

            //Must be cleared as this impl. clears this buffer
            //as data is read out, and ORs new data into it
            bit_buffer = 0;
        }

        #endregion

        /// <summary>
        /// Aligns the stream to the next byte.
        /// </summary>
        public void ByteAlign()
        {
            if (n_buf_bits == 0) return;

            //Number of whole bytes in the buffer
            int bytes = n_buf_bits / 8;

            //Number of bits in a partial byte
            int shift = n_buf_bits - bytes * 8;

            //Removes the partial byte.
            ClearBits(shift);
        }

        /// <summary>
        /// Looks at n bits. Always call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 24</param>
        /// <returns>The bits in the lower end of the int.</returns>
        public int PeekBits(int n)
        {
            return unchecked((int)(bit_buffer >> (32 - n)));
        }

        /// <summary>
        /// Gets n bits. Always call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 24</param>
        /// <returns>The bits in the lower end of the int.</returns>
        [DebuggerStepThrough]
        public int GetBits(int n)
        {
            var ret = unchecked((int)(bit_buffer >> (32 - n)));
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
            return ret;
        }

        /// <summary>
        /// Fetches n bits. No need to call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 24</param>
        /// <returns>The bits in the lower end of the int.</returns>
        //[DebuggerStepThrough]
        public int FetchBits(int n)
        {
            if (!HasBits(n)) throw new EndOfStreamException();
            var ret = unchecked((int)(bit_buffer >> (32 - n)));
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
            return ret;
        }

        /// <summary>
        /// Removes n bits from the buffer. Always call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to remove, max is 24.</param>
        public void ClearBits(int n)
        {
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
        }

        /// <summary>
        /// Tests if there is enough bits and fills the bit buffer if needed.
        /// </summary>
        /// <param name="n">How many bits are needed. Must be 24 or less</param>
        public bool HasBits(int n)
        {
            if (n <= n_buf_bits)
                return true;

            //How many bytes of avalible space is in the buffer
            int to_fill = (32 - n_buf_bits);
            int to_offset = to_fill % 8;
            to_fill /= 8;

            //Fills the buffer.
            uint tmp_buffer = 0; 
            for (int c = 0; c < to_fill; c++ )
            {
                int a_byte = _str.ReadByte();
                if (a_byte == -1) 
                {
                    tmp_buffer = tmp_buffer << (8 * (to_fill - c));
                    bit_buffer |= (tmp_buffer << to_offset);
                    return (n <= n_buf_bits);
                }
                tmp_buffer = tmp_buffer << 8 | (uint) a_byte;
                n_buf_bits += 8;
            }
            bit_buffer |= (tmp_buffer << to_offset);

            return true;
        }

        /* Copilot sugested version:
        public bool HasBits(int n)
        {
            if (n <= n_buf_bits)
                return true;

            while (n_buf_bits < n)
            {
                int a_byte = _str.ReadByte();
                if (a_byte == -1)
                {
                    // If we reach the end of the stream, pad the buffer with zeros
                    bit_buffer <<= (n - n_buf_bits);
                    n_buf_bits = n;
                    return false;
                }

                bit_buffer = (bit_buffer << 8) | (uint)a_byte;
                n_buf_bits += 8;
            }

            return true;
        }*/


        public void Skip(int n)
        {
            while (n_buf_bits > 0 && n > 0)
            {
                ClearBits(1);
                n--;
            }
            while (n >= 8)
            {
                _str.ReadByte();
                n -= 8;
            }
            while (n > 0)
            {
                ClearBits(1);
                n--;
            }
        }
    }

    /// <summary>
    /// For reading induvidual bits out of a stream.
    /// </summary>
    /// <remarks>
    /// Similar to the normal bitstream, but buffers up
    /// to 64 bits (i.e. you can read out up to 56 bits)
    /// </remarks>
    public class BitStream64
    {
        #region Variables and properties

        Stream _str;

        /// <summary>
        /// Maximum number of bits this reader can handle in one go
        /// </summary>
        public const int MAX = 56;

        /// <summary>
        /// A buffer containing up to 64 bits
        /// </summary>
        internal ulong bit_buffer = 0;

        /// <summary>
        /// Number of bits in the buffer
        /// </summary>
        internal int n_buf_bits = 0;

        #endregion

        #region Init

        public BitStream64(byte[] data)
            : this(new MemoryStream(data))
        { }

        public BitStream64(Stream str)
        {
            _str = str;
        }

        public void Reset()
        {
            _str.Position = 0;
            n_buf_bits = 0;

            //Must be cleared as this impl. clears this buffer
            //as data is read out, and ORs new data into it
            bit_buffer = 0;
        }

        public void Reset(int new_pos)
        {
            _str.Position = new_pos;
            n_buf_bits = 0;

            //Must be cleared as this impl. clears this buffer
            //as data is read out, and ORs new data into it
            bit_buffer = 0;
        }

        #endregion

        /// <summary>
        /// Aligns the stream to the next byte.
        /// </summary>
        public void ByteAlign()
        {
            if (n_buf_bits == 0) return;

            //Number of bits in a partial byte
            int shift = n_buf_bits % 8;

            //Removes the partial byte.
            ClearBits(shift);
        }

        /// <summary>
        /// Looks at n bits. Always call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 32</param>
        /// <returns>The bits in the lower end of the int.</returns>
        public ulong PeekBits(int n)
        {
            return (bit_buffer >> (64 - n));
        }

        /// <summary>
        /// Gets n bits. Always call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 32</param>
        /// <returns>The bits in the lower end of the int.</returns>
        [DebuggerStepThrough]
        public ulong GetBits(int n)
        {
            var ret =(bit_buffer >> (64 - n));
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
            return ret;
        }

        /// <summary>
        /// Gets n bits. Always call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 32</param>
        /// <returns>The bits in the lower end of the int.</returns>
        [DebuggerStepThrough]
        public uint GetBitsU(int n)
        {
            var ret = (uint) (bit_buffer >> (64 - n));
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
            return ret;
        }

        /// <summary>
        /// Fetches n bits. No need to call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 32</param>
        /// <returns>The bits in the lower end of the int.</returns>
        [DebuggerStepThrough]
        public ulong FetchBits(int n)
        {
            if (!HasBits(n)) throw new EndOfStreamException();
            var ret = (bit_buffer >> (64 - n));
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
            return ret;
        }

        /// <summary>
        /// Fetches n bits. No need to call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to fetch, max is 32</param>
        /// <returns>The bits in the lower end of the int.</returns>
        //[DebuggerStepThrough]
        public uint FetchBitsU(int n)
        {
            if (!HasBits(n)) throw new EndOfStreamException();
            var ret = (uint) (bit_buffer >> (64 - n));
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
            return ret;
        }

        /// <summary>
        /// Removes n bits from the buffer. Always call HasBits first
        /// </summary>
        /// <param name="n">Number of bits to remove, max is 56.</param>
        public void ClearBits(int n)
        {
            bit_buffer = bit_buffer << n;
            n_buf_bits -= n;
        }

        /// <summary>
        /// Tests if there is enough bits and fills the bit buffer if needed.
        /// </summary>
        /// <param name="n">How many bits are needed. Must be 56 or less</param>
        public bool HasBits(int n)
        {
            if (n <= n_buf_bits)
                return true;

            //How many bytes of avalible space is in the buffer
            int to_fill = (64 - n_buf_bits);
            int to_offset = to_fill % 8;
            to_fill /= 8;

            //Fills the buffer.
            ulong tmp_buffer = 0;
            for (int c = 0; c < to_fill; c++)
            {
                int a_byte = _str.ReadByte();
                if (a_byte == -1)
                {
                    tmp_buffer = tmp_buffer << (8 * (to_fill - c));
                    bit_buffer |= (tmp_buffer << to_offset);
                    return (n <= n_buf_bits);
                }
                tmp_buffer = tmp_buffer << 8 | (uint)a_byte;
                n_buf_bits += 8;
            }
            bit_buffer |= (tmp_buffer << to_offset);

            return true;
        }

        public void Skip(int n)
        {
            while (n_buf_bits > 0 && n > 0)
            {
                ClearBits(1);
                n--;
            }
            while (n >= 8)
            {
                _str.ReadByte();
                n -= 8;
            }
            while (n > 0)
            {
                ClearBits(1);
                n--;
            }
        }
    }

    /// <summary>
    /// Utility class for writing a stream of bits
    /// </summary>
    public class BitWriter : IDisposable
    {
        #region Variables and properties

        readonly Stream _stream;

        /// <summary>
        /// This buffer can contain up to 7 bits
        /// </summary>
        int _buffer = 0;

        /// <summary>
        /// How many bits are in the buffer
        /// </summary>
        int _n_buff_bits = 0;

        /// <summary>
        /// Gets the position, or sets the position (but remeber to flush first)
        /// </summary>
        public long Position
        {
            get { return _stream.Position; }
            set { Debug.Assert(_n_buff_bits == 0, "Did you remeber to flush?"); _stream.Position = value; }
        }

        #endregion

        #region Init

        public BitWriter(Stream stream)
        { _stream = stream; }
        public BitWriter(byte[] buffer)
            : this(new MemoryStream(buffer))
        { }
        public BitWriter(int capacity)
            : this(new MemoryStream(capacity))
        { }

        public void Dispose()
        {
            Flush();
        }

        public void Flush()
        {
            if (_n_buff_bits > 0)
            {
                _stream.WriteByte((byte)(_buffer << (8 - _n_buff_bits)));
                _n_buff_bits = 0;
                _buffer = 0;
            }
        }

        public void Reset()
        {
            Flush();
            _stream.Position = 0;
        }

        #endregion

        public void Write(int value, int n_bits)
        {
            while (_n_buff_bits > 0 && n_bits > 0)
                WriteBit((value >> --n_bits) & 0x1);
            while (n_bits >= 8)
            {
                n_bits -= 8;
                _stream.WriteByte((byte) ((value >> n_bits) & 0xFF));
            }
            while (n_bits > 0)
                WriteBit((value >> --n_bits) & 0x1);
        }

        public void Write(ulong value, int n_bits)
        {
            while (_n_buff_bits > 0 && n_bits > 0)
                WriteBit((int) ((value >> --n_bits) & 0x1));
            while (n_bits >= 8)
            {
                n_bits -= 8;
                _stream.WriteByte((byte)((value >> n_bits) & 0xFF));
            }
            while (n_bits > 0)
                WriteBit((int) ((value >> --n_bits) & 0x1));
        }

        public void WriteBit(int value)
        {
            _buffer = (_buffer << 1) | (value & 0x1);
            if (_n_buff_bits < 7)
                _n_buff_bits++;
            else
            {
                _stream.WriteByte((byte)_buffer);
                _n_buff_bits = 0;
                _buffer = 0;
            }
        }

        public void Skip(int n_bits)
        {
            while (_n_buff_bits > 0 && n_bits > 0)
            {
                WriteBit(0);
                n_bits--;
            }
            while (n_bits >= 8)
            {
                n_bits -= 8;
                _stream.WriteByte(0);
            }
            while (n_bits > 0)
            {
                WriteBit(0);
                n_bits--;
            }
        }

        /// <summary>
        /// Alings to byte boundary and effecivly writes out all buffered data
        /// </summary>
        public void Align()
        {
            if (_n_buff_bits != 0)
            {
                _stream.WriteByte((byte)(_buffer << (8 - _n_buff_bits)));
                _n_buff_bits = 0;
            }
        }

        /// <summary>
        /// Aligns and moves to the new offset from the
        /// current position
        /// </summary>
        public void Seek(long offset)
        {
            Align();
            _stream.Seek(offset, SeekOrigin.Current);
        }

        public byte[] ToArray()
        {
            Flush();
            if (_stream is MemoryStream)
                return ((MemoryStream)_stream).ToArray();
            throw new NotSupportedException();
        }
    }
}
