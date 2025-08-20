using PdfLib.Compile;
using PdfLib.Pdf.Primitives;
using PdfLib.Render.Commands;
using System;
using System.Diagnostics;

namespace PdfLib.Render
{
    public class PdfRender : IExecutor
    {
        /// <summary>
        /// If one want to abort a render run, set this flag false.
        /// </summary>
        public bool Running = false;

        /// <summary>
        /// If the page is to be streached or not, depending on input height / width.
        /// </summary>
        public bool RespectAspectRatro = true;

        /// <summary>
        /// Set true to create more visualy striking thumbnails
        /// of vector images. (True is defualt in the specs)
        /// </summary>
        public bool ScaleLinesToOutput = true;

        /// <summary>
        /// Whenever the rendered page should be cropped to the crop box
        /// </summary>
        public bool CropPage = true;

#if DEBUG
        /// <summary>
        /// Used for debugging
        /// </summary>
        protected int _n_steps = -1;
        protected int _c_step = 0;
#endif

        public void RenderWithIDraw(CompiledPage page, IDraw draw, double output_width, double output_height, int rotation)
        {
            Running = true;
            var start = DateTime.Now;
            Execute(page, draw, output_width, output_height, rotation);

            var end = DateTime.Now;

            Debug.WriteLine("Render Time: " + (end - start).TotalSeconds.ToString());
        }

        /// <summary>
        /// Renders an annotation
        /// </summary>
        /// <param name="annotation">Annotation to render</param>
        /// <param name="draw">target</param>
        public void Render(Pdf.Annotation.PdfMarkupAnnotation annotation, IDraw draw)
        {
            if (annotation == null || draw == null) return;
            CompiledAnnotation ca = new PdfCompiler().Compile(annotation);
            if (ca.Apperance != null)
            {
                Running = true;
                Execute(ca.Apperance, draw);
                Running = false;
            }
        }        

        /// <summary>
        /// Get the size of the rendered page
        /// </summary>
        /// <param name="page">Page</param>
        /// <returns>Cropbox or Mediabox</returns>
        public PdfRectangle GetPaperSize(CompiledPage page)
        {
            return (CropPage && page.CropBox != null) ? page.CropBox : page.MediaBox;
        }

        protected void Execute(CompiledPage page, IDraw renderer, double output_width, double output_height, int rotation)
        {
            //Whenever we are printing or not
            bool print = false;
            var annotations = page.Annotations;
            if (annotations != null && annotations.Length > 0)
                renderer.PrepForAnnotations(true);

            renderer.Executor = this;
            Execute(page.Commands, renderer);
            if (annotations == null) return;

            //Draws annotations
            if (annotations != null && annotations.Length > 0)
                renderer.PrepForAnnotations(false);
            var size = GetPaperSize(page);

            //Calculates scale values for NoZoom. Note that NoZoom annotations are not to be made bigger,
            //but can be made smaller. 
            double i_scale_x = output_width > size.Width ? 1 / (output_width / size.Width) : 1;
            double i_scale_y = output_height > size.Height ? 1 /(output_height / size.Height) : 1;


            foreach (var annot in annotations)
            {
                //Annots the compiler doesn't support is set to null
                if (annot == null) continue;

                //We only draw visible annotations
                if (annot.Flags.Hidden)
                    continue;
                if (print)
                {
                    if (!annot.Flags.Print)
                        continue;
                }
                else
                {
                    if (annot.Flags.NoView)
                        continue;
                }

                renderer.Save();


                //NoZoom is implemented by using a scale matrix that undoes the userspace to device space scaling factor. These
                //annotations are anchored by the "UpperLeft" point, not LowerLeft, so some additional movement is done to place
                //the annotation right.
                if (annot.Flags.NoZoom)
                {
                    var r = annot.Clip;
                    //This matrix can be divided into:
                    // 1. Prepend scale matrix (i_scale_x, 0, 0, i_scale_y)
                    // 2. Move figure to 0,0 with translate matrix (-r.X, -r.Y)
                    // 3. Move figure to scaled XY coordinate with translate matrix (r.x * scale_x, r.Y * scaly_y)
                    // 4. Move figure to Upper Left coordinate with tanslate matrix (0, r.Height * scale_y - r.Height)
                    renderer.PrependCM(new xMatrix(i_scale_x, 0, 0, i_scale_y, -r.X * i_scale_x + r.X, -r.Y * i_scale_y + r.Y + r.Height - r.Height * i_scale_y));
                }

                if (annot.Flags.NoRotate)
                {
                    var r = annot.Rect;
                    var counter_rotation = rotation % 360;// (360 - rotation) % 360;
                    if (counter_rotation != 0)
                    {
                        var m = xMatrix.Identity.Rotate(counter_rotation, r.X, r.Y + r.Height);
                        renderer.PrependCM(m);
                    }
                }

                if (annot.ApperanceStream != null)
                {
                    CompiledForms forms = annot.ApperanceStream.Normal;
                    CompiledForm cf = null;
                    if (forms != null && forms.Forms.Length > 0)
                    {
                        if (forms.FormNames != null)
                        {
                            if (annot.ApperanceStream.SelectedApperance != null)
                            {
                                var index = Array.IndexOf<string>(forms.FormNames, annot.ApperanceStream.SelectedApperance);
                                if (index != -1 && index < forms.Forms.Length)
                                    cf = forms.Forms[index];
                            }
                        }
                        else
                            cf = forms.Forms[0];
                    }

                    if (cf != null)
                    {
                        // Step. 1
                        // The appearance’s bounding box (specified by its BBox entry) shall be transformed, using Matrix, 
                        // to produce a quadrilateral with arbitrary orientation.
                        var quadrilateral = cf.Matrix.Transform(new xQuadrilateral(cf.BBox));

                        // Step. 2
                        // The transformed appearance box is the smallest upright rectangle that encompasses this quadrilateral.
                        var tab = quadrilateral.Bounds;
                        if (Util.Real.IsZero(tab.Height) || Util.Real.IsZero(tab.Width))
                            goto divide_on_zero;

                        // Step. 3
                        // A matrix A shall be computed that scales and translates the transformed appearance box to align with 
                        // the edges of the annotation’s rectangle.

                        //Scales arround the center of the figure so that tab becomes the same size as BBox
                        var BBox = annot.Rect;
                        var a = xMatrix.Identity.Scale(BBox.Width / tab.Width, BBox.Height / tab.Height, tab.X + tab.Width / 2, tab.Y + tab.Height / 2);
                        
                        //Figures out the new position
                        tab = a.Transform(tab);

                        //Offsets so that tab will align with BBox
                        a = a.Translate(BBox.Left - tab.Left, BBox.Bottom - tab.Bottom);
                        var m = cf.Matrix.Append(a);

                        //Clips to the annot's bounding box.
                        Compose.cDraw.Clip(renderer, BBox);

                        //Scales and translates
                        renderer.PrependCM(m);

                        //Draws
                        Execute(cf.Commands, renderer);
                    }
                }
                else if (annot.Apperance != null)
                    Execute(annot.Apperance, renderer);
               
            divide_on_zero: 
                renderer.Restore();
            }
        }

