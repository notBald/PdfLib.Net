using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Transparency
{
    public sealed class PdfAlphaMask : PdfSoftMask
    {
        #region Init

        internal PdfAlphaMask(PdfDictionary dict)
            : base(dict)
        {

        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfAlphaMask(elems);
        }

        #endregion
    }
}
