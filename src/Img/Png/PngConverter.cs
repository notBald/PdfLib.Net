#define BitStream64 //BitStream can't handle images > 24 BPP. 
using DeflateLib;
using PdfLib.Pdf;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Primitives;
using PdfLib.Util;
using System;
using System.IO;

namespace PdfLib.Img.Png
{
    /// <summary>
    /// Converts a Pdf Image to PNG
    /// </summary>
    /// <remarks>
    /// For compression, see this document for a simple yet effective enough stratery.
    ///  - http://optipng.sourceforge.net/pngtech/optipng.html
    ///  
    /// Basically: Filter the image based on these heuristic:
    ///     1. If the image type is Palette, or the bit depth is smaller than 8, then do not filter the image (i.e. use fixed filtering, with the filter None). 
    ///     2. If the image type is Grayscale or RGB(with or without Alpha), and the bit depth is not smaller than 8, then use adaptive filtering as follows: 
    ///         - independently for each row, apply all five filters and select the filter that produces the smallest sum of absolute values per row.
    /// Compress the image using the different zlib stratergies and pick the smallest.
    ///     Z_DEFAULT_STRATEGY = 0, the default greedy search strategy. 
    ///     Z_FILTERED = 1, a strategy in which the matches are accepted only if their length is 6 or bigger.
    ///     Z_HUFFMAN_ONLY = 2, a fast strategy in which the Ziv-Lempel algorithm is entirely bypassed. 
    ///     Z_RLE = 3, a fast strategy in which the LZ77 algorithm is essentially reduced to the Run-Length Encoding algorithm.
    /// </remarks>
    public class PngConverter
    {
        public static PngImage Convert(PdfImage img, PNGCompression compression = PNGCompression.Normal)
        {
            bool ignored;
            return Convert(img, PDFtoPNGOptions.FOR_DISK, compression, out ignored);
        }

