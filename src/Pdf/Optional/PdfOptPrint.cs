using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptPrint : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.PrintDictionary; } }

        public string Subtype { get { return _elems.GetName("Subtype"); } }

        public string PrintState { get { return _elems.GetName("PrintState"); } }

        #endregion

        #region Init

        internal PdfOptPrint(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptPrint(elems);
        }

        #endregion
    }
}
