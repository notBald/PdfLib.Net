using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Render.Font.TT
{
    /// <summary>
    /// Vertical Header
    /// </summary>
    internal class VheaTable : Table
    {
        #region Variables and properties

        public override bool Valid { get { return _td.IsValid(Tag.vhea); } }

        /// <summary>
        /// Typographic ascent in em units
        /// </summary>
        public readonly short Ascent;

        /// <summary>
        /// Typographic descent in em units
        /// </summary>
        public readonly short Descent;

        /// <summary>
        /// The maximum advance height measurement in FUnits
        /// </summary>
        public readonly short AdvanceHeightMax;

        /// <summary>
        /// The minimum top sidebearing measurement found in 
        /// the font, in FUnits. 
        /// </summary>
        public readonly short MinTopSideBearing;

        /// <summary>
        /// The minimum bottom sidebearing measurement found 
        /// in the font, in FUnits. 
        /// </summary>
        public readonly short MinBottomSideBearing;

        /// <summary>
        /// Defined as yMaxExtent=minTopSideBearing+(yMax-yMin)
        /// </summary>
        public readonly short YMaxExtent;

        /// <summary>
        /// Field determines the slope of the caret.
        /// </summary>
        public readonly short CaretSlopeRise;

        /// <summary>
        /// Field determines the slope of the caret.
        /// </summary>
        public readonly short CaretSlopeRun;

        /// <summary>
        /// Field determines the slope of the caret.
        /// </summary>
        public readonly short CaretOffset;

        /// <summary>
        /// Number of advance heights in the vertical 
        /// metrics table.
        /// </summary>
        public readonly ushort NumOfLongVerMetrics;

        #endregion

        #region Init

        public VheaTable(TableDirectory td, StreamReaderEx s)
            : base(td)
        {
            var version = TableDirectory.ReadFixed(s);
            if (version != 1.0) throw new UnknownVersionException();
            Ascent = s.ReadShort();
            Descent = s.ReadShort();
            Debug.Assert(s.ReadShort() == 0, "LineGap");
            AdvanceHeightMax = s.ReadShort();
            MinTopSideBearing = s.ReadShort();
            MinBottomSideBearing = s.ReadShort();
            YMaxExtent = s.ReadShort();
            CaretSlopeRise = s.ReadShort();
            CaretSlopeRun = s.ReadShort();
            CaretOffset = s.ReadShort();
            Debug.Assert(s.ReadShort() == 0, "reserved");
            Debug.Assert(s.ReadShort() == 0, "reserved");
            Debug.Assert(s.ReadShort() == 0, "reserved");
            Debug.Assert(s.ReadShort() == 0, "reserved");
            if (s.ReadShort() != 0)
                throw new UnknownVersionException("MetricDataFormat");
            NumOfLongVerMetrics = s.ReadUShort();
        }

        #endregion
    }
}
