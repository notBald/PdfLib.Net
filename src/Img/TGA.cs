using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Util;

namespace PdfLib.Img
{
    /// <summary>
    /// Simple Targa image parser
    /// </summary>
    /// <remarks>
    /// Todo: Undo premultiplied alpha. It's basically to split every color channel on the alpha channel. I.e. (int) (color / (double) alpha)
    /// </remarks>
    public class TGA
    {
        Header _head;
        Footer _foot;
        Bmp.CLUT _pal;
        byte[] _data;

        /// <summary>
        /// If this is a color image or not
        /// </summary>
        public bool HasColor => _head.ImageType != ImageType.RUN_LENGTH_ENCODED_BLACK_AND_WHITE && _head.ImageType != ImageType.UNCOMPRESSED_BLACK_AND_WHITE;
        public bool HasAlpha
        {
            get
            {
                if (_head.BitsPerPixel == 24 && _head.AttributesPerPixel == 8)
                    return false;
                if (_head.AttributesPerPixel > 0 && _head.AttributesPerPixel < _head.BitsPerPixel)
                {
                    if (_foot != null)
                        return _foot.AtrribType == AtrribType.Alpha || _foot.AtrribType == AtrribType.PreMultipliedAlpha;
                    return true;
                }
                return false;
            }
        }
        public bool HasPreMultipliedAlpha => HasAlpha && _foot != null && _foot.AtrribType == AtrribType.PreMultipliedAlpha;

        public Bmp.CLUT Palette => _pal;
        public int BitsPerPixel => _head.BitsPerPixel;
        public int BitsPerComponent
        {
            get
            {
                if (Palette != null)
                {
                    switch (_head.ColorMapEnterySize)
                    {
                        case 15:
                        case 16: return 5;
                        case 24:
                        case 32: return 8;
                        default: throw new NotSupportedException("Pallette with bpp size: " + _head.ColorMapEnterySize);
                    }
                }

                if (HasColor)
                {
                    switch (BitsPerPixel)
                    {
                        case 15:
                        case 16: return 5;
                        case 24:
                        case 32: return 8;
                        default: throw new NotSupportedException("Targa image with bpp size: " + _head.ColorMapEnterySize);
                    }
                }

                return BitsPerPixel;
            }
        }
        public int Width => _head.Width;
        public int Height => _head.Height;
        public byte[] Pixels => _data;

        private TGA(Header header, Footer foot, Bmp.CLUT pal, byte[] pixels)
        {
            _head = header;
            _foot = foot;
            _pal = pal;
            _data = pixels;
        }

