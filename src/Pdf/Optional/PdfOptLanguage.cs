using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptLanguage : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.LanguageDictionary; } }

        public string Lang { get { return _elems.GetUnicodeStringEx("Lang"); } }

        public string Preferred { get { return _elems.GetName("Preferred"); } }

        #endregion

        #region Init

        internal PdfOptLanguage(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptLanguage(elems);
        }

        #endregion
    }
}
