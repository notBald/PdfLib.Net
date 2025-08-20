#define COMPRESS_DATA
//#define INCLUDE_VIEWMODE
using PdfLib.Pdf;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Filter;
using PdfLib.Util;
using System;
using System.IO;

/// <summary>
/// Parser for Amiga IFF images
/// 
/// @see https://wiki.amigaos.net/wiki/ILBM_IFF_Interleaved_Bitmap
/// </summary>
/// <remarks>
/// Todo: Color key and Lasso mask. 
/// </remarks>
namespace PdfLib.Img
{
    public class IFF
    {
        readonly Header _head;
        readonly byte[] _cmap;
        readonly byte[] _alpha;
        readonly byte[] _body;
        readonly int _bpp;

        public int Width => _head.Width;
        public int Height => _head.Height;
        public int BitsPerPixel => _bpp;
        public byte[] CMap => _cmap;
        public byte[] Body => _body;
        public byte[] AlphaMask => _alpha;

        public double Aspect => _head.YAspect != 0 ? _head.XAspect / (double)_head.YAspect : 1;

        private IFF(Header h, byte[] b, byte[] c, int bpp, byte[] a)
        {
            _head = h;
            _cmap = c;
            _body = b;
            _bpp = bpp;
            _alpha = a;
        }

        public void Save(Stream s)
        {
            if (_head.NumberOfPlanes > 8 || _cmap == null || _alpha != null)
                throw new NotImplementedException();

            int n_planes = _head.NumberOfPlanes;
            int width = _head.Width, height = _head.Height;
            int bit_stride = (width + 15) / 16 * 2; //In bytes
            int stride = bit_stride * n_planes;
            var dest = new byte[stride * height];
            BitWriter[] bw = new BitWriter[n_planes];
            for (int c=0; c < bw.Length; c++)
                bw[c] = new BitWriter(dest);
            var br = new BitStream(_body);

            for (int h = 0, write_pos = 0; h < height; h++)
            {
                for(int c=0, row_pos = 0; c < n_planes; c++, row_pos += bit_stride)
                {
                    var one_bw = bw[c];
                    one_bw.Flush();
                    one_bw.Position = write_pos + row_pos;
                }

                int skip = BitsPerPixel - n_planes;
                for (int w = 0; w < width; w++)
                {
                    br.Skip(skip);
                    for (int plane_nr = n_planes - 1; plane_nr >= 0; plane_nr--)
                    {
                        bw[plane_nr].WriteBit(br.FetchBits(1));
                    }
                }

                write_pos += stride;
                br.ByteAlign();
            }

            for (int c = 0; c < n_planes; c++)
                bw[c].Dispose();

            if (_head.Compression == Compression.ByteRun1)
                dest = PdfRunLengthFilter.Encode(dest, bit_stride);

            //ILBM + "BMHD" + SIZE and BMHD.LENGTH + "BODY" and SIZE + BODY.Length + cmap and camg...
            int size_of_file = 4 + 8 + 20 + 8 + dest.Length + 8 + _cmap.Length;
#if INCLUDE_VIEWMODE
            size_of_file += 12;
#endif

            if ((_cmap.Length & 1) != 0)
                size_of_file++;
            if ((dest.Length & 1) != 0)
                size_of_file++;

            var sw = new StreamWriterEx(s, true);

            //FORM
            sw.Write(new byte[] { 0x46, 0x4F, 0x52, 0x4D }, 0, 4);
            sw.Write(size_of_file);

            //ILBM
            sw.Write(new byte[] { 0x49, 0x4C, 0x42, 0x4D }, 0, 4);

            //Header
            sw.Write(Read.Lexer.GetBytes("BMHD"), 0, 4);
            sw.Write(20);
            sw.Write(_head.Bytes, 0, 20);

            //Color palette
            sw.Write(Read.Lexer.GetBytes("CMAP"), 0, 4);
            sw.Write(_cmap.Length);
            sw.Write(_cmap, 0, _cmap.Length);
            if ((_cmap.Length & 1) != 0)
                sw.Write((byte)0);

            //Viewmode
#if INCLUDE_VIEWMODE
            sw.Write(Read.Lexer.GetBytes("CAMG"), 0, 4);
            sw.Write(4);
            sw.Write((int)(ViewMode.LACE | ViewMode.HIRES));
#endif

            //Image data
            sw.Write(Read.Lexer.GetBytes("BODY"), 0, 4);
            sw.Write(dest.Length);
            sw.Write(dest, 0, dest.Length);
            if ((dest.Length & 1) != 0)
                sw.Write((byte)0);

            System.Diagnostics.Debug.Assert(size_of_file + 8 == s.Length);
        }



