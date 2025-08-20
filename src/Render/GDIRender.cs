using PdfLib.Compile;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace PdfLib.Render
{
    public sealed class GDIRender : PdfRender
    {
        public BitmapImage RenderWithGDI(CompiledPage page, double width, double height, bool rotate_page)
        {
            Running = true;
            var size = GetPaperSize(page);
            xSize output = CalcDimensions(size, width, height, RespectAspectRatro, 0);
            var start = DateTime.Now;

            using (var myBitmap = new System.Drawing.Bitmap((int)width, (int)height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(myBitmap))
                {
                    using (var ddc = new GDI.DrawGDI(g))
                    {
                        ddc.Init(size, output.Width, output.Height, RespectAspectRatro, ScaleLinesToOutput, rotate_page ? page.Rotate : 0);
                        Execute(page, ddc, output.Width, output.Height, rotate_page ? page.Rotate : 0);
                    }
                }
                var end = DateTime.Now;
                Debug.WriteLine("Render Time: " + (end - start).TotalSeconds.ToString());

                return ToBitmapImage(myBitmap);
            }
        }

        public PdfImage RenderToImage(PdfPage page)
        {
            throw new NotImplementedException();
        }

        public MemoryStream RenderToBMP(CompiledPage page)
        {
            throw new NotImplementedException();
        }

        public MemoryStream RenderToBMP(CompiledPage page, int width, int height)
        {
            throw new NotImplementedException();
        }

        private static BitmapImage ToBitmapImage(System.Drawing.Bitmap image)
        {
            MemoryStream ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            ms.Seek(0, SeekOrigin.Begin);
            BitmapImage bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.EndInit();
            return bi;
        }
    }
}
