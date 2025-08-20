using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Read.CFF;
using PdfLib.Pdf.Font;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Measure Compact Font Format Fonts
    /// </summary>
    /// <remarks>
    /// Looks like I got to implement my own glyph parser (that inherits from parser)
    /// ParserCMD1 must also be modified so that if false is returned, execution ends.
    /// </remarks>
    public class MCFFFont : MeasureFont
    {
        private readonly TopDICT _td;

        /// <summary>
        /// The height from baseline to baseline
        /// </summary>
        public override double LineHeight
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Height from baseline up to highest point
        /// </summary>
        public override double Ascent
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Height from baseline to bottom
        /// </summary>
        public override double Descent
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Height from baseline to top of captial H
        /// </summary>
        public override double CapHeight
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// The underline's position relative to the baseline
        /// </summary>
        public override double UnderlinePosition
        {
            get { return _td.UnderlinePosition; }
        }

        /// <summary>
        /// The thickness of the underline
        /// </summary>
        public override double UnderlineThickness
        {
            get { return _td.UnderlineThickness; }
        }

        #region Init

        public MCFFFont(Stream file)
            :this(new Util.StreamReaderEx(file))
        { }

        public MCFFFont(Util.StreamReaderEx s)
        {
            //Reads the header of the font
            var header = CFont.ReadHeader(s);
            if (header.major != 1) throw new NotSupportedException("Unsupported CFF file");

            //Move to Name INDEX
            s.Position = header.hdrSize;
            var named_index = CFont.ReadIndex(s);

            //Moves to the Top DICT INDEX
            s.Position = named_index.end;
            var TopDI = CFont.ReadIndex(s);

            //Only supports files with one font.
            //Alt. one could just default to the first font
            if (TopDI.count != 1)
                throw new NotSupportedException("Multiple fonts in CFF file");

            //Moves to the String INDEX
            s.Position = TopDI.end;
            var StringIndex = CFont.ReadIndex(s);
            var strings = CFont.ReadNames(StringIndex, s);

            //Executes the dictionary for the first font.
            _td = new TopDICT(s);
            _td.Parse(TopDI.GetSizePos(0, s));

            if (_td.CID != null)
            {
                throw new NotImplementedException("Measuring CFF CID fonts");
            }
            else
            {
                Index glyph_table;
                var glyph_names = CFont.ReadGlyphNames(_td, s, out glyph_table);
            }
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
            return 0;
        }

        /// <summary>
        /// Gets the width of a glyph
        /// </summary>
        /// <param name="gid">Glyph id</param>
        /// <returns>Width</returns>
        public override double GetWidth(int gid)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets width and YMin/Max for a glyph
        /// </summary>
        public override chGlyph GetGlyphInfo(int gid)
        {
            throw new NotImplementedException();
        }
    }
}
