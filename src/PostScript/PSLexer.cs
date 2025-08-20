#define USE_BUFFER //<-- Must be used since EexecStream can't handle seeking
//#define OLD_SWALLOW
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using System.Diagnostics;
using PdfLib.Read;

namespace PdfLib.PostScript
{
    /// <summary>
    /// Lexer that handles ASCII and Binary PostScript data. 
    /// 
    /// Note that the read functions consume a token while the
    /// get functions must be used with "setnexttoken"
    /// </summary>
    /// <remarks>
    /// This lexer is oriented towards working from a MemoryStream. 
    /// So there's no buffering or other cleverness. 
    ///  (This turned out to be a poor assumtion. PostScript will
    ///  also come in the form of embeded resources, but there is
    ///  a buffer now so I think this addressed well enough)
    /// 
    /// Note that unlike PDFLexer this class consume whitespace seperators*. 
    /// This is to make it easier to find binary data in a Postscript
    /// stream
    /// 
    /// * When a token is read, the seperator that ends the token
    ///   is consumed if it's a whitespace character.
    /// 
    /// There are also other minor differences from the PDFLexer.
    /// Note that PSLexer is based on a copy of the PDFLexer so 
    /// some comments may only be relevant for the latter.
    /// </remarks>
    public class PSLexer
    {
        #region Variables

#if USE_BUFFER

        /// <summary>
        /// Bufferes read data.
        /// </summary>
        /// <remarks>
        /// 8, 16 and 32 is all sensible values. Anything larger
        /// sees only small reductions in amount of data read.
        /// 
        /// However, perhaps a growing cache is better. With a
        /// "add byte" in the setnum, setkeyword, sethex/pdfstring
        /// and setname methods (they are the only ones that need it).
        /// 
        /// I sugest making a new SetNextChar that handles
        /// the chaching issue.
        /// 
        /// Comments can not be read out, so they don't need to
        /// do any caching, and even if they did one could use
        /// the "range" stuff and read it out that way.
        /// </remarks>
        const int BUFF_SIZE = 64;
        byte[] _buf = new byte[BUFF_SIZE];
        int _buf_pos = 0;

#endif

        /// <summary>
        /// The PostScript stream
        /// </summary>
        Stream _ps;

        /// <summary>
        /// Integer length of the stream
        /// </summary>
        /// <remarks>Two shorter than the stream as those bytes are in _cur and _next</remarks>
        readonly int _length;

        /// <summary>
        /// Start position of a token
        /// </summary>
        int _token_start_pos;

        /// <summary>
        /// End position of a token
        /// </summary>
        int _token_length;

        /// <summary>
        /// Position in the stream
        /// (Two behind _ps)
        /// </summary>
        int _pos;

#if OLD_SWALLOW
        /// <summary>
        /// If the last line feed was double
        /// </summary>
        /// <remarks>
        /// Must be manualy reset by functions that use it
        /// </remarks>
        bool _double_nl;
#endif

        /// <summary>
        /// Current character
        /// </summary>
        private Chars _cur;

        /// <summary>
        /// Next character
        /// </summary>
        private Chars _next;

        #endregion

        #region Properties

        public bool DecryptStream
        {
            get { return false; }
            set
            {

                if (value)
                {
                    if (!(_ps is PostScript.Internal.EexecStream))
                    {
                        //Set the position back
                        _ps.Position = Position;

                        _ps = new PostScript.Internal.EexecStream(_ps);

                        //Fixes positional issues
                        Position = (int)_ps.Position;
                    }
                }
                else
                {
                    if (_ps is PostScript.Internal.EexecStream)
                    {
                        _ps = ((PostScript.Internal.EexecStream)_ps).InnerStream;
                        _ps.Position = Position;
                        Position = (int) _ps.Position;
                    }
                }
            }
        }

        /// <summary>
        /// Gets current token as a string
        /// </summary>
        /// <remarks>
        /// When translated to characters the bytes in the PDF stream
        /// should be treated as if they were UTF8 encoded.
        /// (PDF Specs 7.3.5)
        /// 
        /// But is it nessesary? All tokens use from the UTF7 subset.
        /// A for loop with (char) will be faster.
        /// </remarks>
        public string Token
        {
            [DebuggerStepThrough()]
            get
            {
#if USE_BUFFER
                return Lexer.GetString(_buf, 0, _token_length);
#else
                long pos = _ps.Position;
                _ps.Position = _token_start_pos;
                var bytes = new byte[_token_length];
                _ps.Read(bytes, 0, bytes.Length);
                _ps.Position = pos;
                string tok = Lexer.GetString(bytes);//Encoding.UTF8.GetString(bytes);
                return tok;
#endif
            }
        }

