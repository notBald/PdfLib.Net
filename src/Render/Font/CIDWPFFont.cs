using System;
using System.Windows;
using System.Windows.Media;
using PdfLib.Pdf.Font;
using System.Collections.Generic;

namespace PdfLib.Render.Font
{
    /// <summary>
    /// Renders CID fonts using WPF
    /// </summary>
    /// <remarks>Uses it's own width implementation</remarks>
    internal class CIDWPFFont : WPFFont
    {
        #region Variables and properties

        //May be inefficent for large fonts, as each character is
        //mapped into this dict
        private readonly PdfHMTX _w;
        private readonly double _dw;

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

        internal CIDWPFFont(string name, string alt_name, AdobeFontMetrixs afm, PdfCIDFont font, bool vertical, IFontFactory factory)
            : base(name, alt_name, null, afm, factory)
        {
            _vertical = vertical;
            _w = font.W;
            _dw = font.DW / 1000d;

            if (vertical)
                _v = font.W2;
        }

        #endregion

        /// <summary>
        /// Renders glyphs based on a unicode string
        /// </summary>
        internal override rGlyph[] GetGlyphs(ushort[] unicode_string, rCMap.Glyph[] cid_codes)
        {
            ushort glyphIndex;
            rGlyph[] rg = new rGlyph[unicode_string.Length];

            //Translates over to glyphindexes
            for (int c = 0; c < unicode_string.Length; c++)
            {
                if (!_gtf.CharacterToGlyphMap.TryGetValue(unicode_string[c], out glyphIndex))
                    glyphIndex = 0;

                if (_vertical)
                {
                    var v = _v[cid_codes[c].CID];
                    rg[c] = GetGlyph((char)unicode_string[c], glyphIndex, 0, v.W1y, v.Pos.X, v.Pos.Y, cid_codes[c].CodePoint == 32);
                }
                else
                    rg[c] = GetGlyph((char)unicode_string[c], glyphIndex, _w[cid_codes[c].CID], 0, 0, 0, cid_codes[c].CodePoint == 32);
            }

            return rg;

            /** A non WPF way of doing it:
            var font = _gtf.GetFontStream();
            var td = new TT.TableDirectory(font);
            var unicode_cmap = td.Cmap.GetCmap(TrueType.PlatformID.Microsoft, (ushort) TrueType.MicrosoftEncodingID.encodingUGL);
            // Use  unicode_cmap.GetGlyphIndex here
            font.Close();
              
            //for surrogates one needs a CMap that supports it. Apple has defined a format that supports it, but
            //it's not present in common Windows font files at least. Uh, not that PDF support surrogates
            */
        }

        protected override rGlyph GetGlyph(int c, bool is_space)
        {
            //AFAICT the "c" comming in here is already
            //as it should be. 
            var glyphIndex = (ushort) c;
            
            //This hack to is get the "space" character to render correctly.
            //I.e. we're transforming a glyph index back to a character. It
            //does not matter if this character is wrong as long as it works
            //for the "space" character. 
            var ch = -1; //(Now using "is_space" instead...)

            //Doing a reverse lookup for the charcode
            foreach (KeyValuePair<int, ushort> kp in _gtf.CharacterToGlyphMap)
                if (kp.Value == glyphIndex)
                {
                    ch = kp.Key;
                    break;
                }

            if (Vertical)
            {
                var v = _v[ch];
                return GetGlyph(ch, glyphIndex, 0, v.W1y, v.Pos.X, v.Pos.Y, is_space);
            }
            else
            {
                //Fetches the width
                double width = _w[c];
                return GetGlyph(ch, glyphIndex, width, 0, 0, 0, is_space);
            }
        }
    }
}
