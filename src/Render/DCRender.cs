using PdfLib.Compile;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PdfLib.Render
{
    public sealed class DCRender : PdfRender
    {
        public PdfImage RenderToImage(PdfPage page)
        {
            var bms = RenderToIS(new PdfCompiler().Compile(page, false, false), page.Width, page.Height, true);
            return PdfImage.Create(Img.IMGTools.BMStoBMP(bms));
        }

        public MemoryStream RenderToBMP(CompiledPage page)
        {
            return Img.IMGTools.BMStoBMP(RenderToIS(page, page.MediaBox.Width, page.MediaBox.Height, true));
        }

        public MemoryStream RenderToBMP(CompiledPage page, int width, int height)
        {
            return Img.IMGTools.BMStoBMP(RenderToIS(page, width, height, true));
        }

        [DebuggerStepThrough]
        public DrawingVisual RenderToDV(CompiledPage page)
        {
            return RenderToDV(page, page.MediaBox.Width, page.MediaBox.Height, true);
        }
        [DebuggerStepThrough]
        public DrawingVisual RenderToDV(PdfPage page)
        {
            return RenderToDV(new PdfCompiler().Compile(page, false, false));
        }

        /// <summary>
        /// Renders a page to a drawing visual
        /// </summary>
        /// <param name="page">The page to render</param>
        /// <param name="width">Width of the page</param>
        /// <param name="height">Height of the page</param>
        /// <param name="respect_aspect">Adjust height to aspect ratio</param>
        /// <param name="rotate_page">If the page be rotated as set by page.rotate when rendering</param>
        /// <returns></returns>
        public DrawingVisual RenderToDV(CompiledPage page, double width, double height, bool rotate_page)
        {
            return RenderToDV(page, width, height, rotate_page, null);
        }

        /// <summary>
        /// Renders a page to a drawing visual
        /// </summary>
        /// <param name="page">The page to render</param>
        /// <param name="width">Width of the page</param>
        /// <param name="height">Height of the page</param>
        /// <param name="respect_aspect">Adjust height to aspect ratio</param>
        /// <param name="rotate_page">If the page be rotated as set by page.rotate when rendering</param>
        /// <returns></returns>
        public DrawingVisual RenderToDV(CompiledPage page, double width, double height, bool rotate_page, PdfRectangle box)
        {
            var dv = new MyDrawingVisual();
            //This is not the "correct" scaling algorithem. Works well enough on large images,
            //while small images* can look "blury".
            // * That will say, large images that are scaled down to small sizes and then zoomed
            //   up. Looks well enough on 1to1 zoom level.
            dv.BitmapScalingMode = BitmapScalingMode.Fant;
            //dv.EdgeMode = EdgeMode.Aliased;
            Running = true;
            var ddc = new WPF.DrawDC();
            var size = box == null ? GetPaperSize(page) : box;
            xSize output = CalcDimensions(size, width, height, RespectAspectRatro, 0);
            var start = DateTime.Now;
            using (var dc = dv.RenderOpen())
            {
                ddc.Init(size, output.Width, output.Height, RespectAspectRatro, ScaleLinesToOutput, dc, rotate_page ? page.Rotate : 0);
                Execute(page, ddc, output.Width, output.Height, rotate_page ? page.Rotate : 0);
            }
            ddc.Dispose();
            var end = DateTime.Now;

            Debug.WriteLine("Render Time: " + (end - start).TotalSeconds.ToString());

            //Pixel shader test.
            /*var de = new Shader.DesaturateEffect();
            //var de = new Shader.GrayScaleEffect();
            dv.PixelShader = de;
            dv.PixelShader = new Shader.GrayScaleEffect();
            de.Saturation = 10.0;//*/
            return dv;
        }

        /// <summary>
        /// Renders a page to a drawing context
        /// </summary>
        /// <param name="page">The page to render</param>
        /// <param name="width">Width of the page</param>
        /// <param name="height">Height of the page</param>
        /// <param name="respect_aspect">Adjust height to aspect ratio</param>
        /// <param name="rotate_page">If the page be rotated as set by page.rotate when rendering</param>
        /// <returns></returns>
        public void RenderToDC(DrawingContext dc, CompiledPage page, double width, double height, bool rotate_page, PdfRectangle box)
        {
            var size = box == null ? GetPaperSize(page) : box;
            xSize output = CalcDimensions(size, width, height, RespectAspectRatro, 0);
            var start = DateTime.Now;

            Running = true;
            using (var ddc = new WPF.DrawDC())
            {
                ddc.Init(size, output.Width, output.Height, RespectAspectRatro, ScaleLinesToOutput, dc, rotate_page ? page.Rotate : 0);
                Execute(page, ddc, output.Width, output.Height, rotate_page ? page.Rotate : 0);
            }
            var end = DateTime.Now;

            Debug.WriteLine("Render Time: " + (end - start).TotalSeconds.ToString());
        }



        /// <summary>
        /// Renders a page into an image.
        /// </summary>
        /// <param name="page">Page to render</param>
        /// <param name="width">width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="respect_aspect">Adjust height to aspect ratio</param>
        /// <param name="rotate_page">If the page be rotated as set by page.rotate when rendering</param>
        /// <returns>Page as an image, or null if canceled</returns>
        public BitmapSource RenderToIS(CompiledPage page, double width, double height, bool rotate_page)
        {
            return RenderToIS(page, width, height, rotate_page, null);
        }

        /// <summary>
        /// Renders a page into an image.
        /// </summary>
        /// <param name="page">Page to render</param>
        /// <param name="width">width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="respect_aspect">Adjust height to aspect ratio</param>
        /// <param name="rotate_page">If the page be rotated as set by page.rotate when rendering</param>
        /// <param name="box">Box to render the page with</param>
        /// <returns>Page as an image, or null if canceled</returns>
        public BitmapSource RenderToIS(CompiledPage page, double width, double height, bool rotate_page, PdfRectangle box)
        {
            if (width < 1) width = 1;
            if (height < 1) height = 1;
            var vis = RenderToDV(page, width, height, rotate_page, box);
            //var vis = DebugRender(page, width, height, respect_aspect);
            if (!Running) return null;

            if (box == null)
                box = (CropPage && page.CropBox != null) ? page.CropBox : page.MediaBox;
            xSize size = CalcDimensions(box, width, height, RespectAspectRatro, rotate_page ? page.Rotate : 0);
            RenderTargetBitmap rtb = null;
            try
            {
                rtb = new RenderTargetBitmap((int)size.Width,
                                 (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                var sw = new Stopwatch();
                sw.Start();
                rtb.Render(vis);
                sw.Stop();
                Debug.WriteLine("Visual render Time: " + sw.Elapsed.TotalSeconds.ToString());
            }
            catch (OutOfMemoryException)
            {
                //Workaround for bug in RenderTargetBitmap
                ((MyDrawingVisual)vis).BitmapScalingMode = BitmapScalingMode.Unspecified;
                try { rtb.Render(vis); }
                catch (OutOfMemoryException)
                {
                    var epage = new PdfPage();
                    using (var cdraw = new Compose.cDraw(epage))
                    {
                        cdraw.DrawString(50, 50, "Out of memory");
                    }
                    vis = RenderToDV(epage);
                    rtb.Render(vis);
                }
            }
            rtb.Freeze();

            return rtb;
        }
        [DebuggerStepThrough]
        public BitmapSource RenderToIS(CompiledPage page)
        {
            return RenderToIS(page, page.MediaBox.Width, page.MediaBox.Height, true);
        }

#if DEBUG
        public DrawingVisual DebugRender(CompiledPage page, double width, double height, bool rotate_page)
        {
            var dv = new MyDrawingVisual();
            Running = true;
            var dbeug = new WPF.DrawDebug();
            ((IDraw)dbeug).Executor = this;
            using (var dc = dv.RenderOpen())
            {
                dbeug.Init(page.MediaBox, width, height, RespectAspectRatro, true, dc, rotate_page ? page.Rotate : 0);
                Execute(page.Commands, dbeug);
            }
            dbeug.Dispose();
            return dv;
        }

        public BitmapSource DebugRenderToIS(CompiledPage page, double width, double height, bool rotate_page)
        {
            var vis = DebugRender(page, width, height, rotate_page);
            //var vis = DebugRender(page, width, height, respect_aspect);
            if (!Running) return null;


            var box = (CropPage && page.CropBox != null) ? page.CropBox : page.MediaBox;
            xSize size = CalcDimensions(box, width, height, RespectAspectRatro, rotate_page ? page.Rotate : 0);
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)size.Width,
                (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(vis);
            rtb.Freeze();

            return rtb;
        }

        [DebuggerStepThrough]
        public DrawingVisual RenderToDV(CompiledPage page, int n_steps)
        {
            _c_step = 0;
            _n_steps = n_steps;

            return RenderToDV(page, page.MediaBox.Width, page.MediaBox.Height, true);
        }
#endif
    }


    //QuickFix:
    //http://stackoverflow.com/questions/2967936/image-resize-aliasing-in-wpf-v4-but-not-under-v3-5
    internal sealed class MyDrawingVisual : DrawingVisual
    {
        public BitmapScalingMode BitmapScalingMode
        {
            get { return this.VisualBitmapScalingMode; }
            set { this.VisualBitmapScalingMode = value; }
        }

        public Effect PixelShader
        {
            get { return base.VisualEffect; }
            set { base.VisualEffect = value; }
        }

        public EdgeMode EdgeMode
        {
            get { return base.VisualEdgeMode; }
            set { VisualEdgeMode = value; }
        }
    }
}
