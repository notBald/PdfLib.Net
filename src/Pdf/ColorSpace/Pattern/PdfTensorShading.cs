using PdfLib.Pdf.Function;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using System.Collections.Generic;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    public sealed class PdfTensorShading : PdfPatchShading
    {
        #region Variables and properties

        #endregion

        #region Init

        internal PdfTensorShading(IWStream stream)
            : base(stream) { }

        private PdfTensorShading(PdfDictionary dict, IStream stream)
            : base(dict, stream) { }

        public PdfTensorShading(PdfTensor[] tensors, int bits_per_coordinate)
            : this(tensors, bits_per_coordinate, DeviceRGB.Instance)
        {   }

        public PdfTensorShading(IEnumerable<PdfCoonsPatch> tensors, int bits_per_coordinate, IColorSpace cs, 
            PdfFunctionArray functions = null, int bits_per_component = 8)
            : base(tensors, cs, functions, bits_per_coordinate, bits_per_component, 7)
        {  }

        #endregion

        #region Required overrides

        protected override PdfStreamShading MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfTensorShading(dict, stream);
        }

        #endregion
    }

    /// <summary>

    /// </summary>
    /// <remarks>
    /// Uses the nomaclure of the PDF3200_2008 spec,
    /// page 199.
    /// 
    /// P03, P13, P23, P33,
    /// P02, P12, P22, P32,
    /// P01, P11, P21, P31,
    /// P00, P10, P20, P30;
    /// </remarks>
    public sealed class PdfTensor : PdfCoonsPatch
    {
        public override xPoint P11 => _coords[1, 1];
        public override xPoint P12 => _coords[1, 2];
        public override xPoint P22 => _coords[2, 2];
        public override xPoint P21 => _coords[2, 1];

        internal PdfTensor(xPoint[] points, int ncomps)
            : base(ncomps)
        {
            int w = 0;

            //P00 -> P03
            for (; w < 4; w++)
                _coords[0, w] = points[w];

            //P13 -> P33
            for (int c=1; c < 4; c++)
                _coords[c, 3] = points[w++];

            //P32 -> P30
            for (int c = 2; c >= 0; c--)
                _coords[3, c] = points[w++];

            //P20
            _coords[2, 0] = points[w++];

            //P10 -> P12
            for (int c = 0; c < 3; c++)
                _coords[1, c] = points[w++];

            //P22
            _coords[2, 2] = points[w++];

            //P21
            _coords[2, 1] = points[w];
        }

        internal PdfTensor(xPoint[,] points, double[] c00, double[] c03, double[] c33, double[] c30)
            : base(points, c00, c03, c33, c30)
        {  }
    }

    /// </summary>
    /// <remarks>
    /// Uses the nomaclure of the PDF3200_2008 spec,
    /// page 199.
    /// 
    /// P03, P13, P23, P33,
    /// P02, P12, P22, P32,
    /// P01, P11, P21, P31,
    /// P00, P10, P20, P30;
    /// </remarks>
    public sealed class PdfTensorF : PdfCoonsPatchF
    {
        public override xPointF P11 => _coords[1, 1];
        public override xPointF P12 => _coords[1, 2];
        public override xPointF P22 => _coords[2, 2];
        public override xPointF P21 => _coords[2, 1];

        internal PdfTensorF(xPointF[] points, int ncomps)
            : base(ncomps)
        {
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

            //P10 -> P12
            for (int c = 0; c < 3; c++)
                _coords[1, c] = points[w++];

            //P22
            _coords[2, 2] = points[w++];

            //P21
            _coords[2, 1] = points[w];
        }

        internal PdfTensorF(xPointF[,] points, double[] c00, double[] c03, double[] c33, double[] c30)
            : base(points, c00, c03, c33, c30)
        { }
    }
}