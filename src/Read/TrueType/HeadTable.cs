using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Read.TrueType
{
    /// <summary>
    /// The 'head' table contains global information about the font. It records 
    /// such facts as the font version number, the creation and modification 
    /// dates, revision number and basic typographic data that applies to the 
    /// font as a whole. This includes a specification of the font bounding 
    /// box, the direction in which the font's glyphs are most likely to be 
    /// written and other information about the placement of glyphs in the em 
    /// square.
    /// </summary>
    class HeadTable : Table
    {
        public override bool Valid { get { return _td.IsValid(Tag.head); } }

        public readonly float FontRevision;

        /// <summary>
        /// To compute: set it to 0, calculate the checksum for the 'head' table 
        /// and put it in the table directory, sum the entire font as uint32, 
        /// then store B1B0AFBA - sum. The checksum for the 'head' table will 
        /// not be wrong. That is OK.
        /// </summary>
        public readonly uint CheckSumAdjustment;

        public readonly ushort Flags;
        public readonly long Created;
        public readonly long Modified;

        public readonly short xMin, yMin, xMax, yMax;

        public readonly ushort MacStyle;

        /// <summary>
        /// Smallest readable size in pixels
        /// </summary>
        public readonly ushort LowestRecPPEM;

        /// <summary>
        /// Range from 64 to 16384
        /// </summary>
        public readonly ushort UnitsPerEm;

        public readonly short FontDirectionHint, IndexToLocFormat, GlyphDataFormat;

        internal HeadTable(TableDirectory td, StreamReaderEx s)
            : base(td)
        {
            var version = TableDirectory.ReadFixed(s);
            if (version != 1.0) throw new UnknownVersionException("HeadTable");
            FontRevision = (float)TableDirectory.ReadFixed(s);
            CheckSumAdjustment = s.ReadUInt();
            uint magic = s.ReadUInt();
            if (magic != 0x5F0F3CF5) throw new UnknownVersionException();
            Flags = s.ReadUShort();
            UnitsPerEm = s.ReadUShort();
            Created = s.ReadLong();
            Modified = s.ReadLong();
            xMin = s.ReadShort();
            yMin = s.ReadShort();
            xMax = s.ReadShort();
            yMax = s.ReadShort();
            MacStyle = s.ReadUShort();
            LowestRecPPEM = s.ReadUShort();
            FontDirectionHint = s.ReadShort();
            IndexToLocFormat = s.ReadShort();
            GlyphDataFormat = s.ReadShort();
            if (GlyphDataFormat != 0) throw new UnknownVersionException();
        }
    }
}
