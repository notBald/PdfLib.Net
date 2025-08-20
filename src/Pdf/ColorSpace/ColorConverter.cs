using PdfLib.Pdf.ColorSpace;
namespace PdfLib.Pdf.ColorSpace
{
    public abstract class ColorConverter
    {
        /// <summary>
        /// Creates a color from raw byte values
        /// </summary>
        /// <remarks>
        /// For implementors:
        /// 1 byte equal 1 component. If a color space 
        /// expects a different range, then the byte is 
        /// to be scaled into the range the color space 
        /// expects.
        /// </remarks>
        public virtual PdfColor MakeColor(byte[] raw)
        {
            var d = new double[raw.Length];
            for (int c = 0; c < d.Length; c++)
                d[c] = raw[c] / 255d;
            return MakeColor(d);
        }

        /// <summary>
        /// Makes a grayscaled color
        /// </summary>
        public virtual GrayColor MakeGrayColor(double[] comps)
        {
            return DeviceGray.GrayScaleColor(MakeColor(comps));
        }

        /// <summary>
        /// Create color from an array of floats.
        /// </summary>
        public abstract DblColor MakeColor(double[] comps);

        /// <summary>
        /// Create double colors from a PdfColor
        /// </summary>
        public abstract double[] MakeColor(PdfColor col);
    }
}
