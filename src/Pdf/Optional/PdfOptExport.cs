using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptExport : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.ExportDictionary; } }

        public string ExportState { get { return _elems.GetName("ExportState"); } }

        #endregion

        #region Init

        internal PdfOptExport(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptExport(elems);
        }

        #endregion
    }
}
