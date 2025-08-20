using System.Collections.Generic;
using PdfLib.Pdf;
using PdfLib.Compile;
using PdfLib.Render.Commands;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Compose;
using PdfLib.Compose.Text;

namespace PdfLib.Render.PDF
{
    /// <summary>
    /// For when one wish to create "RenderCMD" commands
    /// </summary>
    public class DrawCMD : IDraw
    {
        #region Variables and properties

        private List<RenderCMD> _cmds;

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

        /// <summary>
        /// Will refuse to emit restore commands that pops the stack beyond this point.
        /// </summary>
        private int _restore_threshold = 0;

        /// <summary>
        /// Does not track graphic state.
        /// </summary>
        public GS GraphicState { get { return GS.Unknown; } }

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
        public DrawCMD()
        {
            Init();
            _cmds = new List<RenderCMD>();
        }

        public DrawCMD(List<RenderCMD> cmds)
        {
            Init();
            _cmds = cmds;
        }

        public void Dispose()
        {

        }

        private void Init()
        {
            Running = true;
            ((IDraw) this).Executor = StdExecutor.STD;
        }

        public void PrepForAnnotations(bool init)
        {
            if (init)
            {
                _restore_threshold = 1;
                Save();
            }
            else
            {
                _restore_threshold--;
                while (_cs.dc_pos > _restore_threshold)
                    Restore();
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
            get { return -1; }
            set { _cmds.Add(new w_RND(value)); }
        }

        public void SetFlatness(double i)
        {
            _cmds.Add(new i_RND(i));
        }

        public void SetLineJoinStyle(xLineJoin style)
        {
            _cmds.Add(new j_RND((int)style));
        }

        public void SetLineCapStyle(xLineCap style)
        {
            _cmds.Add(new J_RND((int)style));
        }

        public void SetMiterLimit(double limit)
        {
            _cmds.Add(new M_RND(limit));
        }

        public void SetStrokeDashAr(xDashStyle ds)
        {
            _cmds.Add(new d_RND(ds));
        }

        /// <summary>
        /// Sets the graphic state
        /// </summary>
        /// <param name="gstate">The graphic state</param>
        public void SetGState(PdfGState gstate)
        {
            _cmds.Add(new gs_RND(gstate));
        }

        public void SetRI(string ri)
        {
            _cmds.Add(new ri_RND(ri));
        }

        #endregion

        #region Special graphics state

        public void Save()
        {
            _cmds.Add(new q_RND());
            _cs.dc_pos++;
        }

        public void Restore()
        {
            if (_cs.dc_pos > _restore_threshold)
            {
                _cs.dc_pos--;
                _cmds.Add(new Q_RND());
            }
        }

        public void PrependCM(xMatrix m)
        {
            _cmds.Add(new cm_RND(m));
        }

        #endregion

        #region Color

        public void SetFillColor(double cyan, double magenta, double yellow, double black)
        {
            _cmds.Add(new k_CMD(black, yellow, magenta, cyan));
        }
        public void SetFillColor(double red, double green, double blue)
        {
            _cmds.Add(new rg_CMD(blue, green, red));
        }
        public void SetFillColor(double gray)
        {
            _cmds.Add(new g_CMD(gray));
        }
        public void SetFillColorSC(double[] color)
        {
            _cmds.Add(new sc_CMD(color));
        }
        public void SetFillColor(double[] color)
        {
            _cmds.Add(new scn_CMD(color));
        }

        public void SetStrokeColor(double cyan, double magenta, double yellow, double black)
        {
            _cmds.Add(new K_CMD(black, yellow, magenta, cyan));
        }
        public void SetStrokeColor(double red, double green, double blue)
        {
            _cmds.Add(new RG_CMD(blue, green, red));
        }
        public void SetStrokeColor(double gray)
        {
            _cmds.Add(new G_CMD(gray));
        }
        public void SetStrokeColorSC(double[] color)
        {
            _cmds.Add(new SC_CMD(color));
        }
        public void SetStrokeColor(double[] color)
        {
            _cmds.Add(new SCN_CMD(color));
        }

        public void SetFillCS(IColorSpace cs)
        {
            _cmds.Add(new cs_CMD(cs));
        }

        public void SetStrokeCS(IColorSpace cs)
        {
            _cmds.Add(new CS_CMD(cs));
        }

        public void SetFillPattern(PdfShadingPattern pat)
        {
            _cmds.Add(new scn_pattern_CMD(pat));
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
        /// Forms  must be compiled before they can be used
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
        public void SetFillPattern(double[] color, CompiledPattern pat)
        {
            _cmds.Add(new scn_tile_CMD(color, pat));
        }

        /// <summary>
        /// Sets a uncompiled tiling pattern.
        /// </summary>
        public void SetFillPattern(double[] color, PdfTilingPattern pat)
        {
            _cmds.Add(new scn_raw_tile_CMD(color, pat));
        }

        public void SetStrokePattern(PdfShadingPattern pat)
        {
            _cmds.Add(new SCN_pattern_CMD(pat));
        }

        public void SetStrokePattern(double[] color, CompiledPattern pat)
        {
            _cmds.Add(new SCN_tile_CMD(color, pat));
        }

        /// <summary>
        /// Sets a uncompiled tiling pattern.
        /// </summary>
        public void SetStrokePattern(double[] color, PdfTilingPattern pat)
        {
            _cmds.Add(new SCN_raw_tile_CMD(color, pat));
        }

        #endregion

        #region Shading patterns

        public void Shade(PdfShading shading)
        {
            _cmds.Add(new sh_CMD(shading));
        }

        #endregion

        #region Inline images

        public void DrawInlineImage(PdfImage img)
        {
            _cmds.Add(new BI_CMD(img));
        }

        #endregion

        #region XObjects

        public void DrawImage(PdfImage img)
        {
            _cmds.Add(new Do_CMD(img));
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
            _cmds.Add(new Do_FORM(img));
        }

        #endregion

        #region Path construction

        public void MoveTo(double x, double y) { _cmds.Add(new m_CMD(y, x)); }
        public void LineTo(double x, double y) { _cmds.Add(new l_CMD(y, x)); }
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            _cmds.Add(new c_CMD(y3, x3, y2, x2, y1, x1));
        }
        public void CurveTo(double x1, double y1, double x3, double y3)
        {
            _cmds.Add(new y_CMD(y3, x3, y1, x1));
        }
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            _cmds.Add(new v_CMD(y3, x3, y2, x2));
        }
        public void RectAt(double x, double y, double width, double height)
        {
            _cmds.Add(new re_CMD(height, width, y, x));
        }

