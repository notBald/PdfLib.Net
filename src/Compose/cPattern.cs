using PdfLib.Pdf.ColorSpace;
using PdfLib.Compile;

namespace PdfLib.Compose
{
    /// <summary>
    /// Compose Color
    /// 
    /// Convenience class that encapsulates every color variation, including color space, that
    /// PDF supports. 
    /// </summary>
    public sealed class cPattern : cBrush
    {
        #region Init

        public cPattern(double[] color, CompiledPattern pat)
            : base(new PdfLib.Render.CSPattern())
        {
            _color = color;
            ((PdfLib.Render.CSPattern)MyColorSpace).CPat = pat;
            _pattern = null;
        }
        public cPattern(PdfPattern pat)
            : base(PatternCS.Instance)
        {
            _color = null;
            _pattern = pat;
        }
        public cPattern(double[] color, PdfPattern pat)
            : base(PatternCS.Instance)
        {
            _color = color;
            _pattern = pat;
        }

        #endregion
    }
}
