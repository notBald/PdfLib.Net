#define USE_BUFFER //This buffer may speed up lexing. May not. Something to test.
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Diagnostics;
using PdfLib.Pdf.Internal; //Imported for PdfType enum

namespace PdfLib.Read
{
    /// <summary>
    /// A lexical analysator for PDF files. 
    /// </summary>
    public class Lexer
    {
        #region Variables

        /// <summary>
        /// I'm getting errors when reading files on nettwork shares, when
        /// reading above 123569 bytes. So never read more that this
        /// at once.
        /// </summary>
        const int MAX_BUFFERED_READ = 32768;

#if USE_BUFFER

        /// <summary>
        /// Bufferes read data.
        /// </summary>
        /// <remarks>
        /// 8, 16 and 32 are all sensible values. Anything larger
        /// sees only small reductions in the amount of data read.
        /// 
        /// Keep in mind that the OS will cache files, so anything 
        /// more complex than this is likely a wasted effort. 
        /// 
        /// However, perhaps a growing cache is better. With a
        /// "add byte" in the setnum, setkeyword, sethex/pdfstring
        /// and setname methods (they are the only ones that need it).
        /// 
        /// I suggest making a new SetNextChar that handles
        /// the caching issue.
        /// 
        /// Comments can not be read out, so they don't need to
        /// do any caching, and even if they did one could use
        /// the "range" stuff and read it out that way.
        /// </remarks>
        byte[] _buf = new byte[32];
        int _buf_pos = 0;

#endif

        /// <summary>
        /// The PDF stream
        /// </summary>
        private Stream _pdf;

        /// <summary>
        /// If the last line feed was double
        /// </summary>
        /// <remarks>
        /// Must be manualy reset by functions that use it
        /// </remarks>
        bool _double_nl;

        /// <summary>
        /// Current character
        /// </summary>
        private Chars _cur;

        /// <summary>
        /// Next character
        /// </summary>
        private Chars _next;
#if LONGPDF
        /// <summary>
        /// Integer length of the stream
        /// </summary>
        /// <remarks>Two shorter than the stream</remarks>
        long _length;

        /// <summary>
        /// Position in the stream
        /// (Two behind _pdf)
        /// </summary>
        long _pos;

        /// <summary>
        /// Start position of a token
        /// </summary>
        long _token_start_pos;
#else
        /// <summary>
        /// Integer length of the stream
        /// </summary>
        /// <remarks>Two shorter than the stream</remarks>
        int _length;

        /// <summary>
        /// Position in the stream
        /// (Two behind _pdf)
        /// </summary>
        int _pos;

        /// <summary>
        /// Start position of a token
        /// </summary>
        int _token_start_pos;
#endif

        /// <summary>
        /// End position of a token
        /// </summary>
        int _token_length;


        /// <summary>
        /// DON'T USE! (and remeber to reset position
        /// if you do use it)
        /// </summary>
        /// <remarks>
        /// This is only intended for reading out inline image data from
        /// the lexer.
        /// 
        /// In those situations multi threading will never be an issue
        /// as the data gets copied out. 
        /// </remarks>
        internal Stream ContentsStream { get { return _pdf; } }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the keyword type.
        /// </summary>
        /// <remarks>Noet that boolean and null is also keywords</remarks>
        public PdfKeyword Keyword { get { return ToKeyword(Token); } }

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
            get
            {
#if USE_BUFFER
                if (_token_length <= _buf_pos)
                    return GetString(_buf, 0, _token_length);//Encoding.UTF8.GetString(_buf, 0, _token_length);
#endif

                long pos = _pdf.Position;
                _pdf.Position = _token_start_pos;
                var bytes = new byte[_token_length];
                if (_pdf.Read(bytes, 0, bytes.Length) != _token_length)
                    throw new PdfReadException(ErrSource.Lexer, PdfType.None, ErrCode.UnexpectedEOD);
                _pdf.Position = pos;
                string tok = GetString(bytes);//Encoding.UTF8.GetString(bytes);
                return tok;
            }
        }

        /// <summary>
        /// Length of current token
        /// </summary>
        public int TokenLength
        {
            get { return _token_length; }
        }

        /// <summary>
        /// Returns the raw byte values of the token
        /// </summary>
        public byte[] RawToken
        {
            get
            {
#if USE_BUFFER
                if (_token_length <= _buf_pos)
                {
                    var by = new byte[_token_length];
                    Buffer.BlockCopy(_buf, 0, by, 0, _token_length);
                    return by;
                }
#endif

                long pos = _pdf.Position;
                _pdf.Position = _token_start_pos;
                var bytes = new byte[_token_length];
                _pdf.Read(bytes, 0, bytes.Length);
                _pdf.Position = pos;
                return bytes;
            }
        }

        /// <summary>
        /// Returns the character range.
        /// </summary>
        public rRange ByteRange
        {
            get { return new rRange(_token_start_pos, _token_length); }
        }

        /// <summary>
        /// Gets or sets the position in the stream
        /// </summary>
#if LONGPDF
        public long Position
#else
        public int Position
#endif
        {
            get { return _pos; }
            set
            {
                _pos = value;
                _pdf.Position = value;
                _double_nl = false;
                _cur = (Chars) _pdf.ReadByte();
                _next = (Chars) _pdf.ReadByte();
            }
        }

        /// <summary>
        /// Gets the length of the PDF document.
        /// </summary>
        public int Length { get { return (int)_pdf.Length; } }

        /// <summary>
        /// If the current token is "End of file".
        /// </summary>
        public bool IsEOF { get { return _cur == Chars.EOF; } }

        /// <summary>
        /// If the lexer's stream is closed.
        /// </summary>
        internal bool IsClosed { get { return _pdf == null; } }

        #region Debug aid
#if DEBUG

        internal string DebugLines
        {
            get
            {
                var p = _pdf.Position;
                _pdf.Position -= 2;
                StreamReader sr = new StreamReader(_pdf);
                var sb = new StringBuilder();
                for(int c=0; c < 10; c++)
                    sb.AppendLine(sr.ReadLine());
                _pdf.Position = p;
                return sb.ToString();
            }
        }

        internal string DebugHelp
        {
            get
            {
                var p = _pdf.Position;
                _pdf.Position = 0;
                StreamReader sr = new StreamReader(_pdf);
                var ret = sr.ReadToEnd().Replace('\0', ' ');
                _pdf.Position = p;
                return ret;
            }
        }

#endif
        #endregion

