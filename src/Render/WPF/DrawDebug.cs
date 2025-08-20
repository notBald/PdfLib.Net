//AutoAdjustLikeAdobe with UseGuidelineSet is fairly close to how Adobe renders lines,
//but unlike how MuPdf renders lines. UseGuidelineSet alone is more like MuPdf.
#define AutoAdjustLikeAdobe
//Experimental. Assumes all linwidths are "some constant" (line width is calced when
//calling "create pen" so I just fudged it). It's not impl. for DrawPathEO or any other 
//vector commands other than MoveTo and LineTo (See MoveToo)
//#define UseGuidelineSet
//#define SHARE_TEXT
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfLib.Pdf;
using PdfLib.Compile;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Render.Commands;
using PdfLib.Compose.Text;
using PdfLib.Compose;

namespace PdfLib.Render.WPF
{
#if DEBUG
    /// <summary>
    /// Used for debugging
    /// </summary>
    /// <remarks>
    /// Renders without images, color or text. Primarily used
    /// to see where objects are placed.
    /// </remarks>
    internal class DrawDebug : IDraw, IFontFactory<StreamGeometry>
    {
        #region State

        DrawingContext _dc;
        IExecutor _executor;
#if UseGuidelineSet
        GuidelineSet _gs;
#endif

        Pen _dbug_pen = null;

        /// <summary>
        /// Reusing pens
        /// </summary>
        Pen _last_pen = null;
        bool _new_pen = true;

        /// <summary>
        /// Path used for dawing shapes.
        /// </summary>
        List<PathFigure> _closed_paths;
        PathFigure _path;
        PathSegmentCollection _paths;

        /// <summary>
        /// From how I'm reading the specs, the W and W*
        /// commands does not end the path. They just mark
        /// the path for use with clipping.
        /// </summary>
        private bool _clip = false;
        private FillRule _clip_rule;

        /// <summary>
        /// Current state
        /// </summary>
        private State _cs = new State();
        Stack<State> _state_stack = new Stack<State>();

        /// <summary>
        /// TextMetrix transform
        /// </summary>
        private TextMetrix _tm = new TextMetrix();

        /// <summary>
        /// Does not track graphic state.
        /// </summary>
        /// <remarks>Isn't this done by the compiler anyway?</remarks>
        public GS GraphicState { get { return GS.Unknown; } }

        /// <summary>
        /// Cache over fonts and glyphs. 
        /// </summary>
        private FontCache _font_cache = new FontCache();

        #endregion

        #region Execution

