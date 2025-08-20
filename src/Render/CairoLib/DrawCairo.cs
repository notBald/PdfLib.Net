#region Imports and defines
//Defines this class
#define USE_CAIRO

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
using PdfLib.Render.Commands;
using PdfLib.Compose.Text;
using PdfLib.Compose;

#endregion
namespace PdfLib.Render.CairoLib
{
#if USE_CAIRO
    /// <summary>
    /// A plain implementation that use Cairo as the render target
    /// </summary>
    internal class DrawCairo : IFontFactory<CairoPath>, IDraw
    {
        #region Variables and properties

        /// <summary>
        /// Cairo renderer
        /// </summary>
        Cairo _cr;

        xPoint _current_point; //<-- Todo: Detect invalid current points (i.e. if not set before use)

        /// <summary>
        /// Cache over fonts and glyphs. 
        /// </summary>
        private FontCache _font_cache = new FontCache();

        /// <summary>
        /// From how I'm reading the specs, the W and W*
        /// commands does not end the path. They just mark
        /// the path for use with clipping.
        /// </summary>
        private bool _clip = false;
        private xFillRule _clip_rule;

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
        /// Matrix used to transform CTM to the
        /// user space
        /// </summary>
        private cMatrix _from_device_space;

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
        private cMatrix _to_device_space;

        /// <summary>
        /// For measuring the render size of drawn text.
        /// Given in device coordinates.
        /// </summary>
        private xRect? _glyph_bounds;

        /// <summary>
        /// Knowing when the reset _glyph_bounds can be tricky,
        /// so we only do it when this counter reaches zero.
        /// </summary>
        private int _reset_text_bounds;

        /// <summary>
        /// Used when clipping text
        /// </summary>
        private ClipMask _clip_mask = null;

        /// <summary>
        /// The bounds of rendered text. Check HasTextBounds before
        /// using these bounds.
        /// </summary>
        public xRect? TextBounds { get { return _glyph_bounds; } }

        /// <summary>
        /// Does not track graphic state.
        /// </summary>
        /// <remarks>Isn't this done by the compiler anyway?</remarks>
        public GS GraphicState { get { return GS.Unknown; } }

        /// <summary>
        /// Height of the drawing surface
        /// </summary>
        public int SurfaceHeight { get { return _cr.Height; } }

        /// <summary>
        /// Width of the drawing surface
        /// </summary>
        public int SurfaceWidth { get { return _cr.Width; } }

        /// <summary>
        /// Stride of the drawing surface
        /// </summary>
        public int SurfaceStride { get { return _cr.Stride; } }

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
                         bool respect_aspect, bool scale_lines_to_output, int rotate)
        {
#if !AutoAdjustLikeAdobe
            //Forced true to avoid potential issues. 
            //I think this function still works, but it needs testing. Run through the test
            //files for Forms and Patterns to see if they still render correctly.
            scale_lines_to_output = true;
#endif
            // Calcs the size of the user space. Negative size is intended (though poorly tested).
            double user_space_width = MediaBox.URx - MediaBox.LLx;
            double user_space_heigth = MediaBox.LLy - MediaBox.URy;
            double abs_us_width = Math.Abs(user_space_width);
            double abs_us_height = Math.Abs(user_space_heigth);

            // Corrects for aspect ratio.
            xSize output = PdfRender.CalcDimensions(MediaBox, output_width, output_height, respect_aspect, 0);

            //Sets up mapping from the defualt PDF user space to Cairo device space.
            //
            //   PDF: 0.1---1.1    WPF: 0.0---1.0   Scale matrix: Sx--0   WPF matrix: M11--M12
            //         |     |           |     |                  |   |                |    |
            //        0.0---1.0         0.1---1.1                 0--Sy               M21--M22
            double device_width = output.Width;
            double device_height = output.Height;
            double scale_x = device_width / user_space_width;
            double scale_y = device_height / user_space_heigth;

            //Note: If nothing is rendered it's likely because the offset_x/y is set wrong
            //      here.
            xMatrix from_userspace_to_device_coords = new xMatrix(scale_x, 0, 0,
                                                                scale_y,
                                                                (scale_x < 0) ? output.Width : 0,
                                                                (scale_y < 0) ? output.Height : 0);

            //Translates so that the media box starts at 0,0
            from_userspace_to_device_coords = from_userspace_to_device_coords.TranslatePrepend(-MediaBox.LLx, -MediaBox.LLy);

            //Resets all state information
            _cr = new Cairo(output.Width, output.Height);
            _cs.Reset(_cr);
            _tm.Tm = _tm.Tlm = cMatrix.Identity;

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
                from_userspace_to_device_coords = from_userspace_to_device_coords.Rotate(rotate);
                //Assumes angular rotation.
                if (rotate == 90)
                    from_userspace_to_device_coords = from_userspace_to_device_coords.Translate(output.Height, 0);
                else if (rotate == 180)
                    from_userspace_to_device_coords = from_userspace_to_device_coords.Translate(output.Width, output.Height);
                else if (rotate == 270)
                    from_userspace_to_device_coords = from_userspace_to_device_coords.Translate(0, output.Width);
            }//*/

#if !AutoAdjustLikeAdobe
            //if (scale_lines_to_output)
#endif
            {
                _to_device_space = new cMatrix(from_userspace_to_device_coords);
                from_userspace_to_device_coords = from_userspace_to_device_coords.Inverse;
                _from_device_space = new cMatrix(from_userspace_to_device_coords);
            }

            _cr.PushTransform(_to_device_space);

            //Fills the whole page white. (Note, _cr is not used to store color state, so no need to change back to black.)
            _cr.SetColor(1, 1, 1);
            _cr.Paint();
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
            _cr.LineCap = Cairo.Enums.LineCap.CAIRO_LINE_CAP_BUTT;
            _cr.LineJoin = Cairo.Enums.LineJoin.CAIRO_LINE_JOIN_MITER;
            _cr.MiterLimit = 10;
            _cr.DashStyle = new xDashStyle(0);

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

        ~DrawCairo()
        {
            Deconstruct();
        }

