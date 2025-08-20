using System;
using System.Collections.Generic;
using System.IO;
using PdfLib.Pdf;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using LibJpeg.Classic;
using PdfLib.Util;

namespace PdfLib.Img
{
    //Quick port of JPX.cs for use with Jpeg.
    public static class Jpeg
    {
        /// <summary>
        /// Compresses an image into jpeg format
        /// </summary>
        /// <param name="input">Image to compress</param>
        /// <param name="output">Compressed data</param>
        /// <param name="jp2">Whenever to save as Jp2</param>
        /// <param name="quality">Quality of compression, from 0 to 100</param>
        /// <param name="linear">Use distro allocation</param>
        /// <returns></returns>
        public static byte[] Compress(JPGImage input, int quality, bool linear)
        {
            using (var ms = new MemoryStream(20000))
            {
                input.Compress(ms, quality, linear);
                return ms.ToArray();
            }
        }
        public static byte[] Compress(JPGImage input, int quality)
        {
            return Compress(input, quality, false);
        }
        public static void Compress(JPGImage input, int quality, Stream output)
        {
            input.Compress(output, quality, false);
        }

        /// <summary>
        /// compresses a PDF image into a jpeg PDF image
        /// </summary>
        /// <param name="img">The image to compress</param>
        /// <param name="quality">Quality level</param>
        /// <param name="gray_quality">Quality level of grayscale images</param>
        /// <param name="linear">What scale to use for quality. non-linear is 1-100</param>
        /// <param name="force_grayscale">Grayscales all images</param>
        /// <param name="force_compress">Force jpeg compression, even if the filesize gets bigger</param>
        /// <returns>The compressed image or the original image if no compression could be achived</returns>
        /// <remarks>
        /// Know bug: does not take into account ziped jpegs, i.e. an image can
        /// be a zipped jpeg of 50KB, and that is compared with a compressed jpeg
        /// of 50Kb (that will be 25KB ziped) with the conclution that there is
        /// no change.
        /// </remarks>
        public static PdfImage CompressPDF(PdfImage img, int quality, int gray_quality, bool linear, bool force_grayscale, bool force_compress)
        {
            var format = img.Format;
            byte[] data;

            //CCITT and JBIG2 images are rarely worth compressing, so we
            //won't bother with them at all.
            if (!force_compress && (format == ImageFormat.CCITT || format == ImageFormat.JBIG2))
                return img;
            var options = PDFtoJPGOptions.NONE;
            if (img.BitsPerComponent != 8)
                img = PdfImage.ChangeBPC(img, 8, false);

            bool force_rgb = false;
            var jpg = PdfImageToJPEG(img, options, ref force_rgb);

            if (force_grayscale || jpg.IsGray(5, 25))
            {
                //Unfortunatly decode may now have to be applied.
                double[] def_dec = null;
                try { def_dec = img.ColorSpace.DefaultDecode; } catch { }
                if (!ArrayHelper.ArraysEqual(def_dec, xRange.ToArray(img.Decode)))
                    jpg = PdfImageToJPEG(img, PDFtoJPGOptions.APPLY_DECODE, ref force_rgb);

                //Grayscales
                jpg = jpg.Grayscale();
                quality = gray_quality;
                force_rgb = true;
            }

            data = Compress(jpg, quality, linear);

            if (!force_compress && data.Length >= img.Stream.Length)
                return img;

            //File.WriteAllBytes("C:/temp/test.jpg", data);

            var new_img = PdfImage.Create(data);

            if (force_rgb)
            {
                //When force_rgb is true one can assume that the colors
                //have been translated into RGB with default decode.
                //The "Create" function has already set the color space,
                //with the advantage that we also get grayscale images
                //this way.
            }
            else
            {
                //One must set the color space before one set the decode
                //array. 
                new_img.ColorSpace = img.ColorSpace;

                //The purpose of a decode array is to translate the
                //color values into something the color space understands
                //It may be tempting to always apply the decode array,
                //even when keeping the color space, but that can potentially
                //break color spaces that use ranges that can't be represented
                //with integers and the default decode array. 
                new_img.Decode = img.Decode;
            }

            return new_img;
        }

        /// <summary>
        /// This functions makes a PDF image into a jpeg image.
        /// </summary>
        /// <param name="img">Image to convert</param>
        public static JPGImage PdfImageToJPEG(PdfImage img)
        {
            bool rbg = false;
            return PdfImageToJPEG(img, PDFtoJPGOptions.FOR_DISK, ref rbg);
        }

