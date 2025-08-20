using PdfLib.Pdf.ColorSpace;
using PdfLib.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Windows;
using System.Windows.Media.Effects;

namespace PdfLib.Img
{
    /// <summary>
    /// Simple BMP parser
    /// </summary>
    /// <remarks>
    /// Todo: I strongly suspect the palette color conversion is wrong. This needs to be
    /// tested. Perhaps 1bpp is to be assumed to have a grayscale palette? I currently
    /// assume that data to be Alpha.
    /// So test: 4bpp, 8bpp paletteterisized images. Also, OS/2 likely won't work. 
    /// </remarks>
    public class Bmp
    {
        public enum ColorMode
        {
            None,
            RGB,
            BGR,
            Gray
        }

        public enum ColorModeAlpha
        {
            None,
            ARGB,
            BGRA,
        }

        public abstract class Pixels
        {
            public readonly int Width, Height;
            public Pixels(int w, int h)
            {
                Width = w; Height = h;
            }
        }

        public sealed class ChunkyPixels : Pixels
        {
            public readonly ColorMode Order;
            public readonly ColorModeAlpha Alpha;
            public int BitsPerPixels, BitsPerComponent;
            public readonly byte[] RawPixels;
            public ChunkyPixels(int w, int h, byte[] raw, int bpp, int bpc, ColorMode order, ColorModeAlpha alpha = ColorModeAlpha.None)
                : base(w, h)
            {
                RawPixels = raw;
                BitsPerPixels = bpp;
                BitsPerComponent = bpc;
                Order = order;
                Alpha = alpha;
            }
        }

        public sealed class ChunkyWithAlphaPlane : Pixels
        {
            public readonly ChunkyPixels Colors;
            public readonly Plane Alpha;

            public ChunkyWithAlphaPlane(ChunkyPixels colors,  Plane alpha)
                : base(colors.Width, colors.Height)
            {
                Colors = colors;
                Alpha = alpha;
            }
        }

        public sealed class IndexedPixels : Pixels
        {
            public int BitsPerPixels;
            public readonly CLUT Palette;
            public readonly byte[] RawPixels;
            public IndexedPixels(int w, int h, byte[] raw, int bpp, CLUT pal)
                : base(w, h)
            {
                Palette = pal;
                RawPixels = raw;
                BitsPerPixels = bpp;
            }
        }

        public sealed class Planes : Pixels
        {
            public readonly Plane[] Components;
            internal Planes(int w, int h, Plane[] comps)
                : base(w, h)
            {
                Components = comps;
            }
        }


        public struct Plane
        {
            public int bpc;
            public int[] Subpixels;
        }

        Header _head;
        CLUT _pal;
        byte[] _data;

        public uint RedMask => _head.RedMask;
        public uint GreenMask => _head.GreenMask;
        public uint BlueMask => _head.BlueMask;
        public uint AlphaMask => _head.AlphaMask;

        /// <summary>
        /// If this is a 565 bitmap.
        /// </summary>
        private bool IsUniform
        {
            get
            {
                if (_head.Compression == COMPRESSION.BITFIELDS)
                {
                    uint gm = GreenMask, bm = BlueMask, rm = RedMask;
                    if (gm == 0 && bm == 0 && rm == 0)
                        return _head.BPP != 16;
                    int ones = CountOnes(gm, out _);
                    return ones == CountOnes(rm, out _) && ones == CountOnes(bm, out _);
                }
                return false;
            }
        }

        /// <summary>
        /// If this is a 1555 bitmap.
        /// </summary>
        private bool Is1555
        {
            get
            {
                if (BitsPerPixel == 16 && _head.Compression == COMPRESSION.BITFIELDS)
                    return CountOnes(GreenMask, out _) == 5 && CountOnes(AlphaMask, out _) == 1;
                return false;
            }
        }

        /// <summary>
        /// Information about compression and color space
        /// </summary>
        public COMPRESSION Format { get { return _head.Compression; } }

        /// <summary>
        /// Pixels where each row is alligned on 4 byte bounderies, and the
        /// image may also be upside down
        /// </summary>
        public byte[] RawPixels { get { return _data; } }

