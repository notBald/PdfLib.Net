using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;
using PdfLib.Pdf.Font;
using PdfLib.Read.TrueType;
using System.Windows;
using System.Windows.Media;

namespace PdfLib.Render.Font
{
    internal sealed class CIDTTFont<Path> : TTFont<Path>
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

        //Some true type fonts have a CIDtoGID array that is to
        //be used to translate characters into the font's internal
        //glyph indexes
        private readonly ushort[] _cid_to_gid;

        //This is the cmap internal to the font. It is not the
        //cap supplied by the PDF file.
        private readonly CmapTable.CmapFormat _cmap;

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

        internal CIDTTFont(CIDFontType2 font, bool vertical, IFontFactory<Path> factory)
            : base(font, factory)
        {
            _vertical = vertical;
            _w = font.W;
            _cid_to_gid = font.CIDToGIDMap;
            _cmap = null;

            if (vertical)
                _v = font.W2;
        }

        /// <summary>
        /// Used for the Droid font 
        /// </summary>
        internal CIDTTFont(PdfCIDFont font, Stream substitute_font, bool vertical, IFontFactory<Path> factory)
            : base(substitute_font, factory)
        {
            _vertical = vertical;
            _w = font.W;
            _cid_to_gid = null;
            var td = (TableDirectory)GetTD();
            _cmap = td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Microsoft, (ushort) MicrosoftEncodingID.Unicode);
            if (_cmap == null)
                _cmap = td.Cmap.GetCmap(Read.TrueType.PlatformID.AppleUnicode, 3);

            if (vertical)
                _v = font.W2;
        }

        #endregion

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            //Todo: ignores the font's internal cmaps altogether 
            // (perhaps ignoring internal cmaps is correct though)

            int g_index = ch;
            if (_cid_to_gid != null)
                g_index = _cid_to_gid[ch];

            if (_vertical)
            {
                var v = _v[ch];
                return RenderGlyph(g_index, 0, v.W1y, v.Pos.X, v.Pos.Y, is_space);
            }
            else
            {
                return RenderGlyph(g_index, _w[ch], 0, 0, 0, is_space);
            }
        }

        /// <remarks>_cid_to_gid does not make sense in this function</remarks>
        internal override rGlyph[] GetGlyphs(ushort[] unicode_string, rCMap.Glyph[] cid_codes)
        {
            //The way space needs to be done is through use of "codepoints". A code point
            //is the raw value used to create the CID. Headache headache
            rGlyph[] glyphs = new rGlyph[unicode_string.Length];

            for (int c = 0; c < glyphs.Length; c++)
            {
                var glyphIndex = _cmap.GetGlyphIndex(unicode_string[c]);
                rCMap.Glyph cid = cid_codes[c];

                rGlyph glyph = GetCachedGlyph(cid.CID);
                if (glyph != null)
                {
                    glyphs[c] = glyph;
                    continue;
                }

                if (_vertical)
                {
                    var v = _v[cid.CID];
                    glyph = RenderGlyph(glyphIndex, 0, v.W1y, v.Pos.X, v.Pos.Y, cid.CodePoint == 32);
                }
                else
                {
                    glyph = RenderGlyph(glyphIndex, _w[cid.CID], 0, 0, 0, cid.CodePoint == 32);
                }

                glyphs[c] = glyph;
                AddCachedGlyph(cid.CID, glyph);
            }

            return glyphs;
        }
    }
}
