#region Imports and defines
//Defines this class
#define USE_GDI

//AutoAdjustLikeAdobe with UseGuidelineSet is fairly close to how Adobe renders lines,
//but there are still some difference to when this auto adjust kicks in.
#define AutoAdjustLikeAdobe
// ^Cairo does not yet autoadjust, this because the "create new pen" logic has been dropped.
//  However don't remove the define, as that breaks stuff

//There's no functional difference, it's just sligtly nicer to debug with strings
//over arrays of bytes.
//#define UseString //<-- does not work anymore :(

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf;
using PdfLib.Util;
using PdfLib.Compile;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Function;
using PdfLib.Render.CairoLib;
using PdfLib.Compose.Text;
using PdfLib.Compose;
using System.Drawing;
using System.Drawing.Drawing2D;

#endregion

namespace PdfLib.Render.GDI
{
    /// <summary>
    /// Renders a PDF document using GDI+
    /// </summary>
    /// <remarks>
    /// bugs not present in DrawCairo: 
    ///  AxialShaderTests - test_2a.pdf, test_2c.pdf
    ///  RadialShaderTests - monitor.pdf
    ///  
    /// How to do clipping:
    /// 
    /// Alt. 1
    ///  - When clipping is needed, create a "clip path", where all draw calls are added.
    ///     - These methods make draw calls:
    ///       - DrawImage.
    ///       - DrawPath
    ///       - DrawString
    ///  - Before adding a path, transform the path down to the same level of the clip path
    ///    - Naturally this means there needs to be a "CTM" for all transforms up to that path.
    ///    - Region seems to be ideal. It got methods for "Intersecting" and "union".
    ///       I'm thinking:
    ///        Have one region (A) to collect clipped geometry into
    ///        Another region (B) for collecting clip paths
    ///        When adding a path to A, first make it into a region, then intersect.
    ///         I.e. r_path = new region(path)
    ///              r_path.Intersect(B)
    ///              A.Union(r_path)
    ///    - Unsolved problem: Images with masks. Say: ImageMaskWithPattern.pdf
    ///    
    /// Alt. 2
    ///  - Image soft masks must be implemented, the hard way. So...
    ///  - 1. get bounds for the device space
    ///    2. Use the bounds to create a bitmap
    ///    3. Render the soft mask
    ///    4. All susequent draw calls must be towards a new bitmap, the size of the soft mask
    ///    5. When the imagemask is popped off the stack, complete the drawing. 
    ///       Somehow use the image mask as a soft mask (There's multiple approaches) and paint
    ///       the softmasked image onto the image*.
    ///       
    /// * For plain GDI: http://msdn.microsoft.com/en-us/library/dd183351.aspx
    ///                  http://parnassus.co/transparent-graphics-with-pure-gdi-part-1/
    ///   For GDI+:      DrawImage should do the job
    /// </remarks>
    internal class DrawGDI : IDraw
    {
        #region Variables and properties

        /// <summary>
        /// Cairo renderer
        /// </summary>
        Graphics _cr;
        GraphicsPath _path;

        PointF _current_point; //<-- Todo: Detect invalid current points (i.e. if not set before use)

        /// <summary>
        /// Current state
        /// </summary>
        private State _cs = new State();
        private RenderState _rs = new RenderState();
        Stack<State> _state_stack = new Stack<State>();

        /// <summary>
        /// TextMetrix transform
        /// </summary>
        private TextMetrix _tm = new TextMetrix();

        /// <summary>
        /// Matrix used to transform CTM to the
        /// user space
        /// </summary>
        private Matrix _from_device_space;

        /// <summary>
        /// Matrix used to transform from user space
        /// to device space
        /// </summary>
        /// <remarks>
        /// With the way Cairo renderes patterns (Cairo
        /// patterns that is, not PDF patters) this
        /// matrix must be set straight on the pattern
        /// 
        /// This because the CTM does not affect the
        /// Cairo pattern itself.
        /// 
        /// When PDF patterns are rendered the Cairo
        /// pattern CTM is set to this matrix.
        /// </remarks>
        private Matrix _to_device_space;

        /// <summary>
        /// For measuring the render size of drawn text.
        /// Given in device coordinates.
        /// </summary>
        private xRect _glyph_bounds;

        /// <summary>
        /// Knowing when the reset _glyph_bounds can be tricky,
        /// so we only do it when this counter reaches zero.
        /// </summary>
        private int _reset_text_bounds;

        /// <summary>
        /// The bounds of rendered text. Check HasTextBounds before
        /// using these bounds.
        /// </summary>
        public xRect TextBounds { get { return _glyph_bounds; } }

        /// <summary>
        /// Does not track graphic state.
        /// </summary>
        /// <remarks>Isn't this done by the compiler anyway?</remarks>
        public GS GraphicState { get { return GS.Unknown; } }

        #endregion

        #region Execution

        /// <summary>
        /// Rendering precision
        /// </summary>
        public int Precision { get { return 16; } }

        /// <summary>
        /// For executing commands
        /// </summary>
        IExecutor IDraw.Executor { get; set; }

        /// <summary>
        /// Draws the raw commands.
        /// </summary>
        public void Draw(IExecutable cmds)
        {
            ((IDraw)this).Executor.Execute(cmds, this);
        }

        public void Execute(object cmds)
        {
            ((IDraw)this).Executor.Execute(cmds, this);
        }

        #endregion

        #region Init and dispose

        public DrawGDI(Graphics g) { _cr = g; _cr.SmoothingMode = SmoothingMode.AntiAlias; }

