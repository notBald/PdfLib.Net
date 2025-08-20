#define BitStream64 //BitStream can't handle images > 24 BPP. 
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using PdfLib.Encryption;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Compose;

namespace PdfLib.Util
{
    /// <summary>
    /// Simple PNG parser
    /// </summary>
    /// <remarks>
    /// Handles all BBP formats
    ///  1, 2, 4, 8, 16, 24, 32, 48 and 64 bits per pixel.
    ///  
    ///  * 64 BPP works becase:
    ///   - It runs on the Adam7 byte path
    ///   - When extract alpha, at most 48 bits are read, which
    ///     is below the 56 bit max of BitStream64
    /// </remarks>
    public class Png
    {
        private List<Chunk> _chunks;
        IHDR _header;
        IDAT _data;

        /// <summary>
        /// If this image has a palette
        /// </summary>
        public bool Indexed { get { return (_header.ColorType & ColorType.Palette) != 0; } }

        /// <summary>
        /// If the data is stored in Adam7 order
        /// </summary>
        public bool Interlaced { get { return _header.Interlace == InterlaceType.Adam7; } }

        /// <summary>
        /// If there's an alpha channel in the data
        /// </summary>
        public bool HasAlpha { get { return (_header.ColorType & ColorType.Alpha) != 0; } }

        /// <summary>
        /// If this image is RGB or Grayscale
        /// </summary>
        public bool HasColor { get { return (_header.ColorType & ColorType.Color) != 0; } }

        /// <summary>
        /// Bits per color component
        /// </summary>
        public int BitsPerComponent { get { return _header.BitDepth; } }

        /// <summary>
        /// Width of the image in pixels
        /// </summary>
        public int Width { get { return _header.Width; } }

        /// <summary>
        /// Height of the image in pixels
        /// </summary>
        public int Height { get { return _header.Height; } }

        /// <summary>
        /// Gamma value of the image
        /// </summary>
        public double? Gamma
        {
            get
            {
                var gamma = (gAMA) GetChunk("gAMA");
                if (gamma == null) return null;
                return gamma.Gamma;
            }
        }

        /// <summary>
        /// Number of color components, including alpha
        /// </summary>
        public int NComponents
        {
            get
            {
                if (Indexed) return 1;

                if ((_header.ColorType & ColorType.Color) != 0)
                {
                    if ((_header.ColorType & ColorType.Alpha) != 0)
                        return 4;
                    return 3;
                }
                else
                {
                    if ((_header.ColorType & ColorType.Alpha) != 0)
                        return 2;
                    return 1;
                }
            }
        }

        /// <summary>
        /// How many bits there are in a single pixel. All pixels are of this size.
        /// </summary>
        public int BitsPerPixel { get { return BitsPerComponent * NComponents; } }

        /// <summary>
        /// The compressed image data
        /// </summary>
        public byte[] RawData { get { return _data.Data; } }

        /// <summary>
        /// Transparent colors.
        /// </summary>
        public ushort[] ColorKeys
        {
            get
            {
                var ck = GetChunk("tRNS") as tRNS_ck;
                if (ck == null) return null;
                return ck.Keys;
            }
            set
            {
                var ck = GetChunk("tRNS");
                if (ck != null)
                    _chunks.Remove(ck);
                ck = new tRNS_ck(value);
                _chunks.Add(ck);
            }
        }

        /// <summary>
        /// Transparent colors for the palette
        /// </summary>
        public byte[] AlphaPalette
        {
            get
            {
                var ck = GetChunk("tRNS") as tRNS_pal;
                if (ck == null) return null;
                return ck.Palette;
            }
        }

        /// <summary>
        /// Byte values of the palette
        /// </summary>
        /// <remarks>PDF supports this palette format, so there's no need to transform it</remarks>
        public byte[] RawPaletteData 
        { 
            get 
            {
                var clut = (PLTE) GetChunk("PLTE");
                if (clut == null)
                    return null;
                return clut.RawData;
            }
        }

        /// <summary>
        /// Non-gamma corrected color space
        /// </summary>
        public IColorSpace ColorSpace
        {
            get
            {
                if (Indexed)
                    return new IndexedCS(RawPaletteData);
                if (HasColor)
                    return DeviceRGB.Instance;
                return DeviceGray.Instance;
            }
        }

        /// <summary>
        /// Creates a lookup table that converts raw image data "ushorts",
        /// interpolated into the decode range.
        /// </summary>
        /// <returns>The lookup table</returns>
        internal double[,] DecodeLookupTable
        {
            get
            {
                var cs = ColorSpace;
                var bpc = BitsPerComponent;
                var def_decode = cs is IndexedCS ? ((IndexedCS)cs).GetDefaultDecode(bpc) : cs.DefaultDecode;
                return PdfLib.Pdf.PdfImage.CreateDLT(Pdf.Primitives.xRange.Create(def_decode), 0, bpc, cs.NComponents);
            }
        }

        /// <summary>
        /// Backround color as an array of doubles
        /// </summary>
        public double[] BackgroundAr
        {
            get
            {
                var bk = GetChunk("bKGD") as bKGD;
                if (bk == null) return null;

                //Creates an array large enough to hold one pixel.
                var pixel = new double[ColorSpace.NComponents];
                var bck = bk.Color;
                var decode = DecodeLookupTable;
                for (int c = 0; c < pixel.Length; c++)
                    pixel[c] = decode[c, bck[c]];

                return pixel;
            }
        }

        /// <summary>
        /// Background color as a compose color
        /// </summary>
        public Compose.cColor Background
        {
            get
            {
                var pixel = BackgroundAr;
                if (pixel == null) return null;
                var col = ColorSpace.Converter.MakeColor(pixel);
                return new cColor(col);
            }
        }

        /// <summary>
        /// If all the chunks in this PNG file is valid
        /// </summary>
        public bool Valid
        {
            get
            {
                foreach (var chunk in _chunks)
                    if (!chunk.Valid)
                        return false;
                if (_header == null || _data == null)
                    return false;
                if (Indexed && GetChunk("PLTE") == null)
                    return false;
                return true;
            }
        }

        #region Init

        private Png(IHDR header, IDAT data, List<Chunk> chunks)
        {
            _chunks = chunks;
            _header = header;
            _data = data;
        }

        #endregion

        /// <summary>
        /// Decodes the image data
        /// </summary>
        /// <returns>True if data was decoded</returns>
        public byte[] Decode()
        {
            if (!Valid) return null;

            var data = RawData;
            data = new Pdf.Filter.PdfFlateFilter().Decode(data, null);
            if (Interlaced)
                data = BitsPerPixel % 8 == 0 ? DeAdam7Byte(data) : DeAdam7(data);
            else
                data = Pdf.Filter.PNGPredictor.Recon(new MemoryStream(data), NComponents, BitsPerComponent, Width);
                
            return data;
        }