        private static byte[] Uncompress(byte[] body, Header head)
        {
            //Source data is organized into ushort aligned bitplanes, which means each plane has "1bpp".
            //By adding 15 and dividing by 16 we get an ushort aligned width of half the byte length. So
            //we multiply it by two to get the full byte length.
            int stride = (head.Width + 15) / 16 * 2;
            int nr_planes = head.NumberOfPlanes;
            if (head.Masking == Masking.HasMask)
                nr_planes++;
            int full_stride = stride * nr_planes;

            byte[] dest = new byte[full_stride * head.Height];
            for (int read = 0, write = 0; /* read < body.Length && */ write < dest.Length;)
            {
                int n = (sbyte)body[read++];

                if (n < 0)
                {
                    //Adobe Photoshop incorrectly use the n=128 no-op as a repeat code, so
                    //we must allow it. No writer use noop anyway, so this probably breaks
                    //nothing.
                    //if (n > -128)
                    {
                        n = -n + 1;
                        byte next = body[read++];
                        for (int c = 0; c < n; c++)
                            dest[write++] = next;
                    }
                }
                else
                {
                    Buffer.BlockCopy(body, read, dest, write, ++n);
                    read += n;
                    write += n;
                }
            }

            return dest;
        }

        /// <summary>
        /// http://netghost.narod.ru/gff/graphics/summary/iff.htm
        /// </summary>
        private static IFF CreateHamIFF(Header head, byte[] body, byte[] cmap)
        {
            var iff = CreateIFF(head, body, cmap);

            //We convert the HAM6/8 image into a 24-bit image
            body = iff.Body;
            int w = head.Width;
            byte[] dest = new byte[w * head.Height * 3];
            byte[] rgb = new byte[3];
            int shift = head.NumberOfPlanes - 2, right_shift = 8 - shift;
            int clear = ~(0xFF << shift) & 0xFF, rgb_clear = ~(0xFF << right_shift) & 0xFF;

            for (int r = 0, col = w; r < body.Length; r++, col++)
            {
                //On the left edge of the screen use the color index zero from the color palette. 
                if (col == w)
                {
                    Array.Copy(cmap, 0, rgb, 0, 3);
                    col = 0;
                }

                var ham_pixel = body[r];
                var value = ham_pixel & clear;

                var mode = (HAM_MODE)(ham_pixel >> shift);
                if (mode == 0)
                {
                    //Use the 4 bits of data to index a color from the 16 color palette. Use that color for this pixel.
                    Array.Copy(cmap, value * 3, rgb, 0, 3);
                }
                else
                {
                    //Modify one of the three colors
                    int adr = (int)mode - 2;
                    if (adr < 0) adr = 2;
                    rgb[adr] = (byte)(value << right_shift | rgb[adr] & rgb_clear);
                }

                Array.Copy(rgb, 0, dest, r * 3, 3);
            }

            //HAM6 happens to be 4bpc, which PDF allows. Doing it this way is a little ugly, but at the
            //same time since the four least significant bits are always zero I belive the result is 100%
            //accurate despite the floating point math going on inside the ChangeBPC func. 
            if (shift == 4)
            {
                var bpp12 = Pdf.PdfImage.ChangeBPC(new Pdf.PdfImage(dest, Pdf.ColorSpace.DeviceRGB.Instance, w, head.Height, 8), 4, false);
                return new IFF(head, bpp12.Stream.RawStream, null, 12, null);
            }

            return new IFF(head, dest, null, 24, null);
        }

        enum HAM_MODE : byte
        {
            SET,
            ModifyBlue,
            ModifyRED,
            ModifyGreen
        }