        public static TGA Open(Stream stream)
        {
            //Reads out the header
            long start = stream.Position;
            var sr = new StreamReaderEx(stream, false);

            const int HeaderByteLength = 18;
            byte[] buffer = new byte[HeaderByteLength];
            sr.ReadEx(buffer, 0, buffer.Length);

            var header = new Header(buffer);
            if (!header.IsValid)
                throw new InvalidDataException("Not a TGA file");
            stream.Seek(header.ID_Length, SeekOrigin.Current);

            //Reads out the palette
            Bmp.CLUT clut = null;
            if (header.HasColorMap)
            {
                int length = header.ColorMapLength;

                byte[] data = new byte[header.ColorMapLength * ((header.ColorMapEnterySize + 7) / 8)];
                sr.ReadEx(data, 0, data.Length);

                //Specs are unclear on how color is determined in a ColorMap, but it appears general usage is 555, 5551, 888 and 8888.
                switch (header.ColorMapEnterySize)
                {
                    case 15:
                        clut = new RGB15Palette(data, header.FirstColorMapIndex);
                        break;
                    case 16:
                        if (header.AttributesPerPixel == 0) goto case 15;
                        clut = new RGBA16Palette(data, header.FirstColorMapIndex);
                        break;
                    case 24:
                        clut = new RGB24Palette(data, header.FirstColorMapIndex);
                        break;
                    case 32:
                        if (header.AttributesPerPixel == 8)
                            clut = new RGBA32Palette(data, header.FirstColorMapIndex);
                        else
                            clut = new RGB32Palette(data, header.FirstColorMapIndex);
                        break;

                    default:
                        throw new NotImplementedException("Targa colormap with entery size: " + header.ColorMapEnterySize);
                }
            }

            //Reads out the image data.
            int stride = header.Width * header.BitsPerPixel / 8;
            int size = header.Height * stride;
            byte[] raw = new byte[size];
            if (header.ImageType >= ImageType.RUN_LENGTH_ENCODED_COLOR_MAPPED)
            {
                byte[] one_pixel = new byte[header.BitsPerPixel / 8];

                //We do not know the size of the compressed data, so we decompress it
                for (int bytes_read = 0; bytes_read < size;)
                {
                    //Repetition Count field
                    byte rcf = sr.ReadByte();
                    int rcf_low = (rcf & 0x7F) + 1;

                    if ((rcf & 0x80) != 0)
                    {
                        //Run-length Packet

                        //Reads one pixel, this  will be repeated (rcf_low + 1) number of times
                        sr.ReadEx(one_pixel, 0, one_pixel.Length);

                        for (int c = 0; c < rcf_low; c++)
                        {
                            for (int k = 0; k < one_pixel.Length; k++)
                                raw[bytes_read++] = one_pixel[k];
                        }
                    }
                    else
                    {
                        //Raw Packets
                        int num_bytes_to_read = one_pixel.Length * rcf_low;
                        sr.ReadEx(raw, bytes_read, num_bytes_to_read);

                        bytes_read += num_bytes_to_read;
                    }
                }
            }
            else
            {
                sr.ReadEx(raw, 0, raw.Length);
            }

            //Flips the data
            if (header.VerticalFlip)
            {
                int top = 0, bottom = raw.Length - stride, end = raw.Length / 2;
                buffer = new byte[stride];

                for (; top < end; top += stride, bottom -= stride)
                {
                    // 1. Copy top to buffer
                    Buffer.BlockCopy(raw, top, buffer, 0, stride);

                    //2. Copy bottom to top
                    Buffer.BlockCopy(raw, bottom, raw, top, stride);

                    //3. Copy buffer to bottom
                    Buffer.BlockCopy(buffer, 0, raw, bottom, stride);
                }
            }
            if (header.HorizontalFlip)
            {
                //buffer = new byte[stride];
                for (int top = 0; top < raw.Length; top += stride)
                {
                    // 1. Copy the raw data to the buffer
                    //Buffer.BlockCopy(raw, top, buffer, 0, stride);

                    // 2. Reverse the data
                    Array.Reverse(raw, top, stride);
                }

#if DEBUG
                System.Diagnostics.Debug.Assert(false, "This codepath has not been tested");
#endif
            }

            //Reads out the footer
            Footer footer = null;
            const int FooterOffsetFromEnd = 18;
            byte[] FooterASCIISignature = new byte[] { 84, 82, 85, 69, 86, 73, 83, 73, 79, 78, 45, 88, 70, 73, 76, 69 }; // "TRUEVISION-XFILE"
            if (stream.Length > FooterOffsetFromEnd + HeaderByteLength)
            {
                sr.Seek(-FooterOffsetFromEnd, SeekOrigin.End);
                buffer = new byte[FooterASCIISignature.Length];

                //This read is so small the chance of not everything being read is negiable. If it happens, this will be treated as a tga10 image.
                sr.ReadEx(buffer, 0, buffer.Length);

                if (ArrayHelper.ArraysEqual(buffer, FooterASCIISignature))
                {
                    //Reads the reserved character
                    byte reserved_char = sr.ReadByte();

                    //Moves to the start of the footer
                    const int FooterLength = 26;
                    stream.Seek(-FooterLength, SeekOrigin.End);

                    //Position of the Extention area
                    uint ext_offset = sr.ReadUInt();

                    if (ext_offset > 0)
                        footer = ReadExtArea(sr, ext_offset, reserved_char);
                }
            }

            sr.Position = start;
            return new TGA(header, footer, clut, raw); ;
        }

        private static Footer ReadExtArea(StreamReaderEx sr, uint offset, byte reserved)
        {
            sr.Seek(offset);
            ushort ext_size = sr.ReadUShort();

            if (ext_size < 465)
                return null;

            sr.Seek(465, SeekOrigin.Current);

            // Color key
            byte a = sr.ReadByte();
            byte r = sr.ReadByte();
            byte g = sr.ReadByte();
            byte b = sr.ReadByte();

            //Aspect ratio for pixels
            sr.Seek(4, SeekOrigin.Current);

            uint gamma_numerator = sr.ReadUInt();
            uint gamma_denomiator = sr.ReadUInt(); //If zero = no gamma

            //Color correction, postage stamp (thumb), scan line offset
            sr.Seek(12, SeekOrigin.Current);

            //Attribues that determine transparency
            AtrribType attrib = (AtrribType)sr.ReadByte();

            return new Footer(new IntColor(r, g, b, a), gamma_numerator, gamma_denomiator, attrib);
        }

        class Header
        {
            readonly byte[] _data;

