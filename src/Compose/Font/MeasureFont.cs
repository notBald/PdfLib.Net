using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Base class for objects focused on taking meassurments of
    /// fonts
    /// </summary>
    /// <remarks>
    /// Putting this code in a sepperate class so that one later
    /// can use it with fonts one do not intend to embeded. 
    /// </remarks>
    public abstract class MeasureFont
    {
        #region Variables and properties

        /// <summary>
        /// The lineheight of this font, expressed in em.
        /// </summary>
        public abstract double LineHeight { get; }

        /// <summary>
        /// The height from the baseline to the top
        /// of characters (discounting ascent)
        /// </summary>
        public abstract double CapHeight { get; }

        /// <summary>
        /// Height from baseline down to lowest point
        /// </summary>
        public abstract double Ascent { get; }

        /// <summary>
        /// Height from baseline down to lowest point
        /// </summary>
        public abstract double Descent { get; }

        /// <summary>
        /// The underline's position relative to the baseline
        /// </summary>
        public abstract double UnderlinePosition { get; }

        /// <summary>
        /// The thickness of the underline
        /// </summary>
        public abstract double UnderlineThickness { get; }

        #endregion

        #region Init

        #endregion

        /// <summary>
        /// Gets the kerning of two glyphs
        /// </summary>
        /// <param name="from_gid">Left hand glyph</param>
        /// <param name="to_gid">Right hand glyph</param>
        /// <returns>Their kerning</returns>
        public abstract double GetKerning(int from_gid, int to_gid);

        /// <summary>
        /// Gets the width of a glyph
        /// </summary>
        /// <param name="gid">Glyph id</param>
        /// <returns>Width</returns>
        public abstract double GetWidth(int gid);

        /// <summary>
        /// Returns a chGlyph struct
        /// </summary>
        public abstract chGlyph GetGlyphInfo(int gid);
    }
}
