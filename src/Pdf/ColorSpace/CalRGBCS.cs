#define WHITE_POINT
//This alternate algorithm was posted on a web forum. Perhaps it's more correct than mine, though from my
//tests it produce worse results.
//#define ALT_ALGORITHM
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    //http://graphics.stanford.edu/courses/cs178/applets/gamutmapping.html
    //http://www.cambridgeincolour.com/tutorials/color-space-conversion.htm
    //
    // WhitePoint is ignored when RI is uh, check the specs :)
    //
    // Idea: 
    //  1. Change XYZ -> xyY
    //  2. Substract whitepoint? 
    //  3. Change xyY -> XYZ
    [PdfVersion("1.1")]
    public sealed class CalRGBCS : ItemArray, IColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// Cached pointer to the dictionary in _items[1]
        /// </summary>
        readonly PdfDictionary _elems;

        /// <summary>
        /// PdfType.ColorSpace
        /// </summary>
        internal override PdfType Type { get { return PdfType.ColorSpace; } }

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public PdfCSType CSType { get { return PdfCSType.CIE; } }

        public PdfColor DefaultColor { get { return Converter.MakeColor(new double[] { 0, 0, 0 }); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 0, 0, 0 }; } }

        public int NComponents { get { return 3; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode { get { return new double[] { 0, 1, 0, 1, 0, 1 }; } }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public ColorConverter Converter { get { return new CalRGBConverter(WhitePoint, Gamma, Matrix); } }

        public LabPoint WhitePoint
        {
            get
            {
                var ret = ((RealArray)_elems.GetPdfTypeEx("WhitePoint", PdfType.RealArray));
                if (ret.Length != 3) throw new PdfInternalException("Corrupt token");
                double y = ret[1];
                if (y != 1) throw new PdfInternalException("Corrupt token");
                return new LabPoint(ret[0], y, ret[2]);
            }
            set
            {
                if (value == null)
                    throw new PdfNotSupportedException("White point is required, can't be set null");
                _elems.SetItem("WhitePoint", new RealArray(value.X, 1, value.Y), false);
            }
        }

        public LabPoint BlackPoint
        {
            get
            {
                var ret = (RealArray)_elems.GetPdfType("BlackPoint", PdfType.RealArray);
                if (ret == null) return new LabPoint(0, 0, 0);
                if (ret.Length != 3) throw new PdfInternalException("Corrupt token");
                return new LabPoint(ret[0], ret[1], ret[2]);
            }
        }

        public LabPoint Gamma
        {
            get
            {
                var ret = (RealArray)_elems.GetPdfType("Gamma", PdfType.RealArray);
                if (ret == null) return new LabPoint(1, 1, 1);
                if (ret.Length != 3) throw new PdfInternalException("Corrupt token");
                return new LabPoint(ret[0], ret[1], ret[2]);
            }
            set
            {
                if (value == null)
                    _elems.Remove("Gamma");
                else
                    _elems.SetItem("Gamma", new RealArray(value.X, value.Y, value.Y), false);
            }
        }

        public x3x3Matrix Matrix
        {
            get
            {
                var ret = (RealArray)_elems.GetPdfType("Matrix", PdfType.RealArray);
                if (ret == null) return x3x3Matrix.Identity;
                return new x3x3Matrix(ret);
            }
            set
            {
                if (value.IsIdentity)
                    _elems.Remove("Matrix");
                else
                    _elems.SetItem("Matrix", value.ToArray(), false);
            }
        }

        #endregion

        #region Init

        public CalRGBCS(LabPoint white_point, LabPoint gamma, x3x3Matrix? matrix)
            : base(new TemporaryArray(2))
        {
            _items.SetNewItem(0, new PdfName("CalRGB"), false);
            _elems = new TemporaryDictionary();
            _items.SetNewItem(1, _elems, false);

            if (white_point == null) throw new PdfNotSupportedException("Whitepoint is required");
            WhitePoint = white_point;
            if (gamma != null) Gamma = gamma;
            if (matrix != null)
                Matrix = matrix.Value;
        }

        public CalRGBCS(double gamma)
            : this(new LabPoint(1, 1, 1), new LabPoint(gamma, gamma, gamma), null)
        { }

        internal CalRGBCS(PdfArray ar)
            : base(ar)
        {
            var dict = ar[1].Deref();
            if (!(dict is PdfDictionary)) throw new PdfInternalException("corrupt object");
            _elems = (PdfDictionary)dict;
        }

        #endregion

        #region Required overrides

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            return Equivalent(cs);
        }

        protected override ItemArray MakeCopy(PdfArray data, ResTracker tracker)
        {
            return new CalRGBCS(data);
        }

        #endregion

        sealed class CalRGBConverter : ColorConverter
        {
#if WHITE_POINT
            readonly LabPoint _white;
#endif
            readonly LabPoint _gamma;
            readonly x3x3Matrix _m;

            /// <summary>
            /// Used for gamut correction
            /// </summary>
            /// <remarks>
            /// Gamut correction, menionened lightly is in 10.2.
            /// </remarks>
            double _r, _g, _b;

            public CalRGBConverter(LabPoint white, LabPoint Gamma, x3x3Matrix Matrix)
            {
                _gamma = Gamma; // new LabPoint(1 / Gamma.X, 1 / Gamma.Y, 1 / Gamma.Z);
                _m = Matrix;
#if WHITE_POINT
                _white = white;
#endif
                _r = (3.2404542 - 1.5371385 - 0.4985314);
                _g = (-0.9692660 + 1.8760108 + 0.0415560);
                _b = (0.0556434 - 0.2040259 + 1.0572252);
            }

            public override PdfColor MakeColor(byte[] raw)
            {
                Debug.Assert(raw.Length == 3);
                return new IntColor(raw[0], raw[1], raw[2]);
            }

            public override DblColor MakeColor(double[] comps)
            {
                var A = Math.Pow(comps[0], _gamma.X);
                var B = Math.Pow(comps[1], _gamma.Y);
                var C = Math.Pow(comps[2], _gamma.Z);

                var X = _m.M11 * A + _m.M21 * B + _m.M31 * C;
                var Y = _m.M12 * A + _m.M22 * B + _m.M32 * C;
                var Z = _m.M13 * A + _m.M23 * B + _m.M33 * C;


#if ALT_ALGORITHM
                //Von Kries Matrix
                double rho = (0.4002400) * X + (0.7076000) * Y + (-0.0808100) * Z;
                double gamma = (-0.2263000) * X + (1.1653200) * Y + (0.0457000) * Z;
                double beta = (0) * X + (0) * Y + (0.9182200) * Z;

                /*Destination White Point is D65:
                    X: 0.95047 Y: 1.0 Z: 1.08883

                    Source White Point is from ColorSpace dictionary.
                */
                rho *= .95047 / _white.X;
                gamma *= 1 / _white.Y;
                beta *= 1.08883 / _white.Z;

                //Inverse Von Kries Matrix
                X = (1.8599364) * rho + (-1.1293816) * gamma + (0.2198974) * beta;
                Y = (0.3611914) * rho + (0.6388125) * gamma + (-0.0000064) * beta;
                Z = (0) * rho + (0) * gamma + (1.0890636) * beta;
#else
#if WHITE_POINT
                #region Test code
                //http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html
                //double[,] VonKries = {  { 0.40024, -0.22630, 0.00000 } ,
                //                         { 0.70760,  1.16532, 0.00000 } ,
                //                         { -0.08081,  0.04570, 0.91822 }};
                //double[,] VonKriesN = { { 1.859936,  0.361191, 0.000000 },
                //                         {-1.129382,  0.638812, 0.000000 }, 
                //                         { 0.219897, -0.000006, 1.089064 }};
                //double[,] Bradford = {  {  0.8951,  0.2664, -0.1614 } ,
                //                         { -0.7502,  1.7135,  0.0367 } ,
                //                         {  0.0389, -0.0685,  1.0296 }};
                //double[,] BradfordN = { {  0.9869929, -0.1470543, 0.1599627 },
                //                         {  0.4323053,  0.5183603, 0.0492912 }, 
                //                         { -0.0085287,  0.0400428, 0.9684867 }};
                //double[,] sRGB_D65 = {  { 3.2404542, -1.5371385, -0.4985314 } ,
                //                         { -0.9692660,  1.8760108, 0.0415560 } ,
                //                         { 0.0556434,  -0.2040259, 1.0572252 }};
                //double[,] src_white = { { _white.X, _white.Y, _white.Z } };
                //double[,] dst_white = { { 0.9505, 1, 1.089 } };

                //src_white = MatrixLibrary.Matrix.Multiply(src_white, VonKries);
                //dst_white = MatrixLibrary.Matrix.Multiply(dst_white, vonKriesN);

                //var scale = new double[,] { { dst_white[0,0] / src_white[0,0], 0, 0 },
                //                            { 0, dst_white[0,1] / src_white[0,1], 0 },
                //                            { 0, 0, dst_white[0,2] / src_white[0,2] }};
                //scale = MatrixLibrary.Matrix.Multiply(VonKries, scale);
                //scale = MatrixLibrary.Matrix.Multiply(scale, VonKriesN);

                //Using Bradford
                //sRGB_D65 = MatrixLibrary.Matrix.Inverse(sRGB_D65);
                //double[,] d65_white = { { 0.95047, 1, 1.08883 } };
                ////double[,] d50_white = { { 0.96422, 1, 0.82521 } };
                //double[,] source_white = { { _white.X, _white.Y, _white.Z } };
                //double[,] xyz = { { X, Y, Z } };
                //
                //d65_white = MatrixLibrary.Matrix.Multiply(d65_white, Bradford);
                //source_white = MatrixLibrary.Matrix.Multiply(source_white, Bradford);
                //
                //var scale = new double[,] { { d65_white[0,0] / source_white[0,0], 0, 0 },
                //                            { 0, d65_white[0,1] / source_white[0,1], 0 },
                //                            { 0, 0, d65_white[0,2] / source_white[0,2] }};
                //
                //var mul = MatrixLibrary.Matrix.Multiply(xyz, Bradford);
                //var scaled = MatrixLibrary.Matrix.Multiply(mul, scale);
                //xyz = MatrixLibrary.Matrix.Multiply(scaled, BradfordN);
                //
                //X = xyz[0, 0];
                //Y = xyz[0, 1];
                //Z = xyz[0, 2];

                //Specs say that the whitepoint is to be used as input for the 
                //"CIE-based gamut mapping function", without elaborating further
                //
                //I've experimented a bit, but this simple function got the most
                //consistent "close enough" results. One thing I've not tried is
                //to change the whitepoint of the D65 sRGB Matrix, so try making a file
                //with a D50 whitespace and use the D50 sRGB matrix*. 
                //
                // sRGB (D50 - X=0.9642, Y=1.00, Z=0.8249)
                //  3.1338561 -1.6168667 -0.4906146
                // -0.9787684  1.9161415  0.0334540 
                //  0.0719453 -0.2289914  1.4052427
                //
                // * Almost tried, but my eyes just can't spot the difference of D50 and D65

                //Converting XYZ to xyY
                //
                // sRGB's color coordniates:
                //         (     x,      y,        Y) 
                //   Red = (0.6400, 0.3300, 0.212656)
                // Green = (0.3000, 0.6000, 0.715158)
                //  Blue = (0.1500, 0.0600, 0.072186)
                //
                // Basic idea is to convert to xyY, then
                // fit inside the sRGB's coordinates depending
                // on the current rendering intent
                //var XYZ = X + Y + Z;
                //if (XYZ == 0)
                //{
                //    //set to crominants?
                //}
                //else
                //{
                //    var x = X / XYZ;
                //    var y = Y / XYZ;
                //    //Y equals Y

                //}
                #endregion

                //If I'm thinking correctly, the white point is the values needed to
                //get the color white. By dividinng on the white point, the values are
                //moved into our 1-1-1 coordinate space. Say, if x = .5, then one need
                //the value x = 0.5 to be white, i.e. it needs to be 1. .5 / .5 = 1
                X /= _white.X;
                Y /= _white.Y;
                Z /= _white.Z;
#endif
#endif

                //Converts from XYZ (D65) to sRGB:
                //sRGB (D65). I've tried various matrixes, and this seems to be
                //what adobe make use of.
                double r = X * 3.2404542 - Y * 1.5371385 - Z * 0.4985314;
                double g = -X * 0.9692660 + Y * 1.8760108 + Z * 0.0415560;
                double b = X * 0.0556434 - Y * 0.2040259 + Z * 1.0572252;

#if ALT_ALGORITHM
                r = sRGBTransferFunction(r);
                g = sRGBTransferFunction(g);
                b = sRGBTransferFunction(b);

                return new DblColor(r, g, b);
#else
                //Quick gamut correction. 
                r /= _r;
                g /= _g;
                b /= _b;

                //Clips to 0, 1
                if (r < 0) r = 0;
                else if (r > 1) r = 1;
                if (g < 0) g = 0;
                else if (g > 1) g = 1;
                if (b < 0) b = 0;
                else if (b > 1) b = 1;

                //Adjusts gamma. sRGB has a gamma of ~2.2, but a gamma
                //of 2 seems to give "good enough" result.
                return new DblColor(Math.Sqrt(r),
                                    Math.Sqrt(g),
                                    Math.Sqrt(b));

                //const double Gamma = 1d / 2.2;
                //return new DblColor(Math.Pow(r, Gamma),
                //        Math.Pow(g, Gamma),
                //        Math.Pow(b, Gamma));
#endif
            }

            private double sRGBTransferFunction(double rgb_SColor)
            {
                double rgbColor;

                //Color component transfer function:
                if (rgb_SColor <= 0.0031308)
                {
                    rgbColor = rgb_SColor * 12.92;
                }
                else
                {
                    rgbColor = (float)(1.055 * (Math.Pow(rgb_SColor, 1.0 / 2.4)) - 0.055);
                }

                //ensure result is bounded from 0 to 1
                if (rgbColor > 1)
                {
                    rgbColor = 1;
                }

                if (rgbColor < 0)
                {
                    rgbColor = 0;
                }

                return rgbColor;
            }

            public override double[] MakeColor(PdfColor col)
            {
                return new double[] { col.Red, col.Green, col.Blue };
            }
        }
    }
}