        #endregion

        #region Init an dispose

        /// <summary>
        /// Constructor
        /// </summary>
        public Lexer(byte[] data)
            : this(new MemoryStream(data))
             { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pdf_stream">Seekable PDF formated stream</param>
        public Lexer(Stream pdf_stream) 
        {
            if (!pdf_stream.CanSeek)
                throw new PdfInternalException(SR.SeekStream);
            
            _pdf = pdf_stream;
            _length = (int)_pdf.Length - 2;
            Position = 0;
        }

        /// <summary>
        /// Closes the underlying stream.
        /// </summary>
        internal void Close()
        {
            if (_pdf != null) _pdf.Close();
            _pdf = null;
        }

        /// <summary>
        /// Replaces the underlying stream, if it's null
        /// </summary>
        /// <param name="s">Stream to use from now on</param>
        internal void Reopen(Stream s)
        {
            if (_pdf == null)
                _pdf = s;
            _length = (int)_pdf.Length - 2;
            Position = 0;
        }

        #endregion

        #region Public set methods

        /// <summary>
        /// Looks for the next token in the stream.
        /// </summary>
        /// <returns>The type of token that was found</returns>
        [DebuggerStepThrough]
        public PdfType SetNextToken()
        {
        Redo:
            SkipWhiteSpace();

            switch (_cur)
            {
                case Chars.Percent:
                    SetComment();
                    goto Redo; //Skip comments

                case Chars.Slash:
#if USE_BUFFER
                    _buf_pos = 0;
#endif
                    SetName();
                    return PdfType.Name;

                case Chars.Pluss:
                case Chars.Minus:
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
                        //Note, next char can be whatever it wants
                        SetNextChar();
                        return PdfType.BeginDictionary;
                    }
                    return SetHexString();

                case Chars.Greater: // >
                    SetNextChar();
                    if (_cur == Chars.Greater)
                    {
                        //Note, next char can be whatever it wants
                        SetNextChar();
                        return PdfType.EndDictionary;
                    }
                    goto unexpected_char; // SR.Unexpected(">");

                case Chars.BracketLeft: // [
                    SetNextChar();
                    return PdfType.BeginArray;
                case Chars.BracketRight: // ]
                    SetNextChar();
                    return PdfType.EndArray;

            }

#if USE_BUFFER
            _buf_pos = 1;
            _buf[0] = (byte)_cur;
#endif

            if (IsDigit(_cur))
                return SetNum(false);
            
            //7.8.2 Content Streams:
            //keyword shall be distinguished from a name object by the absence of an initial 
            //SOLIDUS character (2Fh) (/ ). 
            // All other keywords are "letters only".
            if (IsLetter(_cur) || _cur == Chars.QuoteSingle || _cur == Chars.QuoteDbl)
            {
                SetKeyword();
                return PdfType.Keyword;
            }

            if (_cur == Chars.EOF)
                return PdfType.EOF;

        unexpected_char:
            var cur = _cur;
            SetNextChar(); //<-- Insure forward progress
            //return PdfType.UnexpectedChar;
            throw new PdfLexerException(ErrCode.UnexpectedChar, cur.ToString());
        }

        #endregion

        #region Private set methods

        /// <summary>
        /// Set literal string.
        /// </summary>
        PdfType SetLiteralString()
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
                SetNextChar();