        /// <summary>
        /// http://netghost.narod.ru/gff/graphics/summary/iff.htm
        /// </summary>
        private static IFF CreateIFF(Header head, byte[] body)
        {
            if (head.NumberOfPlanes != 24)
                throw new NotImplementedException();

            int n_planes = head.NumberOfPlanes;
            var br = new BitStream(body);
            int width = head.Width, height = head.Height;
            int skip = (width + 15) / 16 * 16 - width;
            System.Diagnostics.Debug.Assert(skip == 0, "Untested code");
            var dest = new int[width * height];

            for (int h = 0; h < height; h++)
            {
                for (int plane_nr = 0; plane_nr < n_planes; plane_nr++)
                {
                    //int shift = n_planes - plane_nr - 1;
                    int wb = h * width;

                    for (int c = 0; c < width; c++)
                        dest[wb++] |= br.FetchBits(1) << plane_nr;

                    br.Skip(skip);

                }

                //if (alpha_write != null)
                //{
                //    for (int c = 0; c < width; c++)
                //        alpha_write.Write(br.FetchBits(1), 1);

                //    br.Skip(skip);
                //}
            }

            body = new byte[dest.Length * 3];
            for (int r = 0, w = 0; r < dest.Length; r++)
            {
                int color = dest[r];
                body[w++] = (byte)color;
                body[w++] = (byte)(color >> 8);
                body[w++] = (byte)(color >> 16);
            }

            return new IFF(head, body, null, 24, null);
        }

        private static IFF CreateIFF(Header head, byte[] body, byte[] cmap)
        {
            if (head.Compression == Compression.ByteRun1)
                body = Uncompress(body, head);

            if (cmap == null && head.NumberOfPlanes > 8)
                return CreateIFF(head, body);

            //PDF only supports sizes of 1,2,4,8 and 16. With that in mind we convert to a format more
            //easily digestible by PDF. To simplify the algorithm, we start by putting all planes into
            //8bpp pixels
            int n_planes = head.NumberOfPlanes;
            if (n_planes > 8)
                throw new NotImplementedException("More than 8 planes in IFF image");

            var br = new BitStream(body);
            int width = head.Width, height = head.Height;
            int skip = (width + 15) / 16 * 16 - width;
            var dest = new byte[width * height];

            byte[] alpha_mask = null;
            BitWriter alpha_write = null;
            if (head.Masking == Masking.HasMask)
            {
                alpha_mask = new byte[(head.Width * head.Height + 7) / 8];
                alpha_write = new BitWriter(alpha_mask);

                System.Diagnostics.Debug.Assert(false, "Untested code");
            }

            for (int h = 0; h < height; h++)
            {
                for (int plane_nr = 0; plane_nr < n_planes; plane_nr++)
                {
                    //int shift = n_planes - plane_nr - 1;
                    int wb = h * width;

                    for (int c = 0; c < width; c++)
                        dest[wb++] |= (byte)(br.FetchBits(1) << plane_nr);

                    br.Skip(skip);

                }

                if (alpha_write != null)
                {
                    for (int c = 0; c < width; c++)
                        alpha_write.Write(br.FetchBits(1), 1);

                    br.Skip(skip);
                }
            }
            if (alpha_write != null)
                alpha_write.Dispose();

            int write = n_planes;
            if (n_planes > 2)
            {
                if (n_planes > 4)
                    write = 8;
                else if (n_planes == 3)
                    write = 4;
            }

            if (cmap == null)
            {
                //Must scale values to the nearest bitsize
                int max = (1 << write) - 1;
                int old_max = (1 << n_planes) - 1;
                double scale = max / (double)old_max;

                for (int c = 0; c < dest.Length; c++)
                    dest[c] = (byte)Math.Round(dest[c] * scale);

                System.Diagnostics.Debug.Assert(false, "Untested code");
            }

            if (write < 8)
            {
                int stride = (width * write + 7) / 8;
                var d = new byte[stride * height];
                skip = stride * 8 - (width * write);
                var wb = new BitWriter(d);
                for (int c = 0, s = 1; c < dest.Length; c++, s++)
                {
                    wb.Write(dest[c], write);
                    if (s == width)
                    {
                        wb.Skip(skip);
                        s = 0;
                    }
                }

                wb.Dispose();
                dest = d;
            }

            return new IFF(head, dest, cmap, write, alpha_mask);
        }

