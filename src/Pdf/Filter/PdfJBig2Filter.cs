using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using JBig2;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// http://jbig2dec.sourceforge.net/
    /// http://www.cl.cam.ac.uk/~mgk25/jbigkit/
    /// </summary>
    [PdfVersion(PdfVersion.V14)]
    public sealed class PdfJBig2Filter : PdfFilter
    {
        #region Variables and properties

        public override string Name { get { return "JBIG2Decode"; } }

        #endregion

        #region Init

        #endregion

        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
            var dec = new JBIG2Decoder();
            if (fparams != null)
                dec.setGlobalData(((JBig2Params)fparams).JBIG2Globals);
            dec.decodeJBIG2(data);

            if (dec.NumberOfPages > 0)
            {
                var page = dec.GetPage(0);
                return page.getData(true);
            }

            throw new PdfFilterException(PdfType.Stream, ErrCode.CorruptToken);
        }

        #region Other overrides

        public override bool Equals(PdfFilter filter) { return filter is PdfJBig2Filter; }

        public override string ToString()
        {
            return "/JBIG2Decode";
        }

        #endregion
    }
}