        void IExecutor.Execute(IExecutable cmds, IDraw renderer)
        {
            Execute(((IExecutableImpl)cmds).Commands, renderer);
        }

        void IExecutor.Execute(object cmds, IDraw renderer)
        {
            Execute((RenderCMD[])cmds, renderer);
        }

        protected void Execute(RenderCMD[] cmds, IDraw renderer)
        {
            int c = 0;

            restart:
                try
                {
                    for (; c < cmds.Length && Running; c++)
                    {
#if DEBUG
                        //var cmd = cmds[c];
                        /*if (c == 70)
                            break;
                        //    Console.Write("hi");
                        Console.WriteLine("Command nr: " + c);//*/

                        //Only used for debugging, implements
                        //"incremental" rendering
                        if (_n_steps != -1)
                        {
                            if (_c_step++ >= _n_steps)
                                break;
                        }
#endif
                        cmds[c].Execute(renderer);
                    }

                }
                catch (Exception e)
                {
                    //Todo: Repport error
                    //Debug.Assert(false);
                    //System.IO.File.WriteAllText("test.txt", "PdfRender.Execute: " + e.Message);
                    //System.IO.File.AppendAllText("test.txt", "\r\n" + e.StackTrace);
                    Console.WriteLine("PdfRender.Execute: " + e.Message);
                    Console.WriteLine(e.StackTrace);
                    Console.WriteLine("=========================");
                    c++;
                    goto restart;
                }
        }

        /// <summary>
        /// For calculating the size of the render when respecting aspect ratio
        /// </summary>
        public static xSize CalcDimensions(PdfRectangle MediaBox, double output_width, double output_height,
            bool respect_aspect, int rotate)
        {
            if (respect_aspect)
            {
                double asb_us_width = Math.Abs(MediaBox.Width);
                double abs_us_height = Math.Abs(MediaBox.Height);

                double us_aspect_ratio = abs_us_height / asb_us_width;
                double ds_aspect_ratio = output_height / output_width;
                if (us_aspect_ratio > ds_aspect_ratio)
                    output_width = output_height / us_aspect_ratio;
                else
                    output_height = output_width * us_aspect_ratio;
            }
            if (rotate == 0 || rotate == 180)
                return new xSize(output_width, output_height);
            else
                return new xSize(output_height, output_width);

        }
    }
}