            public byte ID_Length => _data[0];

            public bool HasColorMap => _data[1] == 1;

            public ImageType ImageType => (ImageType)_data[2];

            public ushort FirstColorMapIndex => StreamReaderEx.ReadUShortLE(3, _data);

            public ushort ColorMapLength => StreamReaderEx.ReadUShortLE(5, _data);

            public byte ColorMapEnterySize => _data[7];

            public ushort XOrigin => StreamReaderEx.ReadUShortLE(8, _data);
            public ushort YOrigin => StreamReaderEx.ReadUShortLE(10, _data);
            public ushort Width => StreamReaderEx.ReadUShortLE(12, _data);
            public ushort Height => StreamReaderEx.ReadUShortLE(14, _data);

            public byte BitsPerPixel => _data[16];
            public int AttributesPerPixel => _data[17] & 0x0F;

            public bool HorizontalFlip => (_data[17] & 0x10) != 0;
            public bool VerticalFlip => (_data[17] & 0x20) == 0;

            public bool IsValid
            {
                get
                {
                    if (_data[1] > 1) return false;
                    byte image_type = _data[2];
                    if (image_type == 0 || image_type > 11 || image_type > 3 && image_type < 9)
                        return false;
                    if (_data[1] == 1)
                    {
                        if (image_type != 1 && image_type != 9)
                            return false;
                    }
                    else
                    {
                        if (image_type == 1 || image_type == 9)
                            return false;
                    }
                    if (_data[1] == 0)
                    {
                        if (FirstColorMapIndex != 0 || ColorMapLength != 0 || ColorMapEnterySize != 0)
                            return false;
                    }
                    else
                    {
                        if (ColorMapLength == 0 || ColorMapEnterySize == 0)
                            return false;
                    }
                    int bpp = BitsPerPixel;
                    if (Width == 0 || Height == 0 || bpp != 8 && bpp != 16 && bpp != 24 && bpp != 32)
                        return false;
                    switch (bpp)
                    {
                        case 32:
                        case 24: if (AttributesPerPixel != 8 && AttributesPerPixel != 0) return false; break;
                        case 16: if (AttributesPerPixel > 1) return false; break;
                        case 8: if (AttributesPerPixel > 0 && !HasColorMap && AttributesPerPixel != bpp) return false; break;
                        default: if (AttributesPerPixel > 0) return false; break;
                    }
                    return true;
                }
            }

            public Header(byte[] data)
            {
                _data = data;
            }
        }

        class Footer
        {
            readonly IntColor _color_key;
            readonly uint gamma_numerator, gamma_denomiator;
            public bool HasGamma => gamma_denomiator != 0;
            public double Gamma => gamma_numerator / (double)gamma_denomiator;
            public AtrribType AtrribType { get; private set; }

            public Footer(IntColor ck, uint gamma_n, uint gamma_d, AtrribType at)
            {
                _color_key = ck;
                gamma_numerator = gamma_n;
                gamma_denomiator = gamma_d;
                AtrribType = at;
            }
        }

        #region Palette

        class RGB15Palette : Bmp.CLUT
        {
            byte[] _data;
            BitStream _bs;
            int _offset;
            public RGB15Palette(byte[] data, int offset) { _data = data; _offset = offset; _bs = new BitStream(data); SwapBytes(data); }
            public override int NColors { get { return _data.Length / 2 + _offset; } }
            public override int BPC => 5;
            public override IntColor[] AlphaColors => new IntColor[0];
            public override IntColor this[int index]
            {
                get
                {
                    if (index < _offset) return IntColor.FromUInt(0u);
                    _bs.StreamPosition = (index - _offset) * 2;
                    _bs.ClearBits(1);
                    byte r = (byte)_bs.FetchBits(5);
                    byte g = (byte)_bs.FetchBits(5);
                    byte b = (byte)_bs.FetchBits(5);

                    return new IntColor(r, g, b);
                }
            }

            internal static void SwapBytes(byte[] data)
            {
                if (data.Length % 2 != 0) throw new NotSupportedException("Uneven number of bytes in a 16 bpp palette");

                //For some reason this is a byteswapped ARGB 1555 format
                byte swap;
                for (int c = 0; c < data.Length;)
                {
                    swap = data[c];
                    data[c] = data[++c];
                    data[c++] = swap;
                }
            }
        }

