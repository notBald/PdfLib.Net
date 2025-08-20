using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Filter
{
    internal sealed class JBig2Params : PdfFilterParms
    {
        #region Variables and properties

        public byte[] JBIG2Globals
        {
            get
            {
                var s = _elems.GetPdfTypeEx("JBIG2Globals", PdfType.Dictionary) as IStream;
                if (s == null) throw new PdfFilterException(PdfType.Stream, ErrCode.WrongType);
                return s.DecodedStream;
            }
        }

        #endregion

        #region Init

        internal JBig2Params(PdfDictionary dict)
            : base(dict)
        {

        }

        #endregion
        
        #region Boilerplate code

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new JBig2Params(elems);
        }

        #endregion
    }
}
