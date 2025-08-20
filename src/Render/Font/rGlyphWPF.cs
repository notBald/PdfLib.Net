using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PdfLib.Render.Font
{
    internal sealed class rGlyphWPF : rGlyph
    {
        /// <summary>
        /// Glyph outline
        /// </summary>
        public readonly GeometryGroup Outlines;

        /// <summary>
        /// Movement from this glyph after painting it
        /// </summary>
        public readonly Point MoveDist;

        /// <summary>
        /// Offset from where to draw the glyph
        /// </summary>
        public readonly Point Origin;

        /// <summary>
        /// Transform for this glyph that scales it down to 1x1
        /// </summary>
        public readonly Matrix Transform;

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
        public rGlyphWPF(StreamGeometry[] outline, Point advance, Point origin, Matrix mt, bool freeze, bool is_space_char)
        {
            MoveDist = advance;
            if (freeze)
                for(int i=0; i < outline.Length; i++)
                    outline[i].Freeze();
            Transform = mt;
            GeometryCollection gc = new GeometryCollection(outline.Length);
            for (int i = 0; i < outline.Length; i++)
                gc.Add(outline[i]);
            gc.Freeze();
            Outlines = new GeometryGroup() { Children = gc, FillRule = FillRule.Nonzero };
            Outlines.Freeze();
            IsSpace = is_space_char;
            Origin = origin;
        }

        /// <summary>
        /// Creates a PDFGlyph
        /// </summary>
        /// <param name="outline">Shape of the glyph</param>
        /// <param name="advance">XY advance</param>
        /// <param name="origin">XY offset from where to draw the glyph</param>
        /// <param name="mt">A transform to apply before drawing</param>
        /// <param name="is_space_char">If this is a space character</param>
        public rGlyphWPF(GeometryGroup outline, Point advance, Point origin, Matrix mt, bool is_space_char)
        {
            MoveDist = advance;
            Transform = mt;
            Outlines = outline;
            Outlines.Freeze();
            IsSpace = is_space_char;
            Origin = origin;
        }
    }
}