        class RGBA16Palette : Bmp.CLUT
        {
            byte[] _data;
            BitStream _bs;
            int _offset;
            public RGBA16Palette(byte[] data, int offset) { _data = data; _offset = offset; _bs = new BitStream(data); RGB15Palette.SwapBytes(data); }
            public override int NColors { get { return _data.Length / 2 + _offset; } }
            public override int BPC => 5;
            int NAlpha
            {
                get
                {
                    int count = 0;
                    for (int c = 1; c < _data.Length; c += 2)
                        if ((_data[c] & 1) == 0)
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
                    for (int c = 0; c < _data.Length; c += 2)
                        if ((_data[c] & 0x80) == 0)
                            colors[count] = this[count++];
                    return colors;
                }
            }
            public override IntColor this[int index]
            {
                get
                {
                    if (index < _offset) return IntColor.FromUInt(0u);
                    _bs.StreamPosition = (index - _offset) * 2;
                    byte a = (byte)_bs.FetchBits(1);
                    byte r = (byte)_bs.FetchBits(5);
                    byte g = (byte)_bs.FetchBits(5);
                    byte b = (byte)_bs.FetchBits(5);
                    return new IntColor(r, g, b, a == 1 ? (byte)255 : (byte)0);
                }
            }
        }

        class RGB24Palette : Bmp.CLUT
        {
            byte[] _data;
            int _offset;
            public RGB24Palette(byte[] data, int offset) { _data = data; _offset = offset; }
            public override int NColors { get { return _data.Length / 3 + _offset; } }
            public override int BPC => 8;
            public override IntColor[] AlphaColors => new IntColor[0];
            public override IntColor this[int index]
            {
                get
                {
                    if (index < _offset) return IntColor.FromUInt(0u);
                    byte[] buf = new byte[4];
                    for (int pos = (index - _offset) * 3 + 2, count = 2; count >= 0; count--, pos--)
                        buf[count] = _data[pos];
                    return IntColor.FromUInt(StreamReaderEx.ReadUIntLE(0, buf));
                }
            }
        }

        class RGB32Palette : Bmp.CLUT
        {
            byte[] _data;
            int _offset;
            public RGB32Palette(byte[] data, int offset) { _data = data; _offset = offset; }
            public override int NColors { get { return _data.Length / 4 + _offset; } }
            public override int BPC => 8;
            public override IntColor[] AlphaColors => new IntColor[0];
            public override IntColor this[int index]
            {
                get
                {
                    if (index < _offset) return IntColor.FromUInt(0u);
                    return IntColor.FromUInt(StreamReaderEx.ReadUIntLE((index - _offset) * 4, _data) | 0xFF000000);
                }
            }
        }

        class RGBA32Palette : Bmp.CLUT
        {
            byte[] _data;
            int _offset;
            public RGBA32Palette(byte[] data, int offset) { _data = data; _offset = offset; }
            public override int NColors { get { return _data.Length / 4 + _offset; } }
            public override int BPC => 8;
            int NAlpha
            {
                get
                {
                    int count = 0;
                    for (int c = 0; c < _data.Length; c += 4)
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
                    for (int c = 0; c < _data.Length; c += 4)
                        if (_data[c] < 0xFF)
                            colors[count++] = this[c / 4 + _offset];
                    return colors;
                }
            }
            public override IntColor this[int index]
            {
                get
                {
                    if (index < _offset) return IntColor.FromUInt(0u);
                    return IntColor.FromUInt(StreamReaderEx.ReadUIntLE((index - _offset) * 4, _data));
                }
            }
        }

        #endregion

        enum ImageType
        {
            UNKNOWNN = 0,
            UNCOMPRESSED_COLOR_MAPPED = 1,
            UNCOMPRESSED_TRUE_COLOR = 2,
            UNCOMPRESSED_BLACK_AND_WHITE = 3,
            RUN_LENGTH_ENCODED_COLOR_MAPPED = 9,
            RUN_LENGTH_ENCODED_TRUE_COLOR = 10,
            RUN_LENGTH_ENCODED_BLACK_AND_WHITE = 11
        }

        enum AtrribType : byte
        {
            NoAlpha,
            UndefinedDataInAlpha,

            /// <summary>
            /// The specs indicate on should retain these values, but I'm not sure what they mean by that. Should we treat them as
            /// alpha when displaying the image?
            /// </summary>
            UndefinedDataInAlpha2,
            Alpha,

            /// <summary>
            /// This means the color values has been multiplied with the alpha value
            /// 
            /// Say you have a color ot argb: 0.5, 1, 0.5, 0.2
            /// The that value will be: 0.5, 0.5, 0.25, 0.1
            /// 
            /// Divide on the alpha to get the color back to the way it's suppose to be
            /// </summary>
            PreMultipliedAlpha
        }
    }
}