        public static IFF Open(Stream stream)
        {
            var sr = new StreamReaderEx(stream, true);
            byte[] buffer = new byte[4];

            //Checks the identifier (FORM)
            sr.ReadEx(buffer, 0, 4);
            if (!ArrayHelper.ArraysEqual(buffer, new byte[] { 0x46, 0x4F, 0x52, 0x4D }))
                throw new InvalidDataException("Not an IFF file");

            //Reads out the size of the IFF file, negative the 8 byte header.
            uint size = sr.ReadUInt();

            //IFF images have a "PBM " or "ILBM" header
            sr.ReadEx(buffer, 0, 4);
#pragma warning disable IDE0059 // Unnecessary assignment of a value
#pragma warning disable CS0219 // Variable is assigned but its value is never used
            bool isILBM = false;
#pragma warning restore CS0219 // Variable is assigned but its value is never used
            if (ArrayHelper.ArraysEqual(buffer, new byte[] { 0x49, 0x4C, 0x42, 0x4D }))
                isILBM = true;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
            else if (ArrayHelper.ArraysEqual(buffer, new byte[] { 0x50, 0x42, 0x4D, 0x20 }))
                //'Packed Bitmap', probably a chunky mode.
                throw new NotImplementedException("PBM iff files");
            else
                throw new InvalidDataException("Not an IFF image file");

            //Reads out all the chunks
            uint bytes_read = 4;
            Header head = null; byte[] cmap = null; byte[] body = null; ViewMode vm = ViewMode.NONE;
            while (bytes_read < size)
            {
                sr.ReadEx(buffer, 0, 4);
                uint chunk_size = sr.ReadUInt();

                string chunk_name = Read.Lexer.GetString(buffer);
                switch (chunk_name)
                {
                    case "BMHD":
                        head = ReadBMHD(sr, chunk_size);
                        break;

                    case "CMAP":
                        //if (chunk_size % 3 != 0) throw new InvalidDataException("Ivalid IFF image color map");
                        cmap = new byte[chunk_size];
                        sr.ReadEx(cmap, 0, (int)chunk_size);
                        break;

                    case "BODY":
                        body = new byte[chunk_size];
                        sr.ReadEx(body, 0, (int)chunk_size);
                        break;

                    case "CAMG":
                        if (chunk_size != 4) throw new InvalidDataException("Ivalid IFF CAMG chunk");
                        vm = (ViewMode)sr.ReadInt();
                        break;

                    default:
                        if ((chunk_size & 1) != 0) chunk_size++;
                        sr.Seek(chunk_size, SeekOrigin.Current);
                        break;
                }

                if ((chunk_size & 1) != 0)
                {
                    chunk_size++;
                    sr.ReadByte();
                }
                bytes_read += chunk_size + 8;
            }

            if (head == null || body == null)
                throw new InvalidDataException("IFF image header missing");

            if (cmap != null)
            {
                if ((vm & ViewMode.HAM) != 0 || 
                    vm == ViewMode.NONE && head.NumberOfPlanes == 6 && cmap.Length != 64 * 3)
                    return CreateHamIFF(head, body, cmap);

                int cmap_size = 3 * (1 << head.NumberOfPlanes);
                if ((vm & ViewMode.HALFBRITE) != 0)
                {
                    //We create a halfbrite palette
                    byte[] hb = new byte[cmap_size];
                    cmap_size /= 2;

                    if (cmap.Length > cmap_size)
                        //Trunctuate the cmap (some iff files have junk in the "half bright" palette
                        Array.Resize<byte>(ref cmap, cmap_size);
                    else if (cmap.Length < cmap_size)
                        throw new InvalidDataException("Ivalid IFF image color map");

                    Buffer.BlockCopy(cmap, 0, hb, 0, cmap_size);
                    for (int r = 0, w = cmap_size; r < cmap_size; r++, w++)
                        hb[w] = (byte)(cmap[r] >> 1);
                    cmap = hb;
                }
                else if (cmap.Length != cmap_size)
                {
                    if (cmap.Length > cmap_size)
                        //Trunctuate the cmap
                        Array.Resize<byte>(ref cmap, cmap_size);
                    else
                        throw new InvalidDataException("Ivalid IFF image color map");
                }
            }

            return CreateIFF(head, body, cmap);
        }