        private byte[] DeAdam7Byte(byte[] data)
        {
            #region Init

            int w = Width, h = Height;

            //Bytes per pixel
            int bits_pp = BitsPerPixel, bpp = bits_pp / 8;

            //Adam 7 divides the image into 8x8 blocks. This is the
            //number of blocks in horz/vert directions. 
            int pass_width = (w + 7) / 8, pass_height = (h + 7) / 8;

            //Stride of the full image
            int stride = bpp * w;

            //New data
            var nd = new byte[stride * h];

            //New data bit writer
            var nd_bw = new CopyStream(nd);

            //Pass data bit reader
            var pd_br = new CopyStream(data);

            //Reads from the source data
            CopyStream source_data = new CopyStream(data);

            //For when images aren't a multiple of 8.
            int mod_w = w % 8, mod_h = h % 8;
            int col_start = mod_w == 0 ? 0 : 1;
            int row_start = mod_h == 0 ? 0 : 1;

            #endregion

            #region Pass 1

            //Pass 1 (1 pixel, row 1)

            #region Init pass 1

            int pass_stride = bpp * pass_width;

            #endregion

            #region Read and filter pass 1 data

            Recon(source_data, pass_stride, bits_pp, pass_height);

            #endregion

            #region Write pass 1 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 7);
                }

                if (mod_w > 0)
                {
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * (7 - (8 - mod_w)));
                }