        /// <summary>
        /// This functions makes a PDF image into a jpeg image.
        /// </summary>
        /// <param name="img">Image to convert</param>
        /// <param name="options">
        /// A varity of options on what to embed.
        /// </param>
        /// <param name="force_rgb">
        /// Not all color spaces are supported for convertion, when such
        /// a color space is encountered this flag is set true. Note that
        /// for images that are RGB already this flag will be set false
        /// even if it's originally true
        /// 
        /// Note, if set true the decode array will be applied.
        /// </param>
        public static JPGImage PdfImageToJPEG(PdfImage img, PDFtoJPGOptions options, ref bool force_rgb)
        {
            #region Step 0. JPX 2000

            if (img.Format == ImageFormat.JPEG2000)
            {
                //This converts to BGRA32
                //var jpx = img.DecodeImage2K();
                throw new NotImplementedException();
            }

            #endregion

            #region Step 1. Determine what colorspace to use

            var cs = img.ColorSpace;
            J_COLOR_SPACE jpg_cs;
            if (cs is DeviceRGB)
            {
                jpg_cs = J_COLOR_SPACE.JCS_RGB;
                force_rgb = false;
            }
            else if (cs is DeviceGray)
            {
                jpg_cs = J_COLOR_SPACE.JCS_GRAYSCALE;
                force_rgb = false;
            }
            else if (cs is DeviceCMYK)
                jpg_cs = J_COLOR_SPACE.JCS_CMYK;
            else if (cs is ICCBased)
            {
                var icc = (ICCBased)cs;
                var alt = icc.Alternate;
                if (alt is DeviceCMYK)
                    jpg_cs = J_COLOR_SPACE.JCS_CMYK;
                else
                {
                    force_rgb = false;
                    jpg_cs = alt is DeviceRGB ? J_COLOR_SPACE.JCS_RGB : J_COLOR_SPACE.JCS_GRAYSCALE;
                }
            }
            else
            {
                //This is perhaps not nessesary as long as the number of channels are the
                //same as the jpeg's color space. 
                force_rgb = true;
                jpg_cs = J_COLOR_SPACE.JCS_RGB;
            }

            if (force_rgb)
            {

                //If we need to force RGB, it's probably best to apply the
                //decode array. Otherwise one will have to translate the
                //decode array from the color space it's in to RGB (Two
                //calls to iColorSpace.MakeColor with decode min/max as
                //parameters does this, but for now we'll simly force this
                //flag regardless of what the caller wants).
                options |= PDFtoJPGOptions.APPLY_DECODE;
            }

            #endregion

            #region Step 2. Handle Alpha channel

            //The raw pixel data
            var components = img.SepPixelComponents;
            int bpc = img.BitsPerComponent;

            if (force_rgb || (options & PDFtoJPGOptions.APPLY_MATTE) != 0)
            {
                if (img.Matte != null)
                {
                    //When force rgb is set one must undo the matte color, or alternativly convert
                    //the matte color to RGB (using iColorSpace.MakeColor). For now we undo.
                    UndoMatte(img, ref components, cs, bpc, options, force_rgb);
                    goto pack;
                }
            }

            #endregion

            #region Step 3. Applies Decode array

            if (force_rgb)
            {
                //This means we have to convert the colors from their current colorspace to RGB colors.
                //The IntDecodeTable could in theory be used for this just fine, but few if any of my
                //colorspaces can handle integer colors.
                double[,] dlut;
                if ((options & PDFtoJPGOptions.APPLY_DECODE) != 0)
                    dlut = img.DecodeLookupTable;
                else
                    dlut = PdfImage.CreateDLT(xRange.Create(cs.DefaultDecode), 0, bpc, cs.NComponents);

                int size = components[0].Length, max = (1 << bpc) - 1;
                double[] color = new double[cs.NComponents];
                var cc = cs.Converter;

                //Rezises the componets array to be big enough.
                if (components.Length < 3)
                {
                    Array.Resize(ref components, 3);
                    for (int c = color.Length; c < 3; c++)
                        components[c] = new int[size];
                }

                for (int c = 0; c < size; c++)
                {
                    //Converts the integer into a double through use of a LUT
                    for (int i = 0; i < color.Length; i++)
                        color[i] = dlut[i, components[i][c]];

                    //Makes the colorspace convert the color to RGB
                    var dblcol = cc.MakeColor(color);

                    //Converts back to int
                    components[0][c] = (int)(Clip(dblcol.R) * max);
                    components[1][c] = (int)(Clip(dblcol.G) * max);
                    components[2][c] = (int)(Clip(dblcol.B) * max);
                }

                //Components can be above 3
                if (components.Length != 3)
                    Array.Resize(ref components, 3);
                jpg_cs = J_COLOR_SPACE.JCS_RGB;
            }
            else
            {
                if ((options & PDFtoJPGOptions.APPLY_DECODE) != 0)
                {
                    //Checks if the image uses the default decode array
                    bool default_decode = xRange.Compare(img.Decode, xRange.Create(cs.DefaultDecode));
                    if (!default_decode)
                    {
                        int max = (1 << bpc) - 1;
                        var dlut = img.CreateIntDecodeTable();

                        for (int c = 0; c < components.Length; c++)
                        {
                            var comp = components[c];
                            var lookup = dlut[c];
                            for (int i = 0; i < comp.Length; i++)
                                comp[i] = Clip(lookup[comp[i]], max); //<-- Could also do the clipping on the lookup table
                        }
                    }
                }
            }

        #endregion

            #region Step 4. Pack data into JPX structure
        pack:

            var jpg = new JPGImage(img.Width, img.Height, img.BitsPerComponent, jpg_cs, components);

            return jpg;

            #endregion
        }

