using System;
using System.IO;
using System.Collections.Generic;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Function;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfLib.Render.WPF
{
    /// <summary>
    /// Transforms images into image source objects, 
    /// or extracts the data for the image.
    /// </summary>
    /// <remarks>
    /// Ways to speed up decoding:
    ///  - Jpegs can be read raw from the stream and rendered directly
    ///  - Faster code for common bit sizes and color spaces
    ///  - Use palette
    ///  
    /// Todo
    ///  - More Sanity checking (image width = 0, bbp = 0, etc) to
    ///    avoid eternal loops
    /// </remarks>
    public static class rDCImage
    {
        public static BitmapSource DecodeImage(PdfImage img, bool include_mask)
        {
            if (include_mask)
            {
                var mask = img.SMask;
                if (mask == null)
                {
                    var ckmask = img.Mask;
                    if (ckmask is int[])
                        return DecodeImage(img);
                    mask = ckmask as PdfImage;
                }
                if (mask == null)
                    return DecodeImage(img);

                var bytes = img.DecodeImage();
                int h = img.Height, w = img.Width;

                if (bytes.Length < w * h)
                    Array.Resize<byte>(ref bytes, w * h);
                mask = PdfImage.ChangeSize(mask, w, h);

                var comps = mask.PixelComponents;
                var decode = mask.DecodeLookupTable;
                if (mask.ColorSpace != DeviceGray.Instance)
                    throw new PdfReadException(PdfType.XObject, ErrCode.Invalid);
                for (int c = 0, j = 0, l = Math.Min(comps.Length, bytes.Length / 4); c < l;)
                {
                    //Color value will be used as is (since we know cs is DeviceGray)
                    var pixel = decode[0, comps[c++]];

                    //Converts the color to bgra32. 
                    j += 3;
                    bytes[j++] = PdfImage.Clip((int)(pixel * byte.MaxValue));
                }

                //Creates WPF image
                var bs = BitmapSource.Create(w, h,
                    96, 96, //DPI (Not used though)
                    PixelFormats.Bgra32, null, bytes, w * 4);

                bs.Freeze();
                return bs;
            }
            else
                return DecodeImage(img);
        }

        public static BitmapSource DecodeImage(PdfImage img)
        {
            if (img.ImageMask)
                return DecodeImageMask(img);

            PixelFormat pf; int stride; byte[] bytes;

            //bytes = SlowImageDecode(img, out stride, out pf);
            bytes = FastImageDecode(img, out stride, out pf);
            var h = img.Height;

            if (bytes.Length < stride * h)
                Array.Resize<byte>(ref bytes, stride * h);

            //Creates WPF image
            var bs = BitmapSource.Create(img.Width, h,
                96, 96, //DPI (Not used though)
                pf, null, bytes, stride);

            bs.Freeze();
            return bs;
        }

        /// <summary>
        /// Converts rImage to a BitmapSource
        /// </summary>
        /// <param name="img">Image to decode</param>
        /// <param name="copy">If false, original data will be modified if format is PREMUL_RGBA8888</param>
        /// <returns>Converted image</returns>
        public static BitmapSource DecodeImage(rImage img, bool copy = true)
        {
            PixelFormat pf;
            var raw = img.RawData;
            switch (img.Format)
            {
                case rFormat.PREMUL_RGBA8888:
                    if (copy)
                    {
                        raw = new byte[raw.Length];
                        Buffer.BlockCopy(img.RawData, 0, raw, 0, img.RawData.Length);
                    }
                    int width = img.Width * 4;
                    for (int y = 0, row = 0; y < img.Height; y++, row += img.Stride)
                    {
                        for (int x = 0; x < width; x += 4)
                        {
                            byte swap = raw[row + x];
                            raw[row + x] = raw[row + x + 2];
                            raw[row + x + 2] = swap;
                        }
                    }
                    pf = PixelFormats.Pbgra32;
                    break;

                case rFormat.RGB24:
                    pf = PixelFormats.Rgb24;
                    break;

                case rFormat.BGRA32:
                    pf = PixelFormats.Bgra32;
                    break;

                default:
                    return DecodeImage(rImage.MakeImage(img));
            }

            var bs = BitmapSource.Create(img.Width, img.Height,
                96, 96, //DPI (Not used though)
                pf, null, raw, img.Stride);

            bs.Freeze();
            return bs;
        }

        /// <remarks>
        /// Soft Mask images don't know that they're soft masks, 
        /// that's decided by the "parent" image, so using a
        /// sepperate decode path for them.
        /// </remarks>
        internal static BitmapSource DecodeSMask(PdfImage img)
        {
            var bytes = img.DecodeSMaskImage();
            int stride = img.Width * 4;
            var bs = BitmapSource.Create(img.Width, img.Height,
                    96, 96, //DPI
                    PixelFormats.Bgra32, null, bytes, stride);
            return bs;
        }

        /// <summary>
        /// Image masks are 1bpp images. Decodes them into a 1bpp
        /// indexed image with alpha.
        /// </summary>
        private static BitmapSource DecodeImageMask(PdfImage img)
        {
            var bytes = img.Stream.DecodedStream;
            var decode = img.Decode;
            var palette = new List<Color> { (decode[0].Min == 0) ? Colors.Black : Colors.Transparent,
                                            (decode[0].Max == 0) ? Colors.Black : Colors.Transparent };
            return BitmapSource.Create(img.Width, img.Height,
                96, 96, //DPI
                PixelFormats.Indexed1, new BitmapPalette(palette), bytes, (img.Width + 7) / 8);
        }

        private static byte[] SlowImageDecode(PdfImage img, out int stride, out PixelFormat pf)
        {
            pf = PixelFormats.Bgra32;
            var width = img.Width;
            stride = (width * pf.BitsPerPixel + 7) / 8;

            return img.DecodeImage();
        }

        private static byte[] FastImageDecode(PdfImage img, out int stride, out PixelFormat pf)
        {
            if (img.Format == PdfLib.Pdf.Internal.ImageFormat.JPEG2000)
                return SlowImageDecode(img, out stride, out pf);

            int nComp;
            var width = img.Width;
            var bpc = img.BitsPerComponent;
            var decode = img.Decode;
            var mask = img.SMask;

            //Not all types of masking is supported
            if (mask != null && mask.Matte != null || img.Mask is int[])
                return SlowImageDecode(img, out stride, out pf);

            //Soft masks is not yet supported.
            // todo: After the image is decoded, check if mask != null. Then decode the image, and then
            //       insert the alpha data into the image and update the color space. You may have to
            //       adjust bpp and the size of the mask, but the jpeg 2000 impl has code for this. 
            if (mask != null)
                return SlowImageDecode(img, out stride, out pf);

            //Fetches the raw pixel data
            byte[] bytes = null;

            if (img.ImageMask)
            {
                //Imagemasks are BI level images

                nComp = 1;
                pf = PixelFormats.BlackWhite;

                bytes = img.Stream.DecodedStream;

                if (decode != null && decode[0].Min == 1 && decode[0].Max == 0)
                {
                    //Special cased as there's only two legal states
                    for (int c = 0; c < bytes.Length; c++)
                        bytes[c] = (byte)~bytes[c];
                }
            }
            else
            {
                //Need to know the pixel format of the image.
                var cs = img.ColorSpace;
                nComp = cs.NComponents;

                //IndexedCS will throw an error on cs.DefaultDecode, so
                //we manually calc default decode
                if (cs is IndexedCS)
                {
                    var def_decode = new xRange(0, (1 << bpc) - 1);
                    if (!xRange.Compare(decode, new xRange[] { def_decode }))
                        return SlowImageDecode(img, out stride, out pf);
                }
                else
                {
                    //Decode is not currently supported
                    if (!xRange.Compare(decode, xRange.Create(cs.DefaultDecode)))
                        return SlowImageDecode(img, out stride, out pf);
                }

                //ICC is not currently supported.
                if (cs is ICCBased)
                    cs = ((ICCBased)cs).Alternate;

                if (cs == DeviceRGB.Instance)
                {
                    if (bpc == 8)
                        pf = PixelFormats.Rgb24;
                    //else if (bpc == 16)
                    //    pf = PixelFormats.Rgb48; //Does not work right, probably have to byteswap
                    else if (bpc < 8)
                    {
                        //For Rgb 111, 222 or 444
                        bytes = img.Stream.DecodedStream;
                        bytes = Img.IMGTools.ExpandBPPto8(bytes, width * 3, img.Height, bpc);
                        pf = PixelFormats.Rgb24;
                        bpc = 8;
                    }
                    else
                        return SlowImageDecode(img, out stride, out pf);
                }
                else if (cs == DeviceGray.Instance)
                {
                    if (bpc == 8)
                        pf = PixelFormats.Gray8;
                    else if (bpc == 1)
                        pf = PixelFormats.BlackWhite;
                    else if (bpc == 2)
                        pf = PixelFormats.Gray2;
                    else if (bpc == 4)
                        pf = PixelFormats.Gray4;
                    //else if (bpc == 16)
                    //     pf = PixelFormats.Gray16;
                    else
                        return SlowImageDecode(img, out stride, out pf);
                }
                else if (cs == DeviceCMYK.Instance)
                {
                    pf = PixelFormats.Cmyk32;
                    if (bpc < 8)
                    {
                        //AFAIK never used, but does not hurt to support it.
                        bytes = img.Stream.DecodedStream;
                        bytes = Img.IMGTools.ExpandBPPto8(bytes, width * 4, img.Height * 4, bpc);
                        bpc = 8;
                    }
                    else if (bpc != 8)
                        return SlowImageDecode(img, out stride, out pf);
                }
                else if (cs is IndexedCS)
                {
                    var indexed = (IndexedCS)cs;
                    var pal = ToWPFPalette(indexed.Palette);

                    //Execute decode array on original data.

                    //Todo: Color key mask (See BAUpload's impl.)

                    //Create image and goto "Mask" step
                    if (bpc == 8)
                        pf = PixelFormats.Indexed8;
                    else if (bpc == 4)
                        pf = PixelFormats.Indexed4;
                    else if (bpc == 2)
                        pf = PixelFormats.Indexed2;
                    else if (bpc == 1)
                        pf = PixelFormats.Indexed1;
                    else
                        return SlowImageDecode(img, out stride, out pf);

                    //Converts the indexed image into a non indexed image.
                    bytes = img.Stream.DecodedStream;
                    stride = (width * bpc + 7) / 8;
                    var tbs = Img.IMGTools.ChangePixelFormat(BitmapSource.Create(width, img.Height,
                        96, 96, //DPI
                        pf, new BitmapPalette(pal),
                        bytes, stride), PixelFormats.Rgb24);
                    bytes = Img.IMGTools.BMStoBA(tbs);
                    pf = PixelFormats.Rgb24;
                    nComp = 3;
                    bpc = 8;
                    //stride = width * 3; //Commented out because stride gets set anyway further down. 
                }
                else if (cs is LabCS)
                {
                    if (bpc != 8) return SlowImageDecode(img, out stride, out pf);
                    pf = PixelFormats.Rgb24;
                    bytes = img.Stream.DecodedStream;
                    ((LabCS)cs).ToRGB.Convert(bytes);
                }
                else
                    return SlowImageDecode(img, out stride, out pf);
            }

            //Calculate stride
            int bitsperpixel = bpc * nComp;
            stride = (width * bitsperpixel + 7) / 8;
            //int padding = stride - (((width * bitsperpixel) + 7) / 8);
            if (bytes == null) bytes = img.Stream.DecodedStream;

            return bytes;
        }

        public static List<Color> ToWPFPalette(PdfColor[] pal)
        {
            var wpf_pal = new List<Color>(pal.Length);
            for (int c = 0; c < pal.Length; c++)
            {
                var col = pal[c].ToARGB();
                wpf_pal.Add(Color.FromArgb(col[0], col[1], col[2], col[3]));
            }
            return wpf_pal;
        }

        public static BitmapSource Transfer(BitmapSource bms, FCalcualte[] transfer)
        {
            bms = Img.IMGTools.ChangePixelFormat(bms, PixelFormats.Rgb24);
            var bytes = Img.IMGTools.BMStoBA(bms);
            rImage.TransferRGB24(bytes, transfer);
            return BitmapSource.Create(bms.PixelWidth, bms.PixelHeight, bms.DpiX, bms.DpiY, PixelFormats.Rgb24, null, bytes, bms.PixelWidth * 3);
        }

        public static BitmapSource Transfer32(BitmapSource bms, FCalcualte[] transfer)
        {
            bms = Img.IMGTools.ChangePixelFormat(bms, PixelFormats.Bgra32);
            var bytes = Img.IMGTools.BMStoBA(bms);
            rImage.TransferBGRA32(bytes, transfer);
            return BitmapSource.Create(bms.PixelWidth, bms.PixelHeight, bms.DpiX, bms.DpiY, PixelFormats.Bgra32, null, bytes, bms.PixelWidth * 4);
        }

        /// <summary>
        /// Saves the image as a RGB24 jpeg, regardles of the original bit depth
        /// </summary>
        /// <remarks>This function needs some work to support other bit depths and color spaces</remarks>
        public static void SaveAsJpeg(string file_path, PdfImage image, int quality)
        {
            using (FileStream fs = File.Open(file_path, FileMode.Create, FileAccess.Write))
            {
                JpegBitmapEncoder jpeg = new JpegBitmapEncoder();
                jpeg.QualityLevel = quality;
                jpeg.Frames.Add(BitmapFrame.Create(DecodeImage(image)));
                jpeg.Save(fs);
            }
        }

        /// <summary>
        /// Saves the image as a RGBA32 PNG (regarless of the original bit depth)
        /// </summary>
        /// <remarks>This function needs some work to support other bit depths</remarks>
        public static void SaveAsPng(string file_path, PdfImage image)
        {
            using (FileStream fs = File.Open(file_path, FileMode.Create, FileAccess.Write))
            {
                PngBitmapEncoder png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(DecodeImage(image)));
                png.Save(fs);
            }
        }
    }
}