                nd_bw.Skip(stride * 7);
            }//*/

            if (mod_h > 0)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 7);
                }

                if (mod_w > 0)
                {
                    nd_bw.Write(pd_br, bpp);
                }
            }

            #endregion

            #endregion

            #region Pass 2

            //Pass 2 (1 pixel, row 1)

            #region Init pass 2

            //Has the same stride as pass 1
            if (mod_w > 0)
                pass_stride = bpp * ((pass_width - 1) + (mod_w < 5 ? 0 : 1));

            pd_br.Reset();
            nd_bw.Reset();

            //New data bit reader
            var nd_br = new CopyStream(nd);

            #endregion

            #region Read and filter pass 2 data

            Recon(source_data, pass_stride, bits_pp, pass_height);

            #endregion

            #region Write pass 2 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br, bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 3);

                    //Writes the pass 2 data
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 4);
                }

                if (mod_w > 0)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br, bpp);

                    if (mod_w > 1)
                    {
                        int ww = mod_w - 1;
                        int skip = Math.Min(3, ww);
                        nd_bw.Skip(bpp * skip);
                        nd_br.Skip(bpp * skip);
                        ww -= 3;

                        if (ww > 0)
                        {
                            //Writes the pass 2 data
                            nd_bw.Write(pd_br, bpp);
                            nd_br.Skip(bpp);
                            if (--ww > 0)
                            {
                                skip = Math.Min(3, ww);
                                nd_bw.Skip(bpp * skip);
                                nd_br.Skip(bpp * skip);
                            }
                        }
                    }
                }

                nd_bw.Skip(stride * 7);
                nd_br.Skip(stride * 7);
            }

            if (mod_h > 0)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br, bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 3);

                    //Writes the pass 2 data
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 4);
                }

                if (mod_w > 4)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br, bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 3);

                    //Writes the pass 2 data
                    nd_bw.Write(pd_br, bpp);
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 3

            //Pass 3 (2 pixels row 5)

            #region Init pass 3

            if (mod_w > 0)
                pass_stride = bpp * 2 * (pass_width - 1) + (1 + (mod_w > 4 ? 1 : 0)) * bpp;
            else
                pass_stride = bpp * 2 * pass_width;
            pd_br.Reset();
            nd_bw.Reset();

            #endregion

            #region Read and filter pass 3 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            //Since this 8x8 only has data on row 5, we'll have to check if
            //the last 8x8 is to be included.
            int sub_height = (mod_h != 0 && mod_h < 5) ? 1 : 0;

            Recon(source_data, pass_stride, bits_pp, pass_height - sub_height);

            #endregion

            #region Write pass 3 data

            for (int row = row_start; row < pass_height; row++)
            {
                nd_bw.Skip(stride * 4);

                for (int column = col_start; column < pass_width; column++)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 3);

                    //Writes second pixel
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 3);
                }

                if (mod_w > 0)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br, bpp);
                    int ww = mod_w - 1;
                    if (ww > 0)
                    {
                        int skip = Math.Min(3, ww);
                        nd_bw.Skip(bpp * skip);
                        ww -= 3;
                        if (ww > 0)
                        {
                            //Writes second pixel
                            nd_bw.Write(pd_br, bpp);
                            if (--ww > 0)
                            {
                                skip = Math.Min(3, ww);
                                nd_bw.Skip(bpp * skip);
                            }
                        }
                    }
                }

                nd_bw.Skip(stride * 3);
            }

            if (mod_h >= 5)
            {
                nd_bw.Skip(stride * 4);

                for (int column = col_start; column < pass_width; column++)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 3);

                    //Writes second pixel
                    nd_bw.Write(pd_br, bpp);
                    nd_bw.Skip(bpp * 3);
                }

                if (mod_w > 0)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br, bpp);
                    int ww = mod_w - 1;
                    if (ww > 0)
                    {
                        int skip = Math.Min(3, ww);
                        nd_bw.Skip(bpp * skip);
                        ww -= 3;
                        if (ww > 0)
                        {
                            //Writes second pixel
                            nd_bw.Write(pd_br, bpp);
                        }
                    }
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 4

            //Pass 4 (2 pixels on row 1, and 2 pixels on row 5)

            #region Init pass 4

            //Has same stride as pass 3
            if (mod_w > 0)
                pass_stride = bpp * (2 * (pass_width - 1) + (mod_w < 3 ? 0 : mod_w < 7 ? 1 : 2));

            pd_br.Reset();
            nd_bw.Reset();
            nd_br.Reset();

            #endregion

            #region Read and filter pass 4 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            //Since this 8x8 only may not have data on row 5, we'll have to check if
            //it's to be included.
            if (mod_h != 0)
                sub_height = (mod_h < 5) ? 1 : 0;

            Recon(source_data, pass_stride, bits_pp, pass_height * 2 - sub_height);

            #endregion

            #region Write pass 4 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 2; c++)
                {
                    //Writes to row 1 and 5
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            //Reads and writes the pass 1 or 3 data
                            nd_bw.Write(nd_br, bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp);

                            //Writes pass 4 data, first pixel then second pixel
                            nd_bw.Write(pd_br, bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp * 2);
                        }
                    }

                    //Writes out the less than 8 width box.
                    for (int k = mod_w; k > 0; k--)
                    {
                        //Reads and writes the pass 1 or 3 data
                        nd_bw.Write(nd_br, bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);

                        //Writes pass 4 data, first pixel then second pixel
                        if (--k == 0) break;
                        nd_bw.Write(pd_br, bpp);
                        nd_br.Skip(bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);
                    }

                    nd_bw.Skip(stride * 3);
                    nd_br.Skip(stride * 3);
                }
            }

            if (mod_h > 0)
            {
                for (int c = 0, row_skip = mod_h; c < 2; c++)
                {
                    //Writes to row 1 and 5
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            //Reads and writes the pass 1 or 3 data
                            nd_bw.Write(nd_br, bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp);

                            //Writes pass 4 data, first pixel then second pixel
                            nd_bw.Write(pd_br, bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp * 2);
                        }
                    }

                    //Writes out the less than 8 width box.
                    for (int k = mod_w; k > 0; k--)
                    {
                        //Reads and writes the pass 1 or 3 data
                        nd_bw.Write(nd_br, bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);

                        //Writes pass 4 data, first pixel then second pixel
                        if (--k == 0) break;
                        nd_bw.Write(pd_br, bpp);
                        nd_br.Skip(bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);
                    }

                    row_skip--;

                    if (row_skip < 4)
                        break;

                    nd_bw.Skip(stride * 3);
                    nd_br.Skip(stride * 3);
                    row_skip -= 3;
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 5

            //Pass 5 (4 pixels on row 3, and 4 pixels on row 7)

            #region Init pass 5

            if (mod_w > 0)
                pass_stride = bpp * 4 * (pass_width - 1) + ((int)((mod_w + 1) / 2)) * bpp;
            else
                pass_stride = bpp * 4 * pass_width;
            pd_br.Reset();
            nd_bw.Reset();

            #endregion

            #region Read and filter pass 5 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            //Since this 8x8 only may not have data on row 3,7, we'll have to check if
            //they are to be included.
            if (mod_h != 0)
                sub_height = (mod_h < 3) ? 2 : (mod_h < 7) ? 1 : 0;

            Recon(source_data, pass_stride, bits_pp, pass_height * 2 - sub_height);

            #endregion

            #region Write pass 5 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 2; c++)
                {
                    //c=0: Skips row 1 and 2
                    //c=1: Skips row 5 and 6
                    nd_bw.Skip(stride * 2);

                    //c=0: Writes to row 3
                    //c=1: Writes to row 7
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            //Writes pass 5 data, first, third, fifth and seventh pixel
                            nd_bw.Write(pd_br, bpp);
                            nd_bw.Skip(bpp);
                        }
                    }

                    for (int k = mod_w; k > 0; k--)
                    {
                        //Writes pass 5 data, first, third, fifth and seventh pixel
                        nd_bw.Write(pd_br, bpp);
                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                    }

                    //c=0: Skips row 4
                    //c=1: Skips row 8
                    nd_bw.Skip(stride * 1);
                }
            }

            for (int c = mod_h; c > 2; c -= 4)
            {
                nd_bw.Skip(stride * 2);

                for (int column = col_start; column < pass_width; column++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        //Writes pass 5 data, first, third, fifth and seventh pixel
                        nd_bw.Write(pd_br, bpp);
                        nd_bw.Skip(bpp);
                    }
                }

                for (int k = mod_w; k > 0; k--)
                {
                    //Writes pass 5 data, first, third, fifth and seventh pixel
                    nd_bw.Write(pd_br, bpp);
                    if (--k == 0) break;
                    nd_bw.Skip(bpp);
                }

                if (c < 7) break;

                nd_bw.Skip(stride * 1);
            }

            #endregion
            //*/
            #endregion

            #region Pass 6

            //Pass 6 (4 pixels on row 1, 3, 5, 7)

            #region Init pass 6

            if (mod_w > 0)
                pass_stride = bpp * 4 * (pass_width - 1) + ((int)mod_w / 2) * bpp;

            pd_br.Reset();
            nd_bw.Reset();
            nd_br.Reset();

            #endregion

            #region Read and filter pass 6 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            if (mod_h != 0 && mod_h != 7)
                sub_height = (mod_h < 3) ? 3 : (mod_h < 5) ? 2 : 1;

            Recon(source_data, pass_stride, bits_pp, pass_height * 4 - sub_height);

            #endregion

            #region Write pass 6 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 4; c++)
                {
                    //c=0: Writes to row 1
                    //c=1: Writes to row 3
                    //c=2: Writes to row 5
                    //c=3: Writes to row 7
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            //Reads and writes the existing pass 5 data
                            nd_bw.Write(nd_br, bpp);

                            //Writes pass 6 pixel
                            nd_bw.Write(pd_br, bpp);
                            nd_br.Skip(bpp);
                        }
                    }

                    for (int k = mod_w; k > 0; k--)
                    {
                        //Reads and writes the existing pass 5 data
                        nd_bw.Write(nd_br, bpp);
                        if (--k == 0) break;

                        //Writes pass 6 pixel
                        nd_bw.Write(pd_br, bpp);
                        nd_br.Skip(bpp);
                    }

                    //c=0: Skips row 2
                    //c=1: Skips to row 4
                    //c=2: Skips to row 6
                    //c=3: Skips to row 8
                    nd_bw.Skip(stride * 1);
                    nd_br.Skip(stride * 1);
                }
            }

            for (int c = mod_h; c > 0; c--)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        //Reads and writes the existing pass 5 data
                        nd_bw.Write(nd_br, bpp);

                        //Writes pass 6 pixel
                        nd_bw.Write(pd_br, bpp);
                        nd_br.Skip(bpp);
                    }
                }

                for (int k = mod_w; k > 0; k--)
                {
                    //Reads and writes the existing pass 5 data
                    nd_bw.Write(nd_br, bpp);
                    if (--k == 0) break;

                    //Writes pass 6 pixel
                    nd_bw.Write(pd_br, bpp);
                    nd_br.Skip(bpp);
                }

                if (--c > 0)
                {
                    nd_bw.Skip(stride * 1);
                    nd_br.Skip(stride * 1);
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 7

            //Pass 7 (8 pixels on row 2, 4, 6, 8)

            #region Init pass 7

            pass_stride = stride;
            pd_br.Reset();
            nd_bw.Reset();

            #endregion

            #region Read and filter pass 7 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            if (mod_h != 0)
                sub_height = (mod_h < 2) ? 4 : (mod_h < 4) ? 3 : (mod_h < 6) ? 2 : 1;

            Recon(source_data, pass_stride, bits_pp, pass_height * 4 - sub_height);

            #endregion

            #region Write pass 7 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 4; c++)
                {
                    //c=0: Skips row 1
                    //c=1: Skips row 3
                    //c=2: Skips row 5
                    //c=3: Skips row 7
                    nd_bw.Skip(stride * 1);

                    //c=0: Writes to row 2
                    //c=1: Writes to row 4
                    //c=2: Writes to row 6
                    //c=3: Writes to row 8
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            //Writes pass 7 pixel
                            nd_bw.Write(pd_br, bpp);
                        }
                    }

                    //Writes out right block if it's not 8 in width
                    for (int k = 0; k < mod_w; k++)
                    {
                        //Writes pass 7 pixel
                        nd_bw.Write(pd_br, bpp);
                    }
                }
            }

            //Writes out bottom block if it's not 8 rows in height
            for (int c = mod_h; c > 0; c--)
            {
                nd_bw.Skip(stride * 1);
                if (--c == 0) break;

                for (int column = col_start; column < pass_width; column++)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        //Writes pass 7 pixel
                        nd_bw.Write(pd_br, bpp);
                    }
                }

                //Writes out right block if it's not 8 in width
                for (int k = 0; k < mod_w; k++)
                {
                    //Writes pass 7 pixel
                    nd_bw.Write(pd_br, bpp);
                }
            }

            #endregion
            //*/
            #endregion

            return nd;
        }

        /// <summary>
        /// Adam7 splits the image into 8x8 blocks, then transmits parts of each block
        /// over seven passes
        /// 
        /// Block structure:
        /// 
        /// 1 6 4 6 2 6 4 6
        /// 7 7 7 7 7 7 7 7
        /// 5 6 5 6 5 6 5 6
        /// 7 7 7 7 7 7 7 7
        /// 3 6 4 6 3 6 4 6
        /// 7 7 7 7 7 7 7 7
        /// 5 6 5 6 5 6 5 6
        /// 7 7 7 7 7 7 7 7
        /// 
        /// </summary>
        private byte[] DeAdam7(byte[] data)
        {
            #region Init

            int w = Width, h = Height, bpp = BitsPerPixel;

            //Adam 7 divides the image into 8x8 blocks. This is the
            //number of blocks in horz/vert directions. 
            int pass_width = (w + 7) / 8, pass_height = (h + 7) / 8;

            //Stride of the full image
            int stride = (bpp * w + 7) / 8;

            //New data
            var nd = new byte[stride * h];

            //Reads from the source data
            CopyStream source_data = new CopyStream(data);
            
            //New data bit writer
            var nd_bw = new Util.BitWriter(nd);

            //Pass data bit reader
#if BitStream64
            var pd_br = new BitStream64(data);
#else
            var pd_br = new BitStream(data);
#endif

            //For when images aren't a multiple of 8.
            int mod_w = w % 8, mod_h = h % 8;
            int col_start = mod_w == 0 ? 0 : 1;
            int row_start = mod_h == 0 ? 0 : 1;

            #endregion

            #region Pass 1

            //Pass 1 (1 pixel, row 1)

            #region Init pass 1

            int pass_stride = (bpp * pass_width + 7) / 8;

            #endregion

            #region Read and filter pass 1 data

            Recon(source_data, pass_stride, bpp, pass_height);

            #endregion

            #region Write pass 1 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 7);
                }

                if (mod_w > 0)
                {
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * (7 - (8 - mod_w)));
                }

                pd_br.ByteAlign();
                nd_bw.Seek(stride * 7);
            }//*/

            if (mod_h > 0)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 7);
                }

                if (mod_w > 0)
                {
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                }
            }

            #endregion

            #endregion

            #region Pass 2

            //Pass 2 (1 pixel, row 1)
            
            #region Init pass 2

            //Has the same stride as pass 1
            if (mod_w > 0)
                pass_stride = (bpp * ((pass_width - 1) + (mod_w < 5 ? 0 : 1)) + 7) / 8;

            pd_br.Reset();
            nd_bw.Reset();

            //New data bit reader
