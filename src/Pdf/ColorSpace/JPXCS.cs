using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;


namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// For JPX files in the CMY color space
    /// </summary>
    /// <remarks>Simple wrapper around CMYK</remarks>
    internal class JPXCMY : IColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public PdfCSType CSType { get { return PdfCSType.Device; } }

        /// <summary>
        /// Color that will be set when this colorspace is selected
        /// </summary>
        public PdfColor DefaultColor { get { return new IntColor(0, 0, 0); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 0, 0, 0 }; } }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public int NComponents { get { return 3; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode
        {
            get { return new double[] { 0, 1, 0, 1, 0, 1 }; }
        }

        public ColorConverter Converter
        {
            get { return new CMYConverter(); }
        }

        #endregion

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            return cs is JPXCMY;
        }

        internal sealed class CMYConverter : ColorConverter
        {
            readonly ColorConverter _cmyk;
            readonly double[] CMYK = new double[4];

            public CMYConverter() 
            {
                _cmyk = DeviceCMYK.Instance.Converter;
            }

            public override GrayColor MakeGrayColor(double[] comps)
            {
                return DeviceGray.GrayScaleColor(MakeColor(comps));
            }

            public override DblColor MakeColor(double[] comps)
            {
                //Extracts "black" out of CMY
                double k = Math.Min(comps[0], comps[1]);
                k = Math.Min(k, comps[2]);

                //converts to CMYK
                CMYK[3] = k;
                if (k >= 1)
                    k = 0.99999;
                CMYK[0] = Clip((comps[0] - k) / (1 - k));
                CMYK[1] = Clip((comps[1] - k) / (1 - k));
                CMYK[2] = Clip((comps[2] - k) / (1 - k));

                //Then converts to RGB. 
                return _cmyk.MakeColor(CMYK);
            }

            private double Clip(double val) { return val < 0 ? 0 : val > 1 ? 1 : val; }

            public override double[] MakeColor(PdfColor col)
            {
                var cmy = new double[3];
                cmy[0] = 1 - col.Red;
                cmy[1] = 1 - col.Green;
                cmy[2] = 1 - col.Blue;
                return cmy;
            }
        }
    }

    /// <summary>
    /// For JPX files in the YCC color space
    /// </summary>
    /// <remarks>Quick and dirty</remarks>
    internal class JPXYCC : IColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public PdfCSType CSType { get { return PdfCSType.Device; } }

        /// <summary>
        /// Color that will be set when this colorspace is selected
        /// </summary>
        public PdfColor DefaultColor { get { return new IntColor(0, 0, 0); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 0, 0, 0 }; } }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public int NComponents { get { return 3; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode
        {
            get { return new double[] { 0, 1, 0, 1, 0, 1 }; }
        }

        public ColorConverter Converter
        {
            get { return new YCCConverter(); }
        }

        #endregion

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            return cs is JPXYCC;
        }

        internal sealed class YCCConverter : ColorConverter
        {
            public override DblColor MakeColor(double[] comps)
            {
                //Formula taken from wikipedia (YCbCr article - JPEG conversion)
                double Y = comps[0], Cb = comps[1], Cr = comps[2];

                return new DblColor(
                    Y + 1.402 * (Cr - 0.5),
                    Y - 0.34414 * (Cb - 0.5) - 0.71414 * (Cr - 0.5),
                    Y + 1.772 * (Cb - 0.5));
            }

            public override double[] MakeColor(PdfColor col)
            {
                //Formula taken from wikipedia (YCbCr article - JPEG conversion)
                var cmy = new double[3];
                cmy[0] = 0.299 * col.Red + 0.857 * col.Green + 0.114 * col.Blue;
                cmy[1] = 0.5 - 0.168736 * col.Red - 0.331264 * col.Green + 0.5 * col.Blue;
                cmy[2] = 0.5 + 0.5 * col.Red - 0.418688 * col.Green - 0.081312 * col.Blue;
                return cmy;
            }
        }
    }

    /// <summary>
    /// For JPX files in the YCCK color space
    /// </summary>
    /// <remarks>Quick and dirty</remarks>
    internal class JPXYCCK : IColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public PdfCSType CSType { get { return PdfCSType.Device; } }

        /// <summary>
        /// Color that will be set when this colorspace is selected
        /// </summary>
        public PdfColor DefaultColor { get { return new IntColor(0, 0, 0); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 0, 0, 0, 0 }; } }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public int NComponents { get { return 4; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode
        {
            get { return new double[] { 0, 1, 0, 1, 0, 1, 0, 1 }; }
        }

        public ColorConverter Converter
        {
            get { return new YCCKConverter(); }
        }

        #endregion

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            return cs is JPXYCCK;
        }

        internal sealed class YCCKConverter : ColorConverter
        {
            readonly ColorConverter _cmyk;
            readonly double[] CMYK = new double[4];

            public YCCKConverter() 
            {
                _cmyk = DeviceCMYK.Instance.Converter;
            }

            public override DblColor MakeColor(double[] comps)
            {
                //Formula taken from wikipedia (YCbCr article - JPEG conversion)
                double Y = comps[0], Cb = comps[1], Cr = comps[2];

                CMYK[0] = 1 - (Y + 1.402 * (Cr - 0.5));
                CMYK[1] = 1 - (Y - 0.34414 * (Cb - 0.5) - 0.71414 * (Cr - 0.5));
                CMYK[2] = 1 - (Y + 1.772 * (Cb - 0.5));
                CMYK[3] = comps[3];
                return _cmyk.MakeColor(CMYK);
            }

            public override double[] MakeColor(PdfColor col)
            {
                //Formula taken from wikipedia (YCbCr article - JPEG conversion)
                var cmyk = new double[4];
                cmyk[0] = 1 - (0.299 * col.Red + 0.857 * col.Green + 0.114 * col.Blue);
                cmyk[1] = 1 - (0.5 - 0.168736 * col.Red - 0.331264 * col.Green + 0.5 * col.Blue);
                cmyk[2] = 1 - (0.5 + 0.5 * col.Red - 0.418688 * col.Green - 0.081312 * col.Blue);
                double k = Math.Min(cmyk[0], cmyk[1]);
                k = Math.Min(k, cmyk[2]);
                cmyk[3] = k;
                cmyk[0] -= k;
                cmyk[1] -= k;
                cmyk[2] -= k;
                return cmyk;
            }
        }
    }

    /// <summary>
    /// For JPX files in the Lab color space
    /// </summary>
    /// <remarks>
    /// Intended for experimenting without breaking the PDF side.
    /// 
    /// Uses a differen Lab conversion algo, not derivered from the PDF
    /// specs. It's useing ITU-T encoding with D50 whitepoint then a to "sRGB D65"
    /// conversion (valeus found in "Color Imaging on the Internet.pdf"). This 
    /// gives a result very similar to Kakadu on my single test image.
    /// </remarks>
    internal class JPXLAB : IColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public PdfCSType CSType { get { return PdfCSType.Device; } }

        /// <summary>
        /// Color that will be set when this colorspace is selected
        /// </summary>
        public PdfColor DefaultColor { get { return new IntColor(0, 0, 0); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 0, 0, 0 }; } }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public int NComponents { get { return 3; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode
        {
            get { return new double[] { 0, 100, -85, 85, -75, 120 }; }
        }

        public ColorConverter Converter
        {
            get { return new LABConverter(); }
        }

        #endregion

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            return cs is JPXLAB;
        }

        internal sealed class LABConverter : ColorConverter
        {
            public override DblColor MakeColor(double[] comps)
            {
                double L = comps[0], a = comps[1], b = comps[2];

                //Converts Lab to XYZ
                double Y = L / 116d + 16d / 116;
                double X = Y + (a / 500d);
                double Z = Y - (b / 200d);

                //Perhaps try http://www.codeproject.com/Articles/19045/Manipulating-colors-in-NET-Part-1
                // which corrects for D65 white point new CIEXYZ(0.9505, 1.0, 1.0890);
                /*X = X > 6.0 / 29.0 ? X * X * X : X * (108.0 / 841.0) - 432.0 / 24389.0;
                Y = L > 8.0 ? Y * Y * Y : L * (27.0 / 24389.0);
                Z = Z > 6.0 / 29.0 ? Z * Z * Z : Z * (108.0 / 841.0) - 432.0 / 24389.0;//*/
                X = X > 6d / 29d ? X * X * X * 0.96422 : (X - 16d / 116) * 3 * (6d / 29 * 6d / 29) * 0.96422;
                Y = Y > 6d / 29d ? Y * Y * Y * 1       : (Y - 16d / 116) * 3 * (6d / 29 * 6d / 29) * 1;
                Z = Z > 6d / 29d ? Z * Z * Z * 0.82521 : (Z - 16d / 116) * 3 * (6d / 29 * 6d / 29) * 0.82521;//*/

                //Converts XYZ to RGB
                /*double R = X * (1219569.0 / 395920.0) + Y * (-608687.0 / 395920.0) + Z * (-107481.0 / 197960.0);
                double G = X * (-80960619.0 / 87888100.0) + Y * (82435961.0 / 43944050.0) + Z * (3976797.0 / 87888100.0);
                double B = X * (93813.0 / 1774030.0) + Y * (-180961.0 / 887015.0) + Z * (107481.0 / 93370.0);//*/
                //Converts to sRGB (D65 white point)
                double R = X * 3.2404542 - Y * 1.5371385 - Z * 0.4985314;
                double G = -X * 0.9692660 + Y * 1.8760108 + Z * 0.0415560;
                double B = X * 0.0556434 - Y * 0.2040259 + Z * 1.0572252;//*/

                // RGB to gamma-compressed RGB
                R = R > 0.0031308 ? Math.Pow(R, 1.0 / 2.4) * 1.055 - 0.055 : R * 12.92;
                G = G > 0.0031308 ? Math.Pow(G, 1.0 / 2.4) * 1.055 - 0.055 : G * 12.92;
                B = B > 0.0031308 ? Math.Pow(B, 1.0 / 2.4) * 1.055 - 0.055 : B * 12.92;

                return new DblColor(R, G, B);
            }

            public override double[] MakeColor(PdfColor col)
            {
                //Formula taken from wikipedia (YCbCr article - JPEG conversion)
                var cmy = new double[3];
                cmy[0] = 0.299 * col.Red + 0.857 * col.Green + 0.114 * col.Blue;
                cmy[1] = 0.5 - 0.168736 * col.Red - 0.331264 * col.Green + 0.5 * col.Blue;
                cmy[2] = 0.5 + 0.5 * col.Red - 0.418688 * col.Green - 0.081312 * col.Blue;
                return cmy;
            }
        }
    }
}
