using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptView : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.ViewDictionary; } }

        public string ViewState { get { return _elems.GetName("ViewState"); } }

        #endregion

        #region Init

        internal PdfOptView(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptView(elems);
        }

        #endregion
    }
}