        /// <summary>
        /// Returns the raw byte values of the token
        /// </summary>
        public byte[] RawToken
        {
            get
            {
#if USE_BUFFER
                var by = new byte[_token_length];
                Buffer.BlockCopy(_buf, 0, by, 0, _token_length);
                return by;
#else
                long pos = _ps.Position;
                _ps.Position = _token_start_pos;
                var bytes = new byte[_token_length];
                _ps.Read(bytes, 0, bytes.Length);
                _ps.Position = pos;
                return bytes;
#endif
            }
        }

        /// <summary>
        /// Gets or sets the position in the stream
        /// </summary>
        public int Position
        {
            get { return _pos; }
            set
            {
                _pos = value;
                _ps.Position = value;
#if OLD_SWALLOW
                _double_nl = false;
#endif
                if (_pos < _length)
                {
                    _cur = (Chars)_ps.ReadByte();
                    _next = (Chars)_ps.ReadByte();
                }
            }
        }

        #endregion

        #region Init an dispose

        public PSLexer(string str)
            : this(Lexer.GetBytes(str)) { }

        public PSLexer(byte[] data)
            : this(new MemoryStream(data), data.Length)
        { }

        public PSLexer(byte[] data, int length)
            : this(new MemoryStream(data), length)
             { }

        /// <param name="ps_stream">Seekable PDF formated stream</param>
        /// <param name="length">How long to read into the stream</param>
        public PSLexer(Stream ps_stream, int length) 
        {
            if (!ps_stream.CanSeek)
                throw new PdfInternalException(SR.SeekStream);
            
            _ps = ps_stream;
            _length = length - 2;
            Position = 0;
        }

        /// <summary>
        /// Closes the underlying stream.
        /// </summary>
        internal void Close()
        {
            if (_ps != null) _ps.Close();
            _ps = null;
        }

        #endregion

        #region Public set methods

        /// <summary>
        /// Looks for the next token in the stream.
        /// </summary>
        /// <returns>The type of token that was found</returns>
        //[DebuggerStepThrough]
        public PSType SetNextToken()
        {
        Redo:
#if USE_BUFFER
            //Shrinks the buffer
            if (_buf.Length > BUFF_SIZE)
            {
                _buf = new byte[BUFF_SIZE];
                _buf_pos = 0;
            }
#endif
            SkipWhiteSpace();

            switch (_cur)
            {
                case Chars.Percent:
                    SetComment();
                    goto Redo; //Skips comments

                case Chars.Slash:
#if USE_BUFFER
                    _buf_pos = 0;
#endif
                    SetName();
                    return PSType.Name;

                case Chars.Minus:
                    if (!Lexer.IsDigit(_next))
                        break;
                    goto case Chars.Pluss;
                case Chars.Pluss:
                case Chars.Period:
#if USE_BUFFER
                    _buf_pos = 1;
                    _buf[0] = (byte)_cur;
#endif
                    return SetNum(_cur == Chars.Period);

                case Chars.ParenLeft:
                    return SetLiteralString();

                case Chars.Less: // <
#if USE_BUFFER
                    _buf_pos = 0;
#endif
                    SetNextChar();
                    if (_cur == Chars.Less)
                    {
                        SetNextChar();
                        return PSType.BeginDictionary;
                    }
                    //Todo <~
                    return SetHexString();

                case Chars.Greater: // >
                    SetNextChar();
                    if (_cur == Chars.Greater)
                    {
                        //Note, next char can be whatever it wants
                        SetNextChar();
                        return PSType.EndDictionary;
                    }
                    goto unexpected_char; // SR.Unexpected(">");

                case Chars.BracketLeft: // [
                    SetNextChar();
                    return PSType.BeginArray;
                case Chars.BracketRight: // ]
                    SetNextChar();
                    return PSType.EndArray;

                case Chars.BraceLeft: // {
                    SetNextChar();
                    return PSType.BeginProcedure;
                case Chars.BraceRight: // }
                    SetNextChar();
                    return PSType.EndProcedure;
            }

#if USE_BUFFER
            _buf_pos = 1;
            _buf[0] = (byte)_cur;
#endif

            if (Lexer.IsDigit(_cur))
                return SetNum(false);

            //7.8.2 Content Streams:
            //keyword shall be distinguished from a name object by the absence of an initial 
            //SOLIDUS character (2Fh) (/ ). 
            // All other keywords are "letters only".
            if (Lexer.IsLetter(_cur) || _cur == Chars.QuoteSingle || _cur == Chars.QuoteDbl)
            {
                SetKeyword(); //<-- Does not handle * and other such characters
                return PSType.Keyword;
            }

            if (_cur == Chars.EOF)
                return PSType.EOF;

            //Keyword opperators are apparently allowed in Type3 fonts. Not 100% sure
            //on this point.
            SetOpKeyword();
            return PSType.Keyword;

        unexpected_char:
            var cur = _cur;
            SetNextChar(); //<-- Force forward progress
            //return PdfType.UnexpectedChar;
            throw new NotImplementedException("Keyword char: " + ((char)cur));
        }

