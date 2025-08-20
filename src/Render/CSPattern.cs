using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Compile;

namespace PdfLib.Render
{
    /// <summary>
    /// Used to wrap a pattern color spaces 
    /// in the renderer. 
    /// </summary>
    public class CSPattern : IColorSpace
    {
        #region Variables and properties

        public PdfShadingPattern Pat;
        public CompiledPattern CPat;
        public IColorSpace CS;

        public PdfCSType CSType { get { return PdfCSType.Special; } }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public int NComponents { get { return 1; } }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        /// <remarks>Patterns has to be rendered</remarks>
        public ColorConverter Converter { get { throw new NotSupportedException(); } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode { get { throw new PdfNotSupportedException(); } }

        /// <summary>
        /// Default color for patterns are "nothing"
        /// </summary>
        public PdfColor DefaultColor { get { return null; } }

        /// <summary>
        /// Default color for patterns are "nothing"
        /// </summary>
        public double[] DefaultColorAr { get { return null; } }

        #endregion

        #region Init

        public CSPattern() {  }
        public CSPattern(IColorSpace cs) { CS = cs; }

        #endregion

        #region IColorSpace

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            return false;
        }

        #endregion
    }
}
