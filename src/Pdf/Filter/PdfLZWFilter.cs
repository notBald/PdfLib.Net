#define OPTIMIZED 
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Todo: This function does not support "FindEnd" needed for 100% support for inline images. 
    /// </summary>
    public sealed class PdfLZWFilter : PdfFilter
    {
        /// <summary>
        /// This filter's PDF name
        /// </summary>
        public override string Name { get { return "LZWDecode"; } }

        public override byte[] Decode(byte[] buf, PdfFilterParms fparams)
        {
#if OPTIMIZED
            var decode = new Util.LZW();
            var baos = new MemoryStream(buf.Length  * 4);
            decode.Decode(new MemoryStream(buf), fparams == null || !(fparams.Deref() is FlateParams fpa) || fpa.EarlyChange, baos);
#else
            var decode = new Util.RefLZW();
            var baos = new MemoryStream(buf.Length * 4);
            decode.Decode(buf, fparams == null || !(fparams.Deref() is FlateParams fpa) || fpa.EarlyChange, baos);
#endif

            if (fparams != null)
            {
                var fp = (FlateParams)fparams.Deref();
                var pred = fp.Predictor;
                if (pred > Predictor.None)
                {
                    baos.Position = 0;
                    if (pred == Predictor.Tiff2)
                        //This predictor is pretty simple, basically PRED.Sub just
                        //that it works on pixel component values instead of bytes.
                        // So a 4BBC gray image must be handled differently than a 
                        // 8BPC RGB image.
                        return PNGPredictor.TiffPredicor(baos, fp.Colors, fp.BitsPerComponent, fp.Columns);

                    //According to 7.4.4.4 all PNG predicors are to be treated
                    //the same way.
                    if (pred <= Predictor.PNG_Opt)
                        return PNGPredictor.Recon(baos, fp.Colors, fp.BitsPerComponent, fp.Columns);

                    System.Diagnostics.Debug.Assert(false);
                    throw new NotImplementedException("Predicor: " + pred);
                }
            }

            return baos.ToArray();
        }

        public override bool Equals(PdfFilter filter) { return filter is PdfLZWFilter; }

        public override string ToString()
        {
            return "/LZWDecode";
        }
    }
}
