using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    /// <summary>
    /// Unkown annotations are wrapped into this class and ignored.
    /// </summary>
    internal sealed class PdfUnkownAnnot : PdfMarkupAnnotation
    {
        #region Init

        internal PdfUnkownAnnot(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfUnkownAnnot(elems);
        }

        #endregion
    }
}
