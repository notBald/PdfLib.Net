using System;
using System.IO;
using System.Text;
using System.Globalization;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Read;
using PdfLib.Pdf.Filter;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Writes strings and binary data to a PDF file. Tracking the size
    /// of the write.
    /// </summary>
    internal class PdfWriter
    {
        #region Variables and properties

        /// <summary>
        /// This buffer is used for string to byte conversion
        /// </summary>
        byte[] _buf = new byte[1024];

        /// <summary>
        /// Used for creating strings
        /// </summary>
        StringBuilder _sb = new StringBuilder();
        
        /// <summary>
        /// Stream being written to
        /// </summary>
        Stream _s;

        /// <summary>
        /// The last type of delimiter
        /// </summary>
        CharType _last = CharType.Newline;

        /// <summary>
        /// Commonly used byte arrays
        /// </summary>
        byte[] True = Lexer.GetBytes("true");
        byte[] False = Lexer.GetBytes("false");
        byte[] Null = Lexer.GetBytes("null");

        /// <summary>
        /// Precition of real strings
        /// </summary>
        private string _precision = "0.###"; //"0.########"
        public const int DefaultRealPrecision = 3;

        /// <summary>
        /// Precision of real numbers
        /// </summary>
        public int Precision
        {
            get { return _precision.Length - 2; }
            set
            {
                if (value != Precision)
                {
                    var prec = Math.Max(value, 14);
                    var sb = new StringBuilder(2 + prec);
                    sb.Append("0.");
                    for (int c = 0; c < prec; c++)
                        sb.Append("#");
                    _precision = sb.ToString();
                }
            }
        }

        /// <summary>
        /// How the pdf file should be generated
        /// </summary>
        public readonly SaveMode SaveMode;

        /// <summary>
        /// Not really tested, but adds padding to the generated PDF file.
        /// Keep it at "none"
        /// </summary>
        public readonly PM PaddMode;

        /// <summary>
        /// Version number used in the header.
        /// </summary>
        /// <remarks>Needed for object streams</remarks>
        public readonly PdfVersion HeaderVersion;

        public readonly CompressionMode Compression;

        /// <summary>
        /// Current write position
        /// </summary>
        public long Position { get { return _s.Position; } }

        #endregion

        #region Init

        public PdfWriter(Stream s, SaveMode save_mode, PM padd_mode, CompressionMode cm, PdfVersion head)
        { _s = s; SaveMode = save_mode; HeaderVersion = head; PaddMode = padd_mode; Compression = cm; }

        #endregion

        /// <summary>
        /// Writes a PDF header to the stream
        /// </summary>
        /// <param name="ver">Pdf version to write</param>
        /// <param name="binary">If this is a binary header</param>
        public void WriteHeader(PdfVersion ver, bool binary)
        {
            string header = string.Format("%PDF-1.{0}\n", (int)ver);
            if (binary)
            {
                _sb.Length = 0;
                _sb.Append(header);
                _sb.Append("%âãÏÓ\n"); //Four characters > 128 in value
                header = _sb.ToString();
            }

            WriteRaw(header);
        }

        /// <summary>
        /// Appends a line ending
        /// </summary>
        public void WriteLine()
        {
            _s.WriteByte(10);
            _last = CharType.Newline;
        }

        /// <summary>
        /// Writes a null value to the stream
        /// </summary>
        public void WriteNull()
        {
            Padding(CharType.Name);
            _s.Write(Null, 0, Null.Length);
            _last = CharType.Name;
        }

        /// <summary>
        /// Writes a name to the stream
        /// </summary>
        public void WriteName(string name)
        {
            Padding(CharType.Delimiter);

            #region Encodes
            int i = FindIligalChar(name, 0);
            if (i != -1)
            {
                int p = 0;
                _sb.Length = 0;
                do
                {
                    _sb.Append(name.Substring(p, i - p));
                    _sb.Append('#');
                    p = i+1;
                    int code = (int)name[i];
                    if (code < 128)
                        _sb.Append(code.ToString("X2"));
                    else
                        _sb.Append(Encoding.ASCII.GetBytes(name.Substring(i, 1))[0].ToString("X2"));

                } while ((i = FindIligalChar(name, p)) != -1);
                if (p < name.Length)
                    _sb.Append(name.Substring(p, name.Length - p));
                name = _sb.ToString();
            }
            #endregion

            #region Writes

            int len = name.Length + 1;
            byte[] buf = (len< _buf.Length) ? _buf : new byte[len];
            buf[0] = 47;
            for (int c = 1; c < len; c++)
                buf[c] = (byte)name[c-1];
            _s.Write(buf, 0, len);
            _last = CharType.Name;

            #endregion
        }

        /// <summary>
        /// Writes a keyword to the stream
        /// </summary>
        public void WriteKeyword(string keyword)
        {
            Padding(CharType.Name);

            #region Writes

            byte[] buf = (keyword.Length < _buf.Length) ? _buf : new byte[keyword.Length];
            for (int c = 0; c < keyword.Length; c++)
                buf[c] = (byte)keyword[c];
            _s.Write(buf, 0, keyword.Length);
            SetLastChar(buf[keyword.Length - 1]);

            #endregion
        }

        /// <summary>
        /// Writes a array to the stream
        /// </summary>
        public void WriteArray(PdfItem[] ar)
        {
            #region Writes [
            Padding(CharType.Delimiter);
            _s.WriteByte(91);
            _last = CharType.Delimiter;
            #endregion

            lock (ar)
            {
                for (int c = 0; c < ar.Length; c++)
                    ar[c].Write(this);
            }

            #region Writes ]
            Padding(CharType.Delimiter);
            _s.WriteByte(93);
            _last = CharType.Delimiter;
            #endregion
        }

        /// <summary>
        /// Writes a catalog to the stream
        /// </summary>
        public void WriteDictionary(Catalog cat)
        {
            #region Writes <<
            Padding(CharType.Delimiter);
            _s.Write(Lexer.GetBytes("<<"), 0, 2);
            _last = CharType.Delimiter;
            #endregion

            lock (cat)
            {
                foreach (var kp in cat)
                {
                    //Names starting with 'Ĭ' leads to internal
                    //data that is not to be saved out into the
                    //actual PDF. ('Ĭ' has a value > 255)
                    //
                    //One can also use \0, but it's possible
                    //that other libabries allow that character
                    //in the name. Another option is actually
                    //the space character, as /#20 is a 
                    //invalid name (though again, other readers
                    //may allow it. Worth testing at least)
                    if (kp.Key[0] == 'Ĭ') continue;

                    WriteName(kp.Key);

                    kp.Value.Write(this);

                    //Todo: Verbose mode
                    if (PaddMode > PM.None)
                        WriteLine();
                }
            }

            #region Writes >>
            Padding(CharType.Delimiter);
            _s.Write(Lexer.GetBytes(">>"), 0, 2);
            _last = CharType.Delimiter;
            #endregion
        }

        internal void WriteStream(PdfDictionary dict, byte[] data)
        {
            WriteStream((Catalog)((ICRef)dict).GetChildren(), data);
        }

        /// <summary>
        /// Writes out a stream, compresses as specified by the compression mode
        /// </summary>
        /// <param name="cat">
        /// The streams catalog. Length must be set correctly
        /// </param>
        /// <param name="data">
        /// Raw or compressed data, in accordance with parameters set
        /// in the filter array
        /// </param>
        internal void WriteStream(Catalog cat, byte[] data)
        {
            var org_cat = cat;
            lock (org_cat)
            {
                if (Compression != CompressionMode.None)
                {
                    //I'm not entierly happy with this approatch as unused indirect 
                    //filter arrays will be written out to the file, or may already 
                    //have been written out to the file.

                    #region Fetches the filter array

                    //We need to know how the data is compressed.
                    PdfItem item;
                    FilterArray filters = null;
                    bool error = false;
                    if (cat.TryGetValue("Filter", out item))
                    {
                        //The filter can be a name, array or FilterArray
                        //object. It can also be somthing else, in
                        //which case we must save it unchanged.
                        try
                        {
                            if (item is PdfName)
                                filters = new FilterArray(new PdfItem[] { item });
                            else
                                filters = (FilterArray)item.ToType(PdfType.FilterArray);
                        }
                        catch (PdfCastException)
                        { error = true; }
                    }

                    FilterParmsArray fpa = null;
                    if (!error && filters != null && cat.TryGetValue("DecodeParms", out item))
                    {
                        try
                        {
                            if (item is PdfDictionary)
                                fpa = new FilterParmsArray(new PdfItem[] { item }, filters);
                            else
                                fpa = (FilterParmsArray)item.ToType(PdfType.FilterParmsArray);
                        }
                        catch (PdfCastException)
                        { error = true; }
                    }

                    #endregion

                    if (!error)
                    {
                        switch (Compression)
                        {
                            #region Normal
                            case CompressionMode.Normal:
                                //In this mode we only compress if the resource isn't
                                //compressed already
                                if (filters == null)
                                {
                                    var compressed_data = PdfFlateFilter.Encode(data);
                                    if (compressed_data.Length >= data.Length - PdfFlateFilter.SKIP)
                                        break;
                                    data = compressed_data;
                                    cat = cat.Clone();

                                    //Perhaps not ideal as it will be impossible to
                                    //share filter arrays if they're always set direct,
                                    //and it's too late to make them indirect at this
                                    //point.
                                    cat.Remove("Length");
                                    cat.Add("Length", new PdfInt(data.Length));
                                    cat.Add("Filter", new PdfName("FlateDecode"));
                                }
                                break;
                            #endregion

                            #region Uncompressed
                            case CompressionMode.Uncompressed:
                                if (filters != null && !filters.HasJ2K)
                                {
                                    //Decompresses the data, except for J2K filters
                                    //as they contain stuff needed to decode the image
                                    data = PdfStream.DecodStream(data, filters, fpa);
                                    cat = cat.Clone();
                                    cat.Remove("Length");
                                    cat.Add("Length", new PdfInt(data.Length));
                                    cat.Remove("Filter");
                                    if (fpa != null)
                                        cat.Remove("DecodeParms");
                                    filters = null;
                                    fpa = null;
                                }
                                break;

                            #endregion

                            #region Maximum

                            case CompressionMode.Maximum:
                                //In this mode we compress all resources, except those
                                //that has already been deflate compressed
                                if (filters == null) goto case CompressionMode.Normal;
                                if (!filters.HasDeflate && !filters.HasJ2K)
                                {
                                    byte[] compressed_data;
                                    try
                                    {
                                        compressed_data = PdfFlateFilter.Encode(data);
                                    } catch (Exception e)
                                    {
                                        System.Diagnostics.Debug.Assert(false, "Zip compression failed");
                                        Console.WriteLine(e.Message);
                                        File.WriteAllBytes("FailedToCompress.zlib", data);

                                        //If the compression failed, it's better that we leave it uncompressed than that we give up.
                                        break;
                                    }
                                    if (compressed_data.Length >= data.Length - PdfFlateFilter.SKIP)
                                        break;
                                    cat = cat.Clone();
                                    data = compressed_data;
                                    cat.Remove("Length");
                                    cat.Add("Length", new PdfInt(data.Length));

                                    //One must add the deflate filter to the array
                                    var fa = (PdfItem[])((ICRef)filters).GetChildren();
                                    lock (fa)
                                    {
                                        var na = new PdfItem[fa.Length + 1];
                                        Array.Copy(fa, 0, na, 1, fa.Length);
                                        na[0] = new PdfName("FlateDecode");
                                        filters = new FilterArray(na);
                                    }
                                    cat["Filter"] = filters;

                                    if (fpa != null)
                                    {
                                        fa = (PdfItem[])((ICRef)fpa).GetChildren();
                                        lock (fa)
                                        {
                                            var na = new PdfItem[fa.Length + 1];
                                            Array.Copy(fa, 0, na, 1, fa.Length);
                                            na[0] = PdfNull.Value;
                                            fpa = new FilterParmsArray(na, filters);
                                        }
                                        cat["DecodeParms"] = fpa;
                                    }
                                }
                                break;

                                #endregion
                        }
                    }
                }

                //If this code is needed, it's a bug. Length must always be up to date.
                ////Add length if it's missing
                //if (!cat.TryGetValue("Length", out PdfItem length) ||
                //    !(length is PdfInt l) || l.GetInteger() != data.Length)
                //{
                //    cat = cat.Clone();
                //    if (length != null)
                //        cat.Remove("Length");
                //    cat.Add("Length", new PdfInt(data.Length));
                //}

                #region Writes the stream
                WriteDictionary(cat);

                //Then stream keywords and data
                WriteRawLine("stream\n");
                WriteRaw(data);
                WriteRawLine("endstream");
                #endregion
            }
        }

        /// <summary>
        /// Adds padding depending on verbosity and seperator
        /// </summary>
        private void Padding(CharType next)
        {
            if (_last == CharType.Newline || 
                _last == CharType.White || 
                next == CharType.Newline)
            {

            }
            else if (_last == CharType.Delimiter)
            {
                //Adds a space for readability
                if (PaddMode > PM.None)
                {
                    _s.WriteByte(32);
                    _last = CharType.White;
                }
            }
            else //Last is name
            {
                //There must be padding after names
                if (PaddMode > PM.None || next == CharType.Name)
                {
                    _s.WriteByte(32);
                    _last = CharType.White;
                }
            }
        }

        /// <remarks>
        /// White space used as part of a name shall always be coded
        /// using the 2-digit hexadecimal notation and no white space 
        /// may intervene between the SOLIDUS and the encoded name.
        /// 
        /// Regular characters that are outside the range 
        /// EXCLAMATION MARK(21h) (!) to TILDE (7Eh) (~) should be 
        /// written using the hexadecimal notation.
        /// 
        /// The token SOLIDUS (a slash followed by no regular characters) 
        /// introduces a unique valid name defined by the empty sequence 
        /// of characters.
        /// </remarks>
        private int FindIligalChar(string name, int offset)
        {
            for (int i = offset; i < name.Length; i++)
            {
                char ch = name[i];
                switch (ch)
                {
                    //Whitespace falls outside the range
                    case '#':

                    //Delimiters
                    case '(':
                    case ')':
                    case '<':
                    case '>':
                    case '[':
                    case ']':
                    case '{':
                    case '}':
                    case '/':
                    case '%':
                        return i;
                }
                if (ch < '!' || ch > '~')
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Writes a boolean
        /// </summary>
        public void Write(bool b)
        {
            Padding(CharType.Name);
            var bytes = (b) ? True : False;
            _s.Write(bytes, 0, bytes.Length);
            _last = CharType.Name;
        }

        /// <summary>
        /// Writes an integer.
        /// </summary>
        /// <remarks>Named WriteInt to seperate it from a byte</remarks>
        public void WriteInt(int num)
        {
            Padding(CharType.Name);
            var b = Lexer.GetBytes(num.ToString());
            _s.Write(b, 0, b.Length);
            _last = CharType.Name;
        }

        /// <summary>
        /// Writes a real.
        /// </summary>
        /// <remarks>Named WriteDouble to seperate it from a byte</remarks>
        public void WriteDouble(double num)
        {
            Padding(CharType.Name);
            var b = Lexer.GetBytes(num.ToString(_precision, CultureInfo.InvariantCulture));
            _s.Write(b, 0, b.Length);
            _last = CharType.Name;
        }

        /// <summary>
        /// Used when one need to write on a new line.
        /// </summary>
        internal void WriteRawLine(string str)
        {
            if (_last != CharType.Newline)
                _s.WriteByte(10);
            WriteRaw(str);
        }

        /// <summary>
        /// Writes raw string data to the stream. No padding of any kind or ( ) included
        /// </summary>
        public void WriteRaw(string str)
        {
            byte[] buf = (str.Length < _buf.Length) ? _buf : new byte[str.Length];
            for(int c=0; c < str.Length; c++)
                buf[c] = (byte) str[c];
            _s.Write(buf, 0, str.Length);
            if (str.Length != 0)
                SetLastChar(buf[str.Length - 1]);
        }

        /// <summary>
        /// Writes raw bytes to the stream, but makes sure padding
        /// is correct.
        /// </summary>
        /// <param name="bytes">Array with bytes to write</param>
        /// <param name="offset">Offset into the array</param>
        /// <param name="count">Number of bytes to write</param>
        public void Write(byte[] bytes, int offset, int count)
        {
            if (count == 0) return;

            Padding(GetCharType(bytes[offset]));
            _s.Write(bytes, offset, count);
            SetLastChar(bytes[count - 1]);
        }

        /// <summary>
        /// Writes all bytes in the array to the stream. Handles padding.
        /// </summary>
        /// <param name="bytes">Bytes to write</param>
        public void Write(byte[] bytes)
        {
            if (bytes.Length == 0) return;

            Padding(GetCharType(bytes[0]));
            _s.Write(bytes, 0, bytes.Length);
            SetLastChar(bytes[bytes.Length - 1]);
        }

        /// <summary>
        /// Writes a single byte to the stream. Handles padding before and after.
        /// </summary>
        /// <param name="b">Byte to write</param>
        public void Write(byte b)
        {
            Padding(GetCharType(b));
            _s.WriteByte(b);
            SetLastChar(b);
        }

        /// <summary>
        /// For when one don't want padding
        /// </summary>
        public void WriteRaw(byte[] bytes)
        {
            _s.Write(bytes, 0, bytes.Length);
            if (bytes.Length != 0)
                SetLastChar(bytes[bytes.Length - 1]);
        }

        /// <summary>
        /// For when one don't want padding
        /// </summary>
        public void WriteRaw(byte b)
        {
            _s.WriteByte(b);
            SetLastChar(b);
        }

        /// <summary>
        /// Needed for padding. Examines the byte to determine
        /// what type of padding will be needed.
        /// </summary>
        /// <param name="b">Must be the last byte written</param>
        private void SetLastChar(byte b)
        {
            if (b == 10)
                _last = CharType.Newline;
            else if (Lexer.IsDelimiter((Chars)b))
                _last = CharType.Delimiter;
            else if (b == 10)
                _last = CharType.Newline;
            else
                _last = CharType.Name;
        }

        /// <summary>
        /// Determines what type of padding is needed before
        /// that byte
        /// </summary>
        /// <param name="b">Byte that will be written</param>
        private CharType GetCharType(byte b)
        {
            if (b == 10)
                return CharType.Newline;
            else if (Lexer.IsDelimiter((Chars)b))
                return CharType.Delimiter;
            else if (b == 10)
                return CharType.Newline;
            return CharType.Name;
        }

        /// <summary>
        /// Keeps track of the last chartype written.
        /// This is used when deciding on how a token
        /// have to be sepperated, if at all.
        /// </summary>
        enum CharType
        {
            Name,
            Newline,
            Delimiter,
            White
        }
    }
}