        /// <summary>
        /// Tears down the object
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Deconstruct();
        }

        private void Deconstruct()
        {
            _cr.Dispose();
        }

        public void FlushCachedData()
        {
            if (_cs.ts.Tf != null)
            {
                //The font can still be reused after this, so
                //calling dismiss instead of dispose.
                _cs.ts.Tf.Dismiss();
                _cs.ts.Tf = null;
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
                _cr.LineWidth = value;
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
            _cr.LineJoin = (Cairo.Enums.LineJoin)style;
        }

        /// <summary>
        /// Set how lines are ended
        /// </summary>
        public void SetLineCapStyle(xLineCap style)
        {
            _cr.LineCap = (Cairo.Enums.LineCap)style;
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
            _cr.MiterLimit = limit;
        }

        /// <remarks>
        /// WPF's stroke dash array is depend to the tickness of the current
        /// stroke width. Must thererefor wait to create the WPF object 
        /// until the line width is known. 
        /// </remarks>
        public void SetStrokeDashAr(xDashStyle ds)
        {
            _cs.dash_array = ds.Dashes;
            _cs.dash_offset = ds.Phase;
            _cr.DashStyle = ds;
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
            }
            //No alpha related parameters supported by this renderer

            ////Some of my test files require alpha to look correct.
#if ALPHA_HACK
            var ca = gs.ca;
            if (ca != null && ca.Value != 1)
            {
                _dc.PushOpacity(ca.Value); _cs.dc_pos++;
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
            _state_stack.Push(_cs);
            _cr.Save();
        }

        /// <summary>
        /// Restores the previous state
        /// </summary>
        public void Restore()
        {
            //Should perhaps log this error
            if (_state_stack.Count == 0)
                return;

            _cs = _state_stack.Pop();
            _clip = false;
            _cr.Restore();
        }

        /// <summary>
        /// Prepend matrix to CTM
        /// </summary>
        public void PrependCM(xMatrix xm)
        {
            cMatrix m = new cMatrix(xm.M11, xm.M12, xm.M21, xm.M22, xm.OffsetX, xm.OffsetY);
            _cr.Transform(m);
        }

        #endregion

        #region Color

        public void SetFillColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.fillCS = DeviceCMYK.Instance;
            _cs.fill = _cs.fillCS.Converter.MakeColor(
                            new double[] { cyan, magenta, yellow, black }
                        );
        }
        public void SetFillColor(double red, double green, double blue)
        {
            _cs.fillCS = DeviceRGB.Instance;
            _cs.fill = new DblColor(red, green, blue);
        }
        public void SetFillColor(double gray)
        {
            _cs.fillCS = DeviceGray.Instance;
            _cs.fill = new DblColor(gray, gray, gray);
        }
        public void SetFillColorSC(double[] color)
        {
            _cs.fill = _cs.fillCS.Converter.MakeColor(color);
        }
        public void SetFillColor(double[] color)
        {
            _cs.fill = _cs.fillCS.Converter.MakeColor(color);
        }

        public void SetStrokeColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.strokeCS = DeviceCMYK.Instance;
            _cs.stroke = _cs.strokeCS.Converter.MakeColor(
                                new double[] { cyan, magenta, yellow, black }
                         );
        }
        public void SetStrokeColor(double red, double green, double blue)
        {
            _cs.strokeCS = DeviceRGB.Instance;
            _cs.stroke = new DblColor(red, green, blue);
        }
        public void SetStrokeColor(double gray)
        {
            _cs.strokeCS = DeviceGray.Instance;
            _cs.stroke = new DblColor(gray, gray, gray);
        }
        public void SetStrokeColorSC(double[] color)
        {
            _cs.stroke = _cs.strokeCS.Converter.MakeColor(color);
        }
        public void SetStrokeColor(double[] color)
        {
            _cs.stroke = _cs.strokeCS.Converter.MakeColor(color);
        }

        public void SetFillCS(IColorSpace cs)
        {
            if (cs is PatternCS)
                _cs.fillCS = new CSPattern(((PatternCS)cs).UnderCS);
            else
            {
                _cs.fillCS = cs;
                _cs.fill = cs.DefaultColor.ToDblColor();
            }
        }

        public void SetStrokeCS(IColorSpace cs)
        {
            if (cs is PatternCS)
                _cs.strokeCS = new CSPattern(((PatternCS)cs).UnderCS);
            else
            {
                _cs.strokeCS = cs;
                _cs.stroke = cs.DefaultColor.ToDblColor();
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
                    _cs.fill = csp.CS.Converter.MakeColor(color);
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
                _cs.stroke = csp.CS.Converter.MakeColor(color);
        }

        #endregion

        #region Shading patterns

        public void Shade(PdfShading shading)
        {
            //We draw into the clip path
            var clip_path = _cr.ClipBounds;
            _cr.Save();

            try
            {
                if (shading is PdfAxialShading)
                {
                    var ax = (PdfAxialShading)shading;

                    //The AA compensation is potentalliy buggy, so don't
                    //use if not needed.
                    _cr.Antialias = (shading.AntiAlias) ? Cairo.Enums.AntialiasMethod.DEFAULT : Cairo.Enums.AntialiasMethod.NONE;
                    xVector px = (shading.AntiAlias) ? px = _cr.TransformFromDeviceDistance(new xVector(1, 1)) : new xVector();

                    rCairoPattern.RenderShade(ax, _cr, clip_path, px);
                }
                else
                {
                    //Radial and Sampler patterns don't support AA.
                    _cr.Antialias = Cairo.Enums.AntialiasMethod.NONE;
                    rCairoPattern.RenderShade(shading, _cr, clip_path);
                }
            }
            finally
            {
                //Restores antialising, and potentially other stuff. 
                _cr.Restore();
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
        /// <remarks>Currently only drawing 4-byte aligned images</remarks>
        public void DrawImage(PdfImage img)
        {
            rImage bs = rImage.MakeImage(img);
            int pixel_height = img.Height, pixel_width = img.Width;
            if (bs == null)
            {
                //ToDo: Report
                return;
            }

            // Note 1: that I flip the y axis (-1d). Otherwise the image gets drawn upside
            // down.
            //
            // Note 2: OffsetY is "1" to move the image up by its full height.
            var mt = new cMatrix(1d / pixel_width, 0, 0, -1d / pixel_height, 0, 1);


            // According to the specs the images are always drawn 1-1 to user 
            // coordinates. Not sure what the specs mean by that as an image's
            // size is decided by the CTM.
            //
            // What I'm doing is scaling the image down to 1x1 pixels, then
            // letting the CTM scale it back up to size. 
            _cr.PushTransform(mt);

            if (_cs.transfer != null)
                rImage.TransferBGRA32(bs.RawData, _cs.transfer);

            if (img.ImageMask)
            {
                //Image masks works as a alpha mask. So we draw the image
                //as alpha colors. (Note that the rImage is already prepeared
                //for this)
                _cr.PushGroupAlpha();
                _cr.DrawImage(bs, 0, 0, pixel_width, pixel_height);

                //Pops the result of the drawn image as a pattern. This
                //pattern will be used as the alpha mask
                using (var pat = _cr.PopGroup())
                {
                    //The imagemask may be filled with a solid color or
                    //a pattern
                    if (_cs.fillCS is CSPattern)
                    {
                        //We need to inform "FillPattern" where to draw.
                        //This is done by setting up a rect path
                        _cr.RectangleAt(0, 0, pixel_width, pixel_height);

                        //One could change the fillrule here, but for rectangles
                        //it shoudn't matter.

                        //This command creates a source that can later be drawn
                        FillPattern();

                        //We remove the rectangle path. This path was actually
                        //created by FillPattern, and not _cr.RectAt
                        _cr.NewPath();
                    }
                    else
                        //Sets a solid color as the source
                        _cr.SetColor(_cs.fill);

                    //This operation blends with source with the alpha
                    //mask (the pat). 
                    _cr.Mask(pat);
                }

                _cr.Pop(); //Pops mt (and restores CTM and fillrule)
                return;
            }
            else
            {
                //SMask. Decode into alpha format
                object smask = img.SMask ?? img.Mask;
                if (smask is PdfImage)
                {
                    var mask = (PdfImage)smask;
                    rImage mask_img = mask.ImageMask ? rImage.MakeImage(mask) : rImage.MakeA8Image(mask); ;

                    //This informs the _cs that we're going to create an
                    //alpha channel group
                    _cr.PushGroupAlpha();

                    ////The mask image gets painted as an alpha channel
                    if (mask_img.Width != pixel_width || mask_img.Height != pixel_height)
                    {
                        //Cairo does not scale a surface, so we have to manually instruct
                        //it to scale.
                        _cr.PushTransform(new cMatrix(pixel_width / (double)mask_img.Width, pixel_height / (double)mask_img.Height));
                        _cr.DrawImage(mask_img, 0, 0, mask_img.Width, mask_img.Height, Cairo.Enums.Filter.NEAREST);
                        _cr.Pop();
                    }
                    else
                        _cr.DrawImage(mask_img, 0, 0, mask_img.Width, mask_img.Height);

                    ////We grab the alpha channel as a pattern
                    using (var pat = _cr.PopGroup())
                    {
                        //Then we create a group with alpha and color data
                        //_cr.PushGroup();

                        //We set the normal image as a source and mask it.
                        _cr.MaskImage(bs, pat);
                    }

                    //We set the recently created group as a source
                    //_cr.PopGroupAndSetSource();

                    //And draw it.
                    //_cr.DrawRectangle(0, 0, pixel_width, pixel_height);
                }
                else
                {
                    if (img.SMaskInData != 0 || img.Mask != null)
                    {
                        //There can be alpha information in J2K images. It does
                        //not get drawn automatically (though pixels with 0
                        //in alpha is at least dropped)

                        //In any case we must drawn the alpha. Like with masks
                        //we start by creating a group for alpha
                        _cr.PushGroupAlpha();

                        //Then we draw the image into that group. Only the alpha
                        //data will be kept in this case
                        _cr.DrawImage(bs, 0, 0, pixel_width, pixel_height);

                        //We make the group into a pattern
                        using (var pat = _cr.PopGroup())
                        {
                            //Then we create a new group for where we
                            //will draw the blended image
                            //_cr.PushGroup();

                            //MaskImage works over the entire surface, which is
                            //why we use a group for this.
                            _cr.MaskImage(bs, pat);
                        }

                        //Finally we set the masked group as source and draws it.
                        //_cr.PopGroupAndSetSource();
                        //_cr.DrawRectangle(0, 0, pixel_width, pixel_height);
                    }
                    else
                    {
                        _cr.DrawImage(bs, 0, 0, pixel_width, pixel_height);
                    }
                }
                _cr.Pop(); // Pops mt
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

            //Sets state back to defaults
            ResetState();

            //Pushes the form's matrix onto the stack,
            //it's effectivly the "to device space"
            //matrix
            _cr.Transform(form.Matrix);

            //A form's coordinate system is sepperate
            //from the page. The to/from device space
            //matrixes are used to transform points to
            //userspace, so be setting them to CTM one
            //effectivly makes userspace the current CTM
            var to_device_space = _to_device_space;
            _to_device_space = _cr.CTM;
            var from_device_space = _from_device_space;
            _from_device_space = _to_device_space;
            _from_device_space.Invert();


            //Pushes the clip after the form.Matrix, meaning the BBox
            //coordinates lies in the form's coordinate system.
            _cr.RectangleAt(form.BBox);
            _cr.Clip();

            //Renders the form. No try/catch is needed.
            ((IDraw) this).Executor.Execute(form.Commands, this);

            //Restores state back to what it was before execution.
            Restore();
            _from_device_space = from_device_space;
            _to_device_space = to_device_space;
        }

        #endregion

        #region Path construction

        /// <summary>
        /// Starts a path from the given point.
        /// </summary>
        public void MoveTo(double x, double y)
        {
            _current_point = new xPoint(x, y);

            _cr.MoveTo(x, y);
        }

        /// <summary>
        /// Draws a line to the given point
        /// </summary>
        public void LineTo(double x, double y)
        {
            _cr.LineTo(x, y);

            _current_point = new xPoint(x, y);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            _current_point = new xPoint(x3, y3);
            _cr.CurveTo(x1, y1, x2, y2, x3, y3);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x3, double y3)
        {
            _current_point = new xPoint(x3, y3);
            _cr.CurveTo(x1, y1, x3, y3, x3, y3);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            //Could use _current_point, but I'm considering dropping it.
            var point = _cr.CurrentPoint;
            _cr.CurveTo(point.X, point.Y, x2, y2, x3, y3);
            _current_point = new xPoint(x3, y3);
        }

        /// <summary>
        /// Draws a rectangle as a new figure/subpath.
        /// </summary>
        public void RectAt(double x, double y, double width, double height)
        {
            //After a path is closed the current point is moved back to
            //the start of the path. 
            _current_point = new xPoint(x, y);

            _cr.RectangleAt(x, y, width, height);
        }

        public void ClosePath()
        {
            _cr.ClosePath();
            _current_point = _cr.CurrentPoint;
        }

        public void DrawClip(xFillRule fr)
        {
            SetClip(fr);
            DrawPath(_clip_rule, false, false, false);
        }

        public void DrawPathNZ(bool close, bool stroke, bool fill)
        {
            DrawPath(xFillRule.Nonzero, close, stroke, fill);
        }

        //In retrospect, it's pointless to have sepperate DrawPathEO/NZ
        public void DrawPathEO(bool close, bool stroke, bool fill)
        {
            DrawPath(xFillRule.EvenOdd, close, stroke, fill);
        }

        private void DrawPath(xFillRule fr, bool close, bool stroke, bool fill)
        {
            _cr.FillRule = fr == xFillRule.EvenOdd ? Cairo.Enums.FillRule.EVEN_ODD : Cairo.Enums.FillRule.WINDING;

            if (close)
                _cr.ClosePath();

            //Only one pattern can be rendered at the time, so splitting up
            //the draws.
            if (fill)
            {
                if (_cs.fillCS is CSPattern)
                    FillPattern();
                else
                    _cr.SetColor(_cs.fill.R, _cs.fill.G, _cs.fill.B);

                if (stroke || _clip)
                    _cr.FillPreserve();
                else
                    _cr.Fill();
            }

            if (stroke)
            {
                if (_cs.strokeCS is CSPattern)
                    StrokePattern();
                else
                    _cr.SetColor(_cs.stroke.R, _cs.stroke.G, _cs.stroke.B);

                if (_clip)
                    _cr.StrokePreserve();
                else
                    _cr.Stroke();
            }

            if (_clip)
            {
                _cr.FillRule = _clip_rule == xFillRule.EvenOdd ? Cairo.Enums.FillRule.EVEN_ODD : Cairo.Enums.FillRule.WINDING; ;
                _cr.Clip();
                _clip = false;
            }
        }

        /// <remarks>
        /// </remarks>
        private void FillPattern()
        {
            var cspat = (CSPattern)_cs.fillCS;
            if (cspat.Pat == null && cspat.CPat == null) return;

            //Get the bounding box of the geometry being drawn as
            //it's a good bit easier to work with rectangles.
            xRect outline = _cr.FillBounds;

            //We then transform the bounds down to the device level
            outline = _cr.Transform(outline);

            //However that was a little too far, so we transform
            //the bounds up to the user space again.
            outline = _from_device_space.Transform(outline);

            //When not preserving one could just clip here instead,
            //but to simplify the code we do the same regardless. I.e.
            //we make a copy of the figure to be drawn so that we
            //can draw it later.
            var path = _cr.CopyPath();
            _cr.NewPath();

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

            //The pattern is drawn onto its own surface.
            _cr.PushGroup();
            bool has_pushed = true;

            try
            {
                //Cairo is easier than WPF here. Instead of a inverse matrix we simply
                //set the CTM to identity. Weeell not identity then. Cairo patterns are
                //apparently not affected directly by the CTM (the CTM used when drawing
                //the pattern, not the one used when creating the pattern).
                //
                //So we set this matrix so that the pattern will be flipped and streached
                //as needed during creation.
                _cr.CTM = _to_device_space;

                if (cspat.Pat != null)
                {
                    //Renders the pattern. This rendering is independent from how the
                    //page is scaled/rotated/etc through the CTM (it's not independent
                    //from the rotation property on a page object though)
                    Render(cspat.Pat, outline);

                    _cr.PopGroupAndSetSource();
                    has_pushed = false;
                }
                else
                {
                    //Tiling pattern.
                    var pat = cspat.CPat;

                    //Save state data.
                    Save();

                    //Sets color for uncolored patterns
                    if (cspat.CS != null)
                    {
                        _cs.fillCS = cspat.CS;
                        _cs.strokeCS = _cs.fillCS;
                        _cs.stroke = _cs.fill;
                    }
                    //Sets default color spaces. Todo: default color
                    else
                    {
                        _cs.fillCS = DeviceGray.Instance;
                        _cs.strokeCS = _cs.fillCS;
                    }

                    //Saves non-state related data
                    var device_space = _from_device_space;
                    var to_device_space = _to_device_space;
                    bool b_clip = _clip; var clip_rule = _clip_rule;
                    
                    //This stuff I'm a little unsure of. How much state must be restored?
                    //todo: Test: try setting colors, width, etc, and draw a tiling pattern
                    //      that makes use of it. 
                    ResetState();
                    _clip = false;

                    //Renders the tile straight onto the page.
                    try { Render(pat, outline); }
                    catch (Exception e)
                    { Debug.WriteLine("DrawDC.FillPattern:" + e.Message); throw; }
                    finally
                    {
                        _from_device_space = device_space;
                        _to_device_space = to_device_space;
                        Restore();

                        //Restores non-state related data
                        _clip = b_clip; _clip_rule = clip_rule;
                    }

                    //Sets the rendered data as a source pattern
                    _cr.PopGroupAndSetSource();
                    has_pushed = false;
                }
            }
            catch (Exception) { path.Dispose(); throw; }
            finally
            {
                //In a finaly clause to prevent the dc from ending up in an inconsistant
                //state if something happens during rendering.

                //Pops the identity CTM 
                if (has_pushed)
                    _cr.Pop();
            }

            //Restores the path
            _cr.AppendPath(path);
            path.Dispose();
        }

        /// <summary>
        /// This function is very similar to "FillPattern", but I've split them
        /// up as it makes the code easier to read.
        /// </summary>
        /// <param name="gem">Geometry to render into</param>
        /// <param name="has_transform">
        /// If the geometry has a transform. Don't put this transform
        /// on the _cs.CTM
        /// </param>
        private void StrokePattern()
        {
            var cspat = (CSPattern)_cs.strokeCS;
            if (cspat.Pat == null && cspat.CPat == null) return;

            //Get the bounding box of the geometry being drawn as
            //it's a good bit easier to work with rectangles.
            xRect outline = _cr.StrokeBounds;

            //We then transform the bounds down to the device level
            outline = _cr.Transform(outline);

            //However that was a little too far, so we transform
            //the bounds up to the user space again.
            outline = _from_device_space.Transform(outline);

            //We set the current path as the clip path.
            //Hmm, Cairo does not support stroked clip paths.
            //Must do things a bit different from WPF here then.
            //
            //First we save the current path away so that it can
            //be used later.
            var path = _cr.CopyPath();

            //We don't want any left over path when we render the pattern.
            _cr.NewPath();
            
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

            //We tell cairo that we're going to render to
            //a sepperate group
            _cr.PushGroup();
            bool has_pushed = true;

            try
            {
                //Cairo is easier than WPF here. Instead of a inverse matrix we simply
                //set the CTM to identity. Weeell not identity then. Cairo patterns are
                //apparently not affected directly by the CTM (the CTM used when drawing
                //the pattern, not the one used when creating the pattern).
                //
                //So we set this matrix so that the pattern will be flipped and streached
                //as needed during creation.
                _cr.CTM = _to_device_space;

                if (cspat.Pat != null)
                {
                    //Renders the pattern. This rendering is independent from how the
                    //page is scaled/rotated/etc through the CTM (it's not independent
                    //from the rotation property on a page object though)
                    Render(cspat.Pat, outline);

                    _cr.PopGroupAndSetSource();
                    has_pushed = false;
                }
                else
                {
                    //Tiling pattern.
                    var pat = cspat.CPat;

                    //Save state data. Consider putting these in a struct to ease
                    //save/restore.
                    Save();
                    var device_space = _from_device_space;
                    var to_device_space = _to_device_space;
                    bool b_clip = _clip; var clip_rule = _clip_rule;

                    //Sets color for uncolored patterns
                    if (cspat.CS != null)
                    {
                        _cs.strokeCS = cspat.CS;
                        _cs.fillCS = _cs.strokeCS;
                        _cs.fill = _cs.stroke;
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

                    //This stuff I'm a little unsure of. Need to test
                    //this.
                    ResetState();
                    _clip = false;

                    try
                    {
                        Render(pat, outline);
                    }
                    finally
                    {
                        //Restores the state
                        _from_device_space = device_space;
                        _to_device_space = to_device_space;
                        Restore();
                        _clip = b_clip; _clip_rule = clip_rule;
                    }

                    //Sets the rendered data as a source pattern
                    _cr.PopGroupAndSetSource();
                    has_pushed = false;
                }
            }
            catch (Exception) { path.Dispose(); throw; }
            finally
            {
                //In a finaly clause to prevent the dc from ending up in an inconsistant
                //state if something happens during rendering.

                //Pops the identity CTM 
                if (has_pushed)
                    _cr.Pop();
            }

            //Restores the path
            _cr.AppendPath(path);
            path.Dispose();
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
        internal void Render(CompiledPattern pat, xRect bounds)
        {
            //Pushes on the transform of the pattern. It's possibly
            //a better idea to set the inverse this matrix on the
            //pattern itself, but then it will have to be done by
            //the calling function.
            var m = new cMatrix(pat.Matrix);
            _cr.Transform(m);

            //Moves bounds into the pattern space
            m.Invert();
            bounds = m.Transform(bounds);

            //Gets the clip to a pattern cell's bounding box
            var bbox = new xRect(pat.BBox);

            //How much to move for each draw call
            double xstep = pat.XStep, ystep = pat.YStep;

            //Can be negative.
            //Note: Must substract "bbox.Right", as the size of the figure being
            //      drawn can be bigger than xstep. (paints.pdf got examples of this)
            //      bbox.Left is less important as only some extra drawing will
            //      incure without it (though this isn't propperly tested)
            int x_min_pos = (int)Math.Ceiling((bounds.Left - bbox.Right) / xstep);
            int x_max_pos = (int)Math.Floor((bounds.Right - bbox.Left) / xstep);
            int y_min_pos = (int)Math.Ceiling((bounds.Bottom - bbox.Top) / ystep);
            int y_max_pos = (int)Math.Floor((bounds.Top - bbox.Bottom) / ystep);

            //Creates a clip path
            _cr.RectangleAt(bbox);
            using (var clip = _cr.CopyPath())
            {

                //In any case, the xy_min/max pos contains the ammount of titles one
                //have to draw on the two axis. Drawing every title is quite inefficient
                //though, so one could replace _dc with a dc from a visual brush. Render
                //one tile, and then use the tile feature of the visual brush... but 
                //tile patterns are hardly ever used and redrawing is easy.

                cMatrix translate;
                for (int y = y_min_pos; y <= y_max_pos; y++)
                {
                    translate = cMatrix.Identity;
                    translate.Translate(xstep * x_min_pos, ystep * y);
                    for (int x = x_min_pos; x <= x_max_pos; x++)
                    {
                        Save();

                        //Set up CTM for a single cell
                        _cr.Transform(translate);
                        _cr.AppendPath(clip);
                        _cr.Clip();

                        //This is important when rendering patterns
                        //inside paterns, as they view the pattern
                        //as the "user space"
                        _from_device_space = _cr.CTM;
                        _to_device_space = _from_device_space;
                        _from_device_space.Invert();

                        //Runs the pattern
                        ((IDraw)this).Executor.Execute(pat.Commands, this);

                        Restore();

                        //Moves to the next step
                        translate.Translate(xstep, 0);
                    }
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
        public void Render(PdfShadingPattern pat, xRect bounds)
        {
            var shade = pat.Shading;

            //AA is only supported by the axial shader
            _cr.Antialias = (shade is PdfAxialShading && shade.AntiAlias) ? Cairo.Enums.AntialiasMethod.DEFAULT : Cairo.Enums.AntialiasMethod.NONE;

            //The pattern may have its own coordinate system. I've not done a
            //lot of experimenting with this, but as far as I can tell the
            //COORDS* array is in the pattern's coordinate system, and all drawing
            //calls are to be in the pattern's coordinate system.
            // *(Specs say "target coordinate space", without elaborating what
            //   the target is)
            var m =  new cMatrix(pat.Matrix);
            _cr.Transform(m);

            //The bounds rectangle also needs to be inversly transformed 
            //from the "page space" to the "pattern space".
            m.Invert();
            bounds = m.Transform(bounds);

            //Paints the backround color. This must be done before pushing
            //the BBox clip. This is not to be done when using the sh
            //operator, and isn't quite right for the transparent rendering
            //model.
            var back = shade.Background;
            if (back != null)
            {
                _cr.SetColor(back.ToDblColor());
                _cr.DrawRectangle(bounds);
            }

            //The pattern is clipped to its bounding box
            var bbox = shade.BBox;
            if (bbox != null)
            {
                _cr.RectangleAt(new xRect(bbox));
                _cr.Clip();
            }

            //Then one can finally run the shader
            if (shade is PdfAxialShading)
            {
                xVector px;
                if (_cr.Antialias != Cairo.Enums.AntialiasMethod.NONE)
                {
                    px = _cr.TransformFromDeviceDistance(new xVector(1, 1));
                }
                else px = new xVector();
                rCairoPattern.RenderShade((PdfAxialShading)shade, _cr, bounds, px);
            }
            else if (shade is PdfRadialShading)
                rCairoPattern.RenderShade((PdfRadialShading)shade, _cr, bounds);
            else if (shade is PdfFunctionShading)
                rCairoPattern.RenderShade((PdfFunctionShading)shade, _cr, bounds);
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            _clip = true;
            _clip_rule = rule;
        }

        #endregion

        #region Text objects

        /// <summary>
        /// Sets the TM back to identity
        /// </summary>
        public void BeginText()
        {
            //_text_mode = true;
            _tm.Tm = cMatrix.Identity;
            _tm.Tlm = cMatrix.Identity;
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
                    var clip_group = _clip_mask.Geometry;
                    if (_clip_mask.ClipDV)
                    {
                        if (clip_group != null)
                            _cr.Save();
                    }
                    if (clip_group != null)
                    {
                        _cr.NewPath();
                        foreach (var clip in clip_group)
                            _cr.AppendPath(clip);
                        _cr.Clip();
                        foreach (var clip in clip_group)
                            clip.Dispose();
                    }
                    if (_clip_mask.ClipDV)
                    {
                        if (clip_group != null)
                        {
                            //Bug: Overdraws
                            _cr.SetColor(0, 0, 0);
                            _cr.Fill();
                            _cr.Restore();
                        }
                        CairoLib.cPattern t3_alpha = _cr.PopGroup();
                        _cr.SetMask(t3_alpha, true);
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

            _cs.ts.Tf = font.Realize<CairoPath>(this);
            _cs.ts.Tfs = size;
        }

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
            _tm.Tm = new cMatrix(m);
            _tm.Tlm = _tm.Tm;
        }

        /// <summary>
        /// Translates the TML and sets it to TM
        /// </summary>
        public void TranslateTLM(double x, double y)
        {
            var m = cMatrix.Identity;
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
                //Todo: Clipping must be done at ET
                //
                //Idea:
                //When the TrackShape functionalty is down, it can be used
                //to find the shape of the drawn T3 glyphs. One may need
                //to do a PushGroup on the shape cairo, though.
                //
                //Then one can "nerf" drawing by pushing a grup on the _cr,
                //in the cases where (_cs.ts.Fill || _cs.ts.Stroke) == false

                if (_cs.ts.Clip)
                {
                    _cr.PushGroupAlpha();
                }
                var clip_mask = _clip_mask;

                #endregion

                DrawT3String(text.ByteString);

                #region Clips
                _clip_mask = clip_mask;

                //Draws the full string as a big image when clipping.
                if (_cs.ts.Clip)
                {
                    var t3_alpha = _cr.PopGroup();
                    if (_cs.ts.Fill || _cs.ts.Stroke)
                    {
                        _cr.Save();
                        _cr.SetPattern(t3_alpha);
                        _cr.Paint();
                        _cr.Restore();
                    }
                    _cr.SetMask(t3_alpha, true);
                }

                #endregion
            }
            else
            {
                #region Init for clipping

                //Clipping must be done as a single opperation, so collecting all
                //the geometry into this group.
                List<cPath> clip_group = null;
                var bs = text.ByteString;
                if (_cs.ts.Clip)
                {
                    if (_clip_mask == null)
                        _clip_mask = new ClipMask();

                    if (_clip_mask.Geometry == null)
                        _clip_mask.Geometry = new List<cPath>(bs.Length);

                    clip_group = _clip_mask.Geometry;
                }

                #endregion

                DrawString(bs, clip_group);

                #region Clips

                //Done in ET

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
        /// "Clip" can be copied straight back into the draw strings methods 
        /// (at the start and end) as they were copied unmodified out.
        /// </remarks>
        public void DrawString(object[] text)
        {
            var v = (_cs.ts.Tf != null) ? _cs.ts.Tf.Vertical : false;
            if (_cs.ts.Tf is Type3Font)
            {
                #region Init for clipping
                //Todo: Clipping must be done at ET
                //
                //Idea:
                //When the TrackShape functionalty is down, it can be used
                //to find the shape of the drawn T3 glyphs. One may need
                //to do a PushGroup on the shape cairo, though.
                //
                //Then one can "nerf" drawing by pushing a grup on the _cr,
                //in the cases where (_cs.ts.Fill || _cs.ts.Stroke) == false


                if (_cs.ts.Clip)
                {
                    _cr.PushGroupAlpha();
                }
                var clip_mask = _clip_mask;

                #endregion

                #region Draws T3 strings

                for (int c = 0; c < text.Length; c++)
                {
                    var o = text[c];
                    if (o is PdfString)
                        DrawT3String(((PdfString)o).ByteString);
                    else if (o is double)
                    {
                        if (v)
                            _tm.Tm.TranslatePrepend(0, (double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th);
                        else
                            _tm.Tm.TranslatePrepend((double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th, 0);
                    }
                    else
                    {
                        //Todo: Logg error
                        throw new PdfNotSupportedException();
                    }
                }

                #endregion

                #region Clips
                _clip_mask = clip_mask;

                //Draws the full string as a big image when clipping.
                if (_cs.ts.Clip)
                {
                    var t3_alpha = _cr.PopGroup();
                    if (_cs.ts.Fill || _cs.ts.Stroke)
                    {
                        _cr.Save();
                        _cr.SetPattern(t3_alpha);
                        _cr.Paint();
                        _cr.Restore();
                    }
                    _cr.SetMask(t3_alpha, true);
                }

                #endregion
            }
            else
            {
                #region Init for clipping

                //Clipping must be done as a single opperation, so collecting all
                //the geometry into this group.
                List<cPath> clip_group = null;
                if (_cs.ts.Clip)
                {
                    if (_clip_mask == null)
                        _clip_mask = new ClipMask();

                    if (_clip_mask.Geometry == null)
                        _clip_mask.Geometry = new List<cPath>(20);

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
                            _tm.Tm.TranslatePrepend(0, (double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th);
                        else
                            _tm.Tm.TranslatePrepend((double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th, 0);
                    }
                    else
                    {
                        //Todo: Logg error
                        throw new PdfNotSupportedException();
                    }
                }

                #endregion

                #region Clips

                //Done in ET

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
        private void DrawString(byte[] str, List<cPath> clip_group)
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

            _cr.FillRule = Cairo.Enums.FillRule.EVEN_ODD;

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th;

            //The glyphs will be scaled up to the font
            //size using this matrix
            cMatrix scale = new cMatrix(fsize * th, 0, 0, fsize, 0, _cs.ts.Tr);
            cMatrix tm;

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
                var glyph = (rGlyphCairo) glyps[c];

                if (_cs.ts.Clip || _cs.ts.Fill || _cs.ts.Stroke)
                {
                    //Fetches the Tm struct and prepends the
                    //scale matrix.
                    tm = _tm.Tm.Clone();
                    tm.Prepend(scale); //Scales text up to font size
                    tm.Prepend(glyph.Transform); //Scales text down to 1x1

                    //Moves the glyph to it's origin.
                    tm.Translate(-glyph.Origin.X * fsize, -glyph.Origin.Y * fsize);

                    //Outlines to draw, there can be more than one
                    var outlines = glyph.Outlines;

                    //Problem, the glyph is to be scaled with text metrix, but strokes
                    //are not to be scaled. For WPF the workaround is to attach the
                    //transform to the geometry object, however in Cairo I do not
                    //know how to achive this.
                    //
                    //One potential workaround is to retrive the path, and then scale
                    //each point.
                    var cg = new CairoGeometry(outlines) { Transform = tm };

                    //I'm not sure what should happen if a composite glyph has overlapping
                    //glyphs. Todo: Create a composite glyph with overlapping subglyphs
                    //and test this.
                    _cr.NewPath();
                    cg.Append(_cr);

                    if (_reset_text_bounds != 0)
                    {
                        var bounds = _cs.ts.Stroke ? _cr.StrokeBounds : _cr.FillBounds;
                        bounds = _cr.Transform(bounds);
                        _glyph_bounds = _glyph_bounds != null ? _glyph_bounds.Value.Enlarge(bounds) : bounds.Normalized;
                    }

                    //Fills are drawn before strokes, as the stroke will overlap
                    //part of the fill.
                    if (_cs.ts.Fill)
                    {
                        if (_cs.fillCS is CSPattern)
                            FillPattern();
                        else
                            _cr.SetColor(_cs.fill.R, _cs.fill.G, _cs.fill.B);

                        if (_cs.ts.Stroke || _cs.ts.Clip)
                            _cr.FillPreserve();
                        else
                            _cr.Fill();
                    }

                    //Strokes are tricker than fills. The pen's stroke width
                    //must lay in the correct coordinate system. The width of
                    //strokes are always in relation to CTM, so we take care
                    //to render on the "current transform matrix" level.
                    if (_cs.ts.Stroke)
                    {
                        if (_cs.strokeCS is CSPattern)
                            StrokePattern();
                        else
                            _cr.SetColor(_cs.stroke.R, _cs.stroke.G, _cs.stroke.B);

                        if (_cs.ts.Clip)
                            _cr.StrokePreserve();
                        else
                            _cr.Stroke();
                    }
                    
                    if (_cs.ts.Clip)
                    {
                        clip_group.Add(_cr.CopyPath());
                    }
                }

                if (font.Vertical)
                {
                    double ay = glyph.MoveDist.Y * fsize + _cs.ts.Tc;
                    if (glyph.IsSpace) ay += _cs.ts.Tw;
                    _tm.Tm.TranslatePrepend(0, ay);
                }
                else
                {
                    //Calcs next glyph position (9.4.4)
                    double ax = glyph.MoveDist.X * fsize + _cs.ts.Tc;
                    if (glyph.IsSpace) ax += _cs.ts.Tw;

                    //Adjusts the position so that the next glyphis drawn after
                    //this glyph.
                    _tm.Tm.TranslatePrepend(ax * th, 0);
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
        ///         
        /// Shape:
        ///  Now that I've gotten to the point of impl. clipping I
        ///  see that the whole "render the glyph to an image" step
        ///  isn't nessesary or sensible. Instead impl. a shape
        ///  tracking feature to Cairo, and use that as the alpha mask.
        /// </remarks>
#if UseString
        private void DrawT3String(string str)
#else
        private void DrawT3String(byte[] str)
#endif
        {
#if DEBUG_
            //Renders each glyph onto its own bitmap, then draws that bitmap. 
            //Could be usefull for implementing caching of glyph bitmaps.
            var font = (Type3Font)_cs.ts.Tf;

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th, tr = _cs.ts.Tr;

            //The glyphs will be scaled up to the font
            //size using this matrix
            cMatrix scale = new cMatrix(fsize, 0, 0, fsize * th, 0, tr);
            TextMetrix tm;

            //Type 3 fonts have their own coordinate system.
            var font_matrix = new cMatrix(font.FontMatrix);

            //Gets the font to creates the glyphs. Later the font will
            //cache bitmaps. Or perhaps the glyps will. Whatever.
            var glyps = font.GetGlyphs(str);

            //Renders all glyphs into bitmaps.
            for (int c = 0; c < glyps.Length; c++)
            {
                //Gets a single glyph to draw
                var glyph = glyps[c];

                if (_cs.ts.Clip || _cs.ts.Fill || _cs.ts.Stroke)
                {
                    //T3 glyphs are rendered onto their own surface
                    _cr.PushGroupWithAlpha();
                    _state_stack.Push(_cs);
                    

                    //Fetches the Tm struct and prepends the
                    //scale matrix.
                    tm = _tm.Clone();
                    _tm.Tm.Prepend(scale); //Scales text up to font size

                    //Unlike with normal font rendering, the TM must be
                    //added to the CTM. This so line scaling will be
                    //correct. (Of course, only line scaling that takes
                    //device space into account needs this, and that
                    //scaling is forced off. Todo: Look into how Adobe
                    //handles "automatic linewidth adjustment inside
                    //type 3 fonts (and patterns and forms.)
                    _cr.Transform(_tm.Tm);

                    //The font has its own coordinate system. Adds it.
                    _cr.Transform(font_matrix);

                    //The "device space" matrix needs to be updated.
                    //for the sake of the line scaling.
                    var page_level = _from_device_space;
                    _from_device_space = _cr.CTM;
                    _from_device_space.Invert();

                    //Prepeares to render the glyph to a drawing visual
                    var executor = _executor;
                    xRect device_bounds;
                    try
                    {
                        //Problem: The normal executor does not keep track of the
                        //size of drawing commands. By using a bounds executor we
                        //get arround that, with the loss of any "abort thread"
                        //functinality, but glyphs are usually quick to render so
                        //that should be axceptable.
                        var be = new BoundsExecutor(_cr);

                        //By setting this we insure that forms and such use the
                        //bounds executor
                        _executor = be;
                        _executor.Execute(glyph.Commands, this);

                        //We now have the device space render bounds
                        device_bounds = be.DeviceBounds;
                    }
                    catch (Exception)
                    {
                        _cr.PopGroup().Dispose();
                        throw;
                    }
                    finally 
                    { 
                        _executor = executor;
                        _cs = _state_stack.Pop();
                        _tm = tm;
                        _clip = false;
                        _from_device_space = page_level; 
                    }

                    if (_reset_text_bounds != 0)
                    {
                        _glyph_bounds = _glyph_bounds != null ? _glyph_bounds.Enlarge(device_bounds) : device_bounds;
                    }

                    using (var pat = _cr.PopGroup())
                    {
                        #region Renders the glyph into a bitmap

                        if (glyph.Commands.Length == 0)
                        {
                            //An empty T3 glyfs will cause dv.Drawing and dv.Transform to be null. In that case Adobe
                            //X skips the glyph in it's entierty

                            //Sets back the device space matrix
                            pat.Dispose();
                            _from_device_space = page_level;
                            continue;
                        }

                        #region Adjust image to render at device resolution

                        //Cairo does not support invertible matrixes, so we can assume that the matrix
                        //is invertible. We move the device bounds into user coordinates
                        var user_bounds = _cr.TransformFromDevice(device_bounds);

                        //The width and height of the rendered drawing should be set to the device widts/height, as we
                        //wish to render at that resolution.
                        int width = (int)Math.Ceiling(Math.Abs(device_bounds.Width));
                        int height = (int)Math.Ceiling(Math.Abs(device_bounds.Height));

                        //The drawing must be scaled up to this resolution
                        var scale_x = width / user_bounds.Width;
                        var scale_y = height / user_bounds.Height;

                        #endregion

                        rImage image;
                        using (var bitmap = _cr.CreateNewImage(width, height))
                        {
                            //Scales up the drawing
                            bitmap.Transform(new cMatrix(scale_x, scale_y));

                            //When scaling the drawing will move, so we need to compensate
                            //for that movment
                            bitmap.Transform(new cMatrix(1, 0, 0, 1, -user_bounds.LowerLeft.X, -user_bounds.LowerLeft.Y));
                            bitmap.SetPattern(pat);
                            
                            //The we draw the pattern at the drawing's original position. The drawing will then be scaled
                            //and moved so that it covers the entire image.
                            bitmap.RectangleAt(user_bounds);
                            bitmap.Fill();

                            //This isn't entierly optimal. It's possible to avoid this memcopy through use of pointers.
                            //What the "DrawImage" function does is in essence to convert this byte[] into a pointer.
                            image = bitmap.GetImage();
                        }

                        #endregion

                        if (!_cs.ts.Clip)
                        {
                            //The image must be scaled down to fit into the drawing rectangle. This is not done
                            //automatically.
                            _cr.PushTransform(new PdfLib.Render.CairoLib.cMatrix(1 / scale_x, 0, 0, 1 / scale_y, user_bounds.LowerLeft.X, user_bounds.LowerLeft.Y));

                            //Finally we draw the full image at it's scaled position. (Note that the transform above translate the image
                            //to its drawing position. Alternativly one could supply this function with user_bounds.ll.x * scale_x and ur.ll.y * scale_y.
                            _cr.DrawImage(image, 0, 0, width, height);
                            
                            _cr.Pop();
                        }
                        else
                        {
                            //Experimental code to see if I'm on the right track.
                            _cr.PushTransform(new PdfLib.Render.CairoLib.cMatrix(1 / scale_x, 0, 0, 1 / scale_y, user_bounds.LowerLeft.X, user_bounds.LowerLeft.Y));
                            _cr.PushGroupAlpha();

                            //_cr.DrawImage(image, 0, 0, width, height);
                            _cr.MoveTo(0, 0);
                            _cr.LineTo(width / 2, height);
                            _cr.LineTo(width, height / 2);
                            _cr.ClosePath();
                            _cr.SetColor(1, 1, 0);
                            _cr.Fill();

                            using (var alpha_mask = _cr.PopGroup())
                            {
                                _cr.RectangleAt(0, 0, width, height);
                                _cr.SetColor(1, 0, 0);
                                _cr.Mask(pat);
                            }
                            _cr.Pop();
                        }
                    }
                }

                //Calcs next glyph position
                double ax = glyph.MoveX * font_matrix.M11 * fsize + _cs.ts.Tc;
                if (glyph.IsSpace) ax += _cs.ts.Tw;

                //Adjusts the position so that the next glyphis drawn after
                //this glyph.
                _tm.Tm.TranslatePrepend(ax * th, glyph.MoveY * font_matrix.M11);
            }
#else
            var font = (Type3Font)_cs.ts.Tf;

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th, tr = _cs.ts.Tr;

            //The glyphs will be scaled up to the font
            //size using this matrix
            cMatrix scale = new cMatrix(fsize, 0, 0, fsize * th, 0, tr);
            TextMetrix tm;

            //Type 3 fonts have their own coordinate system.
            var font_matrix = new cMatrix(font.FontMatrix);

            //Gets the font to creates the glyphs. Later the font will
            //cache bitmaps. Or perhaps the glyps will. Whatever.
            var glyps = font.GetGlyphs(str);

            //Renders all glyphs.
            for (int c = 0; c < glyps.Length; c++)
            {
                //Gets a single glyph to draw
                var glyph = glyps[c];

                if (_cs.ts.Clip || _cs.ts.Fill || _cs.ts.Stroke)
                {
                    Save();

                    //Fetches the Tm struct and prepends the
                    //scale matrix.
                    tm = _tm.Clone();
                    _tm.Tm.Prepend(scale); //Scales text up to font size

                    //Unlike with normal font rendering, the TM must be
                    //added to the CTM. This so line scaling will be
                    //correct. (Of course, only line scaling that takes
                    //device space into account needs this, and that
                    //scaling is forced off. Todo: Look into how Adobe
                    //handles "automatic linewidth adjustment inside
                    //type 3 fonts (and patterns and forms.)
                    _cr.Transform(_tm.Tm);

                    //The font has its own coordinate system. Adds it.
                    _cr.Transform(font_matrix);

                    //The "device space" matrix needs to be updated.
                    //for the sake of the line scaling.
                    var page_level = _from_device_space;
                    _from_device_space = _cr.CTM;
                    _from_device_space.Invert();

                    ResetState();

                    //Subtelty: The fill color is to be used for both strokes and
                    //fills. This as one only set a font's color through the fill
                    //property.
                    _cs.stroke = _cs.fill;
                    _cs.strokeCS = _cs.fillCS;

                    //Prepeares to render the glyph to a drawing visual
                    try
                    {
                        ((IDraw)this).Executor.Execute(glyph.Commands, this);
                    }
                    finally
                    {
                        _tm = tm;
                        _clip = false;
                        _from_device_space = page_level;
                        Restore();
                    }
                }

                //Calcs next glyph position
                double ax = glyph.MoveX * font_matrix.M11 * fsize + _cs.ts.Tc;
                if (glyph.IsSpace) ax += _cs.ts.Tw;

                //Adjusts the position so that the next glyphis drawn after
                //this glyph.
                _tm.Tm.TranslatePrepend(ax * th, glyph.MoveY * font_matrix.M11);
            }
#endif
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
        public void SetT3Glyph(double wx, double wy, double llx, double lly, double urx, double ury) { /* Ignored */}

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
            public DblColor fill;

            /// <summary>
            /// Color used for stroking
            /// </summary>
            public DblColor stroke;

            /// <summary>
            /// Graphics state parameters that only affect text
            /// </summary>
            public TextState ts;

            /// <summary>
            /// Thickness of lines
            /// </summary>
            public double line_width;

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
            public void Reset(Cairo cr)
            {
                strokeCS = DeviceGray.Instance;
                fillCS = DeviceGray.Instance;
                line_width = 1;
                cr.LineWidth = line_width;
                cr.LineCap = Cairo.Enums.LineCap.CAIRO_LINE_CAP_BUTT;
                cr.LineJoin = Cairo.Enums.LineJoin.CAIRO_LINE_JOIN_MITER;
                cr.MiterLimit = 10;
                dash_array = new double[0];
                dash_offset = 0;
#if AutoAdjustLikeAdobe
                stroke_adj = true;
#else
                stroke_adj = false;
#endif
                fill = new DblColor(0, 0, 0);
                stroke = fill;
                transfer = null;

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

        class ClipMask
        {
            public List<cPath> Geometry;
            public bool ClipDV;
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
            public cMatrix Tm;

            /// <summary>
            /// Text line metrix
            /// </summary>
            public cMatrix Tlm;

            public TextMetrix Clone()
            {
                TextMetrix tm = new TextMetrix();
                tm.Tm = Tm.Clone();
                tm.Tlm = Tlm.Clone();
                return tm;
            }
        }

        #endregion

        #region IFontFactory

        //RenderEngine IFontFactory.Engine
        //{ get { return RenderEngine.DrawCairo; } }

        FontCache IFontFactory.FontCache { get { return _font_cache; } }

        IGlyphCache IFontFactory.CreateGlyphCache() => new StandardGlyphCache();

        rFont IFontFactory.CreateMemTTFont(PdfLib.Read.TrueType.TableDirectory td, bool symbolic, ushort[] cid_to_gid)
        {
            return new MemTTFont<CairoPath>(td, this, symbolic, cid_to_gid);
        }

        rFont IFontFactory.CreateBuiltInFont(PdfType1Font font, double[] widths, AdobeFontMetrixs afm, bool substituted)
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

                        return rFont.Create<CairoPath>(ttf, widths, font, this);
                    }
                }
            }

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

                    return new TTFont<CairoPath>(cff, widths, font, this);
                }
            }
            if (cff == null)
                throw new NotImplementedException();
            var fd = font.FontDescriptor;
            if (fd == null)
                fd = afm.CreateFontDescriptor();
            return new Type1CFont<CairoPath>(widths, fd, font.Encoding, this, cff, true);
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
                        throw new NotImplementedException();
                    }
                }
            }

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

                    //return new TTFont<StreamGeometry>(cff, widths, font, this);
                }
            }
            if (cff == null)
                throw new NotImplementedException();

            return new CIDT1CFont<CairoPath>((CIDFontType2)font.DescendantFonts, this, font.Encoding.WMode, cff);
        }

        /// <summary>
        /// Attach a Matrix transform to the path
        /// </summary>
        void IGlyphFactory<CairoPath>.SetTransform(CairoPath path, xMatrix transform)
        {
            ((CairoPath)path).Transform = new cMatrix(transform);
        }

        /// <summary>
        /// Creates a path without anything in it
        /// </summary>
        CairoPath IGlyphFactory<CairoPath>.CreateEmptyPath()
        {
            //Todo: Perhaps check if there's a path being constructed
            _cr.NewPath();
            return new CairoPath(_cr.CopyPath());
        }

        rGlyph IGlyphFactory<CairoPath>.CreateGlyph(CairoPath[] outline, xPoint advance, xPoint origin, xMatrix mt, bool freeze, bool is_space_char)
        {
            //var ar = new CairoPath[outline.Length];
            //for (int c = 0; c < ar.Length; c++)
            //    ar[c] = (CairoPath)outline[c];
            return new rGlyphCairo(outline, advance, origin, mt, is_space_char);
        }

        IGlyph<CairoPath> IGlyphFactory<CairoPath>.GlyphRenderer()
        {
            return new GlyphCreator(_cr);
        }
        #endregion

        #region Helper functions

        /// <summary>
        /// Instructs the text drawing routines to track the size of the text.
        /// Note, this method must be paired with "FetchTextBounds"
        /// </summary>
        public void TrackTextBounds()
        {
            _reset_text_bounds++;
        }

        /// <summary>
        /// Fetches the current text bounds, return null if there are none.
        /// </summary>
        public xRect? FetchTextBounds()
        {
            var bounds = _glyph_bounds;
            if (_reset_text_bounds > 0)
                _reset_text_bounds--;
            if (_reset_text_bounds == 0)
                _glyph_bounds = null;
            return bounds;
        }

        class GlyphCreator : IGlyph<CairoPath>, IGlyphDraw
        {
            Cairo _cr;
            bool closed/*, open*/;

            public GlyphCreator(Cairo cr)
            { _cr = cr; }

            public IGlyphDraw Open()
            {
                //Todo: Perhaps check if there's a path being constructed
                _cr.NewPath();
                return this;
            }

            void IGlyphDraw.BeginFigure(xPoint startPoint, bool isFilled, bool isClosed)
            {
                if (/*open && */closed)
                    _cr.ClosePath();
                closed = isClosed;
                //open = true;
                _cr.MoveTo(startPoint.X, startPoint.Y);
            }
            void IGlyphDraw.QuadraticBezierTo(xPoint point1, xPoint point2, bool isStroked, bool isSmoothJoin)
            {
                _cr.CurveTo(point1.X, point1.Y, point2.X, point2.Y);
            }
            void IGlyphDraw.LineTo(xPoint point, bool isStroked, bool isSmoothJoin)
            {
                _cr.LineTo(point.X, point.Y);
            }
            void IGlyphDraw.BezierTo(xPoint point1, xPoint point2, xPoint point3, bool isStroked, bool isSmoothJoin)
            {
                _cr.CurveTo(point1.X, point1.Y, point2.X, point2.Y, point3.X, point3.Y);
            }

            public void Dispose()
            {
                //Todo: Perhaps restore the path being constructed
            }

            public CairoPath GetPath()
            {
                if (/*open && */closed)
                    _cr.ClosePath();
                //open = false;
                closed = false;
                return new CairoPath(_cr.CopyPath());
            }
        }

        /// <summary>
        /// Returns the rendered data in bgra32 format
        /// </summary>
        public byte[] GetRenderedData()
        {
            return _cr.GetRenderedData();
        }

        /// <summary>
        /// Sets a clipping rectangle
        /// </summary>
        /// <remarks>
        /// Don't use this function while a path is being drawn.
        /// </remarks>
        void SetClip(xRect rect)
        {
            _cr.ClipRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private class BoundsExecutor : IExecutor
        {
            public xRect? DeviceBounds = null;
            private static readonly xRect _img = new xRect(0, 0, 1, 1);
            Cairo _cr;
            public BoundsExecutor(Cairo cr) { _cr = cr; }

            void IExecutor.Execute(IExecutable cmds, IDraw renderer)
            {
                Execute(((IExecutableImpl)cmds).Commands, renderer);
            }

            void IExecutor.Execute(object cmds, IDraw renderer)
            {
                Execute((RenderCMD[])cmds, renderer);
            }

            public void Execute(RenderCMD[] cmds, IDraw renderer)
            {
                var dc = renderer as DrawCairo;
                if (dc != null)
                    dc.TrackTextBounds();
                xRect? text_bounds = null;

                try
                {
                    for (int c = 0; c < cmds.Length; c++)
                    {
                        var cmd = cmds[c];
                        var type = cmd.Type;

                        if (type == RenderCMD.CMDType.Path)
                        {
                            bool stroke = cmd is BS_CMD || cmd is B_CMD ||
                                cmd is b_CMD || cmd is bS_CMD || cmd is s_CMD;

                            //User coordinate bounds
                            var bounds = stroke ? _cr.StrokeBounds : _cr.FillBounds;

                            //Transform them down to device space
                            bounds = _cr.Transform(bounds);

                            DeviceBounds = DeviceBounds != null ? DeviceBounds.Value.Enlarge(bounds) : bounds.Normalized;
                        }
                        else if (type == RenderCMD.CMDType.Image)
                        {
                            DeviceBounds = DeviceBounds != null ? DeviceBounds.Value.Enlarge(_cr.Transform(_img)) : _cr.Transform(_img).Normalized;
                        }

                        cmd.Execute(renderer);
                    }
                }
                finally
                {
                    if (dc != null)
                        text_bounds = dc.FetchTextBounds();
                }

                if (text_bounds != null)
                    DeviceBounds = DeviceBounds != null ? DeviceBounds.Value.Enlarge(text_bounds.Value) : text_bounds;
            }
        }

        #endregion
    }
#endif
}
