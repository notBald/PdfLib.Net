//Glyph scaling does not work right. It's suppose to scale glyphs up to
//the size of the adobe font metrix, but while it manages that it gets
//the glyph position wrong.
//
//I'm putting this feature on hold. I'm thinking that I'll get back to
//this after fixing up text rendering in Cairo. In all likelyhood the
//problem is best adressed by dropping WPF font in favor of some sort
//of "built in font" class that handles cases where CID fonts don't
//supply the font data and dosn't use one of the 14 built in fonts. 
//#define SCALE_GLYPH
using System;
using System.Windows;
using System.Windows.Media;
using PdfLib.Pdf.Font;
using PdfLib.Read.TrueType;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;

namespace PdfLib.Render.Font
{
    class WPFFont : rFont
    {
        protected readonly int[] _encode;
#if SCALE_GLYPH
        protected readonly AFMGlyphInfo[] _glyph_meta;
#endif
        readonly string _name, _alt_name;
        readonly FontStyle _fs;
        readonly FontWeight _fw;
        protected GlyphTypeface _gtf = null;

        public override bool Vertical { get { return false; } }

        internal System.IO.Stream FontFile
        { get { return _gtf.GetFontStream(); } }

        internal TableDirectory FontDir
        { get { return new TableDirectory(FontFile); } }

        private readonly IFontFactory _factory;
        internal sealed override IFontFactory FontFactory => _factory;

        #region Init and dispose

        internal WPFFont(string name, string alt_name, double[] widths, AdobeFontMetrixs afm, IFontFactory factory)
            : base(widths)
        {
            _factory = factory;
            _name = name;
            _alt_name = alt_name;
            var names = afm.Encoding;
            _encode = (afm.Symbolic) ? afm.XrefTable : PdfEncoding.CreateUnicodeEnc(names);
            _fw = (afm.Bold) ? FontWeights.Bold : FontWeights.Normal;
            _fs = (afm.Italic) ? FontStyles.Italic : FontStyles.Normal;

#if SCALE_GLYPH
            _glyph_meta = new AFMGlyphInfo[256];
            var gi = afm.GlyfInfo;
            for (int c = 0; c < _encode.Length; c++)
            {
                name = names[_encode[c]];
                gi.TryGetValue(name, out _glyph_meta[c]);
            }
#endif
        }

        protected sealed override void InitImpl()
        {
            if (_gtf != null) return;
            Typeface tf;

            tf = new Typeface(new FontFamily(_name), _fs, _fw, FontStretches.Normal);
            if (tf.TryGetGlyphTypeface(out _gtf))
                return;

            tf = new Typeface(new FontFamily(_alt_name), _fs, _fw, FontStretches.Normal);
            if (tf.TryGetGlyphTypeface(out _gtf))
                return;

            //Todo: Throw exception or take another suitable action.
            throw new NotImplementedException();
        }

        public sealed override void Dismiss()
        {
            _gtf = null;
        }

        public sealed override void DisposeImpl()
        {

        }

        #endregion

        #region Glyph rendering

        protected override rGlyph GetGlyph(int c, bool is_space)
        {
            double width;

            //Translates into unicode character value
            int ch = _encode[c];
            //System.Diagnostics.Debug.Assert((ch == 32) == is_space, "Space missmatch, what to do?"); //Answer: Nothing

            ushort glyphIndex;
            if (!_gtf.CharacterToGlyphMap.TryGetValue(ch, out glyphIndex))
            {
                //Todo: Logg error
                //throw new NotImplementedException("Failed to get glyph 0");
                width = (_widths != null) ? _widths[c] / 1000d : 1;
                return new rGlyphWPF(new GeometryGroup(), new Point(width, 0), new Point(0, 0), Matrix.Identity, false);
            }

            //Assumes that all built in fonts are horizontal
            if (_widths == null)
                width = _gtf.AdvanceWidths[glyphIndex]*1000;
            else
                width = _widths[c];

            return GetGlyph(c, glyphIndex, width/1000d, 0, 0, 0, is_space);
        }

        /// <summary>
        /// Creates a glyph geomerty object
        /// </summary>
        /// <param name="org_ch">Original charcode, not CID</param>
        /// <param name="glyphIndex">Glyphindex inside the font</param>
        /// <param name="width">Width of the chacacter</param>
        /// <param name="height">Height of the character</param>
        /// <param name="origin_x">x placement</param>
        /// <param name="origin_y">y placement</param>
        /// <param name="is_space">If this is a space character (ignored)</param>
        /// <returns>A glyph with all information needed for rendering</returns>
        protected rGlyph GetGlyph(int org_ch, ushort glyphIndex, double width, double height, double origin_x, double origin_y, bool is_space)
        {
            //Not entierly sure what a glyph index is, but I assume it's a
            //mapping from the character code to the glyph to be drawn
            ushort[] glyphIndexes = new ushort[] { glyphIndex };
            Matrix st;

            //Tells WPF to build the glyphs
            GlyphRun glyphRun = new GlyphRun(_gtf, 0, false, 1,
                    glyphIndexes, new Point(0, 0), new double[] { width }, null, null, null, null,
                    null, null);
            var collection = glyphRun.BuildGeometry();

#if SCALE_GLYPH
            if (org_ch != -1)
            {
                var afm = _glyph_meta[org_ch];
                Rect ink_box = collection.Bounds;

                //This is not 100% correct, but the best I've come up with at the moment. It might
                //be smarter to simply drop the WPFFont in favor of one that manually builds the
                //glyph. 
                double afm_width = afm.Width / 1000d;
                double afm_height = afm.Height / 1000d;
                double scale_x = afm_width / ink_box.Width;
                double scale_y = afm_height / ink_box.Height;
                double offset_x = afm.llx / 1000d;//((scale_x - 1) * afm_width) / 2;
                double offset_y = afm.lly / 1000d;//((scale_y - 1) * afm_height) / 2;

                //First the character is moved to its origin
                st = new Matrix(1, 0, 0, 1, -ink_box.Left, -ink_box.Top);

                //The glyph must be flipped.
                st.Prepend(new Matrix(1, 0, 0, -1, 0, ink_box.Top + ink_box.Bottom));

                //Then the glyph is scaled up to the size we need it to be
                st.Prepend(new Matrix(scale_x, 0, 0, scale_y, 0, 0));

                //Finally the glyph is moved to its actual location
                st.Prepend(new Matrix(1, 0, 0, 1, offset_x, -offset_y));
            }
            else
            {
                //Cid fonts needs som TLC as org_char don't map to _glyph_meta
                st = new Matrix(1, 0, 0, -1, 0, 0/*1 - _gtf.AdvanceHeights[glyphIndex]*/);
            }
#else
            //The text needs to be flipped. The flipped text is then adjusted
            //down with the height of the text.
            // Todo: Is AdvanceHeights correct?
            //  Reply: Apparently not. Seems one get better result with a plain zero
            st = new Matrix(1, 0, 0, -1, 0, 0/*1 - _gtf.AdvanceHeights[glyphIndex]*/);
#endif
            if (collection is StreamGeometry)
            {
                var sg = (StreamGeometry)collection;

                return new rGlyphWPF(new StreamGeometry[] { sg }, new Point(width, height), new Point(origin_x, origin_y), st, true, is_space);
            }
            else if (collection is GeometryGroup)
                return new rGlyphWPF((GeometryGroup)collection, new Point(width, height), new Point(origin_x, origin_y), st, is_space);

            throw new NotSupportedException();
        }

        #endregion
    }
}
