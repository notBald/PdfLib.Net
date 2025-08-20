using System;
using System.IO;
using System.Collections.Generic;

namespace PdfLib.Util
{
    /// <summary>
    /// Recordes all data read or written through the stream
    /// 
    /// This data will be recorded serialy with no regard as
    /// to where the data is fetced in the target stream
    /// </summary>
    public class RecordStream : Stream
    {
        #region Variables

        /// <summary>
        /// Recorded data
        /// </summary>
        private byte[] _buffer;

        /// <summary>
        /// Stream being read from or written to
        /// </summary>
        private readonly Stream _target;

        private int _buffer_pos;

        #endregion

        #region Properties

        public override bool CanRead { get { return _target.CanRead; } }
        public override bool CanSeek { get { return _target.CanSeek; } }
        public override bool CanTimeout { get { return _target.CanTimeout; } }
        public override bool CanWrite { get { return _target.CanWrite; } }
        public override long Length { get { return _target.Length; } }
        public override long Position { get { return _target.Position; } set { _target.Position = value; } }
        public override int ReadTimeout { get { return _target.ReadTimeout; } set { _target.ReadTimeout = value; } }
        public override int WriteTimeout { get { return base.WriteTimeout; } set { base.WriteTimeout = value; } }
        public int BufferLength { get { return _buffer_pos; } }

        #endregion

        #region Init

        public RecordStream(int buffer_size, Stream target)
        {
            if (target == null) throw new ArgumentNullException();

            _buffer = new byte[buffer_size];
            _target = target;
        }

        #endregion

        /// <summary>
        /// Returns all recorded data
        /// </summary>
        public byte[] ToArray()
        {
            return ToArray(_buffer_pos);
        }

        /// <summary>
        /// Returns up to size recorded data
        /// </summary>
        /// <param name="size">Maximum amount of data to return</param>
        public byte[] ToArray(int size)
        {
            size = Math.Min(_buffer_pos, size);
            var ba = new byte[size];
            Buffer.BlockCopy(_buffer, 0, ba, 0, size);
            return ba;
        }

        #region Read and Write methods

        public override int ReadByte()
        {
            var ret = _target.ReadByte();
            if (ret != -1) PutByte((byte) ret);
            return ret;
        }

        public override void WriteByte(byte b)
        {
            _target.WriteByte(b);
            PutByte(b);
        }

        private void PutByte(byte b)
        {
            if (_buffer_pos == _buffer.Length)
                Array.Resize<byte>(ref _buffer, _buffer_pos * 2);
            _buffer[_buffer_pos++] = b;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _target.Write(buffer, offset, count);
            if (_buffer_pos + count >= _buffer.Length)
                Array.Resize<byte>(ref _buffer, _buffer.Length * 2 + count);
            Buffer.BlockCopy(buffer, offset, _buffer, _buffer_pos, count);
            _buffer_pos += count;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = _target.Read(buffer, offset, count);
            if (count > 0)
            {
                if (_buffer_pos + count >= _buffer.Length)
                    Array.Resize<byte>(ref _buffer, _buffer.Length * 2 + count);
                Buffer.BlockCopy(buffer, offset, _buffer, _buffer_pos, count);
                _buffer_pos += count;
            }
            return count;
        }

        #endregion

        #region Required overrides

        public override void SetLength(long value) { _target.SetLength(value); }
        public override long Seek(long offset, SeekOrigin origin)
        { return _target.Seek(offset, origin); }
        public override void Close() { _target.Close(); }
        public override void Flush() { _target.Flush(); }
        protected override void Dispose(bool disposing)
        { _target.Dispose(); }
        public override string ToString()
        { return _target.ToString(); }

        #endregion
    }
}
