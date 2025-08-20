using System;
using System.Collections.Generic;
using PdfLib.Pdf;
using PdfLib.Render.Commands;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Compose;
using PdfLib.Compose.Text;
using PdfLib.Render;

namespace PdfLib.Compile
{
    public class PdfAnalyze : IDraw
    {
        #region Variables and properties

        /// <summary>
        /// Precition of real strings
        /// </summary>
        public int Precision
        {
            get { return 16; }
            set { }
        }

        
        bool _is_running = false;

        /// <summary>
        /// Set this false to end execution in a clean manner
        /// </summary>
        public bool Running
        {
            get => _is_running;
            set
            {
                _is_running = value;
                if (((IDraw)this).Executor is StdExecutor std)
                    std.Running = value;
            }
        }

        /// <summary>
        /// Current state
        /// </summary>
        private State _cs = new State();
        Stack<State> _state_stack = new Stack<State>(32);

        /// <summary>
        /// TextMetrix transform
        /// </summary>
        private TextMetrix _tm = new TextMetrix();

        /// <summary>
        /// Will refuse to emit restore commands that pops the stack beyond this point.
        /// </summary>
        private int _restore_threshold = 0;

        private readonly IFontFactory<InfoPath>  _font_factory;

        /// <summary>
        /// Does not track graphic state.
        /// </summary>
        public GS GraphicState { get { return GS.Unknown; } }

        /// <summary>
        /// Cache over fonts and glyphs. 
        /// </summary>
        private FontCache _font_cache = new FontCache();

        #endregion

        #region Execution

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

        #region Init

        /// <summary>
        /// Create a draw page object
        /// </summary>
        /// <param name="page">Page one wants to draw to</param>
        public PdfAnalyze()
        {
            _font_factory = InfoFF.CreateFactory();
            Init();
        }

        public xMatrix CreateToDeviceMatrix(PdfRectangle MediaBox, double output_width, double output_height,
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

            return from_userspace_to_device_coords;
        }

        public void Dispose()
        {

        }

        private void Init()
        {
            Running = true;
            ((IDraw) this).Executor = StdExecutor.STD;
            _cs.block = new Analyze.AnalyzeBlock(false, null);
            _cs.CTM = xMatrix.Identity;
            _cs.cmds = new List<RenderCMD>();
            _cs.Reset();
        }

        void IDraw.PrepForAnnotations(bool init)
        {
            if (init)
            {
                _restore_threshold = 1;
                ((IDraw) this).Save();
            }
            else
            {
                _restore_threshold--;
                while (_state_stack.Count > _restore_threshold)
                    ((IDraw)this).Restore();
            }
        }

        #endregion

        #region General Graphic State

        /// <summary>
        /// Width lines will be stroked with.
        /// Will always return -1
        /// </summary>
        double IDraw.StrokeWidth
        {
            get { return -1; }
            set { _cs.cmds.Add(new w_RND(value)); }
        }

        void IDraw.SetFlatness(double i)
        {
            _cs.cmds.Add(new i_RND(i));
        }

        void IDraw.SetLineJoinStyle(xLineJoin style)
        {
            _cs.cmds.Add(new j_RND((int)style));
        }

        void IDraw.SetLineCapStyle(xLineCap style)
        {
            _cs.cmds.Add(new J_RND((int)style));
        }

        void IDraw.SetMiterLimit(double limit)
        {
            _cs.cmds.Add(new M_RND(limit));
        }

        void IDraw.SetStrokeDashAr(xDashStyle ds)
        {
            _cs.cmds.Add(new d_RND(ds));
        }

        /// <summary>
        /// Sets the graphic state
        /// </summary>
        /// <param name="gstate">The graphic state</param>
        void IDraw.SetGState(PdfGState gstate)
        {
            _cs.cmds.Add(new gs_RND(gstate));
        }

        void IDraw.SetRI(string ri)
        {
            _cs.cmds.Add(new ri_RND(ri));
        }

        #endregion

        #region Special graphics state

        void IDraw.Save()
        {
            var b = new Analyze.AnalyzeBlock(true, null);
            _cs.cmds.Add(b);
            _state_stack.Push(_cs);
            _cs.cmds = new List<RenderCMD>();
            _cs.block = b;
        }

