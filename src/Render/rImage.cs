using PdfLib.Pdf;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Function;
using System;

namespace PdfLib.Render
{
    public sealed class rImage
    {
        #region image container

        public byte[] RawData;
        public int Stride;
        public int Width;
        public int Height;
        public rFormat Format;

        public rImage(byte[] data, int stride, int width, int height, CairoLib.Cairo.Enums.Format format)
        { RawData = data; Stride = stride; Width = width; Height = height; Format = (rFormat) format; }

        public rImage(byte[] data, int stride, int width, int height, rFormat format)
        { RawData = data; Stride = stride; Width = width; Height = height; Format = format; }

        #endregion

        /// <summary>
        /// When an image is required to be decoded to A8 format
        /// </summary>
        /// <remarks>
        /// There's no way to know when a A8 image is needed, so
        /// this has to be a explicit call from the calle.
        /// </remarks>
        public static rImage MakeA8Image(PdfImage img)
        {
            var bs = img.DecodeImage(new PdfImage.GRAY8().Convert);
            if (bs == null) return null;
            int pixel_width = img.Width, pixel_height = img.Height;
            return new rImage(bs, (int)(Math.Ceiling(pixel_width / 4d) * 4), pixel_width, pixel_height, rFormat.A8);
        }

        public static rImage MakeAlphaImage(PdfImage img)
        {
            return img.ImageMask ? MakeImage(img) : MakeA8Image(img);
        }

        /// <summary>
        /// Makes a PdfImage from a rImage
        /// </summary>
        /// <returns>rImage or null</returns>
        public static PdfImage MakeImage(rImage img)
        {
            switch(img.Format)
            {
                case rFormat.BGRA32:
                    return PdfImage.CreateFromBGRA32(img.RawData, img.Height, img.Width, img.Stride, true);

                case rFormat.A1:
                    return new PdfImage(img.RawData, img.Width, img.Height);

                case rFormat.A8:
                    return new PdfImage(img.RawData, DeviceGray.Instance, img.Width, img.Height, 8);

                case rFormat.RGB24:
                    return new PdfImage(img.RawData, DeviceRGB.Instance, img.Width, img.Height, 8);

                case rFormat.RGB30:
                    return new PdfImage(img.RawData, DeviceRGB.Instance, img.Width, img.Height, 10);

                case rFormat.PREMUL_RGBA8888:
                    return PdfImage.CreateFromPremulRGBA32(img.RawData, img.Height, img.Width, img.Stride, true);

                //565 images are not supported. Will have to do a 565 to 555 or 666 conversion.
                // Or possibly convert the image to Jpeg 2000. 
                default: throw new NotImplementedException();
            }      
        }

