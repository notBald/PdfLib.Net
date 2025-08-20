using System;
using System.IO;
using System.Text;

namespace PdfLib.Util
{
    /// <summary>
    /// Allows one to use a string builder as it was a stream.
    /// Written data will always be appended to the end, regardless
    /// of the position.
    /// </summary>
    public class StringBuilderStream : Stream
    {
        #region Variables and properties

        readonly StringBuilder _sb;
        int _pos;

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanTimeout { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { return _sb.Length; } }
        public override long Position { get { return _pos; } set { _pos = (int) value; } }

        #endregion

        #region Init

        public StringBuilderStream() : this(new StringBuilder()) { }
        public StringBuilderStream(StringBuilder sb) { _sb = sb; _pos = sb.Length - 1; }

        #endregion

        public override int ReadByte()
        {
            if (_pos >= _sb.Length) return -1;
            return _sb[_pos++];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = (_pos + count >= _sb.Length) ? _sb.Length - _pos : count;
            for (int c = 0; c < count; c++)
                buffer[offset + c] = (byte) _sb[_pos++];
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _pos = (int) offset; break;

                case SeekOrigin.Current:
                    _pos += (int) offset; break;

                case SeekOrigin.End:
                    _pos = (int) (_sb.Length - 1 + offset); break;
            }
            return _pos;
        }

        public override void SetLength(long value) { _sb.Length = (int) value; }

        public override void Flush() { }
        protected override void Dispose(bool disposing) { }
        public override void WriteByte(byte value)
        {
            _pos++;
            _sb.Append((char) value);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            _pos += count;
            _sb.Append(PdfLib.Read.Lexer.GetString(buffer, offset, count));
        }

        public override string ToString()
        {
            return _sb.ToString();
        }
    }
}
