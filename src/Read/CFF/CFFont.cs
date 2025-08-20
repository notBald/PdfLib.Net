using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Font;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render.Font;

namespace PdfLib.Read.CFF
{
    /// <summary>
    /// Compact Type 1 font parser
    /// </summary>
    public static class CFont
    {
        /// <summary>
        /// Tests if a stream is in CFF format.
        /// </summary>
        /// <param name="s">Stream to test</param>
        /// <returns>This is a CFF font</returns>
        public static bool IsCFF(Stream s)
        {
            if (s == null || !s.CanRead || !s.CanSeek)
                return false;
            try
            {
                var hold = s.Position;
                var size = s.Length - hold;
                try
                {
                    var ex = new Util.StreamReaderEx(s);
                    var h = ReadHeader(ex);
                    if (h.major != 1 || h.hdrSize < size)
                        return false;

                    //Move to Name INDEX. (This will quite likely throw an exception. 
                    //It'd be better to validate the numbers before doing things with them.)
                    s.Position = h.hdrSize;
                    var named_index = ReadIndex(ex);

                    //Moves to the Top DICT INDEX
                    s.Position = named_index.end;
                    var TopDI = ReadIndex(ex);

                    //We can say with a fair bit of confidence that this is a CFF file that we can read
                    return TopDI.count == 1;
                }
                finally { s.Position = hold; }
            }
            catch { return false; }
        }

        /// <summary>
        /// Gets the header information
        /// </summary>
        internal static Header ReadHeader(Util.StreamReaderEx s)
        {
            Header h;
            h.major = s.ReadByte();
            h.minor = s.ReadByte();
            h.hdrSize = s.ReadByte();
            h.offSize = s.ReadByte();
            return h;
        }

        /// <summary>
        /// Reads an index.
        /// </summary>
        /// <remarks>Position of stream is undefined after the call</remarks>
        internal static Index ReadIndex(Util.StreamReaderEx s)
        {
            Index i;
            i.count = s.ReadUShort();
            i.offSize = s.ReadByte();
            i.offset = (int)s.Position;

            //The size/end of an index is determined by looking at the
            //offset specified by the last element of the offset array
            //
            //The only exception is when count equals 0. In that case
            //there's no offsets and the size will be 2 bytes.
            if (i.count == 0)
            {
                i.end = i.offset;
                return i;
            }

            //Moves to the last offset
            s.Position += i.count * i.offSize;

            //Reads the last offset
            int last_offset = Index.ReadOffset(i.offSize, s);

            //Marks the end of the index.
            i.end = (int)s.Position - 1 + last_offset;

            return i;
        }

        /// <summary>
        /// Reads out a/the named index
        /// </summary>
        internal static string[] ReadNames(Index i, Util.StreamReaderEx s)
        {
            var strs = new string[i.count];
            for (int c = 0; c < i.count; c++)
            {
                //Length is found by looking at the next offset.
                strs[c] = ASCIIEncoding.ASCII.GetString(i.GetBytes(c, s));
            }
            return strs;
        }

        internal static int[] ReadGlyphNames(TopDICT td, Util.StreamReaderEx s, out Index glyph_table)
        {
            int nGlyphs = 0;
            if (td.CharStrings != null)
            {
                s.Position = td.CharStrings.Value;
                glyph_table = CFont.ReadIndex(s);
                nGlyphs = glyph_table.count;
            }
            else
                throw new NotImplementedException("No CharStrings");

            int[] gnames;

            //ISOAbdobe
            if (td.Charset == 0)
            {
                //Iso abdobe has 229 glyphs that goes 1 to 1 with the
                //standar string table
                gnames = new int[229];
                for (int c = 0; c < gnames.Length; c++)
                    gnames[c] = c;
                return gnames;
            }

            if (td.Charset == 1)
                throw new NotImplementedException("AdobeConst.ExpertCharset");
            //return AdobeConst.ExpertCharset;
            if (td.Charset == 2)
                throw new NotImplementedException("AdobeConst.ExpertSubCharset");
            //return AdobeConst.ExpertSubCharset;

            s.Position = td.Charset;
            gnames = new int[nGlyphs];
            gnames[0] = 0; //First is undefined.
            byte b = s.ReadByte();
            if (b == 0) //Format 0
            {
                //Reads out glyph name array
                for (int c = 1; c < nGlyphs; c++)
                    gnames[c] = s.ReadUShort();
                return gnames;
            }
            if (b == 1 || b == 2) //Format 1 and 2
            {
                for (int i = 1; i < nGlyphs; )
                {
                    ushort sid = s.ReadUShort();
                    ushort nleft = (b == 1) ? s.ReadByte() : s.ReadUShort();
                    for (int k = 0; k <= nleft; k++)
                        gnames[i++] = sid++;
                }

                return gnames;
            }

            throw new NotSupportedException();
        }
    }