        #endregion

        #region Private set methods

        /// <summary>
        /// Set literal string.
        /// </summary>
        PSType SetLiteralString()
        {
            _token_start_pos = _pos + 1;
#if USE_BUFFER
            _buf_pos = 0;
#endif

            //Counts the parenthesis
            int par_count = 1;
            while (_cur != Chars.EOF)
            {
                //Idea: Have this function test for \, #, and <
                //      if any occur, flag as "must convert" string.
                //      Combine this with removing PdfType.HexString

                if (_next == Chars.ParenLeft)
                {
                    if (_cur != Chars.BackSlash)
                        par_count++;
                }
                else if (_next == Chars.ParenRight)
                {
                    if (_cur != Chars.BackSlash)
                    {
                        par_count--;
                        if (par_count == 0)
                        {
                            SetNextChar();
                            _token_length = _pos - _token_start_pos;
                            SetNextChar();
                            return PSType.String;
                        }
                    }
                }
                SetNextChar();
            }
            throw new PSLexerException(PSType.String, ErrCode.UnexpectedEOF);
        }

        /// <summary>
        /// Sets a string, searcing for the end character
        /// </summary>
        /// <param name="end">Character to end the string width</param>
        PSType SetHexString()
        {
            _token_start_pos = _pos;
            while (_cur != Chars.EOF)
            {
                if (_cur == Chars.Greater)
                {
                    _token_length = _pos - _token_start_pos;
                    SetNextChar();
                    return PSType.HexString;
                }
                SetNextChar();
            }
            throw new PSLexerException(PSType.HexString, ErrCode.UnexpectedEOF);
        }

        /// <summary>
        /// Todo: Radix numbers (3.2.2 in the PostScript specs)
        /// </summary>
        [DebuggerStepThrough]
        PSType SetNum(bool period)
        {
            bool real = false;
            //_double_nl = false;

            _token_start_pos = _pos;
            SetNextChar();
            if (period)
                real = true;
            else
            {
                if (_cur == Chars.Pluss || _cur == Chars.Minus)
                    SetNextChar();
            }
            while (true)
            {
                if (_cur == Chars.Period)
                {
                    if (real) throw new PSLexerException(PSType.Number, ErrCode.CorruptToken);
                    real = true;
                }
                else if (!Lexer.IsDigit(_cur) || _cur == Chars.EOF)
                {
                    _token_length = _pos - _token_start_pos;
#if OLD_SWALLOW
                    //Must correct for double line endings (\r\n)
                    if (_cur == Chars.LF && _double_nl) _token_length--;
#endif

                    break;
                }
                SetNextChar();
            }
            if (Lexer.IsWhiteSpace(_cur)) SetNextChar();
            return (real) ? PSType.Real : PSType.Integer;
        }

        /// <summary>
        /// Sets an opperator keyword
        /// </summary>
        /// <remarks>
        /// Not sure if this is to the specs, but it's needed for some type 3 fonts
        /// using |, -| and |-
        /// </remarks>
        void SetOpKeyword()
        {
            _token_start_pos = _pos;
            SetNextChar();
            while (!Lexer.IsWhiteSpace(_cur) && _cur != Chars.EOF)
                SetNextChar();
            _token_length = _pos - _token_start_pos;
            if (Lexer.IsWhiteSpace(_cur)) SetNextChar();
        }