        /// <summary>
        /// Undoes premultiplied matte color. Only tested for RGB images
        /// 
        /// Note that the "Lab" color space is not actually supported but Adobe spits
        /// out some odd result. Look to PdfLib Alpha 24 for code to render such images.
        /// </summary>
        /// <remarks>
        /// Inefficiency:
        /// The "alpha" array is decoded twice. Once by the calling method and once here.</remarks>
        internal static void UndoMatte(PdfImage img, ref int[][] components, IColorSpace cs, int bpc, PDFtoJPGOptions options, bool force_rgb)
        {
            var matte = img.Matte;
            var smask = img.SMask;
            var mask_comps = smask.PixelComponents;
            var mask_decode = smask.DecodeLookupTable;

            //Using double for undoing matte. Saves me a bit of trouble since the matte 
            //color itself is a double.
            // i.e. integer -> double -> do work -> convert back to integer
            double[,] dlut;
            if ((options & PDFtoJPGOptions.APPLY_DECODE) != 0)
                dlut = img.DecodeLookupTable;
            else
                dlut = PdfImage.CreateDLT(xRange.Create(cs.DefaultDecode), 0, bpc, cs.NComponents);

            int size = components[0].Length, max = (1 << bpc) - 1;

            if (force_rgb)
            {
                double[] color = new double[cs.NComponents];
                var cc = cs.Converter;

                //Rezises the componets array to be big enough.
                if (components.Length < 3)
                {
                    Array.Resize(ref components, 3);
                    for (int c = color.Length; c < 3; c++)
                        components[c] = new int[size];
                }

                for (int c = 0; c < size; c++)
                {
                    //Converts the integer into a double through use of a LUT
                    var a = mask_decode[0, mask_comps[c]];
                    for (int i = 0; i < color.Length; i++)
                    {
                        //c = (c' - m) / a + m
                        var m = matte[i];
                        color[i] = (dlut[i, components[i][c]] - m) / a + m;
                    }

                    //Makes the colorspace convert the color to RGB
                    var dblcol = cc.MakeColor(color);

                    //Converts back to int
                    components[0][c] = (int)(Clip(dblcol.R) * max);
                    components[1][c] = (int)(Clip(dblcol.G) * max);
                    components[2][c] = (int)(Clip(dblcol.B) * max);
                }

                if (components.Length != 3)
                    Array.Resize(ref components, 3);
            }
            else
            {
                for (int c = 0; c < size; c++)
                {
                    //Converting the alpha to a double using it's lookup table
                    var a = mask_decode[0, mask_comps[c]];
                    for (int i = 0; i < components.Length; i++)
                    {
                        //c = (c' - m) / a + m
                        var m = matte[i];

                        //First the integer component is converted to a double using the "dlut",
                        //then a bit of math with the matte and alpha values, the result is clipped
                        //to 0 <-> 1 and converted back to an integer.
                        components[i][c] = (int)(Clip((dlut[i, components[i][c]] - m) / a + m) * max);
                    }
                }
            }
        }

