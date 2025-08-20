using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Read.TrueType;
using PdfLib.Pdf.Font;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Measure True Type font
    /// </summary>
    /// <remarks>
    /// Note: Tables are not cached, so every time a value is read they are reparesed.
    /// </remarks>
    internal class MTTFont : MeasureFont
    {
        #region Variables and properties

        /// <summary>
        /// Font parser
        /// </summary>
        readonly TableDirectory _td;

        /// <summary>
        /// Used to make FWord datatype into
        /// floating points
        /// </summary>
        /// <remarks>This is actually an int value stored in a double for convinience</remarks>
        readonly double _units_per_em;

        /// <summary>
        /// The glyf id of capital letter H
        /// </summary>
        readonly int _h_gid;

        /// <summary>
        /// Kerning information
        /// </summary>
        readonly Dictionary<uint, double> _kern;

        /// <summary>
        /// Override is not currently supported, will need some
        /// API changes to comunicate that the kern values are
        /// to override a glyph's width. (If I've understood
        /// override correctly. Need a font to test with before
        /// I bother beyond this)
        /// </summary>
        readonly bool _kern_override;

        /// <summary>
        /// The horizontal metrix for glyphs
        /// </summary>
        readonly LhmxAr _hmtx;

        /// <summary>
        /// Glyph data
        /// </summary>
        readonly GlyfTable _glyf;

        /// <summary>
        /// The height from baseline to baseline, expressed in em
        /// </summary>
        public override double LineHeight
        {
            get
            {
                var hhea = _td.Hhea;
                var os2 = _td.OS2;
                int ret_val;
                if (os2 != null)
                {
                    int windist = os2.WinAscent + os2.WinDescent;

                    ret_val = windist + Math.Max(0, hhea.LineGap - windist -
                        (hhea.Ascender - hhea.Descender));
                }
                else
                {
                    ret_val = hhea.Ascender + hhea.Descender + hhea.LineGap;
                }
                return ret_val / _units_per_em;
            }
        }

        /// <summary>
        /// Height from baseline up to highest point
        /// </summary>
        public override double Ascent
        {
            get
            {
                int ret_val;
                var os2 = _td.OS2;
                if (os2 != null)
                    ret_val = os2.WinAscent;
                else
                    ret_val = _td.Hhea.Ascender;
                return ret_val / _units_per_em;
            }
        }

        /// <summary>
        /// Height from baseline to bottom
        /// </summary>
        public override double Descent
        {
            get
            {
                int ret_val;
                var os2 = _td.OS2;
                if (os2 != null)
                    ret_val = Math.Abs(os2.WinDescent) * Math.Sign(_td.Hhea.Descender);
                else
                    ret_val = _td.Hhea.Descender;
                return ret_val / _units_per_em;
            }
        }

        /// <summary>
        /// Height from baseline to top of captial H
        /// </summary>
        public override double CapHeight
        {
            get
            {
                var os2 = _td.OS2;
                int ret_val;
                if (os2 != null && os2.Version >= 2)
                    ret_val = os2.CapHeight;
                else
                {
                    Glyph h = null;
                    if (_h_gid != 0)
                        h = _glyf.GetGlyph(_h_gid);
                    if (h != null)
                        ret_val = h.YMax;
                    else
                        ret_val = _td.Head.yMax;
                }
                return ret_val / _units_per_em;
            }
        }

        /// <summary>
        /// Height from baseline to top of lower case X
        /// </summary>
        public double XHeight
        {
            get
            {
                var os2 = _td.OS2;
                int ret_val;
                if (os2 != null && os2.Version >= 2)
                    ret_val = os2.XHeight;
                else
                {
                    Glyph x = null;
                    var map = _td.Cmap.GetCmap(Read.TrueType.PlatformID.Microsoft, 1);
                    int index = map.GetGlyphIndex((int)'x');
                    if (index != 0)
                        x = _glyf.GetGlyph(index);
                    if (x != null)
                        ret_val = x.YMax;
                    else
                        ret_val = _td.Head.yMax;
                }
                return ret_val / _units_per_em;
            }
        }

        /// <summary>
        /// The underline's position relative to the baseline
        /// </summary>
        public override double UnderlinePosition
        {
            get 
            {
                var post = _td.Post;
                if (post == null)
                    return Descent / 2;
                return post.UnderlinePosition / _units_per_em;
            }
        }

        /// <summary>
        /// The thickness of the underline
        /// </summary>
        public override double UnderlineThickness
        {
            get 
            {
                var post = _td.Post;
                if (post != null)
                    return post.UnderlineThickness / _units_per_em;
                return Ascent / 17.16; 
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Constructs a measure font
        /// </summary>
        /// <param name="td">Table directory to take measures from</param>
        /// <param name="h_gid">The glyph index of the H glyph, or 0</param>
        public MTTFont(TableDirectory td, int h_gid)
        {
            if (td.IsCFF)
                throw new NotSupportedException("Can't measure CFF fonts");

            _td = td;
            var head = _td.Head;
            _units_per_em = head.UnitsPerEm;
            _h_gid = h_gid;

            var kern = td.Kern;
            if (kern != null)
            {
                var f0 = kern.GetFormat0(true, false, false, false, _units_per_em);
                if (f0 != null)
                {
                    _kern = f0.Pairs;
                    _kern_override = f0.Override;
                }
            }

            _hmtx = _td.Hmtx.Lhmx;
            _glyf = _td.Glyf;
        }

        #endregion

        /// <summary>
        /// Fetches the kerning between two glyphs
        /// </summary>
        /// <param name="from_gid">Left hand glyph</param>
        /// <param name="to_gid">Right hand glyph</param>
        /// <returns>Their kerning</returns>
        public override double GetKerning(int from_gid, int to_gid)
        {
            if (_kern != null)
            {
                double val;
                uint pair = unchecked((uint)((from_gid << 16) | to_gid));
                if (_kern.TryGetValue(pair, out val))
                    return val;
            }
            return 0;
        }

        /// <summary>
        /// Gets the width of a glyph
        /// </summary>
        /// <param name="gid">Glyph id</param>
        /// <returns>Width</returns>
        public override double GetWidth(int gid)
        {
            return _hmtx[gid].AdvanceWidth / _units_per_em;
        }

        public chBox GetGlyphBox(int gid)
        {
            //Todo: This method parses the entire glyph when we only need X Y Max/Min, which is
            //      stored in the header. Make a function that only fetches that data.
            var g = _glyf.GetGlyph(gid);
            var lsb = _hmtx[gid];
            if (g == null)
                return new chBox((int) _units_per_em, lsb.AdvanceWidth, 0, 0, 0, 0);
            return new chBox((int)_units_per_em, lsb.AdvanceWidth, g.XMin, g.YMin, g.XMax, g.YMax);
        }

        /// <summary>
        /// Gets width and YMin/Max for a glyph
        /// </summary>
        /// <param name="gid"></param>
        /// <returns></returns>
        public override chGlyph GetGlyphInfo(int gid)
        {
            //Todo: This method parses the entire glyph when we only need X Y Max/Min, which is
            //      stored in the header. Make a function that only fetches that data.
            var g = _glyf.GetGlyph(gid);

            var lsb = _hmtx[gid];
            if (g == null)
                return new chGlyph(gid, (int) _units_per_em, lsb.AdvanceWidth, 0, 0, 0, 0);
            return new chGlyph(gid, (int)_units_per_em, lsb.AdvanceWidth, g.YMin, g.YMax, lsb.Lsb, lsb.CalculateRSB(g));
        }
    }
}