        void IDraw.Restore()
        {
            if (_state_stack.Count > _restore_threshold)
                FinishBlock();
        }

        private void FinishBlock()
        {
            //Detects the q ... Q patterns
            var b = _cs.block;
            if (b.n_imgs == 1)
            {
                var cmd = _cs.cmds;
                int count = cmd.Count;

                //Pattern cm Do
                if (count == 2 && cmd[0] is cm_RND)
                    b.BType = Analyze.BTYPE.SingleImage;
                else if(b.n_forms == 0 && b.n_text == 0)
                {
                    if (b.n_shapes == 1 && cmd.Count > 3)
                    {
                        //Could be a single image with clip
                        //Pattern re ? n cm Do
                        if (cmd[--count] is Analyze.Do_Analyze_CMD && cmd[--count] is cm_RND && cmd[--count] is n_CMD && cmd[0] is re_CMD)
                            b.BType = Analyze.BTYPE.ClippedSingleImage;
                    }
                }
            }

            _cs.block.Close((_cs.cmds.Count > 0) ? _cs.cmds.ToArray() : null, true);
            _cs = _state_stack.Pop();

            var c = _cs.block;
            c.n_imgs += b.n_imgs;
            c.n_forms += b.n_forms;
            c.n_shapes += b.n_shapes;
            c.n_text += b.n_text;
        }

        void IDraw.PrependCM(xMatrix m)
        {
            _cs.cmds.Add(new cm_RND(m));
            _cs.CTM = _cs.CTM.Prepend(m);
        }

        #endregion

        #region Color

        void IDraw.SetFillColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.cmds.Add(new k_CMD(black, yellow, magenta, cyan));
        }
        void IDraw.SetFillColor(double red, double green, double blue)
        {
            _cs.cmds.Add(new rg_CMD(blue, green, red));
        }
        void IDraw.SetFillColor(double gray)
        {
            _cs.cmds.Add(new g_CMD(gray));
        }
        void IDraw.SetFillColorSC(double[] color)
        {
            _cs.cmds.Add(new sc_CMD(color));
        }
        void IDraw.SetFillColor(double[] color)
        {
            _cs.cmds.Add(new scn_CMD(color));
        }

        void IDraw.SetStrokeColor(double cyan, double magenta, double yellow, double black)
        {
            _cs.cmds.Add(new K_CMD(black, yellow, magenta, cyan));
        }
        void IDraw.SetStrokeColor(double red, double green, double blue)
        {
            _cs.cmds.Add(new RG_CMD(blue, green, red));
        }
        void IDraw.SetStrokeColor(double gray)
        {
            _cs.cmds.Add(new G_CMD(gray));
        }
        void IDraw.SetStrokeColorSC(double[] color)
        {
            _cs.cmds.Add(new SC_CMD(color));
        }
        void IDraw.SetStrokeColor(double[] color)
        {
            _cs.cmds.Add(new SCN_CMD(color));
        }

        void IDraw.SetFillCS(IColorSpace cs)
        {
            _cs.cmds.Add(new cs_CMD(cs));
        }

        void IDraw.SetStrokeCS(IColorSpace cs)
        {
            _cs.cmds.Add(new CS_CMD(cs));
        }

        void IDraw.SetFillPattern(PdfShadingPattern pat)
        {
            _cs.cmds.Add(new scn_pattern_CMD(pat));
        }

        /// <summary>
        /// Tiling patterns must be compiled before they can be used
        /// </summary>
        /// <param name="pat">Pattern to compile</param>
        public CompiledPattern CompilePattern(PdfTilingPattern pat)
        {
            var comp = new PdfCompiler();
            return comp.Compile(pat);
        }

        /// <summary>
        /// Forms must be compiled before they can be used
        /// </summary>
        /// <param name="pat">Pattern to compile</param>
        /// <remarks>Perhaps allow a page's resources are a parameter?</remarks>
        public CompiledForm CompileForm(PdfForm form)
        {
            var comp = new PdfCompiler();
            return new CompiledForm(comp.Compile(form.Contents, form.Resources), form.BBox, form.Matrix, form.Group);
        }