        public void ClosePath()
        {
            _cmds.Add(new h_CMD());
        }

        public void DrawClip(xFillRule fr)
        {
            SetClip(fr);
            DrawPathNZ(false, false, false);
        }

        public void DrawPathNZ(bool close, bool stroke, bool fill)
        {
            if (stroke)
            {
                if (fill)
                {
                    if (close)
                        _cmds.Add(new b_CMD());
                    else
                        _cmds.Add(new B_CMD());
                }
                else
                {
                    if (close)
                        _cmds.Add(new s_CMD());
                    else
                        _cmds.Add(new S_CMD());
                }
            }
            else
            {
                if (fill)
                {
                    if (close)
                        throw new PdfNotSupportedException("Can't close and fill");
                    else
                        _cmds.Add(new f_CMD());
                }
                else
                {
                    if (close)
                        throw new PdfNotSupportedException("Use ClosePath instead");
                    else
                        _cmds.Add(new n_CMD());
                }
            }
        }

        public void DrawPathEO(bool close, bool stroke, bool fill)
        {
            if (stroke)
            {
                if (fill)
                {
                    if (close)
                        _cmds.Add(new bS_CMD());
                    else
                        _cmds.Add(new BS_CMD());
                }
                else
                {
                    if (close)
                        _cmds.Add(new s_CMD());
                    else
                        _cmds.Add(new S_CMD());
                }
            }
            else
            {
                if (fill)
                {
                    if (close)
                        throw new PdfNotSupportedException("Can't close and fill");
                    else
                        _cmds.Add(new fs_CMD());
                }
                else
                {
                    if (close)
                        throw new PdfNotSupportedException("Use ClosePath instead");
                    else
                        _cmds.Add(new n_CMD());
                }
            }
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            _cmds.Add(new W_CMD(rule));
        }

