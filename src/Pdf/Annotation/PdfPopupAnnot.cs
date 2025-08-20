using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfPopupAnnot : PdfMarkupAnnotation
    {
        #region Init

        internal PdfPopupAnnot(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfPopupAnnot(elems);
        }

        #endregion
    }
}
