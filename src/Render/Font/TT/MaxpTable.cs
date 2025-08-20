using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Render.Font.TT
{
    /// <summary>
    /// The 'maxp' table establishes the memory requirements for a font.
    /// </summary>
    class MaxpTable : Table
    {
        public override bool Valid { get { return _td.IsValid(Tag.maxp); } }

        /// <summary>
        /// The number of glyphs in the font
        /// </summary>
        public readonly ushort NumGlyphs;

        /// <summary>
        /// Points in non-compound glyph
        /// </summary>
        public readonly ushort MaxPoints;

        /// <summary>
        /// Contours in non-compound glyph
        /// </summary>
        public readonly ushort MaxContours;

        /// <summary>
        /// Points in compound glyph
        /// </summary>
        public readonly ushort MaxComponentPoints;

        /// <summary>
        /// Contours in compound glyph
        /// </summary>
        public readonly ushort MaxComponentContours;

        /// <summary>
        /// Set to 2
        /// </summary>
        public readonly ushort MaxZones;

        /// <summary>
        /// Points used in Twilight Zone (Z0)
        /// </summary>
        public readonly ushort MaxTwilightPoints;

        /// <summary>
        /// Number of Storage Area locations
        /// </summary>
        public readonly ushort MaxStorage;

        /// <summary>
        /// Number of FDEFs
        /// </summary>
        public readonly ushort MaxFunctionDefs;

        /// <summary>
        /// Number of IDEFs
        /// </summary>
        public readonly ushort MaxInstructionDefs;

        /// <summary>
        /// Maximum stack depth
        /// </summary>
        public readonly ushort MaxStackElements;

        /// <summary>
        /// Byte count for glyph instructions
        /// </summary>
        public readonly ushort MaxSizeOfInstructions;

        /// <summary>
        /// Number of glyphs referenced at top level
        /// </summary>
        public readonly ushort MaxComponentElements;

        /// <summary>
        /// Levels of recursion, set to 0 if font has only simple glyphs 
        /// </summary>
        public readonly ushort MaxComponentDepth;

        internal MaxpTable(TableDirectory td, StreamReaderEx s)
            : base(td)
        {
            var Version = TableDirectory.ReadFixed(s);
            if (Version != 1.0) throw new UnknownVersionException("MaxpTable");
            NumGlyphs = s.ReadUShort();
            MaxPoints = s.ReadUShort();
            MaxContours = s.ReadUShort();
            MaxComponentPoints = s.ReadUShort();
            MaxComponentContours = s.ReadUShort();
            MaxZones = s.ReadUShort();
            MaxTwilightPoints = s.ReadUShort();
            MaxStorage = s.ReadUShort();
            MaxFunctionDefs = s.ReadUShort();
            MaxInstructionDefs = s.ReadUShort();
            MaxStackElements = s.ReadUShort();
            MaxSizeOfInstructions = s.ReadUShort();
            MaxComponentElements = s.ReadUShort();
            MaxComponentDepth = s.ReadUShort();
        }
    }
}
