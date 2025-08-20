//#define CMAP4_INSERT_FFFF_TEST_DATA (Assumes app.CreateTTFont())

//Just intended for testing. Works fine, but can not be used when 
//creating CID fonts.
//
//The advantage of keeping the Glyph IDs is that one can use the
//existing cmap table inside the fonts, instead of creating a new
//one.
//#define KEEP_GIDS //<-- Must match with BuildTables.cs

//Usefull for when dumping fontsubsets to disk. Allows for them to be
//used in editors and such. But this is not needed in PDF documents.
//#define INCLUDE_OS2

//Contains copyright notice. On that note, this class must be fixed
//to respect the "can be embeded" flags. I.e. refuse to embed fonts
//without those flags set.
//#define INCLUDE_STRINGS

//Workaround for bug in SumatraPDF viewer
#define FIX_FOR_SUMATRA

//Workaround for Adobe being confused by carriage return
#define FIX_FOR_ADOBE

using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Read.TrueType;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Render.Font;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// When embeding true type fonts into PDF files one generally only
    /// need to take the characters that are displayed. This class
    /// can be used to create a reduced true type font file with a
    /// selection of glyphs from another font.
    /// </summary>
    /// <remarks>
    /// This is almost a full true type parser, but it makes use of bits
    /// and pieces of the renderer's true type parser. One could of course
    /// combine the two, or extend this one into a full parser.
    /// 
    /// Todo: Change the order tables are written to comply with recomendations
    ///       in the specs (see WriteTo method)
    ///       
    /// On the subject of character encoding
    ///  - Current implemetation does support symbolic fonts, but it is more limited
    ///  - This also limits itself to true type fonts. Some details differs for
    ///    other types.
    ///  - There's a large number of built in cmaps that can be used. I've not
    ///    explored these options, but they're certainly there for a reason.
    ///    Along with a CIDtoGID map, or fine control over where glyphs ends 
    ///    up in the subset font, these are likely quite potent. Should map
    ///    most unicode characters, I'd think.
    ///    
    /// On encoding of symbolic fonts
    /// The symbolic font must either be a built in font or a true type font with a 1 0 cmap table
	/// The font is then created with a 3 0 cmap table. Since it is symbolic, I don't think it matters
	/// for PDF. (This does mean that this impl. won't edit symbolic fonts itself created, but changing
    /// it to support the 3 0 cmap table should be fairly trivial. I just don't want to bother with all
    /// the testing)
	///	 - I.e.unicode charcodes are used against the 1 0 cmap table to fetch glyphs,
	///	 - Then a 3 0 table is constructed to speak the same charcodes. This 3 0 table does
    ///    not conform to any particular character encoding, which is the point of symbolic
    ///    fonts. As opposed to 3 1 which has to conform to unicode.
    ///    
    /// Termionology
    ///  - Raw character code:
    ///    This is the value the character will have in the strings supplied to
    ///    Tj text commands. For CID fonts, we are free to select the values of
    ///    these characters. For plain fonts, they must comform to one of the
    ///    various encoding tables (WinANSI, MacRoman, etc.)
    ///  - Cid (character id) code:
    ///    These are characters "understood" by the font. I.e. you take the raw
    ///    char code, make a table lookup into the approriate encoding table,
    ///    and you have a Cid.
    ///  - Gid (glyph id) code:
    ///    These are indexes internal to the font. Fonts can have "cmap" tables that
    ///    maps from cid to gid. For cid fonts, the font's internal cmap tables
    ///    are ignored (i.e. cid == gid unless there's a cid_to_gid map table).
    ///  - For symbolic fonts, things are a little simpler than described above 
    ///    and below. 
    ///    
    ///  Fetching a glyph
    ///  - There's three stratergies for getting a glyph, depending on the font.
    ///    Note, this is from the perspective of the "reader", so in particular
    ///    for non-cid fonts, implemetation details may differ.
    ///    Also, this is for fonts created by cTTFont, other sofware may do things
    ///    differently.
    ///  - Non-CID:
    ///    The raw charcode is already in a understood format (WinANSI). This means
    ///    copy and paste works, (assuming the font's name is set correctly), and
    ///    also means the character can be converted to unicode (As all ANSI 
    ///    characters can). 
    ///    
    ///    The created font is supplied with a (3.1) cmap. This is a cmap that
    ///    understands unicode. It's also possible to supply a cmap that understands
    ///    ANSI, but this isn't done by cTTFont
    ///    
    ///    The reader then has a ANSI character and a cmap that understands unicode.
    ///    Thus, it translates the ANSI to unciode, then gets the gid by using
    ///    the font's unicode cmap.
    ///  - CID 2 byte encoding:
    ///    When using 2 byte encoding, it means that the raw charcodes use two
    ///    bytes. The CID font is then supplied with two CMaps. One cmap
    ///    translates from raw charcodes to GID, the other from raw charcodes
    ///    to unicode.
    ///    
    ///    For convinience, the raw charcodes conforms to UCS-16. Making it
    ///    possible to read them in text editors. However, this is a cTTFont
    ///    implementation detail.
    ///  - CID 1-byte encoding - Alternative 1
    ///    cTTFont allows for 1-byte CID fonts when the total numer of characters
    ///    is no greater than 90. 
    /// 
    ///    When there's no need to respect existing encodings, one byte encoded
    ///    CID fonts functions just like 2 byte encoded cid fonts. Two cmaps
    ///    are supplied, which does the same job, just consuming 1-byte instead
    ///    of two per character
    ///  - CID 1-byte encoding - Alternative 2
    ///    If the font switches from to "cid font" after characters already have
    ///    been encoded, things are done a bit differently. Basically more work
    ///    is done when creating the 1-byte cmap tables. 
    ///    
    ///    It would be possible to supply the descendant font with a cid_to_gid
    ///    map. One could then consivably use the same cmap as alternative 1,
    ///    though one would still have to create a sepperate to_unicode cmap.
    /// 
    ///  Copy text
    ///  - It must be possible to copy non-symbolic text. This is accomplished
    ///    by two means:
    ///     - For non-cid fonts, encode the text as WinANSI (or MacRoman). It's
    ///       possible to supply a differences dictionary to expand on this, but
    ///       it says in the specs that differences dictionaries shouldn't be used
    ///       with TrueType.
    ///     - For CID fonts, supply a ToUnicode cmap. This cmap maps from raw
    ///       character codes to unicode characters
    ///  - A problem:
    ///    If a character can't be encoded as plain WinANSI (a cTTFont limitation*), 
    ///    then one has to use a CID font. From my reading of the specs, there's no
    ///    supported way to encode nonANSI characters with a non-cid 
    ///    non-symbolic font.
    ///    * But other implementations are still limited to the built in tables.
    /// 
    /// 
    /// </remarks>
    internal class cTTFont : cFont
    {
        #region Variables and properties

        cWrapFont _wrapper;

        /// <summary>
        /// Cached cmap, used for CID fonts so that
        /// the cmap need not be generated multiple times
        /// </summary>
        CMapBuild _my_cmap;
        int _cmap_revision = -1, _next_gid;

        /// <summary>
        /// The maximum number of characters allowed in a single byte encoded CID font.
        /// This number leaves headroom for the space character. 
        /// </summary>
        /// <remarks>
        /// The algo that places the character, can't handle a situation where there's no room.
        /// There's room for 90 characters, including space, and 96 characters, including 
        /// various characters that will be escaped in the final string. When placed out, the
        /// space and escape characers will count towards the total, while the placement algo
        /// will never put anything in those spots.
        /// 
        /// I.e. Since one don't know how many "unremapable" characters has been placed out,
        /// one can never have 96 characters, or even 90 characters, in the current impl, as one
        /// has to assume they've havent been placed out.
        /// </remarks>
        const int MAX_SINGLE_BYTE_CID_CHARACTERS = 89;

        /// <summary>
        /// Font parser
        /// </summary>
        readonly TableDirectory _td;

        /// <summary>
        /// Initially contains unicode ->  WinANSI mapping, but will
        /// contain a different mapping for cid fonts.
        /// 
        /// Can also contain mapping from uincode -> custom, in case
        /// of cid fonts with less than 97 characters.
        /// </summary>
        /// <remarks>
        /// Will be set null when switching to a dual byte encoding.
        /// 
        /// Also, since it's perfectly possible to use int?[256] instead
        /// of a Dictionary. (As with _unicode_to_mb_charcode)
        /// </remarks>
        Dictionary<char, int> _unicode_to_charcode;

        Unicode_to_mb_charcode _unicode_to_mb_charcode;

        class Unicode_to_mb_charcode
        {
            /// <summary>
            /// Bassically a unicode to charcode lookup table. Works like _unicode_to_charcode,
            /// just multibyte
            /// </summary>
            public Dictionary<char, ushort> Index = new Dictionary<char,ushort>(128);

            /// <summary>
            /// A table for charcode -> unicode. Also used to keep track of what charcodes
            /// has been claimed.
            /// </summary>
            public CharAr[] Charcode = new CharAr[256];

            public void AddIndex(char unicode, int high, int low)
            {
                Index.Add(unicode, (ushort)(((high & 0xFF) << 8) | (low & 0xFF)));
            }

            public void NextRange()
            {
                first_free++;
            }

            public char GetCharacter(byte high, byte low)
            {
                var ar = Charcode[high];
                if (ar == null)
                    return '\0';
                var val = ar.Characters[low];
                if (val.HasValue)
                    return val.Value;
                return '\0';
            }

#if FIX_FOR_SUMATRA            
            int first_free = 1;
#else
            int first_free = 0;
#endif

            public IEnumerable<KeyValuePair<int, CharAr>> GetRanges()
            {
                for (int high_byte = 0; high_byte < Charcode.Length; high_byte++)
                {
                    var ca = Charcode[high_byte];
                    if (ca != null && ca.Count > 0 && (high_byte != 0 || ca.Count > 1))
                        yield return new KeyValuePair<int, CharAr>(high_byte, ca);
                }
            }

            public KeyValuePair<int, CharAr> GetFreeRange(Dictionary<char, int> unicode_to_charcode)
            {
                for (int high_byte = first_free; high_byte < Charcode.Length; high_byte++)
                {
#if FIX_FOR_SUMATRA
                    //Prevents 32 to be used
                    if (high_byte == 32)
                        continue;
#else
                    //Prevents 32 to be used when there's a \0 character.
                    if (high_byte == 32 && unicode_to_charcode.ContainsKey('\0'))
                        continue;
#endif

                    var ca = Charcode[high_byte];
                    if (ca == null)
                    {
                        if (!unicode_to_charcode.ContainsKey((char)high_byte))
                        {
                            Charcode[high_byte] = ca = new CharAr();
                            first_free = high_byte;
                            return new KeyValuePair<int, CharAr>(high_byte, ca);
                        }
                    }
                    else
                    {
                        if (!ca.Full)
                        {
                            first_free = high_byte;
                            return new KeyValuePair<int, CharAr>(high_byte, ca);
                        }
                    }
                }

                throw new PdfInternalException("No more free space for glyphs");
            }

            public class CharAr
            {
                public int Count;
                public char?[] Characters = new char?[256];

#if FIX_FOR_ADOBE
                public bool Full { get { return Count == 255; } }
                public bool AlmostFull { get { return Count + (Characters[0] == null ? 2 : 1) >= 255; } }
#else
                public bool Full { get { return Count == 256; } }
                public bool AlmostFull { get { return Count + (Characters[0] == null ? 2 : 1) >= 256; } }
#endif
                public int FirstFree 
                { 
                    get 
                    { 
                        var free = Array.IndexOf<char?>(Characters, null);
#if FIX_FOR_ADOBE
                        if (free == 0x0d /* Carriage return */)
                            free = Array.IndexOf<char?>(Characters, null, 14);
#endif
                        return free;
                    } 
                }

                public int NextFree(int idx) 
                { 
                    var free = Array.IndexOf<char?>(Characters, null, idx + 1);
#if FIX_FOR_ADOBE
                    if (free == 0x0d /* Carriage return */)
                        free = Array.IndexOf<char?>(Characters, null, 14);
#endif
                    return free;
                }

                public bool Contains(int ch)
                {
#if FIX_FOR_ADOBE
                    return ch == 0x0d || Characters[ch] != null;
#else
                    return Characters[ch] != null;
#endif
                }

                public void Set(int low_byte, char character)
                {
                    Count++;
                    Characters[low_byte] = character;
                }

                /// <summary>
                /// Adds the characters in this range as MapRanges
                /// </summary>
                /// <param name="high_byte">The high byte of this range</param>
                internal void AddMaps(int high_byte, List<MapRange> char_ranges, List<MapRange> unicode_ranges, cTTFont font)
                {
                    Debug.Assert(Count != 0);
                    
                    //Moves to the first character in the range
                    int low_byte = 0;
                    while (Characters[low_byte] == null)
                        low_byte++;
                    char unicode = Characters[low_byte].Value;
                    TTGlyphInfo gi = font.FetchGI(unicode);

                    int char_code = (high_byte << 8) | low_byte;
                    int start_character = char_code, stop_character = char_code, start_gid = gi.Gid, end_gid = start_gid;
                    int u_start_char = unicode, u_stop_char = u_start_char, u_start_gid = start_gid, u_end_gid = start_gid;
                    int u_start = char_code, u_end = char_code;
                    for (low_byte++; low_byte < Characters.Length; low_byte++)
                    {
                        while (Characters[low_byte] == null)
                        {
                            low_byte++;
                            if (low_byte == Characters.Length)
                            {
                                low_byte = -1;
                                break;
                            }
                        }
                        if (low_byte == -1) break;

                        unicode = Characters[low_byte].Value;
                        gi = font.FetchGI(unicode);
                        char_code = (high_byte << 8) | low_byte;
                        int gid = gi.Gid;
                        if (stop_character + 1 != char_code || end_gid + 1 != gid)
                        {
                            char_ranges.Add(new MapRange(start_character, stop_character, start_gid) { NBytes = 2 });
                            start_character = stop_character = char_code;
                            start_gid = end_gid = gid;
                        }
                        else
                        {
                            stop_character++;
                            end_gid++;
                        }
                        if (u_stop_char + 1 != unicode || u_end_gid + 1 != gid)
                        {
                            unicode_ranges.Add(new MapRange(u_start_char, u_start, u_end, u_start_gid) { NBytes = 2 });
                            u_start_char = u_stop_char = unicode;
                            u_start_gid = u_end_gid = gid;
                            u_start = u_end = char_code;
                        }
                        else
                        {
                            u_stop_char++;
                            u_end_gid++;
                            u_end++;
                        }
                    }
                    char_ranges.Add(new MapRange(start_character, stop_character, start_gid) { NBytes = 2 });
                    unicode_ranges.Add(new MapRange(u_start_char, u_start, u_end, u_start_gid) { NBytes = 2 });                    
                }
            }
        }
       

        /// <summary>
        /// Used for taking meassurments.
        /// </summary>
        readonly MTTFont _meassure;

        /// <summary>
        /// Used to map unicode charcodes to gid
        /// </summary>
        readonly CmapTable.CmapFormat _unicode_cmap;

        /// <summary>
        /// All glyphs that are used gets registered in this
        /// dictionary. That's how we know what glyphs to
        /// save to the final file.
        /// </summary>
        readonly Dictionary<int, TTGlyphInfo> _glyphs = new Dictionary<int, TTGlyphInfo>();

        /// <summary>
        /// The true number of characters in this font. A font can have more
        /// glyphs than characters, and more characters than glyphs so there's
        /// no easy way to get this number
        /// </summary>
        int _n_chars = 0;

        /// <summary>
        /// This dictionary is used for additional cid to gid mappings
        /// when multiple characters map to the same gid
        /// </summary>
        Dictionary<int, TTGlyphInfo> _cidtogid = null;

        /// <summary>
        /// Set true when Encode is called. Determines how charaters
        /// is to be cmapped. in CID fonts with less than 97 characters
        /// </summary>
        bool _has_encoded;

        /// <summary>
        /// How characters are to be encoded by the "Encode" function.
        /// </summary>
        CharEncodeMode _encode_mode = CharEncodeMode.one_byte;

        //CreateTTFont intially assumes that ANSI encoding is the right choice
        //
        //The advantage of ANSI is that one can copy and paste characters from
        //the document, with the drawback that not all characters can be translated
        //to ANSI (the font automatically switches to CID in this case).
        //
        //It's possible to improve on this by setting the "Difference" array or
        //in some cases make use of other encoding tables. However, ANSI covers
        //commonly used latin characters pretty well, so there's little to gain
        //from doing this, and the specs discurrage usage of Difference with TT
        //fonts.
        CharCodeEncoding _encoding = CharCodeEncoding.WinANSI;


        //
        // When two byte encoding kicks in, time to get very clever.
        //  - CID fonts don't use the embeded cmap. Non-cid's do. This opens up
        //    the posibility of using the same font file for the single byte and
        //    dual byte fonts.
        //  - One can know if there's a need for "single byte" support by tracking
        //    if "Encode" has been called. This flag can be reset after "compile font"
        //    - Do note, two wrappes will result in two "compile font" calls. This
        //      must be handled painlessly.

        /// <summary>
        /// To formalize what "mode" cTTFont is in. Easier to keep track of
        /// than a bunch of bools.
        /// </summary>
        enum CharEncodeMode
        {
            /// <summary>
            /// Text is encoded with one byte per character
            /// </summary>
            one_byte,

            /// <summary>
            /// Tries to be "unicode", but basically preserves whatever single byte
            /// encoding existed when a need for multibyte kicked in. This way,
            /// strings that has been encoded already, need not be encoded again.
            /// </summary>
            /// <remarks>
            /// Has an worst case character limit of ~10000. This is a very high
            /// limit, seeings as the max number of glyphs is ~64000. No special
            /// attention is paid to this limit. Never been tested.
            /// </remarks>
            multibyte,

            /// <summary>
            /// Text is encoded with two bytes per character
            /// </summary>
            two_byte
        }

        enum CharCodeEncoding
        {
            /// <summary>
            /// Characters are not encoded, i.e. unicode input is converted straight to char codes.
            /// </summary>
            None,

            /// <summary>
            /// Characters are encoded in WinANSI format
            /// </summary>
            WinANSI,

            /// <summary>
            /// Characters are encoded in MacRoman format. This is also used for symbolic fonts,
            /// which can be confusing.
            /// </summary>
            //MacRoman,

            /// <summary>
            /// There is no system to how characters are encoded. 
            /// Basically this means the CMap table must be larger.
            /// </summary>
            Random,

            /// <summary>
            /// Charcodes has no meaning, by they are 1 to 1 with glyph indexes.
            /// </summary>
            Uniform,

            /// <summary>
            /// Characters conforms to a unicode encoding. 
            /// </summary>
            Unicode
        }

        /// <summary>
        /// Cache for various values
        /// </summary>
        double _cap_height, _line_height, _descent, _ascent, _ul_pos, _ul_thick;

        /// <summary>
        /// This font's full name
        /// </summary>
        public override string Name { get { return _td.Name.FullName; } }

        /// <summary>
        /// True when this font does not speak unicode, but rather some
        /// other encoding. What we do then is translate unicode directly
        /// to char_codes. 
        /// </summary>
        public override bool IsSymbolic { get { return _is_symbolic; } }
        private bool _is_symbolic;

        /// <summary>
        /// The font's bounding box
        /// </summary>
        public override PdfRectangle FontBBox 
        {
            get
            {
                var head = _td.Head;
                double units_per_em = head.UnitsPerEm;
                double XMax = head.xMax / units_per_em * 1000d;
                double XMin = head.xMin / units_per_em * 1000d;
                double YMax = head.yMax / units_per_em * 1000d;
                double YMin = head.yMin / units_per_em * 1000d;
                return new PdfRectangle((int)XMin, (int)YMin, (int)XMax, (int)YMax);
            }
        }

        /// <summary>
        /// If this font is italic
        /// </summary>
        public override double ItalicAngle
        {
            get 
            {
                var post = _td.Post;
                return (post != null) ? post.ItalicAngle : 0;
            }
        }

        /// <summary>
        /// If this font's characters all have the same width
        /// </summary>
        public override bool IsFixedPitch
        {
            get 
            {
                var post = _td.Post;
                return (post != null) ? post.IsFixedPitch : false;
            }
        }

        /// <summary>
        /// This font is closed, any futher modification will result in
        /// a new font being created.
        /// </summary>
        public override bool IsClosed { get { return _wrapper != null; } }

        /// <summary>
        /// Height from baseline to the top of capital H
        /// </summary>
        public override double CapHeight 
        { 
            get 
            { 
                if (_cap_height == 0)
                    _cap_height = _meassure.CapHeight;
                return _cap_height;
            } 
        }

        /// <summary>
        /// Height from baseline to the top of lower case X
        /// </summary>
        public override double XHeight { get { return _meassure.XHeight; } }

        /// <summary>
        /// Height from baseline to baseline
        /// </summary>
        public override double LineHeight
        {
            get
            {
                if (_line_height == 0)
                    _line_height = _meassure.LineHeight;
                return _line_height;
            }
        }

        /// <summary>
        /// Height from baseline to bottom
        /// </summary>
        public override double Descent
        {
            get 
            {
                if (_descent == 0)
                    _descent = _meassure.Descent;
                return _descent;
            }
        }
        
        /// <summary>
        /// Height from basline to top
        /// </summary>
        public override double Ascent
        {
            get
            {
                if (_ascent == 0)
                    _ascent = _meassure.Ascent;
                return _ascent;
            }
        }

        /// <summary>
        /// The position of the underline relative to the baseline
        /// </summary>
        public override double UnderlinePosition
        {
            get 
            {
                if (_ul_pos == 0)
                    _ul_pos = _meassure.UnderlinePosition;
                return _ul_pos;
            }
        }

        /// <summary>
        /// The recommended thickness of the underline
        /// </summary>
        public override double UnderlineThickness
        {
            get 
            {
                if (_ul_thick == 0)
                    _ul_thick = _meassure.UnderlineThickness;
                return _ul_thick;
            }
        }

        /// <summary>
        /// Whenever this is a cid font or not
        /// </summary>
        private bool _is_cid_font; //Same as: _encode_mode > CharEncodeMode.one_byte || _encoding != WinANSY && _encoding != MacRoman
        public override bool IsCIDFont 
        { 
            get { return _is_cid_font; }
            set
            {
                if (value)
                {
                    //Forces the font into double byte CID mode.
                    // Intended for testing, but can be usefull if you want plain
                    // double byte unicode.
                    if (!IsCIDFont)
                    {
                        var hold = _n_chars;
                        _n_chars = MAX_SINGLE_BYTE_CID_CHARACTERS + 1;
                        NeedsCID(char.MaxValue);
                        _n_chars = hold;
                    }
                }
            }
        }

        /// <summary>
        /// First character in the width array. Only sensible for non-CID fonts.
        /// </summary>
        public override int FirstChar
        {
            get 
            {
                if (IsCIDFont || _glyphs.Count == 0)
                    return 0;
                int low = int.MaxValue;
                if (_encoding == CharCodeEncoding.WinANSI /*|| _encoding == CharCodeEncoding.MacRoman*/)
                {
                    foreach (var kv in _glyphs)
                        if (kv.Value.CharCode > TTGlyphInfo.UNKNOWN)
                            low = Math.Min(low, _unicode_to_charcode[(char)kv.Value.CharCode]);   
                }
                else
                {
                    foreach (var kv in _glyphs)
                        if (kv.Value.CharCode > TTGlyphInfo.UNKNOWN)
                            low = Math.Min(low, kv.Value.CharCode);
                }
                return low;
            }
        }

        /// <summary>
        /// Last character in the width array
        /// </summary>
        public override int LastChar
        {
            get
            {
                if (IsCIDFont || _glyphs.Count == 0)
                    return 0;
                int hi = int.MinValue;
                if (_encoding == CharCodeEncoding.WinANSI /*|| _encoding == CharCodeEncoding.MacRoman*/)
                {
                    foreach (var kv in _glyphs)
                        if (kv.Value.CharCode > TTGlyphInfo.UNKNOWN)
                            hi = Math.Max(hi, _unicode_to_charcode[(char)kv.Value.CharCode]);
                }
                else
                {
                    foreach (var kv in _glyphs)
                        if (kv.Value.CharCode > TTGlyphInfo.UNKNOWN)
                            hi = Math.Max(hi, kv.Value.CharCode);
                }
                return hi;
            }
        }

        /// <summary>
        /// The current widths of the glyphs
        /// </summary>
        public override double[] Widths
        {
            get 
            {
                if (IsCIDFont) return null;
                if (_glyphs.Count == 0) return new double[0];
                
                //First we need to sort the charcodes into the correct order
                var map = new Map[_glyphs.Count];
                int c = 0, not_mapped = 0;
                if (_encoding == CharCodeEncoding.WinANSI /*|| _encoding == CharCodeEncoding.MacRoman*/)
                {
                    foreach (var kv in _glyphs)
                        if (kv.Value.CharCode <= TTGlyphInfo.UNKNOWN)
                            //Unknown glyps are not mapped. Do to their low value
                            //they'll be at the top after the map is sorted, i.e we
                            // keep track of the unmapped glyphs by counting them 
                            //(not_mapped++) and then skipping over them when getting
                            //the widths.
                            map[c++] = new Map(TTGlyphInfo.UNKNOWN, not_mapped++);
                        else
                            map[c++] = new Map(_unicode_to_charcode[(char)kv.Value.CharCode], kv.Key);
                }
                else
                {
                    foreach (var kv in _glyphs)
                        if (kv.Value.CharCode <= TTGlyphInfo.UNKNOWN)
                            map[c++] = new Map(TTGlyphInfo.UNKNOWN, not_mapped++);
                        else
                            map[c++] = new Map(kv.Value.CharCode, kv.Key);
                }
                Array.Sort<Map>(map);

                if (not_mapped >= map.Length)
                    //A font can have 0 mapped glyphs.
                    return new double[0];

                int low = map[not_mapped].From;
                int high = map[map.Length - 1].From;
                double[] widths = new double[high - low + 1];
                for (c = not_mapped; c < map.Length; c++)
                    widths[map[c].From - low] = _meassure.GetWidth(map[c].To);
                return widths;
            }
        }

        struct Map : IComparable
        {
            public int From;
            public int To;
            public Map(int from, int to)
            { From = from; To = to; }
            public int CompareTo(object obj)
            {
                return From - ((Map)obj).From;
            }
            public override string ToString()
            {
                return string.Format("{0} -> {1}", From, To);
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Makes a TTFont
        /// </summary>
        /// <param name="source_font">Source of the font</param>
        internal cTTFont(Stream source_font)
            : this(source_font, null)
        { }

        /// <summary>
        /// Makes a TTFont
        /// </summary>
        /// <param name="source_font">Source of the font</param>
        /// <param name="cmap">
        /// If the font lacks a unicode cmap, one can supply one. (It need not actually be a unicode_cmap)
        /// </param>
        internal cTTFont(Stream source_font, CmapTable cmap)
        {
            //By default we start out with a one byte encoding scheme
            _encode_mode = CharEncodeMode.one_byte;

            _td = new TableDirectory(source_font);
            _unicode_cmap = (cmap ?? _td.Cmap).GetCmap(Read.TrueType.PlatformID.Microsoft, (ushort)MicrosoftEncodingID.encodingUGL);

            //For now, we only support unicode text, fonts that speak other character encodings are treated as symbolic fonts.
            //We also ignore symbolic unicode. Fonts like "Wingdings" has such tables, but I'm unsure what exactly is in said table.
            //Unicode does not cover Wingdings, after all, but has a few equivalent glyphs.
            // (Symbolic fonts will not feature a ToUnicode PSCmap, meaning text copy is a problem, but we try to work around this
            //  by keeping the character codes as native to the font. That increeses the chance that a text copy will work.)
            if (_unicode_cmap == null)
            {
                _is_symbolic = true;

                //We look for another cmap. For now we'll only support 1 byte cmaps. This because we only support 1byte native, though table
                //4,0 may also be usable. Will have to gander on the PDF specs to see if 4,0 is used. 
                _unicode_cmap = (cmap ?? _td.Cmap).GetCmap(Read.TrueType.PlatformID.Macintosh, (ushort) MacintoshEncodingID.encodingRoman);
                if (_unicode_cmap == null)
                    throw new TTException("Needs cmap table 4.1 or 1.0");

                _meassure = new MTTFont(_td, 0);

                //For symbolic fonts, it does not make any sence to do any encoding.
                _unicode_to_charcode = null;
                _encoding = CharCodeEncoding.None;
            }
            else
            {
                //The capital H glyph is used to get the largest measurments from the font. 
                //If there is no such glyph, MTTFont will still work so there's no need to fret.
                _meassure = new MTTFont(_td, _unicode_cmap.GetGlyphIndex((int)'H'));

                //Since we're using the Microsoft cmap
                _encoding = CharCodeEncoding.WinANSI;
                _unicode_to_charcode = Enc.UnicodeToANSI;
            }

            //Glyph 0 is special.
            var gi = CreateGlyphInfo(TTGlyphInfo.UNDEFINED, 0);
            gi.Tag = _meassure.GetGlyphInfo(0);
            //In fact the first four glyphs are special.
            // 0 = .notdef
            // 1 = .null
            // 2 = \r
            // 3 = space
            //
            // However for PDF, only the fist one is required.
            //
            // Other considerations:
            //  Characters 1-1f should mapth to .notdef
            //  Characters 0, 8 and 1D should map to .null
            //  Characters 9, 20, A0 should map to "space"
            //  Characters 9 and 20 should have the same width

            #region Debug code
            /*var g = _td.Glyf;
            int c=190;
            for (; c < 10000; c++)
                if (g.GetGlyph(_unicode_cmap.GetGlyphIndex(c)) is CompositeGlyphs)
                    break;
            c = c * 1;//*/
            #endregion
        }

        #endregion

        #region Render
        bool _is_rendering;

        internal override void InitRenderNoCache(IFontFactory factory)
        {
            _is_rendering = true;
            _wrapper.RenderFont = factory.CreateMemTTFont(_td, IsSymbolic, null);
            _wrapper.RenderFont.SetNullGlyphCache();
            _wrapper.RenderFont.Init();
        }

        internal override void InitRender(IFontFactory factory)
        {
            _is_rendering = true;
            //In this case, the font will set itself up with a unicode cmap. IOW, feed it unicode data, and get ahold of
            //the glyph we want. 
            _wrapper.RenderFont = factory.CreateMemTTFont(_td, IsSymbolic, null);
            _wrapper.RenderFont.Init();

            //if (_cid_font)
            //{

            //}
            //else
            //{
            //    //This sets up the font to speak "our language." With the drawback, that if cFont becomes a cid fonts in the
            //    //midsts of rendering, we'd have to refresh the rTTFont. Instead we'll just translate the characters to unicode
            //    //as we get them.
            //
            //    var cmap = _td.Cmap.GetCmap(Read.TrueType.PlatformID.Microsoft, (int)Read.TrueType.MicrosoftEncodingID.encodingUGL);
            //    if (cmap == null)
            //        throw new NotImplementedException("Support for non-unicode cmaps");
            //    ushort[] ansi_to_unicode;
            //    if (_encode_mode == CharEncodeMode.WinANSI)
            //    {
            //        ansi_to_unicode = PdfEncoding.CreateUshortUnicodeEnc(Enc.WinANSI);

            //        //Retargets ANSI->gid
            //        for (ushort c = 0; c < ansi_to_unicode.Length; c++)
            //            ansi_to_unicode[c] = (ushort)cmap.GetGlyphIndex(ansi_to_unicode[c]);
            //    }
            //    else
            //    {
            //        //Sets up one to one mapping
            //        ansi_to_unicode = new ushort[256];
            //        for (ushort c = 0; c < ansi_to_unicode.Length; c++)
            //            ansi_to_unicode[c] = (ushort)cmap.GetGlyphIndex(c);
            //    }
            //    _wrapper.RenderFont = _wrapper.Factory.CreateMemTTFont(_td, false, ansi_to_unicode);
            //}
        }

        internal override rGlyph GetGlyph(int char_code, bool is_space, int n_bytes)
        {
            switch(_encode_mode)
            {
                case CharEncodeMode.one_byte:
                    switch(_encoding)
                    {
                        case CharCodeEncoding.None:
                            return _wrapper.RenderFont.GetCachedGlyph((char)char_code, is_space);

                        case CharCodeEncoding.WinANSI:
                            return _wrapper.RenderFont.GetCachedGlyph(Enc.GetUnicodeFromAnsi((byte)char_code), is_space);

                        //case CharCodeEncoding.MacRoman:
                        //    return _wrapper.RenderFont.GetCachedGlyph(Enc.GetUnicodeFromRoman((byte)char_code), is_space);

                        case CharCodeEncoding.Random:
                        case CharCodeEncoding.Uniform:
                            //Does a reverse lookup in the _unicode_to_charcode dictionary. Slow, but should
                            //be fast enough, as there's never all that many glyphs there.
                            foreach (var kp in _unicode_to_charcode)
                            {
                                if (kp.Value == char_code)
                                    return _wrapper.RenderFont.GetCachedGlyph(kp.Key, is_space);
                            }
                            return _wrapper.RenderFont.GetCachedGlyph('\0', is_space);

                        default:
                            throw new PdfNotSupportedException("Single byte "+_encoding);
                    }

                case CharEncodeMode.multibyte:
                    switch(_encoding)
                    {
                        case CharCodeEncoding.None:
                            return _wrapper.RenderFont.GetCachedGlyph((char)char_code, is_space);

                        case CharCodeEncoding.Random:
                            if (n_bytes == 1)
                            {
                                //Does a reverse lookup in the _unicode_to_charcode dictionary. Slow, but should
                                //be fast enough, as there's never all that many glyphs there.
                                foreach (var kp in _unicode_to_charcode)
                                {
                                    if (kp.Value == char_code)
                                        return _wrapper.RenderFont.GetCachedGlyph(kp.Key, is_space);
                                }

                                //.ndef
                                return _wrapper.RenderFont.GetCachedGlyph(0, is_space);
                            }

                            //Does a reverse lookup for the unicode character.
                            byte high = (byte)(char_code >> 8), low = (byte)char_code;
                            return _wrapper.RenderFont.GetCachedGlyph(_unicode_to_mb_charcode.GetCharacter(high, low), is_space);

                        default: throw new PdfNotSupportedException("Multibyte char encoding: " + _encoding);
                    }

                case CharEncodeMode.two_byte:
                    switch (_encoding)
                    {
                        case CharCodeEncoding.Unicode:
                            return _wrapper.RenderFont.GetCachedGlyph(char_code, is_space);

                        default: throw new PdfNotSupportedException("Two byte char encoding: " + _encoding);
                    }

                default: throw new NotImplementedException("Encoding mode "+_encode_mode);
            }
        }

        internal override int GetNBytes(byte b)
        {
            switch (_encode_mode)
            {
                case CharEncodeMode.two_byte:
                    return 2;
                case CharEncodeMode.multibyte:
                    foreach (var kp in _unicode_to_charcode)
                    {
                        if (kp.Value == b)
                            return 1;
                    }
                    return 2;
                default:
                    return 1;
            }
        }

        internal override void DissmissRender()
        {
            _is_rendering = false;
            //throw new NotImplementedException();
        }

        internal override void DisposeRender()
        {
            //throw new NotImplementedException();
        }

        #endregion

        #region Encode

        /// <summary>
        /// This function converts a string into bytes the font
        /// can understand. It handles both single byte and dual
        /// byte characters
        /// </summary>
        /// <param name="str">Unicode string to convert</param>
        /// <returns>A string of bytes to embed into the PDF document</returns>
        public override byte[] Encode(string str)
        {
            //We assume that all characters used in the string is
            //in the font. It's not impossible to alter the function
            //to add new characters to the font.

            byte[] ret;
            _has_encoded = true;

            switch(_encode_mode)
            {
                default: throw new NotImplementedException();

                case CharEncodeMode.one_byte:
                    switch (_encoding)
                    {
                        default:
                        case CharCodeEncoding.None:
                            return Read.Lexer.GetBytes(str);

                        case CharCodeEncoding.Random:
                        case CharCodeEncoding.Uniform:
                        case CharCodeEncoding.WinANSI:
                        //case CharCodeEncoding.MacRoman:
                            Debug.Assert(_wrapper != null, "If there's no wrapper then _unicode_to_charcode may be out of date."); //Or you have forgotten to set the font in the first place
                            ret = new byte[str.Length];
                            for (int c = 0; c < str.Length; c++)
                            {
                                char test = str[c];
                                if (!_unicode_to_charcode.ContainsKey(test))
                                    ret[c] = 0;
                                else
                                    ret[c] = (byte)_unicode_to_charcode[str[c]];
                            }
                            return ret;
                    }

                case CharEncodeMode.multibyte:
                    switch (_encoding)
                    {
                        default: throw new NotSupportedException();

                        case CharCodeEncoding.Random:
                            ret = new byte[str.Length * 2];
                            int r_pos = 0;
                            for (int c = 0; c < str.Length; c++)
                            {
                                char val = str[c];
                                int high = ((val >> 8) & 0xFF);
                                int ch;
                                if (_unicode_to_charcode.TryGetValue(val, out ch))
                                {
                                    //Single byte encoded
                                    ret[r_pos++] = (byte)ch;
                                    continue;
                                }
                                ushort high_us;
                                if (_unicode_to_mb_charcode.Index.TryGetValue(val, out high_us))
                                {
                                    //Dual byte encoded
                                    ret[r_pos++] = (byte)((high_us >> 8) & 0xFF);
                                    ret[r_pos++] = (byte)(high_us & 0xFF);
                                    continue;
                                }

                                //Character not mapped, we therefore set it to zero. Zero can be mapped to \0,
                                //but this is an error situation anyway, so no need to bother searching for
                                //an .ndef.
                                //
                                //Also note, we don't want to point at '\0', we want to point at a .ndef, which
                                //is a "not defined" character. Any undefined character will do, but I prefer to
                                //have actual zeroes in the output, even if it's not 100% right. 

                                //Null is single byte encoded
                                ret[r_pos++] = 0;

                                if (!_unicode_to_charcode.ContainsKey('\0'))
                                {
                                    //Null is dual byte encoded
                                    ret[r_pos++] = 0;
                                }
                            }
                            Array.Resize<byte>(ref ret, r_pos);
                            return ret;
                    }

                case CharEncodeMode.two_byte:
                    switch(_encoding)
                    {
                        default: throw new NotSupportedException();

                        case CharCodeEncoding.Unicode:
                            //One to one unicode mapping.
                            ret = new byte[str.Length * 2];
                            for (int c = 0, r_p = 0; c < str.Length; c++)
                            {
                                int val = str[c];
                                ret[r_p++] = (byte)((val >> 8) & 0xFF);
                                ret[r_p++] = (byte)(val & 0xFF);
                            }
                            return ret;
                    }
            }
            
        }

        #endregion

        #region Font metrics

        /// <summary>
        /// Fetches the kerning between two glyphs
        /// </summary>
        /// <param name="from_gid">Left hand glyph</param>
        /// <param name="to_gid">Right hand glyph</param>
        /// <returns>Their kerning</returns>
        public override double GetKerning(int from_gid, int to_gid)
        {
            return _meassure.GetKerning(from_gid, to_gid);
        }

        /// <summary>
        /// Switches the font to cid mode, if needed.
        /// </summary>
        private void NeedsCID(char ch)
        {
            Debug.Assert(_encode_mode == CharEncodeMode.one_byte,"This function must only be used in ANSI or NONE modes.");
            bool cid_font;

            if (_encoding ==  CharCodeEncoding.WinANSI)
                cid_font = !_unicode_to_charcode.ContainsKey(ch);
            //else if (_encoding == CharCodeEncoding.MacRoman)
            //{
            //    if (!_unicode_to_charcode.ContainsKey(ch))
            //        throw new TTException("This font does not support char code: " + ((int)ch).ToString());
            //    cid_font = false;
            //}
            else cid_font = ((int)ch) > 255;

            if (cid_font)
            {
                _is_cid_font = true;

                if (_n_chars <= MAX_SINGLE_BYTE_CID_CHARACTERS)
                {
                    //Note. _unicode_to_charcode was previously used to verify that a charcode had
                    //a "ansi" representation. Now it gets repurposed for use when encoding unicode
                    //strings into charcodes.
                    //
                    //This initial table will contain Unicode -> ANSI mappings, while future enteries
                    //will map from unicode -> any avalible character
                    _unicode_to_charcode = CreateUtoChDict();

                    //Encode mode can also be Uniform_1byte, but only if "compile font" is called withouth
                    //the _has_encoded flag set true. Random is therefore assumed until proven otherwise
                    //_encode_mode = CharEncodeMode.one_byte;
                    _encoding = CharCodeEncoding.Random;
                }
                else
                {
                    if (_has_encoded)
                    {
                        //Must switch from ANSI -> multibyte.
                        _unicode_to_charcode = CreateUtoChDict();
                        _unicode_to_mb_charcode = new Unicode_to_mb_charcode();
                        _encode_mode = CharEncodeMode.multibyte;
                        _encoding = CharCodeEncoding.Random;
                    }
                    else
                    {
                        _encode_mode = CharEncodeMode.two_byte;
                        _encoding = CharCodeEncoding.Unicode;
                    }
                }
            }
        }

        /// <summary>
        /// Create Unicode to Character dictionary
        /// </summary>
        /// <returns></returns>
        private Dictionary<char, int> CreateUtoChDict()
        {
            // _unicode_to_charcode contains a mapping already (to ANSI) or is
            // null for 1 to 1 mapping. We already know witch glyphs that may have
            // been encoded already. (I.e. all glyphs in _glyphs).
            //
            // So we make a new _unicode_to_charcode dict, with all known glyps at their
            // current mapping. Then slot in new glyps into the holes.
            var unicode_to_charcode = new Dictionary<char, int>(MAX_SINGLE_BYTE_CID_CHARACTERS + 1);
            //Debug.Assert(_encoding != CharCodeEncoding.MacRoman, "Haven't added code for MacRoman");

            //Creates mappings for existing characters.
            foreach (var glyph in _glyphs)
            {
                var info = glyph.Value;
                if (info.CharCode > TTGlyphInfo.UNKNOWN)
                {
                    int dest_cc;
                    if (_encoding == CharCodeEncoding.WinANSI)
                        dest_cc = _unicode_to_charcode[(char)info.CharCode];
                    else
                        dest_cc = info.CharCode;

                    unicode_to_charcode.Add((char)info.CharCode, dest_cc);

                    Debug.Assert(dest_cc <= byte.MaxValue);
                }
            }

            if (_cidtogid != null)
            {
                //Multiple characters can map to the same gid. This dictionary contain those
                //characters
                foreach (var glyph in _cidtogid)
                {
                    //The unicode charcode is in the key, not the value
                    int dest_cc;
                    if (_encoding == CharCodeEncoding.WinANSI)
                        dest_cc = _unicode_to_charcode[(char)glyph.Key];
                    else
                        dest_cc = glyph.Key;

                    unicode_to_charcode.Add((char)glyph.Key, dest_cc);

                    Debug.Assert(dest_cc <= byte.MaxValue);
                }
            }
            Debug.Assert(unicode_to_charcode.Count == _n_chars);

            return unicode_to_charcode;
        }

        /// <summary>
        /// Adds a unicode to "arbitrary char code" entery to the _unicode_to_charcode table
        /// </summary>
        /// <param name="unicode_character">Unicode character to add</param>
        private void AddNewEncoding(char unicode_character)
        {
            //Debug.Assert(_encoding != CharCodeEncoding.MacRoman, "Haven't added code for MacRoman");
            int ch; Unicode_to_mb_charcode.CharAr range;
            if (_encoding == CharCodeEncoding.WinANSI ||
                _encoding == CharCodeEncoding.Unicode)
                return;

            if (_encode_mode == CharEncodeMode.one_byte)
            {
                if (_n_chars == MAX_SINGLE_BYTE_CID_CHARACTERS)
                {
                    if (!_has_encoded)
                    {
                        _unicode_to_charcode = null;
                        _encode_mode = CharEncodeMode.two_byte;
                        _encoding = CharCodeEncoding.Unicode;
                        _is_cid_font = true;
                        return;
                    }

                    //Must switch from single byte to multibyte
                    if (_encoding <= CharCodeEncoding.WinANSI)
                        _unicode_to_charcode = CreateUtoChDict();
                    _unicode_to_mb_charcode = new Unicode_to_mb_charcode();
                    _encode_mode = CharEncodeMode.multibyte;
                    _is_cid_font = true;
                }
                else
                {
                    //First check if the prefered spot is open. This will always map the
                    //space character, as nothing else will ever be placed there. Do note that
                    //characters '(', ')', '<', '>' and '\' also slips into here. This isn't
                    //intended, but neither does it hurt much.
                    if (' ' <= unicode_character && unicode_character <= '~' && !char_in_use((int)unicode_character))
                    {
                        _unicode_to_charcode.Add(unicode_character, (int)unicode_character);
                        return;
                    }

                    //32 = space, and numbers less than 32 are control character, so we start searching at 33
                    ch = 33;

                    //This seach is innefficient, but there will never be more than 90 values, so
                    //it shouldn't be too bad.
                    while (true)
                    {
                        if (!char_in_use(ch))
                        {
                            _unicode_to_charcode.Add(unicode_character, ch);
                            break;
                        }

                        //Search for the next character
                        ch++;

                        //Charcodes that shouldn't be used: '(' 40, ')' 41, '<' 60, '>' 62, '\' 92. (They won't break
                        //anything if they are used, though, just bloat the resulting PDF strings with escape characters)
                        switch (ch)
                        {
                            case 40: ch += 2; break;
                            case 60:
                            case 62:
                            case 92:
                                ch++;
                                break;
                        }

                        if (ch == 127) //This should never happen. The function must never be called when there's not enough room.
                            throw new PdfInternalException("Failed to find room for unicode character");
                    }

                    return;
                }
            }

            if (_encode_mode != CharEncodeMode.multibyte)
                throw new NotImplementedException();

            //Space must have codepoint 32
#if FIX_FOR_SUMATRA
            if (' ' == unicode_character /*|| '\0' == unicode_character - sumatra hates this too */)
#else
            if (' ' == unicode_character && _unicode_to_charcode.ContainsKey('\0'))
#endif
            {
                //It is not possible to map this to 0032, under which circumstance, the 32
                //slot will be kept free.
                _unicode_to_charcode.Add(unicode_character, (int)unicode_character);
                return;
            }

            //Splits the character into two bytes
            ch = (int) unicode_character;
            int high = (ch >> 8) & 0xFF;
            int low = ch & 0xFF;

            //First, tries placing the character in its perfered position
#if FIX_FOR_SUMATRA
            if (high != 0 && high != 32 && !_unicode_to_charcode.ContainsKey((char)high))
#else
            if ((high != 32 || !_unicode_to_charcode.ContainsKey('\0')) && !_unicode_to_charcode.ContainsKey((char)high))
#endif
            {
                range = _unicode_to_mb_charcode.Charcode[high];
                if (range == null)
                    range = _unicode_to_mb_charcode.Charcode[high] = new Unicode_to_mb_charcode.CharAr();
                if (!range.Contains(low))
                {
                    range.Set(low, unicode_character);
                    _unicode_to_mb_charcode.AddIndex(unicode_character, high, low);
                    return;
                }
            }

            //Gets the first free range.
            //We don't have to worry about ' ' (space), as we won't get the 32 range unless it's safe to use
            var kp = _unicode_to_mb_charcode.GetFreeRange(_unicode_to_charcode);
            range = kp.Value;

            //First we try the prefered low byte, and if not avalible, we go for the first free.
            //It could be an idea to exclude '\' '(' and ')' characters, as they will be escaped
            //when encoded into a PDF string.
#if FIX_FOR_SUMATRA
            if (range.Contains(low))
                low = range.FirstFree;
#else
            if (range.Contains(low) || (low == 0 || low == 32) && kp.Key == 0)
            {
                //Skips the first pos on range 0, as that is reserved for the \0 character. This
                //is merely as I prefer to have zero be zero. 
                low = kp.Key == 0 ? range.NextFree(0) : range.FirstFree;

                if (low == 32 && kp.Key == 0)
                {
                    //Space character (0032) is reserved, so it must be left free.
                    if (range.AlmostFull)
                    {
                        //With space reserved, this range is now full. 
                        //So we move to the next free range.
                        _unicode_to_mb_charcode.NextRange();
                        kp = _unicode_to_mb_charcode.GetFreeRange(_unicode_to_charcode);
                        range = kp.Value;
                        if (range.Contains(low))
                            low = range.FirstFree;
                    }
                    else
                    {
                        //There's free space after pos 32, so we slot it in there.
                        low = range.NextFree(low);
                    }
                }
            }
#endif

            range.Set(low, unicode_character);
            _unicode_to_mb_charcode.AddIndex(unicode_character, kp.Key, low);

        }

        private bool char_in_use(int asci_character)
        {
            foreach (var kp in _unicode_to_charcode)
            {
                if (kp.Value == asci_character)
                    return true;
            }
            return false;
        }

        public override chBox GetGlyphBox(char ch)
        {
            var gid = _unicode_cmap.GetGlyphIndex((ushort)ch);
            return _meassure.GetGlyphBox(gid);
        }

        /// <summary>
        /// Gets the gid and advance width of a glyph
        /// </summary>
        /// <param name="ch">Unicode character</param>
        /// <returns>The gid and width</returns>
        public override chGlyph GetGlyphMetrix(char ch)
        {
            var gid = _unicode_cmap.GetGlyphIndex((ushort)ch);

            TTGlyphInfo gi;
            if (_glyphs.TryGetValue(gid, out gi))
            {
                if (gi.CharCode != ch)
                {
                    if (gi.CharCode == TTGlyphInfo.UNKNOWN)
                    {
                        //This glyph has been registered as part
                        //of a composite glyph. Since composit
                        //glyphs don't care about the charcode
                        //they simply set it UNKNOWN
                        gi.CharCode = ch;
                        _n_chars++;
                    }
                    else
                    {
                        //A gid can have multiple charcode
                        //mappings. (That is to say, multiple
                        //characters map to the same glyph)
                        //This only matters for the
                        //cmap bulider so there's no need to
                        //do anything for with the _glyphs
                        //
                        //Note "UNDEFINED" can too have 
                        //multiple mappings, so it's treated
                        //like all else.
                        if (_cidtogid == null)
                            _cidtogid = new Dictionary<int, TTGlyphInfo>(16);

                        if (!_cidtogid.ContainsKey(ch))
                        {
                            if (IsCIDFont)
                                AddNewEncoding(ch);
                            else
                            {
                                NeedsCID(ch);
                                if (IsCIDFont)
                                    AddNewEncoding(ch);
                            }

                            _cidtogid.Add(ch, gi);
                            _n_chars++;
                        }
                    }
                }

                return (chGlyph)gi.Tag;
            }
            else
            {
                if (IsCIDFont)
                    AddNewEncoding(ch);
                else
                {
                    NeedsCID(ch);
                    if (IsCIDFont)
                        AddNewEncoding(ch);
                }

                //Note since glyph 0 is always in the table, we
                //don't have to worry about it being mapped to
                //a spesific character and ending up with a
                //gid other than 0

                _n_chars++;
                gi = CreateGlyphInfo(ch, gid);
                var ch_g = _meassure.GetGlyphInfo(gid);
                gi.Tag = ch_g;
                return ch_g;
            }
        }

        private TTGlyphInfo CreateGlyphInfo(int charcode, int gid)
        {
            var ret = new TTGlyphInfo(charcode);
            _glyphs.Add(gid, ret);
            return ret;
        }

        #endregion

        #region Create PDF Font

        public override PdfFont MakeWrapper()
        {
            if (_wrapper != null)
                return _wrapper;

            var dict = new TemporaryDictionary();
            dict.SetType("Font");
            dict.SetName("Subtype", "TrueType");

            //_wrapper = CreateTTFont.Create(this, _cid_font);
            _wrapper = new cWrapFont(this, dict);
            return _wrapper;
        }

        internal override void CompileFont(PdfDictionary dict)
        {
            CreateTTFont.Create(this, IsCIDFont, dict);   
        }

        /// <summary>
        /// Creates two post script cmaps, one for cp to cid,
        /// the other for cp to unicode
        /// </summary>
        /// <returns>A width array</returns>
        internal PdfArray CreateCMaps(out PdfCmap to_cid, out PdfCmap to_unicode)
        {
#if KEEP_GIDS
            //Keep gids is just experimental, and does not seem to be all that usefull,
            //so for now it's simply not implemented for CID fonts.
            throw new NotImplementedException();
#endif

            switch(_encode_mode)
            {
                case CharEncodeMode.multibyte:
                    return CreateMBCmaps(out to_cid, out to_unicode);

                case CharEncodeMode.two_byte:
                    return CreateUCFCMaps(out to_cid, out to_unicode);

                default:
                    if (_has_encoded)
                        return CreateByteCMapsR(out to_cid, out to_unicode);
                    //When has_encoded = false, we are free to alter the character's
                    //char codes as we see fit. This function rewrites the
                    //_unicode_to_charcode dictionary. 
                    return CreateByteCMaps(out to_cid, out to_unicode);
            }
        }


        /// <summary>
        /// Creates two post script cmaps, one for cp to cid,
        /// the other for cp to unicode
        /// </summary>
        /// <returns>A width array</returns>
        PdfArray CreateUCFCMaps(out PdfCmap to_cid, out PdfCmap to_unicode)
        {
#region 0. Prep

            //Use double byte encoding and leave characters as unicode
            //Set up a CMap or CIDTOGID map that translates to GID
            //Advantage: Can use a simplified "ToUnicode" cmap (Based on Identity-0)
            //Drawback: Double byte encoding

            //The cmap build table does the heavy lifting of sorting the charcodes
            //and assigning them gid values.
            //
            //Note that CID fonts don't actually use this cmap, it's not embeded, 
            //this is just code reuse.
            SetMyCmap();

#endregion

#region 1. Unicode -> charcode cmap

            //By nulling this we make the encoding rutine
            //do one to one encoding.

            _unicode_to_charcode = null;

#endregion

#region 2. Charcode -> gid cmap
            //A plain cidtogid map is probably preferable, but we'll stick to making a cmap cidtogid for now
            var org_ranges = _my_cmap.Ranges;
            var cmap = new PSCMap(false);
            cmap.AddCodespaceRange(0, ushort.MaxValue, 2);

            for (int c = 0, gid = 1; c < org_ranges.Length; c++)
            {
                var range = org_ranges[c];
                if (range.Gids != null)
                {
                    int start = range.StartChar, end = range.EndChar;
                    var gids = range.Gids; int g_pos = 0;
                    while (start <= end)
                        cmap.AddNextChar(start++, gids[g_pos], 2);
                }
                else
                {
                    if (range.Length == 1)
                        cmap.AddNextChar(range.StartChar, gid++, 2);
                    else
                    {
                        cmap.AddRange(range.StartChar, range.EndChar, gid, 2);
                        gid += range.Length;
                    }
                }
            }

            to_cid = PdfCmap.Create(cmap.Compile(), cmap.Name);

#endregion

#region 3. Charcode -> unicode cmap

            cmap = new PSCMap(true);
            cmap.AddCodespaceRange(0, ushort.MaxValue, 2);
            cmap.AddRange(0, ushort.MaxValue, 0, 2);
            to_unicode = PdfCmap.Create(cmap.Compile(), cmap.Name);

#endregion

#region 4. Charcode -> charwidth

            var mtx = new chMTX();
            bool has_set_null = false; //<-- if the \0 glyph's width has been set

            for (int c = 0, gid = 1; c < org_ranges.Length; c++)
            {
                var range = org_ranges[c];
                if (range.Gids == null)
                {
                    for (int start = range.StartChar; start <= range.EndChar; start++)
                    {
                        if (_cidtogid != null)
                        {
                            if (gid == 0 && !has_set_null)
                            {
                                has_set_null = true;

                                //Default with == 1. So one can skip those,
                                //but then there's the potential problem of
                                //casees where DW has been changed
                                //if (_meassure.GetWidth(0) == 1)
                                //    continue;
                            }
                            else
                            {
                                //With the exception of glyph 0, these will be set
                                if (_cidtogid.ContainsKey(start))
                                    continue;
                            }
                        }
                        mtx.Add(gid++, _meassure.GetWidth(_unicode_cmap.GetGlyphIndex(start)));
                    }
                }
                else
                {
                    var gids = range.Gids;
                    for (int start = range.StartChar, g_pos = 0; start <= range.EndChar; start++)
                    {
                        var the_gid = gids[g_pos++];
                        if (_cidtogid != null)
                        {
                            if (the_gid == 0 && !has_set_null)
                            {
                                has_set_null = true;

                                //Default with == 1. So one can skip those,
                                //but then there's the potential problem of
                                //casees where DW has been changed
                                //if (_meassure.GetWidth(0) == 1)
                                //    continue;
                            }
                            else
                            {
                                //With the exception of glyph 0, these will be set
                                if (_cidtogid.ContainsKey(start))
                                    continue;
                            }
                        }
                        mtx.Add(the_gid, _meassure.GetWidth(_unicode_cmap.GetGlyphIndex(start)));
                    }
                }
            }

            return (Precision == 0) ? mtx.CreateInt() : mtx.CreateReal(Precision);

#endregion
        }

        PdfArray CreateMBCmaps(out PdfCmap to_cid, out PdfCmap to_unicode)
        {
#region Sort ranges

            //The cmap build table does the heavy lifting of sorting the charcodes
            //and assigning them gid values.
            //
            //Note that CID fonts don't actually use this cmap, it's not embeded, 
            //this is just code reuse.
            SetMyCmap();

            //We got 1 byte and 2 byte ranges. One byte ranges in _unicode_to_char, and
            //two byte ranges in _unicode_to_char_mb

            //Makes the one byte range maps.
            var char_ranges = new List<MapRange>(16);
            var unicode_ranges = new List<MapRange>(16);
            SortOneByteRanges(char_ranges, unicode_ranges);

            //Makes the two byte range maps.
            foreach (var range in _unicode_to_mb_charcode.GetRanges())
                range.Value.AddMaps(range.Key, char_ranges, unicode_ranges, this);

            //Sorts the ranges.
            char_ranges.Sort(CompareRange);
            unicode_ranges.Sort(CompareRange);

            //Combines ranges.
            // Not done, but here's where to do it. 

#endregion

#region Sets code space

            var ps_unicode = new PSCMap(true);
            var ps_cmap = new PSCMap(false);

            //There's no need to set unicode and cmap differently, as
            //regardless, they will read the same amount of data.
            if (char_ranges.Count > 0)
            {
                var range = char_ranges[0];
                int start = range.Start, end = range.End, nbytes = range.NBytes;
                for (int c = 1; c < char_ranges.Count; c++)
                {
                    range = char_ranges[c];
                    if (range.NBytes != nbytes)
                    {
                        ps_cmap.AddCodespaceRange(start, end, nbytes);
                        ps_unicode.AddCodespaceRange(start, end, nbytes);
                        start = range.Start;
                        end = range.End;
                        nbytes = range.NBytes;
                    }
                    else
                        end = range.End;
                }
                ps_cmap.AddCodespaceRange(start, end, nbytes);
                ps_unicode.AddCodespaceRange(start, end, nbytes);
            }
            
#endregion

#region 2. Charcode -> gid cmap

            //Then we add all the range information
            foreach (var range in char_ranges)
            {
                if (range.Length == 1)
                    ps_cmap.AddNextChar(range.Start, range.gid, range.NBytes);
                else
                    ps_cmap.AddRange(range.Start, range.End, range.gid, range.NBytes);
            }

            to_cid = PdfCmap.Create(ps_cmap.Compile(), ps_cmap.Name);

#endregion

#region 3. Charcode -> unicode cmap

            //Then we add all the range information
            foreach (var range in unicode_ranges)
            {
                if (range.Length == 1)
                    ps_unicode.AddNextChar(range.Start, range.UnicodeStart, range.NBytes);
                else
                    ps_unicode.AddRange(range.Start, range.End, range.UnicodeStart, range.NBytes);
            }

            to_unicode = PdfCmap.Create(ps_unicode.Compile(), ps_unicode.Name);

#endregion

#region Creates widths

            return CreateWidths(unicode_ranges);

#endregion
        }

        /// <summary>
        /// Sorts so that ranges follow in an order independed of byte size
        /// I.e.
        /// 00 -> 80
        /// 8140 -> 9FFC
        /// A0 -> DF
        /// </summary>
        static int CompareRange(MapRange a, MapRange b)
        {
            //Using 4 - nbytes can result in overflow, as this is ints. I belive PDF files are limited
            //to two characters, so this shouldn't be a problem.
            Debug.Assert(a.NBytes < 3 && b.NBytes < 3);
            return (a.Start << (8 * (2 - a.NBytes))).CompareTo(b.Start << (8 * (2 - b.NBytes)));
        }

        private TTGlyphInfo FetchGI(char unicode)
        {
            TTGlyphInfo gi;
            int gid = _unicode_cmap.GetGlyphIndex(unicode);
            if (_glyphs.TryGetValue(gid, out gi) && gi.CharCode == unicode)
                return gi;
            return _cidtogid[unicode];
        }

        /// <summary>
        /// For creating non-uniform one byte per character cmaps
        /// </summary>
        PdfArray CreateByteCMapsR(out PdfCmap to_cid, out PdfCmap to_unicode)
        {
#region Sort ranges

            //The cmap build table does the heavy lifting of sorting the charcodes
            //and assigning them gid values.
            //
            //Note that CID fonts don't actually use this cmap, it's not embeded, 
            //this is just code reuse.
            SetMyCmap();

            //Makes the range maps.
            var char_ranges = new List<MapRange>(16);
            var unicode_ranges = new List<MapRange>(16);
            SortOneByteRanges(char_ranges, unicode_ranges);

#endregion

            return CreateSBCmap(unicode_ranges, char_ranges, out to_cid, out to_unicode);
        }

        private void SortOneByteRanges(List<MapRange> char_ranges, List<MapRange> unicode_ranges)
        {
            //In this case, all characters I wish to use is in the _unicode_to_charcode array.
            //First thing is to put them into a list and and sort them.
            var all_characters = new List<KeyValuePair<int, TTGlyphInfo>>(_unicode_to_charcode.Count);
            foreach (var ch in _unicode_to_charcode)
            {
                int key = _unicode_cmap.GetGlyphIndex((ushort)ch.Key);
                TTGlyphInfo gi;
                if (!_glyphs.TryGetValue(key, out gi) || gi.CharCode <= TTGlyphInfo.UNKNOWN)
                {
                    //Multiple characters can point at the same glyph. This is solved by putting
                    //these characters in the _cidtogid dictionary. So when a character isn't in
                    //the _glyphs, we know it's in the _cidtogid
                    gi = _cidtogid[ch.Key];

                    //Puts the right unicode character into gi.CharCode.
                    gi = gi.Clone();
                    gi.CharCode = ch.Key;
                }
                all_characters.Add(new KeyValuePair<int, TTGlyphInfo>(ch.Value, gi));
            }
            all_characters.Sort(CompareKey);
            int n_chars = all_characters.Count;

            if (n_chars > 0)
            {
                var kp = all_characters[0];
                int start_character = kp.Key, stop_character = start_character, start_gid = kp.Value.Gid, end_gid = start_gid;
                int u_start_char = kp.Value.CharCode, u_stop_char = u_start_char, u_start_gid = start_gid, u_end_gid = u_start_gid;
                int u_start = start_character, u_end = stop_character;
                for (int c = 1; c < n_chars; c++)
                {
                    kp = all_characters[c];
                    int char_code = kp.Key;
                    int gid = kp.Value.Gid;
                    if (stop_character + 1 != char_code || end_gid + 1 != gid)
                    {
                        char_ranges.Add(new MapRange(start_character, stop_character, start_gid));
                        start_character = stop_character = char_code;
                        start_gid = end_gid = gid;
                    }
                    else
                    {
                        stop_character++;
                        end_gid++;
                    }
                    int unicode = kp.Value.CharCode;
                    if (u_stop_char + 1 != unicode || u_end_gid + 1 != gid)
                    {
                        unicode_ranges.Add(new MapRange(u_start_char, u_start, u_end, u_start_gid));
                        u_start_char = u_stop_char = unicode;
                        u_start_gid = u_end_gid = gid;
                        u_start = u_end = char_code;
                    }
                    else
                    {
                        u_stop_char++;
                        u_end_gid++;
                        u_end++;
                    }
                }
                char_ranges.Add(new MapRange(start_character, stop_character, start_gid));
                unicode_ranges.Add(new MapRange(u_start_char, u_start, u_end, u_start_gid));
            }
        }

        static int CompareKey(KeyValuePair<int, TTGlyphInfo> a, KeyValuePair<int, TTGlyphInfo> b)
        {
            return a.Key.CompareTo(b.Key);
        }

        /// <summary>
        /// For creating uniform one byte per character cmaps
        /// </summary>
        PdfArray CreateByteCMaps(out PdfCmap to_cid, out PdfCmap to_unicode)
        {
#region Sort ranges

            //Map characters into a range 32 to 127 -> GID
            // Special: 
            //   - 32 must always be the space character
            //   - 40->41 and 60->62 is going to be escaped during encoding*, and should be
            //     avoided.
            //   * That is to say the (non-hex) PDF doc encoding normally used on strings,
            //     hex encoding does not need any escaping.
            //Set up a corresponding "ToUnicode" cmap
            //Advantage: Single byte encoding
            //Drawback: Limited to 90 characters
            //
            //Why 90? To avoid encoding into binary unsafe characters. Normal unicode
            //may still encode into binary unsafe data, but at least we're not doing
            //it deliberatly.

            //The cmap build table does the heavy lifting of sorting the charcodes
            //and assigning them gid values.
            //
            //Note that CID fonts don't actually use this cmap, it's not embeded, 
            //this is just code reuse.
            SetMyCmap();

            //The character ranges needs to be massaged into the 32 to 127 range.
            //We first move over ranges that already fit.
            var org_ranges = (CMapBuild.GidRange[]) _my_cmap.Ranges.Clone();
            var org_gids = new int[org_ranges.Length];
            var new_ranges = new List<MapRange>(org_ranges.Length);
            var full_range = new CMapBuild.GidRange(32, 127);
            for (int c = 0, gid = 1; c < org_ranges.Length; c++)
            {
                var range = org_ranges[c];
                if (range.Gids == null && range.Inside(full_range))
                {
                    new_ranges.Add(new MapRange(range.StartChar, range.StartChar, range.EndChar, gid));
                    org_ranges[c] = null;
                }

                //We build up a gid table, so that we get the correct gids later
                org_gids[c] = gid;
                if (range.Gids == null)
                    gid += range.Length;
            }

            //Now we move ranges that is outside into the new_ranges. However we want to avoid
            //mapping to 40-41, 60-62 and 92 so we insert dummy ranges for them.
            var dummys = new List<MapRange>(5);
            //32 needs no dummy range as InsertRange starts working from 33
            SetDummyRange(40, 40, new_ranges, dummys);
            SetDummyRange(41, 41, new_ranges, dummys);
            SetDummyRange(60, 60, new_ranges, dummys);
            SetDummyRange(62, 62, new_ranges, dummys);
            SetDummyRange(92, 92, new_ranges, dummys);
            for (int c = 0; c < org_ranges.Length; c++)
            {
                var range = org_ranges[c];
                if (range != null && range.Gids == null)
                {
                    if (InsertRange(range, new_ranges, org_gids[c]))
                        org_ranges[c] = null;
                }
            }
            //Then we remove the dummy ranges
            foreach (var dummy in dummys)
                new_ranges.Remove(dummy);
            //Then we insert ranges again, this time not avoiding the dummys,
            //this is just as I perfer to not break up ranges if I can help it.
            for (int c = 0; c < org_ranges.Length; c++)
            {
                var range = org_ranges[c];
                if (range != null && range.Gids == null)
                {
                    if (InsertRange(range, new_ranges, org_gids[c]))
                        org_ranges[c] = null;
                }
            }

            //Finally we break up any ranges that still don't fit into single
            //characters (or those ranges that comes with gid arrays), and slot them in.
            for (int c = 0; c < org_ranges.Length; c++)
            {
                var range = org_ranges[c];
                if (range != null)
                {
                    //We break the range up into single characters
                    int start = range.StartChar;
                    int end = range.EndChar;
                    if (range.Gids == null)
                    {
                        int gid = org_gids[c];
                        while (start <= end)
                            InsertRange(new CMapBuild.GidRange(start, start), new_ranges, gid++);
                    }
                    else
                    {
                        var gids = range.Gids;
                        for(int g=0; g < gids.Length; g++)
                            InsertRange(new CMapBuild.GidRange(start, start++), new_ranges, gids[g]);
                    }
                }
            }

#endregion

            //We now have a list or ranges that is to be converted into three cmaps.

#region 1. Unicode -> charcode cmap

            //This dictionary is used when encoding strings. It is assumed that this
            //array will be created through calling "GetWrapper" before one starts
            //calling "Encode". If this assumption is false we'll get an incorect 
            //translation.
            _unicode_to_charcode = new Dictionary<char, int>(_n_chars);
            foreach (var range in new_ranges)
            {
                int ch_start = range.Start, ch_end = range.End;
                int uc_start = range.UnicodeStart;
                while (ch_start <= ch_end)
                    _unicode_to_charcode.Add((char)uc_start++, ch_start++);
            }

#endregion

            return CreateSBCmap(new_ranges, new_ranges, out to_cid, out to_unicode);
        }

        PdfArray CreateSBCmap(List<MapRange> unicode_ranges, List<MapRange> char_ranges, out PdfCmap to_cid, out PdfCmap to_unicode)
        {
#region 2. Charcode -> gid cmap
            //This is the cmap that will be used to map characters to the glyphs inside 
            //the font. It's the cmap used during rendering to screen.
            var cmap = new PSCMap(false);

            //Sets single byte encoding
            cmap.AddCodespaceRange(0, 255, 1);

            //Then we add all the range information
            foreach (var range in char_ranges)
            {
                if (range.Length == 1)
                    cmap.AddNextChar(range.Start, range.gid, 1);
                else
                    cmap.AddRange(range.Start, range.End, range.gid, 1);
            }

            to_cid = PdfCmap.Create(cmap.Compile(), cmap.Name);


#endregion

#region 3. Charcode -> unicode cmap

            //We can assume that charcodes and unicodes are uniform. I.e. charcode a->c
            //will give unicode a->c.
            cmap = new PSCMap(true);

            //We only use single byte encoding
            cmap.AddCodespaceRange(0, 255, 1);

            //Then we add all the range information
            foreach (var range in unicode_ranges)
            {
                if (range.Length == 1)
                    cmap.AddNextChar(range.Start, range.UnicodeStart, 1);
                else
                    cmap.AddRange(range.Start, range.End, range.UnicodeStart, 1);
            }

            to_unicode = PdfCmap.Create(cmap.Compile(), cmap.Name);

#endregion

#region 4. Charcode -> charwidth

            return CreateWidths(unicode_ranges);

#endregion
        }

        PdfArray CreateWidths(List<MapRange> unicode_ranges)
        {
            var mtx = new chMTX();
            bool has_set_null = false;

            foreach (var range in unicode_ranges)
            {
                int unicode = range.UnicodeStart, gid = range.gid;
                for (int start = range.Start; start <= range.End; start++)
                {
                    if (_cidtogid != null)
                    {
                        if (gid == 0 && !has_set_null)
                        {
                            has_set_null = true;

                            //Default with == 1. So one can skip those,
                            //but then there's the potential problem of
                            //casees where DW has been changed
                            //if (_meassure.GetWidth(0) == 1)
                            //    continue;
                        }
                        else
                        {
                            //With the exception of glyph 0, these will be set
                            if (_cidtogid.ContainsKey(unicode))
                                continue;
                        }
                    }

                    mtx.Add(gid++, _meassure.GetWidth(_unicode_cmap.GetGlyphIndex(unicode++)));
                }
            }

            return (Precision == 0) ? mtx.CreateInt() : mtx.CreateReal(Precision);
        }

        /// <summary>
        /// Sets a range at a spesific position, returns false it it fails.
        /// </summary>
        void SetDummyRange(int start, int end, List<MapRange> new_ranges, List<MapRange> dummys)
        {
            var dummy_range = new MapRange(start, end);
            int count = new_ranges.Count;
            if (count == 0)
            {
                new_ranges.Add(dummy_range);
                dummys.Add(dummy_range);
                return;
            }
            for (int c = 0; c < count; c++)
            {
                var new_range = new_ranges[c];
                if (dummy_range.End < new_range.Start)
                    continue;
                if (dummy_range.Start > new_range.End)
                {
                    c++;
                    if (c == count)
                    {
                        new_ranges.Add(dummy_range);
                        dummys.Add(dummy_range);
                        return;
                    }
                    new_range = new_ranges[c];
                    if (dummy_range.End < new_range.Start)
                    {
                        new_ranges.Insert(c, dummy_range);
                        dummys.Add(dummy_range);
                        return;
                    }
                    else if (dummy_range.Start > new_range.End)
                    { c--; continue; }
                    return;
                }
            }
            return;
        }

        /// <summary>
        /// Finds a slot in the list of ranges where to place the range.
        /// </summary>
        bool InsertRange(CMapBuild.GidRange range, List<MapRange> new_ranges, int gid)
        {
            var range_length = range.Length;
            for (int i = 0, start = 33, end = 127; start < 128; )
            {
                int new_ranges_count = new_ranges.Count;

                if (i == new_ranges_count)
                {
                    if (new_ranges_count > 0)
                        start = new_ranges[i - 1].End + 1;
                    end = 127;
                }
                else
                {
                    var new_range = new_ranges[i];
                    if (new_range.Start > start)
                        end = new_range.Start - 1;
                    //We don't increment i as it already points at the "end" value
                    else
                    {
                        start = new_range.Start + 1;
                        end = i + 1 < new_ranges_count ? new_ranges[++i].Start - 1 : 127;
                        //When we increment i it points at the value that provided the "end"
                    }
                }
                int gap = end - start + 1;
                if (range_length <= gap)
                {
                    //There's room to slot in the range
                    var new_map = new MapRange(range.StartChar, start, start + range_length - 1, gid);
                    if (i == new_ranges_count)
                        new_ranges.Add(new_map);
                    else
                        new_ranges.Insert(i, new_map);
                    return true;
                }
                start = end + 1;
            }
            return false;
        }

        class MapRange
        {
            public readonly int UnicodeStart, UnicodeEnd;
            public readonly int Start, End, gid;
            public int NBytes = 1;
            public int Length { get { return End - Start + 1; } }
            public MapRange(int unicode_start, int start, int end, int gid_start)
            {
                UnicodeStart = unicode_start; UnicodeEnd = unicode_start + (end - start);
                Start = start; End = end; gid = gid_start;
            }
            public MapRange(int start, int end, int gid_start)
            {
                UnicodeStart = start; UnicodeEnd = end;
                Start = start; End = end; gid = gid_start;
            }
            public MapRange(int start, int end)
            {
                Start = start; End = end;
            }
            public bool Overlaps(CMapBuild.GidRange r)
            {
                return Start <= r.EndChar && Start >= r.StartChar ||
                  r.StartChar <= End && r.StartChar >= Start;
            }
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0} - {1} : {2}", Start, End, (char)UnicodeStart);
                for (int sc = UnicodeStart + 1; sc <= UnicodeEnd; sc++)
                    sb.AppendFormat(", {0}", (char)sc);
                return sb.ToString();
            }
        }

#endregion

        #region Write

        internal TableDirectory GetTD() { return _td; }
        internal MTTFont GetMTTFont() { return _meassure; }

        /// <summary>
        /// Creates a reduced true type font
        /// </summary>
        /// <param name="target">Target stream</param>
        /// <returns>Bytes are return if the target is null</returns>
        /// todo: fix os/2 + add other useful tables
        public override byte[] WriteTo(Stream target)
        {
            #region Debug code for embeding the whole font
            /*_td.Reader.Position = 0;
            var ba = new byte[_td.Reader.Length];
            _td.Reader.Read(ba, 0, ba.Length);
            target.Write(ba, 0, ba.Length);
            return ba;//*/
            #endregion

            #region Step 1. Create tables

            //A font starts with a directory over tables
            var tbuild = new TDBuild(_td.Version);

#if CMAP4_INSERT_FFFF_TEST_DATA
            //Test code for cidtogid (use with the CreateTTFont() test)
            _cidtogid = new Dictionary<int, TTGlyphInfo>();
            //Uniform
            _cidtogid.Add((int)'T', _glyphs[68]);
            _cidtogid.Add((int)'U', _glyphs[69]);
            _cidtogid.Add((int)'V', _glyphs[70]);
            //Non uniform
            _cidtogid.Add((int)'M', _glyphs[70]);
            _cidtogid.Add((int)'N', _glyphs[69]);
            _cidtogid.Add((int)'O', _glyphs[68]);
            _cidtogid.Add((int)'Q', _glyphs[69]);
            _cidtogid.Add((int)'R', _glyphs[68]);
            //Problematic
            _cidtogid.Add(ushort.MaxValue - 2, _glyphs[70]);
            _cidtogid.Add(ushort.MaxValue - 1, _glyphs[70]);
            _cidtogid.Add(ushort.MaxValue, _glyphs[70]); //*/
#endif


#if KEEP_GIDS
            //Cmap now gets written at the bottom, but that's OK for now
            tbuild.Add(new CopyTable(_td.GetTableData(Tag.cmap, true), Tag.cmap));
#else
            //The cmap maps from charcodes to gids, what gids
            //have not been decided just yet as they have to
            //be in an order suited for the cmap
            SetMyCmap();
            var cmap = _my_cmap;
            var next_gid = _next_gid;
            if (!IsCIDFont)
            {
                //Length must be -1 for the tag id to be written out. 
                cmap.Length = -1;

                //cid fonts have no need for a cmap, as they supply that themselves.
                tbuild.Add(cmap);
            }
#endif

            //Now that gids have been decided, we can go
            //ahead and fetch the glyphs. We had to wait
            //this long since composite glyphs must be 
            //updated so they have the correct gids.
            int loc_format;
#if KEEP_GIDS
            loc_format = 1;
            var glyf_build = new GlyfBuild(_glyphs, _td.Glyf, _td.Maxp.NumGlyphs);
#else
            var glyf_build = new GlyfBuild(_glyphs, _td.Glyf, next_gid, out loc_format);
#endif
            tbuild.Add(glyf_build);

            //This table holds the location of all the glyfs
            tbuild.Add(new LocaBuild(glyf_build, loc_format));

            //The maxp table is mostly ignored by parsers, but is a required table. Note
            //that _glyphs.Count is not correct until after GlyfBuild(..) has done it's thing
            var maxt = new MaxpBuild(_td.GetTableData(Tag.maxp, true), _glyphs.Count);
            tbuild.Add(maxt);

            //We add the header table from the source font
            var head_table = new HeadBuild(_td.GetTableData(Tag.head, true), loc_format);
            tbuild.Add(head_table);

            //We can also fetch the metrixs for all these glyphs
#if KEEP_GIDS
            //Cmap now gets written at the bottom, but that's OK for now
            tbuild.Add(new CopyTable(_td.GetTableData(Tag.hmtx, true), Tag.hmtx));
            tbuild.Add(new CopyTable(_td.GetTableData(Tag.hhea, true), Tag.hhea));
            if (_td.Vmtx != null)
                tbuild.Add(new CopyTable(_td.GetTableData(Tag.vmtx, true), Tag.vmtx));
#else
            var hmtx = new HmtxBuild(_glyphs, _td.Hmtx);
            tbuild.Add(hmtx);

            //The horizontal metrics header table we copy from
            //the original font.
            tbuild.Add(new HheaBuild(_td.GetTableData(Tag.hhea, true), hmtx.NumOfLongHorMetrics));

            //The vertical metrics are optional, so we have to check
            //if they actally exist before creating any tables
            var vmtx = _td.Vmtx;
            if (vmtx != null)
            {
                var vmtx_build = new VmtxBuild(_glyphs, vmtx);
                tbuild.Add(vmtx_build);
                tbuild.Add(new VheaBuild(_td.GetTableData(Tag.vhea, true), vmtx_build.NumOfLongVerMetrics));
            }
#endif

            //At this point I belive we've added everything that's required for PDF files
#if INCLUDE_STRINGS
            //The name table consists of standar strings, copy right notice and more.
            //I'm not 100% yet on how the name table is to be embeded, perhaps exlude
            //stings with unknown NameIDs. It does seem though that there's no actual
            //need to rebuild this table. Nice.
            tbuild.Add(new NameBuild(_td.GetTableData(Tag.name, true)));
#endif

            //The postscript table contains postscript names for the glyfs, this table
            //is required but "Format 3" basically says "not here". So that's what we
            //do, change the format to 3 and ignore everything else.
            var post_table = _td.GetTableData(Tag.post, false);
            if (post_table != null)
                tbuild.Add(new PostBuild(post_table));

            //Now we've added all required tables, however these tables are usefull
            //to add as they define stuff used by the hinting instructions.
            var copy_data = _td.GetTableData(Tag.prep, false);
            if (copy_data != null) tbuild.Add(new CopyTable(copy_data, Tag.prep));
            copy_data = _td.GetTableData(Tag.cvt, false);
            if (copy_data != null) tbuild.Add(new CopyTable(copy_data, Tag.cvt));
            copy_data = _td.GetTableData(Tag.fpgm, false);
            if (copy_data != null) tbuild.Add(new CopyTable(copy_data, Tag.fpgm));

            //This table hints at what point sizes one should use font smoothing
            copy_data = _td.GetTableData(Tag.gasp, false);
            if (copy_data != null) tbuild.Add(new CopyTable(copy_data, Tag.gasp));

#if INCLUDE_OS2
            //The OS/2 Table should have some changes, however it's not needed
            //for PDF embeding.
            copy_data = _td.GetTableData(Tag.OS2, false);
            if (copy_data != null) tbuild.Add(new OS2Build(copy_data));//*/
#endif

            //Todo:
            //next comes vdmx, hdmx but these can't be copied as they contain
            //a width/height array that maps to GID (i.e. we have to do these
            //table like we do other GID dependant tables).
            //
            //There's also some other tables that might be an idea to include:
            // cvar, fmtx
            //
            //Tables we can ignore include "kern", for while that is a useful
            //table its data is never needed during PDF rendering.


            #endregion

            #region Step 2. Write tables

            var s = new WriteFont();
            tbuild.WriteFont(s, head_table);

            #endregion

            #region Step 3. Write data
#if CMAP4_INSERT_FFFF_TEST_DATA
            var test = new TableDirectory(new MemoryStream(s.ToArray()));
            foreach (var g in test.Glyf)
                Console.Write("GI");
            int giiid = test.Cmap.GetCmap(Read.TrueType.PlatformID.Microsoft, 1).GetGlyphIndex(((int)'V'));
            int giddy = test.Cmap.GetCmap(Read.TrueType.PlatformID.Microsoft, 1).GetGlyphIndex(((int)'M'));
            int gilly = test.Cmap.GetCmap(Read.TrueType.PlatformID.Microsoft, 1).GetGlyphIndex(0xFFFF);
            //^ all should be 6
            //*/
            #endif

            if (target != null)
                s.WriteTo(target);
            else
                return s.ToArray();
            return null;

            #endregion
        }

        /// <summary>
        /// Creates the CMap bulid table
        /// </summary>
        private void SetMyCmap()
        {
            if (_cmap_revision != _n_chars)
            {
                _cmap_revision = _n_chars;
                _my_cmap = new CMapBuild(
                        new CMapBuild.CMapType[] {
                            IsSymbolic ? CMapBuild.CMapType.cmap_3_0
                                       : CMapBuild.CMapType.cmap_3_1
                        });
                _next_gid = _my_cmap.CreateRanges(_glyphs, _cidtogid);
            }
        }

        #endregion
    }
}