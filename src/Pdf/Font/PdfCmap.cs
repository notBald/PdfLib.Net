using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
using PdfLib.PostScript;
using PdfLib.PostScript.Font;
using PdfLib.Render.Font;
using PdfLib.Res;

namespace PdfLib.Pdf.Font
{
    public abstract class PdfCmap : PdfObject
    {
        #region Variables and properties

        internal sealed override PdfType Type { get { return PdfType.Cmap; } }

        /// <summary>
        /// Write mode. True for vertical, false for horizontal
        /// </summary>
        public abstract bool WMode { get; }

        #endregion

        #region Init

        /// <summary>
        /// Creates a cmap from a embeded resource
        /// </summary>
        internal static PdfCmap Create(string name)
        {
            switch (name)
            {
                case "Identity-H":
                    return new PdfCmapIdentityH();
                case "Identity-V":
                    return new PdfCmapIdentityV();
            }

            //Tries to load the cmap as a resource
            try
            {
                using (var cmap = StrRes.GetBinResource("Cmap." + name))
                {
                    if (cmap != null)
                    {
                        var ps = new PSInterpreter() { LanguageLevel = LangLevel.Three };
                        ps.Run(cmap);

                        var pscmap = ps.GetCMap(name);

                        return new PdfBuiltInCmap(name, pscmap);
                    }
                }
            }
            catch (System.IO.FileNotFoundException) { }

            //In some cases there may be multiple cmap files to pick from, so
            //we return null and let the caller figure out what to do about
            //missing cmaps
            throw new PdfCastException(PdfType.Cmap, ErrCode.Unknown);
        }

        /// <summary>
        /// Creates a CMap from a stream
        /// </summary>
        internal static PdfCmap Create(IWStream stream)
        {
            var ps = new PSInterpreter() { LanguageLevel = LangLevel.Three };
            ps.Run(stream.DecodedStream);

            var cmap_name = stream.Elements.GetName("CMapName");
            PSCMap pscmap;
            if (cmap_name != null)
                pscmap = ps.GetCMap(cmap_name);
            else
            {
                //ToUnicode cmaps need not have a CMapName defined
                pscmap = ps.GetCMap();
            }

            return new PdfEmbededCmap(stream, pscmap);
        }

        /// <summary>
        /// Creates a new cmap from a post script cmap
        /// </summary>
        /// <param name="stream">Byte array with the cmap</param>
        /// <param name="name">Name of the cmap</param>
        /// <returns></returns>
        public static PdfCmap Create(byte[] stream, string name)
        {
            //We compile the cmap
            var ps = new PostScript.PSInterpreter() { LanguageLevel = PostScript.LangLevel.Three };
            ps.Run(stream);
            var pscmap = ps.GetCMap(name);

            //Create the PdfCmap dictionary
            var td = new TemporaryDictionary();
            td.SetType("CMap");
            td.SetName("CMapName", name);
            if (pscmap.WMode == PostScript.Font.WMode.Vertical)
                td.SetBool("WMode", true);
            var psinfo = pscmap.CIDSystemInfo;
            td.SetItem("CIDSystemInfo", new PdfCIDSystemInfo(psinfo.Registry, psinfo.Ordering, (uint)psinfo.Supplement), false);
            td.SetInt("Length", stream.Length);

            //Creates the stream. Must use PdfStream since WritableStream isn't supported by PdfEmbededCmap
            var s = new PdfStream(td, new PdfStream.ByteSource(stream), new Read.rRange(0, stream.Length));

            return new PdfEmbededCmap(s, pscmap);
        }

        #endregion

        /// <summary>
        /// Creates a mapping object
        /// </summary>
        /// <param name="to_unicode">Optional PostScript cmap that converts characters to unicode</param>
        /// <returns>usable mapper</returns>
        internal abstract rCMap CreateMapper(PdfPSCmap to_unicode);
        internal rCMap CreateMapper() { return CreateMapper(null); }

        /// <summary>
        /// Object that can be used to map charcode to unicode and back
        /// </summary>
        /// <returns>A mapper</returns>
        /// <remarks>
        /// To map between characters, various tables and such may need to be parsed.
        /// Doing this for each induvidual character would be slow, so we fit this into
        /// an object.
        /// </remarks>
        public CharCodeMapper CreateCharCodeMap()
        {
            return new CharCodeMapper(CreateMapper());
        }

