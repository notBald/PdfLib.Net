using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Render.Font.TT
{
    /// <summary>
    /// The 'loca' table stores the offsets to the locations of the glyphs in the font 
    /// relative to the beginning of the 'glyf' table.
    /// </summary>
    internal class LocaTable : Table
    {
        #region Variables and properties

        public override bool Valid { get { return _td.IsValid(Tag.loca); } }

        /// <summary>
        /// Glyfoffset. Adjusted so they count from the
        /// beginning of the file (instead from the start
        /// of the glyf table)
        /// </summary>
        internal readonly uint[] GlyfOffsets;

        #endregion

        #region Init

        public LocaTable(TableDirectory td, StreamReaderEx s, int n_glypgs, int indexToLocFormat, uint glyf_table_offset)
            : base(td)
        {
            GlyfOffsets = new uint[n_glypgs];
            if (indexToLocFormat == 0)
            {
                for (int c = 0; c < GlyfOffsets.Length; c++)
                    GlyfOffsets[c] = glyf_table_offset + s.ReadUShort() * (uint)2;
            }
            else if (indexToLocFormat == 1)
            {
                for (int c = 0; c < GlyfOffsets.Length; c++)
                    GlyfOffsets[c] = glyf_table_offset + s.ReadUInt();
            }
            else
                throw new UnknownFormatException();
        }

        #endregion
    }
}