        /// <summary>
        /// Clips the value to 0 to 1
        /// </summary>
        private static double Clip(double val) { return val > 1 ? 1 : val < 0 ? 0 : val; }

        /// <summary>
        /// Clips the value from 0 to max
        /// </summary>
        /// <remarks>This is done since decode arrays can be invalid in various ways</remarks>
        private static int Clip(int val, int max) { return val > max ? max : val < 0 ? 0 : val; }

        [Flags()]
        public enum PDFtoJPGOptions
        {
            NONE,
            APPLY_DECODE = 0x01,
            APPLY_MATTE = 0x02,
            FOR_DISK = APPLY_DECODE | APPLY_MATTE
        }

        public class JPGImage
        {
            private readonly int[][] _data;
            private readonly int _bpc;
            private readonly J_COLOR_SPACE _cs;
            readonly int _width, _height;

            internal JPGImage(int width, int height, int bpc, J_COLOR_SPACE cs, int[][] components)
            {
                _width = width; _height = height;
                _bpc = bpc;
                _cs = cs;
                _data = components;
            }

            /// <summary>
            /// Checks if a JPX image is grayscale.
            /// </summary>
            /// <param name="jpx">Image to check</param>
            /// <param name="threshold">
            /// How much difference is tolerated between the color
            /// channels. The range goes from 0 to 1, where 1 is full
            /// color and 0 is no color.
            /// </param>
            /// <param name="ppt">
            /// Parts per thousand pixels that can be colored.
            /// </param>
            public bool IsGray(double threshold, int ppt)
            {
                #region Fetches component data

                var comps = _data;

                if (comps.Length == 0) return false;
                if (comps.Length == 1) return true;

                #endregion

                #region Checks channel size
                //If the channels are of a different size one can be
                //reasonable certain that this image isn't grayscale

                //Gets the full image resolution
                int width = _width, height = _height;

                //Precalcs prec
                double precs = (1 << _bpc) - 1;

                #endregion

                #region Checks the colors

                int size = comps[0].Length;

                int max_ncolored = (int)(size * ppt / 1000d), ncolored = 0;

                double min, max;
                for (int c = 0; c < size; c++)
                {
                    min = double.MaxValue;
                    max = double.MinValue;
                    for (int i = 0; i < comps.Length; i++)
                    {
                        double color = comps[i][c] / precs;
                        max = Math.Max(max, color);
                        min = Math.Min(min, color);
                    }

                    if (max - min > threshold && ++ncolored > max_ncolored)
                        return false;
                }

                return true;

                #endregion
            }

            /// <summary>
            /// Grayscales a JPX image
            /// </summary>
            public JPGImage Grayscale()
            {
                #region Gets data

                //I've not yet investigated what impact dx,dy and different resolutions
                //have on the channels. 

                //Gets the full image resolution
                int width = _width, height = _height;

                //Resize channels if needed
                var comps = _data;

                //Grayscaled images are returned as they are
                if (comps.Length == 1) return this;

                var cc = comps;

                #endregion

                #region Set up color space conversion

                var col = ResolveJPGColorSpace();

                double[][] dlut = new double[comps.Length][];
                xRange[] ranges = xRange.Create(col.DefaultDecode);
                int gray_bpp = 0;

                for (int c = 0, last_bpp = 0; c < comps.Length; c++)
                {
                    var comp = comps[c];
                    int bpp = _bpc;
                    if (last_bpp == bpp && ranges[c] == ranges[c - 1])
                        dlut[c] = dlut[c - 1]; //<-- Using old LUT instead of making a new one
                    else
                    {
                        gray_bpp = Math.Max(gray_bpp, bpp);
                        dlut[c] = PdfImage.CreateDLT(ranges[c], bpp);
                        last_bpp = bpp;
                    }
                }

                var pixel = new double[col.NComponents];
                var to_gray = DeviceGray.Instance.Converter;
                var to_rgb = col.Converter;
                int max_gray = (1 << gray_bpp) - 1;

                #endregion

                #region grayscale

                int size = cc[0].Length;
                int[] gray_channel = new int[size];

                //Reads out one pixel at a time and converts it to BGRA32
                for (int c = 0; c < size; c++)
                {
                    //Fills the pixel with data
                    for (int i = 0; i < pixel.Length; i++)
                        pixel[i] = dlut[i][cc[i][c]];

                    //Converts it to a RGB color
                    var rgb = to_rgb.MakeColor(pixel);

                    //Then to a grayscale color
                    var gray = to_gray.MakeColor(rgb);

                    //Converts the color to int
                    gray_channel[c] = (int)(gray[0] * max_gray);
                }

                #endregion

                return new JPGImage(_width, _height, _bpc, J_COLOR_SPACE.JCS_GRAYSCALE, new int[][] { gray_channel });
            }

