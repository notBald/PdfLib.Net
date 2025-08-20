using System;
using System.Text;
using System.IO;
using PdfLib.Img.Tiff;

namespace PdfLib.Util
{
    /// <summary>
    /// Byte writer, not MT safe
    /// </summary>
    internal class StreamWriterEx
    {
        byte[] _buffer = new byte[8];

        bool _endian;

        Stream _target;

        public StreamWriterEx(byte[] data, bool big_endian)
            : this(new MemoryStream(data), big_endian)
        { }
        public StreamWriterEx(Stream target, bool big_endian)
        {
            _endian = big_endian;
            _target = target;
        }

        public void Skip(int nr)
        {
            _target.Seek(nr, SeekOrigin.Current);
        }

        public void Write(int num)
        {
            BWriter.Write(_endian, unchecked((uint)num), 0, _buffer);
            _target.Write(_buffer, 0, 4);
        }

        public void Write(int num, CRC32_Alt crc)
        {
            Write(num);
            crc.Update(_buffer, 0, 4);
        }

        public void Write(byte num)
        {
            _target.WriteByte(num);
        }

        public void Write(byte num, CRC32_Alt crc)
        {
            _target.WriteByte(num);
            crc.Update(num);
        }

        public void Write(short num)
        {
            BWriter.Write(_endian, (ushort) num, 0, _buffer);
            _target.Write(_buffer, 0, 2);
        }

        public void Write(short num, CRC32_Alt crc)
        {
            Write(num);
            crc.Update(_buffer, 0, 2);
        }

        public void Write(ushort num)
        {
            BWriter.Write(_endian, num, 0, _buffer);
            _target.Write(_buffer, 0, 2);
        }

        public void Write(ushort num, CRC32_Alt crc)
        {
            Write(num);
            crc.Update(_buffer, 0, 2);
        }

        public void Write(uint num)
        {
            BWriter.Write(_endian, num, 0, _buffer);
            _target.Write(_buffer, 0, 4);
        }

        public void Write(uint num, CRC32_Alt crc)
        {
            Write(num);
            crc.Update(_buffer, 0, 4);
        }

        /// <summary>
        /// Writes string to target
        /// </summary>
        /// <param name="str">String to write</param>
        public void Write(string str)
        {
            var b = ASCIIEncoding.ASCII.GetBytes(str);
            _target.Write(b, 0, b.Length);
        }

        /// <summary>
        /// Writes string to target
        /// </summary>
        /// <param name="str">String to write</param>
        /// <param name="crc">For calculating CRC sum</param>
        public void Write(string str, CRC32_Alt crc)
        {
            var b = ASCIIEncoding.ASCII.GetBytes(str);
            _target.Write(b, 0, b.Length);
            crc.Update(b, 0, b.Length);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            _target.Write(buffer, offset, count);
        }

        public void Write(byte[] buffer, int offset, int count, CRC32_Alt crc)
        {
            _target.Write(buffer, offset, count);
            crc.Update(buffer, offset, count);
        }
    }
}
