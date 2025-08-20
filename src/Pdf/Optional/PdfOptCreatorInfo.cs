using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptCreatorInfo : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.CreatorInfo; } }

        public string Subtype { get { return _elems.GetName("Subtype"); } }

        public string Creator { get { return _elems.GetUnicodeString("Creator"); } }

        #endregion

        #region Init

        internal PdfOptCreatorInfo(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptCreatorInfo(elems);
        }

        #endregion
    }
}