        /// <summary>
        /// Pixels where each row is byte aligned
        /// </summary>
        [Obsolete("DepricatedPixels is obsolete. Use RawPixels instead.")]
        public byte[] DepricatedPixels
        {
            get
            {
                int w = _head.Width, h = Height, bpp = _head.BPP, d_pos;
                int stride_8 = (bpp * w + 7) / 8, stride_32;
                byte[] pxs;
                bool rle4 = false;
                switch (_head.Compression)
                {
                    case COMPRESSION.RLE4:
                        rle4 = true;
                        goto case COMPRESSION.RLE8;
                    case COMPRESSION.RLE8:
                        pxs = new byte[stride_8 * h];
                        d_pos = 0;
                        if (_head.Height > 0)
                        {
                            d_pos = pxs.Length - stride_8;
                            stride_8 = -stride_8;
                        }

                        for (int c = 0, row_pos = 0; c < _data.Length;)
                        {
                            var cmd = _data[c++];
                            if (c == _data.Length)
                                throw new InvalidDataException("Error in run length data");
                            var par = _data[c++];

                            if (cmd == 0)
                            {
                                switch (par)
                                {
                                    case 0: //End of line.
                                        d_pos += stride_8;
                                        row_pos = 0;
                                        break;
                                    case 1: //End of bitmap
                                        return pxs;
                                    case 2: //Delta
                                        if (c + 1 >= _data.Length)
                                            throw new InvalidDataException("Error in run length data");
                                        row_pos += _data[c++];
                                        d_pos += stride_8 * _data[c++];
                                        break;
                                    default: //Absolute
                                        if (rle4)
                                            par /= 2;
                                        for (int k = 0; k < par; k++)
                                        {
                                            if (c == _data.Length)
                                                throw new InvalidDataException("Error in run length data");

                                            pxs[d_pos + row_pos++] = _data[c++];
                                        }
                                        if (c % 2 != 0)
                                            c++;
                                        break;
                                }
                            }
                            else
                            {
                                if (rle4)
                                    cmd /= 2;
                                for (int k = 0; k < cmd; k++)
                                    pxs[d_pos + row_pos++] = par;
                            }
                        }

                        throw new InvalidDataException("BMP run length data not terminated properly.");
                    case COMPRESSION.CMYK:
                    case COMPRESSION.RGB:
                        stride_32 = (bpp * w + 31) / 32 * 4;

                        if (stride_8 == stride_32 && _head.Height < 0)
                            return _data;

                        pxs = new byte[stride_8 * h];
                        d_pos = 0;
                        if (_head.Height > 0)
                        {
                            d_pos = _data.Length - stride_32;
                            stride_32 = -stride_32;
                        }

                        for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32, p_pos += stride_8)
                            Buffer.BlockCopy(_data, d_pos, pxs, p_pos, stride_8);
                        return pxs;

                    case COMPRESSION.ALPHABITFIELDS:
                    case COMPRESSION.BITFIELDS:
                        //For images with alpha, we return BGRA, for images without alpha we return RGB
                        if (bpp == 32)
                        {
                            if (_head is Win_v3_header)
                            {
                                stride_32 = (bpp * w + 31) / 32 * 4;
                                pxs = new byte[stride_8 * h];
                                d_pos = 0;
                                if (_head.Height > 0)
                                {
                                    d_pos = _data.Length - stride_32;
                                    stride_32 = -stride_32;
                                }

                                int r_pos = MaskPos(_head.RedMask);
                                int g_pos = MaskPos(_head.GreenMask);
                                int b_pos = MaskPos(_head.BlueMask);
                                int a_pos = MaskPos(_head.AlphaMask);

                                for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32, p_pos += stride_8)
                                {
                                    for(int x=0; x < stride_8; x+= 4)
                                    {
                                        pxs[p_pos + x + 0] = _data[d_pos + x + b_pos];
                                        pxs[p_pos + x + 1] = _data[d_pos + x + g_pos];
                                        pxs[p_pos + x + 2] = _data[d_pos + x + r_pos];
                                        pxs[p_pos + x + 3] = _data[d_pos + x + a_pos];
                                    }
                                }

                                return pxs;
                            }
                            else
                            {
                                throw new NotImplementedException("32 bpp bitfield without alpha");
                            }
                        }
                        else if (bpp == 16)
                        {
                            if (CountOnes(AlphaMask, out _) != 0)
                                throw new NotImplementedException("16 bpp bitfield with alpha");

                            int r_shift, b_shift, g_shift;
                            uint rm = RedMask, gm = GreenMask, bm = BlueMask;

                            int r_bpp = CountOnes(rm, out r_shift);
                            int g_bpp = CountOnes(gm, out g_shift);
                            int b_bpp = CountOnes(bm, out b_shift);

                            stride_32 = (bpp * w + 31) / 32 * 4;
                            pxs = new byte[stride_8 * h];
                            d_pos = 0;
                            if (_head.Height > 0)
                            {
                                d_pos = _data.Length - stride_32;
                                stride_32 = -stride_32;
                            }

                            var bw = new BitWriter(pxs);

                            if (rm == 0 && gm == 0 && bm == 0)
                            {
                                var br = new BitStream(_data);
                                //int clear = (Math.Abs(stride_32) - stride_8) * 8;
                                for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32, p_pos += stride_8)
                                {
                                    br.StreamPosition = d_pos;
                                    for (int x = 0; x < stride_8; x += 2)
                                    {
                                        //br.Skip(1);
                                        int b = br.FetchBits(5), g = br.FetchBits(5), r = br.FetchBits(5);
                                        bw.Write(b, 5);
                                        bw.Write(g, 5);
                                        bw.Write(r, 5);
                                        //br.ByteAlign();
                                        bw.Align();
                                    }

                                    //br.Skip(clear);
                                }
                            }
                            else
                            {
                                for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32, p_pos += stride_8)
                                {
                                    for (int x = 0; x < stride_8; x += 2)
                                    {
                                        var val = StreamReaderEx.ReadUShortLE(d_pos + x, _data);
                                        bw.Write((val & bm) >> b_shift, b_bpp);
                                        bw.Write((val & gm) >> g_shift, g_bpp);
                                        bw.Write((val & rm) >> r_shift, r_bpp);
                                        bw.Align();
                                    }
                                }
                            }

