using PdfLib.Pdf.Function;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using System.Collections.Generic;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    public sealed class PdfCoonsShading : PdfPatchShading
    {
        #region Variables and properties

        public PdfCoonsPatch[] Patches
        {
            get
            {
                var list = new List<PdfCoonsPatch>(50);
                new TensorReader(this, _stream.DecodedStream).Read((points, ncomps) =>
                {
                    var patch = new PdfCoonsPatch(points, ncomps);
                    list.Add(patch);
                    return patch;
                }, false);

                return list.ToArray();
            }
        }

        #endregion

        #region Init

        internal PdfCoonsShading(IWStream stream)
            : base(stream) { }

        private PdfCoonsShading(PdfDictionary dict, IStream stream)
            : base(dict, stream) { }

        public PdfCoonsShading(PdfCoonsPatch[] coons, int bits_per_coordinate, IColorSpace cs, PdfFunctionArray functions = null, int bits_per_component = 8)
            : this((IEnumerable<PdfCoonsPatch>) coons, bits_per_coordinate, cs, functions, bits_per_component)
        { }

        public PdfCoonsShading(PdfTensor[] tensors, int bits_per_coordinate, IColorSpace cs, PdfFunctionArray functions = null, int bits_per_component = 8)
            : this((IEnumerable<PdfCoonsPatch>)tensors, bits_per_coordinate, cs, functions, bits_per_component)
        { }

        private PdfCoonsShading(IEnumerable<PdfCoonsPatch> coons, int bits_per_coordinate, IColorSpace cs, PdfFunctionArray functions, int bits_per_component)
            : base(coons, cs, functions, bits_per_coordinate, bits_per_component, 6)
        {  }

        #endregion

        #region Required overrides

        protected override PdfStreamShading MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfCoonsShading(dict, stream);
        }

        #endregion
    }

    /// <summary>
    /// A coons patch has four cournes, each with its own color.
    /// 
    /// There's also twelve coordinates, for the four Bêzier curves
    /// </summary>
    /// <remarks>
    /// Uses the nomaclure of the PDF3200_2008 spec,
    /// page 199.
    /// 
    /// P03, P13, P23, P33,
    /// P02,           P32,
    /// P01,           P31,
    /// P00, P10, P20, P30;
    /// </remarks>
    public class PdfCoonsPatch
    {
        protected readonly xPoint[,] _coords;
        internal xPoint[,] Coords => _coords;

        internal xRect Bounds
        {
            get
            {
                double x_min = double.MaxValue, x_max = double.MinValue,
                       y_min = double.MaxValue, y_max = double.MinValue;

                for (int c = 0; c < 4; c++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var v = _coords[c, k];
                        if (v.X < x_min)
                            x_min = v.X;
                        if (v.X > x_max)
                            x_max = v.X;
                        if (v.Y < y_min)
                            y_min = v.Y;
                        if (v.Y > y_max)
                            y_max = v.Y;
                    }
                }

                return new xRect(x_min, y_min, x_max, y_max);
            }
        }

        public xPoint P00 => _coords[0, 0];
        public xPoint P01 => _coords[0, 1];
        public xPoint P02 => _coords[0, 2];
        public xPoint P03 => _coords[0, 3];
        public xPoint P13 => _coords[1, 3];
        public xPoint P23 => _coords[2, 3];
        public xPoint P33 => _coords[3, 3];
        public xPoint P32 => _coords[3, 2];
        public xPoint P31 => _coords[3, 1];
        public xPoint P30 => _coords[3, 0];
        public xPoint P20 => _coords[2, 0];
        public xPoint P10 => _coords[1, 0];
        public virtual xPoint P11 => new xPoint(
                    calc_tensor(P00.X, P01.X, P10.X, P03.X, P30.X, P31.X, P13.X, P33.X),
                    calc_tensor(P00.Y, P01.Y, P10.Y, P03.Y, P30.Y, P31.Y, P13.Y, P33.Y)
                );
        public virtual xPoint P12 => new xPoint(
                    calc_tensor(P03.X, P02.X, P13.X, P00.X, P33.X, P32.X, P10.X, P30.X),
                    calc_tensor(P03.Y, P02.Y, P13.Y, P00.Y, P33.Y, P32.Y, P10.Y, P30.Y)
                );
        public virtual xPoint P22 => new xPoint(
                    calc_tensor(P33.X, P32.X, P23.X, P30.X, P03.X, P02.X, P20.X, P00.X),
                    calc_tensor(P33.Y, P32.Y, P23.Y, P30.Y, P03.Y, P02.Y, P20.Y, P00.Y)
                );
        public virtual xPoint P21 => new xPoint(
                    calc_tensor(P30.X, P31.X, P20.X, P33.X, P00.X, P01.X, P23.X, P03.X),
                    calc_tensor(P30.Y, P31.Y, P20.Y, P33.Y, P00.Y, P01.Y, P23.Y, P03.Y)
                );
        public virtual PdfTensor Tensor
        {
            get
            {
                //Page 200. PDF3200_2008
                _coords[1, 1] = P11;
                _coords[1, 2] = P12;
                _coords[2, 2] = P22;
                _coords[2, 1] = P21;

                return new PdfTensor(_coords, Color_ll, Color_ul, Color_ur, Color_lr);
            }
        }

        private double calc_tensor(double p1, double p2, double p3, double p4, double p5, double p6, double p7, double p8)
        {
            return (-4 * p1 + 6 * (p2 + p3) - 2 * (p4 + p5) + 3 * (p6 + p7) - p8) / 9;
        }

        /// <summary>
        /// Color lower left corner
        /// </summary>
        public readonly double[] Color_ll;

        /// <summary>
        /// Color upper right corner
        /// </summary>
        public readonly double[] Color_ur;

        /// <summary>
        /// Color upper left corner
        /// </summary>
        public readonly double[] Color_ul;

        /// <summary>
        /// Color lower right corner
        /// </summary>
        public readonly double[] Color_lr;

        protected PdfCoonsPatch(int ncomps)
        {
            _coords = new xPoint[4, 4];
            Color_ll = new double[ncomps];
            Color_ur = new double[ncomps];
            Color_ul = new double[ncomps];
            Color_lr = new double[ncomps];
        }

        protected PdfCoonsPatch(xPoint[,] points, double[] c00, double[] c03, double[] c33, double[] c30)
        {
            _coords = points;
            Color_ll = c00;
            Color_ur = c33;
            Color_ul = c03;
            Color_lr = c30;
        }

        internal PdfCoonsPatch(xPoint[] points, int ncomps)
        {
            _coords = new xPoint[4, 4];
            int w = 0;

            //P00 -> P03
            for (; w < 4; w++)
                _coords[0, w] = points[w];

            //P13 -> P33
            for (int c = 1; c < 4; c++)
                _coords[c, 3] = points[w++];

            //P32 -> P30
            for (int c = 2; c >= 0; c--)
                _coords[3, c] = points[w++];

            //P20
            _coords[2, 0] = points[w++];

            //P10
            _coords[1, 0] = points[w];

            Color_ll = new double[ncomps];
            Color_ur = new double[ncomps];
            Color_ul = new double[ncomps];
            Color_lr = new double[ncomps];
        }
    }

    /// <summary>
    /// A coons patch has four cournes, each with its own color.
    /// 
    /// There's also twelve coordinates, for the four Bêzier curves
    /// </summary>
    /// <remarks>
    /// Uses the nomaclure of the PDF3200_2008 spec,
    /// page 199.
    /// 
    /// P03, P13, P23, P33,
    /// P02,           P32,
    /// P01,           P31,
    /// P00, P10, P20, P30;
    /// </remarks>
    public class PdfCoonsPatchF
    {
        protected readonly xPointF[,] _coords;
        internal xPointF[,] Coords => _coords;

        internal xRectF Bounds
        {
            get
            {
                float x_min = float.MaxValue, x_max = float.MinValue,
                       y_min = float.MaxValue, y_max = float.MinValue;

                for (int c = 0; c < 4; c++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var v = _coords[c, k];
                        if (v.X < x_min)
                            x_min = v.X;
                        if (v.X > x_max)
                            x_max = v.X;
                        if (v.Y < y_min)
                            y_min = v.Y;
                        if (v.Y > y_max)
                            y_max = v.Y;
                    }
                }

                return new xRectF(x_min, y_min, x_max, y_max);
            }
        }

        public xPointF P00 => _coords[0, 0];
        public xPointF P01 => _coords[0, 1];
        public xPointF P02 => _coords[0, 2];
        public xPointF P03 => _coords[0, 3];
        public xPointF P13 => _coords[1, 3];
        public xPointF P23 => _coords[2, 3];
        public xPointF P33 => _coords[3, 3];
        public xPointF P32 => _coords[3, 2];
        public xPointF P31 => _coords[3, 1];
        public xPointF P30 => _coords[3, 0];
        public xPointF P20 => _coords[2, 0];
        public xPointF P10 => _coords[1, 0];
        public virtual xPointF P11 => new xPointF(
                    calc_tensor(P00.X, P01.X, P10.X, P03.X, P30.X, P31.X, P13.X, P33.X),
                    calc_tensor(P00.Y, P01.Y, P10.Y, P03.Y, P30.Y, P31.Y, P13.Y, P33.Y)
                );
        public virtual xPointF P12 => new xPointF(
                    calc_tensor(P03.X, P02.X, P13.X, P00.X, P33.X, P32.X, P10.X, P30.X),
                    calc_tensor(P03.Y, P02.Y, P13.Y, P00.Y, P33.Y, P32.Y, P10.Y, P30.Y)
                );
        public virtual xPointF P22 => new xPointF(
                    calc_tensor(P33.X, P32.X, P23.X, P30.X, P03.X, P02.X, P20.X, P00.X),
                    calc_tensor(P33.Y, P32.Y, P23.Y, P30.Y, P03.Y, P02.Y, P20.Y, P00.Y)
                );
        public virtual xPointF P21 => new xPointF(
                    calc_tensor(P30.X, P31.X, P20.X, P33.X, P00.X, P01.X, P23.X, P03.X),
                    calc_tensor(P30.Y, P31.Y, P20.Y, P33.Y, P00.Y, P01.Y, P23.Y, P03.Y)
                );
        public virtual PdfTensorF Tensor
        {
            get
            {
                //Page 200. PDF3200_2008
                _coords[1, 1] = P11;
                _coords[1, 2] = P12;
                _coords[2, 2] = P22;
                _coords[2, 1] = P21;

                return new PdfTensorF(_coords, Color_ll, Color_ul, Color_ur, Color_lr);
            }
        }

        private float calc_tensor(float p1, float p2, float p3, float p4, float p5, float p6, float p7, float p8)
        {
            return (-4 * p1 + 6 * (p2 + p3) - 2 * (p4 + p5) + 3 * (p6 + p7) - p8) / 9;
        }

        /// <summary>
        /// Color lower left corner
        /// </summary>
        public readonly double[] Color_ll;

        /// <summary>
        /// Color upper right corner
        /// </summary>
        public readonly double[] Color_ur;

        /// <summary>
        /// Color upper left corner
        /// </summary>
        public readonly double[] Color_ul;

        /// <summary>
        /// Color lower right corner
        /// </summary>
        public readonly double[] Color_lr;

        protected PdfCoonsPatchF(int ncomps)
        {
            _coords = new xPointF[4, 4];
            Color_ll = new double[ncomps];
            Color_ur = new double[ncomps];
            Color_ul = new double[ncomps];
            Color_lr = new double[ncomps];
        }

        protected PdfCoonsPatchF(xPointF[,] points, double[] c00, double[] c03, double[] c33, double[] c30)
        {
            _coords = points;
            Color_ll = c00;
            Color_ur = c33;
            Color_ul = c03;
            Color_lr = c30;
        }

        internal PdfCoonsPatchF(xPointF[] points, int ncomps)
        {
            _coords = new xPointF[4, 4];
            int w = 0;

            //P00 -> P03
            for (; w < 4; w++)
                _coords[0, w] = points[w];

            //P13 -> P33
            for (int c = 1; c < 4; c++)
                _coords[c, 3] = points[w++];

            //P32 -> P30
            for (int c = 2; c >= 0; c--)
                _coords[3, c] = points[w++];

            //P20
            _coords[2, 0] = points[w++];

            //P10
            _coords[1, 0] = points[w];

            Color_ll = new double[ncomps];
            Color_ur = new double[ncomps];
            Color_ul = new double[ncomps];
            Color_lr = new double[ncomps];
        }
    }
}
