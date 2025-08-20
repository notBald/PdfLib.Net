using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using PdfLib.Read;

namespace PdfLib.PostScript.Internal
{
    /// <summary>
    /// Decrypts eexec encrypted data
    /// </summary>
    public class EexecStream : Stream
    {
        #region Variables and properties

        readonly Stream _s;
        readonly bool _ascii;

        internal const ushort c1 = 52845, c2 = 22719;
        ushort r;
        readonly long _start_pos;

        public override bool CanRead { get { return _s.CanRead; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanTimeout { get { return _s.CanTimeout; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _s.Length; } }
        /// <summary>
        /// Note, this function should not be used. (But needs to be implemented)
        /// </summary>
        public override long Position { get { return _s.Position; } set { _s.Position = value; } }
        public override int ReadTimeout { get { return _s.ReadTimeout; } set { _s.ReadTimeout = value; } }
        public override int WriteTimeout { get { return base.WriteTimeout; } set { base.WriteTimeout = value; } }

        public Stream InnerStream { get { return _s; } }

        public string UnencryptedData
        {
            get
            {
                long current_pos = _s.Position;
                ushort cr = r;

                var sb = new StringBuilder();
                Reset();

                int ch = ReadByte();
                while (ch != -1)
                {
                    sb.Append((char)ch);
                    ch = ReadByte();
                }

                _s.Position = current_pos;
                r = cr;

                return sb.Replace('\0', ' ').ToString();
            }
        }

        #endregion

        #region Init

        public EexecStream(Stream s) 
        { 
            _s = s;

            //Skips whitespace
            Chars cur = (Chars) _s.ReadByte();
            /*while (Lexer.IsWhiteSpace(cur))
                cur = (Chars)_s.ReadByte();*/

            //Tests for ASCII
            bool ascii = true;
            _start_pos = _s.Position - 1;
            for (int c = 0; c < 4; c++)
            {
                if (!Lexer.IsHex(cur))
                {
                    ascii = false;
                    break;
                }
                cur = (Chars)_s.ReadByte();
            }
            _ascii = ascii;
            Reset();
        }

        /// <summary>
        /// Puts the stream back at the start position
        /// </summary>
        void Reset()
        {
            _s.Position = _start_pos;
            r = 55665;

            //Reads the first four bytes
            ReadByte();
            ReadByte();
            ReadByte();
            ReadByte();
        }

        #endregion

        /*public int Decrypt(int cipher)
        {
            int plain = (cipher ^ (r>>8));
            r = (ushort) ((cipher + r) * c1 + c2);
            return plain;
        }*/

        public override int ReadByte()
        {
            var ret = _s.ReadByte();
            if (ret == -1) return ret;

            if (_ascii) throw new NotImplementedException("ASCII decryption");

            var cipher = ret;
            int plain = (cipher ^ (r >> 8));
            r = (ushort)((cipher + r) * c1 + c2);
            return plain;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ret = _s.Read(buffer, offset, count);
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
            //return _s.Seek(offset, origin);
        }

        public override void SetLength(long value) { _s.SetLength(value); }

        public override void Close() { _s.Close(); }
        public override void Flush() { _s.Flush(); }
        protected override void Dispose(bool disposing)
        { _s.Dispose(); }

        //Writing is unencrypted
        public override void WriteByte(byte value)
        {
            _s.WriteByte(value);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            _s.Write(buffer, offset, count);
        }

        #region Helper functions

        /// <summary>
        /// Decrypts a CharString
        /// </summary>
        /// <param name="encrypted">Encrypted data</param>
        /// <param name="r">Initial seed</param>
        /// <param name="n_random">Number of random bytes</param>
        /// <returns>Unencrypted data</returns>
        public static byte[] Decrypt(byte[] encrypted, int r, int n_random)
        {
            var decrypted = new byte[encrypted.Length - n_random];

            //Decrypts the data
            for (int c = 0; c < n_random && c < encrypted.Length; c++)
                r = (ushort)((encrypted[c] + r) * c1 + c2);
            for (int c = n_random; c < encrypted.Length; c++)
            {
                byte cipher = encrypted[c];
                decrypted[c - n_random] = (byte)(cipher ^ (r >> 8));
                r = (ushort)((cipher + r) * c1 + c2);
            }

            return decrypted;
        }

        /// <summary>
        /// Decodes charstring data into a string (nice for debugging)
        /// </summary>
        /// <param name="dec">decrypted charstring</param>
        /// <returns>Human readable charstring</returns>
        public static string MakeReadable(byte[] dec)
        {
            var sb = new StringBuilder();
            for (int c = 0; c < dec.Length; c++)
            {
                byte b = dec[c];

                //This is a number
                if (b >= 32)
                {
                    if (b <= 246)
                        sb.Append(b - 139);
                    else if (b <= 250)
                        sb.Append(((b - 247) * 256) + dec[++c] + 108);
                    else if (b <= 254)
                        sb.Append(-((b - 251) * 256) - dec[++c] - 108);
                    else
                        sb.Append((dec[++c] << 24 | dec[++c] << 16 | dec[++c] << 8 | dec[++c]));

                    sb.Append(' ');
                }
                else
                {
                    switch (b)
                    {
                        case 0: sb.Append("NOP"); break;
                        case 1: sb.Append("hstem"); break;
                        case 2: sb.Append("NOP"); break;
                        case 3: sb.Append("vstem"); break;
                        case 4: sb.Append("vmoveto"); break;
                        case 5: sb.Append("rlineto"); break;
                        case 6: sb.Append("hlineto"); break;
                        case 7: sb.Append("vlineto"); break;
                        case 8: sb.Append("rcurveto"); break;
                        case 9: sb.Append("closepath"); break;
                        case 10: sb.Append("callsubr"); break;
                        case 11: sb.Append("return"); break;
                        case 12:
                            switch (dec[++c])
                            {
                                case 0: sb.Append("dotsection"); break;
                                case 1: sb.Append("vstem3"); break;
                                case 2: sb.Append("hstem3"); break;
                                case 6: sb.Append("seac"); break;
                                case 7: sb.Append("sbw"); break;
                                case 12: sb.Append("div"); break;
                                case 16: sb.Append("callothersubr"); break;
                                case 17: sb.Append("pop"); break;
                                case 33: sb.Append("setcurrentpoint"); break;
                            }
                            break;
                        case 13: sb.Append("hsbw"); break;
                        case 14: sb.Append("endchar"); break;
                        case 15: sb.Append("NOP"); break;
                        case 16: sb.Append("NOP"); break;
                        case 17: sb.Append("NOP"); break;
                        case 18: sb.Append("NOP"); break;
                        case 19: sb.Append("NOP"); break;
                        case 20: sb.Append("NOP"); break;
                        case 21: sb.Append("rmoveto"); break;
                        case 22: sb.Append("hmoveto"); break;
                        case 23: sb.Append("NOP"); break;
                        case 24: sb.Append("NOP"); break;
                        case 25: sb.Append("NOP"); break;
                        case 26: sb.Append("NOP"); break;
                        case 27: sb.Append("NOP"); break;
                        case 28: sb.Append("NOP"); break;
                        case 29: sb.Append("NOP"); break;
                        case 30: sb.Append("vhcurveto"); break;
                        case 31: sb.Append("hvcurveto"); break;
                        default: sb.Append("Uknown: " + b); break;
                    }

                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        #endregion
    }
}
