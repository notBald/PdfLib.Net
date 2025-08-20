using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptPageElement : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.PageElementDictionary; } }

        public string Subtype { get { return _elems.GetNameEx("Subtype"); } }

        #endregion

        #region Init

        internal PdfOptPageElement(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptPageElement(elems);
        }

        #endregion
    }
}