        public void Init(PdfRectangle MediaBox, double output_width, double output_height,
                         bool respect_aspect, bool scale_lines_to_output, int rotate)
        {
#if !AutoAdjustLikeAdobe
            //Forced true to avoid potential issues. 
            //I think this function still works, but it needs testing. Run through the test
            //files for Forms and Patterns to see if they still render correctly.
            scale_lines_to_output = true;
#endif
            // Calcs the size of the user space. Negative size is intended (though poorly tested).
            float user_space_width = (float) (MediaBox.URx - MediaBox.LLx);
            float user_space_heigth = (float) (MediaBox.LLy - MediaBox.URy);
            float abs_us_width = Math.Abs(user_space_width);
            float abs_us_height = Math.Abs(user_space_heigth);
            var r = new Region();

            // Corrects for aspect ratio.
            xSize output = PdfRender.CalcDimensions(MediaBox, output_width, output_height, respect_aspect, 0);

            //Sets up mapping from the defualt PDF user space to Cairo device space.
            //
            //   PDF: 0.1---1.1    WPF: 0.0---1.0   Scale matrix: Sx--0   WPF matrix: M11--M12
            //         |     |           |     |                  |   |                |    |
            //        0.0---1.0         0.1---1.1                 0--Sy               M21--M22
            float device_width = (float) output.Width;
            float device_height = (float) output.Height;
            float scale_x = device_width / user_space_width;
            float scale_y = device_height / user_space_heigth;

            //Note: If nothing is rendered it's likely because the offset_x/y is set wrong
            //      here.
            Matrix from_userspace_to_device_coords = new Matrix(scale_x, 0, 0,
                                                                scale_y,
                                                                (scale_x < 0) ? (float) output.Width : 0,
                                                                (scale_y < 0) ? (float) output.Height : 0);

            //Translates so that the media box starts at 0,0
            from_userspace_to_device_coords.Translate((float) -MediaBox.LLx, (float) -MediaBox.LLy);

            //Resets all state information
            _cs.Reset();
            _rs.SetUp();

            //It's important that the "rotation" matrix don't end up on the CTM, as
            //that will affect "automatic stroke width adjustments"
#if !AutoAdjustLikeAdobe
            if (scale_lines_to_output)
#endif
            {
                //_cs.CTM = from_userspace_to_device_coords;
            }

            //Rotates the page
            if (rotate != 0)
            {
                from_userspace_to_device_coords.Rotate(rotate);
                //Assumes angular rotation.
                if (rotate == 90)
                    from_userspace_to_device_coords.Translate((float) output.Height, 0);
                else if (rotate == 180)
                    from_userspace_to_device_coords.Translate((float) output.Width, (float) output.Height);
                else if (rotate == 270)
                    from_userspace_to_device_coords.Translate(0, (float) output.Width);
            }//*/

#if !AutoAdjustLikeAdobe
            //if (scale_lines_to_output)
#endif
            {
                _to_device_space = (Matrix) from_userspace_to_device_coords.Clone();
                from_userspace_to_device_coords.Invert();
                _from_device_space = (Matrix) from_userspace_to_device_coords.Clone();
            }

            _cr.Transform = _to_device_space;

            _path = new GraphicsPath();

            //Fills the whole page white. (Note, _cr is not used to store color state, so no need to change back to black.)
            FillRectangle(Brushes.White, new xRect(MediaBox));
        }

        /// <summary>
        /// Resets the path objects
        /// </summary>
        internal void ClearPath()
        {
            _path.Reset();
        }

        /// <summary>
        /// Resets state information back to default.
        /// </summary>
        /// <remarks>
        /// Does not reset all state, just what's needed
        /// when drawing forms/tiles/T3 glyphs</remarks>
        private void ResetState()
        {
            _cs.line_width = 1;
            _cs.line_cap = LineCap.Flat;
            _cs.line_join = LineJoin.Miter;
            _cs.miter_limit = 5;
            _cs.dash_array = new float[0];
            _cs.dash_offset = 0;

            //Text
            _cs.ts.Tf = null;
            _cs.ts.Tfs = 1;
            _cs.ts.Th = 1;
            _cs.ts.Tr = 0;
            _cs.ts.Tc = 0;
            _cs.ts.Tl = 0;
            _cs.ts.Tw = 0;
            _cs.ts.Fill = true;
            _cs.ts.Stroke = false;
            _cs.ts.Clip = false;
        }

        public void PrepForAnnotations(bool init)
        {
            if (init)
            {
                //Saves the init state
                Save();
            }
            else
            {
                //Pops down any leftover state.
                while (_state_stack.Count != 0)
                    Restore();
            }
        }

        /// <summary>
        /// Clears cached data. Object can still be used after disposal.
        /// </summary>
        public void Dispose()
        {
            if (_cs.ts.Tf != null)
            {
                //The font can still be reused after this, so
                //calling dismiss instead of dispose.
                _cs.ts.Tf.Dismiss();
                _cs.ts.Tf = null;
            }
            _path.Dispose();
            if (_rs.last_pen != null)
            {
                _rs.last_pen.Dispose();
                _rs.last_pen = null;
            }
            if (_cs.fill != null)
                _cs.fill.Dispose();
            _cs.CTM.Dispose();
            if (_rs.tm.Tlm != null)
                _rs.tm.Tlm.Dispose();
            if (_rs.tm.Tm != null)
                _rs.tm.Tm.Dispose();
        }

        #endregion

        #region General Graphic State

        /// <summary>
        /// The width of strokes.
        /// </summary>
        public double StrokeWidth
        {
            get { return _cs.line_width; }
            set
            {
                _cs.line_width = (float) value;
                _rs.new_pen = true;
            }
        }

        /// <remarks>
        /// Except for tolerance on bounds calculations there does not
        /// seem to be an equivalent to flatness tolerance in WPF
        /// </remarks>
        public void SetFlatness(double i) { }

        /// <summary>
        /// Set how lines are joined togheter
        /// </summary>
        public void SetLineJoinStyle(xLineJoin style)
        {
            LineJoin plj;
            if (style == xLineJoin.Miter)
                plj = LineJoin.Miter;
            else if (style == xLineJoin.Round)
                plj = LineJoin.Round;
            else plj = LineJoin.Bevel;

            _cs.line_join = plj;
            _rs.new_pen = true;
        }

        /// <summary>
        /// Set how lines are ended
        /// </summary>
        public void SetLineCapStyle(xLineCap style)
        {
            LineCap plc;
            if (style == xLineCap.Butt)
                plc = LineCap.Flat;
            else if (style == xLineCap.Round)
                plc = LineCap.Round;
            else plc = LineCap.Square;

            _cs.line_cap = plc;
            _rs.new_pen = true;
        }

        /// <summary>
        /// Sets the miter limit
        /// </summary>
        /// <remarks>
        ///  Adobe and WPF use conceptually different miter limits. There's no
        /// practical way of equating them with eachother. See:
        ///  http://blogs.msdn.com/b/mswanson/archive/2006/03/23/559698.aspx
        /// 
        ///  Basic problem is:
        ///   - WPF: Miter if a cuttoff calulated to 0.5 * tickness * limit
        ///          (measured from the center of the join to its 
        ///           outside point)
        ///   - PDF: Miter is a treshhold caluclated to tickness * limit.
        ///          (measured from the inside of the join to its
        ///           outside point)
        /// 
        ///  While it's possible to make a single join match up, all of them
        ///  is a diffferent matter as Miter Limit is set per pen, not per 
        ///  joint.
        /// </remarks>
        public void SetMiterLimit(double limit)
        {
            if (limit == 0) _cs.miter_limit = 0;
            else _cs.miter_limit = (float) (limit / 2);
            _rs.new_pen = true;
        }

        /// <remarks>
        /// WPF's stroke dash array depend to the tickness of the current
        /// stroke width. Must thererefor wait to create the WPF object 
        /// until the line width is known. 
        /// </remarks>
        public void SetStrokeDashAr(xDashStyle ds)
        {
            _cs.dash_array = XtoGDI.ToFloat(ds.Dashes);
            _cs.dash_offset = (float) ds.Phase;
            _rs.new_pen = true;
        }

