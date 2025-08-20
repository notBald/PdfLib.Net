#region Imports and defines

//AutoAdjustLikeAdobe with UseGuidelineSet is fairly close to how Adobe renders lines,
//but there are still some difference to when this auto adjust kicks in.
#define AutoAdjustLikeAdobe

//Use WPF's built in glyph builder when rendering built in fonts. Gets scaling incorrect
//in one known case (X too small.pdf), likely because of differences in Window's symbol
//font and PDF's cff symbol font.
//#define USE_WPF_FONT

//Scales font drawings up to the device render size. Looks nicer when zommed in. However,
//this code is experimental and needs spit, polish and testing.
//#define ScaleT3FontDrawing

//Experimental. Assumes all linwidths are "some constant" (line width is calced when
//calling "create pen" so I just fudged it). It's not impl. for DrawPathEO or any other 
//vector commands other than MoveTo and LineTo (See MoveTo)
//#define UseGuidelineSet

//This code works, it seems, but does not improve things much on the truck test, and
//that's the only PDF that I got which benifits from this (and even then, only when 
//zoomed). In theory this truck test file could be fixed by drawing images with AA disabled,
//but using a DV with AA disabled is useless, as AA will be applied to the DV's edges 
//anyhow. 
//#define UseGuidelinesOnImages

//Quick way of getting alpha, but ignores blend modes/knockout groups and such stuff.
//
//Page 11 (from 1) of "JRC_africa_soil_atlas_part1" is a good file to use when implementing alpha
//It has alpha that both works with this hack, and don't.
// * Notice, drop shadows on images and the "What is PH" arrow. (LuminosityMask)
// * Notice the lac of "rocks" in the ground under the rat
// * Notice how the lines on the mushroom aren't as "thick" as in Adobe. 
// * If the colors on the images isn't as colorful as in Adobe, that's because the image is rendered. 
//   on the "fastimagepath." If you change the image over to the slow image path and use the CLUT
//   table for color conversion, notice how the biggest image is a bit more grainy than it should be.
//   I belive this can be fixed by adding more samples to the sample table. 
#define ALPHA_HACK

//There's no functional difference, it's just sligtly nicer to debug with strings
//over arrays of bytes.
//#define UseString //<-- does not work anymore :(

//Just for testing. Does not play nice with clipping, so don't use.
//#define ScaleIMGsToDevicePixels

//Strokes lines using the fill command when only a single line is drawn.
#define STROKE_FILLED_LINES

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
using PdfLib.Util;
using PdfLib.Compile;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Render.Commands;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Function;
using PdfLib.Compose.Text;
using PdfLib.Compose;

#endregion

namespace PdfLib.Render.WPF
{
    /// <summary>
    /// Renders a PDF commands into a drawing visual
    /// </summary>
    /// <remarks>
    /// This class has no support for transparency/blending beyond what's in PDF 1.3.
    /// The problem is WPF's lack of a back buffer and blend modes. One potential work
    /// around is to rasterise the drawing at opertune moments, then manually blend
    /// against the raster. Also in some cases it may be fersible to use a drawing 
    /// content's PushOpacity method to emulate soft masking.
    /// 
    /// Idea
    ///  - Create a RGBA32 back buffer. (Just a byte[])
    ///  - Create a Writable Bitmap, fill it transparent (to know where a shape 
    ///    has been drawn)
    ///  - Keep track of the clip/paint region through whatever means.
    ///    (Bounds, pen size, CTM, Clip, Miter)
    ///  - When painting operations overlap, check if blending is needed.
    ///     -> Blending is needed, copy front to back, paint, blend at next overlap
    ///  - AA will work poorly though.
    /// 
    /// WPF also supports shaders, but they don't let you blend against the background.
    /// (One can in theory also use BitmapEffect inside the DrawingContent, but that's 
    /// depricated)
    /// 
    /// This idea will have to be implemented through a new renderer. There's no
    /// simple way to retrofit it on this renderer. However one may as well use GDI
    /// then.
    /// 
    /// Todo: Freze all geo transforms
    ///       Test drawing with CS pattern, but no pattern set (should draw nothing)
    /// </remarks>
    internal sealed class DrawDC : IDraw, IFontFactory<StreamGeometry>
    {
        #region State

#if UseGuidelineSet
        GuidelineSet _gs;
#endif
        /// <summary>
        /// Path used for dawing shapes.
        /// </summary>
        List<PathFigure> _closed_paths;
        PathFigure _path;
        PathSegmentCollection _paths;
        Point _current_point; //<-- Todo: Detect invalid current points (i.e. if not set before use)

        /// <summary>
        /// Current state
        /// </summary>
        private State _cs = new State();
        private RenderState _rs = new RenderState();
        Stack<State> _state_stack = new Stack<State>(32);

        /// <summary>
        /// Does not track graphic state.
        /// </summary>
        /// <remarks>Isn't this done by the compiler anyway?</remarks>
        public GS GraphicState { get { return GS.Unknown; } }

        ClipMask _clip_mask = null;

        /// <summary>
        /// Cache over fonts and glyphs. 
        /// </summary>
        private FontCache _font_cache = new FontCache();

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

        public void Init(PdfRectangle MediaBox, double output_width, double output_height,
                         bool respect_aspect, bool scale_lines_to_output, DrawingContext dc, int rotate)
        {
#if !AutoAdjustLikeAdobe
            //Forced true to avoid potential issues. 
            //I think this function still works, but it needs testing. Run through the test
            //files for Forms and Patterns to see if they still render correctly.
            scale_lines_to_output = true;
#endif
            // Calcs the size of the user space. Negative size is intended.
            double user_space_width = MediaBox.URx - MediaBox.LLx;
            double user_space_heigth = MediaBox.LLy - MediaBox.URy;

            // Corrects for aspect ratio.
            xSize output = PdfRender.CalcDimensions(MediaBox, output_width, output_height, respect_aspect, 0);

            //Sets up mapping from the defualt PDF user space to WPF device space.
            //
            //   PDF: 0.1---1.1    WPF: 0.0---1.0   Scale matrix: Sx--0   WPF matrix: M11--M12
            //         |     |           |     |                  |   |                |    |
            //        0.0---1.0         0.1---1.1                 0--Sy               M21--M22
            double device_width = output.Width;
            double device_height = output.Height;
            double scale_x = device_width / user_space_width;
            double scale_y = device_height / user_space_heigth;

            //Note: Device space is typically the monitor.
            Matrix from_userspace_to_device_coords = new Matrix(scale_x, 0, 0,
                                                                scale_y,
                                                                (scale_x < 0) ? output.Width : 0,
                                                                (scale_y < 0) ? output.Height : 0);
            //Invertible matrix is required by this implementation.
            if (!from_userspace_to_device_coords.HasInverse)
                throw new NotSupportedException("Check if any dimension is zero in either Mediabox or output width/height");

            //Translates so that the media box starts at 0,0
            from_userspace_to_device_coords.TranslatePrepend(-MediaBox.LLx, -MediaBox.LLy);

            //Resets all state information
            _cs.Reset();
            _rs.SetUp();

            //It's important that the "rotation" matrix don't end up on the CTM, as
            //that will affect "automatic stroke width adjustments"
#if !AutoAdjustLikeAdobe
            if (scale_lines_to_output)
#endif
            {
                _cs.CTM = from_userspace_to_device_coords;
            }

            //Rotates the page
            if (rotate != 0)
            {
                from_userspace_to_device_coords.Rotate(rotate);
                //Assumes angular rotation.
                if (rotate == 90)
                    from_userspace_to_device_coords.Translate(output.Height, 0);
                else if (rotate == 180)
                    from_userspace_to_device_coords.Translate(output.Width, output.Height);
                else if (rotate == 270)
                    from_userspace_to_device_coords.Translate(0, output.Width);
            }//*/

#if !AutoAdjustLikeAdobe
            if (scale_lines_to_output)
#endif
            {
                _rs.from_device_space = from_userspace_to_device_coords;
            }

            _rs.dc = dc;
            var mt = new MatrixTransform(from_userspace_to_device_coords);
            mt.Freeze();
            _rs.dc.PushTransform(mt);

            //Sets up the state
            _cs.dc_pos = 1;

            _rs.from_device_space.Invert();
            _rs.new_pen = true;
            _closed_paths = new List<PathFigure>();
            _path = new PathFigure();
            _paths = _path.Segments;

            //Fills the whole page white
            var page_rect = XtoWPF.ToRect(MediaBox);
            _rs.dc.DrawRectangle(Brushes.White, null, page_rect);

            //Prevents drawing outside the page (can show up if one
            //place the dv straight onto the screen)
            //The CTM clip must also be updated, to prevent NaN when
            //using the sh op on a blank new page.
            PushClip(new RectangleGeometry(page_rect), page_rect);
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
        }

        /// <summary>
        /// Resets the path objects
        /// </summary>
        internal void ClearPath()
        {
            _closed_paths.Clear();
            _path = new PathFigure();
            _paths = _path.Segments;
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
            _cs.line_cap = PenLineCap.Flat;
            _cs.line_join = PenLineJoin.Miter;
            _cs.miter_limit = 5;
            _cs.dash_array = new double[0];
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
            _clip_mask = null;
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
                _cs.line_width = value;
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
            PenLineJoin plj;
            if (style == xLineJoin.Miter)
                plj = PenLineJoin.Miter;
            else if (style == xLineJoin.Round)
                plj = PenLineJoin.Round;
            else plj = PenLineJoin.Bevel;

            _cs.line_join = plj;
            _rs.new_pen = true;
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
            else _cs.miter_limit = limit / 2;
            _rs.new_pen = true;
        }

