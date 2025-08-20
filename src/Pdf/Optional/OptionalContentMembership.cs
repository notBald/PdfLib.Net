using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class OptionalContentMembership : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.OptionalContentMembership; } }

        public OptionalContentGroup[] OCGs
        {
            get
            {
                var ocg = _elems["OCGs"];
                if (ocg == null)
                    return null;
                var ocg_d = ocg.Deref();
                if (ocg_d is OptionalContentGroup) 
                    return new OptionalContentGroup[] { (OptionalContentGroup)ocg };
                if (ocg_d is PdfDictionary)
                    return new OptionalContentGroup[] { (OptionalContentGroup) _elems.GetPdfType("OCGs", PdfType.OptionalContentGroup) };
                if (ocg_d is PdfArray)
                {
                    var ar = (PdfArray) ocg_d;
                    var ocgs = new OptionalContentGroup[ar.Length];
                    for (int c = 0; c < ocgs.Length; c++)
                        ocgs[c] = (OptionalContentGroup) ar.GetPdfType(c, PdfType.OptionalContentGroup, IntMsg.NoMessage, null);
                    return ocgs;
                }
                //Todo: Logg error: Ignored unexpected value
                return null;
            }
        }

        public string P 
        { 
            get 
            { 
                var p = _elems.GetName("P");
                if (p == null) return "AnyOn";
                return p;
            } 
        }

        [PdfVersion("1.6")]
        public PdfArray VE
        {
            get
            {
                return _elems.GetArray("VE");
            }
        }

        #endregion

        #region Init

        internal OptionalContentMembership(PdfDictionary dict)
            : base(dict)
        { dict.CheckTypeEx("OCMD"); }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new OptionalContentMembership(elems);
        }

        #endregion
    }
}