        /// <summary>
        /// Set a compiled tiling pattern. Use CompilePattern.
        /// </summary>
        /// <param name="color">Color in the underlying color space (can't
        /// be a pattern)</param>
        /// <param name="pat">The compiled tiling pattern</param>
        void IDraw.SetFillPattern(double[] color, CompiledPattern pat)
        {
            _cs.cmds.Add(new scn_tile_CMD(color, pat));
        }

        /// <summary>
        /// Sets a uncompiled tiling pattern.
        /// </summary>
        void IDraw.SetFillPattern(double[] color, PdfTilingPattern pat)
        {
            _cs.cmds.Add(new scn_raw_tile_CMD(color, pat));
        }

        void IDraw.SetStrokePattern(PdfShadingPattern pat)
        {
            _cs.cmds.Add(new SCN_pattern_CMD(pat));
        }

        void IDraw.SetStrokePattern(double[] color, CompiledPattern pat)
        {
            _cs.cmds.Add(new SCN_tile_CMD(color, pat));
        }

        /// <summary>
        /// Sets a uncompiled tiling pattern.
        /// </summary>
        void IDraw.SetStrokePattern(double[] color, PdfTilingPattern pat)
        {
            _cs.cmds.Add(new SCN_raw_tile_CMD(color, pat));
        }

        #endregion

        #region Shading patterns

        void IDraw.Shade(PdfShading shading)
        {
            _cs.cmds.Add(new sh_CMD(shading));
        }

        #endregion

        #region Inline images

        void IDraw.DrawInlineImage(PdfImage img)
        {
            _cs.cmds.Add(new BI_CMD(img));
        }

        #endregion

        #region XObjects

        void IDraw.DrawImage(PdfImage img)
        {
            //_cs.cmds.Add(new Do_CMD(img));

            //We get the four final rendersize points by
            //transforming them down to the user level
            xPoint p1 = _cs.CTM.Transform(new xPoint(0, 0));
            //xPoint p2 = _cs.CTM.Transform(new xPoint(1, 0));
            //xPoint p3 = _cs.CTM.Transform(new xPoint(0, 1));
            xPoint p4 = _cs.CTM.Transform(new xPoint(1, 1));
            xPoint p2 = new xPoint(p4.X, p1.Y), p3 = new xPoint(p1.X, p4.Y);

            //Then the create a Path that describes the
            //shape of the image
            var ip = new xSegCollection(
                new xSegLine(p1, p3), new xSegLine(p1, p2),
                new xSegLine(p2, p4), new xSegLine(p3, p4));


            //Commonly images are drawn with this pattern:
            // q cm Do Q
            //We don't know if this is the case yet, so we store away the image
            _cs.cmds.Add(new Analyze.Do_Analyze_CMD(img, ip));
            _cs.block.n_imgs++;
        }

        /// <summary>
        /// Draws a form. 
        /// </summary>
        /// <param name="form">Form to draw</param>
        /// <returns>True if the form was drawn, false if this function is unsuported</returns>
        bool IDraw.DrawForm(PdfForm form)
        {
            return false;
        }

        /// <summary>
        /// TODO: Anaylze forms
        /// </summary>
        /// <param name="cform"></param>
        void IDraw.DrawForm(CompiledForm cform)
        {
            _cs.block.n_forms++;

            //First we must save the current state. This creates a new AnalyzeBlock
            ((IDraw) this).Save();

            //Pushes the form's matrix onto the stack,
            //it's effectivly the "to device space"
            //matrix
            _cs.CTM = _cs.CTM.Prepend(cform.Matrix);

            //Pushes the clip after the form.Matrix, meaning the BBox
            //coordinates lies in the form's coordinate system.
            // Note: Current clip is not currently tracked

            //Renders the form. No try/catch is needed.
            ((IDraw) this).Executor.Execute(cform.Commands, this);

            //Grab a ref to the block
            var b = _cs.block;

            //Restore the state
            ((IDraw) this).Restore();

            //Update the block with needed information
            b.BType = Analyze.BTYPE.Form;
            b.tag = cform;
        }

        #endregion

        #region Path construction

