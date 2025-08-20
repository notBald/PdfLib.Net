using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Render.Font;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// For making built in fonts
    /// </summary>
    /// <remarks>
    /// Todo: Support built in symbol fonts
    /// 
    /// Note the .notdef implementation is a hack. BuiltInFonts don't have a .notdef, so
    /// repurposing charcode 31 for that (can't use 0 or 32 as they are both padded with Tw)
    /// </remarks>
    internal sealed class cBuiltInFont : cFont
    {
        #region Variables

        private readonly MBuiltInFont _meassure;
        private Dictionary<char, string> _unicode_cmap;
        private readonly string _name;
        private double _missing_width = 0;
        cWrapFont _wrapper;

        /// <summary>
        /// All glyphs that are used gets registered in this
        /// dictionary. That's how we know what glyphs to
        /// save to the final file.
        /// </summary>
        readonly Dictionary<char, chGlyph> _glyphs = new Dictionary<char, chGlyph>();

        #endregion

        #region Properties

        public override double Ascent { get { return _meassure.Ascent / 1000d; } }
        public override double CapHeight { get { return _meassure.CapHeight / 1000d; } }
        public override double XHeight { get { return _meassure.XHeight / 1000d; } }
        public override double Descent { get { return _meassure.Descent / 1000d; } }
        public override double UnderlinePosition { get { return _meassure.UnderlinePosition / 1000d; } }
        public override double UnderlineThickness { get { return _meassure.UnderlineThickness / 1000d; } }
        public override double LineHeight { get { return _meassure.LineHeight / 1000d; } }
        public override string Name { get { return _name; } }
        public override double ItalicAngle { get { return _meassure.ItalicAngle; } }
        public override bool IsFixedPitch { get { return _meassure.IsFixedPitch; } }
        public override PdfRectangle FontBBox { get { return new PdfRectangle(_meassure.FontBBox); } }
        public override bool IsSymbolic => _meassure.IsSymbolic;

        public override int FirstChar
        {
            get 
            {
                if (_glyphs.Count == 0) return 0;
                int low = int.MaxValue;
                var afm = _meassure.AFM;
                if (IsSymbolic)
                {
                    foreach (var kv in _glyphs)
                    {
                        //GID == CharCode
                        low = Math.Min(low, kv.Value.GID);
                    }
                }
                else
                {
                    var cmap = Enc.UnicodeToANSI;
                    var chars = Enc.GlyphNames;
                    foreach (var kv in _glyphs)
                    {
                        //We have to translate the afm glyph id to WinANSI
                        var afm_gid = kv.Value.GID;

                        //First we translate it to a name, using the afm's
                        //glyph name array
                        var unicode_name = afm.Encoding[afm_gid];

                        //Looks up the unicode character value for said namn
                        char unicode;
                        if (!chars.TryGetValue(unicode_name, out unicode))
                            unicode = '\0';

                        //Translates the unicode character value to WinANSI
                        int val;
                        if (!cmap.TryGetValue(unicode, out val))
                            val = 0;

                        //Alternativly:
                        //val = Util.ArrayHelper.IndexOf(Enc.WinANSI, 0, unicode_name);

                        if (val < low)
                            low = val;
                    }
                }
                return low;
            }
        }

        public override int LastChar
        {
            get
            {
                if (_glyphs.Count == 0) return 0;
                int hi = int.MinValue;
                var afm = _meassure.AFM;
                if (IsSymbolic)
                {
                    foreach (var kv in _glyphs)
                    {
                        //GID == CharCode
                        hi = Math.Max(hi, kv.Value.GID);
                    }
                }
                else
                {
                    var cmap = Enc.UnicodeToANSI;
                    var chars = Enc.GlyphNames;
                    foreach (var kv in _glyphs)
                    {
                        var afm_gid = kv.Value.GID;
                        var unicode_name = afm.Encoding[afm_gid];
                        char unicode;
                        if (!chars.TryGetValue(unicode_name, out unicode))
                            unicode = '\0';

                        int val;
                        if (!cmap.TryGetValue(unicode, out val))
                            val = 0;

                        //Alternativly:
                        //val = Util.ArrayHelper.IndexOf(Enc.WinANSI, 0, unicode_name);

                        if (val > hi)
                            hi = val;
                    }
                }
                return hi;
            }
        }

        /// <summary>
        /// This font is closed, any futher modification will result in
        /// a new font being created.
        /// </summary>
        public override bool IsClosed { get { return _wrapper != null; } }

        public override double[] Widths
        {
            get 
            {
                var r = _meassure.GetWidths(FirstChar, LastChar, IsSymbolic ? null : Enc.WinANSI);
                if (_missing_width != 0 && FirstChar == 31)
                    r[0] = _missing_width;
                return r;
            }
        }

        #endregion

        #region Init

        public cBuiltInFont(string name)
            : this(AdobeFontMetrixs.Create(name, new PdfEncoding("WinAnsiEncoding"), true))
        { }

        public cBuiltInFont(AdobeFontMetrixs afm)
        {
            _meassure = new MBuiltInFont(afm);
            if (afm.Symbolic)
            {
                _unicode_cmap = new Dictionary<char, string>(256);
                foreach (var glyph in afm.GlyfInfo)
                    _unicode_cmap.Add((char)glyph.Value.charcode, glyph.Key);
            }
            else
                _unicode_cmap = Enc.UnicodeToNames;
            _name = afm.FontName;
        }

        #endregion

        #region Render

        internal override rGlyph GetGlyph(int char_code, bool is_space, int n_bytes)
        {
            return _wrapper.RenderFont.GetCachedGlyph(char_code, is_space);
        }

        internal override int GetNBytes(byte b)
        {
            return 1;
        }
        internal override void InitRenderNoCache(IFontFactory factory)
        {
            SetRenderFont(factory);
            _wrapper.RenderFont.SetNullGlyphCache();
            _wrapper.RenderFont.Init();
        }

        internal override void InitRender(IFontFactory factory)
        {
            SetRenderFont(factory);
            _wrapper.RenderFont.Init();
        }

        private void SetRenderFont(IFontFactory factory)
        {
            var rf = _wrapper.RenderFont;
            if (rf == null || rf.FontFactory != factory && rf.FontFactory != null)
            {
                if (rf != null)
                    rf.Dispose();

                var widths = _meassure.GetWidths(0, 255, IsSymbolic ? null : Enc.WinANSI);
                _wrapper.RenderFont = factory.CreateBuiltInFont(new PdfType1Font(_wrapper.Elements), widths, _meassure.AFM, _meassure.AFM.GhostName == "");
            }
        }

        internal override void DissmissRender()
        {
            _wrapper.RenderFont.Dismiss();
        }

        internal override void DisposeRender()
        {
            if (_wrapper.RenderFont != null)
            {
                _wrapper.RenderFont.Dispose();
                _wrapper.RenderFont = null;
            }
        }

        #endregion

        public override byte[] Encode(string str)
        {
            if (IsSymbolic)
                return Read.Lexer.GetBytes(str);

            var cmap = Enc.UnicodeToANSI;
            var bytes = new byte[str.Length];
            for (int c = 0; c < str.Length; c++)
            {
                int val;
                if (cmap.TryGetValue(str[c], out val))
                    bytes[c] = (byte)val;
                else
                    //Adobe treats GID 0 as if it was a space character, AFAICT, so using 31 instead.
                    bytes[c] = 31;
            }
            return bytes;
        }

        public override chBox GetGlyphBox(char c)
        {
            return _meassure.GetGlyphBox(_unicode_cmap[c]);
        }

        public override chGlyph GetGlyphMetrix(char c)
        {
            chGlyph ch;
            if (_glyphs.TryGetValue(c, out ch))
                return ch;

            //Input is unicode -> translate to AFM name.
            string name;
            if (!_unicode_cmap.TryGetValue(c, out name))
                name = ".notdef";
            ch = _meassure.GetGlyphInfo(name);
            if (ch.GID == 0)
            {
                //There's no ndef glyph defined in AFM, using space then. Note
                //the use of GID 31. Adobe treats GID 0 as if it was a space character,
                //AFAICT, so using 31 instead.
                ch = new chGlyph(31, _meassure.GetGlyphInfo(_unicode_cmap[' ']));
                _missing_width = ch.Width * 1000d;
            }
            _glyphs.Add(c, ch);
            return ch;
        }

        public override double GetKerning(int from_gid, int to_gid)
        {
            return 0;
        }

        public override PdfFont MakeWrapper()
        {
            if (_wrapper != null)
                return _wrapper;

            var dict = new TemporaryDictionary();
            SetUpDict(dict);

            _wrapper = new cWrapFont(this, dict);
            return _wrapper;
        }

        private void SetUpDict(PdfDictionary dict)
        {
            dict.SetType("Font");
            dict.SetName("BaseFont", _name);
            dict.SetName("Subtype", "Type1");
            if (!IsSymbolic)
                dict.SetItem("Encoding", new PdfEncoding("WinAnsiEncoding"), true);

            GenerateFontDescriptor(dict, this);
        }

        internal override void CompileFont(PdfDictionary dict)
        {
            if (!dict.Contains("Type"))
                SetUpDict(dict);

            var da = Widths;
            var widths = new PdfItem[da.Length];
            if (Precision == 0)
            {
                for (int c = 0; c < da.Length; c++)
                    widths[c] = new PdfInt((int)da[c]);
            }
            else
            {
                var mul = Math.Pow(10, Precision);
                for (int c = 0; c < da.Length; c++)
                    widths[c] = new PdfReal(Math.Truncate(da[c] * mul) / mul);
            }

            //Note, these enteries will note be used for anything, so they
            //need not be removed after the write.
            dict.SetItem("Widths", new RealArray(widths), false);
            dict.SetInt("FirstChar", FirstChar);
            dict.SetInt("LastChar", LastChar);

            //Currently (ab)using _missing_width for .ndef
            if (_missing_width != 0)
                dict.SetReal("MissingWidth", _missing_width);
        }

        static void GenerateFontDescriptor(PdfDictionary elems, cBuiltInFont font)
        {
            #region Sets the name

            var wd = new TemporaryDictionary();
            wd.SetType("FontDescriptor");
            wd.SetName("FontName", elems.GetNameEx("BaseFont"));

            #endregion

            #region Sets the flags

            FontFlags flags = font._meassure.IsSymbolic ? FontFlags.Symbolic : FontFlags.Nonsymbolic;
            if (font._meassure.IsFixedPitch)
                flags |= FontFlags.FixedPitch;

            //todo: Italic and other such info should be set in the flags

            wd.SetInt("Flags", (int)flags);

            #endregion

            #region Sets the font box

            wd.SetNewItem("FontBBox", new PdfRectangle(font._meassure.FontBBox), false);

            #endregion

            #region Sets the italic angle

            wd.SetReal("ItalicAngle", font._meassure.ItalicAngle);

            #endregion

            #region Sets Asccent to StemV

            wd.SetReal("Ascent", font._meassure.Ascent);
            wd.SetReal("Descent", font._meassure.Descent);
            wd.SetReal("CapHeight", font._meassure.CapHeight);

            //Unknown for AFM fonts
            wd.SetInt("StemV", 0);

            #endregion

            elems.SetItem("FontDescriptor", new PdfFontDescriptor(wd), true);
            //var test = ((PdfFontDescriptor)_elems["FontDescriptor"].Deref()).FontFile2.Length;
        }

        public override byte[] WriteTo(Stream target)
        {
            throw new NotImplementedException();
        }
    }
}
