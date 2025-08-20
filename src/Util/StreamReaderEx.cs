using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib.Util
{
    /// <summary>
    /// Reads ints, shorts, etc.
    /// </summary>
    /// <remarks>
    /// Throws EndOfStreamException at end of stream
    /// 
    /// Somewhat similar to "System.IO.BinaryReader",
    /// but supports big endian. 
    /// </remarks>
    public class StreamReaderEx : IDisposable
    {
        /// <summary>
        /// Endianess
        /// </summary>
        readonly bool _big;

        /// <summary>
        /// Source
        /// </summary>
        readonly Stream _in;

        /// <summary>
        /// Read buffer. Only used during reads, nothing is buffered between reads. 
        /// </summary>
        byte[] bytes = new byte[4];

        /// <summary>
        /// For encoding characters
        /// </summary>
        UnicodeEncoding _char_encoder;

        public StreamReaderEx(byte[] inn)
            : this(new MemoryStream(inn), true) { }

        public StreamReaderEx(Stream inn)
            : this(inn, true) { }

        public StreamReaderEx(Stream inn, bool big_endian)
        {
            _in = inn;
            _big = big_endian;
            _char_encoder = new UnicodeEncoding(_big, false);
        }

        /// <summary>
        /// Closes the underlying stream
        /// </summary>
        public void Dispose() { _in.Dispose(); }

        public long Length { get { return _in.Length; } }
        public long Position 
        { 
            get { return _in.Position; } 
            set { _in.Position = value; } 
        }
        public bool CanRead { get { return _in.CanRead; } }
        public bool CanSeek { get { return _in.CanSeek; } }
        public bool BigEndian { get { return _big; } }

        /// <summary>
        /// Reads up to count from the stream, will not stop early
        /// unless the end of stream is reached.
        /// </summary>
        /// <returns>Number of bytes read</returns>
        public int Read(byte[] buffer, int offset, int count) 
        {
            int read = 0;
            while (read < count)
            {
                int r = _in.Read(buffer, offset, count);
                if (r == 0) 
                    return read;
                read += r;
            }
            return read; 
        }

        /// <summary>
        /// Reads the required amount of data from the stream, throws
        /// if the amount wasn't read.
        /// </summary>
        /// <param name="buffer">Buffer to write into</param>
        /// <param name="offset">Offset into the buffer</param>
        /// <param name="count">Number of bytes to read</param>
        public void ReadEx(byte[] buffer, int offset, int count)
        {
            ReadEx(_in, buffer, offset, count);
        }

        /// <summary>
        /// Reads the required amount of data from the stream, throws
        /// if the amount wasn't read.
        /// </summary>
        /// <param name="stream">Stream to read from</param>
        /// <param name="buffer">Buffer to write into</param>
        /// <param name="offset">Offset into the buffer</param>
        /// <param name="count">Number of bytes to read</param>
        public static void ReadEx(Stream stream, byte[] buffer, int offset, int count)
        {
            int read, total = 0;
            do
            {
                read = stream.Read(buffer, offset, count);
                total += read;
            } while (total < count && read > 0);
            if (total != count)
                throw new InvalidDataException("Unexpected end");
        }

        /// <summary>
        /// Reads a single byte from the stream
        /// </summary>
        /// <returns>A single byte</returns>
        public byte ReadByte()
        {
            int ret = _in.ReadByte();
            if (ret == -1) throw new EndOfStreamException();
            return (byte)ret;
        }

        /// <summary>
        /// Reads a single byte from the stream
        /// </summary>
        /// <returns>A single signed byte</returns>
        public sbyte ReadSByte()
        {
            int ret = _in.ReadByte();
            if (ret == -1) throw new EndOfStreamException();
            return unchecked((sbyte)ret);
        }

        /// <summary>
        /// Change the position
        /// </summary>
        /// <param name="offset">Offset from origin</param>
        /// <param name="origin">Start, end or current position</param>
        /// <returns>New position</returns>
        public long Seek(long offset, SeekOrigin origin) { return _in.Seek(offset, origin); }

        /// <summary>
        /// Change the position
        /// </summary>
        /// <param name="offset">Offset from the beginning of the stream</param>
        /// <returns>New position</returns>
        public long Seek(long offset) { return _in.Seek(offset, SeekOrigin.Begin); }

        /// <summary>
        /// Reads a unicode character from the stream
        /// </summary>
        /// <returns>Unicode character</returns>
        public char ReadChar()
        {
            Decoder dec = _char_encoder.GetDecoder();
            bool complete; int countbytes, countchars;
            byte[] bytes = new byte[2]; //Assuming 2 byte encoding
            char[] chars = new char[1];
            int read = 0, last;
            do
            {
                //Reads in two bytes
                while ((read += last = _in.Read(bytes, 0, bytes.Length)) < bytes.Length)
                    if (last == 0) throw new EndOfStreamException();

                //Converts the two bytes into a character. If two bytes wasn't enough,
                //another two bytes are read in.
                dec.Convert(bytes, 0, bytes.Length, chars, 0, chars.Length, false,
                    out countbytes, out countchars, out complete);
            } while (!complete);
            return chars[0];
        }

        /// <summary>
        /// Reads an 8 byte number from the stream
        /// </summary>
        /// <returns>The next 8 bytes as a long</returns>
        public long ReadLong()
        {
            byte[] buf = new byte[8];
            if (_in.Read(buf, 0, 8) != 8)
                throw new EndOfStreamException();

            return (_big) ? GetLongBigEndian(buf) : GetLongLittleEndian(buf);
        }

        /// <summary>
        /// Reads the next 4 bytes in the stream
        /// </summary>
        /// <returns>The next four bytes as a int</returns>
        public int ReadInt()
        {
            if (_in.Read(bytes, 0, 4) != 4)
                throw new EndOfStreamException();

            return (_big) ? GetIntBigEndian() : GetIntLittleEndian();
        }

        public static int ReadInt(bool big_endian, int pos, byte[] bytes)
        {
            return big_endian ? ReadIntBE(pos, bytes) : ReadIntLE(pos, bytes);
        }

        /// <summary>
        /// Read int Little Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>int</returns>
        public static int ReadIntLE(int pos, byte[] bytes)
        {
            if (pos + 3 >= bytes.Length)
                throw new EndOfStreamException();

            return
                bytes[pos + 3] << 24 |
                bytes[pos + 2] << 16 |
                bytes[pos + 1] << 8 |
                bytes[pos];
        }

        /// <summary>
        /// Read int Big Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>int</returns>
        public static int ReadIntBE(int pos, byte[] bytes)
        {
            if (pos + 3 >= bytes.Length)
                throw new EndOfStreamException();

            return
                bytes[pos] << 24 |
                bytes[pos + 1] << 16 |
                bytes[pos + 2] << 8 |
                bytes[pos + 3];
        }

        /// <summary>
        /// Reads the next 4 bytes in the stream
        /// </summary>
        /// <returns>The next four bytes as a uint</returns>
        public uint ReadUInt()
        {
            if (_in.Read(bytes, 0, 4) != 4)
                throw new EndOfStreamException();

            return (_big) ? GetUIntBigEndian() : GetUIntLittleEndian();
        }

        /// <summary>
        /// Reads the next 4 bytes in the stream
        /// </summary>
        /// <returns>The next four bytes as a uint</returns>
        public static uint ReadUInt(bool big_endian, int pos, byte[] bytes)
        {
            return big_endian ? ReadUIntBE(pos, bytes) :
                ReadUIntLE(pos, bytes);
        }

        /// <summary>
        /// Read uint Little Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>uint</returns>
        public static uint ReadUIntLE(int pos, byte[] bytes)
        {
            if (pos + 3 >= bytes.Length)
                throw new EndOfStreamException();

            return
                ((uint)bytes[pos + 3]) << 24 |
                ((uint)bytes[pos + 2]) << 16 |
                ((uint)bytes[pos + 1]) << 8 |
                 (uint)bytes[pos];
        }

        /// <summary>
        /// Read uint Big Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>uint</returns>
        public static uint ReadUIntBE(int pos, byte[] bytes)
        {
            if (pos + 3 >= bytes.Length)
                throw new EndOfStreamException();

            return
                ((uint)bytes[pos]) << 24 |
                ((uint)bytes[pos + 1]) << 16 |
                ((uint)bytes[pos + 2]) << 8 |
                 (uint)bytes[pos + 3];
        }

        /// <summary>
        /// Reads an 8 byte number from the stream
        /// </summary>
        /// <returns>The next 8 bytes as a long</returns>
        public ulong ReadULong()
        {
            byte[] buf = new byte[8];
            if (_in.Read(buf, 0, 8) != 8)
                throw new EndOfStreamException();

            return (_big) ? ReadULongBE(0, buf) : ReadULongLE(0, buf);
        }

        /// <summary>
        /// Reads the next 8 bytes in the stream
        /// </summary>
        /// <returns>The next four bytes as a uint</returns>
        public static ulong ReadULong(bool big_endian, int pos, byte[] bytes)
        {
            return big_endian ? ReadULongBE(pos, bytes) :
                ReadULongLE(pos, bytes);
        }

        /// <summary>
        /// Read ulong Little Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>ulong</returns>
        public static ulong ReadULongLE(int pos, byte[] bytes)
        {
            if (pos + 7 >= bytes.Length)
                throw new EndOfStreamException();

            return
                ((ulong)bytes[pos + 7]) << 56 |
                ((ulong)bytes[pos + 6]) << 48 |
                ((ulong)bytes[pos + 5]) << 40 |
                ((ulong)bytes[pos + 4]) << 32 |
                ((ulong)bytes[pos + 3]) << 24 |
                ((ulong)bytes[pos + 2]) << 16 |
                ((ulong)bytes[pos + 1]) << 8 |
                 (ulong)bytes[pos];
        }

        /// <summary>
        /// Read ulong Big Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>ulong</returns>
        public static ulong ReadULongBE(int pos, byte[] bytes)
        {
            if (pos + 7 >= bytes.Length)
                throw new EndOfStreamException();

            return
                ((ulong)bytes[pos]) << 56 |
                ((ulong)bytes[pos + 1]) << 48 |
                ((ulong)bytes[pos + 2]) << 40 |
                ((ulong)bytes[pos + 3]) << 32 |
                ((ulong)bytes[pos + 4]) << 24 |
                ((ulong)bytes[pos + 5]) << 16 |
                ((ulong)bytes[pos + 6]) << 8 |
                 (ulong)bytes[pos + 7];
        }

        /// <summary>
        /// Reads the next 8 bytes in the stream
        /// </summary>
        /// <returns>The next four bytes as a uint</returns>
        public static long ReadLong(bool big_endian, int pos, byte[] bytes)
        {
            return big_endian ? ReadLongBE(pos, bytes) :
                ReadLongLE(pos, bytes);
        }

        /// <summary>
        /// Read ulong Little Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>ulong</returns>
        public static long ReadLongLE(int pos, byte[] bytes)
        {
            if (pos + 7 >= bytes.Length)
                throw new EndOfStreamException();

            return
                ((long)bytes[pos + 7]) << 56 |
                ((long)bytes[pos + 6]) << 48 |
                ((long)bytes[pos + 5]) << 40 |
                ((long)bytes[pos + 4]) << 32 |
                ((long)bytes[pos + 3]) << 24 |
                ((long)bytes[pos + 2]) << 16 |
                ((long)bytes[pos + 1]) << 8 |
                 (long)bytes[pos];
        }

        /// <summary>
        /// Read ulong Big Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>ulong</returns>
        public static long ReadLongBE(int pos, byte[] bytes)
        {
            if (pos + 7 >= bytes.Length)
                throw new EndOfStreamException();

            return
                ((long)bytes[pos]) << 56 |
                ((long)bytes[pos + 1]) << 48 |
                ((long)bytes[pos + 2]) << 40 |
                ((long)bytes[pos + 3]) << 32 |
                ((long)bytes[pos + 4]) << 24 |
                ((long)bytes[pos + 5]) << 16 |
                ((long)bytes[pos + 6]) << 8 |
                 (long)bytes[pos + 7];
        }

        /// <summary>
        /// Reads the next 2 bytes in the stream
        /// </summary>
        /// <returns>The next two bytes as a short</returns>
        public short ReadShort()
        {
            if (_in.Read(bytes, 0, 2) != 2)
                throw new EndOfStreamException();

            return (_big) ? GetShortBigEndian() : GetShortLittleEndian();
        }

        /// <summary>
        /// Read short Little Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>short</returns>
        public static short ReadShortLE(int pos, byte[] bytes)
        {
            if (pos + 1 >= bytes.Length)
                throw new EndOfStreamException();

            return (short)((bytes[pos] & 0xff) | (bytes[pos + 1] & 0xff) << 8);
        }

        /// <summary>
        /// Read short Big Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>short</returns>
        public static short ReadShortBE(int pos, byte[] bytes)
        {
            if (pos + 1 >= bytes.Length)
                throw new EndOfStreamException();

            return (short)((bytes[pos] & 0xff) << 8 | (bytes[pos + 1] & 0xff));
        }

        /// <summary>
        /// Read short Big Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>short</returns>
        public static short ReadShort(bool big_endian, int pos, byte[] bytes)
        {
            return big_endian ? ReadShortBE(pos, bytes) : ReadShortLE(pos, bytes);
        }

        /// <summary>
        /// Reads the next 2 bytes in the stream
        /// </summary>
        /// <returns>The next two bytes as a ushort</returns>
        public ushort ReadUShort()
        {
            if (_in.Read(bytes, 0, 2) != 2)
                throw new EndOfStreamException();

            return (_big) ? GetUShortBigEndian() : GetUShortLittleEndian();
        }

        /// <summary>
        /// Reads the next 4 bytes in the stream
        /// </summary>
        /// <returns>The next four bytes as a uint</returns>
        public static ushort ReadUShort(bool big_endian, int pos, byte[] bytes)
        {
            return big_endian ? ReadUShortBE(pos, bytes) :
                ReadUShortLE(pos, bytes);
        }

        /// <summary>
        /// Read ushort Little Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>ushort</returns>
        public static ushort ReadUShortLE(int pos, byte[] bytes)
        {
            if (pos + 1 >= bytes.Length)
                throw new EndOfStreamException();

            return (ushort)((bytes[pos] & 0xff) | (bytes[pos + 1] & 0xff) << 8);
        }

        /// <summary>
        /// Read ushort Big Endian
        /// </summary>
        /// <param name="pos">Position of first byte in the array</param>
        /// <param name="bytes">Array with bytes</param>
        /// <returns>ushort</returns>
        public static ushort ReadUShortBE(int pos, byte[] bytes)
        {
            if (pos + 1 >= bytes.Length)
                throw new EndOfStreamException();

            return (ushort)((bytes[pos] & 0xff) << 8 | (bytes[pos + 1] & 0xff));
        }

        private uint GetUIntBigEndian()
        {
            return
                ((uint) bytes[0]) << 24 |
                ((uint) bytes[1]) << 16 |
                ((uint) bytes[2]) << 8 |
                 (uint) bytes[3];
        }

        private uint GetUIntLittleEndian()
        {
            return
                ((uint) bytes[3]) << 24 |
                ((uint) bytes[2]) << 16 |
                ((uint) bytes[1]) << 8 |
                 (uint) bytes[0];
        }

        private int GetIntBigEndian()
        {
            return
                bytes[0] << 24 |
                bytes[1] << 16 |
                bytes[2] << 8 |
                bytes[3];
        }

        private int GetIntLittleEndian()
        {
            return
                bytes[3] << 24 |
                bytes[2] << 16 |
                bytes[1] << 8 |
                bytes[0];
        }

        private long GetLongBigEndian(byte[] buf)
        {
            return
                buf[0] << 56 |
                buf[1] << 48 |
                buf[2] << 40 |
                buf[3] << 32 |
                buf[4] << 24 |
                buf[5] << 16 |
                buf[6] << 8 |
                buf[7];
        }

        private long GetLongLittleEndian(byte[] buf)
        {
            return
                 buf[7] << 56 |
                 buf[6] << 48 |
                 buf[5] << 40 |
                 buf[4] << 32 |
                 buf[3] << 24 |
                 buf[2] << 16 |
                 buf[1] << 8 |
                 buf[0];
        }

        private short GetShortBigEndian()
        {
            return (short)((bytes[0] & 0xff) << 8 | (bytes[1] & 0xff));
        }

        private short GetShortLittleEndian()
        {
            return (short)((bytes[0] & 0xff) | (bytes[1] & 0xff) << 8);
        }

        private ushort GetUShortBigEndian()
        {
            return (ushort)((bytes[0] & 0xff) << 8 | (bytes[1] & 0xff));
        }

        private ushort GetUShortLittleEndian()
        {
            return (ushort)((bytes[0] & 0xff) | (bytes[1] & 0xff) << 8);
        }
    }
}
