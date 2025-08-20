using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PdfLib.Read.TrueType
{
    internal class WriteFont
    {
        #region Variables and properties

        byte[] _buffer = new byte[8192];
        int _buffer_pos = 0;
        int _commit_pos = 0;

        public int Position { get { return _buffer_pos; } set { _buffer_pos = value; } }
        public int Offset { get { return _commit_pos; } }
        public int Length { get { return _buffer_pos - _commit_pos; } }

        public string HexDump
        {
            get
            {
                return PdfLib.Pdf.Filter.PdfHexFilter.HexDump(ToArray(), new int[] { 8, 8 });
            }
        }

        #endregion

        #region Init

        public WriteFont()
        {

        }

        #endregion

        #region Write functions

        public void Write(PdfLib.Read.TrueType.Tag tag)
        { Write((uint)tag); }

        public void Write(uint val)
        {
            CheckBufferSize();
            _buffer[_buffer_pos++] = (byte)((val >> 24) & 0xFF);
            _buffer[_buffer_pos++] = (byte)((val >> 16) & 0xFF);
            _buffer[_buffer_pos++] = (byte)((val >>  8) & 0xFF);
            _buffer[_buffer_pos++] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Writes a big endian short
        /// </summary>
        /// <param name="val">Value of the short</param>
        public void Write(ushort val)
        {
            CheckBufferSize();
            _buffer[_buffer_pos++] = (byte)((val >> 8) & 0xFF);
            _buffer[_buffer_pos++] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Writes a big endian short
        /// </summary>
        /// <param name="val">Value of the short</param>
        public void Write(short val)
        {
            CheckBufferSize();
            _buffer[_buffer_pos++] = (byte)((val >> 8) & 0xFF);
            _buffer[_buffer_pos++] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Writes all bytes in the array
        /// </summary>
        public void Write(byte[] ba)
        { Write(ba, 0, ba.Length); }

        /// <summary>
        /// Writes bytes in a byte array to the data
        /// </summary>
        /// <param name="ba">Byte array with bytes</param>
        /// <param name="offset">Offset into ba</param>
        /// <param name="count">Number of bytes to read from ba</param>
        public void Write(byte[] ba, int offset, int count)
        {
            if (_buffer_pos + count >= _buffer.Length)
                Array.Resize<byte>(ref _buffer, _buffer.Length * 2 + count);
            Buffer.BlockCopy(ba, offset, _buffer, _buffer_pos, count);
            _buffer_pos += count;
        }

        public void Write(byte b)
        {
            CheckBufferSize();
            _buffer[_buffer_pos++] = b;
        }

        public void BeginWrite()
        {
            _commit_pos = _buffer_pos;
        }

        /// <summary>
        /// Reads a fixed format number
        /// </summary>
        public void WriteFixed(double val)
        {
            short int_part = (short)val;
            ushort float_part = (ushort)((val - int_part) * 16384);

            Write(int_part);
            Write(float_part);
        }

        public uint ReadUInt()
        {
            return
                ((uint)_buffer[_buffer_pos++]) << 24 |
                ((uint)_buffer[_buffer_pos++]) << 16 |
                ((uint)_buffer[_buffer_pos++]) << 8 |
                 (uint)_buffer[_buffer_pos++];
        }

        public short ReadShort()
        {
            return (short)((_buffer[_buffer_pos++] & 0xff) << 8 | 
                (_buffer[_buffer_pos++] & 0xff));
        }

        public byte ReadByte() { return _buffer[_buffer_pos++]; } 

        public static void Write(long val, byte[] ba, int offset)
        {
            ba[offset++] = (byte)((val >> 56) & 0xFF);
            ba[offset++] = (byte)((val >> 48) & 0xFF);
            ba[offset++] = (byte)((val >> 40) & 0xFF);
            ba[offset++] = (byte)((val >> 32) & 0xFF);
            ba[offset++] = (byte)((val >> 24) & 0xFF);
            ba[offset++] = (byte)((val >> 16) & 0xFF);
            ba[offset++] = (byte)((val >> 8) & 0xFF);
            ba[offset  ] = (byte)(val & 0xFF);
        }

        public static void Write(uint val, byte[] ba, int offset)
        {
            ba[offset++] = (byte)((val >> 24) & 0xFF);
            ba[offset++] = (byte)((val >> 16) & 0xFF);
            ba[offset++] = (byte)((val >> 8) & 0xFF);
            ba[offset] = (byte)(val & 0xFF);
        }

        public static void Write(int val, byte[] ba, int offset)
        {
            ba[offset++] = (byte)((val >> 24) & 0xFF);
            ba[offset++] = (byte)((val >> 16) & 0xFF);
            ba[offset++] = (byte)((val >> 8) & 0xFF);
            ba[offset] = (byte)(val & 0xFF);
        }

        public static void Write(ushort val, byte[] ba, int offset)
        {
            ba[offset++] = (byte)((val >> 8) & 0xFF);
            ba[offset] = (byte)(val & 0xFF);
        }

        public static void Write(short val, byte[] ba, int offset)
        {
            ba[offset++] = (byte)((val >> 8) & 0xFF);
            ba[offset] = (byte)(val & 0xFF);
        }

        public static ulong ReadULong(byte[] ba, int offset)
        {
            return
                ((ulong)ba[offset + 0]) << 56 |
                ((ulong)ba[offset + 1]) << 48 |
                ((ulong)ba[offset + 2]) << 40 |
                ((ulong)ba[offset + 3]) << 32 |
                ((ulong)ba[offset + 4]) << 24 |
                ((ulong)ba[offset + 5]) << 16 |
                ((ulong)ba[offset + 6]) << 8 |
                ((ulong)ba[offset + 7]);
        }

        public static long ReadLong(byte[] ba, int offset)
        {
            return
                ba[offset + 0] << 56 |
                ba[offset + 1] << 48 |
                ba[offset + 2] << 40 |
                ba[offset + 3] << 32 |
                ba[offset + 4] << 24 |
                ba[offset + 5] << 16 |
                ba[offset + 6] << 8 |
                ba[offset + 7];
        }

        public static double ReadFixed(byte[] ba, int offset)
        {
            return ReadShort(ba, offset) + ReadUShort(ba, offset + 2) / 16384;
        }

        /// <summary>
        /// Reads a fixed format number
        /// </summary>
        public static void WriteFixed(double val, byte[] ba, int offset)
        {
            short int_part = (short)val;
            ushort float_part = (ushort)((val - int_part) * 16384);

            Write(int_part, ba, offset);
            Write(float_part, ba, offset + 2);
        }

        public static uint ReadUInt(byte[] ba, int offset)
        {
            return
                ((uint)ba[offset + 0]) << 24 |
                ((uint)ba[offset + 1]) << 16 |
                ((uint)ba[offset + 2]) << 8 |
                 (uint)ba[offset + 3];
        }

        public static ushort ReadUShort(byte[] ba, int offset)
        {
            return (ushort)((ba[offset] & 0xff) << 8 | (ba[offset+1] & 0xff));
        }

        public static short ReadShort(byte[] ba, int offset)
        {
            return (short)((ba[offset] & 0xff) << 8 | (ba[offset + 1] & 0xff));
        }

        public void Skip(int n)
        {
            _buffer_pos += n;
            CheckBufferSize();
        }

        /// <summary>
        /// Aligns the position to a 32-bit boundary, and
        /// sets the commit pos if this is done.
        /// </summary>
        public void Align()
        {
            //Aligns the position to a 32-bit boundary)
            var adjust = _buffer_pos % 4;
            if (adjust != 0)
            {
                _buffer_pos += 4 - adjust;
                CheckBufferSize();
            }
        }

        /// <summary>
        /// Aligns the position to a 16-bit boundary, and
        /// sets the commit pos if this is done.
        /// </summary>
        public void ShortAlign()
        {
            _buffer_pos += _buffer_pos % 2;
        }

        #endregion

        #region Utility functions

        public ushort MaxPow(int val)
        {
            int p = 0, org = val;
            val /= 2;
            while (val > 1)
            {
                p++;
                val /= 2;
            }

            //Fixes "one to short"
            if (Math.Pow(2, p + 1) <= org)
                p++;

            return (ushort)p;
        }

        public byte[] ToArray()
        {
            var ba = new byte[_buffer_pos];
            Buffer.BlockCopy(_buffer, 0, ba, 0, _buffer_pos);
            return ba;
        }

        public void WriteTo(Stream target)
        {
            target.Write(_buffer, 0, _buffer_pos);
        }

        private void CheckBufferSize()
        {
            if (_buffer_pos + 4 >= _buffer.Length)
                Array.Resize<byte>(ref _buffer, _buffer.Length * 2);
            Debug.Assert(_buffer_pos + 4 < _buffer.Length);
        }

        #endregion
    }
}