                            bw.Flush();

                            return pxs;
                        }
                        else
                        {
                            throw new NotImplementedException("Bitfield that isn't 32bpp");
                        }
                    default:
                        throw new NotImplementedException("Compression format: " + _head.Compression.ToString());
                }
            }
        }

        public int Width { get { return _head.Width; } }
        public int Height { get { return Math.Abs(_head.Height); } }

        /// <summary>
        /// Height that can be negative
        /// </summary>
        public int RawHeight { get { return _head.Height; } }
        public int BitsPerPixel { get { return _head.BPP; } }
        public CLUT Palette { get { return _pal; } }

        private Bmp(Header header, CLUT pal, byte[] pixels)
        {
            _head = header;
            _pal = pal;
            _data = pixels;
        }

        private Pixels RetPixels(byte[] data)
        {
            int bpp = _head.BPP;
            var pal = Palette;
            if (pal != null)
                return new IndexedPixels(_head.Width, _head.Height, data, bpp, pal);

            bool has_alpha = bpp % 3 != 0;
            int bpc = bpp;
            if (has_alpha)
            {
                bpc /= 4;

                //Strips the alpha data unless there truly is alpha
                int w = _head.Width, h = Height;
                int stride_8 = (bpp * w + 7) / 8;
                int aligned_bpp = ((bpc * 3 + 7) / 8) * 8;
                byte[] pxs = new byte[((aligned_bpp * w + 7) / 8) * h];
                has_alpha = false;

                if (bpc == 8)
                {
                    for (int x = 0, d_pos = 0; x < pxs.Length;)
                    {
                        pxs[x++] = data[d_pos++];
                        pxs[x++] = data[d_pos++];
                        pxs[x++] = data[d_pos++];
                        if (data[d_pos++] != 0)
                        {
                            has_alpha = true;
                            break;
                        }
                    }
                }
                else
                {
                    var br = new BitStream(data);
                    var bw = new BitWriter(pxs);
                    for (int x = 0; x < pxs.Length;)
                    {
                        bw.Write(br.FetchBits(bpc), bpc);
                        bw.Write(br.FetchBits(bpc), bpc);
                        bw.Write(br.FetchBits(bpc), bpc);
                        if (br.FetchBits(bpc) != 0)
                        {
                            has_alpha = true;
                            break;
                        }
                        bw.Align();
                        br.ByteAlign();
                    }
                }

                if (!has_alpha)
                {
                    data = pxs;
                    bpp = 3 * bpc;
                }
            }
            else
                bpc /= 3;

            return new ChunkyPixels(_head.Width, _head.Height, data, bpp, bpc, ColorMode.BGR, has_alpha ? ColorModeAlpha.BGRA : ColorModeAlpha.None);
        }

        //I need something less fragaile than this mess.
        // Create a "GetPixles" method that expects the consumer to be on the ball.
        // For 565 images -> return sep components
        // For 1555 images -> return one chunky blob and sep alpha mask
        // For other images, return a chunky boy.
        // In all cases, return a color space in the form of "gray/rgb/bgr/argb/bgra/other"
        //  - implemet this by having two colorspaces, one that includes alpha, one that do not.
        // Include an option to have byte aligned pixels, and int aligned rows
        //  - rows are otherwise byte aligned. 
        // Rember paletes.
        // Remeber to mark when pixels are byte aligned.
        //
        //Bitfields must be continious but non-overlapping, something we can check.
        public Pixels GetPixels(bool perfer_planes = false)
        {
            var compresion = _head.Compression;
            switch (compresion)
            {
                case COMPRESSION.RLE4:
                case COMPRESSION.RLE8:
                    return RetPixels(DecompressRLE(compresion == COMPRESSION.RLE4));
                case COMPRESSION.CMYK:
                case COMPRESSION.RGB:
                    {
                        int w = _head.Width, h = Height, bpp = _head.BPP, d_pos;
                        int stride_8 = (bpp * w + 7) / 8;
                        int stride_32 = (bpp * w + 31) / 32 * 4;
                        byte[] pxs;

                        if (stride_8 == stride_32 && _head.Height < 0)
                        {
                            pxs = _data;
                        }
                        else
                        {
                            pxs = new byte[stride_8 * h];
                            d_pos = 0;
                            if (_head.Height > 0)
                            {
                                d_pos = _data.Length - stride_32;
                                stride_32 = -stride_32;
                            }

                            for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32, p_pos += stride_8)
                                Buffer.BlockCopy(_data, d_pos, pxs, p_pos, stride_8);
                        }

                        return RetPixels(pxs);
                    }

                case COMPRESSION.ALPHABITFIELDS:
                case COMPRESSION.BITFIELDS:
                    {
                        int w = _head.Width, h = Height, bpp = _head.BPP, d_pos;
                        int stride_8 = (bpp * w + 7) / 8;
                        int stride_32 = (bpp * w + 31) / 32 * 4;

                        //For images with alpha, we return BGRA, for images without alpha we return RGB
                        if (bpp == 32 && FFMasks())
                        {
                            if (_head is Win_v3_header)
                            {
                                stride_32 = (bpp * w + 31) / 32 * 4;
                                byte[] pxs = new byte[stride_8 * h];
                                d_pos = 0;
                                if (_head.Height > 0)
                                {
                                    d_pos = _data.Length - stride_32;
                                    stride_32 = -stride_32;
                                }

                                int r_pos = MaskPos(_head.RedMask);
                                int g_pos = MaskPos(_head.GreenMask);
                                int b_pos = MaskPos(_head.BlueMask);
                                int a_pos = MaskPos(_head.AlphaMask);

                                for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32, p_pos += stride_8)
                                {
                                    for (int x = 0; x < stride_8; x += 4)
                                    {
                                        pxs[p_pos + x + 0] = _data[d_pos + x + b_pos];
                                        pxs[p_pos + x + 1] = _data[d_pos + x + g_pos];
                                        pxs[p_pos + x + 2] = _data[d_pos + x + r_pos];
                                        pxs[p_pos + x + 3] = _data[d_pos + x + a_pos];
                                    }
                                }

                                return new ChunkyPixels(w, h, pxs, bpp, bpp / 4, ColorMode.BGR, ColorModeAlpha.BGRA);
                            }
                            else
                            {
                                throw new NotImplementedException("32 bpp bitfield without alpha");
                            }
                        }
                        else
                        {
                            uint rm = RedMask, bm = BlueMask, gm = GreenMask, am = AlphaMask;
                            int r_shift, b_shift, g_shift, a_shift;

                            if (rm == 0 && gm == 0 && bm == 0 && am == 0 && bpp == 16)
                            {
                                bm = 0x001f;
                                gm = 0x07e0;
                                rm = 0xF800;
                            }

                            int r_bpc = CountOnes(rm, out r_shift);
                            int g_bpc = CountOnes(gm, out g_shift);
                            int b_bpc = CountOnes(bm, out b_shift);
                            int a_bpc = CountOnes(am, out a_shift);
                            int comp_len = w * h;

                            var comps = new Plane[] {
                               new Plane() { bpc = r_bpc, Subpixels = new int[comp_len] },
                               new Plane() { bpc = g_bpc, Subpixels = new int[comp_len] },
                               new Plane() { bpc = b_bpc, Subpixels = new int[comp_len] }
                            };
                            if (a_bpc != 0)
                            {
                                Array.Resize(ref comps, 4);
                                comps[3] = new Plane() { bpc = a_bpc, Subpixels = new int[comp_len] };
                            }

                            d_pos = 0;
                            if (_head.Height > 0)
                            {
                                d_pos = _data.Length - stride_32;
                                stride_32 = -stride_32;
                            }

                            if (bpp == 32)
                            {
                                for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32)
                                {
                                    for (int x = 0, w2 = w * 2; x < w2; x += 2, p_pos++)
                                    {
                                        uint val = StreamReaderEx.ReadUIntLE(d_pos + x, _data);
                                        comps[0].Subpixels[p_pos] = (int)((val & rm) >> r_shift);
                                        comps[1].Subpixels[p_pos] = (int)((val & gm) >> g_shift);
                                        comps[2].Subpixels[p_pos] = (int)((val & bm) >> b_shift);
                                        if (a_bpc != 0)
                                            comps[3].Subpixels[p_pos] = (int)((val & am) >> a_shift);
                                    }
                                }
                            }
                            else if (bpp == 16)
                            {
                                for (int y = 0, p_pos = 0; y < h; y++, d_pos += stride_32)
                                {
                                    for (int x = 0, w2 = w * 2; x < w2; x += 2, p_pos++)
                                    {
                                        ushort val = StreamReaderEx.ReadUShortLE(d_pos + x, _data);
                                        comps[0].Subpixels[p_pos] = (int)((val & rm) >> r_shift);
                                        comps[1].Subpixels[p_pos] = (int)((val & gm) >> g_shift);
                                        comps[2].Subpixels[p_pos] = (int)((val & bm) >> b_shift);
                                        if (a_bpc != 0)
                                            comps[3].Subpixels[p_pos] = (int)((val & am) >> a_shift);
                                    }
                                }
                            }
                            else
                            {
                                throw new NotImplementedException("BMP BPP: " + bpp);
                            }
                            
                            var planes = new Planes(w, h, comps);

                            if (!perfer_planes && r_bpc == b_bpc && r_bpc == g_bpc)
                            {
                                var pxs = new byte[stride_8 * h];
                                var bw = new BitWriter(pxs);
                                var pls = planes.Components;
                                for (int y = 0, p_pos = 0; y < h; y++, p_pos++)
                                {
                                    for(int x = 0; x < w; x++)
                                    {
                                        bw.Write(pls[2].Subpixels[p_pos], r_bpc);
                                        bw.Write(pls[1].Subpixels[p_pos], r_bpc);
                                        bw.Write(pls[0].Subpixels[p_pos], r_bpc);
                                        bw.Align();
                                    }
                                }

                                //Not needed, thanks to "Align"
                                //bw.Flush();

                                var cp = new ChunkyPixels(w, h, pxs, bpp, r_bpc, ColorMode.BGR);
                                if (a_bpc == 0 && pls.Length > 3)
                                    return new ChunkyWithAlphaPlane(cp, pls[3]);
                                return cp;
                            }

                            return planes;
                        }
                    }
                default:
                    throw new NotImplementedException("Compression format: " + _head.Compression.ToString());
            }
        }

        private bool FFMasks()
        {
            int r_shift, b_shift, g_shift, a_shift;
            uint rm = RedMask, bm = BlueMask, gm = GreenMask, am = AlphaMask;
            CountOnes(rm, out r_shift);
            CountOnes(gm, out g_shift);
            CountOnes(bm, out b_shift);
            CountOnes(am, out a_shift);

            return (rm >> r_shift) == 0xFF &&
                   (gm >> g_shift) == 0xFF &&
                   (bm >> b_shift) == 0xFF &&
                   (am >> a_shift) == 0xFF;
        }
        private byte[] DecompressRLE(bool rle4)
        {
            int w = _head.Width, h = Height, bpp = _head.BPP, d_pos;
            int stride_8 = (bpp * w + 7) / 8;
            byte[] pxs = new byte[stride_8 * h];
            d_pos = 0;
            if (_head.Height > 0)
            {
                d_pos = pxs.Length - stride_8;
                stride_8 = -stride_8;
            }

            for (int c = 0, row_pos = 0; c < _data.Length;)
            {
                var cmd = _data[c++];
                if (c == _data.Length)
                    throw new InvalidDataException("Error in run length data");
                var par = _data[c++];

                if (cmd == 0)
                {
                    switch (par)
                    {
                        case 0: //End of line.
                            d_pos += stride_8;
                            row_pos = 0;
                            break;
                        case 1: //End of bitmap
                            return pxs;
                        case 2: //Delta
                            if (c + 1 >= _data.Length)
                                throw new InvalidDataException("Error in run length data");
                            row_pos += _data[c++];
                            d_pos += stride_8 * _data[c++];
                            break;
                        default: //Absolute
                            if (rle4)
                                par /= 2;
                            for (int k = 0; k < par; k++)
                            {
                                if (c == _data.Length)
                                    throw new InvalidDataException("Error in run length data");

                                pxs[d_pos + row_pos++] = _data[c++];
                            }
                            if (c % 2 != 0)
                                c++;
                            break;
                    }
                }
                else
                {
                    if (rle4)
                        cmd /= 2;
                    for (int k = 0; k < cmd; k++)
                        pxs[d_pos + row_pos++] = par;
                }
            }

            throw new InvalidDataException("BMP run length data not terminated properly.");
        }

        private int CountOnes(uint mask, out int shift)
        {
            int bpc = 0;
            shift = 0;
            if (mask != 0)
            {
                // Count leading zeros
                while ((mask & 1) == 0)
                {
                    shift++;
                    mask >>= 1;
                }

                // Count consecutive ones
                while ((mask & 1) != 0)
                {
                    bpc++;
                    mask >>= 1;
                }
            }
            return bpc;
        }
        private int MaskPos(uint mask)
        {
            for (int c = 0; c < 4; c++)
            {
                if ((mask & 0xFF) == 0xFF)
                    return c;
                mask >>= 8;
            }

            throw new NotImplementedException("Masks that aren't in the 0xFF form");
        }

        public static Bmp Open(Stream stream)
        {
            //Reads out the header
            long start = stream.Position;
            var sr = new StreamReaderEx(stream, false);
            var buffer = new byte[14];
            int read = sr.Read(buffer, 0, buffer.Length);
            if (read != buffer.Length || buffer[0] != 0x42 || buffer[1] != 0x4D)
                throw new InvalidDataException("Not a BMP file");
            uint filesize = StreamReaderEx.ReadUIntLE(2, buffer);
            uint offset = StreamReaderEx.ReadUIntLE(10, buffer);

            uint header_size = sr.ReadUInt() - 4;
            bool WindowsBMP = header_size != 8 && header_size != 60;

            Header header;
            buffer = new byte[Math.Min(header_size, 124)];
            sr.ReadEx(buffer, 0, buffer.Length);
            if (buffer.Length < header_size)
                sr.Position += header_size - buffer.Length;

            if (WindowsBMP)
            {
                if (buffer.Length >= 120)
                    header = new Win_v5_header(buffer);
                else if (buffer.Length >= 104)
                    header = new Win_v4_header(buffer);
                else if (buffer.Length >= 52)
                    header = new Win_v3_header(buffer);
                else if (buffer.Length >= 48)
                    header = new Win_v2_header(buffer);
                else if (buffer.Length >= 36)
                    header = new Win_v1_header(buffer);
                else
                    throw new InvalidDataException("Not a BMP file");
            }
            else
            {
                if (buffer.Length < 8 || buffer.Length > 60)
                    throw new InvalidDataException("Not a BMP file");
                header = new OS2_v1_header(buffer);
            }

            CLUT pal;
            int palette_size = (int)(offset - 14 - 4 - header_size);
            if (palette_size / header.NColors == 4)
            {
                //This is a RGBA32 palette. OS2 images can use a RGB24 palette
                buffer = new byte[palette_size];
                sr.ReadEx(buffer, 0, buffer.Length);
                pal = new RGBA32Palette(buffer);
            }
            else pal = null;

            if (sr.Position != offset)
                sr.Position = start + offset;

            switch (header.Compression)
            {
                case COMPRESSION.BITFIELDS:
                case COMPRESSION.RGB:
                    buffer = new byte[header.RequiredSize];
                    break;
                default:
                    Debug.Assert(header.Size == sr.Length - offset - start); //Nothing need to be wrong (file can have junk data), but probably is.
                    buffer = new byte[header.Size];
                    break;
            }

            sr.ReadEx(buffer, 0, buffer.Length);

            return new Bmp(header, pal, buffer);
        }

        #region Header

        abstract class Header
        {
            protected readonly byte[] _data;

            /// <summary>
            /// Width of the image
            /// </summary>
            public abstract int Width { get; }

            /// <summary>
            /// Height of the image
            /// </summary>
            public abstract int Height { get; }

            /// <summary>
            /// Bits per pixel
            /// </summary>
            public abstract int BPP { get; }
            public abstract int NColors { get; }

            public abstract int DPI_X { get; }
            public abstract int DPI_Y { get; }

            public abstract COMPRESSION Compression { get; }
            public abstract int Size { get; }
            public int RequiredSize
            { get { return (BPP * Width + 31) / 32 * 4 * Height; } }

            public bool HasMask => Compression == COMPRESSION.BITFIELDS || Compression == COMPRESSION.ALPHABITFIELDS;

            public virtual uint RedMask => 0;
            public virtual uint GreenMask => 0;
            public virtual uint BlueMask => 0;

            public virtual uint AlphaMask => 0;

            protected Header(byte[] data)
            {
                _data = data;
            }
        }

        //This is probably wrong. Documentation conflicts. Look at: http://fileformats.archiveteam.org/wiki/BMP#OS.2F2_BMP_2.0
        class OS2_v1_header : Header
        {
            public override COMPRESSION Compression
            {
                get { return COMPRESSION.RGB; }
            }

            public override int Size
            {
                get { return (Width * BPP + 31) / 32 * 4 * Height; }
            }

            public override int Width
            {
                get { return StreamReaderEx.ReadUShortLE(0, _data); }
            }

            public override int Height
            {
                get { return StreamReaderEx.ReadUShortLE(2, _data); }
            }

            public override int BPP
            {
                get { return StreamReaderEx.ReadUShortLE(6, _data); }
            }
            public override int NColors
            {
                get { return (int)Math.Pow(2, BPP); }
            }

            public override int DPI_X { get { return 72; } }
            public override int DPI_Y { get { return 72; } }

            public OS2_v1_header(byte[] data)
                : base(data)
            {
                //Number of color planes must always equal 1
                if (StreamReaderEx.ReadUShortLE(4, _data) != 1)
                    throw new InvalidDataException("Not a BMP file");
            }
        }

        class OS2_v2_header : OS2_v1_header
        {
            public override COMPRESSION Compression
            {
                get
                {
                    var cmp = StreamReaderEx.ReadUShortLE(8, _data);
                    if (cmp == 3) return COMPRESSION.Huffman1D;
                    if (cmp == 4) return COMPRESSION.RLE24;
                    return (COMPRESSION)cmp;
                }
            }

            public OS2_v2_header(byte[] data)
                : base(data) { }
        }

        class Win_v1_header : Header
        {
            public override int Width
            {
                get { return StreamReaderEx.ReadIntLE(0, _data); }
            }

            public override int Height
            {
                get { return StreamReaderEx.ReadIntLE(4, _data); }
            }

            /// <summary>
            /// Should always be 1
            /// </summary>
            public int Planes => StreamReaderEx.ReadUShortLE(8, _data);

            public override int BPP
            {
                get { return StreamReaderEx.ReadUShortLE(10, _data); }
            }

            public override COMPRESSION Compression
            {
                get { return (COMPRESSION)StreamReaderEx.ReadIntLE(12, _data); }
            }


            public override int Size
            {
                get
                {
                    if (Compression == COMPRESSION.RGB)
                        return (Width * BPP + 31) / 32 * 4 * Height;
                    return StreamReaderEx.ReadIntLE(16, _data);
                }
            }

            public override int DPI_X
            {
                get { return (int)(StreamReaderEx.ReadIntLE(20, _data) * 0.0254); }
            }

            public override int DPI_Y
            {
                get { return (int)(StreamReaderEx.ReadIntLE(24, _data) * 0.0254); }
            }

            public override int NColors
            {
                get
                {
                    var n_colors = StreamReaderEx.ReadIntLE(28, _data);
                    return n_colors > 0 ? n_colors : (int)Math.Pow(2, BPP);
                }
            }

            public int ImportantColors => StreamReaderEx.ReadIntLE(32, _data);

            public Win_v1_header(byte[] data)
                : base(data)
            {
                //Number of color planes must always equal 1
                if (StreamReaderEx.ReadUShortLE(8, _data) != 1)
                    throw new InvalidDataException("Not a BMP file");
            }
        }

        //https://formats.kaitai.io/bmp/
        class Win_v2_header : Win_v1_header
        {
            public override uint RedMask => StreamReaderEx.ReadUIntLE(36, _data);
            public override uint GreenMask => StreamReaderEx.ReadUIntLE(40, _data);
            public override uint BlueMask => StreamReaderEx.ReadUIntLE(44, _data);

            public Win_v2_header(byte[] data)
                : base(data)
            { }
        }

        class Win_v3_header : Win_v2_header
        {
            public override uint AlphaMask => StreamReaderEx.ReadUIntLE(48, _data);

            public Win_v3_header(byte[] data)
                :base(data)
            { }
        }

        class Win_v4_header : Win_v3_header
        {
            public Win_v4_header(byte[] data)
                : base(data)
            { }
        }

        class Win_v5_header : Win_v4_header
        {
            public Win_v5_header(byte[] data)
                : base(data) { }
        }

        public enum COMPRESSION
        {
            RGB,
            RLE8,
            RLE4,
            BITFIELDS,
            JPEG,
            PNG,
            ALPHABITFIELDS,
            CMYK = 11,
            CMYKRLE8,
            CMYKRLE4,

            Huffman1D = 103, //Real value = 3
            RLE24 = 104 //Real value = 4
        }

        #endregion

        #region Palette

        public abstract class CLUT : IEnumerable<IntColor>
        {
            public abstract IntColor this[int index] { get; }
            public abstract int NColors { get; }
            public abstract int BPC { get; }

            /// <summary>
            /// Lists the colors with alpha values in this palette
            /// </summary>
            public abstract IntColor[] AlphaColors { get; }

            /// <summary>
            /// True if this palette has colors other than shades of gray
            /// </summary>
            public bool HasColor 
            { 
                get
                {
                    foreach (IntColor color in this)
                        if (!color.IsGray) return true;

                    return false;
                }
            }

            /// <summary>
            /// If this palette features an Alpha channel
            /// </summary>
            public bool HasAlpha
            {
                get
                {
                    if (this is RGBA32Palette)
                    {
                        bool has_alpha = false, has_opague = false;
                        foreach (IntColor color in AlphaColors)
                        {
                            if (color.Alpha < 1)
                                has_alpha = true;
                            else if (color.Alpha > 0)
                                has_opague = true;
                        }

                        return has_alpha && has_opague;
                    }

                    return false;
                }
            }

            public System.Collections.IEnumerator GetEnumerator()
            {
                var l = Math.Min(NColors, 512);
                for (int c = 0; c < l; c++)
                    yield return this[c];
            }
            IEnumerator<IntColor> IEnumerable<IntColor>.GetEnumerator()
            {
                var l = Math.Min(NColors, 512);
                for (int c = 0; c < l; c++)
                    yield return this[c];
            }
        }

        class IdentityPalette : CLUT
        {
            public override int NColors { get { return int.MaxValue; } }
            public override int BPC => 32;
            public override IntColor[] AlphaColors { get { return new IntColor[0]; } }
            public override IntColor this[int index]
            {
                get
                {
                    return IntColor.FromInt(index);
                }
            }
        }

        class RGBA32Palette : CLUT
        {
            byte[] _data;
            public RGBA32Palette(byte[] data) { _data = data; }
            public override int NColors { get { return _data.Length / 4; } }
            public override int BPC => 8;
            int NAlpha
            {
                get
                {
                    int count = 0;
                    for (int c = 3; c < _data.Length; c += 4)
                        if (_data[c] < 0xFF)
                            count++;
                    return count;
                }
            }
            public override IntColor[] AlphaColors
            {
                get
                {
                    var colors = new IntColor[NAlpha];
                    int count = 0;
                    for (int c = 3; c < _data.Length; c += 4)
                        if (_data[c] < 0xFF)
                            colors[count] = this[count++];
                    return colors;
                }
            }
            public override IntColor this[int index]
            {
                get
                {
                    return IntColor.FromUInt(StreamReaderEx.ReadUIntLE(index * 4, _data));
                }
            }
        }

        #endregion
    }
}
