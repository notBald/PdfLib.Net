//In my testing, white point is ignored by adobe (on a 16-bit grayscale image)
//TO_SPECS is interesting, but less accurate compared with Adobe. It looks like
//         the most accurate result is non-spec with a gamma correction of 2.2.
//         It is within +- 4 of whatever Adobe is doing.
//#define TO_SPECS
#if !TO_SPECS
#define GAMMA
#else
//#define WHITE_POINT
#define GAMMA
//#define SRGB
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    [PdfVersion("1.1")]
    public sealed class CalGrayCS : ItemArray, IColorSpace
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

        public PdfColor DefaultColor { get { return Converter.MakeColor(new double[] { 0 }); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 0 }; } }

        public int NComponents { get { return 1; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode { get { return new double[] { 0, 1 }; } }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public ColorConverter Converter { get { return new CalGrayConverter(WhitePoint, Gamma); } }

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

        public double Gamma
        {
            get
            {
                return _elems.GetReal("Gamma", 1);
            }
            set
            {
                if (value == 1)
                    _elems.Remove("Gamma");
                else
                    _elems.SetReal("Gamma", value);
            }
        }

        #endregion

        #region Init

        public CalGrayCS()
            : this(1) { }

        public CalGrayCS(double gamma)
            : this(new LabPoint(1, 1, 1), gamma)
        {
        }

        public CalGrayCS(LabPoint white_point, double gamma)
            : base(new TemporaryArray(2))
        {
            _items.SetNewItem(0, new PdfName("CalGray"), false);
            _elems = new TemporaryDictionary();
            _items.SetNewItem(1, _elems, false);

            if (white_point == null) throw new PdfNotSupportedException("Whitepoint is required");
            WhitePoint = white_point;
            Gamma = gamma;
        }

        internal CalGrayCS(PdfArray ar)
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
        /// <remarks>All inheritors are single instance classes</remarks>
        public bool Equals(IColorSpace cs)
        {
            return Equivalent(cs);
        }

        protected override ItemArray MakeCopy(PdfArray data, ResTracker tracker)
        {
            return new CalGrayCS(data);
        }

        #endregion

        sealed class CalGrayConverter : ColorConverter
        {
#if WHITE_POINT
            readonly LabPoint _white;
#endif

#if GAMMA
            readonly double _gamma;
#endif

            public CalGrayConverter(LabPoint white, double Gamma)
            {
#if GAMMA
                _gamma = Gamma;
#endif

#if WHITE_POINT
                _white = white;
#endif
            }

            public override PdfColor MakeColor(byte[] raw)
            {
                Debug.Assert(raw.Length == 1);
                return new IntColor(raw[0]);
            }

            public override DblColor MakeColor(double[] comps)
            {
#if TO_SPECS
                var AG = Math.Pow(comps[0], _gamma);
#if WHITE_POINT
                var L = AG * _white.X;
#else
                var L = AG;
#endif
                double col = Math.Max(1.16 * Math.Pow(L, 1d / 3) - .16, 0);
                return new DblColor(col, col, col);
#else
#if GAMMA
                var X = Math.Pow(comps[0], _gamma);
#else
                var X = comps[0];
#endif
#if SRGB || WHITE_POINT
                var Y = X;
                var Z = X;
#endif

#if WHITE_POINT
                Y = X * _white.Y;
                Z = X * _white.Z;
                X *= _white.X;
#endif


#if SRGB
                //sRGB (D65)
                double r = X * 3.2404542 - Y * 1.5371385 - Z * 0.4985314;
                double g = -X * 0.9692660 + Y * 1.8760108 + Z * 0.0415560;
                double b = X * 0.0556434 - Y * 0.2040259 + Z * 1.0572252;

                //Gamut correction. 
                r /= (3.2404542 - 1.5371385 - 0.4985314);
                g /= (-0.9692660 + 1.8760108 + 0.0415560);
                b /= (0.0556434 - 0.2040259 + 1.0572252);

                //Clips to 0, 1
                if (r < 0) r = 0;
                else if (r > 1) r = 1;
                if (g < 0) g = 0;
                else if (g > 1) g = 1;
                if (b < 0) b = 0;
                else if (b > 1) b = 1;

                //Adjusts gamma
                return new DblColor(Math.Sqrt(r),
                                    Math.Sqrt(g),
                                    Math.Sqrt(b));
#else

#if GAMMA && WHITE_POINT
                const double Gamma = 1d / 2.2;
                return new DblColor(Math.Pow(X, Gamma),
                        Math.Pow(Y, Gamma),
                        Math.Pow(Z, Gamma));
#else
#if WHITE_POINT
                return new DblColor(X, Y, Z);
#else
#if GAMMA
                const double Gamma = 1d / 2.2;
                X = Math.Pow(X, Gamma);
#endif
                return new DblColor(X, X, X);
#endif
#endif
#endif
#endif
            }

            public override double[] MakeColor(PdfColor col)
            {
                return new double[] { 0.3 * col.Red + 0.59 * col.Green + 0.11 * col.Blue };
            }
        }
    }
}