    //A type 1 font can contain multiple fonts. 
    public class CFont<Path> : IDisposable
    {
        #region variables

        /// <summary>
        /// The name of the glyphs
        /// </summary>
        internal string[] GlyphNames
        {
            get
            {
                var str = new string[_glyph_names.Length];
                str[0] = ".notdef";
                for (int c = 1; c < str.Length; c++)
                {
                    var name = _glyph_names[c];
                    str[c] = (name < 391) ? Enc.SIDNames[name] : _strings[name - 391];
                }
                return str;
            }
        }

        /// <summary>
        /// Transform applied to each character
        /// </summary>
        readonly xMatrix _mt;
        IGlyph<Path> _glyph_creator;

        //bool _using_standar_encoding = false;
        int[] _glyph_names;

        /// <summary>
        /// Holds charcode -> glyph_index
        /// </summary>
        private int[] _encoding;
        Index _glyph_table;
        readonly string[] _strings;
        public int[] Encoding { get { return _encoding; } set { _encoding = value; } }

        /// <summary>
        /// Note. These objects holds a full uncompressed copy of
        /// the font file.
        /// </summary>
        GlyphParser<Path> _parser;
        Type1Font<Path>.GlyphParser _parser2;
        FontDescFinder _parser3;

#if DEBUG
        //Some basic font information that isn't necessarily all that interesting
        readonly TopDICT _td;

        public string Version { get { return this.GetSIDName(_td.Version); } }
        public string Notice { get { return this.GetSIDName(_td.Notice); } }
        public string Copyright { get { return this.GetSIDName(_td.Copyright); } }
        public string FullName { get { return this.GetSIDName(_td.FullName); } }
        public string FamilyName { get { return this.GetSIDName(_td.FamilyName); } }
        public string Weight { get { return this.GetSIDName(_td.Weight); } }
        public string PostScript { get { return this.GetSIDName(_td.PostScript); } }
        public string BaseFontName { get { return this.GetSIDName(_td.BaseFontName); } }
#endif

        #endregion

        #region Init
        internal CFont(Util.StreamReaderEx s, bool use_encoding, out CreateSG<Path> create_sg, IGlyph<Path> glyph_creator)
        {
            _glyph_creator = glyph_creator;

            //Reads the header of the font
            var header = CFont.ReadHeader(s);
            if (header.major != 1) throw new NotSupportedException("Unsupported CFF file");

            //Move to Name INDEX
            s.Position = header.hdrSize;
            var named_index = CFont.ReadIndex(s);

            //There's no need to read the names
            //of the fonts unless multiple fonts 
            //are to be supported.
            //var names = ReadNames(named_index, s);

            //Moves to the Top DICT INDEX
            s.Position = named_index.end;
            var TopDI = CFont.ReadIndex(s);

            //Only supports files with one font.
            //Alt. one could just default to the first font
            if (TopDI.count != 1)
                throw new NotSupportedException("Multiple fonts in CFF file");

            //Moves to the String INDEX
            s.Position = TopDI.end;
            var StringIndex = CFont.ReadIndex(s);
            _strings = CFont.ReadNames(StringIndex, s);

            //Executes the dictionary for the first font.
            var td = new TopDICT(s);
            td.Parse(TopDI.GetSizePos(0, s));
            _mt = td.FontMatrix;
#if DEBUG
            _td = td;
#endif

            if (td.CID != null)
            {
                //Reads out CID related information
                create_sg = ReadFDSelect(td, s);
                _parser = new GlyphParser<Path>(s, _glyph_table, this, _glyph_creator);

                //Charset maps to CID (A character id)
            }
            else
            {
                //Charset maps to SID (A string name)
                _glyph_names = CFont.ReadGlyphNames(td, s, out _glyph_table);

                //Type 1 Charstings are "Type 1" formated and needs
                //a Type 1 renderer
                if (td.CharstringType != 2)
                {
                    //Todo: Not sure what to do with subrutines in
                    //type 1 font programs. Will get a nullpointexception
                    //for now.
                    _parser2 = new Type1Font<Path>.GlyphParser(null, _glyph_creator);

                    if (use_encoding)
                        create_sg = CreateSG1;
                    else
                        create_sg = CreateSG1_NOENC;
                }
                else
                {
                    _parser = new GlyphParser<Path>(s, _glyph_table, this, _glyph_creator);

                    if (use_encoding)
                        create_sg = CreateSG2;
                    else
                        create_sg = CreateSG2_NOENC;
                }
            }

            //Parses the private dictionary.
            var pd = new PrivateDICT(s);
            pd.Parse(td.Private);

            //Gets the private subrutine index
            if (pd.Subrs != null)
            {
                s.Position = td.Private.start + pd.Subrs.Value;
                var LSubIndex = CFont.ReadIndex(s);
                _parser._lsubrutines = LSubIndex;
                _parser._bias = CalcBias(td, LSubIndex.count);
            }

            //Gets the array used for encoding
            _encoding = ReadEncoding(td.Encoding, s);
            //_using_standar_encoding = td.Encoding == 0 || td.Encoding == 1;

            //Gets the global subrutine index
            try
            {
                s.Position = StringIndex.end;
                var GSubIndex = CFont.ReadIndex(s);

                if (GSubIndex.count != 0)
                {
                    //Debug.Assert(false, "Untested code");
                    _parser._gsubrutines = GSubIndex;
                    _parser._gbias = CalcBias(td, GSubIndex.count);
                }
            }
            catch (IndexOutOfRangeException)
            {
                //At least one test file lacks a the empty
                //"global subrutine index". The quickfix here
                //is to catch this exception, though a better
                //solution is to only init the global subrutines
                //when they are needed.
            }
        }

