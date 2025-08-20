#define OpenJpeg
using System;
using System.IO;
#if OpenJpeg
using OpenJpeg;
using OpenJpeg.Internal;
using OpenJpeg.Util;
#else
using OpenJpeg2;
using OpenJpeg2.Internal;
using OpenJpeg2.Util;
#endif
using PdfLib.Img.Internal;
using PdfLib.Pdf;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Img
{
    /// <summary>
    /// Helper class to make it easier to work with JPX files
    /// </summary>
    public class JPX
    {
        public static JPXImage Decompress(string filepath)
        {
            using (var fs = File.OpenRead(filepath))
            {
                return Decompress(fs);
            }
        }

        public static JPXImage Decompress(Stream img)
        {
            var hold = img.Position;
            var b = img.ReadByte();
            img.Position = hold;
            var cinfo = new CompressionInfo(true, b == 0 ? CodecFormat.Jpeg2P : CodecFormat.Jpeg2K);
            var cio = cinfo.OpenCIO(img, true);
            var parameters = new DecompressionParameters();
            cinfo.SetupDecoder(cio, parameters);
            JPXImage ret_img;
            if (!cinfo.ReadHeader(out ret_img) || !cinfo.Decode(ret_img) || !cinfo.EndDecompress())
                throw new PdfFilterException(ErrCode.General);

            return ret_img;
        }

        public static JPXImage Decompress(byte[] data)
        {
            return Decompress(data, false);
        }

        internal static JPXImage Decompress(byte[] data, bool apply_color_lookup_table)
        {
            var cinfo = new CompressionInfo(true, data[0] == 0 ? CodecFormat.Jpeg2P : CodecFormat.Jpeg2K);
            var parameters = new DecompressionParameters();
            if (apply_color_lookup_table)
                parameters.IgnoreColorLookupTable = true;
            JPXImage img;

            using (var ms = new MemoryStream(data, false))
            {
                var cio = cinfo.OpenCIO(ms, true);
                cinfo.SetupDecoder(cio, parameters);
                if (!cinfo.ReadHeader(out img) || !cinfo.Decode(img) || !cinfo.EndDecompress())
                    throw new PdfFilterException(ErrCode.General);
            }

            return img;
        }

        /// <summary>
        /// Compresses an image into JP2 format
        /// </summary>
        /// <param name="input">Image to compress</param>
        /// <param name="output">Compressed data</param>
        /// <param name="jp2">Whenever to save as Jp2</param>
        /// <param name="quality">Quality of compression, from 0 to 100, where 100 is lossless</param>
        /// <param name="distro">Use distro allocation</param>
        /// <returns></returns>
        public static byte[] Compress(JPXImage input, int quality, bool distro)
        {
            using (var ms = new MemoryStream((int)(input.ImageSize * 0.1625 + 2000)))
            {
                Compress(input, ms, true, quality, distro);
                return ms.ToArray();
            }
        }
        /// <summary>
        /// Compresses an image into JP2 format
        /// </summary>
        /// <param name="input">Image to compress</param>
        /// <param name="quality">Quality of compression, from 0 to 100, where 100 is lossless</param>
        /// <returns></returns>
        public static byte[] Compress(JPXImage input, int quality)
        {
            return Compress(input, quality, false);
        }

        /// <summary>
        /// Compresses an image into J2K or JP2 format
        /// </summary>
        /// <param name="input">Image to compress</param>
        /// <param name="output">Compressed data</param>
        /// <param name="jp2">Whenever to save as Jp2</param>
        /// <param name="quality">Quality of compression, from 0 to 100</param>
        /// <returns></returns>
        public static byte[] Compress(JPXImage input, bool jp2, int quality)
        {
            using (var ms = new MemoryStream((int)(input.ImageSize * 0.1625 + 2000)))
            {
                Compress(input, ms, jp2, quality, false);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Compresses an image into J2K or JP2 format
        /// </summary>
        /// <param name="input">Image to compress</param>
        /// <param name="output">Compressed data</param>
        /// <param name="jp2">Whenever to save as Jp2</param>
        /// <param name="quality">Quality of compression, from 0 to 100</param>
        /// <param name="distro">Distro allocate instead of fixed</param>
        /// <remarks>
        /// Jpeg 2000 allows for image componets to have different precision and
        /// resolution. One can take advantage of that to improve compression, say
        /// by having lover precision on red and blue componets.
        /// 
        /// This has to be done on the input JPXImage, it's not a function of the
        /// encoder. 
        /// </remarks>
        public static void Compress(JPXImage input, Stream output, bool jp2, int quality, bool distro)
        {
            const int MAX_QUALITY = 100;
            const int MIN_QUALITY = 0;
            if (quality < MIN_QUALITY && !distro || quality > MAX_QUALITY)
                throw new ArgumentOutOfRangeException("quality");
            var cp = new CompressionParameters();
            cp.MultipleComponentTransform = input.UseMCT;

            //Tilesize limits memory usage during encoding, but bloats the filesize. Can be
            //set to "null" for no tiles.
            cp.TileSize = new TileSize(512, 512);

            //Observation:
            // Fixed quality with Irreversible = true seems to
            // give good result on small files. However 
            // Irreversible = false is good too. Hard to pick
            // a winner
            //
            // Irreversible should not be used with DistroAlloc
            // as that gave poor result (on small files)
            cp.Irreversible = quality != 100 && !distro;

            //There are two ways to adjust the compression ratio.
            if (distro)
            {
                cp.DistroAlloc = true;

                //Distro alloc sets how many x times an image is to be compressed. I define
                //the range as going from 0 = 60 to 100 = 1. (IOW 0 is 60 times compressed)
                const int MAX_COMPRESS = 160;
                const int MIN_COMPRESS = 1;
                //quality = (quality - MIN_QUALITY) * (MIN_COMPRESS - MAX_COMPRESS) / (MAX_QUALITY - MIN_QUALITY) + MAX_COMPRESS;
                quality = (int)LinearInterpolator.Interpolate(quality, MIN_QUALITY, MAX_QUALITY, MAX_COMPRESS, MIN_COMPRESS);

                //I'm not sure if there's a point in using multiple layers. Does not seem to hurt though.
                int n_layers = quality < 20 ? 3 : quality < 30 ? 2 : 1;
                cp.NumberOfLayers = n_layers;
                for (int c = 0; c < cp.NumberOfLayers; c++)
                    cp.Rates[c] = quality * n_layers--;
            }
            else
            {
                //Fixed quality is a bit tricky as it's the range 30-50 that has the big filesize impact
                //So I define 0 to 10 to be 1 to 30, 11 to 90 to be 30-60 and 61 to 100 to be 90-100
                // cp.DistoRatio[2] = 37.74f;
                cp.FixedQuality = true;

                //Since I may want to change MIN/MAX later I interpolate them into a range from 0 to 100
                double q = LinearInterpolator.Interpolate(quality, MIN_QUALITY, MAX_QUALITY, 0, 100);

                //Then I scale them into whatever range I want.
                if (q <= 10) //0 to _11_ is intended
                    q = LinearInterpolator.Interpolate(q, 0, 11, 1, 30);
                else if (q < 90)
                    q = LinearInterpolator.Interpolate(q, 11, 90, 30, 60);
                else //_90_ to 100 is intended
                    q = LinearInterpolator.Interpolate(q, 90, 100, 60, 100);

                //I'm not sure if there's a point in using multiple layers. Does not seem to hurt 
                //much though.
                int n_layers = q > 60 ? 3 : q > 30 ? 2 : 1;
                cp.NumberOfLayers = n_layers;
                for (int c = 0; c < cp.NumberOfLayers; c++)
                    cp.DistoRatio[c] = (float)(n_layers > 1 ? q / (2d * --n_layers) : q);
            }

            //"FixedAlloc" lets one set the compression of not only layers,
            //but also resolution. By default we use 6 resolutions, I have not experimented
            //on how number of resolutions affect the file size.

            CompressJ2K(input, output, cp, jp2);
        }

        private static void CompressJ2K(JPXImage input, Stream output, CompressionParameters cp, bool JP2)
        {
            var cinfo = new CompressionInfo(false, JP2 ? CodecFormat.Jpeg2P : CodecFormat.Jpeg2K);
            var cio = cinfo.OpenCIO(output, false);
            if (!cinfo.SetupEncoder(cp, input) || !cinfo.StartCompress(cio) || !cinfo.Encode() || !cinfo.EndCompress())
                throw new Exception("Must try harder!");
        }

        /// <summary>
        /// This functions makes a PDF image into a JPX image.
        /// </summary>
        /// <param name="img">Image to convert</param>
        /// <param name="quality">0 is lowest, 100 is loseless</param>
        public static MemoryStream PdfImageToJPX(PdfImage img, int quality)
        {
            bool rgb = false;
            var input = PdfImageToJPX(img, PDFtoJP2Options.FOR_DISK, ref rgb);
            var ms = new MemoryStream((int)(input.ImageSize * 0.1625 + 2000));
            Compress(input, ms, true, quality, false);
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// This functions makes a PDF image into a JPX image.
        /// </summary>
        /// <param name="img">Image to convert</param>
        public static JPXImage PdfImageToJPX(PdfImage img, PDFtoJP2Options options)
        {
            bool rgb = false;
            return PdfImageToJPX(img, options, ref rgb);
        }

        /// <summary>
        /// This functions makes a PDF image into a JPX image.
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
        /// Note, if set true the decode array will be applied and if
        /// there's a matte alpha mask, said mask will be embeded in
        /// the JPX.
        /// </param>
        /// <returns></returns>
        public static JPXImage PdfImageToJPX(PdfImage img, PDFtoJP2Options options, ref bool force_rgb)
        {
            #region Step 0. JPX 2000

            int bpc;

            if (img.Format == ImageFormat.JPEG2000)
            {
                if (force_rgb)
                {
                    //A bit inefficient. First we decode to a JPXImage, then that JPXImage is
                    //converted into a RGBA8888 byte array, then we convert that byte array
                    //into a JPXImage again. 
                    return ConvertToJPX_bgra32(img.DecodeImage2K(), img.Width, img.Height,
                        img.SMask != null || img.Mask != null || img.SMaskInData != 0);

                    //This neglects to check if the JPXImage already is in the RGB colorspace*, also
                    //the bpp is changed to 8.
                    // * In which case the conversion step could be skipped.
                }
                force_rgb = false;

                try { bpc = img.BitsPerComponent; }
                catch (PdfReadException)
                {
                    //bpc is optional on JPX images, so we need to fetch it out of the container.
                    var data = img.Stream.DecodeTo<Pdf.Filter.PdfJPXFilter>();

                    var cinfo = new CompressionInfo(true, data[0] == 0 ? CodecFormat.Jpeg2P : CodecFormat.Jpeg2K);

                    var parameters = new DecompressionParameters();

                    using (var ms = new MemoryStream(data, false))
                    {
                        var cio = cinfo.OpenCIO(ms, true);
                        cinfo.SetupDecoder(cio, parameters);
                        JPXImage jpx_header;

                        if (!cinfo.ReadHeader(out jpx_header))
                            throw new Exception("Failed to read header");

                        if (jpx_header.UniformBPC)
                            bpc = jpx_header.MaxBPC;
                        else
                        {
                            //We can't handle non-uniform BPC, so we return the JPX image as is.
                            //TODO: Log this happening somewhere. 
                            ms.Position = 0;
                            return Decompress(ms);
                        }
                    }
                }
            }
            else
            {
                bpc = img.BitsPerComponent;
            }

            #endregion

            #region Step 1. Determine what colorspace to use

            var cs = img.ColorSpace;
            COLOR_SPACE jpx_cs;
            if (cs is DeviceRGB || cs is CalRGBCS && (options & PDFtoJP2Options.FORCE_RGB) != PDFtoJP2Options.FORCE_RGB)
            {
                jpx_cs = COLOR_SPACE.sRGB;
                force_rgb = false;
            }
            else if (cs is DeviceGray || cs is CalGrayCS && (options & PDFtoJP2Options.FORCE_RGB) != PDFtoJP2Options.FORCE_RGB)
            {
                jpx_cs = COLOR_SPACE.GRAY;
                force_rgb = false;
            }
            else if (cs is DeviceCMYK)
            {
                jpx_cs = COLOR_SPACE.CMYK;
                if ((options & PDFtoJP2Options.FORCE_RGB) == PDFtoJP2Options.FORCE_RGB)
                    force_rgb = true;
            }
            else if (cs is LabCS)
            {
                jpx_cs = COLOR_SPACE.CIELab;
                if ((options & PDFtoJP2Options.FORCE_RGB) == PDFtoJP2Options.FORCE_RGB)
                    force_rgb = true;
            }
            else if (cs is ICCBased)
            {
                var icc = (ICCBased)cs;
                var alt = icc.Alternate;
                if (alt is DeviceCMYK)
                {
                    jpx_cs = COLOR_SPACE.CMYK;
                    if ((options & PDFtoJP2Options.FORCE_RGB) == PDFtoJP2Options.FORCE_RGB)
                        force_rgb = true;
                }
                else
                {
                    force_rgb = false;
                    jpx_cs = alt is DeviceRGB ? COLOR_SPACE.sRGB : COLOR_SPACE.GRAY;
                }
            }
            else
            {
                //JPX files do support indexed colors, but I suspect that one will have to use
                //the cmap (and pclr) for that. However, there is no WritePCLR/CMAP functions so
                //I will have to make that from scratch. OpenJPEG does read and apply
                //the cmaps so it shouldn't be too difficult to reverse engineer that structure
                //from ReadPCLR (Palette), ReadCMAP (ComponentMapping) and ApplyPCLR in JP2.cs
                //
                //Do note, indexed colors only makes sense for losslessly compressed jpx images

                //I suppose you can allow CalGray through as well, but it's a pretty
                //rare color space so doing RGB for it too.                
                jpx_cs = COLOR_SPACE.sRGB;

                //If the palette dosn't have color, we can drop two of the channels.
                if (cs is IndexedCS ics)
                {
                    if (!force_rgb && !ics.HasColor)
                    {
                        jpx_cs = COLOR_SPACE.GRAY;
                    }
                }

                force_rgb = true;
            }

            if (force_rgb)
            {

                //If we need to force RGB, it's probably best to apply the
                //decode array. Otherwise one will have to translate the
                //decode array from the color space it's in to RGB (Two
                //calls to iColorSpace.MakeColor with decode min/max as
                //parameters does this, but for now we'll simly force this
                //flag regardless of what the caller wants).
                options |= PDFtoJP2Options.APPLY_DECODE;

                //Since the ICC profile won't match we remove any such
                //profile embeding
                options &= ~PDFtoJP2Options.EMBED_ICC;
            }

            #endregion

            #region Step 2. Handle Alpha channel

            //The raw pixel data
            var components = img.GetSepPixelComponents(bpc);
            ImageComp alpha_comp = null;


            if (force_rgb && bpc < 8 && cs is IndexedCS)
            {
                //Problem, an index have 8bpp percision. When unpacking the index,
                //we therefore have to have at least 8bpp to maintain the colors.
                bpc = 8;
            }

            if ((options & PDFtoJP2Options.APPLY_SMASK) != 0)
            {
                var smask = img.SMask;
                if (smask != null)
                {
                    var alpha = MakeAlpha(smask, bpc, false);

                    //Images with softmask may have been premultiplied
                    //with alpha. Undoing that.
                    var matte = img.Matte;
                    if (matte != null)
                    {
                        //j2k do support premultiplied alpha, but that is against the
                        //color and not some "matte" color.
                        UndoMatte(img, ref components, cs, bpc, options, force_rgb);

                        if (force_rgb)
                            jpx_cs = COLOR_SPACE.sRGB;
                        alpha_comp = new ImageComp(bpc, bpc, false, 1, 1, img.Width, img.Height, alpha) { IsAlpha = true };

                        //The image has now been converted. To avoid having the decode array applied again
                        //we jump straight to the pack section.
                        goto pack;
                    }
                    else
                    {
                        //We may have to rezise the alpha channel to fit the whole image.
                        //
                        //However if the data is smaller than the dest image one can adjust dx, dy until the alpha
                        //data fits.
                        alpha = Scaler.Rezise(alpha, smask.Width, smask.Height, img.Width, img.Height);
                        alpha_comp = new ImageComp(bpc, bpc, false, 1, 1, img.Width, img.Height, alpha) { IsAlpha = true };
                    }
                }
                else
                {
                    var mask = img.Mask;
                    if (mask is int[])
                    {
                        //We create a alpha channel
                        var alpha = MakeColorKeyAlphaMask(components, (int[])mask, bpc);

                        alpha_comp = new ImageComp(bpc, bpc, false, 1, 1, img.Width, img.Height, alpha) { IsAlpha = true };
                    }
                    else if (mask is PdfImage)
                    {
                        //Note, a MASK works in reverse of a SMask.
                        var mask_image = (PdfImage)mask;
                        var alpha = MakeAlpha(mask_image, 1, true);

                        //We may have to rezise the alpha channel to fit the whole image.
                        //
                        //However if the data is smaller than the dest image, we can adjust dx, dy until the alpha
                        //data fits.
                        alpha = Scaler.Rezise(alpha, mask_image.Width, mask_image.Height, img.Width, img.Height);

                        alpha_comp = new ImageComp(1, 1, false, 1, 1, img.Width, img.Height, alpha) { IsAlpha = true };
                    }
                }
            }
            else if (force_rgb)
            {
                if (img.Matte != null)
                {
                    //When force rgb is set one must undo the matte color, or alternativly convert
                    //the matte color to RGB (using iColorSpace.MakeColor)*. For now we undo.
                    // * Assuming this JPX image is embeded into a PdfImage
                    UndoMatte(img, ref components, cs, bpc, options, force_rgb);
                    alpha_comp = new ImageComp(bpc, bpc, false, 1, 1, img.Width, img.Height, MakeAlpha(img.SMask, bpc, false));
                    goto pack;
                }
                else if (img.Mask is int[])
                {
                    //Alternativly one could convert the color key to RGB
                    var alpha = MakeColorKeyAlphaMask(components, (int[])img.Mask, bpc);
                    alpha_comp = new ImageComp(bpc, bpc, false, 1, 1, img.Width, img.Height, alpha);
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
                if ((options & PDFtoJP2Options.APPLY_DECODE) != 0 || (options & PDFtoJP2Options.SELECTIV_DECODE) != 0)
                    dlut = img.DecodeLookupTable;
                else
                    dlut = PdfImage.CreateDLT(xRange.Create(cs.DefaultDecode), 0, bpc, cs.NComponents);

                int size = components[0].Length, max = (1 << bpc) - 1;
                double[] color = new double[cs.NComponents];
                var cc = cs.Converter;

                //Rezises the componets array to be big enough.
                if (jpx_cs == COLOR_SPACE.GRAY)
                {
                    for (int c = 0; c < size; c++)
                    {
                        //Converts the integer into a double through use of a LUT
                        for (int i = 0; i < color.Length; i++)
                            color[i] = dlut[i, components[i][c]];

                        //Makes the colorspace convert the color to RGB
                        var dblcol = cc.MakeColor(color);

                        //Converts back to int
                        components[0][c] = (int)(Clip(dblcol.R) * max);
                    }

                    Array.Resize(ref components, 1);
                }
                else
                {
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

                    if (components.Length != 3)
                        Array.Resize(ref components, 3);
                    jpx_cs = COLOR_SPACE.sRGB;
                }
            }
            else
            {
                if ((options & PDFtoJP2Options.APPLY_DECODE) != 0)
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
                var img_comps = new ImageComp[components.Length + (alpha_comp != null ? 1 : 0)];

                for (int c = 0; c < components.Length; c++)
                    img_comps[c] = new ImageComp(bpc, bpc, false, 1, 1, img.Width, img.Height, components[c]);
                if (alpha_comp != null)
                    img_comps[img_comps.Length - 1] = alpha_comp;

                var jpx = new JPXImage(0, img.Width, 0, img.Height, img_comps, jpx_cs);

                if (alpha_comp != null)
                    jpx.SetAlpha(img_comps.Length - 1);

                if (!force_rgb && (options & PDFtoJP2Options.EMBED_ICC) != 0 && cs is ICCBased)
                    jpx.ICCProfile = ((ICCBased)img.ColorSpace).Stream.DecodedStream;

                return jpx;

                #endregion
        }

        private static int[] MakeColorKeyAlphaMask(int[][] components, int[] mask, int bpc)
        {
            var alpha = new int[components[0].Length];
            int max_value = (1 << bpc) - 1;
            for (int c = 0; c < alpha.Length; c++)
            {
                for (int i = 0, t = 0; i < components.Length; i++)
                {
                    var r = components[i][c];

                    //If any of the components are outside the range,
                    //the pixel is opague.
                    if (!(mask[t++] <= r && r <= mask[t++]))
                    {
                        //Setting bpp to 1 does not yeild good result.
                        //Not on filesize or blending
                        alpha[c] = max_value;
                        break;
                    }
                }
            }
            return alpha;
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
        internal static void UndoMatte(PdfImage img, ref int[][] components, IColorSpace cs, int bpc, PDFtoJP2Options options, bool force_rgb)
        {
            var matte = img.Matte;
            var smask = img.SMask;
            var mask_comps = smask.PixelComponents;
            var mask_decode = smask.DecodeLookupTable;

            //Using double for undoing matte. Saves me a bit of trouble since the matte 
            //color itself is a double.
            // i.e. integer -> double -> do work -> convert back to integer
            double[,] dlut;
            if ((options & PDFtoJP2Options.APPLY_DECODE) != 0)
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

        /// <summary>
        /// Creates an alpha channel
        /// </summary>
        /// <param name="img">Image to create alpha from</param>
        /// <param name="bpc">Destination bpc</param>
        /// <param name="reverse">If the mask should be reversed</param>
        /// <returns>Alpha channel data</returns>
        private static int[] MakeAlpha(PdfImage img, int bpc, bool reverse)
        {
            //Both mask and smask has at most one channel
            var pixels = img.SepPixelComponents[0];

            //Maximum value
            int max = (1 << bpc) - 1;

            int image_bpc = img.BitsPerComponent;
            var decode = img.Decode;
            if (reverse)
                xRange.Reverse(decode);

            //If decode is default and bpc == img.bpc then one can
            //return straight away
            if (bpc == image_bpc && xRange.Compare(img.Decode, xRange.Create(DeviceGray.Instance.DefaultDecode)))
                return pixels;


            //Uses doubles as they are easier to convert to the correct bpc
            var dlut = PdfImage.CreateDLT(decode, 0, image_bpc, img.NComponents); ;
            for (int c = 0; c < pixels.Length; c++)
                pixels[c] = (int)(dlut[0, pixels[c]] * max);

            return pixels;
        }

        /// <summary>
        /// This function will selectivly compress a PdfImage to JPX.
        /// </summary>
        /// <param name="img">The image to compress</param>
        /// <param name="quality">Quality level to compress with, from 1 to 100</param>
        /// <returns>A JPX compressed PDF image</returns>
        public static PdfImage CompressPDF(PdfImage img, int quality)
        {
            return CompressPDF(img, quality, quality, PDF2JP2Image.strip_icc | PDF2JP2Image.merge_cs);
        }

        /// <summary>
        /// This function will selectivly compress a PdfImage to JPX.
        /// </summary>
        /// <param name="img">The image to compress</param>
        /// <param name="quality">Quality level to compress with, from 1 to 100</param>
        /// <param name="gray_quality">
        /// Gray images can usualy be compressed to a much greater degree. Note that if
        /// this option is set equal to quality, images will not automatically be grayscaled
        /// when appropriate.
        /// </param>
        /// <param name="MakeBILevel">Turns the image into a 1BPP image</param>
        /// <param name="strip_icc">Whenever to remove the ICC profile embeded within the JPX</param>
        /// <param name="merge_cs">
        /// Merges colorspace into the image. Set this false if perserving the 
        /// colorspace is important (Note, this also deactivates automatic grayscaling).
        /// </param>
        /// <returns>A JPX compressed PDF image</returns>
        public static PdfImage CompressPDF(PdfImage img, int quality, int gray_quality, PDF2JP2Image opt)
        {
            var format = img.Format;
            byte[] data;

            //CCITT and JBIG2 images are rarely worth compressing, so we
            //won't bother with them at all. (This can be oversteered by setting the 
            //ignored quality parameter to iligal 101, and gray_quality to a legal value)
            if ((format == ImageFormat.CCITT || format == ImageFormat.JBIG2) && quality != 101 && (opt & PDF2JP2Image.FORCE) == 0)
                return img;

            bool merge_cs = (opt & PDF2JP2Image.merge_cs) != 0;
            bool MakeBILevel = (opt & PDF2JP2Image.MakeBILevel) != 0;

            //We then compress the image, if it's smaller we keep it.
            var options = merge_cs ? PDFtoJP2Options.FORCE_RGB : PDFtoJP2Options.SELECTIV_DECODE;
            if ((opt & PDF2JP2Image.strip_icc) == 0) options |= PDFtoJP2Options.EMBED_ICC;
            if (img.SMask != null && img.Mask is int[])
                //Color key masks are best to apply before compression. Gives
                //very unpredictable results after compression.
                options |= PDFtoJP2Options.APPLY_SMASK;
            bool force_rgb = false, is_grayscaled = false;
            var jpx = PdfImageToJPX(img, options, ref force_rgb);

            //Makes sure the alpha information is correct
            jpx.SetAlphaOnLastChannel((ushort)img.SMaskInData);

            if ((MakeBILevel || gray_quality != quality && merge_cs) && IsGray(jpx, 15, 50))
            {
                //Since the decode array is already applied we can
                //gray scale straight away. Note that this method
                //copies over alpha data.
                //
                //Implementation note: One don't need to do this
                // step, but it cuts a few more KB of the filesize
                // at the cost of the conversion taking longer
                var gray_jpx = Grayscale(jpx);
                if (!ReferenceEquals(gray_jpx, jpx))
                {
                    jpx = gray_jpx;

                    //Flags this image as having been grayscaled, as opposed to one that
                    //was gray to begin with.
                    is_grayscaled = true;
                }

                if (MakeBILevel && (jpx.MaxBPC != 1 || !IsGray(jpx)))
                {
                    //We'll now make the image bi level. For that we
                    //use Otsu’s algorithm to find the ideal threshold
                    //to sepperate between white and black pixels
                    int thresh = OtsuThreshold(jpx.GetOpagueComponents()[0]);
                    jpx.GetOpagueComponents()[0].MakeBILevel(thresh);

                    if (jpx.HasAlpha)
                    {
                        //Alpha must either be reduced to bi level or
                        //extracted and used as a SMask. 
                        throw new NotImplementedException();
                    }

                    return PdfImage.Create(Compress(jpx, quality, true));
                }
                quality = gray_quality;
            }
            else if (IsGray(jpx))
                quality = gray_quality;


            data = Compress(jpx, quality, true);

            if (data.Length >= img.Stream.Length && (opt & PDF2JP2Image.FORCE) == 0)
                return img;

            //File.WriteAllBytes("c:/temp/j2k.jp2", data);

            if (force_rgb)
            {
                //Note that when this flag is set, the decode and colorspace of the
                //original image has no meaning. Since the SELECTIV_DECODE option is
                //set we know that the decode has already beeen applied. (Otherwise
                //one would have the transfrom the decode from the old colorspace to
                //the RGB colorspace)

                //In this case we must ignore the original pdf's color space and
                //decode array. If an SMask has matte or color key mask we must 
                //ignore that too.
                bool ignore_alpha = img.Matte != null || img.Mask is int[];

                var new_image = PdfImage.CreateFromJPXData(data, null, null);

                if (!ignore_alpha)
                {
                    //Mask/ImageMask/SMask data must be transfered. Perhaps also other data. Hmm.
                    new_image.SMask = img.SMask;
                    new_image.Matte = img.Matte;
                    new_image.Mask = img.Mask;
                }

                return new_image;
            }
            else
            {
                PdfImage new_image;

                if ((options & PDFtoJP2Options.APPLY_SMASK) == PDFtoJP2Options.APPLY_SMASK)
                {
                    //Transparency has already been included in the file, and we know that this
                    //transparency isn't premultiplied. Presumably the "Deocde" can be left as is,
                    //but I've not tested that. (I.e. there's now an extra component in the JPX
                    //data, but the Decode array is only for the non-alpha components)
                    new_image = PdfImage.CreateFromJPXData(data, is_grayscaled ? null : img.ColorSpace, null);
                }
                else
                {
                    new_image = PdfImage.CreateFromJPXData(data, is_grayscaled ? null : img.ColorSpace, null);

                    //Mask/ImageMask/SMask data must be transfered. Perhaps also other data. Hmm.
                    new_image.SMask = img.SMask;
                    new_image.Matte = img.Matte;
                    new_image.Mask = img.Mask;
                }

                //Note that "SELECTIV_DECODE" is never set when is_gray = true, but it dosn't hurt
                //to future proof this. Basically we only copy over the decode array if it hasn't
                //already been applied.
                if (!is_grayscaled && (options & PDFtoJP2Options.SELECTIV_DECODE) != 0)
                    new_image.Decode = img.Decode;

                return new_image;
            }
        }

        /// <summary>
        /// Converts a JPX image to the RGB colorspace
        /// </summary>
        public static JPXImage MakeRGB(JPXImage jpx)
        {
            if (jpx.ColorSpace == COLOR_SPACE.sRGB) return jpx;

            #region Todo Undo premultiplied alpha

            //I've not yet investigated what impact dx,dy and different resolutions
            //have on the channels. 

            //Gets the full image resolution
            int width = jpx.Width, height = jpx.Height;

            //Resize channels if needed
            var comps = jpx.GetOpagueComponents();

            //The easiset fix for this is to call "ApplyIndex" on jpx, but instead convert
            //the colorspace to IndexedCS. 
            if (jpx.IsIndexed)
                throw new NotImplementedException("Converting indexed images to grayscale");

            //todo: Undo premultiplied alpha

            #endregion

            #region Resize channels
            //Makes sure all the channels are the same size

            var cc = new int[comps.Length][];
            for (int c = 0; c < cc.Length; c++)
            {
                var comp = comps[c];
                cc[c] = Scaler.Rezise(comp.Data, comp.Width, comp.Height, width, height);
            }

            #endregion

            #region Set up color space conversion

            var col = ResolveJPXColorSpace(jpx);

            double[][] dlut = new double[comps.Length][];
            xRange[] ranges = xRange.Create(col.DefaultDecode);
            int rgb_bpp = 0;

            for (int c = 0, last_bpp = 0; c < comps.Length; c++)
            {
                var comp = comps[c];
                int bpp = comp.Prec;
                if (last_bpp == bpp && ranges[c] == ranges[c - 1])
                    dlut[c] = dlut[c - 1]; //<-- Using old LUT instead of making a new one
                else
                {
                    rgb_bpp = Math.Max(rgb_bpp, bpp);
                    dlut[c] = PdfImage.CreateDLT(ranges[c], bpp);
                    last_bpp = bpp;
                }
            }

            var pixel = new double[col.NComponents];
            var to_rgb = col.Converter;
            int max_rgb = (1 << rgb_bpp) - 1;

            #endregion

            #region RGBify

            int size = cc[0].Length;
            int[][] rgb_channels = new int[][] { new int[size], new int[size], new int[size] };

            //Reads out one pixel at a time and converts it
            for (int c = 0; c < size; c++)
            {
                //Fills the pixel with data
                for (int i = 0; i < pixel.Length; i++)
                    pixel[i] = dlut[i][cc[i][c]];

                //Converts it to a RGB color
                DblColor rgb = to_rgb.MakeColor(pixel);

                //Converts the color to int
                rgb_channels[0][c] = (int)(rgb.R * max_rgb);
                rgb_channels[1][c] = (int)(rgb.G * max_rgb);
                rgb_channels[2][c] = (int)(rgb.B * max_rgb);
            }

            #endregion

            #region Return

            comps = new ImageComp[]
            {
               new ImageComp(rgb_bpp, rgb_bpp, false, 1, 1, width, height, rgb_channels[0]),
               new ImageComp(rgb_bpp, rgb_bpp, false, 1, 1, width, height, rgb_channels[1]),
               new ImageComp(rgb_bpp, rgb_bpp, false, 1, 1, width, height, rgb_channels[2])
            };

            if (jpx.HasAlpha)
            {
                var ac = jpx.AlphaComponents;
                Array.Resize(ref comps, ac.Length + comps.Length);
                for (int c = 0; c < ac.Length; c++)
                    comps[3 + c] = ac[c];

                var ret = new JPXImage(0, width, 0, height, comps, COLOR_SPACE.GRAY);

                //Todo: Copy over the alpha channel defenition data in jpx to ret
                throw new NotImplementedException();
            }
            else
            {
                return new JPXImage(0, width, 0, height, comps, COLOR_SPACE.GRAY);
            }

            #endregion
        }

        /// <summary>
        /// Grayscales a JPX image
        /// </summary>
        public static JPXImage Grayscale(JPXImage jpx)
        {
            #region Todo Undo premultiplied alpha

            //I've not yet investigated what impact dx,dy and different resolutions
            //have on the channels. 

            //Resize channels if needed
            var comps = jpx.GetOpagueComponents();

            //The easiset fix for this is to call "ApplyIndex" on jpx, but instead convert
            //the colorspace to IndexedCS. 
            if (jpx.IsIndexed)
                throw new NotImplementedException("Converting indexed images to grayscale");

            //Grayscaled images are returned as they are. We don't need to check the colorspace,
            //as only indexed, bilevel and grayscale got just one component.
            if (comps.Length == 1 && !jpx.IsIndexed) return jpx;

            //Gets the full image resolution
            int width = jpx.Width, height = jpx.Height;

            //todo: Undo premultiplied alpha

            #endregion

            #region Resize channels
            //Makes sure all the channels are the same size

            var cc = new int[comps.Length][];
            for (int c = 0; c < cc.Length; c++)
            {
                var comp = comps[c];
                cc[c] = Scaler.Rezise(comp.Data, comp.Width, comp.Height, width, height);
            }

            #endregion

            #region Set up color space conversion

            var col = ResolveJPXColorSpace(jpx);

            double[][] dlut = new double[comps.Length][];
            xRange[] ranges = xRange.Create(col.DefaultDecode);
            int gray_bpp = 0;

            for (int c = 0, last_bpp = 0; c < comps.Length; c++)
            {
                var comp = comps[c];
                int bpp = comp.Prec;
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

            //Reads out one pixel at a time and converts it
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

            #region Return

            var gc = new ImageComp(gray_bpp, gray_bpp, false, 1, 1, width, height, gray_channel);

            if (jpx.HasAlpha)
            {
                var ac = jpx.AlphaComponents;
                comps = new ImageComp[ac.Length + 1];
                comps[0] = gc;
                for (int c = 0; c < ac.Length; c++)
                    comps[c + 1] = ac[c];

                var ret = new JPXImage(0, width, 0, height, comps, COLOR_SPACE.GRAY);

                //Todo: Copy over the alpha channel defenition data in jpx to ret
                throw new NotImplementedException();
            }
            else
            {
                comps = new ImageComp[] { gc };

                return new JPXImage(0, width, 0, height, comps, COLOR_SPACE.GRAY);
            }

            #endregion
        }

        /// <summary>
        /// Calculates a threshold
        /// </summary>
        /// <remarks>
        /// Adapted from: http://www.labbookpages.co.uk/software/imgProc/otsuThreshold.html
        /// </remarks>
        public static int OtsuThreshold(ImageComp channel)
        {
            var histogram = CreateHistogram(channel);
            int size = channel.Data.Length;

            //Calculates the total sum of all pixels added
            //together
            double sum = 0;
            for (int c = 0; c < histogram.Length; c++)
                sum += histogram[c];

            //Itterates through each level in the histogram
            //until a good threshold is found
            double sum_background = 0, maximum_variance = 0;
            int weight_background = 0, weight_foreground = 0;
            int threshold = 0;
            for (int c = 0; c < histogram.Length; c++)
            {
                //How many pixels lies on the background (white)
                //color.
                weight_background += histogram[c];

                //If there's no pixels on the background we jump
                //to the next potential threshold
                if (weight_background == 0) continue;

                //How many pixels lay on the forground for this
                //threshold
                weight_foreground = size - weight_background;

                //If no pixels lay on the forground we break,
                //and the threshold we be the last step
                if (weight_foreground == 0) break;

                //This is the sum of all all background pixels
                //times their threshold
                sum_background += c * histogram[c];

                //Calculates the mean value of the background
                double mean_background = sum_background / weight_background;

                //Calculates the mean value of the foreground
                double sum_foreground = sum - sum_background;
                double mean_foreground = sum_foreground / weight_foreground;

                //Calculates the difference between the forground and the background
                double mean = mean_background - mean_foreground;
                double between_class_variance = weight_background * weight_foreground * mean * mean;

                //Updates the treshold if a new max variance is found
                if (between_class_variance > maximum_variance)
                {
                    maximum_variance = between_class_variance;
                    threshold = c;
                }
            }

            return threshold;
        }

        /// <summary>
        /// Creates a histogram over color use in a channel
        /// </summary>
        /// <returns>Array with the number of times each color level is used</returns>
        static int[] CreateHistogram(ImageComp channel)
        {
            int max = 1 << channel.Prec;

            //Makes an array big enough to contain one entery for each level
            int[] histogram = new int[max];

            //Increments for each time a level is used
            var data = channel.Data;
            for (int c = 0; c < data.Length; c++)
                histogram[data[c]]++;

            return histogram;
        }

        public static IColorSpace ResolveJPXColorSpace(JPXImage jpx)
        {
            //Gets the colorspace out of the JPX
            switch (jpx.ColorSpace)
            {
                case COLOR_SPACE.CIELab:
                    return new JPXLAB();
                case COLOR_SPACE.CMYK:
                    return DeviceCMYK.Instance;
                case COLOR_SPACE.sRGB:
                    return DeviceRGB.Instance;
                case COLOR_SPACE.CMY:
                    return new JPXCMY();
                case COLOR_SPACE.GRAY:
                    return DeviceGray.Instance;
                case COLOR_SPACE.sYCC:
                    return new JPXYCC();
                case COLOR_SPACE.YCCK:
                    return new JPXYCCK();
                default:
                    var ncomps = jpx.NumberOfOpagueComponents;
                    if (ncomps == 1)
                        return DeviceGray.Instance;
                    else if (ncomps == 3)
                        return DeviceRGB.Instance;
                    else if (ncomps == 4)
                        return DeviceCMYK.Instance;
                    else
                        throw new PdfInternalException("Unable to get JPX colorspace");
            }
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
        public static bool IsGray(JPXImage jpx, double threshold, int ppt)
        {
            #region Fetches component data

            var comps = jpx.GetOpagueComponents();

            if (comps.Length == 0) return false;
            if (comps.Length == 1) return true;

            #endregion

            #region Checks channel size
            //If the channels are of a different size one can be
            //reasonable certain that this image isn't grayscale

            //Gets the full image resolution
            int width = jpx.Width, height = jpx.Height;

            //Precalcs prec
            var precs = new double[comps.Length];

            //Resize channels if needed
            for (int c = 0; c < comps.Length; c++)
            {
                var comp = comps[c];
                if (comp.Width != width || comp.Height != height)
                    return false;
                precs[c] = (1 << comp.Prec) - 1;
            }

            #endregion

            #region Checks the colors

            int size = comps[0].Data.Length;

            int max_ncolored = (int)(size * ppt / 1000d), ncolored = 0;

            double min, max;
            for (int c = 0; c < size; c++)
            {
                min = double.MaxValue;
                max = double.MinValue;
                for (int i = 0; i < comps.Length; i++)
                {
                    double color = comps[i].Data[c] / precs[i];
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
        /// Checks if a JPX image is grayscale.
        /// </summary>
        /// <param name="jpx">Image to check</param>
        public static bool IsGray(JPXImage jpx)
        {
            return jpx.GetOpagueComponents().Length == 1;
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
        public static bool IsGray(JPXImage jpx, byte threshold, int ppt)
        {
            return IsGray(jpx, threshold / (double)byte.MaxValue, ppt);
        }

        /// <summary>
        /// Options for compressing a PDF image to jp2
        /// </summary>
        public enum PDF2JP2Image
        {
            NONE,

            /// <summary>
            /// Turns the image into a 1BPP image
            /// </summary>
            MakeBILevel = 0x01,

            /// <summary>
            /// Removes the ICC profile
            /// </summary>
            strip_icc = 0x02,

            /// <summary>
            /// Merges colorspace into the image. Set this false if perserving the 
            /// colorspace is important (Note, this also deactivates automatic grayscaling).
            /// </summary>
            merge_cs = 0x04,

            /// <summary>
            /// Force compression even if the filesize if bigger after compression
            /// </summary>
            FORCE = 0x08
        }

        /// <summary>
        /// Options for conversion from PDF to JP2 files.
        /// </summary>
        [Flags()]
        public enum PDFtoJP2Options
        {
            /// <summary>
            /// No extra work will be done
            /// </summary>
            NONE,

            /// <summary>
            /// The decode array of the color space is applied before
            /// compressing the data.
            /// </summary>
            APPLY_DECODE = 0x01,

            /// <summary>
            /// If the image has a transparency mask, said mask will be
            /// embeded within the JPX file.
            /// </summary>
            APPLY_SMASK = 0x02,

            /// <summary>
            /// If the image use a ICC profile, it will be embeded inside
            /// the JPX container.
            /// </summary>
            EMBED_ICC = 0x04,

            /// <summary>
            /// Decode will only be applied when one have to convert
            /// the colorspace
            /// </summary>
            SELECTIV_DECODE = 0x08,

            /// <summary>
            /// For when one want the output to always be RGB. This option
            /// also converts CalGray and CalRGB colorspaces to plain RGB,
            /// but images that are already plain gray will be left gray.
            /// 
            /// To convert those images set the "force_rgb" boolean flag
            /// </summary>
            FORCE_RGB = 0x10 | APPLY_DECODE,

            /// <summary>
            /// If saving the image as a JPX to disk, this option insures that
            /// the image appeares as it would in the PDF document.
            /// </summary>
            FOR_DISK = APPLY_DECODE | APPLY_SMASK | EMBED_ICC
        }

        private static JPXImage SlowConvert(PdfImage img, bool raw_convert)
        {
            //if (img.Decode != defualt)
            //    throw new NotImplementedException
            // Since Jpeg2000 can't have decode unless ImageMask == true one have
            // to apply decode even on non-raw_convert image. Waiting a bit with this
            //
            //Unsuported colorspaces must also be handeled. 

            var comps = img.SepPixelComponents;
            var img_comps = new ImageComp[comps.Length];
            int bpp = img.BitsPerComponent;

            if (comps.Length == 4)
                CMYtoYCC(comps, bpp);

            for (int c = 0; c < img_comps.Length; c++)
                img_comps[c] = new ImageComp(bpp, bpp, false, 1, 1, img.Width, img.Height, comps[c]);

            return new JPXImage(0, img.Width, 0, img.Height, img_comps, COLOR_SPACE.GRAY);
        }

        private static void CMYtoYCC(int[][] colors, int bpp)
        {
            //Step 1. converts the CMY colors to RGB
            int max = (1 << bpp) - 1;
            for (int c = 0; c < 3; c++)
            {
                var ar = colors[c];
                for (int i = 0; i < ar.Length; i++)
                    ar[i] = max - ar[i];
            }

            var Y_ar = colors[0];
            var Cb_ar = colors[1];
            var Cr_ar = colors[2];
            max = (max + 1) / 2;

            //Step 2. converts to YCC
            for (int c = 0; c < Y_ar.Length; c++)
            {
                int R = Y_ar[c], G = Cb_ar[c], B = Cr_ar[c];
                Y_ar[c] = (int)(0.299 * R + 0.587 * G + 0.144 * B);
                Cb_ar[c] = (int)(-0.16874 * R - 0.33126 * G + 0.5 * B) + max;
                Cr_ar[c] = (int)(0.5 * R - 0.41869 * G - 0.08131 * B) + max;
            }
        }

        /// <summary>
        /// This method first convert to bgra32, then split that up.
        /// </summary>
        private static JPXImage ConvertToJPX_bgra32(PdfImage img)
        {
            return ConvertToJPX_bgra32(img.DecodeImage(), img.Width, img.Height, img.SMask != null);
        }

        private static JPXImage ConvertToJPX_bgra32(byte[] bytes, int width, int height, bool include_alpha)
        {
            int size = width * height;
            var comps = new int[4][];
            for (int c = 0; c < comps.Length; c++)
                comps[c] = new int[size];

            for (int c = 0, by = 0; c < size; c++)
            {
                comps[2][c] = bytes[by++]; //B
                comps[1][c] = bytes[by++]; //G
                comps[0][c] = bytes[by++]; //R
                comps[3][c] = bytes[by++]; //A
            }

            var img_comps = new ImageComp[include_alpha ? 4 : 3];

            for (int c = 0; c < img_comps.Length; c++)
                img_comps[c] = new ImageComp(8, 8, false, 1, 1, width, height, comps[c]);

            return new JPXImage(0, width, 0, height, img_comps, COLOR_SPACE.sRGB);
        }
    }
}