        /// <summary>
        /// Rendering precision
        /// </summary>
        public int Precision { get { return 8; } }

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
        public void Init(PdfRectangle MediaBox, double output_width, double output_height,
                         bool respect_aspect, bool scale_lines_to_output, DrawingContext dc, int rotate)
        {
            // Calcs the size of the user space. Negative size is intended.
            double user_space_width = MediaBox.URx - MediaBox.LLx;
            double user_space_heigth = MediaBox.LLy - MediaBox.URy;
            double abs_us_width = Math.Abs(user_space_width);
            double abs_us_height = Math.Abs(user_space_heigth);

            // Corrects for aspect ratio.
            xSize output = PdfRender.CalcDimensions(MediaBox, output_width, output_height, respect_aspect, rotate);

            //Sets up mapping from the defualt PDF user space to WPF device space.
            //Note that I assume 72 DPI on the PDF user space coordinates. 
            // (I.e. that a "user space pixel" is 1/72 of a inch. This can be changed in later pdf versions)
            //
            //  Common PDF: 0.1---1.1    WPF: 0.0---1.0   Scale matrix: Sx--0    M11--M12
            //               |     |           |     |                  |   |     |    |
            //              0.0---1.0         0.1---1.1                 0--Sy    M21--M22
            double userspace_dpi_x = 72;
            double userspace_dpi_y = 72;
            double device_dpi_x = 96 / (1 + 1 / 3d);
            double device_dpi_y = 96 / (1 + 1 / 3d);
            double device_width = output.Width * device_dpi_x / userspace_dpi_x;
            double device_height = output.Height * device_dpi_y / userspace_dpi_y;
            double scale_x = device_width / user_space_width;
            double scale_y = device_height / user_space_heigth;

            //Note: If nothing is rendered it's likely because the offset_x/y is set wrong
            //      here.
            Matrix from_userspace_to_device_coords = new Matrix(scale_x, 0, 0,
                                                                scale_y,
                                                                (scale_x < 0) ? output.Width : 0,
                                                                (scale_y < 0) ? output.Height : 0);

            //Rotates the whole page
            /*if (false)
            {
                double center_w = abs_us_width / 2;
                double center_h = abs_us_height / 2;
                from_userspace_to_device_coords.RotateAtPrepend(270, center_w, center_h);
                double offset = center_w - center_h;
                from_userspace_to_device_coords.TranslatePrepend(offset, offset * -1);
            }*/

            _dc = dc;
            _dc.PushTransform(new MatrixTransform(from_userspace_to_device_coords));

            //Sets up the state
            _cs.Reset();
            _cs.dc_pos = 1;

            //I'm not sure, but Adobe may ignore this parameter alltogether.
            _cs.stroke_adj = true;
#if !AutoAdjustLikeAdobe
            if (scale_lines_to_output) 
#endif
            _cs.CTM = from_userspace_to_device_coords;
            _new_pen = true;
            _closed_paths = new List<PathFigure>();
            _path = new PathFigure();
            _paths = _path.Segments;

            //Fills the whole page white.
            _dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, abs_us_width, abs_us_height));
        }

        public void PrepForAnnotations(bool init)
        {
            if (init)
            {
                //There's not anything that needs to be done.
            }
            else
            {
                //Pops down any leftover state.
                while (_state_stack.Count != 0)
                    Restore();

                //Pops down to the original state
                while (_cs.dc_pos > 2)
                {
                    _cs.dc_pos--;
                }

                _cs.Reset();
            }
        }

        /// <summary>
        /// Clears cached data. Class can be used after disposal.
        /// </summary>
        public void Dispose()
        {
            if (_cs.ts.Tf != null)
            {
                _cs.ts.Tf.Dismiss();
                _cs.ts.Tf = null;
            }
        }

        #endregion

        #region General Graphic State

        /// <summary>
        /// Width lines will be stroked with.
        /// Will always return -1
        /// </summary>
        public double StrokeWidth
        {
            get { return _cs.line_width; }
            set
            {
                _cs.line_width = value;
                _new_pen = true;
            }
        }

        public void SetFlatness(double i) { }

        /// <summary>
        /// Set how lines are joined togheter
        /// </summary>
        public void SetLineJoinStyle(xLineJoin style)
        {
            PenLineJoin plj;
            if (style == xLineJoin.Miter)
                plj = PenLineJoin.Miter;
            else if (style == xLineJoin.Round)
                plj = PenLineJoin.Round;
            else plj = PenLineJoin.Bevel;

            _cs.line_join = plj;
            _new_pen = true;
        }

        /// <summary>
        /// Set how lines are ended
        /// </summary>
        public void SetLineCapStyle(xLineCap style)
        {
            PenLineCap plc;
            if (style == xLineCap.Butt)
                plc = PenLineCap.Flat;
            else if (style == xLineCap.Round)
                plc = PenLineCap.Round;
            else plc = PenLineCap.Square;

            _cs.line_cap = plc;
            _new_pen = true;
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
        ///   - WPF: Miter if a cuttoff calulated to 0.5 * tichness * limit
        ///          (measured from the center of the join to its 
        ///           outside point)
        ///   - PDF: Miter is a treshhold caluclated to tichness * limit.
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
            else _cs.miter_limit = limit / 2;
            _new_pen = true;
        }

        public void SetStrokeDashAr(xDashStyle ds)
        {
            _cs.dash_array = ds.Dashes;
            _cs.dash_offset = ds.Phase;
            _new_pen = true;
        }

        public void SetGState(PdfGState gstate)
        {
            throw new NotImplementedException();
        }

        public void SetRI(string ri) { }

        #endregion

        #region Special graphics state

        public void Save()
        {
            _state_stack.Push(_cs);
            _cs.dc_pos = 0;
        }

        public void Restore()
        {
            //Rewinds the stacks
            while (_cs.dc_pos > 0)
            {
                _dc.Pop();
                _cs.dc_pos--;
            }

            _cs = _state_stack.Pop();
            _new_pen = true;
            _clip = false;
        }

        /// <summary>
        /// Prepend matrix to CTM
        /// </summary>
        public void PrependCM(xMatrix xm)
        {
            Matrix m = new Matrix(xm.M11, xm.M12, xm.M21, xm.M22, xm.OffsetX, xm.OffsetY);
            _dc.PushTransform(new MatrixTransform(m));
            _cs.dc_pos++;
            _cs.CTM.Prepend(m);
            _new_pen = true;
        }

        #endregion

        #region Color
        //Debug drawing ignores color information.

        public void SetFillColor(double cyan, double magenta, double yellow, double black) { }
        public void SetFillColor(double red, double green, double blue) { }
        public void SetFillColor(double gray) { }
        public void SetFillColorSC(double[] color) { }
        public void SetFillColor(double[] color) { }
        public void SetStrokeColor(double cyan, double magenta, double yellow, double black) { }
        public void SetStrokeColor(double red, double green, double blue) { }
        public void SetStrokeColor(double gray) { }
        public void SetStrokeColorSC(double[] color) { }
        public void SetStrokeColor(double[] color) { }
        public void SetFillCS(IColorSpace cs) { }
        public void SetStrokeCS(IColorSpace cs) { }
        public void SetFillPattern(PdfShadingPattern pat) { }
        public void SetFillPattern(double[] color, CompiledPattern pat) { }
        public void SetFillPattern(double[] color, PdfTilingPattern pat) { }
        public void SetStrokePattern(PdfShadingPattern pat) { }
        public void SetStrokePattern(double[] color, CompiledPattern pat) { }
        public void SetStrokePattern(double[] color, PdfTilingPattern pat) { }

        #endregion

        #region Shading patterns

        public void Shade(PdfShading shading)
        {
            throw new NotImplementedException();
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
            var mt = new MatrixTransform(1d / img.Width, 0, 0, -1d / img.Height, 0, 1);

            // According to the specs the images are always drawn 1-1 to user 
            // coordinates. Not entierly sure what the specs mean by that.
            // What I'm doing is scaling the image down to 1x1 pixel, then
            // letting the CTM scale it back up to size. 
            _dc.PushTransform(mt);
            _dc.DrawRectangle(null, CreateDPen(img.Width), new Rect(0, 0, img.Width, img.Height));
            _dc.Pop();
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

        public void DrawForm(CompiledForm img)
        {
            Save();

            PrependCM(img.Matrix);
            _dc.PushClip(ToRectG(img.BBox));
            _cs.dc_pos++;
            //_state_stack.Clear();
            _executor.Execute(img.Commands, this);
            Restore();
        }

        #endregion

        #region Path construction

        /// <summary>
        /// Starts a path from the given point.
        /// </summary>
        public void MoveTo(double x, double y)
        {
#if UseGuidelineSet
            //Note, I've not checked if one should use + or -.
            //0.5 is suppose to be "current stroke width / 2"
            if (_gs == null) _gs = new GuidelineSet();
            _gs.GuidelinesX.Add(x+.5);
            _gs.GuidelinesY.Add(y+.5);
#endif
            if (_paths.Count != 0)
            {
                _closed_paths.Add(_path);
                _path = new PathFigure();
                _paths = _path.Segments;
            }
            _path.StartPoint = new Point(x, y);
        }

        /// <summary>
        /// Draws a line to the given point
        /// </summary>
        public void LineTo(double x, double y)
        {
#if UseGuidelineSet
            _gs.GuidelinesX.Add(x+.5);
            _gs.GuidelinesY.Add(y+.5);
#endif
            var ls = new LineSegment(new Point(x, y), true);
            ls.Freeze();
            _paths.Add(ls);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var bs = new BezierSegment(new Point(x1, y1), new Point(x2, y2), new Point(x3, y3), true);
            bs.Freeze();
            _paths.Add(bs);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x3, double y3)
        {
            var bs = new BezierSegment(new Point(x1, y1), new Point(x3, y3), new Point(x3, y3), true);
            bs.Freeze();
            _paths.Add(bs);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Draws a rectangle as a new figure/subpath.
        /// </summary>
        public void RectAt(double x, double y, double width, double height)
        {
            MoveTo(x, y);
            var ls = new LineSegment(new Point(x + width, y), true);
            ls.Freeze();
            _paths.Add(ls);
            ls = new LineSegment(new Point(x + width, y + height), true);
            ls.Freeze();
            _paths.Add(ls);
            ls = new LineSegment(new Point(x, y + height), true);
            ls.Freeze();
            _paths.Add(ls);
            _path.IsClosed = true;
            _closed_paths.Add(_path);
            _path = new PathFigure();
            _paths = _path.Segments;
        }

        public void ClosePath()
        {
            if (_path.IsClosed == true || _paths.Count == 0)
                return;
            _path.IsClosed = true;
            _closed_paths.Add(_path);
            _path = new PathFigure();
            _paths = _path.Segments;
        }

        public void DrawClip(xFillRule fr)
        {
            SetClip(fr);
            DrawPathNZ(false, false, false);
        }

        public void DrawPathNZ(bool close, bool stroke, bool fill)
        {
            if (close) _path.IsClosed = true;

            //Must freeze the geometry before drawing it.
            PathGeometry pg;
            if (_closed_paths.Count > 0)
            {
                if (_path.Segments.Count > 0)
                    _closed_paths.Add(_path);
                for (int c = 0; c < _closed_paths.Count; c++)
                {
                    _path = _closed_paths[c];
                    //_path.IsFilled = true;
                    _path.Freeze();
                }
                pg = new PathGeometry(_closed_paths);
                _closed_paths.Clear();
            }
            else
            {
                pg = new PathGeometry();
                //_path.IsFilled = true;
                _path.Freeze();
                pg.Figures.Add(_path);
            }

            pg.FillRule = FillRule.Nonzero;
            pg.Freeze();

#if UseGuidelineSet
            _dc.PushGuidelineSet(_gs);
            _cs.dc_pos++;
            _gs = null;
#endif

            var pen = (stroke) ? CreatePen() : null;
            _dc.DrawRectangle(null, CreateDPen(0.75), pg.GetRenderBounds(pen));

            if (_clip)
            {
                _dc.PushClip(pg);
                _cs.dc_pos++;
                _clip = false;
            }

            //Resets path
            _path = new PathFigure();
            _paths = _path.Segments;
        }

        public void DrawPathEO(bool close, bool stroke, bool fill)
        {
            if (close) _path.IsClosed = true;

            //Must freeze the geometry before drawing it.
            PathGeometry pg;
            if (_closed_paths.Count > 0)
            {
                if (_path.Segments.Count > 0)
                    _closed_paths.Add(_path);
                for (int c = 0; c < _closed_paths.Count; c++)
                {
                    _path = _closed_paths[c];
                    //_path.IsFilled = true;
                    _path.Freeze();
                }
                pg = new PathGeometry(_closed_paths);
                _closed_paths.Clear();
            }
            else
            {
                pg = new PathGeometry();
                //_path.IsFilled = true;
                _path.Freeze();
                pg.Figures.Add(_path);
            }

            pg.FillRule = FillRule.EvenOdd;
            pg.Freeze();

            var pen = (stroke) ? CreatePen() : null;
            _dc.DrawRectangle(null, CreateDPen(0.75), pg.GetRenderBounds(pen));

            if (_clip)
            {
                _dc.PushClip(pg);
                _cs.dc_pos++;
                _clip = false;
            }

            //Resets path
            _path = new PathFigure();
            _paths = _path.Segments;
        }

        private Pen CreatePen(double scale_line)
        {
            double w = _cs.line_width;

            //I'm not sure, but Adobe may ignore this parameter alltogether.
            if (_cs.stroke_adj)
            {
                double scale_factor = Math.Abs(_cs.CTM.M11);
                double w_scaled = _cs.line_width * scale_factor;
                if (w_scaled < 1)
                    w = 1 / scale_factor;
            }

            //This is by no means correct
            w *= scale_line;

            //Must adjust the dashstyle to WPF norms
            DashStyle dash_style;
            if (_cs.dash_offset == 0 && _cs.dash_array.Length == 0)
                dash_style = DashStyles.Solid;
            else
            {
                dash_style = new DashStyle();
                for (int c = 0; c < _cs.dash_array.Length; c++)
                    dash_style.Dashes.Add(_cs.dash_array[c] / w);
                dash_style.Offset = _cs.dash_offset;
                dash_style.Freeze();
            }

            return new Pen()
            {
                Thickness = w,
                LineJoin = _cs.line_join,
                StartLineCap = _cs.line_cap,
                EndLineCap = _cs.line_cap,
                Brush = new SolidColorBrush(_cs.stroke),
                MiterLimit = _cs.miter_limit,
                DashStyle = dash_style,
                DashCap = PenLineCap.Flat
            };
        }

        private Pen CreateDPen(double scale_factor_2)
        {
            //if (!_new_pen && _dbug_pen != null)
            //    return _dbug_pen;

            Vector vec = new Vector(1, 0);
            vec = Vector.Multiply(vec, _cs.CTM);
            double l = vec.Length;
            double scale_factor = _cs.CTM.M11 * vec.X / l + _cs.CTM.M12 * vec.Y / l;

            _dbug_pen = new Pen()
            {
                Thickness = 1 / scale_factor * scale_factor_2,
                Brush = Brushes.Black,
            };
            return _dbug_pen;
        }

        private Pen CreatePen()
        {
            if (!_new_pen) return _last_pen;

            double w = _cs.line_width;

            //I'm not sure, but Adobe may ignore this parameter alltogether.
            if (_cs.stroke_adj)
            {
                //As far as I can tell this differs from how Acrobat Reader
                //handles the situation. In cases where M11 is 0 Acrobat Reader
                //will set the line width to 1 and call it a day. 
                double scale_factor = Math.Abs(_cs.CTM.M11);
#if (!AutoAdjustLikeAdobe)
                if (scale_factor == 0)
                    scale_factor = Math.Abs(_cs.CTM.M12);
#endif

                //First we scale down the size to see how big it is "on screen"
                double w_scaled = _cs.line_width * scale_factor;

                //If the size is smaller than 1 we assume it's smaller than one pixel,
                //and according to the specs a stroke should not be less than one pixel.
                if (w_scaled < 1)
                {
                    //We therefor scale it up. 
                    //(The 0.75 seems to give better result)
                    w = 1 / scale_factor * .75;
                }

                //AFAICT WPF does sanity checking. So infinity, etc, will simply result
                //in the lines not being drawn.
            }

            //Must adjust the dashstyle to WPF norms
            DashStyle dash_style;
            if (_cs.dash_offset == 0 && _cs.dash_array.Length == 0)
                dash_style = DashStyles.Solid;
            else
            {
                dash_style = new DashStyle();
                for (int c = 0; c < _cs.dash_array.Length; c++)
                    dash_style.Dashes.Add(_cs.dash_array[c] / w);
                dash_style.Offset = _cs.dash_offset;
                dash_style.Freeze();
            }

            _last_pen = new Pen()
            {
                Thickness = w,
                LineJoin = _cs.line_join,
                StartLineCap = _cs.line_cap,
                EndLineCap = _cs.line_cap,
                Brush = new SolidColorBrush(_cs.stroke),
                MiterLimit = _cs.miter_limit,
                DashStyle = dash_style,
                DashCap = PenLineCap.Flat
            };
            return _last_pen;
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            _clip = true;
            _clip_rule = (FillRule)rule;
        }

        #endregion

        #region Text objects

        /// <summary>
        /// Sets the TM back to identity
        /// </summary>
        public void BeginText()
        {
            //_text_mode = true;
            _tm.Tm = Matrix.Identity;
            _tm.Tlm = Matrix.Identity;
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
            _cs.ts.Tc = tc;
        }

        /// <summary>
        /// Set the distance between words
        /// </summary>
        public void SetWordSpacing(double s)
        {
            _cs.ts.Tw = s;
        }

        public void SetFont(PdfFont font, double size)
        {
            if (_cs.ts.Tf != null)
                _cs.ts.Tf.Dismiss();

            _cs.ts.Tf = font.Realize<StreamGeometry>(this);
            _cs.ts.Tfs = size;
        }

        public void SetFont(cFont font, double size)
        {
            SetFont(font.MakeWrapper(), size);
        }

        public void SetFont(CompiledFont font, double size)
        {
            throw new NotImplementedException();
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
            _cs.ts.Th = th / 100;
        }

        /// <summary>
        /// Set text leading (distance between lines)
        /// </summary>
        public void SetTextLeading(double lead)
        {
            _cs.ts.Tl = lead;
        }

        public void SetTextRise(double tr)
        {
            _cs.ts.Tr = tr;
        }

        #endregion

        #region Text positioning

        public void SetTM(xMatrix m)
        {
            _tm.Tm = new Matrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);
            _tm.Tlm = _tm.Tm;
        }

        /// <summary>
        /// Translates the TML and sets it to TM
        /// </summary>
        public void TranslateTLM(double x, double y)
        {
            var m = Matrix.Identity;
            m.Translate(x, y);
            _tm.Tlm.Prepend(m);
            _tm.Tm = _tm.Tlm;
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
            _cs.ts.Tl = -y;
            TranslateTLM(x, y);
        }

        #endregion

        #region Text showing

        public void DrawString(PdfItem[] str_ar)
        {
            new TJ_BuildCMD(str_ar).Execute(this);
        }
        public void DrawString(BuildString str)
        {
            new Tj_BuildCMD(str).Execute(this);
        }

        public void DrawString(PdfString text, double aw, double ac)
        {
            DrawString(text.GetString());
            _cs.ts.Tw = aw;
            _cs.ts.Tc = ac;
        }

        public void DrawString(PdfString text, bool cr)
        {
            DrawString(text.GetString());
            if (cr)
                TranslateTLM();
        }

        public void DrawString(object[] text)
        {
            var v = (_cs.ts.Tf != null) ? _cs.ts.Tf.Vertical : false;
            for (int c = 0; c < text.Length; c++)
            {
                var o = text[c];
                if (o is PdfString)
                    DrawString(((PdfString)o).GetString());
                else if (o is double)
                {
                    if (v)
                        _tm.Tm.TranslatePrepend(0, (double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th);
                    else
                        _tm.Tm.TranslatePrepend((double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th, 0);
                }
                else
                {
                    throw new PdfNotSupportedException();
                }
            }
        }

        /// <summary>
        /// Draws a string
        /// </summary>
        void DrawString(string str)
        {
            //Implementation note:
            //The string can at this point be viewed as a series of
            //numbers. Each number can range from 0 to 512. The string
            //is most likely readble in the debugger, but this is just
            //a "coincidence".

            var font = _cs.ts.Tf;
            if (font == null)
            {
                //Todo: Log error "tried to render text without font".
                return;
            }

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th;

            //Scales the text by its font size. (The glyph is scaled
            //down to 1x1)
            Matrix scale = new Matrix(fsize, 0, 0, fsize * th, 0, _cs.ts.Tr);
            Matrix tm;

            //Creates the glyphs. Strings can be multiple characters
            //so just let the font figure out how many glyphs there 
            //are. (Note that "very long strings" are a rarity in PDF
            //files, and this renderer does not support incremental
            //updates in any case)
            var glyps = font.GetGlyphs(str);

            for (int c = 0; c < glyps.Length; c++)
            {
                //Gets a single glyph to draw
                var glyph = (rGlyphWPF) glyps[c];

                //Renders with sharing, and such.
#if SHARE_TEXT
                {
                    //Fetches the Tm struct and prepends the
                    //scale matrix. Note that the original
                    //struct is unchanged.
                    tm = _tm.Tm;
                    tm.Prepend(scale); //Scales text up to font size
                    tm.Prepend(glyph.Transform); //Scales text down to 1x1

                    //Pushes the text matrix onto the stack
                    _dc.PushTransform(new MatrixTransform(tm));

                    Pen pen = null;

                        //Problem is, PDF expects the line width to be
                        //in the CTM space. WPF will draw the lines
                        //with width scaled into text space.
                        //This needs to be undone.

                        //First invert the text metrix.
                        tm.Invert();

                        //Create a vector of know lenght.
                        Vector vec = new Vector(1, 0);

                        //Only want the rotation and only the original text metrixs
                        //can have rotation (the others only scales).
                        vec = Vector.Multiply(vec, _tm.Tm);
                        double l = vec.Length;

                        //By dividing the vector point on it's length I get the plain rotation.
                        //It's possible that Math.Abs can be removed, but I've not checked rotation
                        //in all quadrants so staying safe for now.
                        if (_cs.ts.Stroke)
                            //pen = CreatePen(Math.Abs(tm.M11 * vec.X / l) + Math.Abs(tm.M12 * vec.Y / l));
                            pen = CreatePen(tm.M11 * vec.X / l + tm.M12 * vec.Y / l);

                    var outlines = glyph.Outlines;
                    //Todo: set EO by default?
                    //if (outlines.FillRule != fill_rule)
                    //{
                    //    outlines = outlines.Clone();
                    //    outlines.FillRule = fill_rule;
                    //    outlines.Freeze();
                    //}

                    //Waiting to implement brush until it's time to look into
                    //patterns. Some cleverness is needed there.
                    if (_cs.ts.Stroke || _cs.ts.Fill || _cs.ts.Clip)
                    {
                        _dc.DrawRectangle(null, CreateDPen(Math.Abs(tm.M11 * vec.X / l) + Math.Abs(tm.M12 * vec.Y / l)), outlines.GetRenderBounds(pen));
                    }

                    //Pops the text matrix
                    _dc.Pop();

                    //Adds clip.
                    if (_cs.ts.Clip)
                    {
                        _dc.PushClip(outlines);
                        _cs.dc_pos++;
                    }
                }//*/

                //Render without sharing/other TM transforms (I.e. one get the rendering rectangle's full bounds). 
                //Does not compensate for the CTM transform, but a similar technique should be workable
                // Text scale 4.pdf demonstrates this best
#else
                {
                    //Fetches the Tm struct and prepends the
                    //scale matrix. Note that the original
                    //struct is unchanged.
                    tm = _tm.Tm;
                    tm.Prepend(scale); //Scales text up to font size
                    tm.Prepend(glyph.Transform); //Scales text down to 1x1


                    Pen pen = null;
                    tm.Invert();
                    Vector vec = new Vector(1, 0);
                    vec = Vector.Multiply(vec, _tm.Tm);
                    double l = vec.Length;
                    if (_cs.ts.Stroke)
                        //pen = CreatePen(Math.Abs(tm.M11 * vec.X / l) + Math.Abs(tm.M12 * vec.Y / l));
                        pen = CreatePen(tm.M11 * vec.X / l + tm.M12 * vec.Y / l);

                    var outlines = glyph.Outlines;
                    //Todo: set EO by default?
                    //if (outlines.FillRule != fill_rule)
                    //{
                    //    outlines = outlines.Clone();
                    //    outlines.FillRule = fill_rule;
                    //    outlines.Freeze();
                    //}

                    if (_cs.ts.Stroke || _cs.ts.Fill || _cs.ts.Clip)
                    {
                        var render_bounds = outlines.GetRenderBounds(pen);
                        tm.Invert();
                        render_bounds.Transform(tm);
                        _dc.DrawRectangle(null, CreateDPen(1), render_bounds);
                        //Naturaly, also the CTM needs to be "undone", or never
                        //done in the first place.
                    }

                    if (_cs.ts.Clip)
                    {
                        _dc.PushClip(outlines);
                        _cs.dc_pos++;
                    }
                }
#endif

                double ax = glyph.MoveDist.X * fsize + _cs.ts.Tc;
                if (glyph.IsSpace) ax += _cs.ts.Tw;
                _tm.Tm.TranslatePrepend(ax * th, glyph.MoveDist.Y);
            }
        }

        #endregion

        #region Type3 fonts

        /// <summary>
        /// Sets a colored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        public void SetT3Glyph(double wx, double wy) { }

        /// <summary>
        /// Sets uncolored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        /// <param name="llx">Lower left X</param>
        /// <param name="lly">Lower left Y</param>
        /// <param name="urx">Upper right X</param>
        /// <param name="ury">Upper right Y</param>
        public void SetT3Glyph(double wx, double wy, double llx, double lly, double urx, double ury) { }

        #endregion

        #region Compatibility

        public void BeginCompatibility() { }
        public void EndCompatibility() { }

        #endregion

        #region State

        /// <summary>
        /// See 8.4 in the specs. Note that "clipping path" is tracked through the
        /// dc_pos parameter (CTM is too, but is also tracked here for scaling reasons)
        /// </summary>
        struct State
        {
            /// <summary>
            /// How deep this state has incrementet the dc stack.
            /// </summary>
            public int dc_pos;

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
            /// </remarks>
            public Matrix CTM;

            /// <summary>
            /// Colorspace used for strokes.
            /// </summary>
            public PdfColorSpace strokeCS;

            /// <summary>
            /// Colorspace used for fills
            /// </summary>
            public PdfColorSpace fillCS;

            /// <summary>
            /// Color used for non-stroking
            /// </summary>
            public Color fill;

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
            public double line_width;

            /// <summary>
            /// How lines are capped off
            /// </summary>
            public PenLineCap line_cap;

            /// <summary>
            /// How lines are joined together
            /// </summary>
            public PenLineJoin line_join;

            /// <summary>
            /// See: http://blogs.msdn.com/b/mswanson/archive/2006/03/23/559698.aspx
            /// </summary>
            public double miter_limit;

            /// <summary>
            /// The dash style
            /// </summary>

            public double[] dash_array;
            public double dash_offset;

            /// <summary>
            /// Stroke adjustment
            /// </summary>
            /// <remarks>
            /// Perhaps make it so that scale_lines_to_output
            /// locks this at true, and drop the whole "not setting CTM"
            /// </remarks>
            public bool stroke_adj;

            public void Reset()
            {
                dc_pos = 0;
                CTM = Matrix.Identity;
                strokeCS = DeviceGray.Instance;
                fillCS = DeviceGray.Instance;
                line_width = 1;
                line_cap = PenLineCap.Flat;
                line_join = PenLineJoin.Miter;
                miter_limit = 5;
                dash_array = new double[0];
                dash_offset = 0;
                stroke_adj = false;
                fill = Colors.Black;
                stroke = Colors.Black;

                //Text
                ts.Tf = null;
                ts.Tfs = 1;
                ts.Th = 1;
                ts.Tr = 0;
                ts.Tc = 0;
                ts.Tw = 0;
                ts.Fill = true;
                ts.Stroke = false;
                ts.Clip = false;
            }
        }

        /// <summary>
        /// See 9.3.1
        /// </summary>
        struct TextState
        {
            /// <summary>
            /// Character spacing
            /// </summary>
            public double Tc;

            /// <summary>
            /// Word spacing (Not implemented)
            /// </summary>
            public double Tw;

            /// <summary>
            /// Horizontal scaling
            /// </summary>
            public double Th;

            /// <summary>
            /// Font
            /// </summary>
            public rFont Tf;

            /// <summary>
            /// Font size
            /// </summary>
            public double Tfs;

            /// <summary>
            /// Text Rise
            /// </summary>
            public double Tr;
            public double Tl;
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
        }

        #endregion

        #region IFontFactory

        //RenderEngine IFontFactory.Engine
        //{ get { return RenderEngine.DrawDC; } }

        FontCache IFontFactory.FontCache { get { return _font_cache; } }

        IGlyphCache IFontFactory.CreateGlyphCache() => new NullGlyphCache();

        rFont IFontFactory.CreateMemTTFont(PdfLib.Read.TrueType.TableDirectory td, bool symbolic, ushort[] cid_to_gid)
        {
            return new MemTTFont<StreamGeometry>(td, this, symbolic, cid_to_gid);
        }

        rFont IFontFactory.CreateBuiltInFont(PdfType1Font font, double[] widths, AdobeFontMetrixs afm, bool substituted)
        {
            //Not made a desition on this yet. There will be no AFM on "random" fonts.
            Debug.Assert(afm != null && widths != null);

            return new WPFFont(font.BaseFont,
                               afm.SubstituteFont,
                               widths,
                               afm, this);
        }

        rFont IFontFactory.CreateBuiltInFont(PdfType0Font font, AdobeFontMetrixs afm, bool substituted)
        {
            return new CIDWPFFont(font.BaseFont, afm.SubstituteFont, afm, font.DescendantFonts, font.Encoding.WMode, this);
        }

        /// <summary>
        /// Attach a Matrix transform to the path
        /// </summary>
        void IGlyphFactory<StreamGeometry>.SetTransform(StreamGeometry path, xMatrix transform)
        {
            ((StreamGeometry)path).Transform = new MatrixTransform(Util.XtoWPF.ToMatrix(transform));
        }

        /// <summary>
        /// Creates a path without anything in it
        /// </summary>
        StreamGeometry IGlyphFactory<StreamGeometry>.CreateEmptyPath()
        {
            return new StreamGeometry();
        }

        rGlyph IGlyphFactory<StreamGeometry>.CreateGlyph(StreamGeometry[] outline, xPoint advance, xPoint origin, xMatrix mt, bool freeze, bool is_space_char)
        {
            return new rGlyphWPF(new GeometryGroup(), Util.XtoWPF.ToPoint(advance), Util.XtoWPF.ToPoint(origin), Util.XtoWPF.ToMatrix(mt), is_space_char);
        }

        IGlyph<StreamGeometry> IGlyphFactory<StreamGeometry>.GlyphRenderer()
        {
            return new DrawDC.GlyphCreator();
        }

        #endregion

        #region Helper functions

        private RectangleGeometry ToRectG(xRect r)
        {
            return new RectangleGeometry(Util.XtoWPF.ToRect(r));
        }

        #endregion
    }
#endif
}
