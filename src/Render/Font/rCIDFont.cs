using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;

namespace PdfLib.Render.Font
{
    internal sealed class rCIDFont : rFont
    {
        #region Variables and properties

        public override bool Vertical { get { return _cmap.WMode; } }

        private readonly rFont _child;

        private readonly rCMap _cmap;

        internal override IFontFactory FontFactory => _child.FontFactory;

        #endregion

        #region Init

        public rCIDFont(rCMap cmap, rFont child_font)
            : base(null) 
        {
            _child = child_font;

            //Note. Current impl only tested with CIDFontType2.
            _cmap = cmap;
        }

        protected sealed override void InitImpl()
        {
            _child.Init();
        }

        public override void Dismiss()
        {
            _child.Dismiss();
        }

        public override void DisposeImpl()
        {
            _child.Dispose();
        }

        #endregion

        public override string GetUnicode(byte[] str)
        {
            if (_cmap.Unicode)
            {
                throw new NotImplementedException();
            }
            else
            {
                throw new NotImplementedException();
                //var glyphs = _cmap.Map(str);
                //var sb = new StringBuilder(glyphs.Length);
                //foreach(var glyph in glyphs)
                //{
                //    sb.Append((char)glyph.CID);
                //}
                //return sb.ToString();
            }
        }

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            throw new NotSupportedException();
        }

        internal override rGlyph[] GetGlyphs(byte[] str)
        {
            //Using a sepperate codepath for unicode
            if (_cmap.Unicode)
            {
                ushort[] unicode_string;
                rCMap.Glyph[] glyphs = _cmap.Map(str, out unicode_string);

                return _child.GetGlyphs(unicode_string, glyphs);
            }
            else
            {
                //Combine bytes to raw character values
                var glyphs = _cmap.Map(str);
                var r_glyphs = new rGlyph[glyphs.Length];
                
                for (int c = 0; c < glyphs.Length; c++)
                    r_glyphs[c] = _child.GetCachedGlyph(glyphs[c].CID, glyphs[c].CodePoint == 32);

                return r_glyphs;
            }
        }
    }

    /// <summary>
    /// Font with a ToUnicode cmap
    /// </summary>
    internal sealed class rUnicodeFont : rFont
    {
        #region Variables and properties

        public override bool Vertical { get { return _cmap.WMode; } }

        private readonly rFont _child;

        private readonly rCMap _cmap;

        /// <summary>
        /// Fontfactory is irrelevant for this font
        /// </summary>
        internal override IFontFactory FontFactory => null;

        #endregion

        #region Init

        public rUnicodeFont(rCMap cmap, rFont child_font)
            : base(null)
        {
            _child = child_font;

            //Note. Current impl only tested with CIDFontType2.
            _cmap = cmap;

            //Note: This bool does not actually say if the cmap is a ToUnicode cmap.
            //      To test for that one needs to look into the PSCodeMap
            if (!_cmap.Unicode)
                throw new PdfInternalException("Not a unicode cmap");
        }

        protected sealed override void InitImpl()
        {
            _child.Init();
        }

        public override void Dismiss()
        {
            _child.Dismiss();
        }

        public override void DisposeImpl()
        {
            _child.Dispose();
        }

        #endregion

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            throw new NotSupportedException();
        }

        internal override rGlyph[] GetGlyphs(byte[] str)
        {
            ushort[] unicode_string;
            rCMap.Glyph[] glyphs = _cmap.Map(str, out unicode_string);

            return _child.GetGlyphs(unicode_string, glyphs);
        }
    }
}