        public void Dispose()
        {
            if (_parser != null)
            {
                _parser.Dispose();
                _parser = null;
            }
            _parser2 = null;
            if (_parser3 != null)
            {
                _parser3.Dispose();
                _parser3 = null;
            }
        }

        #endregion

        /// <summary>
        /// Calculates offset BIAS (from the CFF specs chapter 16)
        /// </summary>
        private int CalcBias(TopDICT td, int count)
        {
            if (td.CharstringType == 1)
                return 0;
            else if (count < 1240)
                return 107;
            else if (count < 33900)
                return 1131;
            else
                return 32768;
        }

        #region Read methods

        private CreateSG<Path> ReadFDSelect(TopDICT td, Util.StreamReaderEx s)
        {
            //Reads in charstring data. (Also sets the _glyph_table)
            _glyph_names = CFont.ReadGlyphNames(td, s, out _glyph_table);

            //Reads charcode->font table
            byte[] font_dicts = new byte[_glyph_table.count];
            s.Position = td.CID.FDSelect;
            var format = s.ReadByte();
            if (format == 0)
                s.Read(font_dicts, 0, _glyph_table.count);
            else if (format == 3)
            {
                var nRanges = s.ReadUShort();
                if (nRanges == 0) throw new NotSupportedException();

                int first = s.ReadUShort();
                byte fd = s.ReadByte();
                for (int c = 1; c < nRanges; c++)
                {
                    int last = s.ReadUShort();

                    while (first < last)
                        font_dicts[first++] = fd;
                    fd = s.ReadByte();
                }

                int sentinel = s.ReadUShort();
                Debug.Assert(sentinel == _glyph_table.count);
                while (first < sentinel)
                    font_dicts[first++] = fd;
            }
            else
            {
                throw new NotSupportedException("Unkonwn FD Select format");
            }

            //Reads in the FontDescriptor array
            s.Position = td.CID.FDArray;
            var fontDictIndex = CFont.ReadIndex(s);

            _parser3 = new FontDescFinder(font_dicts, fontDictIndex, s);
            return CreateSG3;
        }

        /// <summary>
        /// For type 1 charstrings
        /// </summary>
        private Path ReadGlyph1(int index)
        {
            var sp = _glyph_table.GetBytes(index, _parser.Stream);
            return _parser2.Execute(sp);
        }

        /// <summary>
        /// For type 2 charstrings
        /// </summary>
        private Path ReadGlyph2(int index)
        {
            var sp = _glyph_table.GetSizePos(index, _parser.Stream);
            var sg = _parser.Parse(sp);
            return sg;
        }

        int[] ReadEncoding(int enc, Util.StreamReaderEx s)
        {
            if (enc == 0) return EncConst.StandardEncoding;
            if (enc == 1) return EncConst.ExpertEncoding;

            s.Position = enc;
            byte b = s.ReadByte();
            var enc_ar = new int[256];
            if ((b & 0x7F) == 0) //Format 0
            {
                int ncodes = s.ReadByte() + 1;
                //Each element of the code array represents the encoding for the 
                //corresponding glyph.
                for (int c = 1; c < ncodes; c++)
                    enc_ar[s.ReadByte()] = c;
            }
            else if ((b & 0x7F) == 1)
            {
                byte nranges = s.ReadByte();
                int rpos = 1;
                for (int c = 0; c < nranges; c++)
                {
                    byte start = s.ReadByte();
                    byte more = s.ReadByte();
                    int end = start + more + 1;
                    for (int k = start; k < end; k++)
                        enc_ar[k] = rpos++;
                }
            }
            else
                throw new NotSupportedException();

            if ((b & 0x80) != 0)
            {
                byte nsuplements = s.ReadByte();
                for (int c = 0; c < nsuplements; c++)
                {
                    byte code = s.ReadByte();
                    ushort glyph_name = s.ReadUShort();

                    var e = Array.IndexOf<int>(_glyph_names, glyph_name);
                    if (e == -1)
                    {
                        //Todo: Log missing char
                        //Note, +1 is added later so don't zero e.
                    }
                    //Have to add 1 since ".notDef" isn't included in the _glyph_names
                    enc_ar[code] = e + 1;
                }
                //throw new NotImplementedException("Font has supplimental encoding data");
            }

            return enc_ar;
        }

