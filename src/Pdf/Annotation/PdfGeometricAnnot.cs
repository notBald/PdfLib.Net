using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Annotation
{
    public abstract class PdfGeometricAnnot : PdfMarkupAnnotation
    {
        #region Properties

        /// <summary>
        /// Interior color
        /// </summary>
        [PdfVersion("1.4")]
        public double[] IC
        {
            get
            {
                var r = (RealArray)_elems.GetPdfType("IC", PdfType.RealArray);
                if (r == null) return null;
                if (r.Length == 2 || r.Length > 4)
                    throw new PdfReadException(PdfType.ColorSpace, ErrCode.Invalid);
                return r.ToArray();
            }
            set
            {
                if (value != null)
                {
                    if (value.Length > 4 || value.Length == 2)
                        throw new PdfNotSupportedException("Can't have two or more than four values");
                    foreach (var v in value)
                        if (v < 0 || v > 1)
                            throw new PdfOutOfRangeException("Value was outside supported range 0 - 1");
                    _elems.SetItem("IC", new RealArray(value), false);
                }
                else
                    _elems.Remove("IC");
            }
        }

        /// <summary>
        /// This is the same property as "C", just using a color object instead
        /// </summary>
        public PdfColor IC_Color
        {
            get
            {
                var col = IC;
                if (col == null || col.Length == 0) return null;
                if (col.Length == 1)
                    return DeviceGray.Instance.Converter.MakeColor(col);
                if (col.Length == 3)
                    return DeviceRGB.Instance.Converter.MakeColor(col);
                if (col.Length == 4)
                    return DeviceCMYK.Instance.Converter.MakeColor(col);
                return null;
            }
            set
            {
                if (value == null)
                    IC = null;
                {
                    if (value.Blue == value.Red && value.Blue == value.Green)
                        IC = new double[] { value.Blue };
                    else
                        IC = new double[] { value.Red, value.Green, value.Blue };
                }
            }
        }

        /// <summary>
        /// Border Effect
        /// </summary>
        /// <remarks>Defined for, but has no meaning for PolyLines</remarks>
        [PdfVersion("1.5")]
        public PdfBorderEffect BE
        {
            get { return (PdfBorderEffect)_elems.GetPdfType("BE", PdfType.BorderEffect); }
            set { _elems.SetItem("BE", value, true); }
        }

        #endregion

        #region Init

        internal PdfGeometricAnnot(PdfDictionary dict)
            : base(dict)
        { }

        /// <summary>
        /// Constructor for creating new annotations
        /// </summary>
        /// <param name="dict"></param>
        /// <param name="subtype"></param>
        /// <param name="bounds"></param>
        internal PdfGeometricAnnot(string subtype, PdfRectangle bounds)
            : base(subtype, bounds)
        { }

        #endregion
    }
}
