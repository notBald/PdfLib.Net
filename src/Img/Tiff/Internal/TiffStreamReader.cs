using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib.Img.Tiff.Internal
{
    /// <summary>
    /// Thread safe stream. 
    /// </summary>
    public class TiffStreamReader : IDisposable
    {
        #region Properties

        /// <summary>
        /// Endianess
        /// </summary>
        readonly bool _big;

        /// <summary>
        /// Source
        /// </summary>
        readonly Stream _in;

        /// <summary>
        /// Read buffer
        /// </summary>
        byte[] _buf = new byte[8];

        public long Length { get { return _in.Length; } }
        public bool CanRead { get { return _in.CanRead; } }
        public bool CanSeek { get { return _in.CanSeek; } }
        public bool BigEndian { get { return _big; } }
#if DEBUG
        //This value is not reliable in a multithreaded setting.
        public long StreamPosition { get { return _in.Position; } }
#endif

        #endregion

        #region Init

        public TiffStreamReader(byte[] inn)
            : this(new MemoryStream(inn), true) { }

        public TiffStreamReader(Stream inn)
            : this(inn, true) { }

        public TiffStreamReader(Stream inn, bool big_endian)
        {
            _in = inn;
            _big = big_endian;
        }

        /// <summary>
        /// Closes the underlying stream
        /// </summary>
        public void Dispose() { _in.Dispose(); }

        #endregion

        /// <summary>
        /// Reads an 8 byte number from the stream
        /// </summary>
        /// <returns>The next 8 bytes as a long</returns>
        public long ReadLong(long pos)
        {
            lock (_in)
            {
                _in.Position = pos;
                if (_in.Read(_buf, 0, 8) != 8)
                    throw new EndOfStreamException();

                return (_big) ? GetLongBigEndian(_buf) : GetLongLittleEndian(_buf);
            }
        }

        /// <summary>
        /// Reads the next 4 bytes in the stream
        /// </summary>
        /// <returns>The next four bytes as a uint</returns>
        public uint ReadUInt(long pos)
        {
            lock (_in)
            {
                _in.Position = pos;
                if (_in.Read(_buf, 0, 4) != 4)
                    throw new EndOfStreamException();

                return (_big) ? GetUIntBigEndian() : GetUIntLittleEndian();
            }
        }

        /// <summary>
        /// Reads the next 2 bytes in the stream
        /// </summary>
        /// <returns>The next two bytes as a ushort</returns>
        public ushort ReadUShort(long pos)
        {
            lock (_in)
            {
                _in.Position = pos;
                if (_in.Read(_buf, 0, 2) != 2)
                    throw new EndOfStreamException();

                return (_big) ? GetUShortBigEndian() : GetUShortLittleEndian();
            }
        }

        /// <summary>
        /// Reads up to count from the stream, will not stop early
        /// unless the end of stream is reached.
        /// </summary>
        public int Read(long pos, byte[] buffer, int offset, int count)
        {
            lock (_in)
            {
                _in.Position = pos;
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
        }

        /// <summary>
        /// Reads the required amount of data from the stream, throws
        /// if the amount wasn't read.
        /// </summary>
        /// <param name="buffer">Buffer to write into</param>
        /// <param name="offset">Offset into the buffer</param>
        /// <param name="count">Number of bytes to read</param>
        public void ReadEx(long pos, byte[] buffer, int offset, int count)
        {
            lock (_in)
            {
                _in.Position = pos;
                int read, total = 0;
                do
                {
                    read = _in.Read(buffer, 0, count);
                    total += read;
                } while (total < count && read > 0);
                if (total != count)
                    throw new InvalidDataException("Unexpected end");
            }
        }

        #region Utility

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

        private uint GetUIntBigEndian()
        {
            return
                ((uint)_buf[0]) << 24 |
                ((uint)_buf[1]) << 16 |
                ((uint)_buf[2]) << 8 |
                 (uint)_buf[3];
        }

        private uint GetUIntLittleEndian()
        {
            return
                ((uint)_buf[3]) << 24 |
                ((uint)_buf[2]) << 16 |
                ((uint)_buf[1]) << 8 |
                 (uint)_buf[0];
        }

        private ushort GetUShortBigEndian()
        {
            return (ushort)((_buf[0] & 0xff) << 8 | (_buf[1] & 0xff));
        }

        private ushort GetUShortLittleEndian()
        {
            return (ushort)((_buf[0] & 0xff) | (_buf[1] & 0xff) << 8);
        }

        #endregion
    }
}