        public static IFF CreateFrom(PdfImage img)
        {
            //Only indexed images are supported for now
            img = PdfImage.MakeIndexed(img);
            var idx_cs = (IndexedCS)img.ColorSpace;
            var clut = idx_cs.Lookup;
            var n_bits = (int)Math.Ceiling(Math.Log((idx_cs.Hival + 1), 2));
            int bpp = 8;
            if (idx_cs.Hival < 256)
            {
                var pal_size = (int) Math.Pow(2, n_bits) * 3;
                Array.Resize(ref clut, pal_size);

                if (idx_cs.Hival <= 16)
                {
                    bpp = 4;
                    if (idx_cs.Hival <= 4)
                        bpp = 2;
                    img = PdfImage.ChangeBPC(img, bpp, false, false);
                }
            }

            var header_bytes = new byte[20];
            var sw = new StreamWriterEx(header_bytes, true);

            sw.Write((ushort)img.Width);
            sw.Write((ushort)img.Height);
            sw.Skip(4); //XOrigin and YOrigin
            sw.Write((byte)n_bits); //Number of bitplanes
            sw.Write((byte)Masking.None);
#if COMPRESS_DATA
            sw.Write((byte)Compression.ByteRun1);
#else
            sw.Write((byte)Compression.None);
#endif
            sw.Skip(1); //Unused padding
            sw.Skip(2); //Transparent color
            sw.Write((byte)1); //XAspect
            sw.Write((byte)1); //YAspect
            sw.Write((short)img.Width); //Page width
            sw.Write((short)img.Height); //Page height

            
            return new IFF(new Header(header_bytes), img.Stream.DecodedStream, clut, bpp, null);
        }

        private static Header ReadBMHD(StreamReaderEx sr, uint size)
        {
            if (size != 20) throw new InvalidDataException("Corrupt IFF image file");
            byte[] header = new byte[20];
            sr.ReadEx(header, 0, 20);
            return new Header(header);
        }

        class Header
        {
            readonly byte[] _data;

            internal byte[] Bytes => _data;

            /// <summary>
            /// Number of bitplanes
            /// </summary>
            public byte NumberOfPlanes => _data[8];

            public Masking Masking => (Masking)_data[9];

            public Compression Compression => (Compression)_data[10];

            ///// <summary>
            ///// Unused padding
            ///// </summary>
            //public byte Pad1 => _data[11];

            public ushort TransparentColor => StreamReaderEx.ReadUShortBE(12, _data);

            /// <summary>
            /// Aspect ratio in width
            /// </summary>
            public short XAspect => _data[14];

            /// <summary>
            /// Aspect ratio in height
            /// </summary>
            public short YAspect => _data[15];

            /// <summary>
            /// X position of the image on the page
            /// </summary>
            public short XOrigin => StreamReaderEx.ReadShortBE(4, _data);

            /// <summary>
            /// Y position of the image on the page
            /// </summary>
            public short YOrigin => StreamReaderEx.ReadShortBE(6, _data);

            /// <summary>
            /// Width of the image in pixels
            /// </summary>
            public ushort Width => StreamReaderEx.ReadUShortBE(0, _data);

            /// <summary>
            /// Height of the image in pixels
            /// </summary>
            public ushort Height => StreamReaderEx.ReadUShortBE(2, _data);

            /// <summary>
            /// source "page" width in pixels
            /// </summary>
            public short PageWidth => StreamReaderEx.ReadShortBE(16, _data);

            /// <summary>
            /// source "page" height in pixels
            /// </summary>
            public short PageHeight => StreamReaderEx.ReadShortBE(18, _data);

            public Header(byte[] data)
            {
                _data = data;
            }
        }

        public enum Masking : byte
        {
            None,
            HasMask,
            HasTransparentColor,
            Lasso
        }

        public enum Compression : byte
        {
            None,
            ByteRun1
        }

        [Flags()]
        private enum ViewMode
        {
            NONE = 0x0,
            LACE = 0x4,
            HALFBRITE = 0x80,
            HAM = 0x800,
            HIRES = 0x8000,
        }
    }
}