        /// <remarks>
        /// WPF's stroke dash array depend to the tickness of the current
        /// stroke width. Must thererefor wait to create the WPF object 
        /// until the line width is known. 
        /// </remarks>
        public void SetStrokeDashAr(xDashStyle ds)
        {
            _cs.dash_array = ds.Dashes;
            _cs.dash_offset = ds.Phase;
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
                _cs.transfer = new FCalcualte[tr.Length];
                for (int c = 0; c < tr.Length; c++)
                    _cs.transfer[c] = tr[c].Init();
            }
            else if (gs.TR != null)
            {
                //Todo: Can be name or a single function
                var tr = gs.TR.ToArray();
                if (tr.Length != 4 && tr.Length != 1)
                    throw new PdfNotSupportedException();
                _cs.transfer = new FCalcualte[tr.Length];
                for (int c = 0; c < tr.Length; c++)
                    _cs.transfer[c] = tr[c].Init();
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

            ////Some of my test files require alpha to look correct.
#if ALPHA_HACK
            //var smask = gs.SMask;
            //if (smask != null)
            //{
            //    //if (smask == PdfType.SoftMask)
            //    //{

            //    //    //var imb = new ImageBrush(smask);
            //    //    //_rs.dc.PushOpacityMask
            //    //}
            //}

            var ca = gs.ca;
            if (ca != null)
            {
                if (_cs.ca != 1)
                    _rs.dc.Pop();
                _cs.ca = ca.Value; 
                if (_cs.ca != 1)
                    _rs.dc.PushOpacity(ca.Value);
            }
#endif
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
#if ALPHA_HACK
            if (_cs.ca != 1)
            {
                _rs.dc.Pop();
                _state_stack.Push(_cs);
                _cs.dc_pos = 0;
                _rs.dc.PushOpacity(_cs.ca);
            }
            else
            {
#endif
                _state_stack.Push(_cs);
                _cs.dc_pos = 0;
#if ALPHA_HACK
            }
#endif
        }

        /// <summary>
        /// Restores the previous state
        /// </summary>
        public void Restore()
        {
            //Refuses to pop beyond the state stack
            if (_state_stack.Count == 0)
                return;

#if ALPHA_HACK
            if (_cs.ca != 1)
                _rs.dc.Pop();
#endif

            //Rewinds the stacks
            while (_cs.dc_pos > 0)
            {
                _rs.dc.Pop();
                _cs.dc_pos--;
            }

            _cs = _state_stack.Pop();
            _rs.new_pen = true;
            _rs.clip = false;

#if ALPHA_HACK
            if (_cs.ca != 1)
                _rs.dc.PushOpacity(_cs.ca);
#endif
        }

        /// <summary>
        /// Prepend matrix to CTM
        /// </summary>
        public void PrependCM(xMatrix xm)
        {
            Matrix m = new Matrix(xm.M11, xm.M12, xm.M21, xm.M22, xm.OffsetX, xm.OffsetY);
            var mt = new MatrixTransform(m);
            mt.Freeze();
            _rs.dc.PushTransform(mt);
            _cs.dc_pos++;
            _cs.CTM.Prepend(m);
            _rs.new_pen = true;
        }

        #endregion

        #region Color

        public void SetFillColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.fillCS = DeviceCMYK.Instance;
            _cs.fill = new SolidColorBrush(
                    Util.XtoWPF.ToColor(
                        _cs.fillCS.Converter.MakeColor(
                            new double[] { cyan, magenta, yellow, black }
                        )
                    )
                );
            _cs.fill.Freeze();
        }
        public void SetFillColor(double red, double green, double blue)
        {
            _cs.fillCS = DeviceRGB.Instance;
            _cs.fill = new SolidColorBrush(Color.FromRgb((byte)(red * 255), (byte)(green * 255), (byte)(blue * 255)));
            _cs.fill.Freeze();
        }
        public void SetFillColor(double gray)
        {
            _cs.fillCS = DeviceGray.Instance;
            byte bgray = (byte)(gray * 255);
            _cs.fill = new SolidColorBrush(Color.FromRgb(bgray, bgray, bgray));
            _cs.fill.Freeze();
        }
        public void SetFillColorSC(double[] color)
        {
            _cs.fill = new SolidColorBrush(XtoWPF.ToColor(_cs.fillCS.Converter.MakeColor(color)));
            _cs.fill.Freeze();
        }
        public void SetFillColor(double[] color)
        {
            _cs.fill = new SolidColorBrush(XtoWPF.ToColor(_cs.fillCS.Converter.MakeColor(color)));
            _cs.fill.Freeze();
        }

