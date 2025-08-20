using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Read.TrueType
{
    /// <summary>
    /// Horizontal Header
    /// </summary>
    internal class HheaTable : Table
    {
        #region Variables and properties

        public override bool Valid { get { return _td.IsValid(Tag.hhea); } }

        /// <summary>
        /// Typographic ascent in em units
        /// </summary>
        public readonly short Ascender;

        /// <summary>
        /// Typographic descent in em units
        /// </summary>
        public readonly short Descender;

        /// <summary>
        /// LineGap in em units
        /// </summary>
        public readonly short LineGap;

        /// <summary>
        /// Maximum htmx advance width value in em units
        /// </summary>
        public readonly ushort AdvanceWidthMax;

        /// <summary>
        /// Minimum left htmx sidebearing value in em units
        /// </summary>
        public readonly short MinLeftSideBearing;

        /// <summary>
        /// Minimum right htmx sidebearing value in em units
        /// </summary>
        public readonly short MinRightSideBearing;

        /// <summary>
        /// Max(lsb + (xMax - xMin))
        /// </summary>
        public readonly short XMaxExtent;

        /// <summary>
        /// Used to calculate the slope of the cursor (rise/run); 1 for vertical.
        /// </summary>
        public readonly short CaretSlopeRise;

        /// <summary>
        /// 0 for vertical.
        /// </summary>
        public readonly short CaretSlopeRun;

        public readonly short MetricDataFormat;

        /// <summary>
        /// Number of hMetric entries in  ‘hmtx’ table
        /// </summary>
        public readonly ushort NumberOfHMetrics;

        #endregion

        #region Init

        public HheaTable(TableDirectory td, StreamReaderEx s)
            : base(td)
        {
            var version = TableDirectory.ReadFixed(s);
            if (version != 1.0) throw new UnknownVersionException();
            Ascender = s.ReadShort();
            Descender = s.ReadShort();
            LineGap = s.ReadShort();
            AdvanceWidthMax = s.ReadUShort();
            MinLeftSideBearing = s.ReadShort();
            MinRightSideBearing = s.ReadShort();
            XMaxExtent = s.ReadShort();
            CaretSlopeRise = s.ReadShort();
            CaretSlopeRun = s.ReadShort();
            s.ReadShort();
            s.ReadShort();
            s.ReadShort();
            s.ReadShort();
            s.ReadShort();
            MetricDataFormat = s.ReadShort();
            NumberOfHMetrics = s.ReadUShort();
        }

        #endregion
    }
}