                if (_cur == Chars.BackSlash)
                {
                    //Skips over the next char
                    SetNextChar();
                }
                else
                {
                    if (_cur == Chars.ParenLeft)
                        par_count++;
                    else if (_cur == Chars.ParenRight)
                    {
                        par_count--;
                        if (par_count == 0)
                        {
                            _token_length = (int) (_pos - _token_start_pos);
                            SetNextChar();
                            return PdfType.String;
                        }
                    }
                }
            }
            throw new PdfLexerException(PdfType.String, ErrCode.UnexpectedEOF);
        }

        /// <summary>
        /// Sets a string, searcing for the end character
        /// </summary>
        /// <param name="end">Character to end the string width</param>
        PdfType SetHexString()
        {
            _token_start_pos = _pos;
            while (_cur != Chars.EOF)
            {
                if (_cur == Chars.Greater)
                {
                    _token_length = (int) (_pos - _token_start_pos);
                    SetNextChar();
                    return PdfType.HexString;
                }
                SetNextChar();
            }
            throw new PdfLexerException(PdfType.HexString, ErrCode.UnexpectedEOF);
        }

        /// <summary>
        /// Only for use by the parser.
        /// </summary>
        internal PdfType SetNumNext()
        {
#if USE_BUFFER
            _buf_pos = 1;
            _buf[0] = (byte) _cur;
#endif
            return SetNum(_cur == Chars.Period);
        }

        /// <remarks>Only call this if PeekNextItem gives "number"</remarks>
        //[DebuggerStepThrough]
        PdfType SetNum(bool period)
        {
            bool real = false;
            _double_nl = false;
            var first_char = _cur;

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
                    if (real) throw new PdfLexerException(PdfType.Number, ErrCode.CorruptToken);
                    real = true;
                } 
                else if (!IsDigit(_cur) || _cur == Chars.EOF)
                {
                    _token_length = (int) (_pos - _token_start_pos);

                    //Must correct for double line endings (\r\n)
                    if (_cur == Chars.LF && _double_nl) _token_length--;

                    break;
                }
                SetNextChar();
            }

            if (real) return PdfType.Real;

            if (_token_length >= 10)
            {
                if (_token_length == 10 && first_char <= Chars.d2)
                {
                    //Subtlety: If the first character is + or -, we also end up here

                    if (first_char == Chars.d2 && !int.TryParse(Token, out _))
                        return PdfType.Long;
                }
                else return PdfType.Long;
            }

            return PdfType.Integer;
        }

        /// <summary>
        /// Sets a letter only keyword
        /// </summary>
        /// <remarks>Most keywords are just letters</remarks>
        void SetKeyword()
        {
            _double_nl = false;
            _token_start_pos = _pos;
            SetNextChar();
            while (true)
            {
                if (!IsLetter(_cur) && !IsDigit(_cur) && _cur != Chars.Asterisk || _cur == Chars.EOF)
                {
                    _token_length = (int) (_pos - _token_start_pos);

                    //Must correct for double line endings (\r\n)
                    if (_cur == Chars.LF && _double_nl) _token_length--;
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
            _double_nl = false;
            SetNextChar();
            _token_start_pos = _pos;
            while (true)
            {
                if (IsWhiteSpace(_cur) || IsDelimiter(_cur) || _cur == Chars.EOF)
                {
                    _token_length = (int) (_pos - _token_start_pos);

                    //Must correct for double line endings (\r\n)
                    if (_cur == Chars.LF && _double_nl) _token_length--;
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
            _double_nl = false;
            SetNextChar();
            _token_start_pos = _pos;
            while(true){
                if (_cur == Chars.LF || _cur == Chars.EOF)
                {
                    _token_length = (int) (_pos - _token_start_pos);

                    //Must correct for double line endings (\r\n)
                    if (_cur == Chars.LF && _double_nl) _token_length--;
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
                        throw new PdfLexerException(PdfType.Name, ErrCode.CorruptToken);
                                                    //string.Format(SR.CorruptName, name));}
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
        /// For handeling \r\n.
        /// </summary>
        /// <returns>0 for no line feed, 1 for \n and 2 for \r\n</returns>
        internal int GetLineFeedCount()
        {
            if (_cur == Chars.LF)
                return 1;
            if (_cur == Chars.CR && _next == Chars.LF)
                return 2;
            return 0;
        }

        /// <summary>
        /// Reads a stream from the dictionary
        /// </summary>
        /// <param name="length">The length of the stream</param>
        /// <returns>the stream</returns>
        internal rRange GetStreamRange(int length)
        {
            int start = 0;

            //After a stream keyword there's a \n or \r\n. No
            //other characters are allowed.
            if (_cur == Chars.CR)
            {
                if (_next != Chars.LF)
                {
                    //guidelines_for_smes_1275_2008_okt_09.pdf
                    //This isn't a problem per se, for the reader, but if a writer use CR when saveing streams,
                    //then streams that coincidentally start will LF will have it's first byte skipped.
                    Log.Warn(ErrSource.Stream, WarnType.SolitaryCR);
                    //throw new PdfLexerException(PdfType.Stream, ErrCode.CorruptToken);// SR.CorruptStream);
                    if (length == 0)
                    {
                        SetNextChar(); //Skips CR
                        return new rRange(_pos, 0);
                    }
                    start = 1;
                }
            }
            else
            {
                if (_cur != Chars.LF)
                    throw new PdfLexerException(PdfType.Stream, ErrCode.CorruptToken);
                if (length == 0)
                {
                    SetNextChar(); //Skips LF
                    return new rRange(_pos, 0);
                }
                start = 1;
            }
            //The position from where the stream starts.
#if LONGPDF
            long stream_pos = _pos + start;
#else
            int stream_pos = _pos + start;
#endif

            //Know at this point that _cur and _next are handeled

            //Seeks past the stream. Keep in mind that when start is 1
            //it means that one character has already been read, thus
            //we have to seek one less character
            int new_pos = (int) _pdf.Seek(length - start, SeekOrigin.Current);
            
            //Moves the parser position.
            Position = new_pos;

            //Check if all the data was read.
            if (new_pos - stream_pos != length)
                throw new PdfLexerException(PdfType.Stream, ErrCode.CorruptToken);

            return new rRange(stream_pos, length);
        }

        /// <summary>
        /// Translates a string to a keyword
        /// </summary>
        public static PdfKeyword ToKeyword(string token)
        {
            switch (token)
            {
                case "obj":
                    return PdfKeyword.Obj;

                case "endobj":
                    return PdfKeyword.EndObj;

                case "stream":
                    return PdfKeyword.BeginStream;

                case "endstream":
                    return PdfKeyword.EndStream;

                case "true":
                case "false":
                    return PdfKeyword.Boolean;

                case "null":
                    return PdfKeyword.Null;

                case "R":
                    return PdfKeyword.R;

                case "xref":
                    return PdfKeyword.XRef;

                case "trailer":
                    return PdfKeyword.Trailer;

                case "startxref":
                    return PdfKeyword.StartXRef;

                default:
                    return PdfKeyword.None;
            }
        }

        #endregion

        #region Read data from the stream methods

        /// <summary>
        /// Skips n number of whitespace characters
        /// </summary>
        /// <param name="n">Number of whitespace characters to skip (\r\n is counted as one character)</param>
        /// <param name="require">Whenever to require that number of whitespace characters or not</param>
        internal void SkipWhite(int n, bool require)
        {
            while (n-- > 0)
            {
                //The next character is expected to be a whitespace character
                if (!IsWhiteSpace(_cur))
                {
                    if (require)
                        throw new PdfLexerException(ErrCode.UnexpectedChar, "" + _cur + " (Expected whitespace)");
                    return;
                }

                //Swallows \r\n
                if (_cur == Chars.CR && _next == Chars.LF)
                    SetNextChar();

#if USE_BUFFER
                //Clears the buffer
                _buf_pos = 0;
#endif

                SetNextByte();
            }
        }

        /// <summary>
        /// This is a function used for reading out inline image streams
        /// </summary>
        internal void ReadToEI()
         {
#if USE_BUFFER
            _buf_pos = 1;
            _buf[0] = (byte)_cur;
#endif

            _token_start_pos = _pos;
            do { SetNextByte(); }
            while ((_cur != Chars.E || _next != Chars.I) && _cur != Chars.EOF);
            _token_length = (int) (_pos - _token_start_pos);
        }

        /// <summary>
        /// Reads a single raw byte
        /// </summary>
        /// <remarks>
        /// Used for reading out inline image data.
        /// </remarks>
        internal int ReadByte()
        {
            var ret = _cur;
            SetNextByte();
            return (int) ret;
        }

        /// <summary>
        /// Reads n number of bytes out of the stream and keeps the new position.
        /// </summary>
        /// <remarks>
        /// Used for reading out data for inline images.
        /// </remarks>
        internal byte[] ReadRaw(int size)
        {
            var current_pos = Position;
            var new_pos = current_pos + size;
            if (new_pos >= _pdf.Length)
            {
                new_pos = (int)_pdf.Length;
                size = (int) Math.Max(0, new_pos - current_pos);
            }

            var bytes = new byte[size];
            Read(bytes, (int) current_pos);
            Position = new_pos;
            return bytes;
        }

        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset from the beginning of the stream</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        internal int Read(byte[] buffer, long offset)
#else
        internal int Read(byte[] buffer, int offset)
#endif
        {
            if (_pdf == null)
                throw new PdfStreamClosedException();
            var pos = _pdf.Position;
            _pdf.Position = offset;

            int nRead = 0;
            for (int nToRead = buffer.Length; nToRead > 0; )
            {
                int read = _pdf.Read(buffer, nRead, (nToRead > MAX_BUFFERED_READ) ? MAX_BUFFERED_READ : nToRead);
                if (read == 0) break;

                nRead += read;
                nToRead -= read;
            }

            _pdf.Position = pos;
            return nRead;
        }

        
        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset from the beginning of the stream</param>
        /// <param name="count">How many bytes to read</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        internal int Read(byte[] buffer, long offset, int count)
#else        
        internal int Read(byte[] buffer, int offset, int count)
#endif
        {
            if (_pdf == null)
                throw new PdfStreamClosedException();

            var pos = _pdf.Position;
            _pdf.Position = offset;

            int nRead = 0;
            for (int nToRead = buffer.Length; nToRead > 0; )
            {
                int read = _pdf.Read(buffer, nRead, (nToRead > MAX_BUFFERED_READ) ? MAX_BUFFERED_READ : nToRead);
                if (read == 0) break;

                nRead += read;
                nToRead -= read;
            }
            _pdf.Position = pos;

            return nRead;
        }

        #endregion

        #region Char methods

        /// <summary>
        /// Same as SetNextChar, but does not skip whitespace
        /// </summary>
        [DebuggerStepThrough]
        void SetNextByte()
        {
            _cur = _next;
            _next = (Chars)_pdf.ReadByte();
            _pos++;
            Debug.Assert(_pos + 2 == _pdf.Position);

#if USE_BUFFER
            if (_buf_pos < _buf.Length)
                _buf[_buf_pos++] = (byte)_cur; ;
#endif
        }

        [DebuggerStepThrough]
        void SetNextChar()
        {
            _cur = _next;
            _next = (Chars)_pdf.ReadByte();
            _pos++;
            //Swallows \r\n
            if (_cur == Chars.CR)
            {
                _cur = Chars.LF;
                if (_next == Chars.LF)
                {
                    _next = (Chars)_pdf.ReadByte();
                    _pos++;
                    _double_nl = true;
                }
            }
#if USE_BUFFER
            if (_buf_pos < _buf.Length)
                _buf[_buf_pos++] = (byte)_cur; ;
#endif
        }

        [DebuggerStepThrough]
        void SkipWhiteSpace()
        {
            while (IsWhiteSpace(_cur))
                SetNextChar();
        }

        [DebuggerStepThrough]
        internal static bool IsLetter(Chars b)
        {
            return Chars.a <= b && b <= Chars.z ||
                   Chars.A <= b && b <= Chars.Z;
        }

        [DebuggerStepThrough]
        internal static bool IsHex(Chars b)
        {
            return Chars.a <= b && b <= Chars.z ||
                   Chars.A <= b && b <= Chars.Z ||
                   Chars.d0 <= b && b <= Chars.d9;
        }

        /// <summary>
        /// Checks if the character is a digit
        /// </summary>
        [DebuggerStepThrough]
        internal static bool IsDigit(Chars b)
        {
            return Chars.d0 <= b && b <= Chars.d9;
        }

        /// <summary>
        /// Checks if this char marks the end of a token.
        /// </summary>
        [DebuggerStepThrough]
        internal static bool IsTokSep(Chars b)
        {
            return IsWhiteSpace(b) || IsDelimiter(b) || b == Chars.EOF;
        }

        /// <summary>
        /// Checks if a character is whitespace
        /// </summary>
        /// <remarks>PDF specs 7.2.2</remarks>
        [DebuggerStepThrough]
        internal static bool IsWhiteSpace(Chars b)
        {
            switch (b)
            {
                case Chars.NUL:   // Null
                case Chars.Tab:   // Tab
                case Chars.LF:    // Line feed
                case Chars.FF:    // Form feed
                case Chars.CR:    // Carriage return
                case Chars.Space: // Space
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if a character is a delimiter
        /// </summary>
        /// <remarks>PDF sepcs 7.2.2</remarks>
        [DebuggerStepThrough]
        internal static bool IsDelimiter(Chars b)
        {
            switch (b)
            {
                case Chars.ParenLeft:    // (
                case Chars.ParenRight:   // )
                case Chars.Less:         // <
                case Chars.Greater:      // >
                case Chars.BracketLeft:  // [
                case Chars.BracketRight: // ]
                case Chars.BraceLeft:    // {
                case Chars.BraceRight:   // }
                case Chars.Slash:        // /
                case Chars.Percent:      // %
                    return true;

                default:
                    return false;
            }
        }

        #endregion

        #region Parser helper methods
        //These methods are spesifically tailored for the parser.

        /// <summary>
        /// Usefull for a quick peek on the next characters
        /// </summary>
        /// <param name="cur">First char of the keyword</param>
        /// <param name="next">Second char of the keyword</param>
        /// <remarks>
        /// I prefere to do as little caching as I can get away
        /// with, but at the same time I don't want to set the
        /// lexer/parser position too often. 
        /// 
        /// I cache numbers since they are quite common, while
        /// keywords tends to pass/fail the "quick tests" and
        /// not need resetting the stream too often (I think)
        ///  - To my knowlege this will actually never happen.
        /// </remarks>
        public bool PeekNextKeyword(Chars cur, Chars next, PdfKeyword kw)
        {
            SkipWhiteSpace(); 
            if (_cur == cur && _next == next)
            {   //This might be the desired keyword.
                
                //Saves the position
                var cpos = _pos;

                //Presumably "cur" will be a valid keyword character, so no
                //need to test for keyword - but no harm either.
                if (SetNextToken() == PdfType.Keyword && Keyword == kw)
                    return true;

                //I think I'm right in this assesment, but comment
                //out if not. (Though it can certanly happen in a
                //corrupt file.)
                Debug.Assert(false, "This should never happen");

                //Must set the stream back.
                Position = cpos;
            }

            return false;
        }

        /// <summary>
        /// When parsing references the parser needs to know when
        /// a reference is comming. To save the parser some work
        /// we special case this situation.
        /// </summary>
        /// <remarks>Next keyword is a 'R'</remarks>
        internal bool IsR() { return _cur == Chars.R && (IsWhiteSpace(_next) || IsDelimiter(_next)); }

        /// <summary>
        /// Checks if the next token is a object. (obj)
        /// </summary>
        /// <remarks>
        /// If true the stream is moved beyond the object
        /// 
        /// Note this function is similar to PeekNextKeyword,
        /// but is used by the parser only.
        /// </remarks>
        internal bool IsObj()
        {
            if (_cur != Chars.o || _next != Chars.b)
                return false;

            //Known that this is "ob"
            var cpos = _pos;


            SetNextChar();
            SetNextChar();
            if (_cur == Chars.j && (IsWhiteSpace(_next) || IsDelimiter(_next)))
            {
                SetNextChar();
                return true;
            }

            //Not an object, reseting the stream
            Debug.Assert(false, "I suspect this will never happen in non-corrupt files",
                "This since there's no command nor keyword that starts with 'ob'");

            Position = cpos;
            return false;
        }

        /// <summary>
        /// Checks what type of item comes next, if any.
        /// </summary>
        /// <returns>None if the item can't be determined</returns>
        public PdfType PeekNextItem()
        {
            SkipWhiteSpace();

            switch (_cur)
            {
                case Chars.Percent:
                    return PdfType.Comment;
                case Chars.Slash:
                    return PdfType.Name;
                case Chars.Pluss:
                case Chars.Minus:
                case Chars.Period:
                    return PdfType.Number;
                case Chars.ParenLeft:
                    return PdfType.String;
                case Chars.Less: // <
                    if (_next == Chars.Less) // <<
                        return PdfType.Dictionary;
                    return PdfType.String;

                case Chars.BracketLeft: // [
                    return PdfType.Array;
            }

            if (IsDigit(_cur))
                return PdfType.Number;
            if (IsLetter(_cur))
                return PdfType.Keyword;

            return PdfType.None;
        }

        #endregion

        #region String methods

        /// <summary>
        /// Gets a string. First character must be ( or <
        /// </summary>
        /// <param name="start">Start of string</param>
        /// <param name="length">Length</param>
        /// <returns>The string</returns>
        /// <remarks>
        /// Note: Intended for use by Text string objects.
        /// 
        /// I've checked how Acrobat9 handles unicode strings.
        /// The answer to this question is essentially that
        /// by default it doesn't.
        /// </remarks>
        [Obsolete("Not truly used")]
        internal string GetString(int start, int length)
        {
            if (length == 1) return string.Empty;

            //Fetches the data.
            var bytes = new byte[length];
            if (Read(bytes, start) != length)
                throw new PdfLexerException(PdfType.String, ErrCode.UnexpectedEOF);// SR.UnexpectedEnd);

#pragma warning disable 0429
            //Gets the string
            if (bytes[0] == (byte)Chars.ParenLeft)
                return (false && bytes.Length > 2 && bytes[1] == 0xFE && bytes[2] == 0xFF) ?
                    GetUTF16BEString(bytes, 1, bytes.Length - 1) : PDFString(bytes, 1, bytes.Length - 1);
#pragma warning restore 0429

            else return HexString(bytes, 1, bytes.Length - 1);
        }

        /// <summary>
        /// Gets a raw string.
        /// </summary>
        /// <param name="start">Start of string</param>
        /// <param name="length">Length</param>
        /// <returns>The string</returns>
        /// <remarks>
        /// Note: Intended for use for debug aid
        /// </remarks>
        internal string GetDebugString(int start, int length)
        {
            if (length == 0) return string.Empty;
            int dlength = Math.Min(length, 13);
            bool elipsis = dlength < length;

            //Fetches the data.
            var bytes = new byte[dlength];
            if (Read(bytes, start) != dlength)
                throw new PdfLexerException(PdfType.String, ErrCode.UnexpectedEOF);

            //Makes a string out of it
            var ret = new char[bytes.Length + ((elipsis) ? 2 : 1)];
            for (int c = 0; c < bytes.Length; c++)
            {
                char ch = (char)bytes[c];
                ret[c] = (ch != 0) ? (char)bytes[c] : ' ';
            }
            ret[ret.Length - 1] = (bytes[0] == '(') ? ')' : '>';
            if (elipsis) ret[ret.Length - 2] = '…';
            return new String(ret);
        }

        /// <summary>
        /// Converts a unicode string
        /// </summary>
        private static string GetUTF16BEString(byte[] bytes, int offset, int count)
        {
            #region Old code
            //This was the first string conversion code written. It tries to
            //be quicker at converting a string known to be unicode by not
            //looking at all the data. 

            /*//Position to copy from (note the copy is
            //performed from the index just after copy_pos)
            int copy_pos = -1;

            //How far to copy
            int copy_num = 0;
            int end_loop = count + offset;

            for (int c = offset; c < end_loop; )
            {
                Chars ch = (Chars)bytes[c];
                if (ch == Chars.BackSlash) //Chr code 92
                {
                    //Lone escape characters are to be ignored. By
                    //Setting new_copy_pos to the index of the esc
                    //char it will be copied over
                    int new_copy_pos = c;
                    int inc_copy_num = 1;
                    int esc_len = 2;

                    //Can safly assume that's there's at least one 
                    //more character
                    ch = (Chars)bytes[++c];

                    if (Chars.d0 <= ch && ch < Chars.d9)
                    {
                        // \ddd (Three digits, octal encoding)
                        // \dd and \d is also allowed
                        int code = 0; esc_len = 1;
                        do
                        {
                            code = code * 8 + (ch - Chars.d0);
                            if (esc_len++ == 3) 
                                goto add_octave;
                            if (++c == end_loop)
                                break;
                            ch = (Chars)bytes[c];
                        } while (Chars.d0 <= ch && ch < Chars.d9);

                        //Must go back one character
                        c--;
                    add_octave:
                        if (code < 256)
                        {
                            bytes[new_copy_pos++] = 0;
                            bytes[new_copy_pos] = (byte)code;
                        }
                        else
                        {
                            bytes[new_copy_pos++] = 1;
                            bytes[new_copy_pos] = (byte)(code - 256);
                        }
                        //Using the two first bytes of the escape seq.
                        esc_len -= 2;

                        //No changes needed if count is 2 bytes long.
                        if (esc_len == 0) goto slash_cont;
                        //But need to shift down if not
                        inc_copy_num = esc_len; //How much to shift the bytes
                        new_copy_pos = c; //Byte before bytes to copy
                    }
                    //End of line characters (prepended with \) are 
                    //to be ignored
                    else if (ch == Chars.LF)
                    { new_copy_pos = c; inc_copy_num = 2; esc_len = 2; }
                    else if (ch == Chars.CR)
                    {
                        if (c + 1 < end_loop && bytes[c + 1] == (byte)Chars.LF)
                        { new_copy_pos = ++c; inc_copy_num = 3; esc_len = 3; }
                        else
                        { new_copy_pos = c; inc_copy_num = 2; esc_len = 2; }
                    }

                    //The sequences \\, \( and \) comes naturaly as lone escape characters
                    //are to be ignored, and since the characters need not be changed they
                    //can be "ignored" too.

                    //Shift bytes with copy_num number of bytes
                    if (copy_pos != -1)
                    {
                        //Preformes delayed copy. Copies are delayed so that one knows
                        //how many characters to copy.
                        int copylength = (c - esc_len) - copy_pos++; //Length of the "string" to copy
                        if (copylength > 0)
                            Buffer.BlockCopy(bytes, copy_pos, bytes, copy_pos - copy_num, copylength);
                    }

                    copy_pos = new_copy_pos;
                    copy_num += inc_copy_num;

                slash_cont:
                    c += 1;
                    continue;
                }

                //Moves to the next character. Not sure if one should examine each character or not.
                c += 2;
            }

            //Shifts remaining bytes
            if (copy_pos != -1)
            {
                int copylength = end_loop - ++copy_pos;
                if (copylength > 0)
                    Buffer.BlockCopy(bytes, copy_pos, bytes, copy_pos - copy_num, copylength);
            }

            //For debug help
            //var b = Encoding.GetEncoding("UTF-16BE").GetBytes("Unicode");

            //The string is now encoded into UTF-16BE format. Now encoding it into whatever
            //.net use internaly (I think it's UTF-16LE)
            return Encoding.GetEncoding("UTF-16BE").GetString(bytes, offset, count - copy_num);//*/
            #endregion

            return Encoding.GetEncoding("UTF-16BE").GetString(bytes, offset, count);
        }

        /// <summary>
        /// Converts a unicode string
        /// </summary>
        private static string GetUTF16LEString(byte[] bytes, int offset, int count)
        {
            return Encoding.GetEncoding("UTF-16LE").GetString(bytes, offset, count);
        }

        /// <summary>
        /// Optionally create an ASCII or UTF string.
        /// </summary>
        public static byte[] CreateASCIIorUTF(string str)
        {
            for (int c = 0; c < str.Length; c++)
                if ((int)str[c] > 127)
                    return CreateUTF16BEString(str);
            return CreateASCIIString(str);
        }

        /// <summary>
        /// Converts a string uinto unicode bytes
        /// </summary>
        public static byte[] CreateUTF16BEString(string str)
        {
            var bytes = Encoding.GetEncoding("UTF-16BE").GetBytes(str);
            var unicode = new byte[bytes.Length + 2];
            unicode[0] = 0xFE;
            unicode[1] = 0xFF;
            Buffer.BlockCopy(bytes, 0, unicode, 2, bytes.Length);
            return unicode;
        }

        /// <summary>
        /// Converts a string uinto ascii bytes
        /// </summary>
        public static byte[] CreateASCIIString(string str)
        {
            return Encoding.ASCII.GetBytes(str);
        }

        /// <summary>
        /// Converts a PDF literal string encoded byte array into a string
        /// </summary>
        [DebuggerStepThrough]
        internal static string PDFString(byte[] bytes, int offset, int count)
        {
            //Decoded characters
            char[] ret = new char[count];
            int ret_pos = 0;

            int loop_end = offset + count;

            //Loops through the raw characters
            for (int c = offset; c < loop_end; c++)
            {
                //Fetches one raw character
                Chars ch = (Chars)bytes[c];

                //Checks if it's a special character
                if (ch == Chars.BackSlash) //Chr code 92
                {
                    //It's not possible for a string to end with \
                    //so one does not need to see if there's more
                    //bytes in the array
                    ch = (Chars)bytes[++c];

                    //Subtlety: Iligal octets are handled like Adobe, i.e.
                    //as \999 gives a string 999
                    if (Chars.d0 <= ch && ch < Chars.d8)
                    {
                        // \ddd (Three digits, octal encoding)
                        // \dd and \d is also allowed
                        int code = 0; int esc_len = 1;
                        do
                        {
                            code = code * 8 + (ch - Chars.d0);
                            if (esc_len++ == 3)
                                goto add_octave;
                            if (++c == loop_end)
                                break;
                            ch = (Chars)bytes[c];
                        } while (Chars.d0 <= ch && ch < Chars.d8);

                        //Must go back one character
                        c--;
                    add_octave:
                        //Adobe 7 crops octet values to bytes. Weird, but doing the
                        //same.
                        ret[ret_pos++] = (char)(byte)code;
                        continue;
                    }
                    else if (ch == Chars.n) //Found \n
                        ret[ret_pos++] = (char)Chars.LF;
                    else if (ch == Chars.r) //Found \r
                        ret[ret_pos++] = (char)Chars.CR;
                    else if (ch == Chars.t) //Found \t
                        ret[ret_pos++] = (char)Chars.Tab;
                    else if (ch == Chars.b) //Found \b
                        ret[ret_pos++] = (char)Chars.Backspace;
                    else if (ch == Chars.f) //Found \f
                        ret[ret_pos++] = (char)Chars.FF;
                    else if (ch == Chars.LF)
                    {
                        // Escaped line feed characters are to be ignored
                        // (Adobe does not ignore following CR)
                    }
                    else if (ch == Chars.CR)
                    {
                        //I.e. the sequence \\n\r is also to be ignored
                        if (c + 1 < loop_end && bytes[c + 1] == (byte)Chars.LF)
                            c++;
                    }
                    else
                    {
                        //The sequences \\, \( and \) comes naturaly as lone escape characters
                        //are to be ignored.
                        ret[ret_pos++] = (char)ch;
                    }
                }
                else
                {
                    ret[ret_pos++] = (char) ch;
                }
            }

            if (ret_pos < count)
            {
                var chs = new char[ret_pos];
                Array.Copy(ret, chs, chs.Length);
                return new String(chs);
            }
            
            return new String(ret);
        }

        /// <summary>
        /// Decodes PDF literal string encoded bytes
        /// </summary>
        /// <remarks>Used for unicode. Works exactly like: PDFString</remarks>
        [DebuggerStepThrough]
        internal static byte[] DecodePDFString(byte[] bytes, int offset, int count)
        {
            //Decoded characters
            byte[] ret = new byte[count];
            int ret_pos = 0;

            int loop_end = offset + count;

            //Loops through the raw characters
            for (int c = offset; c < loop_end; c++)
            {
                //Fetches one raw character
                Chars ch = (Chars)bytes[c];

                //Checks if it's a special character
                if (ch == Chars.BackSlash) //Chr code 92
                {
                    //It's not possible for a string to end with \
                    //so one does not need to see if there's more
                    //bytes in the array
                    ch = (Chars)bytes[++c];

                    if (Chars.d0 <= ch && ch < Chars.d9)
                    {
                        // \ddd (Three digits, octal encoding)
                        // \dd and \d is also allowed
                        int code = 0; int esc_len = 1;
                        do
                        {
                            code = code * 8 + (ch - Chars.d0);
                            if (esc_len++ == 3)
                                goto add_octave;
                            if (++c == loop_end)
                                break;
                            ch = (Chars)bytes[c];
                        } while (Chars.d0 <= ch && ch < Chars.d9);

                        //Must go back one character
                        c--;
                    add_octave:
                        ret[ret_pos++] = (byte)code;
                        continue;
                    }
                    else if (ch == Chars.n) //Found \n
                        ret[ret_pos++] = (byte)Chars.LF;
                    else if (ch == Chars.r) //Found \r
                        ret[ret_pos++] = (byte)Chars.CR;
                    else if (ch == Chars.t) //Found \t
                        ret[ret_pos++] = (byte)Chars.Tab;
                    else if (ch == Chars.b) //Found \b
                        ret[ret_pos++] = (byte)Chars.Backspace;
                    else if (ch == Chars.f) //Found \f
                        ret[ret_pos++] = (byte)Chars.FF;
                    else if (ch == Chars.LF)
                    {
                        // Escaped line feed characters are to be ignored
                        // (Adobe does not ignore following CR)
                    }
                    else if (ch == Chars.CR)
                    {
                        //I.e. the sequence \\n\r is also to be ignored
                        if (c + 1 < loop_end && bytes[c + 1] == (byte)Chars.LF)
                            c++;
                    }
                    else
                    {
                        //The sequences \\, \( and \) comes naturaly as lone escape characters
                        //are to be ignored.
                        ret[ret_pos++] = (byte)ch;
                    }
                }
                else
                {
                    ret[ret_pos++] = (byte)ch;
                }
            }

            if (ret_pos < count)
                Array.Resize<byte>(ref ret, ret_pos);

            return ret;
        }

        /// <summary>
        /// Converts a hex encoded byte array into a string
        /// </summary>
        [DebuggerStepThrough]
        internal static string HexString(byte[] bytes, int offset, int count)
        {
            //rpos is read pos, wpos is write pos
            int rpos = offset, wpos = 0;
            var ret = new char[(count + 1) / 2];

            do
            {
                int b1; Chars c;
                while (true)
                {
                    c = (Chars)bytes[rpos++];
                    if (!IsWhiteSpace(c))
                    {
                        b1 = Hex(c);
                        break;
                    }
                    if (rpos == bytes.Length) goto end;
                }

                int b2;
                while (true)
                {
                    c = (Chars)bytes[rpos++];
                    if (!IsWhiteSpace(c))
                    {
                        b2 = Hex(c);
                        break;
                    }
                    if (rpos == bytes.Length)
                    {
                        b2 = 0;
                        break;
                    }
                }

                ret[wpos++] = (char)(b1 << 4 | b2);
            } while (rpos < bytes.Length);

        end:

            return new String(ret);
        }

        /// <summary>
        ///Decodes a hex encoded byte array
        /// </summary>
        [DebuggerStepThrough]
        internal static byte[] DecodeHexString(byte[] bytes, int offset, int count)
        {
            if (count == 0)
                return new byte[0];

            //rpos is read pos, wpos is write pos
            int rpos = offset, wpos = 0,  end = offset + count;
            var ret = new byte[(count + 1) / 2];

            do
            {
                int b1, b2; Chars c;
                c = (Chars)bytes[rpos++];
                if (IsWhiteSpace(c))
                    return RemoveWhiteSpace(bytes, offset, count);
                b1 = Hex(c);

                if (rpos == end)
                    break;

                c = (Chars)bytes[rpos++];
                if (IsWhiteSpace(c))
                    return RemoveWhiteSpace(bytes, offset, count);
                b2 = Hex(c);

                ret[wpos++] = (byte)(b1 << 4 | b2);
            } while (rpos < end);

            return ret;
        }

        //Instead of fixing the code to handle whitespace they are removed if
        //encountered.
        private static byte[] RemoveWhiteSpace(byte[] bytes, int offset, int count)
        {
            var chs = new byte[count];
            int len = 0;
            for (int c = 0; c < chs.Length; c++)
            {
                var ch = bytes[offset++];
                if (!IsWhiteSpace((Chars) ch))
                    chs[len++] = ch;
            }
            return DecodeHexString(chs, 0, len);
        }

        //[DebuggerStepThrough]
        private static int Hex(Chars c)
        {
            const byte d0 = (byte)Chars.d0;
            const int A = ((int)Chars.A) - d0;
            const int a = ((int)Chars.a) - A - d0 + 10;
            int b = (byte) c - d0;
            if (b >= A) b -= (A - 10);
            if (b >= a) b -= (a - 10);
            if (b < 0 || 15 < b)
                throw new PdfLexerException(PdfType.String, ErrCode.CorruptToken);
            return b;
        }

        /// <summary>
        /// Converts a string straight into bytes
        /// </summary>
        /// <remarks>Intended as a debuging aid</remarks>
        [DebuggerStepThrough]
        public static byte[] GetBytes(string str)
        {
            var ret = new byte[str.Length];
            for (int c = 0; c < str.Length; c++)
                ret[c] = (byte)str[c];
            return ret;
        }

        /// <summary>
        /// Converts bytes straight into a string
        /// </summary>
        [DebuggerStepThrough]
        public static string GetString(byte[] bytes)
        {
            var ret = new Char[bytes.Length];
            for (int c = 0; c < ret.Length; c++)
                ret[c] = (char)bytes[c];
            return new String(ret);
        }

        /// <summary>
        /// Converts bytes to characters
        /// </summary>
        /// <param name="bytes">Bytes to decode</param>
        /// <param name="index">First byte to decode</param>
        /// <param name="count">Amount of bytes to decode</param>
        /// <remarks>Only used by the "find startxref" function</remarks>
        [DebuggerStepThrough]
        public static string GetString(byte[] bytes, int offset, int count)
        {
            //return Encoding.UTF8.GetString(bytes, index, count);
            var ret = new Char[count];
            for (int c = 0; c < count; c++)
                ret[c] = (char)bytes[c + offset];
            return new String(ret);
        }

        /// <summary>
        /// Gets a string. First character must be ( or <
        /// </summary>
        /// <param name="bytes">Bytes to decode</param>
        /// <param name="index">First byte to decode</param>
        /// <param name="count">Amount of bytes to decode</param>
        /// <returns>The string</returns>
        /// <remarks>
        /// Note: Intended for use by Text string objects.
        /// 
        /// I've checked how Acrobat9 handles unicode strings.
        /// The answer to this question is essentially that
        /// by default it doesn't.
        /// </remarks>
        internal static string GetUnicodeString(byte[] bytes, int offset, int count)
        {
            if (count == 0) return string.Empty;

            //This test checks if there's a unicode header. There are unicode strings without
            //this header, so cleverer tests should also be employed. 
            if (count > 2)
            {
                if (bytes[offset] == 0xFE && bytes[offset + 1] == 0xFF)
                    return GetUTF16BEString(bytes, offset + 2, count - 2);

                //Little endian encoding isn't offically supported, but files
                //use it anyway.
                else if (bytes[offset] == 0xFF && bytes[offset + 1] == 0xFE)
                    return GetUTF16LEString(bytes, offset + 2, count - 2);
            }
            

            return GetString(bytes, offset, count);
        }
        internal static string GetUnicodeString(byte[] bytes)
        { return GetUnicodeString(bytes, 0, bytes.Length); }

        #endregion

        #region Static helper methods

        public static byte[] GetBytes(bool big_endian, ulong value)
        {
            return big_endian ? GetBytes(value) : GetBytesLE(value);
        }

        /// <summary>
        /// Converts a value to a big endian byte array
        /// </summary>
        /// <returns>Big endian bytes</returns>
        public static byte[] GetBytes(ulong value)
        {
            var ba = new byte[8];
            ba[0] = (byte)((value >> 56) & 0xFF);
            ba[1] = (byte)((value >> 48) & 0xFF);
            ba[2] = (byte)((value >> 40) & 0xFF);
            ba[3] = (byte)((value >> 32) & 0xFF);
            ba[4] = (byte)((value >> 24) & 0xFF);
            ba[5] = (byte)((value >> 16) & 0xFF);
            ba[6] = (byte)((value >> 8) & 0xFF);
            ba[7] = (byte)(value & 0xFF);
            return ba;
        }

        /// <summary>
        /// Converts a value to a little endian byte array
        /// </summary>
        /// <returns>Little endian bytes</returns>
        public static byte[] GetBytesLE(ulong value)
        {
            var ba = new byte[8];
            ba[7] = (byte)((value >> 56) & 0xFF);
            ba[6] = (byte)((value >> 48) & 0xFF);
            ba[5] = (byte)((value >> 40) & 0xFF);
            ba[4] = (byte)((value >> 32) & 0xFF);
            ba[3] = (byte)((value >> 24) & 0xFF);
            ba[2] = (byte)((value >> 16) & 0xFF);
            ba[1] = (byte)((value >> 8) & 0xFF);
            ba[0] = (byte)(value & 0xFF);
            return ba;
        }

        public static byte[] GetBytes(bool big_endian, uint value)
        {
            return big_endian ? GetBytes(value) : GetBytesLE(value);
        }

        /// <summary>
        /// Converts a value to a big endian byte array
        /// </summary>
        /// <returns>Big endian bytes</returns>
        public static byte[] GetBytes(uint value)
        {
            var ba = new byte[4];
            ba[0] = (byte)((value >> 24) & 0xFF);
            ba[1] = (byte)((value >> 16) & 0xFF);
            ba[2] = (byte)((value >> 8) & 0xFF);
            ba[3] = (byte)(value & 0xFF);
            return ba;
        }

        /// <summary>
        /// Converts a value to a little endian byte array
        /// </summary>
        /// <returns>Little endian bytes</returns>
        public static byte[] GetBytesLE(uint value)
        {
            var ba = new byte[4];
            ba[3] = (byte)((value >> 24) & 0xFF);
            ba[2] = (byte)((value >> 16) & 0xFF);
            ba[1] = (byte)((value >> 8) & 0xFF);
            ba[0] = (byte)(value & 0xFF);
            return ba;
        }

        /// <summary>
        /// Converts a value to a little endian byte array
        /// </summary>
        /// <returns>Little endian bytes</returns>
        public static byte[] GetBytesLE(int value)
        {
            var ba = new byte[4];
            ba[3] = (byte)((value >> 24) & 0xFF);
            ba[2] = (byte)((value >> 16) & 0xFF);
            ba[1] = (byte)((value >> 8) & 0xFF);
            ba[0] = (byte)(value & 0xFF);
            return ba;
        }

        public static byte[] GetBytes(bool big_endian, ushort value)
        {
            return big_endian ? GetBytes(value) : GetBytesLE(value);
        }

        /// <summary>
        /// Converts a value to a big endian byte array
        /// </summary>
        /// <returns>Big endian bytes</returns>
        public static byte[] GetBytes(ushort value)
        {
            var ba = new byte[2];
            ba[0] = (byte)((value >> 8) & 0xFF);
            ba[1] = (byte)(value & 0xFF);
            return ba;
        }

        /// <summary>
        /// Converts a value to a little endian byte array
        /// </summary>
        /// <returns>Little endian bytes</returns>
        public static byte[] GetBytesLE(ushort value)
        {
            var ba = new byte[2];
            ba[1] = (byte)((value >> 8) & 0xFF);
            ba[0] = (byte)(value & 0xFF);
            return ba;
        }

        #endregion

        #region Error handeling

        /// <summary>
        /// Same as "setnexttoken" except exceptions are swallowed.
        /// </summary>
        internal PdfType trySetNextToken()
        {
            try { return SetNextToken(); }
            catch (PdfLexerException) { return PdfType.None; }
        }

        internal void AdvanceToNewLine()
        {
            while (_cur == Chars.LF && _cur == Chars.EOF)
                SetNextChar();
        }

        #endregion

        #region Structs

        #endregion
    }
}