        /// <summary>
        /// Converts a PDF image into a pdf image
        /// </summary>
        /// <param name="img">PDF image to convert</param>
        /// <param name="options">Options for converting the image</param>
        /// <param name="compression">How much to compress the image</param>
        /// <param name="was_converted">True if the PDF image was converted to RGB888 format before PNG conversion</param>
        /// <returns>PNG image</returns>
        /// <remarks>
        /// The current implementation does not ever produce indexed images with transparency.
        /// The way the PNG format supports indexed transparency is by using a sepperate
        /// transparency CLUT, which is something the PDF format don't support.
        ///  - Perhaps let the user supply a transparency CLUT, or let the user set such a clut
        ///    on the resulting PNG image.
        /// 
        /// Neither is PNG images with color key produced. The PDF specs does support color key
        /// so this is an area that can be improved upon.
        /// </remarks>
        public static PngImage Convert(PdfImage img, PDFtoPNGOptions options, PNGCompression compression, out bool was_converted)
        {
            #region 0. Make compatible
            bool has_alpha = (options & PDFtoPNGOptions.EMBED_SMASK) != 0
                   && (img.SMask != null || img.Mask != null || img.SMaskInData != Pdf.Internal.PdfAlphaInData.None);

            if (!CanConvert(img, has_alpha))
            {
                img = MakePNGComatible(img);
                was_converted = true;
            }
            else was_converted = false;

            #endregion

            #region 1. Determine what colorspace to use

            var cs = img.ColorSpace;
            var bcs = cs;
            PngColorType ct = PngColorType.None;
            if (cs is CalGrayCS)
            {
                bcs = DeviceGray.Instance;
                //For now, only Gamma 1 is supported. 
            }
            else if (cs is CalRGBCS)
            {
                bcs = DeviceRGB.Instance;
                //For now, only Gamma 1 is supported.
            }
            else if (cs is ICCBased)
                bcs = ((ICCBased)cs).Alternate;
            else if (cs is IndexedCS)
            {
                bcs = ((IndexedCS)cs).Base;
                ct |= PngColorType.Palette;
                if (bcs is CalGrayCS)
                    bcs = DeviceGray.Instance;
                else if (cs is CalRGBCS)
                    bcs = DeviceRGB.Instance;
                
                if (bcs is DeviceGray)
                {
                    //Should perhaps undo the palette, but that can only be done
                    //when the palette is linar to the source. That's probably only
                    //the case for test images.
                    ct |= PngColorType.Color;
                }
            }
            if (bcs is DeviceRGB)
            {
                ct |= PngColorType.Color;
            }
            else if (bcs is DeviceGray)
            {
                //ct |= PngColorType.None;
            }
            else throw new NotSupportedException();

            #endregion

            #region 2. Applies Decode array

            int bpc = img.BitsPerComponent;
            int[][] components = img.GetSepPixelComponents(bpc);

            if ((options & PDFtoPNGOptions.APPLY_DECODE) != 0)
            {
                //Checks if the image uses the default decode array
                double[] d_dec;
                try { d_dec = cs.DefaultDecode; } catch { d_dec = null; }
                bool default_decode = d_dec != null && xRange.Compare(img.Decode, xRange.Create(cs.DefaultDecode));
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

            #endregion

            #region 3. Handle alpha
            PdfImage pdf_alpha = null;

            if (has_alpha)
            {
                var smask = img.SMask;
                if (smask != null)
                    pdf_alpha = smask;
                else
                {
                    var mask = img.Mask;
                    if (mask is int[])
                    {
                        //Do note that PNG does support color keys, but the support is a bit more limited
                        //than that of PDF so we need to do some conversion. So for now we just create a 
                        //alpha mask
                        pdf_alpha = Img.Tiff.TiffImage.CreateMask((int[])mask, img);
                    }
                    else if (mask is PdfImage)
                        pdf_alpha = (PdfImage)mask;
                }

                if (pdf_alpha != null)
                {
                    //Makes sure the alpha mask is the same size
                    pdf_alpha = PdfImage.ChangeSize(pdf_alpha, img.Width, img.Height);

                    //Makes sure the alpha mask has the same bpc, note we don't want to
                    //dither the alpha mask
                    if (pdf_alpha.BitsPerComponent != bpc)
                        pdf_alpha = PdfImage.ChangeBPC(pdf_alpha, bpc, false);

                    //Adds the alpha chanel
                    var ic = pdf_alpha.GetSepPixelComponents(bpc);
                    Array.Resize<int[]>(ref components, components.Length + 1);
                    components[components.Length - 1] = ic[0];

                    //Flags the header with alpha
                    ct |= PngColorType.Alpha;
                }
            }

            #endregion

            #region 4. Filter the image

            int width = img.Width, height = img.Height;
            int stride = (width * bpc * components.Length + 7) / 8 + 1; //The +1 is for the predicor byte
            byte[] row = new byte[stride], smallest_row = null, filtered_row = null;
            var ms = new MemoryStream(stride * height);
            var asm = new assembler(row, width, bpc, components);
            bool filter;
            if (bpc < 8 || (ct & PngColorType.Palette) != 0)
                filter = false;
            else
            {
                filter = true;
                filtered_row = new byte[stride];
                smallest_row = new byte[stride];
            }

            if (filter)
            {
                //How many positions one have to move to the left to
                //get the corresponding byte value. Note, the specs say
                //the offset is 1 for cases where the bpc is less than 8
                int sampl_offset = bpc < 8 ? 1 : (components.Length * bpc + 7) / 8;
                byte[] last_row = new byte[stride];

                for (int h = 0; h < height; h++)
                {
                    Buffer.BlockCopy(row, 1, last_row, 1, row.Length - 1);
                    asm.FillRow();

                    int rsum = sum_row(row);
                    int sum = rsum, nsum;
                    SubFilter(row, filtered_row, sampl_offset);
                    nsum = sum_row(filtered_row);
                    if (nsum < sum)
                    {
                        sum = nsum;
                        swap_row(ref filtered_row, ref smallest_row);
                    }
                    UpFilter(row, last_row, filtered_row);
                    nsum = sum_row(filtered_row);
                    if (nsum < sum)
                    {
                        sum = nsum;
                        swap_row(ref filtered_row, ref smallest_row);
                    }
                    AvgFilter(row, last_row, filtered_row, sampl_offset);
                    nsum = sum_row(filtered_row);
                    if (nsum < sum)
                    {
                        sum = nsum;
                        swap_row(ref filtered_row, ref smallest_row);
                    }
                    PaethFilter(row, last_row, filtered_row, sampl_offset);
                    nsum = sum_row(filtered_row);
                    if (nsum < sum)
                    {
                        sum = nsum;
                        swap_row(ref filtered_row, ref smallest_row);
                    }

                    if (sum < rsum)
                        ms.Write(smallest_row, 0, smallest_row.Length);
                    else
                        ms.Write(row, 0, row.Length);
                }
            }
            else
            {
                for (int h = 0; h < height; h++)
                {
                    asm.FillRow();
                    ms.Write(row, 0, row.Length);
                }
            }

            #endregion

            #region 5. Compress the data

            byte[] org_data = ms.ToArray();
            byte[] comp_data = Compress(org_data);
            if (compression >= PNGCompression.Maximum)
            {
                //if (compression == PNGCompression.Zopli)
                {
                    var output = new MemoryStream(org_data.Length);
                    output.WriteByte(0x78);
                    output.WriteByte(0x9c); //<-- Blind guess
                    var zle = new DeflateLib.Zopfli.ZopfliDeflater(output);
                    zle.NumberOfIterations = org_data.Length < 50000 ? 15 : 5;
                    zle.Deflate(org_data, true);
                    if (output.Length < comp_data.Length)
                        comp_data = output.ToArray();
                } 
            }
            //var data = new Pdf.Filter.PdfFlateFilter().Decode(comp_data, null);

            #endregion

            #region 6. Pack data into PNG

            if (cs is ICCBased icc)
            { 
                if ((options & PDFtoPNGOptions.EMBED_ICC) != 0)
                    return new PngImage(width, height, bpc, ct, comp_data, icc);
                else
                    cs = icc.Alternate;
            }

            if (cs is CalGrayCS)
            {
                return new PngImage(width, height, bpc, ct, comp_data, null, ((CalGrayCS) cs).Gamma);
            }
            if (cs is CalRGBCS)
            {
                return new PngImage(width, height, bpc, ct, comp_data, null, ((CalRGBCS)cs).Gamma.X);
            } 
            if (cs is IndexedCS)
            {
                var pal = ((IndexedCS)cs).Palette;
                int length = Math.Min(pal.Length, 256);
                byte[] palette = new byte[3 * length];
                for(int c = 0, pos = 0; c < pal.Length; c++)
                {
                    var col = pal[c];
                    palette[pos++] = (byte)(col.R * 255);
                    palette[pos++] = (byte)(col.G * 255);
                    palette[pos++] = (byte)(col.B * 255);
                }
                return new PngImage(width, height, bpc, ct | PngColorType.Palette, comp_data, palette);
            }
            
            return new PngImage(width, height, bpc, ct, comp_data);

            #endregion
        }

        private static byte[] Compress(byte[] src)
        {
            //RLE compresses. It's a simpler algorithm, but one that works
            //well with PNG images
            zDeflate zcpr = new zDeflate(zCOMPRESSION.BEST,
                zConstants.Z_DEFLATED, zConstants.MAX_WBITS,
                zConstants.DEF_MEM_LEVEL, zSTRATEGY.RLE)
            {
                InputtBuffer = src,
                OutputBuffer = new byte[src.Length],
                avail_in = src.Length,
                avail_out = src.Length
            };
            zRetCode ret = zRetCode.Z_ERRNO;

            do
            {
                if (ret == zRetCode.Z_OK)
                {
                    //We've compressed worse than org data, so we try another approach.
                    return Pdf.Filter.PdfFlateFilter.Encode(src);
                }

                ret = zcpr.Deflate(zFlush.Z_SYNC_FLUSH);
                
                if (ret != zRetCode.Z_OK)
                    throw new EndOfStreamException("Deflate error: " + zcpr.msg);

            } while (zcpr.avail_in != 0);
            if (zcpr.Deflate(zFlush.Z_FINISH) != zRetCode.Z_STREAM_END)
                throw new EndOfStreamException("Deflate error: " + zcpr.msg);

            var o = zcpr.OutputBuffer;
            Array.Resize(ref o, (int)zcpr.total_out);
            return o;
        }

        private static void SubFilter(byte[] src, byte[] dest, int offset)
        {
            dest[0] = 1;
            for (int c = 1; c <= offset; c++)
                dest[c] = src[c];
            for(int c = 1 + offset; c < src.Length; c++)
                dest[c] = unchecked((byte) (src[c] - src[c - offset]));
        }

        private static void UpFilter(byte[] src, byte[] top, byte[] dest)
        {
            dest[0] = 2;
            for (int c = 1; c < src.Length; c++)
                dest[c] = unchecked((byte) (src[c] - top[c]));
        }

        private static void AvgFilter(byte[] src, byte[] top, byte[] dest, int offset)
        {
            dest[0] = 3;
            for (int c = 1; c <= offset; c++)
                dest[c] = unchecked((byte) (src[c] - (int)(top[c] / 2)));
            for (int c = 1 + offset; c < src.Length; c++)
                dest[c] = unchecked((byte)(src[c] - (int)((src[c - offset] + top[c]) / 2)));
        }

        private static void PaethFilter(byte[] src, byte[] top, byte[] dest, int offset)
        {
            dest[0] = 4;
            for (int c = 1; c <= offset; c++)
                dest[c] = unchecked((byte)(src[c] - PaethPredictor(0, top[c], 0)));
            for (int c = 1 + offset; c < src.Length; c++)
                dest[c] = unchecked((byte)(src[c] - PaethPredictor(src[c - offset], top[c], top[c - offset])));
        }

        /// <summary>
        /// As in (9.4 Filter type 4: Paeth)
        /// </summary>
        private static byte PaethPredictor(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }

        private static void swap_row(ref byte[] r1, ref byte[] r2)
        {
            var r = r1;
            r1 = r2;
            r2 = r;
        }

        private static int sum_row(byte[] row)
        {
            int val = 0;
            for (int c= 1; c < row.Length; c++)
                val += row[c];
            return val;
        }

        private class assembler
        {
            private BitWriter _bw;
            private int _width;
            private int[][] _comps;
            private int _bpc;
            private int pos = 0;

            public assembler(byte[] buf, int w, int bpc, int[][] comps)
            {
                _bw = new BitWriter(buf);
                _width = w;
                this._comps = comps;
                _bpc = bpc;
            }

            public void FillRow()
            {
                _bw.Position = 1;

                for (int c=0; c < _width; c++)
                {
                    for (int k = 0; k < _comps.Length; k++)
                        _bw.Write(_comps[k][pos], _bpc);

                    pos++;
                }

                _bw.Flush();
            }
        }

        /// <summary>
        /// Clips the value from 0 to max
        /// </summary>
        /// <remarks>This is done since decode arrays can be invalid in various ways</remarks>
        private static int Clip(int val, int max) { return val > max ? max : val < 0 ? 0 : val; }

        private static PdfImage MakePNGComatible(PdfImage img)
        {
            if (img.SMaskInData != Pdf.Internal.PdfAlphaInData.None || img.Mask is int[])
            {
                //JPX code already handles the conversion of alpha, so we reuse it
                var jpx = JPX.PdfImageToJPX(img, JPX.PDFtoJP2Options.FOR_DISK | JPX.PDFtoJP2Options.FORCE_RGB);

                //Now we need to convert it back to a PdfImage. Since we don't want to compress it to a J2K image,
                //we convert it to an uncompressed bmp file.
                var ms = new MemoryStream(jpx.Width * jpx.Height * 4 + 1024);
                jpx.ConvertToBMP(ms, null);

                //Then we convert the BMP image to a PdfImage.
                ms.Position = 0;
                img = PdfImage.CreateFromBMPData(Bmp.Open(ms));
            }
            else
            {
                var cimg = PdfImage.CreateFromBGRA32(img.DecodeImage(), img.Height, img.Width, img.Width * 4, false);

                //Copies over image masks. Since these don't depend on the underlying image, we can let the later code
                //handle it. 
                if (img.SMask != null)
                    cimg.SMask = img.SMask;
                //Mask is ignored when there's an smask.
                else if (img.Mask != null)
                    cimg.Mask = img.Mask;


                img = cimg;
            }

            return img;
        }

        /// <summary>
        /// Checks if the image can be converted to PNG
        /// </summary>
        /// <param name="img">Image to convert to PNG</param>
        /// <returns>If this image can be converted</returns>
        private static bool CanConvert(PdfImage img, bool has_alpha)
        {
            if (img.HasColorSpace)
            {
                //PNG does not support premultiplied alpha
                if (has_alpha && img.Matte != null)
                    return false;

                var cs = img.ColorSpace;
                if (cs is CalGrayCS)
                    cs = DeviceGray.Instance;
                else if (cs is CalRGBCS)
                {
                    var crgb = ((CalRGBCS)cs).Gamma;
                    if (crgb.X == crgb.Y && crgb.Y == crgb.Z)
                        cs = DeviceRGB.Instance;
                }
                else if (cs is IndexedCS)
                {
                    cs = ((IndexedCS)cs).Base;

                    //Png does not support alpha on indexed images
                    if (has_alpha) return false;
                }
                else if (cs is ICCBased)
                    cs = ((ICCBased)cs).Alternate;
                
                //Perhaps CalRGB and CalGray should be allowed through too, check out the cHRM chunk. 
                switch (img.BitsPerComponent)
                {
                    case 1:
                    case 2:
                    case 4:
                        return cs is DeviceGray || img.ColorSpace is IndexedCS && cs is DeviceRGB;

                    case 8:
                        return cs is DeviceGray || cs is DeviceRGB;

                    case 16:
                        return (cs is DeviceGray || cs is DeviceRGB) && !(img.ColorSpace is IndexedCS);
                }
            }
            return false;
        }
    }
}
