using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;
using PdfLib.Pdf.Font;
using PdfLib.Read.TrueType;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Render.Font
{
    /// <summary>
    /// CID Type1 font
    /// </summary>
    internal sealed class CIDT1Font<Path> : Type1Font<Path>
    {
        #region Variables and properties

        /// <summary>
        /// Horizontal font metrix
        /// </summary>
        private readonly PdfHMTX _w;

        /// <summary>
        /// Vertical font metrix
        /// </summary>
        private readonly PdfVMTX _v;

        /// <summary>
        /// Whenever this is a vertical font or not
        /// </summary>
        private readonly bool _vertical;

        /// <summary>
        /// If this font is verticaly oriented
        /// </summary>
        public override bool Vertical { get { return _vertical; } }

        #endregion

        #region Init

        internal CIDT1Font(CIDFontType0 font, IFontFactory<Path> factory, bool vertical)
            : base(font, factory)
        {
            _vertical = vertical;
            _w = font.W;

            if (vertical)
                _v = font.W2;
        }

        #endregion

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            Debug.Assert(false, "Untested code");

            //Gets the character's name string
            var name = (_names == null) ? _fd.Encoding[ch] : _names[ch];

            //Gets the glyph program
            var glyph = _fd.CharStrings[name];

            //Runs the glyph program
            var gp = new GlyphParser(_fd, _factory.GlyphRenderer());
            var sg = gp.Execute(glyph);

            var fm = _fd.FontMatrix;
            if (Vertical)
            {
                var v = _v[ch];
                
                return _factory.CreateGlyph(new Path[] { sg }, new xPoint(0, v.W1y), new xPoint(v.Pos.X, v.Pos.Y), fm, true, is_space);
            }
            else
            {
                double width = _w[ch];
                return _factory.CreateGlyph(new Path[] { sg }, new xPoint(width, 0), new xPoint(0, 0), fm, true, is_space);
            }
        }
    }

    internal sealed class CIDT1CFont<Path> : Type1CFont<Path>
    {
        #region Variables and properties

        /// <summary>
        /// Horizontal font metrix
        /// </summary>
        private readonly PdfHMTX _w;

        /// <summary>
        /// Vertical font metrix
        /// </summary>
        private readonly PdfVMTX _v;

        /// <summary>
        /// Whenever this is a vertical font or not
        /// </summary>
        private readonly bool _vertical;

        #endregion

        #region Init

        internal CIDT1CFont(CIDFontType0 font, IFontFactory<Path> factory, bool vertical)
            : base(font, factory)
        {
            _vertical = vertical;
            _w = font.W;

            if (vertical)
                _v = font.W2;
        }

        internal CIDT1CFont(CIDFontType2 font, IFontFactory<Path> factory, bool vertical, Stream cff_font)
            : base(font, factory, cff_font)
        {
            _vertical = vertical;
            _w = font.W;

            if (vertical)
                _v = font.W2;
        }

        #endregion

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            //It appears that the correct way to detect space is to
            //check if "code_point" == 32, however I need to create
            //a test file to check this out.
            bool space;
            var sg = _create_sg(ch, out space);
            //Debug.Assert(!space, "space character in the font. What to do?");
            //Answer: nothing

            if (Vertical)
            {
                Debug.Assert(false, "Untested codepath");
                var v = _v[ch];
                return _factory.CreateGlyph(new Path[] { sg.SG }, new xPoint(0, v.W1y), new xPoint(v.Pos.X, v.Pos.Y), sg.M, true, is_space);
            }
            else
            {
                double width = _w[ch];
                return _factory.CreateGlyph(new Path[] { sg.SG }, new xPoint(width, 0), new xPoint(0, 0), sg.M, true, is_space);
            }
        }

        /// <summary>
        /// Fetches a glyph by use of unicode
        /// </summary>
        /// <param name="unicode_string">String of unicde characters</param>
        /// <param name="cid_values">Ignored</param>
        /// <returns>Unicode glyphs</returns>
        /// <remarks>
        /// This method is suboptimal. Should set upt a unicode -> glyph index table over making
        /// a lookup on the name of every glyph.
        /// 
        /// Note, does not support surrogates (though surrogates are set to 0 by the function 
        /// that makes the unicode_string)
        /// </remarks>
        internal override rGlyph[] GetGlyphs(ushort[] unicode_string, Pdf.Font.rCMap.Glyph[] cid_codes)
        {
            var glyphs = new rGlyph[unicode_string.Length];
            for (int c = 0; c < unicode_string.Length; c++)
            {               
                rCMap.Glyph cid = cid_codes[c];
                rGlyph glyph = GetCachedGlyph(cid.CID);
                if (glyph != null)
                {
                    glyphs[c] = glyph;
                    continue;
                }

                var ch = (char)unicode_string[c];
                var ch_name = Enc.UnicodeToNames[ch];
                int glyphindex = Array.IndexOf<string>(_cfont.GlyphNames, ch_name);

                glyph = GetGlyph(glyphindex, cid.CodePoint == 32);

                glyphs[c] = glyph;
                AddCachedGlyph(cid.CID, glyph);
            }
            return glyphs;
        }
    }
}
