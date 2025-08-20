using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfCaretAnnot : PdfMarkupAnnotation
    {
        #region Init

        internal PdfCaretAnnot(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfCaretAnnot(elems);
        }

        #endregion
    }
}
