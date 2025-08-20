#define OpenJpeg
using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
#if OpenJpeg
using OpenJpeg;
#else
using OpenJpeg2;
#endif

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Jpeg2000 filter
    /// </summary>
    [PdfVersion("1.5")]
    public sealed class PdfJPXFilter : PdfFilter
    {
        #region Variables and properties

        public override string Name { get { return "JPXDecode"; } }

        #endregion

        #region Init

        #endregion

        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
            //Detects the format
            CodecFormat format = data[0] == 0 ? CodecFormat.Jpeg2P : CodecFormat.Jpeg2K;

            //Sets up decoding event management
            var cinfo = new CompressionInfo(true, format);
            cinfo.EventManager = new EventMgr(Error, null, null);

            //Sets up decoding parameters. Can for instance be used to
            //speed up decoding of thumbnails by decoding less resolutions
            var parameters = new DecompressionParameters();

            //Destination for the decoded image
            JPXImage img = null;

            using (var ms = new MemoryStream(data, false))
            {
                //cio is a wrapper that is used by the libary when
                //reading. A bit like "BinaryReader"
                var cio = cinfo.OpenCIO(ms, true);
                cinfo.SetupDecoder(cio, parameters);

                //Decodes the image
                if (!cinfo.ReadHeader(out img) || !cinfo.Decode(img) || !cinfo.EndDecompress())
                    throw new PdfFilterException(ErrCode.General);

                //Presumably a Error notification will arrive before this
                if (img == null)
                    throw new PdfFilterException(ErrCode.General);
            }

            //Assembles the image into a stream of bytes
            return img.ToArray();

            //This is a quick cheat to get some result. Jpeg2k files are too different
            //for the current "slowimagedecode" to be able to handle them. Therefore
            //J2K files should/will be handled by a sepperate decoding path. 
            /*using (var ms = new MemoryStream())
            {
                //Assumes RGB24. Will not work on non-RGB24 J2K images. 
                img.ConvertToBMP(ms, cinfo);
                var bmp = Util.IMGTools.OpenBMP(ms);
                bmp = Util.IMGTools.ChangePixelFormat(bmp, img.NumberOfComponents == 1 ? System.Windows.Media.PixelFormats.Gray8 : System.Windows.Media.PixelFormats.Rgb24);
                return Util.IMGTools.BMStoBA(bmp);
            }*/
        }

        static void Error(string msg, object o)
        {
            throw new PdfFilterException(ErrCode.Wrong, msg);
        }

        #region required overrides

        public override bool Equals(PdfFilter filter) { return filter is PdfJPXFilter; }

        public override string ToString()
        {
            return "/JPXDecode";
        }

        #endregion
    }
}
