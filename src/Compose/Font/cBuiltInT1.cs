using PdfLib.Pdf.Font;
using PdfLib.Pdf.Primitives;
using PdfLib.Render.Font;
using System;
using System.Collections.Generic;
using System.IO;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// For reusing built in Type1 and TrueType fonts found in PDF documents
    /// </summary>
    /// <remarks>
    /// Technically this class does nothing. It is possible to draw using the font directly,
    /// eg "DrawPage.SetFont(PdfFont)", but this also lets one use these fonts
    /// in compose classes i.e. "cDraw.SetFont(PdfFont) wraps the font into this class and calls
    /// SetFont(cFont)".
    /// </remarks>
    internal sealed class cBuiltInT1 : cFont
    {
        #region Variables

        private readonly MBuiltInFont _measure;
        private readonly Dictionary<char, string> _unicode_cmap;
        private readonly Dictionary<char, byte> _unicode_to_cc;
        private readonly PdfType1Font _font;
        private readonly PdfFontDescriptor _fd;
        private rFont _render;

        #endregion

        #region Properties

        /// <remark>
        /// Baseline-to-baseline line height.
        ///
        /// Built-in Type 1 fonts use a fixed 1000-unit em square. We therefore
        /// use 1000d as a constant in the properties below.
        /// </remark>

        public override double Ascent { get { return _fd.Ascent / 1000d; } }
        public override double CapHeight { get { return _fd.CapHeight / 1000d; } }
        public override double XHeight { get { return _fd.XHeight / 1000d; } }
        public override double Descent { get { return _fd.Descent / 1000d; } }
        public override double UnderlinePosition { get { return 0; } }
        public override double UnderlineThickness { get { return 1 / 1000d; } }
        public override double LineHeight { get { return (_fd.Ascent + _fd.Descent) / 1000d; } }
        public override string Name 
        { 
            get 
            {
                var name = _font.BaseFont;
                int i = name.IndexOf('+');
                if (i != -1)
                    name = name.Substring(i + 1);
                return name; 
            } 
        }
        public override double ItalicAngle { get { return _fd.ItalicAngle; } }
        public override bool IsFixedPitch { get { return _fd.IsMonospaced; } }
        public override PdfRectangle FontBBox { get { return _fd.FontBBox; } }
        public override bool IsSymbolic => _fd.IsSymbolic;

        public override int FirstChar
        {
            get { return _font.FirstChar; }
        }

        public override int LastChar
        {
            get { return _font.LastChar; }
        }

        /// <summary>
        /// This font is closed, any futher modification will result in
        /// a new font being created.
        /// </summary>
        public override bool IsClosed { get { return false; } }

        public override double[] Widths
        {
            get { return _font.Widths; }
        }

        #endregion

        #region Init

        public cBuiltInT1(PdfType1Font font)
        {
            _font = font;
            _unicode_cmap = Enc.UnicodeToNames;
            _unicode_to_cc = new Dictionary<char,byte>(256);
            var enc = _font.Encoding.Differences;
            var name_to_unicode = Util.ArrayHelper.Reverse<char, string>(_unicode_cmap);
            for (int c = 0; c < enc.Length; c++)
            {
                char unicode;
                if (name_to_unicode.TryGetValue(enc[c], out unicode) && !_unicode_to_cc.ContainsKey(unicode))
                    _unicode_to_cc.Add(unicode, (byte)c);
            }

            _fd = font.FontDescriptor;
            var afm = AdobeFontMetrixs.Create(_font.BaseFont, _font.Encoding, false);
            if (afm == null)
            {
                var name = font.BaseFont;
                int i = name.IndexOf('+');
                if (i != -1)
                    name = name.Substring(i + 1);
                var cf = cFont.Create(name);
                //^ Throws if the font is not found

                //Now create AFM for this font.
                afm = new AdobeFontMetrixs(AdobeFontMetrixs.Create(cf, font.Encoding));
            }
            else
            {
                //The font's measurments must override the AFM
                afm.MergeWith(font);
                if (_fd == null)
                {
                    //Creates a font descriptor from the afm info
                    _fd = new PdfFontDescriptor(font.BaseFont, afm.Ascender, afm.Descender, afm.CapHeight);
                    _fd.XHeight = afm.XHeight;
                    if (afm.IsFixedPitch)
                        _fd.Flags |= FontFlags.FixedPitch;
                    if (afm.Italic)
                        _fd.Flags |= FontFlags.Italic;
                    if (afm.Symbolic)
                        _fd.Flags |= FontFlags.Symbolic;
                    if (afm.Bold)
                        _fd.FontWeight = 400;
                    _fd.ItalicAngle = afm.ItalicAngle;
                }
            }

            _measure = new MBuiltInFont(afm);
        }

        internal override void InitRender(IFontFactory factory)
        {
            _render = factory.CreateBuiltInFont(_font, Widths, _measure.AFM, false);
        }

        #endregion

        #region Render

        internal override rGlyph GetGlyph(int char_code, bool is_space, int n_bytes)
        {
            return _render.GetCachedGlyph(char_code, is_space);
        }

        internal override int GetNBytes(byte b)
        {
            return 1;
        }

        internal override void DismissRender()
        {
            _render.Dismiss();
        }

        internal override void DisposeRender()
        {
            if (_render != null)
            {
                _render.Dispose();
                _render = null;
            }
        }

        #endregion

        public override byte[] Encode(string str)
        {
            byte[] bytes = new byte[str.Length];
            for(int c=0; c < bytes.Length; c++)
                _unicode_to_cc.TryGetValue(str[c], out bytes[c]);
            return bytes;
        }

        public override chBox GetGlyphBox(char c)
        {
            return _measure.GetGlyphBox(_unicode_cmap[c]);
        }

        public override chGlyph GetGlyphMetrix(char c)
        {
            return _measure.GetGlyphInfo(_unicode_cmap[c]);
        }

        public override double GetKerning(int from_gid, int to_gid)
        {
            return 0;
        }

        public override PdfFont MakeWrapper()
        {
            return _font;
        }

        internal override void CompileFont(PdfDictionary dict)
        {
            //Font already compiled
        }

        public override byte[] WriteTo(Stream target)
        {
            throw new NotSupportedException();
        }
    }
}
