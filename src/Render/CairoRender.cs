using PdfLib.Compile;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using PdfLib.Render.CairoLib;
using System;
using System.Diagnostics;
using System.IO;

namespace PdfLib.Render
{
    public sealed class CairoRender : PdfRender
    {
        [DebuggerStepThrough]
        public rImage RenderWithCairo(CompiledPage page)
        {
            return RenderWithCairo(page, page.MediaBox.Width, page.MediaBox.Height, true);
        }

        public rImage RenderWithCairo(CompiledPage page, double width, double height, bool rotate_page)
        {
            Running = true;
            rImage result;
            var size = GetPaperSize(page);
            xSize output = CalcDimensions(size, width, height, RespectAspectRatro, 0);
            var start = DateTime.Now;
            using (var ddc = new DrawCairo())
            {
                ddc.Init(size, output.Width, output.Height, RespectAspectRatro, ScaleLinesToOutput, rotate_page ? page.Rotate : 0);
                Execute(page, ddc, output.Width, output.Height, rotate_page ? page.Rotate : 0);

                result = new rImage(ddc.GetRenderedData(), ddc.SurfaceStride, ddc.SurfaceWidth, ddc.SurfaceHeight, Cairo.Enums.Format.ARGB32);
            }
            var end = DateTime.Now;

            Debug.WriteLine("Render Time: " + (end - start).TotalSeconds.ToString());

            return result;
        }
    }
}
