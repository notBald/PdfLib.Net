using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using PdfLib.Util;

namespace PdfLib.Render.Font.TT
{
    /// <summary>
    /// Horizontal Metrics
    /// </summary>
    internal class HmtxTable : Table
    {
        #region Variables and properties

        public override bool Valid { get { return _td.IsValid(Tag.hmtx); } }
        
        public readonly LhmxAr Lhmx;

        #endregion

        #region Init

        /// <summary>
        /// Creates a horizontal metrics table
        /// </summary>
        /// <param name="td">Parent file</param>
        /// <param name="s">Stream to read data from</param>
        /// <param name="n_hmtx">number of metrix</param>
        /// <param name="n_glyphs">number of glyphs</param>
        public HmtxTable(TableDirectory td, StreamReaderEx s, int n_hmtx, int n_glyphs, uint length)
            : base(td)
        {
            var lhmx = new LongHorMetrics[n_hmtx];
            for (int c = 0; c < lhmx.Length; c++)
                lhmx[c] = new LongHorMetrics(s.ReadUShort(), s.ReadShort());

            //Seems like some fonts have wrong n_glyph count. Solved it by not
            //trusting the n_glyph count.
            int nlsb = n_glyphs - n_hmtx;
            if (length - 4 * n_hmtx < nlsb * 2) 
                nlsb = (int) Math.Max(0, (length - 4 * n_hmtx) / 2);
            var lsb = new short[nlsb];

            for (int c = 0; c < lsb.Length; c++)
                lsb[c] = s.ReadShort();

            Lhmx = new LhmxAr(lhmx, lsb);
        }

        #endregion
    }

    /// <summary>
    /// Long horizontal metrics array.
    /// </summary>
    /// <remarks>
    /// Pads itself automatically with the last
    /// entery
    /// </remarks>
    internal class LhmxAr
    {
        readonly LongHorMetrics[] _lhmx;
        readonly short[] _lsb;
        internal LhmxAr(LongHorMetrics[] ar, short[] lsb) { _lhmx = ar; _lsb = lsb; }
        public LongHorMetrics this[int i]
        {
            get
            {
                //If the font is monospaced, all lmhx metrix equals the lst
                //lhmx entery.
                if (i >= _lhmx.Length)
                    return new LongHorMetrics(_lhmx[_lhmx.Length - 1].AdvanceWidth, _lsb[i - _lhmx.Length]);
                return _lhmx[i];
            }
        }
    }

    /// <summary>
    /// Glyph metrixs
    /// 
    /// 
    /// <-LSB->Glyph<----RSB-->
    /// 
    /// 
    /// ---- AdvanceWidth -----
    /// x Origin
    /// </summary>
    internal struct LongHorMetrics
    {
        /// <summary>
        /// How much the renderer is to advance. Can be shorter than the glyph
        /// </summary>
        public readonly ushort AdvanceWidth;

        /// <summary>
        /// Left side bearings.
        /// 
        /// This is the white space to the left of th glyoh
        /// </summary>
        public readonly short Lsb;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="aw">AdvanceWidth</param>
        /// <param name="lsb">Left Side Bearing</param>
        public LongHorMetrics(ushort aw, short lsb) { AdvanceWidth = aw; Lsb = lsb; }

        /// <summary>
        /// Calculates the Right Side Bearing (white space at the right of the glyph)
        /// </summary>
        /// <param name="glyph">Glyph this LongHorMertric belongs with</param>
        /// <returns>Right Side bearing</returns>
        /// <remarks>
        /// var p = _glyf.GetGlyph(glyph_index);
        /// var rsb = _hmtx.Lhmx[glyph_index].CalculateRSB(p);
        /// </remarks>
        public short CalculateRSB(Glyph glyph)
        { return (short) (AdvanceWidth - (Lsb + glyph.XMax - glyph.XMin)); }
    }
}
