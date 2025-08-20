using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfCircleAnnot : PdfGeometricAnnot
    {
        #region Properties

        /// <summary>
        /// Border style
        /// </summary>
        public PdfDictionary BS { get { return _elems.GetDictionary("BS"); } }

        /// <summary>
        /// In essence the padding. I.e. Left, Top, Right, and Bottom padding.
        /// </summary>
        [PdfVersion("1.5")]
        public PdfRectangle RD
        {
            get { return (PdfRectangle)_elems.GetPdfType("RD", PdfType.Rectangle); }
            set { _elems.SetItem("RD", value, false); }
        }

        #endregion

        #region Init

        internal PdfCircleAnnot(PdfDictionary dict)
            : base(dict)
        { }

        public PdfCircleAnnot(xRect bounds)
            : this(new PdfRectangle(bounds))
        { }

        public PdfCircleAnnot(PdfRectangle bounds)
            : base("Circle",bounds)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfCircleAnnot(elems);
        }

        #endregion
    }
}