        #endregion

        #region Text Objects

        public void BeginText() { _cmds.Add(new BT_CMD()); }
        public void EndText() { _cmds.Add(new ET_CMD()); }

        #endregion

        #region Text State

        public void SetCharacterSpacing(double tc)
        {
            _cmds.Add(new Tc_CMD(tc));
        }

        public void SetWordSpacing(double tw)
        {
            _cmds.Add(new Tw_CMD(tw));
        }

        public void SetFont(cFont font, double size)
        {
            _cmds.Add(new Tf_BuildCMD(font, size));
        }

        public void SetFont(PdfFont font, double size)
        {
            _cmds.Add(new Tf_CMD(size, font));
        }

        public void SetFont(CompiledFont font, double size)
        {
            _cmds.Add(new Tf_Type3(size, font));
        }

        public void SetTextMode(xTextRenderMode mode)
        {
            _cmds.Add(new Tr_CMD((int)mode));
        }

        /// <summary>
        /// The percentage of normal width (default 100)
        /// </summary>
        public void SetHorizontalScaling(double th)
        {
            _cmds.Add(new Tz_CMD(th));
        }

        public void SetTextLeading(double lead)
        {
            _cmds.Add(new TL_CMD(lead));
        }

        public void SetTextRise(double tr)
        {
            _cmds.Add(new Ts_CMD(tr));
        }

        #endregion

        #region Text positioning

        public void SetTM(xMatrix m)
        {
            _cmds.Add(new Tm_CMD(m));
        }

        public void TranslateTLM(double x, double y)
        {
            _cmds.Add(new Td_CMD(y, x));
        }

        public void TranslateTLM()
        {
            _cmds.Add(new TS_CMD());
        }

        public void SetTlandTransTLM(double x, double y)
        {
            _cmds.Add(new TD_CMD(y, x));
        }

        #endregion

        #region Text showing

        public void DrawString(PdfItem[] str_ar)
        {
            _cmds.Add(new TJ_BuildCMD(str_ar));
        }
        public void DrawString(BuildString str)
        {
            _cmds.Add(new Tj_BuildCMD(str));
        }

        public void DrawString(PdfString text, double aw, double ac)
        {
            _cmds.Add(new Tws_CMD(text, ac, aw));
        }

        public void DrawString(PdfString text, bool cr)
        {
            if (cr)
            {
                _cmds.Add(new Tcr_CMD(text));
            }
            else
            {
                _cmds.Add(new Tj_CMD(text));
            }
        }

        public void DrawString(object[] text)
        {
            _cmds.Add(new TJ_CMD(text));
        }

        #endregion

        #region Type3 fonts

        /// <summary>
        /// Sets a colored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        public void SetT3Glyph(double wx, double wy)
        {
            _cmds.Add(new d0_CMD(wy, wx));
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
        public void SetT3Glyph(double wx, double wy, double llx, double lly, double urx, double ury)
        {
            _cmds.Add(new d1_CMD(ury, urx, lly, llx, wy, wx));
        }

        #endregion

        #region Compatibility

        public void BeginCompatibility() { _cmds.Add(new BX_CMD()); }
        public void EndCompatibility() { _cmds.Add(new EX_CMD()); }

        #endregion

        #region State support

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
        }

        #endregion

        internal RenderCMD[] ToArrayAndClear()
        {
            var r = _cmds.ToArray();
            _cmds.Clear();
            return r;
        }
    }
}