        /// <summary>
        /// Sets a letter only keyword
        /// </summary>
        /// <remarks>Most keywords are just letters</remarks>
        void SetKeyword()
        {
            //_double_nl = false;
            _token_start_pos = _pos;
            SetNextChar();
            while (true)
            {
                if (!Lexer.IsLetter(_cur) && _cur != Chars.Asterisk || _cur == Chars.EOF)
                {
                    _token_length = _pos - _token_start_pos;

#if OLD_SWALLOW
                    //Must correct for double line endings (\r\n)
                    if (_cur == Chars.LF && _double_nl) _token_length--;
#endif
                    if (Lexer.IsWhiteSpace(_cur)) SetNextChar();
                    return;
                }
                SetNextChar();
            }
        }

        /// <summary>
        /// A name object is an atomic symbol uniquely defined by a sequence of any characters 
        /// (8-bit values) except null (character code 0).
        /// </summary>
        void SetName()
        {
            //_double_nl = false;
            SetNextChar();
            _token_start_pos = _pos;
            while (true)
            {
                if (Lexer.IsWhiteSpace(_cur) || Lexer.IsDelimiter(_cur) || _cur == Chars.EOF)
                {
                    _token_length = _pos - _token_start_pos;

                    //Must correct for double line endings (\r\n)
#if OLD_SWALLOW
                    if (_cur == Chars.LF && _double_nl) _token_length--;
#endif
                    if (Lexer.IsWhiteSpace(_cur)) SetNextChar();
                    return;
                }
                SetNextChar();
            }
        }

        /// <summary>
        /// Sets comment position markers
        /// </summary>
        /// <remarks>
        /// Any occurrence of the PERCENT SIGN (25h) outside a string 
        /// or stream introduces a comment. 
        /// 
        /// The comment consists of all characters after the PERCENT 
        /// SIGN and up to but not including the end of the line.
        /// </remarks>
        void SetComment()
        {
            //_double_nl = false;
            SetNextChar();
            _token_start_pos = _pos;
            while(true){
                if (_cur == Chars.LF || _cur == Chars.CR || _cur == Chars.EOF)
                {
                    _token_length = _pos - _token_start_pos;

                    //Must correct for double line endings (\r\n)
#if OLD_SWALLOW
                    if (_cur == Chars.LF && _double_nl) _token_length--;
#endif
                    SetNextChar();
                    return;
                }
                SetNextChar();
            }
        }

        #endregion

        #region Token methods

        /// <summary>
        /// Returns the token as an integer
        /// </summary>
        public int GetInteger()
        {
            return int.Parse(Token);
        }

