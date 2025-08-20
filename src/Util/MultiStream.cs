using System;
using System.IO;
using System.Collections.Generic;

namespace PdfLib.Util
{
    /// <summary>
    /// Class that combines streams into a single stream
    /// From: http://www.csharphelp.com/2007/03/combine-streams-in-a-single-c-stream-object/
    /// </summary>
    public class MultiStream : Stream
    {
        List<Stream> _streams;
        long _pos = 0;
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }

        public override long Length
        {
            get
            {
                long result = 0;
                for(int c=0; c < _streams.Count; c++)
                    result += _streams[c].Length;

                return result;
            }
        }

        public override long Position
        {
            get { return _pos; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public MultiStream() { _streams = new List<Stream>(); }
        public MultiStream(params Stream[] streams) { _streams = new List<Stream>(streams); }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long len = Length;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _pos = offset;
                    break;
                case SeekOrigin.Current:
                    _pos += offset;
                    break;
                case SeekOrigin.End:
                    _pos = len - offset;
                    break;
            }
            if (_pos > len)
                _pos = len;
            else if (_pos < 0)
                _pos = 0;

            return _pos;
        }

        public override void SetLength(long value) { }

        public void AddStream(Stream stream)
        {
            _streams.Add(stream);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long len = 0;
            int result = 0;
            int buf_pos = offset;
            int bytesRead;
            for (int c = 0; c < _streams.Count; c++ )
            {
                var stream = _streams[c];
                if (_pos < (len + stream.Length))
                {
                    stream.Position = _pos - len;
                    bytesRead = stream.Read(buffer, buf_pos, count);
                    result += bytesRead;
                    buf_pos += bytesRead;
                    _pos += bytesRead;
                    if (bytesRead >= count)
                        break;

                    count -= bytesRead;
                }
                len += stream.Length;
            }
            return result;
        }

        public override void Write(byte[] buffer, int offset, int count)
        { throw new NotSupportedException(); }
    }
}
