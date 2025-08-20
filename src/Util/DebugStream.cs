using System;
using System.IO;

namespace PdfLib.Util
{
    /// <summary>
    /// Stream that counts how much data is read from the stream or
    /// written to the stream.
    /// </summary>
    public class DebugStream : Stream
    {
        #region Variables and properties

        public int BytesRead, BytesWritten, NumReads;
        
        private byte[] _break_pattern, _break_buffer;
        private long _break_pos = -1;

        /// <summary>
        /// If this pattern is found, a debug assert is thrown. 
        /// </summary>
        public byte[] BreakPattern 
        { 
            get { return _break_pattern; }
            set
            {
                if (value == null)
                    _break_buffer = null;
                else
                    _break_buffer = new byte[value.Length];
                _break_pattern = value;
            }
        }

        /// <summary>
        /// Stops at this position. Must be used along with Break Pattern, just
        /// set a break pattern. 
        /// </summary>
        public int BreakPos
        {
            get { return (int) _break_pos; }
            set { _break_pos = value; }
        }

        readonly Stream _s;

        public override bool CanRead { get { return _s.CanRead; } }
        public override bool CanSeek { get { return _s.CanSeek; } }
        public override bool CanTimeout { get { return _s.CanTimeout; } }
        public override bool CanWrite { get { return _s.CanWrite; } }
        public override long Length { get { return _s.Length; } }
        public override long Position 
        { 
            get { return _s.Position; } 
            set 
            { 
                _s.Position = value;
                if (_s.Position == _break_pos)
                    System.Diagnostics.Debug.Assert(false);
            } 
        }
        public override int ReadTimeout { get { return _s.ReadTimeout; } set { _s.ReadTimeout = value; } }
        public override int WriteTimeout { get { return base.WriteTimeout; } set { base.WriteTimeout = value; } }

        #endregion

        #region Init

        public DebugStream(Stream s) 
        {
            if (s == null) throw new ArgumentNullException();
            _s = s; 
        }

        #endregion

        public override int ReadByte()
        {
            var ret = _s.ReadByte();
            if (ret != -1) BytesRead++; NumReads++;
            return ret;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ret = _s.Read(buffer, offset, count);
            BytesRead += ret; NumReads++;
            return ret;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var r = _s.Seek(offset, origin);
            if (_s.Position == _break_pos)
                System.Diagnostics.Debug.Assert(false);
            return r;
        }

        public override void SetLength(long value) { _s.SetLength(value); }

        public override void Close() { _s.Close(); }
        public override void Flush() { _s.Flush(); }
        protected override void Dispose(bool disposing)
        { _s.Dispose(); }
        public override void WriteByte(byte value)
        {
            BytesWritten++;
            _s.WriteByte(value);
            if (_break_pattern != null)
                PushBuffer(value);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            BytesWritten += count;
            if (_break_pattern == null)
                _s.Write(buffer, offset, count);
            else
            {
                for (int c = 0; c < count; c++)
                    WriteByte(buffer[offset + c]);
            }
        }

        void PushBuffer(byte val)
        {
            System.Array.Copy(_break_buffer, 1, _break_buffer, 0, _break_buffer.Length - 1);
            _break_buffer[_break_buffer.Length - 1] = val;
            if (_s.Position == _break_pos || Util.ArrayHelper.ArraysEqual<byte>(_break_buffer, _break_pattern))
            {
                System.Diagnostics.Debug.Assert(false);
                //if (val == 3)
                //    System.Console.Write("");
            }
        }

        public override string ToString()
        {
            return string.Format("Read: {0} | NReads {2} | Written: {1}", BytesRead, BytesWritten, NumReads);
        }
    }
}