        public void SetGState(PdfGState gs)
        {
            //This impl. checks for the features that may be worth implementing
            double? num;

            num = gs.LW;
            if (num != null) StrokeWidth = num.Value;
            if (gs.LC != null) SetLineCapStyle(gs.LC.Value);
            if (gs.LJ != null) SetLineJoinStyle(gs.LJ.Value);
            num = gs.ML;
            if (num != null) SetMiterLimit(num.Value);
            if (gs.D != null) SetStrokeDashAr(gs.D.Value);
            //Rendering intent unimplemented
            //Overprint unimplemented
            if (gs.Font != null) throw new NotImplementedException();
            //Black generation unimplemented
            //Undercolor unimplemented
            if (gs.TR2 != null)
            {
                //Todo: Can also be a name or a single function
                var tr = gs.TR2.ToArray();
                if (tr.Length != 4 && tr.Length != 1)
                    throw new PdfNotSupportedException();
                _cs.transfer = tr;
                for (int c = 0; c < tr.Length; c++)
                    tr[c].Init();
            }
            else if (gs.TR != null)
            {
                //Todo: Can be name or a single function
                var tr = gs.TR.ToArray();
                if (tr.Length != 4 && tr.Length != 1)
                    throw new PdfNotSupportedException();
                _cs.transfer = tr;
                for (int c = 0; c < tr.Length; c++)
                    tr[c].Init();
            }

            //Halftones unimplemented
            //Flatness unimplemented
            if (gs.SM != null)
            {
                //Todo:
                //Smothness tolerance. No pattern currently supports this,
                //but basically it sets the expected error tolerance of
                //color calculations. 
            }
            var sa = gs.SA;
            if (sa != null && sa.Value != _cs.stroke_adj)
            {
                _cs.stroke_adj = sa.Value;
                _rs.new_pen = true;
            }
            //No alpha related parameters supported by this renderer


        }

        /// <summary>
        /// Set render intent
        /// </summary>
        public void SetRI(string ri) { }

        #endregion

        #region Special graphics state

        /// <summary>
        /// Saves the state
        /// </summary>
        /// <remarks>
        /// Beware: Not all state information is saved. 
        /// </remarks>
        public void Save()
        {
            _cs.GraphicState = _cr.Save();
            _state_stack.Push(_cs);
        }

        /// <summary>
        /// Restores the previous state
        /// </summary>
        public void Restore()
        {
            //Refuses to pop beyond the state stack
            if (_state_stack.Count == 0)
                return;

            _cs = _state_stack.Pop();
            _cr.Restore(_cs.GraphicState);
            _rs.new_pen = true;
            _rs.clip = false;
        }

        /// <summary>
        /// Prepend matrix to CTM
        /// </summary>
        public void PrependCM(xMatrix xm)
        {
            Matrix m = XtoGDI.ToMatrix(xm);
            _cs.CTM.Multiply(m);
            m.Multiply(_cr.Transform, MatrixOrder.Append);
            _cr.Transform = m;
            _rs.new_pen = true;
        }

        #endregion

        #region Color

        public void SetFillColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.fillCS = DeviceCMYK.Instance;
            if (_cs.fill != null) _cs.fill.Dispose();
            _cs.fill = new SolidBrush(
                    XtoGDI.ToColor(
                        _cs.fillCS.Converter.MakeColor(
                            new double[] { cyan, magenta, yellow, black }
                        )
                    )
                );
        }
        public void SetFillColor(double red, double green, double blue)
        {
            _cs.fillCS = DeviceRGB.Instance;
            if (_cs.fill != null) _cs.fill.Dispose();
            _cs.fill = new SolidBrush(Color.FromArgb((byte)(red * 255), (byte)(green * 255), (byte)(blue * 255)));
        }
        public void SetFillColor(double gray)
        {
            _cs.fillCS = DeviceGray.Instance;
            byte bgray = (byte)(gray * 255);
            if (_cs.fill != null) _cs.fill.Dispose();
            _cs.fill = new SolidBrush(Color.FromArgb(bgray, bgray, bgray));
        }
        public void SetFillColorSC(double[] color)
        {
            if (_cs.fill != null) _cs.fill.Dispose();
            _cs.fill = new SolidBrush(XtoGDI.ToColor(_cs.fillCS.Converter.MakeColor(color)));
        }
        public void SetFillColor(double[] color)
        {
            if (_cs.fill != null) _cs.fill.Dispose();
            _cs.fill = new SolidBrush(XtoGDI.ToColor(_cs.fillCS.Converter.MakeColor(color)));
        }