            /// <summary>
            /// Checks if a JPX image is grayscale.
            /// </summary>
            /// <param name="jpx">Image to check</param>
            /// <param name="threshold">
            /// How much difference is tolerated between the color
            /// channels. The range goes from 0 to 255, where 255 is full
            /// color and 0 is no color.
            /// </param>
            /// <param name="ppt">
            /// Parts per thousand pixels that can be colored.
            /// </param>
            public bool IsGray(byte threshold, int ppt)
            {
                return IsGray(threshold / (double)byte.MaxValue, ppt);
            }

            public IColorSpace ResolveJPGColorSpace()
            {
                //Gets the colorspace out of the JPX
                switch (_cs)
                {
                    case J_COLOR_SPACE.JCS_RGB:
                        return DeviceRGB.Instance;
                    case J_COLOR_SPACE.JCS_CMYK:
                        return DeviceCMYK.Instance;
                    case J_COLOR_SPACE.JCS_GRAYSCALE:
                        return DeviceGray.Instance;
                    case J_COLOR_SPACE.JCS_YCbCr:
                        return new JPXYCC();
                    case J_COLOR_SPACE.JCS_YCCK:
                        return new JPXYCCK();
                    default:
                        var ncomps = _data.Length;
                        if (ncomps == 1)
                            return DeviceGray.Instance;
                        else if (ncomps == 3)
                            return DeviceRGB.Instance;
                        else if (ncomps == 4)
                            return DeviceCMYK.Instance;
                        else
                            throw new PdfInternalException("Unable to get JPG colorspace");
                }
            }


            /// <summary>
            /// Compresses an image into J2K or JP2 format
            /// </summary>
            /// <param name="input">Image to compress</param>
            /// <param name="output">Compressed data</param>
            /// <param name="jp2">Whenever to save as Jp2</param>
            /// <param name="quality">Quality of compression, from 0 to 100</param>
            /// <param name="linear">Distro allocate instead of fixed</param>
            public void Compress(Stream output, int quality, bool linear)
            {
                var err = new jpeg_error_mgr();
                var cinfo = new jpeg_compress_struct(err);
                var ms = output;

                cinfo.Image_width = _width;
                cinfo.Image_height = _height;
                cinfo.In_color_space = _cs;
                cinfo.Input_components = _data.Length;
                if (_bpc != 8)
                    throw new NotSupportedException();
                //cinfo.Data_precision = _bpc;
                cinfo.jpeg_set_defaults();
                cinfo.jpeg_simple_progression();
                if (linear)
                    cinfo.jpeg_set_linear_quality(quality, false);
                else
                    cinfo.jpeg_set_quality(quality, false);
                cinfo.Dct_method = J_DCT_METHOD.JDCT_FLOAT;
                cinfo.Optimize_coding = true;

                //Creates a buffer that can contain one rasterline
                int stride = (_bpc * _data.Length + 7) / 8 * _width;
                var buffer = new byte[stride];
                var bw = new BitWriter(buffer);

                int size = _width * _height;
                cinfo.jpeg_stdio_dest(ms);

                cinfo.jpeg_start_compress(false);
                byte[][] rows = new byte[][] { buffer };
                for (int i = 0; i < size; i += _width)
                {
                    bw.Position = 0;

                    for (int j = 0; j < _width; j++)
                    {
                        var pos = i + j;
                        for (int k = 0; k < _data.Length; k++)
                            bw.Write(_data[k][pos], _bpc);
                    }

                    bw.Flush();
                    cinfo.jpeg_write_scanlines(rows, 1);
                }

                cinfo.jpeg_finish_compress();
            }


            public byte[] ToArray()
            {
                int stride = (_bpc + 7) / 8 * _width;
                int size = stride * _height;
                var bw = new BitWriter(size);

                for (int i = 0; i < size; i += stride)
                {
                    for (int j = 0; j < _width; j++)
                    {
                        var pos = i + j;
                        for (int k = 0; k < _data.Length; k++)
                            bw.Write(_data[k][pos], _bpc);
                    }
                    bw.Align();
                }
                return bw.ToArray();
            }
        }
    }
}
