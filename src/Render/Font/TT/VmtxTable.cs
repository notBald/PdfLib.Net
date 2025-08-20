using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using PdfLib.Util;

namespace PdfLib.Render.Font.TT
{
    /// <summary>
    /// Vertical metrics table
    /// </summary>
    internal class VmtxTable : Table
    {
        #region Variables and properties

        public override bool Valid { get { return _td.IsValid(Tag.vmtx); } }

        internal readonly LvmxAr Lvmx;

        #endregion

        #region Init

        /// <summary>
        /// Creates a vertical metrics table
        /// </summary>
        /// <param name="td">Parent file</param>
        /// <param name="s">Stream to read data from</param>
        /// <param name="n_vmtx">number of metrix</param>
        /// <param name="n_glyphs">number of glyphs</param>
        public VmtxTable(TableDirectory td, StreamReaderEx s, int n_vmtx, int n_glyphs, uint length)
            : base(td)
        {
            var lvmx = new LongVerMetrics[n_vmtx];
            for (int c = 0; c < lvmx.Length; c++)
                lvmx[c] = new LongVerMetrics(s.ReadUShort(), s.ReadShort());

            //Seems like some fonts have wrong n_glyph count. Solved it by not
            //trusting the n_glyph count.
            int ntsb = n_glyphs - n_vmtx;
            if (length - 4 * n_vmtx < ntsb * 2) 
                ntsb = (int) Math.Max(0, (length - 4 * n_vmtx) / 2);
            var tsb = new short[ntsb];

            //Reads in the left side bearings
            for (int c = 0; c < tsb.Length; c++)
                tsb[c] = s.ReadShort();

            Lvmx = new LvmxAr(lvmx, tsb);
        }

        #endregion
    }

    /// <summary>
    /// Long vertical metrics array.
    /// </summary>
    /// <remarks>
    /// Pads itself automatically with the last
    /// entery
    /// </remarks>
    internal class LvmxAr
    {
        readonly LongVerMetrics[] _lvmx;
        readonly short[] _tsb;
        internal LvmxAr(LongVerMetrics[] ar, short[] tsb) { _lvmx = ar; _tsb = tsb; }
        public LongVerMetrics this[int i]
        {
            get
            {
                //If the font is monospaced, all lmhx metrix equals the lst
                //lhmx entery.
                if (i >= _lvmx.Length)
                    return new LongVerMetrics(_lvmx[_lvmx.Length - 1].AdvanceHeight, _tsb[i - _lvmx.Length]);
                return _lvmx[i];
            }
        }
    }

    internal struct LongVerMetrics
    {
        /// <summary>
        /// How much the renderer is to advance. Can be shorter than the glyph
        /// </summary>
        public readonly ushort AdvanceHeight;

        /// <summary>
        /// Top side bearings.
        /// 
        /// This is the white space to the top of the glyoh
        /// </summary>
        public readonly short Tsb;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="aw">AdvanceHeight</param>
        /// <param name="tsb">Top Side Bearing</param>
        public LongVerMetrics(ushort ah, short tsb) { AdvanceHeight = ah; Tsb = tsb; }
    }
}
