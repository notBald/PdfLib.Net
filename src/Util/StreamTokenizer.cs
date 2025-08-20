using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Read;

namespace PdfLib.Util
{
    /// <summary>
    /// Cuts a stream into tokens (sepperated by whitespace)
    /// </summary>
    public class StreamTokenizer
    {
        #region Variables and properties

        char[] _buffer = new char[20];
        int bt;
        Stream _inn;

        public bool SepWasLineFeed => bt == 10;

        #endregion

        #region Init

        public StreamTokenizer(Stream inn)
        {
            _inn = inn;
        }

        #endregion

        public string ReadToken()
        {
            do
            {
                bt = _inn.ReadByte();
                if (bt == -1) return null;
            } while (bt == 32 /* Space */ || bt == 10 /* LF */ || bt == 9 /* Tab */ || bt == 13 /* CR */) ;
            int pos = 1;
            _buffer[0] = (char)bt;
            while (true)
            {
                bt = _inn.ReadByte();
                if (bt == -1) break;
                if (bt == 32 /* Space */ || bt == 10 /* LF */ || bt == 9 /* Tab */ || bt == 13 /* CR */)
                    break;
                if (pos >= _buffer.Length)
                    _buffer = new char[_buffer.Length * 2];
                _buffer[pos++] = (char)bt;
            }

            return new string(_buffer, 0, pos);
        }

        /// <summary>
        /// Reads a token on the current line, null if there's no more tokens
        /// </summary>
        /// <returns></returns>
        public string ReadLineToken()
        {
            if (bt == 10 || bt == 13 || bt == -1) return null;
            do
            {
                bt = _inn.ReadByte();
                if (bt == -1) return null;
            } while (bt == 32 /* Space */ || bt == 9 /* Tab */);

            if (bt == 10 || bt == 13) return null;

            int pos = 1;
            _buffer[0] = (char)bt;
            while (true)
            {
                bt = _inn.ReadByte();
                if (bt == -1 || bt == 32 /* Space */ || bt == 10 /* LF */ || bt == 9 /* Tab */ || bt == 13 /* CR */)
                    break;
                if (pos >= _buffer.Length)
                    _buffer = new char[_buffer.Length * 2];
                _buffer[pos++] = (char)bt;
            }

            return new string(_buffer, 0, pos);
        }

        /// <summary>
        /// Reads to the start of the next line
        /// </summary>
        public void SkipLine()
        {
            do
            {
                bt = _inn.ReadByte();
                if (bt == -1) return;
            } while (bt != 10 /* LF */);
        }

#if DEBUG
        public override string ToString()
        {
            var hold = _inn.Position;
            var chars = new byte[_inn.Length - hold];
            _inn.Read(chars, 0, chars.Length);
            _inn.Position = hold;

            return ASCIIEncoding.ASCII.GetString(chars);
        }
#endif
    }
}
