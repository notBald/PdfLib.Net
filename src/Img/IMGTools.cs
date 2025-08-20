using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Text;
using PdfLib.Util;
using PdfLib.Img.Internal;

namespace PdfLib.Img
{
    /// <summary>
    /// Should perhaps be put in the render namespace to make it clear
    /// that this class depends on Windows.Media
    /// </summary>
    public static class IMGTools
    {
        /// <summary>
        /// Rescales an image useing the input scaling mode.
        /// </summary>
        public static BitmapSource ChangeImageSize(BitmapSource bms, BitmapScalingMode mode, int new_width, int new_height)
        {
            RenderTargetBitmap rtb = new RenderTargetBitmap(new_width, new_height, 96, 96, PixelFormats.Default);

            var vis = new Render.MyDrawingVisual() { BitmapScalingMode = mode };
            using (var dc = vis.RenderOpen())
                dc.DrawImage(bms, new Rect(0, 0, new_width, new_height));

            rtb.Render(vis);
            return rtb;
        }

        public static BitmapSource ChangePixelFormat(BitmapSource bms, PixelFormat pf)
        {
            if (bms.Format != pf)
            {
                FormatConvertedBitmap newFormatedBitmapSource = new FormatConvertedBitmap();
                newFormatedBitmapSource.BeginInit();
                newFormatedBitmapSource.Source = bms;
                newFormatedBitmapSource.DestinationFormat = pf;
                newFormatedBitmapSource.EndInit();
                bms = newFormatedBitmapSource;
            }

            return bms;
        }

        /// <summary>
        /// Converts a bitmapsource to a byte array.
        /// </summary>
        public static byte[] BMStoBA(BitmapSource bs)
        {
            int stride = (bs.Format.BitsPerPixel * bs.PixelWidth + 7) / 8;
            byte[] bytes = new byte[stride * bs.PixelHeight];
            bs.CopyPixels(bytes, stride, 0);
            return bytes;
        }

        /// <summary>
        /// Converts a bitmapsource to a BMP image.
        /// </summary>
        public static MemoryStream BMStoBMP(BitmapSource bs)
        {
            var bbe = new BmpBitmapEncoder();
            MemoryStream memoryStream = new MemoryStream();
            bbe.Frames.Add(BitmapFrame.Create(bs));
            bbe.Save(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        /// <summary>
        /// Expands a 1BPP image to an 8BPP image sutable for display
        /// </summary>
        /// <param name="data">Array of bytes to expand, aligned to w, set w = 0 if this is unwanted</param>
        /// <param name="h">Height (or size when w=0)</param>
        /// <param name="bpp">source bit per pixels</param>
        /// <returns>A byte array up to 8 times the size of the intput (or smaller if bpp is > 8</returns>
        /// <remarks>
        /// Byte aligns to w. Set w=0 if this is unwanted.
        /// 
        /// This method is by no means optimal, the interpolation math can for instance be simplified to:
        ///  Math.Round(pixel x 255d / (1 << bpp - 1))
        ///  
        /// Current impl dosn't do the rounding, which should probably be there. 
        /// </remarks>
        public static byte[] ExpandBPPto8(byte[] data, int w, int h, int bpp)
        {
            int nComponents = w == 0 ? h : w * h;

            var bits = new BitStream(data);
            byte[] img = new byte[nComponents];

            var li = new LinearInterpolator(0, Math.Pow(2, bpp) - 1, 0, 255);

            for (int c = 0, p = 0; bits.HasBits(bpp) && c < nComponents; c++)
            {
                img[c] = (byte)li.Interpolate(bits.GetBits(bpp));

                if (++p == w)
                {
                    p = 0;
                    bits.ByteAlign();
                }
            }

            return img;
        }

        /// <summary>
        /// Adds an alpha channel to a BGR24 or RGB24 image, putting the channel last.
        /// </summary>
        /// <param name="data">Image data</param>
        /// <param name="stride">Stride of the data</param>
        /// <remarks>4 bytes per pixel data, with a stride equal to the width</remarks>
        public static byte[] AddAlphaChaneltoRGB24(byte[] data, int width, int height, int stride)
        {
            byte[] with_alpha = new byte[width * height * 4];

            for(int h=0, write = 0; h < height; h++)
            {
                var pos = h * stride;
                for(int w=0; w < width; w += 3)
                {
                    with_alpha[write++] = data[pos++];
                    with_alpha[write++] = data[pos++];
                    with_alpha[write++] = data[pos++];
                    with_alpha[write++] = 255;
                }
            }

            return with_alpha;
        }

        /// <summary>
        /// Assmbles sepperate components back into bytes
        /// </summary>
        /// <param name="pixels">Components to assemble</param>
        /// <param name="bpc">Bits per component</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <returns>Raw byte data (big endian)</returns>
        public static byte[] PlanarToChunky(int[][] pixels, int bpc, int width, int height)
        {
            int stride = (bpc * pixels.Length * width + 7) / 8;
            byte[] dest = new byte[stride * height];
            var bw = new BitWriter(dest);
            for (int x = 0, pos = 0; x < height; x++)
            {
                for (int y = 0; y < width; y++, pos++)
                {
                    for (int c = 0; c < pixels.Length; c++)
                    {
                        bw.Write(pixels[c][pos], bpc);
                    }
                }
                bw.Align();
            }
            bw.Dispose();
            return dest;
        }

        /// <summary>
        /// Checks if the Pdf Page is just a single image
        /// </summary>
        /// <param name="page">Page to check</param>
        /// <returns>If this page is a single image</returns>
        public static bool IsImage(PdfPage page)
        {
            if (page.Resources.XObject.Count > 1)
            {
                Compile.PdfContentAnalyze.Analyze(page);

                throw new NotImplementedException();
            }

            return false;
        }
    }
}