        public void SetStrokeColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.strokeCS = DeviceCMYK.Instance;
            _cs.stroke = XtoGDI.ToColor(
                            _cs.strokeCS.Converter.MakeColor(
                                new double[] { cyan, magenta, yellow, black }
                            )
                        );
        }
        public void SetStrokeColor(double red, double green, double blue)
        {
            _cs.strokeCS = DeviceRGB.Instance;
            _cs.stroke = Color.FromArgb((byte)(red * 255), (byte)(green * 255), (byte)(blue * 255));
        }
        public void SetStrokeColor(double gray)
        {
            _cs.strokeCS = DeviceGray.Instance;
            byte bgray = (byte)(gray * 255);
            _cs.stroke = Color.FromArgb(bgray, bgray, bgray);
        }
        public void SetStrokeColorSC(double[] color)
        {
            _cs.stroke = XtoGDI.ToColor(_cs.strokeCS.Converter.MakeColor(color));
        }
        public void SetStrokeColor(double[] color)
        {
            _cs.stroke = XtoGDI.ToColor(_cs.strokeCS.Converter.MakeColor(color));
        }

        public void SetFillCS(IColorSpace cs)
        {
            if (cs is PatternCS)
                _cs.fillCS = new CSPattern(((PatternCS)cs).UnderCS);
            else
            {
                _cs.fillCS = cs;
                if (_cs.fill != null) _cs.fill.Dispose();
                _cs.fill = new SolidBrush(XtoGDI.ToColor(cs.DefaultColor.ToDblColor()));
            }
        }

        public void SetStrokeCS(IColorSpace cs)
        {
            if (cs is PatternCS)
                _cs.strokeCS = new CSPattern(((PatternCS)cs).UnderCS);
            else
            {
                _cs.strokeCS = cs;
                _cs.stroke = XtoGDI.ToColor(cs.DefaultColor.ToDblColor());
            }
        }

        public void SetFillPattern(PdfShadingPattern pat)
        {
            //Blindly assumes that _cs.fillCS is CSPattern
            //Todo: fix this (And check what Adobe do in these cases)
            var csp = (CSPattern)_cs.fillCS;
            csp.Pat = pat;
            csp.CPat = null;
        }

        /// <summary>
        /// Pattens should be compiled before rendering.
        /// </summary>
        public void SetFillPattern(double[] color, PdfTilingPattern pat)
        {
            SetFillPattern(color, new PdfCompiler().Compile(pat));
        }

        public void SetFillPattern(double[] color, CompiledPattern pat)
        {
            if (pat != null)
            {
                //Blindly assumes that _cs.fillCS is CSPattern
                //Todo: fix this
                var csp = (CSPattern)_cs.fillCS;
                csp.Pat = null;
                csp.CPat = pat;

                if (color != null)
                {
                    if (_cs.fill != null) _cs.fill.Dispose();
                    _cs.fill = new SolidBrush(XtoGDI.ToColor(csp.CS.Converter.MakeColor(color)));
                }
            }
        }

        public void SetStrokePattern(PdfShadingPattern pat)
        {
            //Blindly assumes that _cs.fillCS is CSPattern
            //Todo: fix this
            var csp = (CSPattern)_cs.strokeCS;
            csp.Pat = pat;
            csp.CPat = null;
        }

        public void SetStrokePattern(double[] color, PdfTilingPattern pat)
        {
            SetStrokePattern(color, new PdfCompiler().Compile(pat));
        }

        public void SetStrokePattern(double[] color, CompiledPattern pat)
        {
            //Blindly assumes that _cs.fillCS is CSPattern
            //Todo: fix this
            var csp = (CSPattern)_cs.strokeCS;
            csp.Pat = null;
            csp.CPat = pat;

            if (color != null)
                _cs.stroke = XtoGDI.ToColor(csp.CS.Converter.MakeColor(color));
        }

        #endregion

        #region Shading patterns

        public void Shade(PdfShading shading)
        {
            //We draw into the clip path
            var clip_path = _cr.ClipBounds;
            var gs = _cr.Save();

            try
            {
                using (var ig = new GDIGraphics(_cr))
                {
                    if (shading is PdfAxialShading)
                    {
                        var ax = (PdfAxialShading)shading;

                        _cr.SmoothingMode = ax.AntiAlias ? SmoothingMode.AntiAlias : SmoothingMode.None;

                        var iCTM = _cr.Transform;
                        iCTM.Invert();
                        var pix = Transform(iCTM, new PointF(1, 1));
                        xVector px = new xVector(pix.X, pix.Y);

                        rGDIPattern.RenderShade(ax, ig, XtoGDI.ToRect(clip_path), px);
                    }
                    else
                    {
                        //Radial and Sampler patterns don't support AA.
                        _cr.SmoothingMode = SmoothingMode.None;
                        rGDIPattern.RenderShade(shading, ig, XtoGDI.ToRect(clip_path));
                    }
                }
            }
            finally
            {
                //Restores antialising, and potentially other stuff. 
                _cr.Restore(gs);
            }
        }

        #endregion

        #region Inline images

        public void DrawInlineImage(PdfImage img)
        { DrawImage(img); }

        #endregion

        #region XObjects

        /// <summary>
        /// Draws an image
        /// </summary>
        public void DrawImage(PdfImage img)
        {
            
        }

        /// <summary>
        /// Draws a form. 
        /// </summary>
        /// <param name="form">Form to draw</param>
        /// <returns>True if the form was drawn, false if this function is unsuported</returns>
        public bool DrawForm(PdfForm form)
        {
            return false;
        }

        public void DrawForm(CompiledForm form)
        {
            //Saves all state information
            Save();
            //There's no need to save the entire RenderState in this
            //case as the values will be reset anyway.

            //Sets state back to defaults
            ResetState();

            //Pushes the form's matrix onto the stack,
            //it's effectivly the "to device space"
            //matrix
            PrependCM(form.Matrix);

            //A form's coordinate system is sepperate
            //from the page. Therefore the "device space"
            //matrix needs to be updated. By inverting the
            //CTM we get a matrix that transforms from device
            //space to user space on the form
            var page_level = _rs.from_device_space;
            _rs.from_device_space = _cs.CTM;
            _rs.from_device_space.Invert();

            //Pushes the clip after the form.Matrix, meaning the BBox
            //coordinates lies in the form's coordinate system.
            var bounds = XtoGDI.ToRect(form.BBox);
            _cr.IntersectClip(bounds);

            //Renders the form. No try/catch is needed.
            ((IDraw)this).Executor.Execute(form.Commands, this);

            //Restores state back to what it was before execution.
            Restore();
            _rs.from_device_space = page_level;
        }

        #endregion

        #region Path construction

        /// <summary>
        /// Starts a path from the given point.
        /// </summary>
        public void MoveTo(double x, double y)
        {
            _current_point = new PointF((float) x, (float) y);
            _path.StartFigure();
        }

        /// <summary>
        /// Draws a line to the given point
        /// </summary>
        public void LineTo(double x, double y)
        {
            var next = new PointF((float) x, (float) y);
            _path.AddLine(_current_point, next);
            _current_point = next;
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var next = new PointF((float)x3, (float)y3);
            _path.AddBezier(_current_point, new PointF((float)x1, (float)y1), new PointF((float)x2, (float)y2), next);
            _current_point = next;
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x3, double y3)
        {
            var next = new PointF((float)x3, (float)y3);
            _path.AddBezier(_current_point, new PointF((float)x1, (float)y1), next, next);
            _current_point = next;
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            var next = new PointF((float)x3, (float)y3);
            _path.AddBezier(_current_point, _current_point, new PointF((float)x2, (float)y2), next);
            _current_point = next;
        }

        /// <summary>
        /// Draws a rectangle as a new figure/subpath.
        /// </summary>
        public void RectAt(double x, double y, double width, double height)
        {
            //After a path is closed the current point is moved back to
            //the start of the path. 
            _current_point = new PointF((float)x, (float)y);
            _path.StartFigure();
            _path.AddRectangle(XtoGDI.Rect(x, y, width, height));
        }

        public void ClosePath()
        {
            _path.CloseFigure();
        }

        public void DrawClip(xFillRule fr)
        {
            //SetClip(fr);
            //DrawPath(_rs.clip_rule, false, false, false);
        }

        public void DrawPathNZ(bool close, bool stroke, bool fill)
        {
            DrawPath(FillMode.Winding, close, stroke, fill);
        }

        //In retrospect, it's pointless to have sepperate DrawPathEO/NZ
        public void DrawPathEO(bool close, bool stroke, bool fill)
        {
            DrawPath(FillMode.Alternate, close, stroke, fill);
        }

        private void DrawPath(FillMode fr, bool close, bool stroke, bool fill)
        {
            _path.FillMode = fr;

            if (close)
                _path.CloseFigure();

            //Only one pattern can be rendered at the time, so splitting up
            //the draws.
            if (fill)
            {
                if (_cs.fillCS is CSPattern)
                   FillPattern(_path);
                else
                    _cr.FillPath(_cs.fill, _path);
            }

            if (stroke)
            {
                if (_cs.strokeCS is CSPattern)
                    StrokePattern(_path);
                else
                    _cr.DrawPath(CreatePen(), _path);
            }

            if (_rs.clip)
            {
                _path.FillMode = _rs.clip_rule;
                _cr.SetClip(_path, CombineMode.Intersect);
                _rs.clip = false;
            }

            ClearPath();
        }

        private void StrokePattern(GraphicsPath path)
        {
            var stroke_path = (GraphicsPath) path.Clone();
            stroke_path.Widen(CreatePen());
            FillPattern(stroke_path);
            stroke_path.Dispose();
        }

        private void FillPattern(GraphicsPath path)
        {
            var cspat = (CSPattern)_cs.fillCS;
            if (cspat.Pat == null && cspat.CPat == null) return;
            _cr.Save();

            //Pushing on the geometry as a clip. This will force
            //the pattern render "into" the geometry.
            var gs = _cr.Save();
            _cr.SetClip(path, CombineMode.Intersect);

            //Get the bounding box of the geometry being drawn as
            //it's a good bit easier to work with rectangles.
            RectangleF outline = path.GetBounds();

            #region About Coordinate systems
            // 
            //Patterns are drawn on the page level. This means in essence
            //that the CTM will be the identity matrix, ignoring the transform 
            //to device space.
            //
            //The bounds objects is not on the page level however, instead
            //it is in whatever coordinate space has been set through the CTM
            //
            //This means the bounding box must be "transformed" down to the
            //page level, so we get it's true size.
            #endregion

            //We then transform the bounds down to the device level
            outline = Transform(outline);

            //However that was a little too far, so we transform
            //the bounds up to the user space again.
            outline = Transform(_from_device_space, outline);

            try
            {
                //Cairo is easier than WPF here. Instead of a inverse matrix we simply
                //set the CTM to identity. Weeell not identity then. Cairo patterns are
                //apparently not affected directly by the CTM (the CTM used when drawing
                //the pattern, not the one used when creating the pattern).
                //
                //So we set this matrix so that the pattern will be flipped and streached
                //as needed during creation.
                _cr.Transform = _from_device_space;

                if (cspat.Pat != null)
                {
                    //Renders the pattern. This rendering is independent from how the
                    //page is scaled/rotated/etc through the CTM (it's not independent
                    //from the rotation property on a page object though)
                    Render(cspat.Pat, outline);

                }
            }
            finally { _cr.Restore(gs); }
        }

                /// <summary>
        /// Renders a pattern
        /// </summary>
        /// <remarks>
        /// This rendering does not depend on the _cs.CTM variable, so it
        /// need not keep _dc.CTM up to date with the dc object (unlike the
        /// tile pattern function above)
        /// </remarks>
        public void Render(PdfShadingPattern pat, RectangleF bounds)
        {
            var shade = pat.Shading;

            _cr.SmoothingMode = (shade is PdfAxialShading && shade.AntiAlias) ? SmoothingMode.AntiAlias : SmoothingMode.None;

            //The pattern may have its own coordinate system. I've not done a
            //lot of experimenting with this, but as far as I can tell the
            //COORDS* array is in the pattern's coordinate system, and all drawing
            //calls are to be in the pattern's coordinate system.
            // *(Specs say "target coordinate space", without elaborating what
            //   the target is)
            var m = XtoGDI.ToMatrix(pat.Matrix);
            var crm = _cr.Transform;
            crm.Multiply(m);
            _cr.Transform = crm;

            //The bounds rectangle also needs to be inversly transformed 
            //from the "page space" to the "pattern space".
            m.Invert();
            bounds = Transform(m, bounds);

            m.Dispose();

            //Paints the backround color. This must be done before pushing
            //the BBox clip. This is not to be done when using the sh
            //operator, and isn't quite right for the transparent rendering
            //model.
            var back = shade.Background;
            if (back != null)
            {
                var col = new SolidBrush(XtoGDI.ToColor(back));
                _cr.FillRectangle(col, bounds);
                col.Dispose();
            }

            //The pattern is clipped to its bounding box
            var bbox = shade.BBox;
            if (bbox != null)
            {
                _cr.IntersectClip(XtoGDI.ToRect(bbox));
            }

            //Then one can finally run the shader
            using (var ig = new GDIGraphics(_cr))
            {
                if (shade is PdfAxialShading)
                {
                    xVector px;
                    if (_cr.SmoothingMode != SmoothingMode.None)
                    {
                        var iCTM = _cr.Transform;
                        iCTM.Invert();
                        var pix = Transform(iCTM, new PointF(1, 1));
                        px = new xVector(pix.X, pix.Y);
                    }
                    else px = new xVector();
                    rGDIPattern.RenderShade((PdfAxialShading)shade, ig, XtoGDI.ToRect(bounds), px);
                }
                else if (shade is PdfRadialShading)
                    rGDIPattern.RenderShade((PdfRadialShading)shade, ig, XtoGDI.ToRect(bounds));
                else if (shade is PdfFunctionShading)
                    rGDIPattern.RenderShade((PdfFunctionShading)shade, ig, XtoGDI.ToRect(bounds));
            }
        }

        private Pen CreateNewPen(Brush brush)
        {
            float w = _cs.line_width;

            //I'm not sure, but Adobe may ignore this parameter alltogether.
            if (_cs.stroke_adj) /* <-- todo: Check how Adobe renders issue58.pdf when small. Add "|| true" here to get sumatra behavior */
            {
                double scale_factor = Math.Abs(_cs.CTM.Elements[0]);
#if AutoAdjustLikeAdobe
                //Behaving like Adobe
                if (Real.IsZero(scale_factor))
                    scale_factor = 1 / _cs.line_width;
#else
                //As far as I can tell this differs from how Acrobat Reader
                //handles the situation. In cases where M11 is 0 Acrobat Reader
                //will set the line width to 1 and call it a day. 

                //Todo: elaborate the math
                if (scale_factor == 0) {
                    //scale_factor = Math.Abs(_cs.CTM.M12);

                    Vector vec = new Vector(1, 0); vec = Vector.Multiply(vec, _cs.CTM);
                    var l = vec.Length;
                    scale_factor = _cs.CTM.M11 * vec.X / l + _cs.CTM.M12 * vec.Y / l;
                }
#endif

                //First we scale down the size to see how big it is "on screen"
                double w_scaled = _cs.line_width * scale_factor;

                //If the size is smaller than 1 we assume it's smaller than one pixel,
                //and according to the specs a stroke should not be less than one pixel.
                if (w_scaled < 1)
                {
                    //We therefor scale it up. 
                    //(The 0.75 seems to give better result)
                    w = (float) (1 / scale_factor * 0.75);
                }

                //AFAICT WPF does sanity checking. So infinity, etc, will simply result
                //in the lines not being drawn.
            }

            var pen = new Pen(brush, w)
            {
                LineJoin = _cs.line_join,
                StartCap = _cs.line_cap,
                EndCap = _cs.line_cap,
                MiterLimit = _cs.miter_limit
            };

            //Must adjust the dashstyle to WPF norms
            if (_cs.dash_offset == 0 && _cs.dash_array.Length == 0)
            {
                pen.DashStyle = DashStyle.Solid;
            }
            else
            {
                // PDF dashes are defined like this:
                //
                // [array] phase.
                //
                // The phase sets when dashes start. A phase of 3 means
                // that you get "three units 
                float[] ar = (float[]) _cs.dash_array.Clone();
                for (int c = 0; c < ar.Length; c++)
                    ar[c] /= w;
                pen.DashStyle = DashStyle.Dash;
                pen.DashOffset = _cs.dash_offset;
                pen.DashPattern = ar;
            }

            return pen;
        }

        private Pen CreatePen()
        {
            if (!_rs.new_pen) return _rs.last_pen;

            if (_rs.last_pen != null)
            {
                _rs.last_pen.Brush.Dispose();
                _rs.last_pen.Dispose();
            }
            _rs.last_pen = CreateNewPen(new SolidBrush(_cs.stroke));

            return _rs.last_pen;
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            _rs.clip = true;
            _rs.clip_rule = rule == xFillRule.Nonzero ? FillMode.Winding : FillMode.Alternate;
        }

        #endregion

        #region Text objects

        /// <summary>
        /// Sets the TM back to identity
        /// </summary>
        public void BeginText()
        {
            //_text_mode = true;
            if (_rs.tm.Tm == null)
                _rs.tm.Tm = new Matrix();
            else
                _rs.tm.Tm.Reset();
            if (_rs.tm.Tlm == null)
                _rs.tm.Tlm = new Matrix();
            else
                _rs.tm.Tlm.Reset();
        }

        /// <summary>
        /// Ends text mode
        /// </summary>
        public void EndText()
        {
            //_text_mode = false;
        }

        #endregion

        #region Text State

        public void SetCharacterSpacing(double tc)
        {
            _cs.ts.Tc = (float) tc;
        }

        /// <summary>
        /// Set the distance between words
        /// </summary>
        public void SetWordSpacing(double s)
        {
            _cs.ts.Tw = (float) s;
        }

        public void SetFont(cFont font, double size)
        {
            SetFont(font.MakeWrapper(), size);
        }

        public void SetFont(PdfFont font, double size)
        {
            if (_cs.ts.Tf != null)
                _cs.ts.Tf.Dismiss();

            //_cs.ts.Tf = font.Realize<StreamGeometry>(this);
            _cs.ts.Tfs = (float) size;
        }

        /// <summary>
        /// Sets a T3 font
        /// </summary>
        public void SetFont(CompiledFont font, double size)
        {
            if (_cs.ts.Tf != null)
                _cs.ts.Tf.Dismiss();

            _cs.ts.Tf = font.Realize();
            _cs.ts.Tfs = (float) size;
        }

        /// <summary>
        /// Set text rendering mode
        /// </summary>
        public void SetTextMode(xTextRenderMode mode)
        {
            _cs.ts.Fill = false;
            _cs.ts.Stroke = false;
            _cs.ts.Clip = false;

            switch (mode)
            {
                case xTextRenderMode.Fill:
                    _cs.ts.Fill = true; break;
                case xTextRenderMode.Stroke:
                    _cs.ts.Stroke = true; break;
                case xTextRenderMode.FillAndStroke:
                    _cs.ts.Fill = true; _cs.ts.Stroke = true; break;
                case xTextRenderMode.FillAndPath:
                    _cs.ts.Fill = true; _cs.ts.Clip = true; break;
                case xTextRenderMode.StrokeAndPath:
                    _cs.ts.Stroke = true; _cs.ts.Clip = true; break;
                case xTextRenderMode.FillStrokeAndPath:
                    _cs.ts.Stroke = true; _cs.ts.Fill = true;
                    _cs.ts.Clip = true; break;
                case xTextRenderMode.Path:
                    _cs.ts.Clip = true; break;
            }
        }

        /// <summary>
        /// Set the scaling of the text in horiontal direction
        /// </summary>
        public void SetHorizontalScaling(double th)
        {
            _cs.ts.Th = (float) th / 100;
        }

        /// <summary>
        /// Set text leading (distance between lines)
        /// </summary>
        public void SetTextLeading(double lead)
        {
            _cs.ts.Tl = (float) lead;
        }

        public void SetTextRise(double tr)
        {
            _cs.ts.Tr = (float) tr;
        }

        #endregion

        #region Text positioning

        public void SetTM(xMatrix m)
        {
            if (_rs.tm.Tm != null)
                _rs.tm.Tm.Dispose();
            _rs.tm.Tm = XtoGDI.ToMatrix(m);
            if (_rs.tm.Tlm != null)
                _rs.tm.Tlm.Dispose();
            _rs.tm.Tlm = (Matrix) _rs.tm.Tm.Clone();
        }

        /// <summary>
        /// Translates the TML and sets it to TM
        /// </summary>
        public void TranslateTLM(double x, double y)
        {
            var m = new Matrix();
            m.Translate((float) x, (float) y);
            _rs.tm.Tlm.Multiply(m);
            if (_rs.tm.Tm != null)
                _rs.tm.Tm.Dispose();
            _rs.tm.Tm = (Matrix) _rs.tm.Tlm.Clone();
            m.Dispose();
        }

        /// <summary>
        /// Move down one line
        /// </summary>
        public void TranslateTLM()
        {
            TranslateTLM(0, -_cs.ts.Tl);
        }

        public void SetTlandTransTLM(double x, double y)
        {
            _cs.ts.Tl = (float) -y;
            TranslateTLM(x, y);
        }

        #endregion

        #region Text showing

        public void DrawString(PdfItem[] str_ar)
        {
            //new TJ_BuildCMD(str_ar).Execute(this);
        }
        public void DrawString(BuildString str)
        {
            //new Tj_BuildCMD(str).Execute(this);
        }

        public void DrawString(PdfString text, double aw, double ac)
        {
            _cs.ts.Tc = (float) ac;
            _cs.ts.Tw = (float) aw;
            TranslateTLM();

            DrawString(text);
        }

        public void DrawString(PdfString text, bool cr)
        {
            if (cr) TranslateTLM();

            DrawString(text);
        }

        /// <summary>
        /// Draws a string
        /// </summary>
        private void DrawString(PdfString text)
        {
            
        }

        /// <summary>
        /// Draws a string array
        /// </summary>
        /// <remarks>
        /// Clipping must be done outside the "DrawString" function as
        /// WPF requires that all clipping is done in one draw call.
        /// We therefore collect all the clipping information before drawing.
        /// 
        /// Whops. Adobe X fails at rendering clipped arrays so there's little
        /// point in this complication after all. "Init for clipping" and
        /// "Clips" can be copied straight back into the draw strings methods 
        /// (at the start and end) as they were copied unmodified out.
        /// </remarks>
        public void DrawString(object[] text)
        {
            
        }

        #endregion

        #region Type3 fonts

        /// <summary>
        /// Sets a colored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        public void SetT3Glyph(double wx, double wy) { /* Ignored */ }

        /// <summary>
        /// Sets uncolored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        /// <param name="llx">Lower left X</param>
        /// <param name="lly">Lower left Y</param>
        /// <param name="urx">Upper right X</param>
        /// <param name="ury">Upper right Y</param>
        public void SetT3Glyph(double wx, double wy, double llx, double lly, double urx, double ury) { /* Ignored */ }

        #endregion

        #region Compatibility

        public void BeginCompatibility() { }
        public void EndCompatibility() { }

        #endregion

        #region State

        /// <summary>
        /// During rendering there are some non-state variables that must
        /// be saved/restored at times. To ease that they are collected
        /// in this struct
        /// </summary>
        struct RenderState
        {
            /// <summary>
            /// Reusing pens
            /// </summary>
            /// <remarks>There's no real need to save/restore these as they are always reset</remarks>
            public Pen last_pen;
            public bool new_pen;

            /// <summary>
            /// Object used for drawing
            /// </summary>
            //public DrawingContext dc;

            /// <summary>
            /// Matrix used to transform CTM to the
            /// user space
            /// </summary>
            public Matrix from_device_space;

            /// <summary>
            /// From how I'm reading the specs, the W and W*
            /// commands does not end the path. They just mark
            /// the path for use with clipping.
            /// </summary>
            /// <remarks>
            /// Note: This value is always set false on restore,
            /// but I've not tested if that's correct behaviour.
            /// 
            /// If it is correct behaviour there's no need to
            /// save/restore these (i.e. take them out of this
            /// struct)
            /// </remarks>
            public bool clip;
            public FillMode clip_rule;

            /// <summary>
            /// TextMetrix transform
            /// </summary>
            public TextMetrix tm;

            public void SetUp()
            {
                clip = false;
                tm = new TextMetrix();
                last_pen = null;
                new_pen = true;
            }
        }

        /// <summary>
        /// See 8.4 in the specs. Note that "clipping path" is tracked through the
        /// dc_pos parameter (CTM is too, but is also tracked here for scaling reasons)
        /// </summary>
        struct State
        {
            public GraphicsState GraphicState;

            /// <summary>
            /// Current transform matrix. 
            /// </summary>
            /// <remarks>
            /// We need to keep track of ctm to keep track of the scale factor.
            /// This factor is used to scale the width of thin lines so that
            /// they're always visible regardless of the zoom level.
            /// 
            /// This is done by Acrobat Reader, and effectivly lets an image have
            /// more detail as one zoom into the page. (Note scale_lines_to_output
            /// must be set to see this)
            /// 
            /// Now also used when dealing with patterns
            /// </remarks>
            public Matrix CTM;

            /// <summary>
            /// Todo: Not fullt implemented.
            /// </summary>
            /// <remarks>
            /// This clip path is on the device level. Any code that changes the
            /// device level will also have to adjust this.
            /// </remarks>
            public RectangleF Current_clip_path;

            /// <summary>
            /// Colorspace used for strokes.
            /// </summary>
            public IColorSpace strokeCS;

            /// <summary>
            /// Colorspace used for fills
            /// </summary>
            public IColorSpace fillCS;

            /// <summary>
            /// Color used for non-stroking
            /// </summary>
            public Brush fill;

            /// <summary>
            /// Color used for stroking
            /// </summary>
            public Color stroke;

            /// <summary>
            /// Graphics state parameters that only affect text
            /// </summary>
            public TextState ts;

            /// <summary>
            /// Thickness of lines
            /// </summary>
            public float line_width;

            /// <summary>
            /// How lines are capped off
            /// </summary>
            public LineCap line_cap;

            /// <summary>
            /// How lines are joined together
            /// </summary>
            public LineJoin line_join;

            /// <summary>
            /// See: http://blogs.msdn.com/b/mswanson/archive/2006/03/23/559698.aspx
            /// </summary>
            public float miter_limit;

            /// <summary>
            /// The dash style
            /// </summary>
            public float[] dash_array;
            public float dash_offset;

            /// <summary>
            /// Stroke adjustment
            /// </summary>
            /// <remarks>
            /// Perhaps make it so that scale_lines_to_output
            /// locks this at true, and drop the whole "not setting CTM"
            /// 
            /// I'm not sure, but Adobe may ignore this parameter alltogether.
            /// </remarks>
            public bool stroke_adj;

            /// <remarks>
            /// Transfer is only implemented for images. Won't do anything
            /// about this before tackeling alpha colors, as they are related.
            /// </remarks>
            public PdfFunction[] transfer;

            /// <summary>
            /// Resets the state back to default
            /// </summary>
            /// <remarks>
            /// Remeber that the clip path is set to infinity
            /// </remarks>
            public void Reset()
            {
                if (CTM == null)
                    CTM = new Matrix();
                else
                    CTM.Reset();
                strokeCS = DeviceGray.Instance;
                fillCS = DeviceGray.Instance;
                line_width = 1;
                line_cap = LineCap.Flat;
                line_join = LineJoin.Miter;
                miter_limit = 5;
                dash_array = new float[0];
                dash_offset = 0;
#if AutoAdjustLikeAdobe
                stroke_adj = true;
#else
                stroke_adj = false;
#endif
                fill = Brushes.Black;
                stroke = Color.Black;
                transfer = null;
                Current_clip_path = new RectangleF(new PointF(float.MinValue, float.MinValue),
                                                   new SizeF(float.MaxValue, float.MaxValue));
                
                //Text
                ts.Tf = null;
                ts.Tfs = 1;
                ts.Th = 1;
                ts.Tr = 0;
                ts.Tc = 0;
                ts.Tl = 0;
                ts.Tw = 0;
                ts.Fill = true;
                ts.Stroke = false;
                ts.Clip = false;
            }
        }

        struct AlphaState
        {

        }

        /// <summary>
        /// See 9.3.1
        /// </summary>
        struct TextState
        {
            /// <summary>
            /// Character spacing, i.e the distance between characters
            /// </summary>
            public float Tc;

            /// <summary>
            /// Word spacing, i.e. the distance between words
            /// </summary>
            public float Tw;

            /// <summary>
            /// Horizontal scaling
            /// </summary>
            public float Th;

            /// <summary>
            /// Font
            /// </summary>
            public rFont Tf;

            /// <summary>
            /// Font size
            /// </summary>
            public float Tfs;

            /// <summary>
            /// Text Rise
            /// </summary>
            public float Tr;

            /// <summary>
            /// Text leading. 
            /// 
            /// The vertical distance between text baselines.
            /// </summary>
            /// <remarks>
            /// Only used by the T*, ', and " operators
            /// </remarks>
            public float Tl;

            /// <summary>
            /// Text rendering mode
            /// </summary>
            public bool Fill;
            public bool Stroke;
            public bool Clip;
        }

        struct TextMetrix
        {
            /// <summary>
            /// Text metrix
            /// </summary>
            public Matrix Tm;

            /// <summary>
            /// Text line metrix
            /// </summary>
            public Matrix Tlm;

            public TextMetrix Clone()
            {
                TextMetrix tm = new TextMetrix();
                tm.Tm = Tm.Clone();
                tm.Tlm = Tlm.Clone();
                return tm;
            }
        }

        #endregion

        #region Helper functions

        private void FillRectangle(Brush pen, xRect r)
        {
            _cr.FillRectangle(pen, (float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
        }

        private RectangleF Transform(RectangleF r)
        {
            return Transform(_cr.Transform, r);
        }

        private PointF Transform(Matrix m, PointF p)
        {
            var pt = new PointF[1];
            pt[0] = p;
            m.TransformPoints(pt);
            return pt[0];
        }

        private RectangleF Transform(Matrix m, RectangleF r)
        {
            var points = new PointF[] 
            { 
                new PointF(r.Left, r.Bottom),
                new PointF(r.Left, r.Top),
                new PointF(r.Right, r.Top),
                new PointF(r.Right, r.Bottom)
            };
            m.TransformPoints(points);

            return Bounds(points);
        }

        private RectangleF Bounds(PointF[] p)
        {
            var p1 = p[1];
            float max_x = p1.X, min_x = max_x, max_y = p1.Y, min_y = max_x;
            for (int c = 1; c < p.Length; c++)
            {
                p1 = p[c];
                float val = p1.X;
                if (val > max_x)
                    max_x = val;
                else if (val < min_x)
                    min_x = val;

                val = p1.Y;
                if (val > max_y)
                    max_y = val;
                else if (val < min_y)
                    min_y = val;
            }

            return new RectangleF(min_x, min_y, max_x - min_x, max_y - min_y);
        }

        private class GDIGraphics : rGDIPattern.iGraphics, IDisposable
        {
            System.Drawing.Graphics _g;
            System.Drawing.Brush _brush;
            System.Drawing.Drawing2D.GraphicsPath _path;
            bool start_figure;
            System.Drawing.PointF _cp;

            public GDIGraphics(System.Drawing.Graphics g)
            {
                _g = g;
                _brush = System.Drawing.Brushes.Black;
                _path = new System.Drawing.Drawing2D.GraphicsPath();
                start_figure = true; ;
            }

            public void Dispose()
            {
                _brush.Dispose();
                _path.Dispose();
            }

            public void SetColor(DblColor col)
            {
                _brush.Dispose();
                _brush = new System.Drawing.SolidBrush(XtoGDI.ToColor(col));
            }

            public void CircleAt(double centerX, double centerY, double radius)
            {
                if (start_figure) { start_figure = true; _path.StartFigure(); }
                _path.AddEllipse((float)(centerX - radius), (float)(centerY - radius),
                              (float)(radius + radius), (float)(radius + radius));
            }

            public void DrawCircle(double centerX, double centerY, double radius)
            {
                CircleAt(centerX, centerY, radius);
                Fill();
            }

            public void RectangleAt(xRect r)
            {
                if (start_figure) { start_figure = true; _path.StartFigure(); }
                _path.AddRectangle(XtoGDI.ToRect(r));
            }

            public void RectangleAt(double x, double y, double width, double height)
            {
                if (start_figure) { start_figure = true; _path.StartFigure(); }
                _path.AddRectangle(new System.Drawing.RectangleF((float)x, (float)y, (float)width, (float)height));
            }

            public void DrawRectangle(double x, double y, double width, double height)
            {
                RectangleAt(x, y, width, height);
                Fill();
            }

            public void DrawRectangle(xRect r)
            {
                RectangleAt(r);
                Fill();
            }

            public void DrawLine(xPoint from, xPoint to, double width)
            {
                var pen = new System.Drawing.Pen(_brush, (float)width);
                _g.DrawLine(pen, XtoGDI.ToPoint(from), XtoGDI.ToPoint(to));
                pen.Dispose();
            }

            public void MoveTo(double x, double y)
            {
                _cp = new System.Drawing.PointF((float)x, (float)y);
                _path.StartFigure();
                start_figure = false;
            }

            public void LineTo(double x, double y)
            {
                var np = new System.Drawing.PointF((float)x, (float)y);
                _path.AddLine(_cp, np);
                _cp = np;
            }

            public void ClosePath()
            {
                _path.CloseFigure();
            }

            public void Fill()
            {
                _g.FillPath(_brush, _path);
                _path.Reset();
                start_figure = true;
            }

            public void Clip()
            {
                _g.SetClip(_path, System.Drawing.Drawing2D.CombineMode.Intersect);
                _path.Reset();
                start_figure = true;
            }

            public void Transform(xMatrix m)
            {
                var um = XtoGDI.ToMatrix(m);
                um.Multiply(_g.Transform, System.Drawing.Drawing2D.MatrixOrder.Append);
                _g.Transform = um;
                um.Dispose();
            }
        }

        #endregion
    }

    class XtoGDI
    {
        public static PointF ToPoint(xPoint p)
        {
            return new PointF((float)p.X, (float)p.Y);
        }

        public static xRect ToRect(RectangleF r)
        {
            return new xRect(r.Left, r.Top, r.Right, r.Bottom);
        }

        public static RectangleF ToRect(PdfRectangle r)
        {
            return ToRect(new xRect(r));
        }

        public static RectangleF ToRect(xRect r)
        {
            return new RectangleF((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
        }

        public static Matrix ToMatrix(xMatrix m)
        {
            return new Matrix((float)m.M11, (float)m.M12, (float)m.M21, (float)m.M22, (float)m.OffsetX, (float)m.OffsetY);
        }

        public static Color ToColor(PdfColor col)
        {
            return ToColor(col.ToDblColor());
        }

        public static Color ToColor(DblColor col)
        {
            return Color.FromArgb((int)(col.R * 255), (int)(col.G * 255), (int)(col.B * 255));
        }
    
        public static RectangleF Rect(double x, double y, double w, double h)
        { 
            return new RectangleF((float) x, (float) y, (float) w, (float) h);
        }

        public static float[] ToFloat(double[] ar)
        {
            if (ar == null) return null;
            var fa = new float[ar.Length];
            for (int c = 0; c < fa.Length; c++)
                fa[c] = (float)ar[c];
            return fa;
        }
    }
}