        void IDraw.MoveTo(double x, double y) { _cs.cmds.Add(new m_CMD(y, x)); }
        void IDraw.LineTo(double x, double y) { _cs.cmds.Add(new l_CMD(y, x)); }
        void IDraw.CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            _cs.cmds.Add(new c_CMD(y3, x3, y2, x2, y1, x1));
        }
        void IDraw.CurveTo(double x1, double y1, double x3, double y3)
        {
            _cs.cmds.Add(new y_CMD(y3, x3, y1, x1));
        }
        void IDraw.CurveToV(double x2, double y2, double x3, double y3)
        {
            _cs.cmds.Add(new v_CMD(y3, x3, y2, x2));
        }
        void IDraw.RectAt(double x, double y, double width, double height)
        {
            _cs.cmds.Add(new re_CMD(height, width, y, x));
        }

        void IDraw.ClosePath()
        {
            _cs.cmds.Add(new h_CMD());
        }

        void IDraw.DrawClip(xFillRule fr)
        {
            SetClip(fr);
            ((IDraw) this).DrawPathNZ(false, false, false);
        }

        void IDraw.DrawPathNZ(bool close, bool stroke, bool fill)
        {
            _cs.block.n_shapes++;

            if (stroke)
            {
                if (fill)
                {
                    if (close)
                        _cs.cmds.Add(new b_CMD());
                    else
                        _cs.cmds.Add(new B_CMD());
                }
                else
                {
                    if (close)
                        _cs.cmds.Add(new s_CMD());
                    else
                        _cs.cmds.Add(new S_CMD());
                }
            }
            else
            {
                if (fill)
                {
                    if (close)
                        throw new PdfNotSupportedException("Can't close and fill");
                    else
                        _cs.cmds.Add(new f_CMD());
                }
                else
                {
                    if (close)
                        throw new PdfNotSupportedException("Use ClosePath instead");
                    else
                        _cs.cmds.Add(new n_CMD());
                }
            }
        }

        void IDraw.DrawPathEO(bool close, bool stroke, bool fill)
        {
            _cs.block.n_shapes++;

            if (stroke)
            {
                if (fill)
                {
                    if (close)
                        _cs.cmds.Add(new bS_CMD());
                    else
                        _cs.cmds.Add(new BS_CMD());
                }
                else
                {
                    if (close)
                        _cs.cmds.Add(new s_CMD());
                    else
                        _cs.cmds.Add(new S_CMD());
                }
            }
            else
            {
                if (fill)
                {
                    if (close)
                        throw new PdfNotSupportedException("Can't close and fill");
                    else
                        _cs.cmds.Add(new fs_CMD());
                }
                else
                {
                    if (close)
                        throw new PdfNotSupportedException("Use ClosePath instead");
                    else
                        _cs.cmds.Add(new n_CMD());
                }
            }
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            _cs.cmds.Add(new W_CMD(rule));
        }

        #endregion

        #region Text Objects

        void IDraw.BeginText()
        {
            //Counts the number of text blocks
            _cs.block.n_text++;
            //_cs.cmds.Add(new BT_CMD());

            //Creates an analyzeblock for text
            var b = new Analyze.AnalyzeBlock(true, null);
            b.BType = Analyze.BTYPE.TextBlock;
            _cs.cmds.Add(b);

            //Saves away the current block
            _cs.block.tag = _cs.cmds;
            b.tag = _cs.block;

            //Inserts the text block.
            _cs.cmds = new List<RenderCMD>();
            _cs.block = b;

            //Updates state
            _tm.Tm = xMatrix.Identity;
            _tm.Tlm = xMatrix.Identity;
        }

        /// <summary>
        /// End text mode
        /// </summary>
        /// <remarks>Does not count images, shapes and other invalid stuff that is not to be put inside BT/ET</remarks>
        void IDraw.EndText()
        {
            if (_cs.block.BType != Analyze.BTYPE.TextBlock)
                throw new System.NotImplementedException("Ending textmode in an invalid way."); //<- Not sure how common this is
            //_cs.cmds.Add(new ET_CMD());

            var b = _cs.block;
            b.Close(_cs.cmds.ToArray(), true);
            _cs.block = (Analyze.AnalyzeBlock)b.tag;
            b.tag = null;
            _cs.cmds = (List<RenderCMD>)_cs.block.tag;
            _cs.block.tag = null;
        }

        #endregion

        #region Text State

        void IDraw.SetCharacterSpacing(double tc)
        {
            _cs.cmds.Add(new Tc_CMD(tc));
            _cs.ts.Tc = tc;
        }

        void IDraw.SetWordSpacing(double tw)
        {
            _cs.cmds.Add(new Tw_CMD(tw));
            _cs.ts.Tw = tw;
        }

        void IDraw.SetFont(cFont font, double size)
        {
            _cs.cmds.Add(new Tf_BuildCMD(font, size));
            SetFont(font.MakeWrapper(), size);
        }

        void IDraw.SetFont(PdfFont font, double size)
        {
            _cs.cmds.Add(new Tf_CMD(size, font));
            SetFont(font, size);
        }

        public void SetFont(PdfFont font, double size)
        {
            if (_cs.ts.Tf != null)
                _cs.ts.Tf.Dismiss();

            _cs.ts.OrgFont = font;
            _cs.ts.Tf = font.Realize<InfoPath>(_font_factory);
            _cs.ts.Tfs = size;
        }

        void IDraw.SetFont(CompiledFont font, double size)
        {
            _cs.cmds.Add(new Tf_Type3(size, font));

            if (_cs.ts.Tf != null)
                _cs.ts.Tf.Dismiss();

            _cs.ts.Tf = font.Realize();
            _cs.ts.Tfs = size;
            _cs.ts.OrgFont = font.Font;
        }

        void IDraw.SetTextMode(xTextRenderMode mode)
        {
            _cs.cmds.Add(new Tr_CMD((int)mode));

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
        /// The percentage of normal width (default 100)
        /// </summary>
        void IDraw.SetHorizontalScaling(double th)
        {
            _cs.cmds.Add(new Tz_CMD(th));
            _cs.ts.Th = th / 100;
        }

        void IDraw.SetTextLeading(double lead)
        {
            _cs.cmds.Add(new TL_CMD(lead));
            _cs.ts.Tl = lead;
        }

        void IDraw.SetTextRise(double tr)
        {
            _cs.cmds.Add(new Ts_CMD(tr));
            _cs.ts.Tr = tr;
        }

        #endregion

        #region Text positioning

        void IDraw.SetTM(xMatrix m)
        {
            _cs.cmds.Add(new Tm_CMD(m));
            _tm.Tm = m;
            _tm.Tlm = _tm.Tm;
        }

        void IDraw.TranslateTLM(double x, double y)
        {
            _cs.cmds.Add(new Td_CMD(y, x));
            TranslateTLM(x, y);
        }

        void IDraw.TranslateTLM()
        {
            _cs.cmds.Add(new TS_CMD());
            TranslateTLM();
        }

        void IDraw.SetTlandTransTLM(double x, double y)
        {
            _cs.cmds.Add(new TD_CMD(y, x));
            SetTlandTransTLM(x, y);
        }

        /// <summary>
        /// Translates the TML and sets it to TM
        /// </summary>
        internal void TranslateTLM(double x, double y)
        {
            var m = xMatrix.Identity.Translate(x, y);
            _tm.Tlm = _tm.Tlm.Prepend(m);
            _tm.Tm = _tm.Tlm;
        }

        /// <summary>
        /// Move down one line
        /// </summary>
        internal void TranslateTLM()
        {
            TranslateTLM(0, -_cs.ts.Tl);
        }

        internal void SetTlandTransTLM(double x, double y)
        {
            _cs.ts.Tl = -y;
            TranslateTLM(x, y);
        }

        #endregion

        #region Text showing

        void IDraw.DrawString(PdfItem[] str_ar)
        {
            _cs.cmds.Add(new TJ_BuildCMD(str_ar));

            object[] text = new object[str_ar.Length];
            for (int c = 0; c < str_ar.Length; c++)
                if (str_ar[c] is BuildString)
                    text[c] = ((BuildString)str_ar[c]).MakeString();
                else
                    text[c] = str_ar[c].GetReal();

            DrawString(text);
        }
        void IDraw.DrawString(BuildString str)
        {
            var tm = _cs.CTM.Prepend(_tm.Tm);
            xSize size = DrawString(str.MakeString(), false);
            _cs.cmds.Add(new Analyze.Text_Bounds(new Tj_BuildCMD(str), tm.Transform(new xRect(size)), _cs.ts, _cs.block));
        }

        void IDraw.DrawString(PdfString text, double aw, double ac)
        {
            _cs.ts.Tc = ac;
            _cs.ts.Tw = aw;
            TranslateTLM();

            var tm = _cs.CTM.Prepend(_tm.Tm);
            xSize size = DrawString(text);
            _cs.cmds.Add(new Analyze.Text_Bounds(new Tws_CMD(text, ac, aw), tm.Transform(new xRect(size)), _cs.ts, _cs.block));
        }

        void IDraw.DrawString(PdfString text, bool cr)
        {
            var tm = _cs.CTM.Prepend(_tm.Tm);
            xSize size = DrawString(text, cr);

            if (cr)
            {
                _cs.cmds.Add(new Analyze.Text_Bounds(new Tcr_CMD(text), tm.Transform(new xRect(size)), _cs.ts, _cs.block));
            }
            else
            {
                _cs.cmds.Add(new Analyze.Text_Bounds(new Tj_CMD(text), tm.Transform(new xRect(size)), _cs.ts, _cs.block));
            }
        }

        internal xSize DrawString(PdfString text, bool cr)
        {
            if (cr) TranslateTLM();

            return DrawString(text);
        }

        void IDraw.DrawString(object[] text)
        {
            //Position ??
            var tm = _cs.CTM.Prepend(_tm.Tm);
            xSize size = DrawString(text);
            _cs.cmds.Add(new Analyze.Text_Bounds(new TJ_CMD(text), tm.Transform(new xRect(size)), _cs.ts, _cs.block));
        }

        internal xSize DrawString(object[] text)
        {
            var v = (_cs.ts.Tf != null) ? _cs.ts.Tf.Vertical : false;
            double w = 0, h = 0;
            if (_cs.ts.Tf is Type3Font)
            {
                #region Init for clipping

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
                            _tm.Tm = _tm.Tm.TranslatePrepend(0, (double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th);
                        else
                            _tm.Tm = _tm.Tm.TranslatePrepend((double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th, 0);
                    }
                    else
                    {
                        //Todo: Logg error
                        throw new PdfNotSupportedException();
                    }
                }

                #endregion

                #region Clips

                #endregion
            }
            else
            {
                #region Init for clipping

                #endregion

                #region Draws normal strings

                for (int c = 0; c < text.Length; c++)
                {
                    var o = text[c];
                    if (o is PdfString)
                    {
                        var s = DrawString(((PdfString)o).ByteString);
                        if (v)
                        {
                            w = Math.Max(s.Width, w);
                            h += s.Height;
                        }
                        else
                        {
                            w += s.Width;
                            h = Math.Max(s.Height, h);
                        }
                    }
                    else if (o is double)
                    {
                        double d = (double)o / -1000d * _cs.ts.Tfs * _cs.ts.Th;

                        if (v)
                        {
                            _tm.Tm = _tm.Tm.TranslatePrepend(0, d);
                            h += d;
                        }
                        else
                        {
                            _tm.Tm = _tm.Tm.TranslatePrepend(d, 0);
                            w += d;
                        }
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

            return new xSize(w, h);
        }

        private xSize DrawString(PdfString text)
        {
            xSize ret;

            if (_cs.ts.Tf is Type3Font)
            {
                #region Init for clipping
                
                #endregion

                ret = DrawT3String(text.ByteString);

                #region Clips
                
                #endregion
            }
            else
            {
                #region Init for clipping

                var bs = text.ByteString;

                #endregion

                ret = DrawString(bs);

                #region Clips

                //Done in ET

                #endregion
            }

            return ret;
        }

        /// <summary>
        /// Draws characters
        /// </summary>
        private xSize DrawString(byte[] str)
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
                return new xSize();
            }
            #endregion

            #region Init for rendering

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th;

            //Creates the glyphs. An optimilization would be to just fetch the
            //glyps' measurments. 
            var glyps = font.GetGlyphs(str);

            double width = 0, height = 0;
            if (!font.Vertical)
                height = fsize;

            #endregion

            for (int c = 0; c < glyps.Length; c++)
            {
                //Gets a single glyph to draw
                var glyph = (rGlyphInfo)glyps[c];

                if (_cs.ts.Clip || _cs.ts.Fill || _cs.ts.Stroke)
                {
                    
                }

                if (font.Vertical)
                {
                    double ay = glyph.MoveDist.Y * fsize + _cs.ts.Tc;
                    if (glyph.IsSpace) ay += _cs.ts.Tw;
                    _tm.Tm = _tm.Tm.TranslatePrepend(0, ay);
                    height += ay;
                    width = System.Math.Max(width, glyph.MoveDist.X);
                }
                else
                {
                    //Calcs next glyph position (9.4.4)
                    double ax = glyph.MoveDist.X * fsize + _cs.ts.Tc;
                    if (glyph.IsSpace) ax += _cs.ts.Tw;

                    //Adjusts the position so that the next glyphis drawn after
                    //this glyph.
                    ax *= th;
                    _tm.Tm = _tm.Tm.TranslatePrepend(ax, 0);
                    width += ax;
                }
            }

            return new xSize(width, height);
        }

        private xSize DrawT3String(byte[] str)
        {
            var font = (Type3Font)_cs.ts.Tf;

            //Gets some often used values
            double fsize = _cs.ts.Tfs;
            double th = _cs.ts.Th, tr = _cs.ts.Tr;

            //Gets the font to creates the glyphs. Later the font will
            //cache bitmaps. Or perhaps the glyps will. Whatever.
            var glyps = font.GetGlyphs(str);

            //Renders all glyphs.
            for (int c = 0; c < glyps.Length; c++)
            {
                //Gets a single glyph to draw
                var glyph = glyps[c];

                //Calcs next glyph position
                double ax = glyph.MoveX * font.FontMatrix.M11 * fsize + _cs.ts.Tc;
                if (glyph.IsSpace) ax += _cs.ts.Tw;

                //Adjusts the position so that the next glyphis drawn after
                //this glyph.
                _tm.Tm = _tm.Tm.TranslatePrepend(ax * th, glyph.MoveY * font.FontMatrix.M11);
            }

            throw new NotImplementedException("t3 size");
        }

        #endregion

        #region Type3 fonts

        /// <summary>
        /// Sets a colored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        void IDraw.SetT3Glyph(double wx, double wy)
        {
            _cs.cmds.Add(new d0_CMD(wy, wx));
        }

        /// <summary>
        /// Sets uncolored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        /// <param name="llx">Lower left X</param>
        /// <param name="lly">Lower left Y</param>
        /// <param name="urx">Upper right X</param>
        /// <param name="ury">Upper right Y</param>
        void IDraw.SetT3Glyph(double wx, double wy, double llx, double lly, double urx, double ury)
        {
            _cs.cmds.Add(new d1_CMD(ury, urx, lly, llx, wy, wx));
        }

        #endregion

        #region Compatibility

        void IDraw.BeginCompatibility() { _cs.cmds.Add(new BX_CMD()); }
        void IDraw.EndCompatibility() { _cs.cmds.Add(new EX_CMD()); }

        #endregion

        #region State support

        /// <summary>
        /// See 8.4 in the specs. Note that "clipping path" is tracked through the
        /// dc_pos parameter (CTM is too, but is also tracked here for scaling reasons)
        /// </summary>
        struct State
        {
            //Information about the current block
            public Analyze.AnalyzeBlock block;

            public List<RenderCMD> cmds;

            public xMatrix CTM;

            /// <summary>
            /// Graphics state parameters that only affect text
            /// </summary>
            public AnalyzeTextState ts;

            public void Reset()
            {
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

        struct TextMetrix
        {
            /// <summary>
            /// Text metrix
            /// </summary>
            public xMatrix Tm;

            /// <summary>
            /// Text line metrix
            /// </summary>
            public xMatrix Tlm;
        }

        #endregion

        internal RenderCMD[] ToArrayAndClear()
        {
            var r = _cs.cmds.ToArray();
            _cs.cmds.Clear();
            return r;
        }

        /// <summary>
        /// Returns a new CompiledPage with an updated set of commands and clears the result.
        /// </summary>
        /// <param name="cpage">Original page</param>
        /// <returns>New page based on the original</returns>
        public CompiledPage UpdateCPage(CompiledPage cpage, bool include_annotations = true)
        {
            return new CompiledPage(cpage, this.ToArrayAndClear(), include_annotations);
        }


#if DEBUG
        public void DrawRegions(IDraw d)
        {
            DrawRegions(d, _cs.cmds.ToArray());
        }

        private void DrawRegions(IDraw d, RenderCMD[] cmds)
        {
            foreach (var cmd in cmds)
            {
                if (cmd is Analyze.AnalyzeBlock)
                {
                    var abc = (Analyze.AnalyzeBlock)cmd;
                    var a = abc.Children;
                    if (a != null)
                        DrawRegions(d, a);
                }
                else if (cmd is Analyze.Do_Analyze_CMD)
                {
                    var b = ((Analyze.Do_Analyze_CMD)cmd).Bounds;

                    //Should draw the actual bounds, but for now we draw the rect
                    d.SetFillColor(0, 0, 1, 0);
                    d.RectAt(b.Bounds.X, b.Bounds.Y, b.Bounds.Width, b.Bounds.Height);
                    d.DrawPathEO(false, false, true);
                }
                else if (cmd is Analyze.AnalyzeCMD_Bounds)
                {
                    var b = ((Analyze.AnalyzeCMD_Bounds)cmd);

                    //Should draw the actual bounds, but for now we draw the rect
                    d.SetFillColor(0, 1, 0);
                    d.RectAt(b.Bounds.X, b.Bounds.Y, b.Bounds.Width, b.Bounds.Height);
                    d.DrawPathEO(false, false, true);
                }
            }
        }
#endif

        public object GetObjectAt(double x, double y)
        {
            List<object> obj = new List<object>();
            GetObjectAt(_cs.block, new xPoint(x, y), this._cs.cmds.ToArray(), obj);
            return obj.Count == 0 ? null : obj[obj.Count - 1];
        }

        private void GetObjectAt(Analyze.AnalyzeBlock ab, xPoint p, RenderCMD[] cmds, List<object> obj)
        {
            foreach(var cmd in cmds)
            {
                if (cmd is Analyze.AnalyzeBlock)
                {
                    var abc = (Analyze.AnalyzeBlock)cmd;
                    var a = abc.Children;
                    if (a != null)
                    {
                        if (abc.BType == Analyze.BTYPE.TextBlock)
                            GetTextObjAt(abc, p, a, obj);
                        else
                            GetObjectAt(abc, p, a, obj);
                    }
                }
                else if (cmd is Analyze.Do_Analyze_CMD)
                {
                    var b = ((Analyze.Do_Analyze_CMD)cmd).Bounds;

                    if (b.IsOn(p))
                    {
                        switch(ab.BType)
                        {
                            case Analyze.BTYPE.ClippedSingleImage: //q re ? n cm Do Q
                            case Analyze.BTYPE.SingleImage: //q cm Do Q
                                obj.Add(new Analyze.SingleImage(ab));
                                break;
                        }
                    }
                }
            }
        }

        private void GetTextObjAt(Analyze.AnalyzeBlock ab, xPoint p, RenderCMD[] cmds, List<object> obj)
        {
            foreach (var cmd in cmds)
            {
                if (cmd is Analyze.AnalyzeCMD_Bounds)
                {
                    var bounds = (Analyze.AnalyzeCMD_Bounds)cmd;
                    if (bounds.Bounds.IsOn(p))
                    {
                        if (bounds is Analyze.Text_Bounds)
                            obj.Add(bounds);
                    }
                }
            }
        }
    }


    /// <summary>
    /// See 9.3.1
    /// </summary>
    internal struct AnalyzeTextState
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
        public PdfFont OrgFont;

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
}
