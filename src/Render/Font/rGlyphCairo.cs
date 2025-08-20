//Allows for reusing the code written for DrawDC, with a quick fallback to check for
//bugs and other issues. This must also be defined/undefined in a varity of other files.

using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render.CairoLib;

namespace PdfLib.Render.Font
{
    internal sealed class rGlyphCairo : rGlyph
    {
        /// <summary>
        /// Glyph outline
        /// </summary>
        public readonly CairoPath[] Outlines;

        /// <summary>
        /// Movement from this glyph after painting it
        /// </summary>
        public readonly xPoint MoveDist;

        /// <summary>
        /// Offset from where to draw the glyph
        /// </summary>
        public readonly xPoint Origin;

        /// <summary>
        /// Transform for this glyph that scales it down to 1x1
        /// </summary>
        public readonly cMatrix Transform;

        /// <summary>
        /// Space glyphs are used to sepperate words.
        /// </summary>
        public readonly bool IsSpace;

        /// <summary>
        /// Creates a PDFGlyph
        /// </summary>
        /// <param name="outline">Shape of the glyph</param>
        /// <param name="advance">XY advance</param>
        /// <param name="origin">XY offset from where to draw the glyph</param>
        /// <param name="mt">A transform to apply before drawing</param>
        /// <param name="freeze">Freezes the outlines inside the collectinon</param>
        /// <param name="is_space_char">If this is a space character</param>
        public rGlyphCairo(CairoPath[] outline, xPoint advance, xPoint origin, xMatrix mt, bool is_space_char)
        {
            MoveDist = advance;
            Transform = new cMatrix(mt);
            
            IsSpace = is_space_char;
            Origin = origin;
            Outlines = outline;
        }
    }
}