        /// <summary>
        /// Returns the token as a real
        /// </summary>
        public double GetReal()
        {
            return double.Parse(Token, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets the current token as a name
        /// </summary>
        public string GetName()
        {
            string name = Token;
            int i = name.IndexOf('#');
            if (i != -1)
            {
                int p = 0;
                var sb = new StringBuilder(name.Length);
                do
                {
                    sb.Append(name.Substring(p, i++ - p));
                    if (i + 1 >= name.Length)
                        throw new PSReadException(PSType.Name, ErrCode.CorruptToken);
                    sb.Append((char)int.Parse(name.Substring(i, 2), NumberStyles.AllowHexSpecifier));
                    i += 2;
                    p = i;
                } while ((i = name.IndexOf('#', i)) != -1);
                if (p < name.Length)
                    sb.Append(name.Substring(p));
                return sb.ToString();
            }
            return name;
        }

        /// <summary>
        /// Gets the current token as hex
        /// </summary>
        /// <returns>Hex values converted into a byte array</returns>
        public byte[] GetHex()
        {
            byte[] hex = RawToken;
            byte[] ba = new byte[hex.Length / 2];

            for (int c = 0, ba_pos = 0; c < hex.Length; )
            {
                //Reads out a hex byte
                var b = (byte)(hex[c++] - 48);
                if (b > 9) b -= 7;
                if (b > 41) b -= 32;
                if (b < 0 || b > 15) throw new PSLexerException(PSType.Integer, ErrCode.Invalid);
                byte num = (byte)(b << 4);
                if (c == hex.Length)
                    throw new PSLexerException(PSType.Integer, ErrCode.Invalid);
                b = (byte)(hex[c++] - 48);
                if (b > 9) b -= 7;
                if (b > 41) b -= 32;
                if (b < 0 || b > 15) throw new PSLexerException(PSType.Integer, ErrCode.Invalid);
                num |= b;

                ba[ba_pos++] = num;
            }
            return ba;
        }

        #endregion

        #region Char methods

        //[DebuggerStepThrough]
        void SetNextChar()
        {
#if !OLD_SWALLOW
            //Swallows \r\n
            //A \r\n sequence may be part of binary data, and if \r\n is at the
            //start of the data we don't want it swallowed. This workaround delays
            //the swalloing a bit, with the drawback that functions now must test
            //against /r too. 
            //
            //Note, this method isn't used to read binary data (look at ReadByte()
            //for that), so it's safe to swallow /r/n... just not ahead of time.
            if (_cur == Chars.CR && _next == Chars.LF && _pos < _length)
            {
                _next = (Chars)_ps.ReadByte();
                _pos++;
            }
#endif
            _cur = _next;
            _pos++;
            if (_pos > _length)
            {
                _next = Chars.EOF;
                if (_pos > _length + 2)
                    _pos = _length + 2;
            }
            else
                _next = (Chars)_ps.ReadByte();

#if OLD_SWALLOW
            //Swallows \r\n
            //This code breaks 1211.6429.pdf
            if (_cur == Chars.CR)
            {
                _cur = Chars.LF;
                if (_next == Chars.LF)
                {
                    _next = (Chars)_ps.ReadByte();
                    _pos++;
                    _double_nl = true;
                }
            }
#endif
#if USE_BUFFER
            if (_buf_pos >= _buf.Length)
            {
                var buf = new byte[_buf.Length + BUFF_SIZE];
                Buffer.BlockCopy(_buf, 0, buf, 0, _buf.Length);
                _buf = buf;
            }
            _buf[_buf_pos++] = (byte)_cur;
#endif
        }

        [DebuggerStepThrough]
        void SkipWhiteSpace()
        {
            while (Lexer.IsWhiteSpace(_cur))
                SetNextChar();
        }

        #endregion

        #region Read methods

        /// <summary>
        /// Reads a int token straight out of the stream
        /// </summary>
        public int ReadInt()
        {
            if (SetNextToken() != PSType.Integer)
                throw new PSLexerException(PSType.Integer, ErrCode.WrongType);
            return GetInteger();
        }

        /// <summary>
        /// Reads out a hex token encoded inside angle brackets (< >)
        /// </summary>
        /// <returns>Hex values converted into a byte array</returns>
        public byte[] ReadHex()
        {
            SkipWhiteSpace();
            if (_cur != Chars.Less)
                throw new PSLexerException(PSType.Integer, ErrCode.Invalid);
            SetNextChar();
            var ba = new byte[1];
            var ba_pos = 0;

            while (_cur != Chars.Greater)
            {
                //Reads out a hex byte
                var b = (byte)(_cur - 48);
                if (b > 9) b -= 7;
                if (b > 41) b -= 32;
                if (b < 0 || b > 15) throw new PSLexerException(PSType.Integer, ErrCode.Invalid);
                byte num = (byte) (b << 4);
                SetNextChar();
                b = (byte)(_cur - 48);
                if (b > 9) b -= 7;
                if (b > 41) b -= 32;
                if (b < 0 || b > 15) throw new PSLexerException(PSType.Integer, ErrCode.Invalid);
                num |= b;
                SetNextChar();

                if (ba_pos == ba.Length)
                    Array.Resize<byte>(ref ba, ba.Length * 2);
                ba[ba_pos++] = num;
            }
            SetNextChar();
            return ba;
        }

        /// <summary>
        /// Reads a byte from the underlying stream
        /// </summary>
        public int ReadByte()
        {
            int ret = (int)_cur;
            _cur = _next;

            if (_pos == _length)
                _next = Chars.EOF;
            else
            {
                _pos++;
                _next = (Chars)_ps.ReadByte();
            }
            return ret;
        }

        public string ReadDebugData()
        {
            if (_ps is PdfLib.PostScript.Internal.EexecStream)
            {
                return ((PdfLib.PostScript.Internal.EexecStream)_ps).UnencryptedData;
            }
            else
            {
                if (_pos > _length)
                    return "End of stream";

                if (_ps.CanSeek)
                {
                    var buf = new byte[_length - _pos];
                    long pos = _ps.Position;
                    _ps.Position = _pos;
                    _ps.Read(buf, 0, buf.Length);
                    _ps.Position = pos;
                    return Lexer.GetString(buf);
                }

                return "Can't read data";
            }
        }

        #endregion

        #region debug
#if DEBUG
        public string DebugStr
        {
            get
            {
                var hold = _ps.Position;
                var size = (int)Math.Min(_ps.Length - hold, 4000);
                var ta = new byte[size];
                try { _ps.Read(ta, 0, size); }
                catch { _ps.Position = hold; }

                return System.Text.ASCIIEncoding.ASCII.GetString(ta);
            }
        }
#endif
#endregion
    }
}