        string ReadString(Util.StreamReaderEx s, int len)
        {
            byte[] bytes = new byte[len];
            s.Read(bytes, 0, len);
            return System.Text.ASCIIEncoding.ASCII.GetString(bytes);
        }

        int[] ReadOffsets(int size, Util.StreamReaderEx s)
        {
            var ints = new int[size];
            for (int c = 0; c < size; c++)
                ints[c] = Index.ReadOffset(size, s);
            return ints;
        }

        #endregion

        #region Geometry creation functions

        /// <summary>
        /// Creates glyphs by decoding a charstring format 1 string
        /// </summary>
        private sg_matrix<Path> CreateSG1(int ch, out bool space)
        {
            sg_matrix<Path> sg;

            int index = (byte)ch;
            index = (index < _encoding.Length) ? _encoding[index] : 0;
            sg.SG = ReadGlyph1(index);
            sg.M = _mt;
            space = "space".Equals(GetGlyphName(index));
            return sg;
        }

        private sg_matrix<Path> CreateSG1_NOENC(int ch, out bool space)
        {
            sg_matrix<Path> sg;

            int index = (byte)ch;
            sg.SG = ReadGlyph1(index);
            sg.M = _mt;
            space = "space".Equals(GetGlyphName(index));
            return sg;
        }

        /// <summary>
        /// Creates glyphs by decoding a charstring format 2 string
        /// </summary>
        private sg_matrix<Path> CreateSG2(int ch, out bool space)
        {
            sg_matrix<Path> sg;

            int index = (byte)ch;
            index = (index < _encoding.Length) ? _encoding[index] : 0;
            sg.SG = ReadGlyph2(index);
            sg.M = _mt;
            //space = "space".Equals(GetGlyphName(index));
            space = false; //<-- not used
            return sg;
        }

        private sg_matrix<Path> CreateSG2_NOENC(int ch, out bool space)
        {
            sg_matrix<Path> sg;
            int index = ch;
            sg.SG = ReadGlyph2(index);
            sg.M = _mt;
            space = "space".Equals(GetGlyphName(index));
            return sg;
        }

        /// <summary>
        /// Finds the relevant font descriptor, then renders the
        /// glyph using charstring format 1 or 2
        /// </summary>
        /// <remarks>Used for CID fonts</remarks>
        private sg_matrix<Path> CreateSG3(int ch, out bool space)
        {
            //Translates the charcode into character id.
            //Todo: Isn't it a bit wastefull to do a reverse lookup?
            //      Or is that related with this method being used by 
            //      CID fonts. I.e. ch is more than 256 possible values.
            int cid = Array.IndexOf<int>(_glyph_names, ch);

            //Uses cid to see if this is a space char. (Todo: drop this)
            space = cid == 32 || (cid >= 391) && "space".Equals(_strings[cid - 391]);

            //axis-sally-p8.pdf has a font with no glyph data, except the ndef glyph.
            //Presumably that's intentional. I.e. missing glyphs are to be treated as 
            //ndef.
            if (cid < 0) cid = 0;

            //Fetches the font descriptor for the font this
            //CID belongs to
            var fd = _parser3.ReadFD(cid);
            sg_matrix<Path> sg;

            if (fd.FD.CharstringType == 2)
                sg.SG = ReadGlyph2(cid);
            else
            {
                if (_parser2 == null)
                    _parser2 = new Type1Font<Path>.GlyphParser(null, _glyph_creator);

                sg.SG = ReadGlyph1(cid);
            }
            sg.M = fd.FD.FontMatrix;
            return sg;
        }

        #endregion

        string GetGlyphName(int index)
        {
            if (index == 0) return ".notdef";
            var name = _glyph_names[index];
            return (name < 391) ? Enc.SIDNames[name] : _strings[name - 391];
        }
#if DEBUG
        string GetSIDName(ushort? name)
        {
            if (name == null) return null;
            return (name.Value < 391) ? Enc.SIDNames[name.Value] : _strings[name.Value - 391];
        }
#endif
    }

    #region Delegate
    public delegate sg_matrix<Path> CreateSG<Path>(int ch, out bool space);

    #endregion
}