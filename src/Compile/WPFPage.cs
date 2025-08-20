using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Font;
using PdfLib.Render;
using PdfLib.Render.WPF;

namespace PdfLib.Compile
{
    public class WPFPage
    {
        #region Variables and properties

        /// <summary>
        /// A flattened media box. Can be used without
        /// checking for references.
        /// </summary>
        internal readonly PdfRectangle MediaBox;
        //internal readonly PdfRectangle TrimBox;
        internal readonly PdfRectangle CropBox;

        internal readonly WPFCommand[] Commands;
        internal readonly int Rotate;

        #endregion

        #region Init

        internal WPFPage(PdfPage page, WPFCommand[] cmds)
        {
            MediaBox = page.MediaBox.Flatten();
            //TrimBox = page.TrimBox.Flatten();
            CropBox = page.CropBox.Flatten();
            Commands = cmds;
            Rotate = page.Rotate;
        }

        #endregion

        public RenderTargetBitmap Render(double width, double height)
        {
            var draw = new DrawWPF(MediaBox, CropBox, width, height, true, Rotate);

            //Todo: Try/Catch/Stop, when how to execute has been decided
            var start = DateTime.Now;
            for (int c = 0; c < Commands.Length; c++)
                Commands[c].Execute(draw);
            var endExecute = DateTime.Now;

            var startDraw = DateTime.Now;
            var drawing = draw.Finish();
            var endDraw = DateTime.Now;

            var startRast = DateTime.Now;
            xSize size = PdfRender.CalcDimensions(MediaBox, width, height, true, Rotate);
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)size.Width,
                (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(drawing);
            rtb.Freeze();
            var end = DateTime.Now;

            Debug.WriteLine("Execution Time: " + (endExecute - start).TotalSeconds.ToString());
            Debug.WriteLine("Drawing Time: " + (endDraw - startDraw).TotalSeconds.ToString());
            Debug.WriteLine("Raster Time: " + (end - startRast).TotalSeconds.ToString());
            Debug.WriteLine("Render Time: " + (end - start).TotalSeconds.ToString());
            Debug.WriteLine("======================");

            return rtb;
        }
    }
}