#if BitStream64
            var nd_br = new Util.BitStream64(nd);
#else
            var nd_br = new Util.BitStream(nd);
#endif

            #endregion

            #region Read and filter pass 2 data

            Recon(source_data, pass_stride, bpp, pass_height);

            #endregion

            #region Write pass 2 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 3);

                    //Writes the pass 2 data
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 4);
                }

                if (mod_w > 0)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br.FetchBits(bpp), bpp);

                    if (mod_w > 1)
                    {
                        int ww = mod_w - 1;
                        int skip = Math.Min(3, ww);
                        nd_bw.Skip(bpp * skip);
                        nd_br.Skip(bpp * skip);
                        ww -= 3;

                        if (ww > 0)
                        {
                            //Writes the pass 2 data
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                            nd_br.Skip(bpp);
                            if (--ww > 0)
                            {
                                skip = Math.Min(3, ww);
                                nd_bw.Skip(bpp * skip);
                                nd_br.Skip(bpp * skip);
                            }
                        }
                    }
                }

                pd_br.ByteAlign();
                nd_bw.Seek(stride * 7);
                nd_br.ByteAlign();

                nd_br.Skip(stride * 8 * 7);
            }

            if (mod_h > 0)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 3);

                    //Writes the pass 2 data
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 4);
                }

                if (mod_w > 4)
                {
                    //Reads and writes the pass 1 data
                    nd_bw.Write(nd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);
                    nd_br.Skip(bpp * 3);

                    //Writes the pass 2 data
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 3

            //Pass 3 (2 pixels row 5)
            
            #region Init pass 3

            if (mod_w > 0)
                pass_stride = (bpp * 2 * (pass_width - 1) + (1 + (mod_w > 4 ? 1 : 0)) * bpp + 7) / 8;
            else
                pass_stride = (bpp * 2 * pass_width + 7) / 8;
            pd_br.Reset();
            nd_bw.Reset();

            #endregion

            #region Read and filter pass 3 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            //Since this 8x8 only has data on row 5, we'll have to check if
            //the last 8x8 is to be included.
            int sub_height = (mod_h != 0 && mod_h < 5) ? 1 : 0;

            Recon(source_data, pass_stride, bpp, pass_height - sub_height);

            #endregion

            #region Write pass 3 data

            for (int row = row_start; row < pass_height; row++)
            {
                nd_bw.Seek(stride * 4);

                for (int column = col_start; column < pass_width; column++)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);

                    //Writes second pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);
                }

                if (mod_w > 0)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    int ww = mod_w - 1;
                    if (ww > 0)
                    {
                        int skip = Math.Min(3, ww);
                        nd_bw.Skip(bpp * skip);
                        ww -= 3;
                        if (ww > 0)
                        {
                            //Writes second pixel
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                            if (--ww > 0)
                            {
                                skip = Math.Min(3, ww);
                                nd_bw.Skip(bpp * skip);
                            }
                        }
                    }
                }

                pd_br.ByteAlign();
                nd_bw.Seek(stride * 3);
            }

            if (mod_h >= 5)
            {
                nd_bw.Seek(stride * 4);

                for (int column = col_start; column < pass_width; column++)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);

                    //Writes second pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_bw.Skip(bpp * 3);
                }

                if (mod_w > 0)
                {
                    //Writes first pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    int ww = mod_w - 1;
                    if (ww > 0)
                    {
                        int skip = Math.Min(3, ww);
                        nd_bw.Skip(bpp * skip);
                        ww -= 3;
                        if (ww > 0)
                        {
                            //Writes second pixel
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        }
                    }
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 4

            //Pass 4 (2 pixels on row 1, and 2 pixels on row 5)
            
            #region Init pass 4

            //Has same stride as pass 3
            if (mod_w > 0)
                pass_stride = (bpp * (2 * (pass_width - 1) + (mod_w < 3 ? 0 : mod_w < 7 ? 1 : 2)) + 7) / 8;

            pd_br.Reset();
            nd_bw.Reset();
            nd_br.Reset();

            #endregion

            #region Read and filter pass 4 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            //Since this 8x8 only may not have data on row 5, we'll have to check if
            //it's to be included.
            if (mod_h != 0)
                sub_height = (mod_h < 5) ? 1 : 0;

            Recon(source_data, pass_stride, bpp, pass_height * 2 - sub_height);

            #endregion

            #region Write pass 4 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 2; c++)
                {
                    //Writes to row 1 and 5
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            //Reads and writes the pass 1 or 3 data
                            nd_bw.Write(nd_br.FetchBits(bpp), bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp);

                            //Writes pass 4 data, first pixel then second pixel
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp * 2);
                        }
                    }

                    //Writes out the less than 8 width box.
                    for (int k = mod_w; k > 0; k--)
                    {
                        //Reads and writes the pass 1 or 3 data
                        nd_bw.Write(nd_br.FetchBits(bpp), bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);

                        //Writes pass 4 data, first pixel then second pixel
                        if (--k == 0) break;
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        nd_br.Skip(bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);
                    }

                    pd_br.ByteAlign();
                    nd_bw.Align();
                    nd_br.ByteAlign();

                    nd_bw.Seek(stride * 3);
                    nd_br.Skip(stride * 8 * 3);
                }
            }

            if (mod_h > 0)
            {
                for (int c = 0, row_skip = mod_h; c < 2; c++)
                {
                    //Writes to row 1 and 5
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            //Reads and writes the pass 1 or 3 data
                            nd_bw.Write(nd_br.FetchBits(bpp), bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp);

                            //Writes pass 4 data, first pixel then second pixel
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                            nd_bw.Skip(bpp);
                            nd_br.Skip(bpp * 2);
                        }
                    }

                    //Writes out the less than 8 width box.
                    for (int k = mod_w; k > 0; k--)
                    {
                        //Reads and writes the pass 1 or 3 data
                        nd_bw.Write(nd_br.FetchBits(bpp), bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);

                        //Writes pass 4 data, first pixel then second pixel
                        if (--k == 0) break;
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        nd_br.Skip(bpp);

                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                        nd_br.Skip(bpp);
                    }

                    row_skip--;
                    pd_br.ByteAlign();
                    nd_bw.Align();
                    nd_br.ByteAlign();

                    if (row_skip < 4)
                        break;

                    nd_bw.Seek(stride * 3);
                    nd_br.Skip(stride * 8 * 3);
                    row_skip -= 3;
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 5

            //Pass 5 (4 pixels on row 3, and 4 pixels on row 7)
            
            #region Init pass 5

            if (mod_w > 0)
                pass_stride = (bpp * 4 * (pass_width - 1) + ((int) ((mod_w + 1) / 2)) * bpp + 7) / 8;
            else
                pass_stride = (bpp * 4 * pass_width + 7) / 8;
            pd_br.Reset();
            nd_bw.Reset();

            #endregion

            #region Read and filter pass 5 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            //Since this 8x8 only may not have data on row 3,7, we'll have to check if
            //they are to be included.
            if (mod_h != 0)
                sub_height = (mod_h < 3) ? 2 : (mod_h < 7) ? 1 : 0;

            Recon(source_data, pass_stride, bpp, pass_height * 2 - sub_height);

            #endregion

            #region Write pass 5 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 2; c++)
                {
                    //c=0: Skips row 1 and 2
                    //c=1: Skips row 5 and 6
                    nd_bw.Seek(stride * 2);

                    //c=0: Writes to row 3
                    //c=1: Writes to row 7
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            //Writes pass 5 data, first, third, fifth and seventh pixel
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                            nd_bw.Skip(bpp);
                        }
                    }

                    for (int k = mod_w; k > 0; k--)
                    {
                        //Writes pass 5 data, first, third, fifth and seventh pixel
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        if (--k == 0) break;
                        nd_bw.Skip(bpp);
                    }

                    pd_br.ByteAlign();
                    nd_bw.Align();

                    //c=0: Skips row 4
                    //c=1: Skips row 8
                    nd_bw.Seek(stride * 1);
                }
            }

            for (int c = mod_h; c > 2; c -= 4)
            {
                nd_bw.Seek(stride * 2);

                for (int column = col_start; column < pass_width; column++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        //Writes pass 5 data, first, third, fifth and seventh pixel
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        nd_bw.Skip(bpp);
                    }
                }

                for (int k = mod_w; k > 0; k--)
                {
                    //Writes pass 5 data, first, third, fifth and seventh pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    if (--k == 0) break;
                    nd_bw.Skip(bpp);
                }

                if (c < 7) break;

                pd_br.ByteAlign();
                nd_bw.Align();
                
                nd_bw.Seek(stride * 1);
            }

            #endregion
            //*/
            #endregion

            #region Pass 6

            //Pass 6 (4 pixels on row 1, 3, 5, 7)
            
            #region Init pass 6

            if (mod_w > 0)
                pass_stride = (bpp * 4 * (pass_width - 1) + ((int) mod_w / 2) * bpp + 7) / 8;

            pd_br.Reset();
            nd_bw.Reset();
            nd_br.Reset();

            #endregion

            #region Read and filter pass 6 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            if (mod_h != 0 && mod_h != 7)
                sub_height = (mod_h < 3) ? 3 : (mod_h < 5) ? 2 : 1;

            Recon(source_data, pass_stride, bpp, pass_height * 4 - sub_height);

            #endregion

            #region Write pass 6 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 4; c++)
                {
                    //c=0: Writes to row 1
                    //c=1: Writes to row 3
                    //c=2: Writes to row 5
                    //c=3: Writes to row 7
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            //Reads and writes the existing pass 5 data
                            nd_bw.Write(nd_br.FetchBits(bpp), bpp);

                            //Writes pass 6 pixel
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                            nd_br.Skip(bpp);
                        }
                    }

                    for (int k = mod_w; k > 0; k--)
                    {
                        //Reads and writes the existing pass 5 data
                        nd_bw.Write(nd_br.FetchBits(bpp), bpp);
                        if (--k == 0) break;

                        //Writes pass 6 pixel
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        nd_br.Skip(bpp);
                    }

                    pd_br.ByteAlign();
                    nd_bw.Align();
                    nd_br.ByteAlign();

                    //c=0: Skips row 2
                    //c=1: Skips to row 4
                    //c=2: Skips to row 6
                    //c=3: Skips to row 8
                    nd_bw.Seek(stride * 1);
                    nd_br.Skip(stride * 8 * 1);
                }
            }

            for (int c = mod_h; c > 0; c--)
            {
                for (int column = col_start; column < pass_width; column++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        //Reads and writes the existing pass 5 data
                        nd_bw.Write(nd_br.FetchBits(bpp), bpp);

                        //Writes pass 6 pixel
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        nd_br.Skip(bpp);
                    }
                }

                for (int k = mod_w; k > 0; k--)
                {
                    //Reads and writes the existing pass 5 data
                    nd_bw.Write(nd_br.FetchBits(bpp), bpp);
                    if (--k == 0) break;

                    //Writes pass 6 pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    nd_br.Skip(bpp);
                }

                if (--c > 0)
                {
                    pd_br.ByteAlign();
                    nd_bw.Align();
                    nd_br.ByteAlign();


                    nd_bw.Seek(stride * 1);
                    nd_br.Skip(stride * 8 * 1);
                }
            }

            #endregion
            //*/
            #endregion

            #region Pass 7

            //Pass 7 (8 pixels on row 2, 4, 6, 8)
            
            #region Init pass 7

            pass_stride = stride;
            pd_br.Reset();
            nd_bw.Reset();

            #endregion

            #region Read and filter pass 7 data

            //If there's no data in a 8x8 quadrant, it will not be encoded.
            if (mod_h != 0)
                sub_height = (mod_h < 2) ? 4 : (mod_h < 4) ? 3 : (mod_h < 6) ? 2 : 1;

            Recon(source_data, pass_stride, bpp, pass_height * 4 - sub_height);

            #endregion

            #region Write pass 7 data

            for (int row = row_start; row < pass_height; row++)
            {
                for (int c = 0; c < 4; c++)
                {
                    //c=0: Skips row 1
                    //c=1: Skips row 3
                    //c=2: Skips row 5
                    //c=3: Skips row 7
                    nd_bw.Seek(stride * 1);

                    //c=0: Writes to row 2
                    //c=1: Writes to row 4
                    //c=2: Writes to row 6
                    //c=3: Writes to row 8
                    for (int column = col_start; column < pass_width; column++)
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            //Writes pass 7 pixel
                            nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                        }
                    }

                    //Writes out right block if it's not 8 in width
                    for (int k = 0; k < mod_w; k++)
                    {
                        //Writes pass 7 pixel
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    }

                    pd_br.ByteAlign();
                    nd_bw.Align();
                }
            }

            //Writes out bottom block if it's not 8 rows in height
            for (int c = mod_h; c > 0; c--)
            {
                nd_bw.Seek(stride * 1);
                if (--c == 0) break;
             
                for (int column = col_start; column < pass_width; column++)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        //Writes pass 7 pixel
                        nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                    }
                }

                //Writes out right block if it's not 8 in width
                for (int k = 0; k < mod_w; k++)
                {
                    //Writes pass 7 pixel
                    nd_bw.Write(pd_br.FetchBits(bpp), bpp);
                }

                pd_br.ByteAlign();
                nd_bw.Align();
            }

            #endregion
            //*/
            #endregion

            return nd;
        }

        private class CopyStream
        {
            public byte[] Data;
            public int ReadPos;
            public CopyStream(byte[] data) { Data = data; }
            public void Write(CopyStream source, int count)
            {
                for (int c = 0; c < count; c++)
                    Data[ReadPos++] = source.Data[source.ReadPos++];
            }
            public void Skip(int count)
            {
                ReadPos += count;
            }
            public void Reset()
            {
                ReadPos = 0;
            }
        }
        
        public DataAndAlpha ExtractAlpha()
        {
            if (!HasAlpha || Indexed) return null;

            var data = Decode();
            if (data == null) return null;
#if BitStream64
            Util.BitStream64 br = new BitStream64(data);
#else
            Util.BitStream br = new BitStream(data);
#endif

            int alpha_bits = BitsPerComponent;
            int color_bits = alpha_bits * (NComponents - 1);
            int w = Width, h = Height;

            int color_stride = (color_bits * w + 7) / 8;
            int alpha_stride = (alpha_bits * w + 7) / 8;

            data = new byte[color_stride * h];
            byte[] alpha_data = new byte[alpha_stride * h];
            var dw = new Util.BitWriter(data);
            var aw = new Util.BitWriter(alpha_data);

            for (int row = 0; row < h; row++)
            {
                for (int column = 0; column < w; column++)
                {
                    var color = br.FetchBits(color_bits);
                    dw.Write(color, color_bits);
                    var alpha = br.FetchBits(alpha_bits);
                    aw.Write(alpha, alpha_bits);
                }

                dw.Align();
                aw.Align();
                br.ByteAlign();
            }

            return new DataAndAlpha(data, alpha_data);
        }

        private Chunk GetChunk(string type)
        {
            foreach (var chunk in _chunks)
                if (chunk.Type == type)
                    return chunk;
            return null;
        }

        public static Png Open(Stream stream)
        {
            //Reads out the header
            var sr = new StreamReaderEx(stream);
            var buffer = new byte[8];
            int read = sr.Read(buffer, 0, 8);
            if (read != 8 || buffer[0] != 137 || buffer[1] != 80 || buffer[2] != 78 || 
                buffer[3] != 71 || buffer[4] != 13 || buffer[5] != 10 || buffer[6] != 26 || 
                buffer[7] != 10)
                throw new InvalidDataException("Not a PNG file");
            var crc = new CRC32();
            
            //Reads out chunks
            List<Chunk> chunks = new List<Chunk>(8);
            IDAT idat = null; IHDR ihdr = null;
            bool has_idat = false;
            while (sr.Read(buffer, 0, 4) == 4)
            {
                crc.Reset();
                var chunk = ReadChunk(sr, crc,  ((uint) buffer[0]) << 24 |
                                                ((uint) buffer[1]) << 16 |
                                                ((uint) buffer[2]) << 8 |
                                                    (uint) buffer[3]);
                if (chunk == null)
                    break;
                if (chunk.Type == "IDAT")
                {
                    if (has_idat)
                        throw new InvalidDataException("Invalid PNG file");
                    if (idat == null)
                    {
                        idat = new IDAT(chunk);
                        chunks.Add(idat);
                    }
                    else
                        idat.add(chunk);
                }
                else
                {
                    if (idat != null)
                        has_idat = true;

                    switch (chunk.Type)
                    {
                        case "IHDR":
                            ihdr = new IHDR(chunk);
                            if (chunks.Count != 0)
                                throw new InvalidDataException("PNG header must come first");
                            chunks.Add(ihdr);
                            break;
                        case "PLTE": chunks.Add(new PLTE(chunk)); break;
                        case "gAMA": chunks.Add(new gAMA(chunk)); break;
                        case "pHYs": chunks.Add(new pHYs(chunk)); break;
                        case "tEXt": chunks.Add(new tEXt(chunk)); break;
                        case "tRNS":
                            if (ihdr.ColorType == ColorType.None || ihdr.ColorType == ColorType.Color)
                                chunks.Add(new tRNS_ck(chunk)); 
                            else if (ihdr.ColorType == ColorType.Palette || ihdr.ColorType == (ColorType.Palette | ColorType.Color))
                                chunks.Add(new tRNS_pal(chunk)); 
                            else
                                goto default;
                            break;
                        case "bKGD": chunks.Add(new bKGD(chunk, ihdr.ColorType)); break;
                        default: chunks.Add(chunk); break;
                    }

                    if (chunk.Type == "IEND")
                        break;
                }
            }

            return new Png(ihdr, idat, chunks);
        }

        static void Recon(CopyStream data_and_pos, int stride, int bpp, int n_rows)
        {
            if (stride == 0) return;

            //How many positions one have to move to the left to
            //get the corresponding byte value.
            int sampl_offset = (bpp + 7) / 8;

            //How many pixel relevant bytes have been read in total
            var data = data_and_pos.Data;

            int cur = data_and_pos.ReadPos, write_cur = 0;
            int last = write_cur;

            //First handled by itself row. Since row 0 does not exist.
            if (n_rows > 0 && cur + stride < data.Length)
            {
                PRED pred = (PRED)data[cur++];

                switch (pred)
                {
                    case PRED.Up:
                    case PRED.None:
                        for (int c = 0; c < stride; c++)
                            data[write_cur++] = data[cur++];
                        break;
                    case PRED.Sub:
                        for (int c=0; c < sampl_offset; c++)
                            data[write_cur++] = data[cur++];
                        for (int c = sampl_offset; c < stride; c++, cur++, write_cur++)
                            data[write_cur] = (byte)(data[cur] + data[write_cur - sampl_offset]);
                        break;
                    case PRED.Avg:
                        for (int c = 0; c < sampl_offset; c++)
                            data[write_cur++] = data[cur++];
                        for (int c = sampl_offset; c < stride; c++, cur++, write_cur++)
                            data[write_cur] = (byte)(data[cur] + data[write_cur - sampl_offset] / 2);
                        break;
                    case PRED.Paeth:
                        for (int c = 0; c < sampl_offset; c++)
                            data[write_cur++] = data[cur++];
                        for (int c = sampl_offset; c < stride; c++, cur++, write_cur++)
                        {
                            data[write_cur] = (byte)(data[cur] + PaethPredictor(data[write_cur - sampl_offset], 0, 0));
                        }
                        break;

                    default:
                        System.Diagnostics.Debug.Assert(false);
                        throw new NotSupportedException();
                }


                for (int rows_count = 1; rows_count < n_rows && cur + stride < data.Length; rows_count++)
                {
                    //First byte of the row tells which predicor to use.
                    pred = (PRED)data[cur++];

                    // Pri(x-1) Pri(x)
                    // Raw(x-1) Raw(x)
                    switch (pred)
                    {
                        case PRED.None:
                            //Recon(x) = Raw(x) (Nothing to do)
                            for (int c = 0; c < stride; c++)
                                data[write_cur++] = data[cur++];
                            last += stride;
                            break;
                        case PRED.Sub:
                            //Recon(x) = Raw(x) + Raw(x-1)
                            //Starts at _sampl_offset as data before that is 0
                            for (int c = 0; c < sampl_offset; c++)
                                data[write_cur++] = data[cur++];
                            for (int c = sampl_offset; c < stride; c++, cur++, write_cur++)
                                data[write_cur] = (byte)(data[cur] + data[write_cur - sampl_offset]);
                            last += stride;
                            break;

                        case PRED.Up:
                            //Recon(x) = Raw(x) + Prior(x) 
                            for (int c = 0; c < stride; c++, cur++, last++)
                                data[write_cur++] = (byte)(data[cur] + data[last]);
                            break;

                        case PRED.Avg:
                            //Recon(x) = Raw(x) + floor((Raw(x-1) + Prior(x)) / 2) 
                            //Special case cur[0], as x-1 is out of bounds.
                            for (int c = 0; c < sampl_offset; c++, cur++, last++)
                                data[write_cur++] = (byte)(data[cur] + data[last] / 2);
                            for (int c = sampl_offset; c < stride; c++, cur++, last++, write_cur++)
                                data[write_cur] = (byte)(data[cur] + (data[write_cur - sampl_offset] + data[last]) / 2);
                            break;

                        case PRED.Paeth:
                            //Recon(x) = Raw(x) + PaethPredictor(Raw(x-1), Prior(x), Prior(x-1)) 
                            //Special case cur[0], as x-1 is out of bounds.
                            for (int c = 0; c < sampl_offset; c++, cur++, last++)
                                data[write_cur++] = (byte)(data[cur] + PaethPredictor(0, data[last], 0));
                            for (int c = sampl_offset; c < stride; c++, cur++, last++, write_cur++)
                            {
                                data[write_cur] = (byte)(data[cur] + PaethPredictor(data[write_cur - sampl_offset], data[last], data[last - sampl_offset]));
                            }
                            break;

                        default:
                            System.Diagnostics.Debug.Assert(false);
                            throw new NotSupportedException();
                    }
                }

                data_and_pos.ReadPos = cur;
            }
        }

        /// <summary>
        /// As in (9.4 Filter type 4: Paeth)
        /// </summary>
        static byte PaethPredictor(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }

        private enum PRED
        {
            EOD = -1,
            None, Sub, Up, Avg, Paeth
        }

        static DefaultChunk ReadChunk(StreamReaderEx sr, CRC32 crc, uint length)
        {
            //Reads the length of the chunk.
            if (length > int.MaxValue)
                return null;

            //Reads the chunk type
            var type = new char[4];
            for (int c = 0; c < type.Length; c++)
            {
                var b = sr.ReadByte();
                if (b < 65 || b > 122 || 90 < b && b < 97)
                    return null;
                type[c] = (char)b;
                crc.Update(b);
            }

            //Reads the chunk data.
            var buffer = new byte[length];
            if (sr.Read(buffer, 0, (int) length) != length)
                return null;

            //Calcs the crc code
            crc.Update(buffer, 0, buffer.Length);

            //Reads the crc (Note, can throw an exception here)
            var check_code = sr.ReadInt();

            return new DefaultChunk(check_code == crc.Value, new string(type), buffer);
        }

        public sealed class DataAndAlpha
        {
            public readonly byte[] Data;
            public readonly byte[] Alpha;
            internal DataAndAlpha(byte[] data, byte[] alpha)
            { Data = data; Alpha = alpha; }
        }

        abstract class Chunk
        {
            protected bool _valid;
            public bool Valid { get { return _valid; } }
            public readonly string Type;

            protected Chunk(bool v, string t)
            { _valid = v; Type = t; }
            public override string ToString() { return Type; }
        }

        class DefaultChunk : Chunk
        {
            public readonly byte[] Data;

            public DefaultChunk(bool v, string t, byte[] d)
                : base(v, t)
            {  Data = d; }
        }

        /// <summary>
        /// Header
        /// </summary>
        class IHDR : Chunk
        {
            public readonly int Width, Height;

            /// <summary>
            /// Bit depth is a single-byte integer giving the number of bits per sample or per palette index (not per pixel). 
            /// Valid values are 1, 2, 4, 8, and 16, although not all values are allowed for all color types. 
            /// </summary>
            public readonly int BitDepth;

            /// <summary>
            /// How to interprent the image data
            /// </summary>
            public readonly ColorType ColorType;

            /// <summary>
            /// Only deflate supported
            /// </summary>
            //public readonly CompressionType Compresion;

            /// <summary>
            /// Only filter method 0 supported
            /// </summary>
            //public readonly FilterType Filter;

            /// <summary>
            /// Order of image data
            /// </summary>
            public readonly InterlaceType Interlace;

            public IHDR(DefaultChunk chunk)
                : base(chunk.Valid, chunk.Type)
            {  
                if (_valid)
                {
                    var sr = new StreamReaderEx(chunk.Data);
                    Width = sr.ReadInt();
                    if (Width <= 0)
                        _valid = false;
                    Height = sr.ReadInt();
                    if (Height <= 0)
                        _valid = false;
                    BitDepth = sr.ReadByte();
                    ColorType = (ColorType) sr.ReadByte();

                    if (_valid)
                    {
                        switch(ColorType)
                        {
                            case Png.ColorType.None: //Crayscale
                                _valid = BitDepth == 1 || BitDepth == 2 || BitDepth == 4 || BitDepth == 8 || BitDepth == 16;
                                break;
                            case Png.ColorType.Color | Png.ColorType.Alpha: //RGB with Alpha
                            case Png.ColorType.Alpha: //Gray with alpha
                            case Png.ColorType.Color: //RGB
                                _valid = BitDepth == 8 || BitDepth == 16;
                                break;
                            case Png.ColorType.Palette | Png.ColorType.Color: //Indexed
                                _valid = BitDepth == 1 || BitDepth == 2 || BitDepth == 4 || BitDepth == 8;
                                break;
                            default: _valid = false; break;
                        }
                    }

                    var Compresion = (CompressionType) sr.ReadByte();
                    if (Compresion != CompressionType.Deflate)
                        _valid = false;
                    var Filter = (FilterType) sr.ReadByte();
                    if (Filter != FilterType.AdaptiveFiltering)
                        _valid = false;
                    Interlace = (InterlaceType) sr.ReadByte();
                    if (Interlace != InterlaceType.None && Interlace != InterlaceType.Adam7)
                        _valid = false;
                }
            }
        }

        /// <summary>
        /// Palette
        /// </summary>
        class PLTE : Chunk
        {
            public readonly byte[] RawData;
            public int[] Palette 
            {
                get
                {
                    var palette = new int[256];
                    var d = RawData;
                    
                    for (int c = 0, k = 0; c < d.Length; )
                    {
                        byte red = d[c++], green = d[c++], blue = d[c++];
                        palette[k++] = 0 | red << 16 | green << 8 | blue;
                    }
                    return palette;
                }
            }

            public PLTE(DefaultChunk chunk)
                : base(chunk.Valid, chunk.Type)
            {
                RawData = chunk.Data;
                if (RawData.Length % 3 != 0)
                    _valid = false;
            }
        }

        /// <summary>
        /// Image data
        /// </summary>
        class IDAT : Chunk
        {
            private List<byte[]> _data = new List<byte[]>(1);

            public byte[] Data
            {
                get
                {
                    if (_data.Count == 1)
                        return _data[0];
                    int total_size = 0;
                    foreach(var data in _data)
                        total_size += data.Length;
                    var d = new byte[total_size];
                    total_size = 0;
                    foreach(var data in _data)
                    {
                        Buffer.BlockCopy(data, 0, d, total_size, data.Length);
                        total_size += data.Length;
                    }
                    return d;
                }
            }

            public IDAT(DefaultChunk c)
                : base(c.Valid, c.Type)
            {
                _data.Add(c.Data);
            }

            public void add(DefaultChunk c)
            {
                if (!c.Valid)
                    _valid = false;
                _data.Add(c.Data);
            }
        }

        /// <summary>
        /// Textual data
        /// </summary>
        class tEXt : Chunk
        {
            public string Keyword, Text;

            public tEXt(DefaultChunk c)
                : base(c.Valid, c.Type)
            {
                var d = c.Data;

                if (_valid)
                {
                    //Search for the null terminator
                    var nul = Array.IndexOf<byte>(d, 0);
                    if (nul <= 0 || nul > 80)
                    {
                        _valid = false;
                        Keyword = ""; Text = "";
                        return;
                    }
                    var enc = Encoding.GetEncoding("iso-8859-1");
                    Keyword = enc.GetString(d, 0, nul);
                    int length = d.Length - (nul + 1);
                    if (length == 0)
                    {
                        Text = string.Empty;
                        return;
                    }

                    if (Array.LastIndexOf<byte>(d, 0) != nul)
                    {
                        _valid = false;
                        Text = "";
                        return;
                    }
                    Text = enc.GetString(d, nul + 1, length);
                }
            }

            public override string ToString()
            {
                return Keyword + ": "+Text;
            }
        }

        /// <summary>
        /// Physical pixel dimensions
        /// </summary>
        class pHYs : Chunk
        {
            public readonly MeasureUnit Unit;
            public readonly uint PixelsPerUnitX, PixelsPerUnitY;

            public pHYs(DefaultChunk c)
                : base(c.Valid, c.Type)
            {
                if (c.Data.Length != 9)
                    _valid = false;
                else
                {
                    var sr = new StreamReaderEx(c.Data);
                    PixelsPerUnitX = sr.ReadUInt();
                    PixelsPerUnitY = sr.ReadUInt();
                    Unit = (MeasureUnit) sr.ReadByte();
                    if (Unit != MeasureUnit.Meter && Unit != MeasureUnit.Unknown)
                        _valid = false;
                }
            }
        }

        /// <summary>
        /// Gamma
        /// </summary>
        class gAMA : Chunk
        {
            private readonly uint _gamma;
            public double Gamma { get { return _gamma / 100000d; } }

            public gAMA(DefaultChunk c)
                : base(c.Valid, c.Type)
            {
                if (c.Data.Length != 4)
                    _valid = false;
                else
                {
                    _gamma = new StreamReaderEx(c.Data).ReadUInt();
                }
            }

        }

        /// <summary>
        /// Transparency color keys
        /// </summary>
        /// <remarks>Colors that mach these keys
        /// is to be fully transparent</remarks>
        class tRNS_ck : Chunk
        {
            public readonly ushort[] Keys;

            public tRNS_ck(DefaultChunk c)
                : base(c.Valid, c.Type)
            {
                var d = c.Data;
                Keys = new ushort[d.Length / 2];
                for (int j = 0, pos = 0; j < Keys.Length; j++)
                    Keys[j] = (ushort) (d[pos++] << 8 | d[pos++]);
            }
            public tRNS_ck(ushort[] shorts)
                : base(true, "tRNS")
            {
                Keys = shorts;
            }
        }

        class tRNS_pal : Chunk
        {
            public readonly byte[] Palette;

            public tRNS_pal(DefaultChunk c)
                : base(c.Valid, c.Type)
            {
                Palette = c.Data;
            }
        }

        class bKGD : Chunk
        {
            public ushort[] Color;

            public bKGD(DefaultChunk c, ColorType ct)
                : base(c.Valid, c.Type)
            {
                var sr = new StreamReaderEx(c.Data);
                if ((ct & ColorType.Palette) != 0)
                    Color = new ushort[] { sr.ReadByte() };
                else if ((ct & ColorType.Color) != 0)
                    Color = new ushort[] { sr.ReadUShort(), sr.ReadUShort(), sr.ReadUShort() };
                else
                    Color = new ushort[] { sr.ReadUShort() };
            }
        }

        enum MeasureUnit
        {
            Unknown,

            /// <summary>
            /// one inch is equal to exactly 0.0254 meters
            /// </summary>
            Meter
        }

        [Flags()]
        enum ColorType
        {
            None = 0,
            Palette = 1,
            Color = 2,
            Alpha = 4
        }

        enum CompressionType
        {
            Deflate
        }

        enum FilterType
        {
            AdaptiveFiltering 
        }

        enum InterlaceType
        {
            None,
            Adam7 
        }
    }
}