        public void SetStrokeColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.strokeCS = DeviceCMYK.Instance;
            _cs.stroke = Util.XtoWPF.ToColor(
                            _cs.strokeCS.Converter.MakeColor(
                                new double[] { cyan, magenta, yellow, black }
                            )
                        );
        }
        public void SetStrokeColor(double red, double green, double blue)
        {
            _cs.strokeCS = DeviceRGB.Instance;
            _cs.stroke = Color.FromRgb((byte)(red * 255), (byte)(green * 255), (byte)(blue * 255));
        }
        public void SetStrokeColor(double gray)
        {
            _cs.strokeCS = DeviceGray.Instance;
            byte bgray = (byte)(gray * 255);
            _cs.stroke = Color.FromRgb(bgray, bgray, bgray);
        }
        public void SetStrokeColorSC(double[] color)
        {
            _cs.stroke = XtoWPF.ToColor(_cs.strokeCS.Converter.MakeColor(color));
        }
        public void SetStrokeColor(double[] color)
        {
            _cs.stroke = XtoWPF.ToColor(_cs.strokeCS.Converter.MakeColor(color));
        }

        public void SetFillCS(IColorSpace cs)
        {
            if (cs is PatternCS)
                _cs.fillCS = new CSPattern(((PatternCS)cs).UnderCS);
            else
            {
                _cs.fillCS = cs;
                _cs.fill = new SolidColorBrush(XtoWPF.ToColor(cs.DefaultColor));
                _cs.fill.Freeze();
            }
        }

        public void SetStrokeCS(IColorSpace cs)
        {
            if (cs is PatternCS)
                _cs.strokeCS = new CSPattern(((PatternCS)cs).UnderCS);
            else
            {
                _cs.strokeCS = cs;
                _cs.stroke = XtoWPF.ToColor(cs.DefaultColor);
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
                    _cs.fill = new SolidColorBrush(XtoWPF.ToColor(csp.CS.Converter.MakeColor(color)));
                    _cs.fill.Freeze();
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
                _cs.stroke = XtoWPF.ToColor(csp.CS.Converter.MakeColor(color));
        }

        #endregion

        #region Shading patterns

        public void Shade(PdfShading shading)
        {
            //The clip path will need to be transformed. Whenever here or
            //while tracking has not been decided.
            // Idea:
            //  Transform clip down to page level
            //  Intersect with current clip
            //  In this function transform current clip by inverse CTM
            var iCTM = _cs.CTM;
            iCTM.Invert();
            var clip_path = _cs.Current_clip_path;

            //My shaders can't handle empty paths. Though nothing is drawn
            //so it shouldn't be a problem. 
            if (clip_path.IsEmpty)
                return;

            clip_path.Transform(iCTM);
            //_rs.dc.DrawRectangle(Brushes.Purple, null, clip_path);


            //According to documentation no the .net it's faster to split
            //rendering up into multiple drawing visuals, even if in this
            //instance one could render straight onto the dv.
            MyDrawingVisual dv = new MyDrawingVisual();

            using (var dc = dv.RenderOpen())
            {
                if (shading is PdfAxialShading)
                {
                    var ax = (PdfAxialShading)shading;

                    //The AA compensation is potentalliy buggy, so don't
                    //use if not needed.
                    dv.EdgeMode = (shading.AntiAlias) ? EdgeMode.Unspecified : EdgeMode.Aliased;
                    Vector px = (dv.EdgeMode == EdgeMode.Unspecified) ? px = iCTM.Transform(new Vector(1, 1)) : new Vector();

                    rWPFPattern.RenderShade(ax, dc, clip_path, px);

                    //Draws a line 5 pixels long for testing
                    //_rs.dc.DrawRectangle(Brushes.Red, null, new Rect(new Point(0.5, 0.5), new Point(0.5 + 5 * px.X, 0.5 + px.Y)));
                }
                else
                {
                    //Radial and Sampler patterns don't support AA.
                    dv.EdgeMode = EdgeMode.Aliased;
                    rWPFPattern.RenderShade(shading, dc, clip_path);
                }
            }

            dv.Clip = new RectangleGeometry(clip_path);
            dv.Clip.Freeze();

            var vb = new VisualBrush(dv);
            _rs.dc.DrawRectangle(vb, null, clip_path);
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
            BitmapSource bs = rDCImage.DecodeImage(img);
            if (bs == null)
            {
                //ToDo: Report
                return;
            }

#if ScaleIMGsToDevicePixels
            //Quick and dirty code. Just for testing
            var size = _cs.CTM.Transform(new Point(1,1));
            size.X = (int)(size.X + 1);
            size.Y = (int)(size.Y);
            var iCTM = _cs.CTM;
            iCTM.Invert();
            size = iCTM.Transform(size);
            var mt = new MatrixTransform((size.X) / bs.PixelWidth, 0, 0, -size.Y / bs.PixelHeight, 0, size.Y);
            //Keep in mind that since the clip isn't adjusted, this will result in a poorer result
            //in the common case.
#else
            // Note 1: that I flip the y axis (-1d). Otherwise the image gets drawn upside
            // down.
            //
            // Note 2: This is a bit wrong. Should use the width/height set in the
            // metadata of the image. (PDF image) Though I doubt it matters.
            //
            // Note 3: OffsetY is "1" to move the image up by its full height.
            var mt = new MatrixTransform(1d / bs.PixelWidth, 0, 0, -1d / bs.PixelHeight, 0, 1);
#endif
            mt.Freeze();

#if UseGuidelinesOnImages
            var gls = new GuidelineSet(new double[] { 0, 1 },
                                       new double[] { 0, 1 });
            _rs.dc.PushGuidelineSet(gls);
#endif

            // According to the specs the images are always drawn 1-1 to user 
            // coordinates. Not sure what the specs mean by that as an image's
            // size is decided by the CTM.
            //
            // What I'm doing is scaling the image down to 1x1 pixels, then
            // letting the CTM scale it back up to size. 
            _rs.dc.PushTransform(mt);

            //Note, this method only handles solid brushes. Non-solid brushes must be
            //handeled manualy
            if (img.ImageMask)
            {
                ImageBrush opacity_mask = new ImageBrush();
                opacity_mask.ImageSource = bs;
                _rs.dc.PushOpacityMask(opacity_mask);

                if (_cs.fillCS is CSPattern)
                {
                    var CTM = _cs.CTM;
                    try
                    {
                        _cs.CTM.Prepend(mt.Matrix);
                        FillPattern(new RectangleGeometry(new Rect(0, 0, bs.PixelWidth, bs.PixelHeight)), FillRule.Nonzero);
                    }
                    finally
                    {
                        _cs.CTM = CTM;
                    }
                }
                else
                {
                    _rs.dc.DrawRectangle(_cs.fill, null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                }

                _rs.dc.Pop(); //Pops opacity mask
                _rs.dc.Pop(); //Pops mt
#if UseGuidelinesOnImages
                _rs.dc.Pop(); //Pops guidelines
#endif
                return;
            }
            else
            {
                //SMask. Decode into alpha format
                var smask = img.SMask;
                BitmapSource mask_img = null;
                if (smask != null)
                    mask_img = rDCImage.DecodeSMask(smask);
                else
                {
                    var mask = img.Mask;
                    if (mask is PdfImage)
                        mask_img = rDCImage.DecodeImage(((PdfImage)mask));
                }
                if (mask_img != null)
                {
                    if (_cs.transfer != null)
                        bs = rDCImage.Transfer(bs, _cs.transfer);

                    
                    ImageBrush opacity_mask = new ImageBrush();
                    if (mask_img.PixelWidth < bs.PixelWidth && mask_img.PixelHeight < bs.PixelHeight)
                    {
                        //Bilinear of Fant scaled alpha-masks look ugly, so scale
                        //it up to image size with nearest neighbor
                        opacity_mask.ImageSource = Img.IMGTools.ChangeImageSize(mask_img,
                                                        BitmapScalingMode.NearestNeighbor,
                                                        bs.PixelWidth, bs.PixelHeight);
                    }
                    else
                        opacity_mask.ImageSource = mask_img;

                    //Draws masked image. WPF will scale the image and alpha 
                    //(to output size) using the default method (Bilinear on Net 4.0)
                    _rs.dc.PushOpacityMask(opacity_mask);
                    _rs.dc.DrawImage(bs, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                    _rs.dc.Pop(); // Pops opacity mask
                }
                else
                {
                    if (_cs.transfer != null)
                    {
                        if (img.Mask == null)
                            bs = rDCImage.Transfer(bs, _cs.transfer);
                        else
                            bs = rDCImage.Transfer32(bs, _cs.transfer);
                    }
                    _rs.dc.DrawImage(bs, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                    
                    //var encoder = new JpegBitmapEncoder();
                    //encoder.Frames.Add(BitmapFrame.Create(bs));
                    //using (var file = File.OpenWrite(@"c:\temp\test.jpg"))
                    //    encoder.Save(file);
                }
                _rs.dc.Pop(); // Pops mt
#if UseGuidelinesOnImages
                _rs.dc.Pop(); //Pops guidelines
#endif
            }
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
            var bounds = Util.XtoWPF.ToRect(form.BBox);
            PushClip(new RectangleGeometry(bounds), bounds);

            //Renders the form. No try/catch is needed.
            ((IDraw) this).Executor.Execute(form.Commands, this);

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
#if UseGuidelineSet
            //Note, I've not checked if one should use + or -.
            //0.5 is suppose to be "current stroke width / 2"
            if (_gs == null) _gs = new GuidelineSet();
            _gs.GuidelinesX.Add(x+.5);
            _gs.GuidelinesY.Add(y+.5);
#endif
            if (_paths.Count != 0)
            {
                _path.Freeze();
                _closed_paths.Add(_path);
                _path = new PathFigure();
                _paths = _path.Segments;
            }
            _current_point = new Point(x, y);
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
            _current_point = new Point(x, y);
            var ls = new LineSegment(new Point(x, y), true);
            ls.Freeze();
            _paths.Add(ls);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            _current_point = new Point(x3, y3);
            var bs = new BezierSegment(new Point(x1, y1), new Point(x2, y2), new Point(x3, y3), true);
            bs.Freeze();
            _paths.Add(bs);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x3, double y3)
        {
            _current_point = new Point(x3, y3);
            var bs = new BezierSegment(new Point(x1, y1), new Point(x3, y3), new Point(x3, y3), true);
            bs.Freeze();
            _paths.Add(bs);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            var bs = new BezierSegment(_current_point, new Point(x2, y2), new Point(x3, y3), true);
            bs.Freeze();
            _paths.Add(bs);
            _current_point = new Point(x3, y3);
        }

        /// <summary>
        /// Draws a rectangle as a new figure/subpath.
        /// </summary>
        public void RectAt(double x, double y, double width, double height)
        {
            //After a path is closed the current point is moved back to
            //the start of the path. 
            _current_point = new Point(x, y);

            MoveTo(x, y);
            var ls = new LineSegment(new Point(x + width, y), true);
            _paths.Add(ls);
            ls = new LineSegment(new Point(x + width, y + height), true);
            _paths.Add(ls);
            ls = new LineSegment(new Point(x, y + height), true);
            _paths.Add(ls);
            _path.IsClosed = true;
            _path.Freeze();
            _closed_paths.Add(_path);
            _path = new PathFigure();
            _paths = _path.Segments;
            _path.StartPoint = _current_point;
        }

        public void ClosePath()
        {
            if (_path.IsClosed == true || _paths.Count == 0)
                return;
            _current_point = _path.StartPoint;
            _path.IsClosed = true;
            _path.Freeze();
            _closed_paths.Add(_path);
            _path = new PathFigure();
            _paths = _path.Segments;

            //Moves current point ot the start of the
            //path. 
            _path.StartPoint = _current_point;
        }

        public void DrawClip(xFillRule fr)
        {
            SetClip(fr);
            DrawPath(_rs.clip_rule, false, false, false);
        }

        public void DrawPathNZ(bool close, bool stroke, bool fill)
        {
            DrawPath(FillRule.Nonzero, close, stroke, fill);
        }

        //In retrospect, it's pointless to have sepperate DrawPathEO/NZ
        public void DrawPathEO(bool close, bool stroke, bool fill)
        {
            DrawPath(FillRule.EvenOdd, close, stroke, fill);
        }

        private void DrawPath(FillRule fr, bool close, bool stroke, bool fill)
        {
            if (close) _path.IsClosed = true;

            PathGeometry pg;
            if (_closed_paths.Count > 0)
            {
                if (_path.Segments.Count > 0)
                {
                    _path.Freeze();
                    _closed_paths.Add(_path);
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

            pg.FillRule = fr;
            pg.Freeze();

#if UseGuidelineSet
            _rs.dc.PushGuidelineSet(_gs);
            _cs.dc_pos++;
            _gs = null;
#endif
            //Only one pattern can be rendered at the time, so splitting up
            //the draws.
            if (fill)
            {
#if STROKE_FILLED_LINES
                if (pg.Figures.Count == 1)
                {
                    //The figure "MoveTo" "LineTo" "Fill" gives a line in Adobe. We check for such a figure, and draw
                    //said line. The Fill will also be drawn afterwards, but that will just be empty. 
                    var fig = pg.Figures[0];
                    if (fig.Segments.Count == 1)
                    {
                        var seg = fig.Segments[0];
                        if (seg is LineSegment)
                        {
                            var hold = _cs.line_width;
                            var a_hold = _cs.stroke_adj;
                            _cs.stroke_adj = false;
                            var ctm = _cs.CTM;
                            //ctm.Append(_rs.from_device_space);
                            _cs.line_width = .2 / Math.Sqrt(Math.Abs(ctm.M11 * ctm.M22 - ctm.M12 * ctm.M21));
                            if (_cs.fillCS is CSPattern)
                            {
                                //var f_hold = _cs.strokeCS;
                                //_cs.strokeCS = _cs.fillCS;
                                //StrokePattern(pg, false);
                                //_cs.strokeCS = f_hold;

                                //Adobe simply renders a black line
                                _rs.dc.DrawGeometry(null, CreateNewPen(Brushes.Black), pg);
                            }
                            else
                            {
                                _rs.dc.DrawGeometry(null, CreateNewPen(_cs.fill), pg);                            
                            }
                            _cs.line_width = hold;
                            _cs.stroke_adj = a_hold;
                        }
                    }
                }
#endif

                if (_cs.fillCS is CSPattern)
                    FillPattern(pg, fr);
                else
                    _rs.dc.DrawGeometry(_cs.fill, null, pg);
            }

            if (stroke)
            {
                if (_cs.strokeCS is CSPattern)
                    StrokePattern(pg, false);
                else
                    _rs.dc.DrawGeometry(null, CreatePen(), pg);
            }

            if (_rs.clip)
            {
                if (pg.FillRule != _rs.clip_rule)
                {
                    pg = pg.Clone();
                    pg.FillRule = _rs.clip_rule;
                    pg.Freeze();
                }
                PushClip(pg, pg.Bounds);
                _rs.clip = false;
            }

            //Resets path
            _path = new PathFigure();
            _paths = _path.Segments;
        }

        /// <remarks>
        /// Drawing a pattern isn't entirely trivial. One problem is getting the
        /// VisualBrush to have the right size. The visual brush sizes itself to its
        /// contents, so I've worked around that by using a transparency brush and
        /// clipping as best I can.
        /// 
        /// Note that patterns are oddly enough rendered on the page level's coordinate
        /// system. This is an annoyance as I have to "undo" the stack. My approach is
        /// to push an inverse CTM matrix onto the stack, which seems to work well
        /// enough (though there will be small rounding errors at the very least).
        /// 
        /// Rendering of tiling patterns is very slow, the reason for this is simply
        /// because each tile is rendered in full. One potential workaround would be to 
        /// only render a single tile, and then use WPS's tiling functionality. 
        /// I believe there is something like that on the VisualBrush, but I've not  
        /// looked into it. This should work as all tiles are identical. The reason 
        /// they are rerendered is because one can't simply copy rendered graphics 
        /// (I think, haven't looked into that either).
        /// </remarks>
        private void FillPattern(Geometry gem, FillRule fr)
        {
            var cspat = (CSPattern)_cs.fillCS;
            if (cspat.Pat == null && cspat.CPat == null) return;
            bool pushed_tm_tranform = false;
            Rect outline;

            //First create a transform down to the page level. Note that
            //_rs.from_device_space contains a invers transform of the "to device
            //space matrix". By appending it to the CTM we remove it from the
            //CTM, baring rounding errors.
            var ctm = _cs.CTM;
            ctm.Append(_rs.from_device_space);

            //Pushing on the geometry as a clip. This will force
            //the pattern render "into" the geometry. This is needed
            //for tilingpatterns, as they are rendered straight onto 
            //the page.
            var current_clip = _cs.Current_clip_path;
            _rs.dc.PushClip(gem);

            //Get the bounding box of the geometry being drawn as
            //it's a good bit easier to work with rectangles.
            outline = gem.Bounds;

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

            //Then simply transform the bounds down to the page level.
            outline.Transform(ctm);

            try
            {
                //Since drawing is done on the page level we reverse
                //the CTM and push it on the stack. Alternativly one could
                //_dc.Pop down to the page level if rounding becomes an issue
                //or non-invertible ctms crops up. 
                ctm.Invert();
                PushTransform(new MatrixTransform(ctm));
                pushed_tm_tranform = true;

                if (cspat.Pat != null)
                {
                    //Renders the pattern. This rendering is independent from how the
                    //page is scaled/rotated/etc through the CTM (it's not independent
                    //from the rotation property on a page object though)
                    var dv = Render(cspat.Pat, outline);

                    #region Old test code

                    //For drawing a test backround
                    //_rs.dc.DrawGeometry(Brushes.BurlyWood, null, new RectangleGeometry(outline));

                    /*// Replicates the rPattern.Render call. For experimentation.
                    dv = new MyDrawingVisual() { EdgeMode = EdgeMode.Aliased };
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.Magenta, null, outline);
                    }//*/

                    #endregion

                    //The drawing visual automatically sizes itself. By setting the clip 
                    //one insure that the dv will not be bigger than some expected value.
                    //This assumes that the visual will size itself up to this value when
                    //rendered in the rPattern.Render call.
                    var clip = new RectangleGeometry(outline);
                    clip.Freeze();
                    dv.Clip = clip;
                    //To avoid most AA issues, clipping is done against the bounds of
                    //a rectangle instead of the original figure. This clipping does
                    //not do AA, but fortune has it that it will only be visible when
                    //lines are straight (and not need AA)

                    //Draws the vb, using the clip as it's figure. Note that the
                    //vb is expected to be the size of the clip and no smaller.
                    var vb = new VisualBrush(dv);
                    _rs.dc.DrawGeometry(vb, null, clip);
                }
                else
                {
                    //Tiling pattern.
                    var pat = cspat.CPat;

                    //Save state data.
                    Save();
                    var render_state = _rs;

                    //Sets color for uncolored patterns
                    if (cspat.CS != null)
                    {
                        _cs.fillCS = cspat.CS;
                        _cs.strokeCS = _cs.fillCS;
                        _cs.stroke = _cs.fill.Color;
                    }
                    //Sets default color spaces. Todo: default color
                    else
                    {
                        _cs.fillCS = DeviceGray.Instance;
                        _cs.strokeCS = _cs.fillCS;
                    }

                    //Saves the path related data, as one are probably in the mist of
                    //rendering this path
                    var path = _path; var paths = _paths; var closed_paths = _closed_paths;

                    //Resets state information.
                    _rs.new_pen = true;
                    ClearPath();

                    //This stuff I'm a little unsure of. How much state must be restored?
                    //todo: Test: try setting colors, width, etc, and draw a tiling pattern
                    //      that makes use of it. 
                    ResetState();
                    _rs.clip = false;

                    //Prepare the CTM/state. (Adding the inverse CTM)
                    _cs.CTM.Prepend(ctm);

                    //Renders the tile straight onto the page.
                    try { Render(pat, outline); }
                    catch (Exception e)
                    { Debug.WriteLine("DrawDC.FillPattern:" + e.Message); throw; }
                    finally
                    {
                        Restore();
                        _rs = render_state;

                        //Restores non-state related data
                        _rs.new_pen = true;
                        _path = path; _paths = paths; _closed_paths = closed_paths;
                    }
                }
            }
            finally
            {
                //In a finaly clause to prevent the dc from ending up in an inconsistant
                //state if something happens during rendering.

                //Pops the clip path
                _rs.dc.Pop();
                _cs.Current_clip_path = current_clip;

                //Inverting the CTM can fail. In that case the matrix will not have been
                //pushed on the dc.
                if (pushed_tm_tranform)
                    PopTransform();
            }
        }

        /// <summary>
        /// This function is very similar to "FillPattern", but I've split them
        /// up as it makes the code easier to read.
        /// </summary>
        /// <param name="gem">Gemetry to render into</param>
        /// <param name="has_transform">
        /// If the geometry has a transform. Don't put this transform
        /// on the _cs.CTM
        /// </param>
        private void StrokePattern(Geometry gem, bool has_transform)
        {
            var cspat = (CSPattern)_cs.strokeCS;
            if (cspat.Pat == null && cspat.CPat == null) return;
            Rect outline; Pen temp_pen = null;
            bool pushed_ctm_tranform = false;

            //First create a transform down to the page level. Note that
            //_rs.from_device_space contains a invers transform of the "to device
            //space matrix". By appending it to the CTM we remove it from the
            //CTM, baring rounding errors.
            var ctm = _cs.CTM;
            ctm.Append(_rs.from_device_space);

            //When a pen is used the size of the bounding box will be a little
            //larger. 
            temp_pen = CreateNewPen(Brushes.Black);
            outline = gem.GetRenderBounds(temp_pen);

            //Pushing on a clip. Needed for sh operator
            var current_clip = _cs.Current_clip_path;
            var clip_gem = new RectangleGeometry(outline);
            PushClip(clip_gem, outline);

            //Can't use the gem as a "clip", like with fills, since the strokes 
            //are supposed to go along the edges. We therefore transform the gem
            //down to page level coords
            if (has_transform)
                gem = new GeometryGroup() { Children = new GeometryCollection(new Geometry[] { gem }) };
            else
                //Since we set the transform on the gem, we clone it regardless. We don't want to
                //affect usage of the gem elsewhere.
                gem = gem.Clone();

            //By adding the ctm transform we can use the gem as it was on the page level.
            gem.Transform = new MatrixTransform(ctm);
            gem.Freeze();

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

            //The bounds must also be transformed down to the page level. Technically I suppose we
            //could have done the clipping down on the page level too, something to consider when
            //cleaing up the code.
            outline.Transform(ctm);

            try
            {
                //Since drawing is done on the page level we reverse
                //the CTM and push it on the stack. Alternativly one could
                //_dc.Pop down to the page level if rounding becomes an issue
                ctm.Invert();
                PushTransform(new MatrixTransform(ctm));
                pushed_ctm_tranform = true;

                if (cspat.Pat != null)
                {
                    //Renders the pattern. This rendering is independent from how the
                    //page is scaled/rotated/etc through the CTM (it's not independent
                    //from the rotation property on a page object though)
                    var dv = Render(cspat.Pat, outline);

                    #region Old test code

                    //For drawing a test backround
                    //_rs.dc.DrawGeometry(Brushes.BurlyWood, null, new RectangleGeometry(outline));

                    /*// Replicates the rPattern.Render call. For experimentation.
                    dv = new MyDrawingVisual() { EdgeMode = EdgeMode.Aliased };
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.Magenta, null, outline);
                    }//*/

                    #endregion

                    //The drawing visual automatically sizes itself. By setting the clip 
                    //one insure that the dv will not be bigger than some expected value.
                    //This assumes that the visual will size itself up to this value when
                    //rendered in the rPattern.Render call.
                    var clip = new RectangleGeometry(outline);
                    dv.Clip = clip;
                    //To avoid most AA issues, clipping is done against the bounds of
                    //a rectangle instead of the original figure. This clipping does
                    //not do AA, but fortune has it that it will only be visible when
                    //lines are straight (and not need AA)

                    //Draws the vb, using the clip as its figure. Note that the
                    //vb is expected to be the size of the clip and no smaller.
                    var vb = new VisualBrush(dv);
                    temp_pen.Brush = vb;
                    //temp_pen.Freeze(); //Can't freeze pen with VB
                    _rs.dc.DrawGeometry(null, temp_pen, gem);
                }
                else
                {
                    //Tiling pattern.
                    var pat = cspat.CPat;

                    //Save state data. Consider putting these in a struct to ease
                    //save/restore.
                    Save();
                    var render_state = _rs;
                    var path = _path; var paths = _paths; var closed_paths = _closed_paths;
                    _rs.new_pen = true;

                    //Sets state back to defaults
                    ResetState();

                    //Sets color for uncolored patterns
                    if (cspat.CS != null)
                    {
                        _cs.strokeCS = cspat.CS;
                        _cs.fillCS = _cs.strokeCS;
                        _cs.fill = new SolidColorBrush(_cs.stroke);
                        _cs.fill.Freeze();
                    }
                    //If by mistake a pattern color space is left in
                    //_cs.fillCS, and one paints a uncolored pattern,
                    //the pattern will try to draw itself and one gets
                    //stack overflow.
                    else
                    {
                        _cs.fillCS = DeviceGray.Instance;
                        _cs.strokeCS = _cs.fillCS;
                    }

                    ClearPath();

                    //This stuff I'm a little unsure of. Need to test
                    //this.
                    _cs.line_width = 1;
                    _rs.clip = false;

                    //Prepare the CTM/state. (Adding the inverse CTM). This since
                    //I want the CTM to reflect what's effectivly is on the _dc 
                    //at all times. The code does not require this however.
                    _cs.CTM.Prepend(ctm);

                    VisualBrush vb = null;
                    try
                    {
                        //For strokes the renderer renders into a visual brush,
                        //which is then painted onto the page.
                        var dv = new MyDrawingVisual();
                        var dc = _rs.dc; var dc_pos = _cs.dc_pos;
                        _rs.dc = dv.RenderOpen();
                        try { Render(pat, outline); }
                        finally { _rs.dc.Close(); _rs.dc = dc; _cs.dc_pos = dc_pos; }

                        //Draws the dv, using the clip as it's figure. Note that the
                        //dv is expected to be the size of the clip and no smaller.
                        var clip = new RectangleGeometry(outline);
                        dv.Clip = clip;
                        vb = new VisualBrush(dv);

                        temp_pen.Brush = vb;
                        //temp_pen.Freeze(); //Can't freeze a pen with a VisualBrush
                        _rs.dc.DrawGeometry(null, temp_pen, gem);
                    }
                    finally
                    {
                        //Restores the state
                        Restore();
                        _rs = render_state;
                        _rs.new_pen = true;
                        _path = path; _paths = paths; _closed_paths = closed_paths;
                    }
                }
            }
            finally
            {
                //In a finaly clause to prevent the dc from ending up in an inconsistant
                //state if something happens during rendering.

                //Restores the clip.
                PopTransform();
                _cs.Current_clip_path = current_clip;

                if (pushed_ctm_tranform)
                    PopTransform();
            }
        }

        /// <summary>
        /// Renders a tiling pattern
        /// </summary>
        /// <param name="pat">The compiled tiling pattern</param>
        /// <param name="bounds">Bounds to render into</param>
        /// <remarks>
        /// Quite slow, as all tiles are rendered (instead of rendering
        /// one tile and using it over and over again.)
        /// </remarks>
        internal void Render(CompiledPattern pat, Rect bounds)
        {
            //Pushes on the transform of the pattern
            var m = Util.XtoWPF.ToMatrix(pat.Matrix);
            _cs.CTM.Prepend(m);
            PushTransform(new MatrixTransform(m));

            //Moves bounds into the pattern space
            m.Invert();
            bounds.Transform(m);

            //Gets the clip to a pattern cell's bounding box
            var bbox = Util.XtoWPF.ToRect(pat.BBox);

            //This is only needed for stroked tilepaterns[*], but
            //I'm lazy and do it for all.
            // [*] Since non-stroked patterns fills the bounds anyway
            _rs.dc.DrawRectangle(Brushes.Transparent, null, bounds);

            //How much to move for each draw call
            double xstep = pat.XStep, ystep = pat.YStep;

            //Can be negative.
            //Note: Must substract "bbox.Right", as the size of the figure being
            //      drawn can be bigger than xstep. (paints.pdf got examples of this)
            //      bbox.Left is less important as only some extra drawing will
            //      incure without it (though this isn't propperly tested)
            int x_min_pos = (int)Math.Ceiling((bounds.Left - bbox.Right) / xstep);
            int x_max_pos = (int)Math.Floor((bounds.Right - bbox.Left) / xstep);
            int y_min_pos = (int)Math.Ceiling((bounds.Top - bbox.Bottom) / ystep);
            int y_max_pos = (int)Math.Floor((bounds.Bottom - bbox.Top) / ystep);
            var clip = new RectangleGeometry(bbox);

            //In any case, the xy_min/max pos contains the ammount of titles one
            //have to draw on the two axis. Drawing every title is quite inefficient
            //though, so one could replace _dc with a dc from a visual brush. Render
            //one tile, and then use the tile feature of the visual brush... but 
            //tile patterns are hardly ever used and redrawing is easy.

            Matrix translate;
            for (int y = y_min_pos; y <= y_max_pos; y++)
            {
                translate = Matrix.Identity;
                translate.TranslatePrepend(xstep * x_min_pos, ystep * y);
                for (int x = x_min_pos; x <= x_max_pos; x++)
                {
                    Save();

                    //Set up CTM for a single cell
                    _cs.CTM.Prepend(translate);
                    PushTransform(new MatrixTransform(translate));
                    PushClip(clip, bbox);

                    //To get the pattern down to the "content layer"
                    _rs.from_device_space = _cs.CTM;
                    _rs.from_device_space.Invert();

                    //Runs the pattern
                    //_dc.DrawRectangle(Brushes.Black, new Pen(Brushes.Red, 5) , bbox);
                    ((IDraw)this).Executor.Execute(pat.Commands, this);

                    Restore();

                    //Moves to the next step
                    translate.TranslatePrepend(xstep, 0);
                }
            }
        }

        /// <summary>
        /// Renders a pattern
        /// </summary>
        /// <remarks>
        /// This rendering does not depend on the _cs.CTM variable, so it
        /// need not keep _dc.CTM up to date with the dc object (unlike the
        /// tile pattern function above)
        /// </remarks>
        public DrawingVisual Render(PdfShadingPattern pat, Rect bounds)
        {
            var dv = new MyDrawingVisual();
            var shade = pat.Shading;

            //AA is only supported by the axial shader
            dv.EdgeMode = (shade is PdfAxialShading && shade.AntiAlias) ? EdgeMode.Unspecified : EdgeMode.Aliased;

            using (var dc = dv.RenderOpen())
            {
                //The pattern may have its own coordinate system. I've not done a
                //lot of experimenting with this, but as far as I can tell the
                //COORDS* array is in the patterns coordinate system, and all drawing
                //calls are to be in the patterns coordinate system.
                // *(Specs say "target coordinate space", without elaborating what
                //   the target is)
                var m = Util.XtoWPF.ToMatrix(pat.Matrix);
                var mt = new MatrixTransform(m);
                mt.Freeze();
                dc.PushTransform(mt);

                //The bounds rectangle also needs to be inversly transformed 
                //from the "page space" to the "pattern space".
                m.Invert();
                bounds.Transform(m);

                //Paints backround color. This must be done before pushing
                //the BBox clip. This is not to be done when using the sh
                //operator, and isn't quite right for the transparent rendering
                //model.
                var back = shade.Background;
                if (back != null)
                {
                    var sb = new SolidColorBrush(XtoWPF.ToColor(back));
                    sb.Freeze();
                    dc.DrawRectangle(sb, null, bounds);
                }

                var bbox = shade.BBox;
                if (bbox != null)
                {
                    //Sizes the pattern by drawing a transparent background.
                    //This is since a VisualBrush will size itsef to its contents.
                    //Similar calls is made other places in the code, in case
                    //the pattern lacks a bbox.
                    dc.DrawRectangle(Brushes.Transparent, null, bounds);

                    // Not tested beyond the Paints1BBox.pdf file. Should also have
                    // a Paints6BBox and such to test more scenarios.
                    var rg = new RectangleGeometry(Util.XtoWPF.ToRect(bbox));
                    rg.Freeze();
                    dc.PushClip(rg);
                }

                //Then one can finally run the shader
                if (shade is PdfAxialShading)
                {
                    Vector px;
                    if (dv.EdgeMode != EdgeMode.Aliased)
                    {
                        var iCTM = _cs.CTM;
                        iCTM.Invert();
                        px = iCTM.Transform(new Vector(1, 1));
                    }
                    else px = new Vector();
                    rWPFPattern.RenderShade((PdfAxialShading)shade, dc, bounds, px);
                }
                else
                    rWPFPattern.RenderShade(shade, dc, bounds);
            }
            return dv;
        }

        private Pen CreateNewPen(Brush brush)
        {
            double w = _cs.line_width;

            //I'm not sure, but Adobe may ignore this parameter alltogether.
            if (_cs.stroke_adj) /* <-- todo: Check how Adobe renders issue58.pdf when small. Add "|| true" here to get sumatra behavior */
            {
                double scale_factor = Math.Sqrt(Math.Abs(_cs.CTM.M11 * _cs.CTM.M22 - _cs.CTM.M21 * _cs.CTM.M12));
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
                    w = 1 / scale_factor * 0.75;
                }

                //AFAICT WPF does sanity checking. So infinity, etc, will simply result
                //in the lines not being drawn.
            }

            //Must adjust the dashstyle to WPF norms
            DashStyle dash_style;
            if (_cs.dash_offset == 0 && _cs.dash_array.Length == 0)
            {
                dash_style = DashStyles.Solid;
            }
            else
            {
                // PDF dashes are defined like this:
                //
                // [array] phase.
                //
                // The phase sets when dashes start. A phase of 3 means
                // that you get "three units 

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
                Brush = brush,
                MiterLimit = _cs.miter_limit,
                DashStyle = dash_style,
                DashCap = _cs.line_cap
            };
        }

        private Pen CreatePen()
        {
            if (!_rs.new_pen) return _rs.last_pen;

            _rs.last_pen = CreateNewPen(new SolidColorBrush(_cs.stroke));
            _rs.last_pen.Freeze();

            return _rs.last_pen;
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            _rs.clip = true;
            _rs.clip_rule = (FillRule)rule;
        }

        #endregion

        #region Text objects

        /// <summary>
        /// Sets the TM back to identity
        /// </summary>
        public void BeginText()
        {
            //_text_mode = true;
            _cs.tm.Tm = Matrix.Identity;
            _cs.tm.Tlm = Matrix.Identity;
        }

        /// <summary>
        /// Ends text mode
        /// </summary>
        public void EndText()
        {
            //_text_mode = false;

            if (_clip_mask != null)
            {
                if (_clip_mask.Geometry != null)
                {
                    var gg = new GeometryGroup() { Children = _clip_mask.Geometry };
                    gg.Freeze();

                    if (_clip_mask.ClipDV != null)
                    {
                        _clip_mask.ClipDC.PushClip(gg);
                        _clip_mask.ClipDC.DrawRectangle(Brushes.Black, null, gg.Bounds);
                        _clip_mask.ClipDC.Pop();
                    }
                    else
                    {
                        PushClip(gg, gg.Bounds);
                    }
                }

                //Draws the full string as a big image when clipping. The drawing must be
                //done here as the clip must be put on the dc, while at the same time the
                //dc must be "set back into its original state". IOW we have to pop all
                //text transforms and such before adding the clip
                if (_clip_mask.ClipDV != null)
                {
                    _clip_mask.ClipDC.Close();
                    if (_clip_mask.ClipDV.Drawing != null)
                    {
                        var r = _clip_mask.ClipDV.Drawing.Bounds;
                        VisualBrush vb = new VisualBrush(_clip_mask.ClipDV);

                        //Draws the text
                        if (_cs.ts.Stroke || _cs.ts.Fill)
                            _rs.dc.DrawRectangle(vb, null, r);

                        //Puses the clip on the dc
                        _rs.dc.PushOpacityMask(vb); _cs.dc_pos++;
                        PushClip(new RectangleGeometry(r), r);
                        _rs.dc.DrawRectangle(Brushes.Transparent, null, r);
                    }
                }

                _clip_mask = null;
            }
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

        public void SetFont(cFont font, double size)
        {
            SetFont(font.MakeWrapper(), size);
        }

        public void SetFont(PdfFont font, double size)
        {
            if (_cs.ts.Tf != null)
                _cs.ts.Tf.Dismiss();

            _cs.ts.Tf = font.Realize<StreamGeometry>(this);
            _cs.ts.Tfs = size;
        }

        /// <summary>
        /// Sets a T3 font
        /// </summary>
        public void SetFont(CompiledFont font, double size)
        {
            if (_cs.ts.Tf != null)
                _cs.ts.Tf.Dismiss();

            _cs.ts.Tf = font.Realize();
            _cs.ts.Tfs = size;
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
            _cs.tm.Tm = new Matrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);
            _cs.tm.Tlm = _cs.tm.Tm;
        }

        /// <summary>
        /// Translates the TML and sets it to TM
        /// </summary>
        public void TranslateTLM(double x, double y)
        {
            var m = Matrix.Identity;
            m.Translate(x, y);
            _cs.tm.Tlm.Prepend(m);
            _cs.tm.Tm = _cs.tm.Tlm;
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
            _cs.ts.Tc = ac;
            _cs.ts.Tw = aw;
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
            if (_cs.ts.Tf is Type3Font)
            {
                #region Init for clipping

                DrawingContext ClipDC = null;
                if (_cs.ts.Clip)
                {
                    if (_clip_mask == null)
                        _clip_mask = new ClipMask();

                    if (_clip_mask.ClipDV == null)
                    {
                        _clip_mask.ClipDV = new MyDrawingVisual();
                        _clip_mask.ClipDC = _clip_mask.ClipDV.RenderOpen();
                    }

                    ClipDC = _clip_mask.ClipDC;
                }
                var clip_mask = _clip_mask;

                #endregion

                DrawT3String(text.ByteString, ClipDC);

                #region Clips

                //T3 resets state while rendering T3 glyphs
                _clip_mask = clip_mask;

                #endregion
            }
            else
            {
                #region Init for clipping
                var bs = text.ByteString;
                GeometryCollection clip_group = null;

                //Clipping must be done as a single opperation, so collecting all
                //the geometry into this group.                
                if (_cs.ts.Clip)
                {
                    if (_clip_mask == null)
                        _clip_mask = new ClipMask();
                    
                    if (_clip_mask.Geometry == null)
                        _clip_mask.Geometry = new GeometryCollection(bs.Length);

                    clip_group = _clip_mask.Geometry;
                }

                #endregion

                DrawString(bs, clip_group);

                #region Clips

                //Done at ET

                #endregion
            }
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
            var v = (_cs.ts.Tf != null) ? _cs.ts.Tf.Vertical : false;
            if (_cs.ts.Tf is Type3Font)
            {
                #region Init for clipping

                DrawingContext ClipDC = null;
                if (_cs.ts.Clip)
                {
                    if (_clip_mask == null)
                        _clip_mask = new ClipMask();

                    if (_clip_mask.ClipDV == null)
                    {
                        _clip_mask.ClipDV = new MyDrawingVisual();
                        _clip_mask.ClipDC = _clip_mask.ClipDV.RenderOpen();
                    }

                    ClipDC = _clip_mask.ClipDC;
                }
                var clip_mask = _clip_mask;

                #endregion

                #region Draws T3 strings

                for (int c = 0; c < text.Length; c++)
                {
                    var o = text[c];
                    if (o is PdfString)
                        DrawT3String(((PdfString)o).ByteString, ClipDC);
                    else if (o is double)
                    {
                        if (v)
                            _cs.tm.Tm.TranslatePrepend(0, (double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th);
                        else
                            _cs.tm.Tm.TranslatePrepend((double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th, 0);
                    }
                    else
                    {
                        //Todo: Logg error
                        throw new PdfNotSupportedException();
                    }
                }

                #endregion

                #region Clips

                //T3 resets state while rendering T3 glyphs
                _clip_mask = clip_mask;

                #endregion
            }
            else
            {
                #region Init for clipping
                GeometryCollection clip_group = null;

                //Clipping must be done as a single opperation, so collecting all
                //the geometry into this group.                
                if (_cs.ts.Clip)
                {
                    if (_clip_mask == null)
                        _clip_mask = new ClipMask();

                    if (_clip_mask.Geometry == null)
                        _clip_mask.Geometry = new GeometryCollection();

                    clip_group = _clip_mask.Geometry;
                }

                #endregion

                #region Draws normal strings

                for (int c = 0; c < text.Length; c++)
                {
                    var o = text[c];
                    if (o is PdfString)
                        DrawString(((PdfString)o).ByteString, clip_group);
                    else if (o is double)
                    {
                        if (v)
                            _cs.tm.Tm.TranslatePrepend(0, (double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th);
                        else
                            _cs.tm.Tm.TranslatePrepend((double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th, 0);
                    }
                    else
                    {
                        //Todo: Logg error
                        throw new PdfNotSupportedException();
                    }
                }

                #endregion

                #region Clips

                //Done at ET

                #endregion
            }
        }

        /// <summary>
        /// Draws characters
        /// </summary>
        /// <param name="str">Effectivly a byte array</param>
#if UseString
        private void DrawString(string str, GeometryCollection clip_group)
#else
        private void DrawString(byte[] str, GeometryCollection clip_group)
#endif
        {
            #region Sanity checks

            //Implementation note:
            //The string can at this point be viewed as a series of
            //numbers. Each number can range from 0 to 255. If converted
            //to a .net string it would most likely readable in the 
            //debugger, but this is just a "coincidence".

            var font = _cs.ts.Tf;
            if (font == null)
            {
                //Todo: Log error "tried to render text without font".
                return;
            }

            #endregion

            #region Init for rendering

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th;

            //The glyphs will be scaled up to the font
            //size using this matrix
            Matrix scale = new Matrix(fsize * th, 0, 0, fsize, 0, _cs.ts.Tr);
            Matrix tm;

            //Creates the glyphs. Strings can be multiple characters
            //so just let the font figure out how many glyphs there 
            //are. (Note that "very long strings" are a rarity in PDF
            //files, so this renderer will not take any breaks until
            //the entire string is rendered)
            var glyps = font.GetGlyphs(str);

            #endregion

            for (int c = 0; c < glyps.Length; c++)
            {
                //Gets a single glyph to draw
                var glyph = (rGlyphWPF) glyps[c];

                if (_cs.ts.Clip || _cs.ts.Fill || _cs.ts.Stroke)
                {
                    //Fetches the Tm struct and prepends the
                    //scale matrix. Note that the original
                    //struct is unchanged.
                    tm = _cs.tm.Tm;
                    tm.Prepend(scale); //Scales text up to font size
                    tm.Prepend(glyph.Transform); //Scales text down to 1x1

                    //Moves the glyph to it's origin.
                    tm.Translate(-glyph.Origin.X * fsize, -glyph.Origin.Y * fsize);

                    //Makes a clone of the geometry
                    var outlines = glyph.Outlines.Clone();

                    //!!!!!!!!!!!!!!!!!
                    //Todo: Check what fillrule Adobe actually use (but how?)
                    //outlines.FillRule = FillRule.EvenOdd;
                    //!!!!!!!!!!!!!!!!!

                    //This only needs to be done when stroking/clipping. IOW,
                    //can recode fills to reuse the geometry object. (In that
                    //case the tm has to be pused and popped on the dc)
                    //
                    //This is done (instead of simply pusing the tm) because 
                    //strokes widths will be scaled by the tm if simply 
                    //pushed onto the stack, but by adding the tm to the 
                    //transform property one avoids the line width scaling. 
                    outlines.Transform = new MatrixTransform(tm);

                    outlines.Freeze();

                    //Fills are drawn before strokes, as the stroke will overlap
                    //part of the fill.
                    if (_cs.ts.Fill)
                    {
                        if (_cs.fillCS is CSPattern)
                            FillPattern(outlines, outlines.FillRule);
                        else
                            _rs.dc.DrawGeometry(_cs.fill, null, outlines);
                    }

                    //Strokes are tricker than fills. The pen's stroke width
                    //must lay in the correct coordinate system. The width of
                    //strokes are always in relation to CTM, so we take care
                    //to render on the "current transform matrix" level.
                    if (_cs.ts.Stroke)
                    {
                        if (_cs.strokeCS is CSPattern)
                            StrokePattern(outlines, true);
                        else
                            _rs.dc.DrawGeometry(null, CreatePen(), outlines);
                    }

                    if (_cs.ts.Clip)
                    {
                        clip_group.Add(outlines);
                    }
                }

                if (font.Vertical)
                {
                    double ay = glyph.MoveDist.Y * fsize + _cs.ts.Tc;
                    if (glyph.IsSpace) ay += _cs.ts.Tw;
                    _cs.tm.Tm.TranslatePrepend(0, ay);
                }
                else
                {
                    //Calcs next glyph position (9.4.4)
                    double ax = glyph.MoveDist.X * fsize + _cs.ts.Tc;
                    if (glyph.IsSpace) ax += _cs.ts.Tw;

                    //Adjusts the position so that the next glyphis drawn after
                    //this glyph.
                    _cs.tm.Tm.TranslatePrepend(ax * th, 0);
                }
            }
        }

        /// <summary>
        /// Draws T3 glyphs
        /// </summary>
        /// <remarks>
        /// Todo:
        ///  1. Create a T3 font with strokes.
        ///  2. Paint it with a rotated text matrix
        ///  3. See what happens to the strokes.
        ///  4. Also try to adjust the cm similarly.
        ///  
        /// Reason: CTM and TM is more or less ignored
        ///         wile rendering the T3 fonts.
        ///         
        ///  1. Test clip rendering with space characters
        ///     before, after and in the middle of the text
        ///     
        /// Reason: Space characters will not size up the
        ///         ClipDV, or will they as space characters
        ///         are rendered as empty images?
        ///         
        ///         Perhaps one can paint a transparent dot
        ///         for the space character to size up the dv.
        /// </remarks>
#if UseString
        private void DrawT3String(string str)
#else
        private void DrawT3String(byte[] str, DrawingContext ClipDC)
#endif
        {
            var font = (Type3Font)_cs.ts.Tf;

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th;

            //The glyphs will be scaled up to the font
            //size using this matrix
            Matrix scale = new Matrix(fsize, 0, 0, fsize * th, 0, _cs.ts.Tr);
            Matrix tm; MatrixTransform mt;

            //Type 3 fonts have their own coordinate system.
            var font_matrix = XtoWPF.ToMatrix(font.FontMatrix);

            //Gets the glyph data for drawing the glyphs.
            var glyps = font.GetGlyphs(str);

            //Renders all glyphs into bitmaps.
            //var bitmaps = new BitmapSource[glyps.Length];
            for (int c = 0; c < glyps.Length; c++)
            {
                //Gets a single glyph to draw
                var glyph = glyps[c];

                if (_cs.ts.Clip || _cs.ts.Fill || _cs.ts.Stroke)
                {
                    Save();
                    var render_state = _rs;

                    //Fetches the Tm struct and prepends the
                    //scale matrix.
                    tm = _cs.tm.Tm;
                    tm.Prepend(scale); //Scales text up to font size

                    //Unlike with normal font rendering, the TM must be
                    //added to the CTM. This so line scaling will be
                    //correct. (Of course, only line scaling that takes
                    //device space into account needs this, and that
                    //scaling is forced off. Todo: Look into how Adobe
                    //handles "automatic linewidth adjustment inside
                    //type 3 fonts (and patterns and forms.)
                    _cs.CTM.Prepend(tm);

                    //The font has its own coordinate system. Adds it.
                    _cs.CTM.Prepend(font_matrix);

                    //The "device space" matrix needs to be updated.
                    //for the sake of the line scaling.
                    _rs.from_device_space = _cs.CTM;
                    _rs.from_device_space.Invert();

                    //Prepeares to render the glyph to a drawing visual
                    var dv = new MyDrawingVisual();
                    var dc_pos = _cs.dc_pos;
                    var clip = _cs.ts.Clip;

                    //This could potentially be move outside the for loop,
                    //with a save/restore before and after the loop. Would
                    //save one from setting this for every single glyph.
                    //
                    //Sets state back to defaults
                    ResetState();

                    //Subtelty: The fill color is to be used for both strokes and
                    //fills. This as one only set a font's color through the fill
                    //property.
                    _cs.stroke = _cs.fill.Color;
                    _cs.strokeCS = _cs.fillCS;

                    _rs.dc = dv.RenderOpen();

                    //Renders the form. No try/catch is needed.
                    ((IDraw)this).Executor.Execute(glyph.Commands, this);

                    //Restores the state
                    _rs.dc.Close();
                    _rs = render_state;
                    _cs.dc_pos = dc_pos;
                    _cs.ts.Clip = clip;

                    #region Note

                    //Technically one could have rendered the glyph
                    //straight onto the document.
                    //
                    //However future modification will include bitmap
                    //caching, so there's no point of not doing it 
                    //this way for now.

                    #endregion

                    #region Calculates true render size
#if ScaleT3FontDrawing
                    //Put outside the "for loop" when this works as it should

                    Vector scale_image = _cs.CTM.Transform(new Vector(1, 1));
                    double scale_x = Math.Abs(scale_image.X);
                    double scale_y = Math.Abs(scale_image.Y);
#endif
                    #endregion

                    #region Renders the glyph into a bitmap

                    //(Todo: Width/Height is "wrong". However, one need to adjust scaling parameters on dv.Transform to change them)
                    //This is to say, the resolution of the rendering will usally be too low, making the text jaggy.

                    if (dv.Drawing == null)
                    {
                        //An empty T3 glyfs will cause dv.Drawing and dv.Transform to be null. In that case Adobe
                        //X skips the glyph in its entierty

                        //Sets back the device space matrix
                        //Restore();
                        //continue;
                        //No, adobe still moves the distance
                    }
                    else
                    {
                        Rect r = dv.Drawing.Bounds;


                        //When rendering the figure it must be positioned within the 0->width, 0->height of the render bitmap. I.e.
                        //we calculate how much to offset the figure to get it inside the render target
#if ScaleT3FontDrawing
                        double render_height = r.Height * scale_y;
                        double render_width = r.Width * scale_x;
                        double offset_y = -r.Top * scale_y;
                        double offset_x = -r.Left * scale_x;
                        dv.Transform = new MatrixTransform(scale_x, 0, 0, scale_y, offset_x, offset_y);
#else
                        double render_height = r.Height;
                        double render_width = r.Width;
                        double offset_y = -r.Top;
                        double offset_x = -r.Left;
                        dv.Transform = new TranslateTransform(offset_x, offset_y);
#endif
                        dv.Transform.Freeze();
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(render_width), (int)Math.Ceiling(render_height), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(dv);
                        bitmap.Freeze();

                    #endregion

                        if (!_cs.ts.Clip)
                        {
                            mt = new MatrixTransform(tm); mt.Freeze();
                            _rs.dc.PushTransform(mt); _cs.dc_pos++;
                            mt = new MatrixTransform(font_matrix); mt.Freeze();
                            _rs.dc.PushTransform(mt); _cs.dc_pos++;
#if ScaleT3FontDrawing
                            var img_size = new Size(bitmap.PixelWidth / scale_x, bitmap.PixelHeight / scale_y);
                            _rs.dc.DrawImage(bitmap, new Rect(new Point(-offset_x / scale_x, -offset_y / scale_y), img_size));
#else
                            _rs.dc.DrawImage(bitmap, new Rect(new Point((int)-offset_x, (int)-offset_y),
                                new Size(bitmap.PixelWidth, bitmap.PixelHeight)));
#endif
                            //For testing
                            //_rs.dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(128, 128, 0, 128)), null, dv.Drawing.Bounds);
                        }
                        else
                        {
                            //When clipping one have to render the entire string into one big bitmap.
                            mt = new MatrixTransform(tm); mt.Freeze();
                            ClipDC.PushTransform(mt);
                            mt = new MatrixTransform(font_matrix); mt.Freeze();
                            ClipDC.PushTransform(mt);
#if ScaleT3FontDrawing
                            var img_size = new Size(bitmap.PixelWidth / scale_x, bitmap.PixelHeight / scale_y);
                            ClipDC.DrawImage(bitmap, new Rect(new Point(-offset_x / scale_x, -offset_y / scale_y), img_size));
#else
                            ClipDC.DrawImage(bitmap, new Rect(new Point(-offset_x, -offset_y),
                                   new Size(bitmap.PixelWidth, bitmap.PixelHeight)));
#endif

                            ClipDC.Pop();
                            ClipDC.Pop();

                            #region Testing PushOpacityMask
                            //{

                            //Clipping is problematic. It can not be implemented through straightforwards use of
                            //"PushOpacityMask"*, as the mask will rezise itself to the size of whatever is drawn after
                            //pushing it. But let's experiment a little.

                            //_rs.dc.PushOpacityMask(new ImageBrush(bitmap));
                            //_rs.dc.PushClip(new RectangleGeometry(new Rect(new Size(50, 50))));
                            //_rs.dc.DrawRectangle(Brushes.Transparent, null, new Rect(new Size(50, 50)));
                            //_rs.dc.DrawRectangle(Brushes.Yellow, null, new Rect(new Size(100, 50)));
                            //_rs.dc.DrawRectangle(Brushes.Purple, null, new Rect(new Size(25, 25)));

                            //_rs.dc.Pop();
                            //_rs.dc.Pop();
                            //}
                            //Right now clipping is done outside this function. 
                            #endregion
                        }
                    }
                    //Sets back the device space matrix
                    Restore();
                }

                //Calcs next glyph position
                double ax = glyph.MoveX * font_matrix.M11 * fsize + _cs.ts.Tc;
                if (glyph.IsSpace) ax += _cs.ts.Tw;

                //Adjusts the position so that the next glyph is drawn after
                //this glyph.
                _cs.tm.Tm.TranslatePrepend(ax * th, glyph.MoveY * font_matrix.M11);
            }
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

        #region State support

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
            public DrawingContext dc;

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
            public FillRule clip_rule;

            public void SetUp()
            {
                clip = false;
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
            public Rect Current_clip_path;

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
            public SolidColorBrush fill;

            /// <summary>
            /// Color used for stroking
            /// </summary>
            public Color stroke;

            /// <summary>
            /// Graphics state parameters that only affect text
            /// </summary>
            /// <remarks>
            /// Not tecnically part of the graphics state, but gets
            /// saved/restored by Adobe, so we simply place it here
            /// </remarks>
            public TextState ts;

            /// <summary>
            /// TextMetrix transform
            /// </summary>
            /// <remarks>
            /// Not tecnically part of the graphics state, but gets
            /// saved/restored by Adobe, so we simply place it here
            /// (Not placed inside ts for reasons I don't recall)
            /// </remarks>
            public TextMetrix tm;

#if ALPHA_HACK
            /// <summary>
            /// Current alpha
            /// </summary>
            public double ca;
#endif

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
            /// 
            /// I'm not sure, but Adobe may ignore this parameter alltogether.
            /// </remarks>
            public bool stroke_adj;

            /// <remarks>
            /// Transfer is only implemented for images. Won't do anything
            /// about this before tackeling alpha colors, as they are related.
            /// </remarks>
            public FCalcualte[] transfer;

            /// <summary>
            /// Resets the state back to default
            /// </summary>
            /// <remarks>
            /// Remeber that the clip path is set to infinity
            /// </remarks>
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
#if ALPHA_HACK
                ca = 1;
#endif
#if AutoAdjustLikeAdobe
                stroke_adj = true;
#else
                stroke_adj = false;
#endif
                fill = Brushes.Black;
                stroke = Colors.Black;
                transfer = null;
                Current_clip_path = new Rect(new Point(double.MinValue, double.MinValue),
                                             new Point(double.MaxValue, double.MaxValue));

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
                // There's no need to reset text metrix
            }
        }

        class ClipMask
        {
            public GeometryCollection Geometry;
            public MyDrawingVisual ClipDV = null;
            public DrawingContext ClipDC = null;
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
            public double Tc;

            /// <summary>
            /// Word spacing, i.e. the distance between words
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

            /// <summary>
            /// Text leading. 
            /// 
            /// The vertical distance between text baselines.
            /// </summary>
            /// <remarks>
            /// Only used by the T*, ', and " operators
            /// </remarks>
            public double Tl;

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
        }

        #endregion

        #region IFontFactory

        //RenderEngine IFontFactory.Engine
        //{ get { return RenderEngine.DrawDC; } }

        FontCache IFontFactory.FontCache { get { return _font_cache; } }

        IGlyphCache IFontFactory.CreateGlyphCache() => new StandardGlyphCache();

        rFont IFontFactory.CreateMemTTFont(PdfLib.Read.TrueType.TableDirectory td, bool symbolic, ushort[] cid_to_gid)
        {
            return new MemTTFont<StreamGeometry>(td, this, symbolic, cid_to_gid);
        }

        rFont IFontFactory.CreateBuiltInFont(PdfType1Font font, double[] widths, AdobeFontMetrixs afm, bool substituted)
        {
            //if (font.BaseFont.StartsWith("Times"))
            //    widths = widths;
            if (substituted)
            {
                string filename = Util.Registery.FindFontFilename(font.BaseFont, afm.Bold, afm.Italic);
                if (filename != null)
                {
                    var font_folder = rFont.FontFolder + filename;

                    if (File.Exists(font_folder))
                    {
                        var ttf = new MemoryStream(File.ReadAllBytes(font_folder));

                        //Can potetially use WPFFont here. All these fonts will be handled perfectly by WPF font, while this
                        //impel. is more fraguile. Of course, you'd have to figure out the correct name to use, then (A reverse
                        //lookup in the registery should do the trick. You know the filename, so you just have to find the 
                        //original name.)
                        return rFont.Create<StreamGeometry>(ttf, widths, font, this);
                    }
                }
            }

#if USE_WPF_FONT
            //Todo: Situations where there's no suitble WPF font representation of a built in font. Could, for instance, fall
            //back on the other code path. Oh, and TrueType Symbol != CFF Symbol, so symbol should use the other path.
            return new WPFFont(font.BaseFont,
                               afm.SubstituteFont,
                               widths,
                               afm);
#else
            //First check if the font exists as a embeded resource.
            var gn = afm.GhostName;
            Stream cff = Res.StrRes.GetBinResource(string.Format("Font.cff.{0}.cff", gn));
            if (cff == null && File.Exists(string.Format("fonts\\{0}.cff", gn)))
            {
                //Next we look into the "font" folder in the executing directory
                cff = new MemoryStream(File.ReadAllBytes(string.Format("fonts\\{0}.cff", gn)));
            }
            if (cff == null)
            {
                var font_folder = rFont.FontFolder;

                font_folder += afm.SubstituteFile + ".ttf";
                if (File.Exists(font_folder))
                {
                    cff = new MemoryStream(File.ReadAllBytes(font_folder));

                    return new TTFont<StreamGeometry>(cff, widths, font, this);
                }
            }
            if (cff == null)
                throw new NotImplementedException();
            var fd = font.FontDescriptor;
            if (fd == null)
                fd = afm.CreateFontDescriptor();

            return new Type1CFont<StreamGeometry>(widths, fd, font.Encoding, this, cff, true);
#endif
        }

        rFont IFontFactory.CreateBuiltInFont(PdfType0Font font, AdobeFontMetrixs afm, bool substituted)
        {
            if (substituted)
            {
                string filename = Util.Registery.FindFontFilename(font.BaseFont, afm.Bold, afm.Italic);
                if (filename != null)
                {
                    var font_folder = rFont.FontFolder + filename;

                    if (File.Exists(font_folder))
                    {
                        var ttf = new MemoryStream(File.ReadAllBytes(font_folder));

                        //The ttf file can be OpenType, CFF, or Truetype. Needs to return the appropriate CID font.
                        return new CIDTTFont<StreamGeometry>((CIDFontType2)font.DescendantFonts, ttf, false, this);
                    }
                }
            }
            
#if USE_WPF_FONT
            return new CIDWPFFont(font.BaseFont, afm.SubstituteFont, afm, font.DescendantFonts, font.Encoding.WMode);
#else
            //First check if the font exists as a embeded resource.
            var gn = afm.GhostName;
            Stream cff = Res.StrRes.GetBinResource(string.Format("Font.cff.{0}.cff", gn));
            if (cff == null && File.Exists(string.Format("fonts\\{0}.cff", gn)))
            {
                //Next we look into the "font" folder in the executing directory
                cff = new MemoryStream(File.ReadAllBytes(string.Format("fonts\\{0}.cff", gn)));
            }
            if (cff == null || afm.PreferTrueType)
            {
                var font_folder = rFont.FontFolder;

                font_folder += afm.SubstituteFile + ".ttf";
                if (File.Exists(font_folder))
                {
                    cff = new MemoryStream(File.ReadAllBytes(font_folder));

                    return new CIDTTFont<StreamGeometry>((CIDFontType2)font.DescendantFonts, cff, false, this);
                }
            }
            if (cff == null)
                throw new NotImplementedException();
            
            return new CIDT1CFont<StreamGeometry>((CIDFontType2) font.DescendantFonts, this, font.Encoding.WMode, cff);
#endif
        }

        /// <summary>
        /// Attach a Matrix transform to the path
        /// </summary>
        void IGlyphFactory<StreamGeometry>.SetTransform(StreamGeometry path, xMatrix transform)
        {
            ((StreamGeometry) path).Transform = new MatrixTransform(Util.XtoWPF.ToMatrix(transform));
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
            //Quick hack
            var sg = new StreamGeometry[outline.Length];
            for (int c = 0; c < sg.Length; c++)
                sg[c] = (StreamGeometry) outline[c];
            return new rGlyphWPF(sg, new Point(advance.X, advance.Y), new Point(origin.X, origin.Y), Util.XtoWPF.ToMatrix(mt), freeze, is_space_char);
        }

        IGlyph<StreamGeometry> IGlyphFactory<StreamGeometry>.GlyphRenderer()
        {
            return new GlyphCreator();
        }

        #endregion

        #region Helper functions

        internal class GlyphCreator : IGlyph<StreamGeometry>, IGlyphDraw
        {
            StreamGeometry _sg;
            StreamGeometryContext _sgc;

            public IGlyphDraw Open()
            {
                Debug.Assert(_sgc == null, "Did you forget to close?");
                if (_sgc != null)
                    _sgc.Close();
                _sg = new StreamGeometry();
                _sg.FillRule = FillRule.EvenOdd;
                _sgc = _sg.Open();
                return this;
            }

            void IGlyphDraw.BeginFigure(xPoint startPoint, bool isFilled, bool isClosed)
            {
                _sgc.BeginFigure(new Point(startPoint.X, startPoint.Y), isFilled, isClosed);
            }
            void IGlyphDraw.QuadraticBezierTo(xPoint point1, xPoint point2, bool isStroked, bool isSmoothJoin)
            {
                _sgc.QuadraticBezierTo(new Point(point1.X, point1.Y), new Point(point2.X, point2.Y), isStroked, isSmoothJoin);
            }
            void IGlyphDraw.LineTo(xPoint point, bool isStroked, bool isSmoothJoin)
            {
                _sgc.LineTo(new Point(point.X, point.Y), isStroked, isSmoothJoin);
            }
            void IGlyphDraw.BezierTo(xPoint point1, xPoint point2, xPoint point3, bool isStroked, bool isSmoothJoin)
            {
                _sgc.BezierTo(new Point(point1.X, point1.Y), new Point(point2.X, point2.Y), new Point(point3.X, point3.Y), isStroked, isSmoothJoin);
            }

            public void Dispose()
            {
                _sgc.Close();
                _sgc = null;
            }

            public StreamGeometry GetPath()
            {
                return _sg;
            }
        }

        /// <summary>
        /// Calculates how much one must draw in x/y directions
        /// to get one pixel on the device level
        /// </summary>
        /// <returns>A vector with needed x and y distance</returns>
        Vector CalcCurrentPixelSize()
        {
            var v = new Vector(1, 1);
            var iCTM = _cs.CTM;
            iCTM.Invert();
            return iCTM.Transform(v);
        }

        void PushTransform(Transform t)
        {
            t.Freeze();
            _rs.dc.PushTransform(t);
            _cs.dc_pos++;
        }

        /// <summary>
        /// Use this when pushing clip, alternativly save and
        /// restore the current clip path
        /// </summary>
        void PushClip(Geometry gem, Rect bounds)
        {
            gem.Freeze();
            _rs.dc.PushClip(gem);
            _cs.dc_pos++;

            //Intersect with the existing clip
            bounds.Transform(_cs.CTM);
            _cs.Current_clip_path.Intersect(bounds);
        }

        void PopTransform()
        {
            _rs.dc.Pop();
            _cs.dc_pos--;
        }

        #endregion
    }
}