        /// <summary>
        /// Decodes and places a image into a rImage struct
        /// </summary>
        /// <param name="img">Image to decode</param>
        /// <param name="include_mask">If to include the smask, if there is one</param>
        /// <returns>rImage or null</returns>
        public static rImage MakeImage(PdfImage img, bool include_mask = false)
        {
            int pixel_width = img.Width, pixel_height = img.Height;
            if (img.ImageMask)
            {
                //The colors of this image are alpha values.

                //Fetches the raw data. There's no colorspace or such
                //complications so it's just as well to work with the
                //raw data.
                var bs = new Util.BitStream(img.Stream.DecodedStream);

                //However there can be a decode array. 
                var decode = img.Decode[0];
                byte max = (byte)(decode.Max * 255), min = (byte)(decode.Min*255);

                //We but each bit in a byte
                int stride = (int)(Math.Ceiling(pixel_width / 4d) * 4);
                var pixels = new byte[stride * pixel_height];

                //There may be some junk data after every row
                int source_stride = pixel_width - 1; //(pixel_width + 7) / 8;

                //Reads out one bit at the time and converts it to a byte
                for (int y = 0; y < pixel_height; y++)
                {
                    int row = y * stride;
                    for (int align = 0, c = 0; c < pixel_width && bs.HasBits(1); c++)
                    {
                        var bit = bs.GetBits(1);
                        pixels[row + c] = (bit != 1) ? max : min;
                        if (align == source_stride)
                        {
                            bs.ByteAlign();
                            align = 0;
                        }
                        else
                            align++;
                    }
                }

                //Format.A1 is depended on the endianess of the arcithecture. So the data would have to
                //be reversed on little endian machines.
                return new rImage(pixels, stride, pixel_width, pixel_height, CairoLib.Cairo.Enums.Format.A8);
            }
            else
            {
                byte[] bs = null;
                if (include_mask)
                {
                    var mask = img.SMask;
                    if (mask == null && img.Mask is PdfImage m)
                        mask = m;
                    if (mask != null)
                    {
                        mask = PdfImage.ChangeSize(mask, img.Width, img.Height);
                        var alpha = mask.DecodeImage(new PdfImage.GRAY8().Convert);
                        bs = img.DecodeImage((px, has_alpha, width, height, cc, fcd) =>
                        {
                            int size = width * height;
                            byte[] bytes = new byte[size * 4];

                            for (int c = 0, j = 0; c < size; c++)
                            {
                                fcd();

                                var col = cc.MakeColor(px);

                                //Converts the color to bgra32. This step
                                //reduces precision to 8BPP
                                bytes[j++] = PdfImage.Clip((int)(col.B * byte.MaxValue));
                                bytes[j++] = PdfImage.Clip((int)(col.G * byte.MaxValue));
                                bytes[j++] = PdfImage.Clip((int)(col.R * byte.MaxValue));
                                bytes[j++] = alpha[c];
                            }

                            return bytes;
                        });
                    }
                }

                if (bs == null)
                {
                    bs = img.DecodeImage();
                    if (bs == null) return null;
                }
                return new rImage(bs, pixel_width * 4, pixel_width, pixel_height, CairoLib.Cairo.Enums.Format.ARGB32);
            }
        }



        internal static void TransferRGB24(byte[] bytes, FCalcualte[] transfer)
        {
            double[] res = new double[1];
            if (transfer.Length == 1)
            {
                //Use the same function on all channels
                var f = transfer[0];
                for (int c=0; c < bytes.Length; c++) 
                {
                    f.Calculate(bytes[c] / 255d, res);
                    bytes[c] = (byte) (res[0] * 255);
                }
            }
            else
            {
                //Use the three first functions
                var r = transfer[0];
                var b = transfer[1];
                var g = transfer[2];

                for (int c = 0; c < bytes.Length; )
                {
                    r.Calculate(bytes[c] / 255d, res);
                    bytes[c++] = (byte)(res[0] * 255);
                    g.Calculate(bytes[c] / 255d, res);
                    bytes[c++] = (byte)(res[0] * 255);
                    b.Calculate(bytes[c] / 255d, res);
                    bytes[c++] = (byte)(res[0] * 255);

                    //if (c == 369627)
                    //    Console.Write("");
                }
            }
        }

        internal static void TransferBGRA32(byte[] bytes, FCalcualte[] transfer)
        {
            double[] res = new double[1];
            if (transfer.Length == 1)
            {
                var ta = new FCalcualte[3];
                for (int c = 0; c < ta.Length; c++)
                    ta[c] = transfer[0];
                transfer = ta;
            }

            //Use the three first functions
            var r = transfer[0];
            var b = transfer[1];
            var g = transfer[2];

            for (int c = 0; c < bytes.Length; c++)
            {
                b.Calculate(bytes[c] / 255d, res);
                bytes[c++] = (byte)(res[0] * 255);
                g.Calculate(bytes[c] / 255d, res);
                bytes[c++] = (byte)(res[0] * 255);
                r.Calculate(bytes[c] / 255d, res);
                bytes[c++] = (byte)(res[0] * 255);
            }
        }

    }

    /// <summary>
    /// How the pixels are layed out
    /// </summary>
    /// <remarks>
    /// 1 to 1 with Cairo.Enums.Format
    /// </remarks>
    public enum rFormat
    {
        INVALID = -1,
        BGRA32 = 0,
        RGB24 = 1,
        A8 = 2,
        A1 = 3,
        RGB16_565 = 4,
        RGB30 = 5,

        //Additional formats
        PREMUL_RGBA8888
    }
}