        internal sealed override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null) return false;
            if (obj.GetType() == GetType())
            {
                //quick hack
                return true;
            }
            return false;
        }

        /// <summary>
        /// This object is usefull for decoding and encoding characters to and
        /// from unicode.
        /// </summary>
        /// <remarks>I don't want to make rCMap public. This class is just a thin
        /// wrapper arround rCMap, with an aim towards beeing user friendly</remarks>
        public class CharCodeMapper
        {
            readonly rCMap _map;

            internal CharCodeMapper(rCMap map) { _map = map; }

            public byte[] ToCharCode(char unicode)
            {
                return _map.FromUnicode(unicode);
            }

            public ushort[] ToUnicode(byte[] ch)
            {
                ushort[] code_points;
                _map.Map(ch, out code_points);
                return code_points;
            }
        }
    }

    /// <summary>
    /// Wraps a PostScript Character map
    /// </summary>
    abstract class PdfPSCmap : PdfCmap
    {
        internal readonly PSCMap _pscmap;

        public PdfPSCmap(PSCMap cmap)
        {
            _pscmap = cmap;
        }

        internal override rCMap CreateMapper(PdfPSCmap to_unicode)
        {
            if (_pscmap.CodeMap.IsUnicode)
                return new rPSUnicodeCMap(_pscmap, WMode);
            return new rPSCMap(to_unicode, _pscmap, WMode);
        }

        class rPSUnicodeCMap : rCMap
        {
            internal readonly PSCMap _pscmap;

            public rPSUnicodeCMap(PSCMap cmap, bool wmode)
                //We add a dummy identity cmap, as the code path requires it. Not optimal, but oh well.
                : base(new PdfBuiltInCmap("Identity", new PostScript.Font.PSCMap()), wmode)
            { _pscmap = cmap; }

            /// <summary>
            /// The problem with unicode mapping is multibyte characters. The font implementations
            /// does not directly support it.
            /// 
            /// In case of Type1 fonts this isn't an issue. At least not as of yet. I've not looked
            /// into what changes is needed to fix this in any case.
            /// 
            /// In any case what we ultimatly want to do is transform a series of bytes into a glyph 
            /// index. Since these indexes potentially needs "multiple characters" I've made this
            /// function work so that this can be later supported. As of right now surrogate pairs
            /// are not supported.
            /// 
            /// This does mean that unicode has it's own set of functions, and that the various
            /// fonts must code special support for this. (GetGlyphs and GetWidths) which is
            /// currently only done for TrueType CID fonts (both WPF and self parsed)
            /// 
            /// Since surrogate pairs aren't supported having sepperate functions for unicode may
            /// seem a little wastefull though. 
            /// </summary>
            internal override rCMap.Glyph[] Map(byte[] str, out ushort[] Unicode)
            {
                //Note that "to_unicode" codemaps aren't the same as normal
                //codemaps despite using the same type of class.
                var unicode_map = _pscmap.CodeMap;

                var cr = unicode_map.Ranges;
                int char_start_pos = 0;
                var glyphs = new rCMap.Glyph[(str.Length < 2) ? 1 : str.Length / 2];
                Unicode = new ushort[glyphs.Length];
                int glyph_pos = 0, unicode_pos = 0;

                while (char_start_pos < str.Length)
                {
                    //The number of bytes needed for the character
                    ushort n_bytes = 0, raw_charcode = 0;

                    //Finds the character by looking at the code ranges
                    for (int c = 0; ; c++)
                    {
                        if (c == cr.Length)
                        {
                            //Should perhaps emit a .ndef char instead? I.e. Consume
                            //a single byte, add .ndef and contiune from the next byte.

                            throw new PdfNotSupportedException("CMap range corrupt");
                            //raw_charcode = 0;
                            //break;
                        }

                        var r = cr[c];
                        var r_bytes = r.NBytes;
                        Debug.Assert(r_bytes <= 2);

                        //Reads out the bytes needed for this range
                        if (r_bytes < n_bytes)
                        {
                            //In case a shorter code range comes after a long one.
                            raw_charcode = 0;
                            n_bytes = 0;
                        }
                        while (n_bytes < r_bytes)
                            raw_charcode = (ushort)(raw_charcode << 8 | (byte)str[char_start_pos + n_bytes++]);

                        if (r.Contains(raw_charcode)) break;
                    }

                    //Finds the unicode
                    var unicode = unicode_map.UnicodeMap(raw_charcode, n_bytes);
                    if (unicode.Length > 1)
                    {
                        //Alternativly you can cut up the array and insert the
                        //surrogate pair. I.e. cut the "map" array so that
                        //"position c" fits the whole surrugate.
                        Debug.Assert(false, "Ignoring surrogate pair");
                        unicode[0] = 0;
                    }
                    Unicode[unicode_pos++] = unicode[0];
                    if (unicode_pos == Unicode.Length)
                        Array.Resize<ushort>(ref Unicode, Unicode.Length * 2);

                    //Moves to the next character
                    char_start_pos += n_bytes;

                    //Stores the character
                    if (glyph_pos == glyphs.Length)
                        Array.Resize<rCMap.Glyph>(ref glyphs, glyphs.Length * 2);

                    rCMap.Glyph glyph;
                    glyph.CodePoint = raw_charcode;
                    glyph.CID = raw_charcode;
                    glyphs[glyph_pos++] = glyph;
                }

                if (unicode_pos < Unicode.Length)
                    Array.Resize<ushort>(ref Unicode, unicode_pos);

                if (glyph_pos == glyphs.Length)
                    return glyphs;

                //Shrinks the array
                Array.Resize<rCMap.Glyph>(ref glyphs, glyph_pos);
                return glyphs;
            }

            static int CodeRangeSort(CodeRange a, CodeRange b)
            {
                return b.NBytes - a.NBytes;
            }

            internal override rCMap.Glyph[] Map(byte[] str)
            {
                ushort[] unicode;
                return Map(str, out unicode);
            }

            internal override byte[] FromUnicode(char ch)
            {
                //Fetches the post script code map
                PSCodeMap cm = _pscmap.CodeMap.IsUnicode ? _pscmap.CodeMap : _to_unicode.CodeMap;
                
                //Does a reverse lookup. Do note, the _to_unicode
                //property IsUnicode is false, but this cmap "speaks"
                //unicode anyway it just isn't aware of this.
                var CID = cm.UnicodeToCID(UTF8Encoding.UTF8.GetBytes("" + ch));
                return cm.CIDtoCharCode(CID);
            }
        }

        class rPSCMap : rCMap
        {
            internal readonly PSCMap _pscmap;

            public rPSCMap(PdfPSCmap to_unicode, PSCMap cmap, bool wmode)
                : base(to_unicode, wmode)
            { _pscmap = cmap; }

            /// <summary>
            /// The problem with unicode mapping is multibyte characters. The font implementations
            /// does not directly support it.
            /// 
            /// In case of Type1 fonts this isn't an issue. At least not as of yet. I've not looked
            /// into what changes is needed to fix this in any case.
            /// 
            /// In any case what we ultimatly want to do is transform a series of bytes into a glyph 
            /// index. Since these indexes potentially needs "multiple characters" I've made this
            /// function work so that this can be later supported. As of right now surrogate pairs
            /// are not supported.
            /// 
            /// This does mean that unicode has it's own set of functions, and that the various
            /// fonts must code special support for this. (GetGlyphs and GetWidths) which is
            /// currently only done for TrueType CID fonts (both WPF and self parsed)
            /// 
            /// Since surrogate pairs aren't supported having sepperate functions for unicode may
            /// seem a little wastefull though. 
            /// </summary>
            internal override rCMap.Glyph[] Map(byte[] str, out ushort[] Unicode)
            {
                //Note that "to_unicode" codemaps aren't the same as normal
                //codemaps despite using the same type of class.
                var unicode_map = _to_unicode.CodeMap;

                PSCodeMap map = _pscmap.CodeMap;
                var cr = map.Ranges;
                int char_start_pos = 0;
                var glyphs = new rCMap.Glyph[(str.Length < 2) ? 1 : str.Length / 2];
                Unicode = new ushort[glyphs.Length];
                int glyph_pos = 0, unicode_pos = 0;

                while (char_start_pos < str.Length)
                {
                    //The number of bytes needed for the character
                    ushort n_bytes = 0, raw_charcode = 0;

                    //Finds the character by looking at the code ranges
                    for (int c = 0; ; c++)
                    {
                        if (c == cr.Length)
                        {
                            //Should perhaps emit a .ndef char instead? I.e. Consume
                            //a single byte, add .ndef and contiune from the next byte.

                            throw new PdfNotSupportedException("CMap range corrupt");
                            //raw_charcode = 0;
                            //break;
                        }

                        var r = cr[c];
                        var r_bytes = r.NBytes;
                        Debug.Assert(r_bytes <= 2);

                        //Reads out the bytes needed for this range
                        if (r_bytes < n_bytes)
                        {
                            //In case a shorter code range comes after a long one.
                            raw_charcode = 0;
                            n_bytes = 0;
                        }
                        while (n_bytes < r_bytes)
                            raw_charcode = (ushort)(raw_charcode << 8 | (byte)str[char_start_pos + n_bytes++]);

                        if (r.Contains(raw_charcode)) break;
                    }

                    //Finds the character's CID
                    ushort cid = (ushort)map.Map(raw_charcode, n_bytes);

                    //Finds the unicode
                    var unicode = unicode_map.UnicodeMap(cid, n_bytes);
                    if (unicode.Length > 1)
                    {
                        //Alternativly you can cut up the array and insert the
                        //surrogate pair. I.e. cut the "map" array so that
                        //"position c" fits the whole surrugate.
                        Debug.Assert(false, "Ignoring surrogate pair");
                        unicode[0] = 0;
                    }
                    Unicode[unicode_pos++] = unicode[0];
                    if (unicode_pos == Unicode.Length)
                        Array.Resize<ushort>(ref Unicode, Unicode.Length * 2);

                    //Moves to the next character
                    char_start_pos += n_bytes;

                    //Stores the character
                    if (glyph_pos == glyphs.Length)
                        Array.Resize<rCMap.Glyph>(ref glyphs, glyphs.Length * 2);

                    rCMap.Glyph glyph;
                    glyph.CodePoint = raw_charcode;
                    glyph.CID = cid;
                    glyphs[glyph_pos++] = glyph;
                }

                if (unicode_pos < Unicode.Length)
                    Array.Resize<ushort>(ref Unicode, unicode_pos);

                if (glyph_pos == glyphs.Length)
                    return glyphs;

                //Shrinks the array
                Array.Resize<rCMap.Glyph>(ref glyphs, glyph_pos);
                return glyphs;                
            }

            internal override rCMap.Glyph[] Map(byte[] str)
            {
                PSCodeMap map = _pscmap.CodeMap;
                CodeRange[] cr = map.Ranges;

                ////I'm unsure if this should be done or not. If it should be done,
                ////then it should be fixed in map.Ranges instead of here.
                ////"See Null terminator character.pdf" for an example where this is
                ////needed, but that file opens wrongly in Sumatra, so there's that.
                //Array.Sort<CodeRange>(cr, (a, b) => { return b.NBytes - a.NBytes; });

                int char_start_pos = 0;
                var glyphs = new rCMap.Glyph[(str.Length < 2) ? 1 : str.Length / 2];
                var glyph_pos = 0;

                while (char_start_pos < str.Length)
                {
                    //The number of bytes needed for the character
                    ushort n_bytes = 0, raw_charcode = 0;

                    //Finds the character by looking at the code ranges
                    for (int c = 0; ; c++)
                    {
                        if (c == cr.Length)
                        {
                            //Emits an .ndef char instead. I.e. Consumes
                            //a single byte.
                            //Todo: log "CMap range corrupt"
                            //throw new PdfNotSupportedException("CMap range corrupt");
                            n_bytes = 1;
                            break;
                        }

                        var r = cr[c];
                        var r_bytes = r.NBytes;
                        Debug.Assert(r_bytes <= 2);

                        if (n_bytes != 1)
                        {
                            raw_charcode = 0;
                            n_bytes = 1;

                            //Checks the fist byte
                            raw_charcode = (byte)str[char_start_pos];
                        }
                        int r_end = 8 * (r_bytes - 1);
                        int r_start = r.Start >> r_end;
                        r_end = r.End >> r_end;
                        if (r_start <= raw_charcode && raw_charcode <= r_end)
                        {
                            //Reads out the bytes needed for this range
                            while (n_bytes < r_bytes && char_start_pos + n_bytes < str.Length)
                                raw_charcode = (ushort)(raw_charcode << 8 | (byte)str[char_start_pos + n_bytes++]);

                            if (r.Contains(raw_charcode)) break;
                        }
                    }

                    //Finds the character's CID
                    ushort cid = (ushort) map.Map(raw_charcode, n_bytes);

                    //Moves to the next character
                    char_start_pos += n_bytes;

                    //Stores the character
                    if (glyph_pos == glyphs.Length)
                        Array.Resize<rCMap.Glyph>(ref glyphs, glyphs.Length * 2);

                    rCMap.Glyph glyph;
                    glyph.CodePoint = raw_charcode;
                    glyph.CID = cid;
                    glyphs[glyph_pos++] = glyph;
                }

                if (glyph_pos == glyphs.Length)
                    return glyphs;

                //Shrinks the array
                Array.Resize<rCMap.Glyph>(ref glyphs, glyph_pos);
                return glyphs;
            }

            internal override byte[] FromUnicode(char ch)
            {
                throw new NotImplementedException();
            }
        }
    }

    /// <summary>
    /// A PostScript Character map embeded in the PDF.
    /// </summary>
    /// <remarks>
    /// Lots of boilerplate code as I can't inherit from "Elements"
    /// </remarks>
    class PdfEmbededCmap : PdfPSCmap, IEnumRef, IKRef
    {
        #region Init and variables

        private readonly IWStream _elems;

        /// <summary>
        /// Whenever this CMap is for horizontal or vertical fonts
        /// </summary>
        public override bool WMode { get { return _elems.Elements.GetBool("WMode", false); } }

        /// <summary>
        /// Name of this cmap. (Optional for ToUnicode cmaps)
        /// </summary>
        public string CMapName { get { return _elems.Elements.GetNameEx("CMapName"); } }

        /// <summary>
        /// Stream based objects must be indirect
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        internal PdfEmbededCmap(IWStream stream, PSCMap cmap)
            : base(cmap)
        {
            _elems = stream;
            Debug.Assert((cmap.WMode == PostScript.Font.WMode.Vertical) == WMode);
        }

        #endregion

        #region Boilerplate code

        #region IRef

        public bool HasReference { get { return _elems.HasReference; } }

        WritableReference IRef.Reference
        {
            [DebuggerStepThrough]
            get { return ((IRef)_elems).Reference; }
            set { ((IRef)_elems).Reference = value; }
        }

        #endregion

        #region ICRef

        object ICRef.GetChildren() { return ((ICRef)_elems).GetChildren(); }

        ICRef ICRef.MakeCopy(object data, ResTracker t)
        { return (ICRef)new PdfEmbededCmap(((PdfStream)((ICRef)_elems).MakeCopy(data, t)), _pscmap); }

        void ICRef.LoadResources(HashSet<object> check)
        {
            ((ICRef)_elems).LoadResources(check);
        }

        bool ICRef.Follow { get { return true; } }

        #endregion

        #region IKRef

        /// <summary>
        /// Default save mode
        /// </summary>
        SM IKRef.DefSaveMode { get { return DefSaveMode; } }

        /// <summary>
        /// If this object has a dummy tracker.
        /// </summary>
        bool IKRef.IsDummy { get { return ((IKRef)_elems).IsDummy; } }

        /// <summary>
        /// Adopt this object. Will return false if the adoption failed.
        /// </summary>
        /// <param name="tracker"></param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker)
        {
            return ((IKRef)_elems).Adopt(tracker);
        }

        /// <summary>
        /// Adobt this object. Will return false if the adoption failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adobt this item</param>
        /// <param name="state">State information about the adoption</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker, object state)
        {
            return ((IKRef)_elems).Adopt(tracker, state);
        }/// 

        /// <summary>
        /// Checks if a tracker owns this object
        /// </summary>
        /// <param name="tracker">Tracker</param>
        /// <returns>True if the tracker is the owner</returns>
        bool IKRef.IsOwner(ResTracker tracker)
        {
            return ((IKRef) _elems).IsOwner(tracker);
        }

        #endregion

        #region IEnumRef

        /// <summary>
        /// If there are child references in need of counting.
        /// </summary>
        bool IEnumRef.HasChildren
        { get { return ((IEnumRef)_elems).HasChildren; } }

        /// <summary>
        /// The child's referecnes
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        { get { return ((IEnumRef)_elems).RefEnumerable; } }

        #endregion

        public override PdfItem Clone()
        {
            return new PdfEmbededCmap((IWStream)_elems.MakeCopy((PdfDictionary) _elems.Elements.Clone()), _pscmap);
        }

        internal override void Write(PdfWriter write)
        {
            _elems.Write(write);
        }

        internal override void Write(PdfWriter write, SM store_mode)
        {
            if (store_mode != SM.Indirect)
                throw new PdfInternalException("Streams must be saved as indirect");
            _elems.Write(write);
        }

        public override string ToString()
        {
            return _pscmap.CMapName;
        }

        #endregion
    }

    /// <summary>
    /// Represents a built in cmap
    /// </summary>
    /// <remarks>The purpose of this class is to facilitate saving. It makes
    /// sure it is saved as just a name</remarks>
    class PdfBuiltInCmap : PdfPSCmap
    {
        private readonly string _name;

        public override bool WMode
        {
            get { return _pscmap.WMode == PostScript.Font.WMode.Vertical; }
        }

        internal PdfBuiltInCmap(string name, PSCMap cmap)
            : base(cmap)
        {
            _name = name;
        }

        #region Boilerplate overrides
        //I've not bothered with "copy", Memberwise clone should work fine.

        internal override void Write(PdfWriter write) { write.WriteName(_name); }

        public override string ToString() { return _name; }

        #endregion
    }

    /// <summary>
    /// The horizontal identity mapping for 2-byte CIDs; may be used with CIDFonts 
    /// using any Registry, Ordering, and Supplement values. It maps 2-byte character 
    /// codes ranging from 0 to 65,535 to the same 2-byte CID value, interpreted high-
    /// order byte first. 
    /// </summary>
    public class PdfCmapIdentityH : PdfCmap
    {
        /// <summary>
        /// Writes horizontaly
        /// </summary>
        public override bool WMode { get { return false; } }

        internal override rCMap CreateMapper(PdfPSCmap to_unicode)
        {
            return new IdentityH(to_unicode);
        }

        /// <summary>
        /// Saves this CMap to a file
        /// </summary>>
        internal override void Write(PdfWriter write) { write.WriteName("Identity-H"); }

        public override string ToString() { return "Identity-H"; }

        internal static rCMap.Glyph[] MapImpl(byte[] str)
        {
            //Not sure what to do with stings of uneven length.
            var glyphs = new rCMap.Glyph[(str.Length + 1) / 2];

            for (int c = 0, gc = 0; c < str.Length; )
            {
                rCMap.Glyph ch;
                ch.CodePoint = str[c++];
                if (c < str.Length)
                    ch.CodePoint = (ushort)(ch.CodePoint << 8 | str[c++]);
                ch.CID = ch.CodePoint;
                glyphs[gc++] = ch;
            }

            return glyphs;
        }

        internal static rCMap.Glyph[] MapImpl(byte[] str, out ushort[] unicode, PSCodeMap unicode_map)
        {
            //Not sure what to do with stings of uneven length.
            var glyphs = new rCMap.Glyph[(str.Length + 1) / 2];
            unicode = new ushort[glyphs.Length];

            for (int c = 0, gc = 0; c < str.Length; gc++)
            {
                rCMap.Glyph ch;
                ch.CodePoint = str[c++];
                if (c < str.Length)
                    ch.CodePoint = (ushort)(ch.CodePoint << 8 | str[c++]);
                ch.CID = ch.CodePoint;
                glyphs[gc] = ch;
                var unicode_char = unicode_map.UnicodeMap(ch.CodePoint, 2);
                if (unicode_char.Length > 1)
                {
                    //Alternativly you can cut up the array and insert the
                    //surrogate pair. I.e. cut the "map" array so that
                    //position c fits the whole surrugate.
                    Debug.Assert(false, "Ignoring surrogate pair");
                    unicode_char[0] = 0;
                }
                unicode[gc] = unicode_char[0];
            }

            return glyphs;
        }

        class IdentityH : rCMap
        {
            public IdentityH(PdfPSCmap to_unicode)
                : base(to_unicode, false) { }

            internal override rCMap.Glyph[] Map(byte[] bytes)
            {
                return PdfCmapIdentityH.MapImpl(bytes);
            }

            internal override rCMap.Glyph[] Map(byte[] bytes, out ushort[] Unicode)
            {
                return PdfCmapIdentityH.MapImpl(bytes, out Unicode, _to_unicode.CodeMap);
            }

            internal override byte[] FromUnicode(char ch)
            {
                throw new NotImplementedException();
            }

            public override string ToString() { return "Identity-H"; }
        }
    }

    public class PdfCmapIdentityV : PdfCmap
    {
        /// <summary>
        /// This CMap writes vertically
        /// </summary>
        public override bool WMode { get { return true; } }

        internal override rCMap CreateMapper(PdfPSCmap to_unicode)
        {
            return new IdentityV(to_unicode);
        }

        internal override void Write(PdfWriter write) { write.WriteName("Identity-V"); }

        public override string ToString() { return "Identity-V"; }

        class IdentityV : rCMap
        {
            public IdentityV(PdfPSCmap to_unicode)
                : base(to_unicode, true) { }

            internal override rCMap.Glyph[] Map(byte[] bytes)
            {
                return PdfCmapIdentityH.MapImpl(bytes);
            }

            internal override rCMap.Glyph[] Map(byte[] bytes, out ushort[] Unicode)
            {
                return PdfCmapIdentityH.MapImpl(bytes, out Unicode, _to_unicode.CodeMap);
            }

            internal override byte[] FromUnicode(char ch)
            {
                throw new NotImplementedException();
            }

            public override string ToString() { return "Identity-V"; }
        }
    }

    /// <summary>
    /// Mappers contains state information (_to_unicode_map) which means one
    /// can't use the classes directly.
    /// </summary>
    internal abstract class rCMap
    {
        readonly protected PSCMap _to_unicode;
        public readonly bool WMode;

        /// <summary>
        /// Whenever to use unicode translation or not.
        /// </summary>
        public bool Unicode { get { return _to_unicode != null; } }

        public rCMap(PdfPSCmap cmap, bool wmode)
        { _to_unicode = cmap == null ? null : cmap._pscmap; WMode = wmode; }

        /// <summary>
        /// Converts a string of bytes into glyph information
        /// </summary>
        internal abstract Glyph[] Map(byte[] bytes);

        /// <summary>
        /// Unicode has it's own codepath.
        /// </summary>
        /// <param name="bytes">Bytes to transform to unicode</param>
        /// <param name="Unicode">The finished unicode string</param>
        /// <returns></returns>
        internal abstract Glyph[] Map(byte[] bytes, out ushort[] Unicode);

        /// <summary>
        /// Translate from unicode to bytecode.
        /// </summary>
        /// <param name="ch">Unicode character</param>
        /// <returns>Char code bytes</returns>
        internal abstract byte[] FromUnicode(char ch);

        /// <summary>
        /// contains information for rendering a glyph
        /// </summary>
        [DebuggerDisplay("{CodePoint} - {CID}")]
        internal struct Glyph
        {
            /// <summary>
            /// The raw value of the glyph
            /// </summary>
            public ushort CodePoint;

            /// <summary>
            /// The character id. Table C.1 in the specs give
            /// these an implementation limit of 65,535
            /// </summary>
            public ushort CID;
        }
    }
}
