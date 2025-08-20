//My whitepoint math is wrong. Check out "There are 9 ways to Rome.pdf" on this and Adobe 7
//#define WHITE_POINT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// Implements the L*a*b* color space. Tries to stay as close to
    /// Adobe X as feasible.
    /// </summary>
    [PdfVersion("1.1")]
    public sealed class LabCS : ItemArray, IColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// Cached copy of the dictionary in _items[1]
        /// </summary>
        readonly PdfDictionary _elems;

        /// <summary>
        /// PdfType.ColorSpace
        /// </summary>
        internal override PdfType Type { get { return PdfType.ColorSpace; } }

        public PdfCSType CSType { get { return PdfCSType.CIE; } }

        /// <summary>
        /// The initial color for this colorspace
        /// </summary>
        /// <remarks>Will be clipped to range by the converter</remarks>
        public PdfColor DefaultColor { get { return Converter.MakeColor(new double[] {0, 0, 0}); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 0, 0, 0 }; } }

        public int NComponents { get { return 3; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode
        {
            get 
            {
                var r = Range;
                return new double[] { 0, 100, r.a.Start, r.a.End, r.a.Start, r.a.End }; 
            }
        }

        /// <summary>
        /// Valid ranges for a* and b*.
        /// </summary>
        public LabRange Range
        {
            get
            {
                var ret = (IntArray)_elems.GetPdfType("Range", PdfType.IntArray);
                if (ret == null) return new LabRange(-100, 100, -100, 100);
                if (ret.Length != 4) throw new PdfInternalException("Corrupt token");
                return new LabRange(ret[0], ret[1], ret[2], ret[3]);
            }
            set
            {
                if (value != null)
                {
                    var ia = value.ToIntArray();
                    if (ia[0] == -100 && ia[1] == 100 && ia[2] == -100 && ia[3] == 100)
                        _elems.Remove("Range");
                    else
                        _elems.SetItem("Range", new IntArray(ia), false);
                }
                else _elems.Remove("Range");
            }
        }

        /// <summary>
        /// The diffuse white point;
        /// </summary>
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
        }

        /// <summary>
        /// Tristimulus value of the diffuse black point
        /// </summary>
        public LabPoint BlackPoint
        {
            get
            {
                var ret = (RealArray)_elems.GetPdfType("BlackPoint", PdfType.RealArray);
                if (ret == null) return new LabPoint(0, 0, 0);
                if (ret.Length != 3) throw new PdfInternalException("Corrupt token");
                return new LabPoint(ret[0], ret[1], ret[2]);
            }
            set
            {
                if (value == null || value.X == 0 && value.Y == 0 && value.Z == 0)
                    _elems.Remove("BlackPoint");
                else
                {
                    _elems.SetItem("BlackPoint", new RealArray(value.ToArray()), false);
                }
            }
        }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public ColorConverter Converter { get { return ToRGB; } }

        /// <summary>
        /// Gets a converter to convert a lab image into a rgb image.
        /// </summary>
        public LabToRGBConverter ToRGB { get { return new LabToRGBConverter(Range, WhitePoint/*, BlackPoint*/); } }

        #endregion

        #region Init

        public LabCS() : this(0.9505, 1.0890) { }

        public LabCS(double Xw, double Zw)
            : base(new TemporaryArray(2))
        {
            _items[0] = new PdfName("Lab");
            _elems = new TemporaryDictionary();
            _elems.SetNewItem("WhitePoint", new RealArray(Xw, 1, Zw), false);
            _items[1] = _elems;
        }

        internal LabCS(PdfArray ar)
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

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new LabCS(array);
        }

        #endregion
    }

    /// <summary>
    /// Converts from L*a*b*24 color space to RGB24
    /// </summary>
    /// <remarks>
    /// Adobe Reader does not seem to make use for White/Black. Have no
    /// effect on screen rendering in any case.
    /// </remarks>
    public sealed class LabToRGBConverter : ColorConverter
    {
        readonly LabRange _range;
#if WHITE_POINT
        readonly LabPoint _white;
#endif
        //readonly LabPoint _black;

        /// <summary>
        /// Interpolation factors.
        /// 
        /// Used to scale the bytes into a doubles.
        /// </summary>
        double _ai, _bi;

        internal LabToRGBConverter(LabRange range, LabPoint white /*, LabPoint black*/)
        {
            _range = range;
#if WHITE_POINT
            _white = white;
            //_black = black;

            //The WhitePoint and BlackPoint entries in the colour space dictionary shall 
            //control the overall effect of the CIE-based gamut mapping function described 
            //in sub-clause 10.2, "CIE-Based Colour to Device Colour".
            //
            //Though AFAICT Adobe X ignores the WhitePoint (and BlackPoint) values.
            // (Though I've only tested this on images, scratch that - works the same 
            //  way on non images)
            //
            //Well then. I'm a little stuck. Perhaps one must set rendering intent?
#else
            //In any case, 10.2 does not describe the "gamut mapping function", just mentions
            //that it's complex, outside the scope of the specs and that a gamut is the subset
            //of all posible colors in the colorspace.
            //
            //Not very helpful
            //
            //But it appears this gamut mapping function is:
            // - "divide on sum of D65 transform"
            // - Clip to range (to avoide negative values when gamma correcting)
            // - Correct gamma (with a gamma of 2)
#endif
            //Interpolation factors (for scaling into range).
            // (THIS IS WRONG, REMOVE?)
            _ai = Math.Abs(_range.a.End - _range.a.Start) / 255d;
            _bi = Math.Abs(_range.b.End - _range.b.Start) / 255d;
            //L is always from 0 to 100
        }

        public override PdfColor MakeColor(byte[] raw)
        {
            return MakeColor(Convert(raw));
        }

        /// <summary>
        /// Converts LAB into RGB.
        /// </summary>
        public override DblColor MakeColor(double[] raw)
        {
            Debug.Assert(raw.Length == 3);

            //Todo: Investigate what happens if Lab has a range and if image has a range.
            //      In the current impl. the image's range will overrule the Lab's range.
            double l = raw[0]; //Range is usualy 0 to 100.
            double a = raw[1]; //Range ia usualy -100 to 100
            double b = raw[2]; //Range ia usualy -100 to 100
             
            //Clips to range
            if (a < _range.a.Start) a = _range.a.Start;
            else if (a > _range.a.End) a = _range.a.End;
            if (b < _range.b.Start) b = _range.b.Start;
            else if (b > _range.b.End) b = _range.b.End;

            //Precomputes some values needed by the XYZ conversion
            double M = (l + 16) / 116d;
            double L = M + (a / 500d);
            double N = M - (b / 200d);

            //To XYZ. Formula given in the specs, 8.6.5.4
#if WHITE_POINT
            var X = _white.X * ((L >= 6d / 29) ? L * L * L : 108d / 841 * (L - 4d / 29));
            var Y = _white.Y * ((M >= 6d / 29) ? M * M * M : 108d / 841 * (M - 4d / 29));
            var Z = _white.Z * ((N >= 6d / 29) ? N * N * N : 108d / 841 * (N - 4d / 29));
#else
            var X = ((L >= 6d / 29) ? L * L * L : 108d / 841 * (L - 4d / 29));
            var Y = ((M >= 6d / 29) ? M * M * M : 108d / 841 * (M - 4d / 29));
            var Z = ((N >= 6d / 29) ? N * N * N : 108d / 841 * (N - 4d / 29));
#endif

            //Converts to sRGB (D65 white point)
            double R = X * 3.2404542 - Y * 1.5371385 - Z * 0.4985314;
            double G = -X * 0.9692660 + Y * 1.8760108 + Z * 0.0415560;
            double B = X * 0.0556434 - Y * 0.2040259 + Z * 1.0572252;

            //Gamut correction. 
            R /= (3.2404542 - 1.5371385 - 0.4985314);
            G /= (-0.9692660 + 1.8760108 + 0.0415560);
            B /= (0.0556434 - 0.2040259 + 1.0572252);

            //Clips to 0, 1
            if (R < 0) R = 0;
            else if (R > 1) R = 1;
            if (G < 0) G = 0;
            else if (G > 1) G = 1;
            if (B < 0) B = 0;
            else if (B > 1) B = 1;

            //Using my own eyes and a crappy monitor it seems a
            //gamma of 2 gets close enough to whatever adobe puts
            //out.
            return new DblColor(Math.Sqrt(R),
                                Math.Sqrt(G),
                                Math.Sqrt(B));

            //const double Gamma = 1d / 2.1;
            //return new DblColor(Math.Pow(R, Gamma),
            //        Math.Pow(G, Gamma),
            //        Math.Pow(B, Gamma));
        }

        /// <summary>
        /// converts a byte array with lab values into a byte
        /// array with rgb values
        /// </summary>
        /// <see cref="http://www.codeproject.com/KB/recipes/colorspace1.aspx"/>
        public byte[] Convert(byte[] lab)
        {
            if (lab.Length % 3 != 0) throw new PdfInternalException("Corrupt data");

            for (int c = 0; c < lab.Length; c += 3)
            {
                //This code is wrong. Must clip, not scale to range
                // (^Todo: test if this truly is the case)
                //
                //Interpolates the values into their range. I.e. the values
                //are in the range 0-255, so they must be converted to the
                //correct equivalent value to what's given in the range
                //parameter. 
                double l = lab[c] * 100 / 255d; //Range is always 0 - 100.
                double a = lab[c + 1] * _ai + _range.a.Start;
                double b = lab[c + 2] * _bi + _range.b.Start;
                //^The math works like this:
                //     --------- <- Byte range from 0 to 255.
                //  ------       <- New range, potentially from -128 to 127
                // Calcs the ratio between the ranges by dividing the new
                // range on the byte range.
                //
                // Then the point is multiplied by this ratio and shifted
                // down (or up) to the origin/min point of the new range.

                /*if (c == 21000)
                {
                    var row = new byte[300];
                    Array.Copy(lab, c, row, 0, 300);
                    var text = Read.Lexer.GetString(Filter.PdfHexFilter.FormatEncode(row, new int[] { 3, 3, 3, 3, 3 }, true));
                }*/

                //Calcs values needed by the XYZ 
                double fy = (l + 16) / 116d;
                double fx = fy + (a / 500d);
                double fz = fy - (b / 200d);

                //To XYZ. Formula given in the specs, 8.6.5.4
#if WHITE_POINT
                var X = _white.X * ((fx >= 6d / 29) ? fx * fx * fx : 108d / 841 * (fx - 4d / 29));
                var Y = _white.Y * ((fy >= 6d / 29) ? fy * fy * fy : 108d / 841 * (fy - 4d / 29));
                var Z = _white.Z * ((fz >= 6d / 29) ? fz * fz * fz : 108d / 841 * (fz - 4d / 29));
#else
                var X = ((fx >= 6d / 29) ? fx * fx * fx : 108d / 841 * (fx - 4d / 29));
                var Y = ((fy >= 6d / 29) ? fy * fy * fy : 108d / 841 * (fy - 4d / 29));
                var Z = ((fz >= 6d / 29) ? fz * fz * fz : 108d / 841 * (fz - 4d / 29));
#endif

                #region Color space matrixes

                //RGB->XYZ
                /* NTSC (D65)
                 *  X   0.412411 0.357585 0.180454   R
                 *  Y = 0.212649 0.715169 0.072182 * G
                 *  Z   0.019332 0.119195 0.950390   B
                */

                /*MatrixLibrary.Matrix m = new MatrixLibrary.Matrix(3,3);
                m[0, 0] = 0.412411; m[0, 1] = 0.357585; m[0, 2] = 0.180454;
                m[1, 0] = 0.212649; m[1, 1] = 0.715169; m[1, 2] = 0.072182;
                m[2, 0] = 0.019332; m[2, 1] = 0.119195; m[2, 2] = 0.950390;

                var ni = MatrixLibrary.Matrix.Inverse(m);*/

                //http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html
                //http://www.scarse.org/docs/color_faq.html
                /* NTSC (C)
                 *  1.9099961 -0.5324542 -0.2882091
                 * -0.9846663  1.9991710 -0.0283082 
                 *  0.0583056 -0.1183781  0.8975535
                 * NTSC (D50)
                 *  1.8464881 -0.5521299 -0.2766458
                 * -0.9692412  2.0044755 -0.0690396 
                 *  0.0736477 -0.1453020  1.3018376
                 * NTSC (D65)
                 *  3.2408110 -1.5373100 -0.4985860
                 * -0.9826630  1.8759665  0.0415539 
                 *  0.0556375 -0.2040073  1.0571298
                /* CIE (E)
                 *  2.3706743 -0.9000405 -0.4706338
                 * -0.5138850  1.4253036  0.0885814 
                 *  0.0052982 -0.0146949  1.0093968
                 * CIE (D50)
                 *  2.3638081 -0.8676030 -0.4988161
                 * -0.5005940  1.3962369  0.1047562 
                 *  0.0141712 -0.0306400  1.2323842
                /* Adobe-1998 (D65)
                    2.0413690 -0.5649464 -0.3446944
                 * -0.9692660  1.8760108  0.0415560 
                 *  0.0134474 -0.1183897  1.0154096 
                 * Adobe-1998 (D50)
                 *  1.9624274 -0.6105343 -0.3413404
                 * -0.9787684  1.9161415  0.0334540 
                 *  0.0286869 -0.1406752  1.3487655
                 * sRGB (D50)
                 *  3.1338561 -1.6168667 -0.4906146
                 * -0.9787684  1.9161415  0.0334540 
                 *  0.0719453 -0.2289914  1.4052427
                /* sRGB (D65)
                    3.2404542 -1.5371385 -0.4985314
                 * -0.9692660  1.8760108  0.0415560 
                 *  0.0556434 -0.2040259  1.0572252
                 */

                #endregion

                //sRGB (D65)
                double R = X * 3.2404542 - Y * 1.5371385 - Z * 0.4985314;
                double G = -X * 0.9692660 + Y * 1.8760108 + Z * 0.0415560;
                double B = X * 0.0556434 - Y * 0.2040259 + Z * 1.0572252;

                R = R / (3.2404542 - 1.5371385 - 0.4985314);
                G = G / (-0.9692660 + 1.8760108 + 0.0415560);
                B = B / (0.0556434 - 0.2040259 + 1.0572252);
                if (R < 0) R = 0;
                else if (R > 1) R = 1;
                if (G < 0) G = 0;
                else if (G > 1) G = 1;
                if (B < 0) B = 0;
                else if (B > 1) B = 1;

                //Gamma corrects (using gamma set at 2, which is equal to sqrt)
                lab[c] = (byte)(Math.Sqrt(R) * 255);
                lab[c + 1] = (byte)(Math.Sqrt(G) * 255);
                lab[c + 2] = (byte)(Math.Sqrt(B) * 255);
            }

            return lab;
        }

        public override double[] MakeColor(PdfColor col)
        {
            var xyz = RGBtoXYZ(col.Red, col.Green, col.Blue);

            //the XYZ to L*a*b* formula
            //L* = 116 * F(Y/Yn) - 16
            //a* = 500 * (F(X/Xn) - F(Y/Yn))
            //b* = 200 * (F(Y/Yn) - F(Z/Zn)
            // Where Xn, Yn and Zn are CIE tristimulus reference 
            var fy = F(xyz[1]);
            return new double[] { 116.0 * fy - 16, 
#if WHITE_POINT
                //Just a quick experiment
                                  500.0 * (F(xyz[0] / _white.X) - fy), 
                                  200.0 * (fy - F(xyz[2] / _white.Z)) };
#else
                                  500.0 * (F(xyz[0]) - fy), 
                                  200.0 * (fy - F(xyz[2])) };
#endif
        }

        //XYZ to L*a*b* function
        // F = t^1/3 for t > 0.008856
        // F = 7.787*t + 16/116
        private static double F(double t)
        {
            if (t > 0.008856) return Math.Pow(t, 1 / 3d);
            return 7.787 * t + 16.0 / 116.0;
        }

        private double[] RGBtoXYZ(double red, double green, double blue)
        {
            //sRGB->XYZ
            /* NTSC (D65)
             *  X   0.412411 0.357585 0.180454   R
             *  Y = 0.212649 0.715169 0.072182 * G
             *  Z   0.019332 0.119195 0.950390   B
            */

            //RGB to gRGB
            double r = ToGRGB(red);
            double g = ToGRGB(green);
            double b = ToGRGB(blue);

            return new double[]
            {
                r * 0.412411 + g * 0.357585 + b * 0.180454,
                r * 0.212649 + g * 0.715169 + b * 0.072182,
                r * 0.019332 + g * 0.119195 + b * 0.950390
            };
        }

        //http://en.wikipedia.org/wiki/SRGB
        private double ToSRGB(double c)
        {
            if (c <= 0.04045) return c / 12.92;
            return Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        //RGB to GRGB function (a = 0.055 and l = 2.2)
        // F = (c + a) / (1 + a)^l for t > 0.04045
        // F = c / 12.92
        private double ToGRGB(double c)
        {
            if (c <= 0.04045) return c / 12.92;
            return Math.Pow((c + 0.055) / 1.055, 2.2);
        }

    }

    public class LabRange
    {
        public readonly iRange a, b;
        public LabRange(int from_a, int to_a, int from_b, int to_b)
        { a = new iRange(from_a, to_a); b = new iRange(from_b, to_b); }
        public override string ToString()
        {
            return string.Format("a: {0} | b: {1}", a, b);
        }
        public int ARange(int A) { return (sbyte)((A < a.Start) ? a.Start : ((A < a.End) ? A : a.End)); }
        public int BRange(int B) { return (sbyte)((B < b.Start) ? b.Start : ((B < b.End) ? B : b.End)); }
        public static double LRange(double l) { return (sbyte)((l > 100) ? 100 : (l < 0) ? 0 : l); }
        internal int[] ToIntArray()
        { return new int[] { a.Start, a.End, b.Start, b.End }; }
    }

    public class LabPoint
    {
        public readonly double X, Y, Z;
        public LabPoint(double x, double y, double z)
        {
            if (x < 0 || y < 0 || z < 0)
                throw new PdfInternalException("Corrupt token");
            X = x; Y = y; Z = z;
        }
        internal double[] ToArray() { return new double[] { X, Y, Z }; }
        public override string ToString()
        {
            return string.Format("XYZ: ({0})  ({1})  ({2})", X, Y, Z);
        }
    }
}
