//#define LibJPEGv9
#if LibJPEGv9
using BitMiracle.LibJpeg.Classic;
#else
using LibJpeg.Classic;
#endif
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Reflection;
using System.IO;
using System;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Decodes Jpeg images
    /// 
    /// This class depends on WPF
    /// </summary>
    public sealed class PdfDCTFilter : PdfFilter
    {
        readonly static bool HAS_LIBJPEG;

        /// <summary>
        /// This filter's PDF name
        /// </summary>
        public override string Name { get { return "DCTDecode"; } }

        static PdfDCTFilter() 
        { 
            //Checks if LibJpeg.net is present
            HAS_LIBJPEG = true;//File.Exists("BitMiracle.LibJpeg.NET.dll");
        }

        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
            int bits_per_pixel = GetBBP(data);
            if ((bits_per_pixel == 32 || fparams != null) && HAS_LIBJPEG)
            {
                //File.WriteAllBytes(@"c:\temp\test_cmyk.jpg", data);

                //Using LibJPEG on 32BPP files as .net won't give me the
                //original data for YCCK->CMYK conversion.
                var err = new jpeg_error_mgr();
                jpeg_decompress_struct cinfo = new jpeg_decompress_struct(err);
                cinfo.jpeg_stdio_src(new MemoryStream(data));
                if (cinfo.jpeg_read_header(true) != ReadResult.JPEG_HEADER_OK)
                    throw new PdfReadException(Internal.PdfType.Stream, ErrCode.IsCorrupt);

                if (fparams != null)
                {
                    //Specs say that jpegs with an Adobe marker should ignore the 
                    //ColorTransform entery if there's a transform set by that marker.
                    if (!cinfo.HasAdobeMarker && !cinfo.HasAdobeTransform)
                    {
                        if (cinfo.Num_components == 3)
                            cinfo.Jpeg_color_space = ((DCTParams)fparams).ColorTransform ? J_COLOR_SPACE.JCS_YCbCr : J_COLOR_SPACE.JCS_RGB;
                        else if (cinfo.Num_components == 4)
                            cinfo.Jpeg_color_space = ((DCTParams)fparams).ColorTransform ? J_COLOR_SPACE.JCS_YCCK : J_COLOR_SPACE.JCS_CMYK;
                    }
                }
                //Output color space is set to CMYK or RGB automatically by LibJpeg.

                //IIRC Data_precision must always be 8, so no need to check for padding.
                int stride = (cinfo.Data_precision * cinfo.Image_width * cinfo.Num_components + 7) / 8;
                byte[][] buf = new byte[][] { new byte[stride] };
                data = new byte[stride * cinfo.Image_height];

                cinfo.jpeg_start_decompress();

                //I suppose all scannlines can be read in one go, but
                //with a dual array buffer I won't bother.
                for (int c = 0; c < cinfo.Image_height; c++)
                {
                    cinfo.jpeg_read_scanlines(buf, 1);
                    Buffer.BlockCopy(buf[0], 0, data, stride * c, stride);
                }

                //Looks like LibJpeg handes the non-standar YCCK to CMYK conversion. Nice.
                return data;
            }
            else
            {
                //I assume this is faster than using LibJpeg.net
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = new System.IO.MemoryStream(data);
                bi.EndInit();
                BitmapSource bms = bi;
                //File.WriteAllBytes(@"c:\temp\test.jpg", data);

                //WPF always decodes to Bgra32, so guessing the correct
                //pixel format with a quick look at the jpeg header data.
                if (bits_per_pixel == 24)
                    bms = Img.IMGTools.ChangePixelFormat(bi, PixelFormats.Rgb24);
                //else if (bits_per_pixel == 32)
                //{
                //    //CMYK images are non-standar, 
                //    bms = Util.IMGTools.ChangePixelFormat(bi, PixelFormats.Cmyk32);
                //}
                else if (bits_per_pixel == 8)
                    bms = Img.IMGTools.ChangePixelFormat(bi, PixelFormats.Gray8);

                //Jpeg BPP must be a multiple of 8 (according to the PDF specs)
                int stride = bms.PixelWidth * bms.Format.BitsPerPixel / 8;
                data = new byte[bms.PixelHeight * stride];
                bms.CopyPixels(data, stride, 0);

                if (bits_per_pixel == 32)
                {
                    //CMYK images are/can be non standar. They're encoded in a YCCK
                    //format.
                    // http://software.intel.com/sites/products/documentation/hpc/ipp/ippi/ippi_ch15/functn_YCCKToCMYK_JPEG.html#functn_YCCKToCMYK_JPEG
                    // http://software.intel.com/sites/products/documentation/hpc/ipp/ippi/ippi_ch15/functn_CMYKToYCCK_JPEG.html#functn_CMYKToYCCK_JPEG
                    // http://www.jroller.com/greenhorn/entry/adobe_photoshop_and_jpeg_cmyk
                    // http://www.randelshofer.ch/blog/2008/10/jpeg-images-with-cmyk-and-ycck-image-data/
                    for (int c = 0; c < data.Length; c += 4)
                    {
                        #region YCCK to CMYK
                        //var Y1 = data[c + 0];
                        //var Cb = data[c + 1];
                        //var Cr = data[c + 2];

                        //This formula is correct for the "real" data
                        //var R = Y1 + 1.402 * Cr - 179.456;
                        //var G = Y1 - 0.34414 * Cb - 0.71414 * Cr + 135.4584;
                        //var B = Y1 + 1.772 * Cb - 226.816;

                        //byte C = (byte)(255 - R);
                        //byte M = (byte)(255 - G);
                        //byte Y = (byte)(255 - B);

                        //data[c + 0] = C;
                        //data[c + 1] = M;
                        //data[c + 2] = Y;
                        ////The k value need is as it sould be
                        #endregion

                        //Removes the alpha channel
                        data[c + 3] = 0;
                    }
                }

                return data;
            }
        }

        public override bool Equals(PdfFilter filter)
        {
            return filter is PdfDCTFilter;
        }

        public override string ToString()
        {
            return "/DCTDecode";
        }

        //Gets BPP out of a jpeg file
        internal static int GetBBP(byte[] jpeg)
        {
            int read_pos = 2;

            while (read_pos + 9 < jpeg.Length)
            {
                int marker = jpeg[read_pos++] << 8 | jpeg[read_pos++];
                int size = jpeg[read_pos++] << 8 | jpeg[read_pos++];

                if (marker >= 0xffc0 && marker <= 0xffcf && marker != 0xffc4 && marker != 0xffc8)
                    return jpeg[read_pos] * jpeg[read_pos + 5];
                else
                    read_pos += size - 2;
            }

            throw new PdfReadException(Internal.PdfType.XObject, ErrCode.IsCorrupt);
        }
    }
}
