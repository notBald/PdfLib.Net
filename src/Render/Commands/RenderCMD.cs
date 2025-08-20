using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Compile;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;
using PdfLib.Compose;
using PdfLib.Compose.Text;
using System.Collections.Generic;
using PdfLib.Render.Font;
using System.Text;

namespace PdfLib.Render.Commands
{
    public abstract class RenderCMD
    {
        internal abstract CMDType Type { get; }

        /// <summary>
        /// If this command needs a resource dictionary
        /// </summary>
        public virtual bool NeedResources { get { return false; } }

#if DEBUG
        public void ExecuteCMD(IDraw draw) => Execute(draw);
#endif
        internal abstract void Execute(IDraw draw);

        /// <summary>
        /// The type of command.
        /// </summary>
        /// <remarks>
        /// See page 111 in the specs for how the specs defines the types. Sufficient to say
        /// those aren't followed here.
        /// 
        /// Currently only used by "CompiledPage.cs" to present statistics on command use. 
        /// </remarks>
        internal enum CMDType
        {
            /// <summary>
            /// A block of commands, used by PdfAnalyze
            /// </summary>
            Block,

            /// <summary>
            /// This command manipulates the general or
            /// special state, including fonts
            /// </summary>
            State,

            /// <summary>
            /// This command draws a path or set a clip
            /// </summary>
            Paint,

            /// <summary>
            /// This command sets a color or a pattern
            /// </summary>
            Texture,

            /// <summary>
            /// This command draws an image
            /// </summary>
            Image,

            /// <summary>
            /// This command draws a form or an inline image
            /// </summary>
            Form,

            /// <summary>
            /// This command draws text
            /// </summary>
            Text,

            /// <summary>
            /// Path building
            /// </summary>
            Path,

            /// <summary>
            /// A commands that help mark up the contents
            /// </summary>
            Markup,

            /// <summary>
            /// Used for the BDC command. Note, CompiledPage.cs assumes this.
            /// </summary>
            Special
        }

        internal static bool NeedRes(RenderCMD[] cmds)
        {
            foreach (var cmd in cmds)
                if (cmd.NeedResources)
                    return true;
            return false;
        }
    }

#region General graphics state

    class d_RND : RenderCMD
    {
        readonly xDashStyle _ds;
        internal override CMDType Type { get { return CMDType.State; } }
        public d_RND(double d, double[] ar)
        { _ds = new xDashStyle(d, ar); }
        public d_RND(xDashStyle ds) { _ds = ds; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokeDashAr(_ds);
        }
    }

    class i_RND : RenderCMD
    {
        readonly double _flatness;
        internal override CMDType Type { get { return CMDType.State; } }
        public i_RND(double flatness) { _flatness = flatness; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFlatness(_flatness);
        }
    }

    class j_RND : RenderCMD
    {
        readonly xLineJoin _style;
        internal override CMDType Type { get { return CMDType.State; } }
        public j_RND(int style) { _style = (xLineJoin)style; }
        internal override void Execute(IDraw draw)
        {
            draw.SetLineJoinStyle(_style);
        }
    }

    class J_RND : RenderCMD
    {
        readonly xLineCap _style;
        internal override CMDType Type { get { return CMDType.State; } }
        public J_RND(int style) { _style = (xLineCap)style; }
        internal override void Execute(IDraw draw)
        {
            draw.SetLineCapStyle(_style);
        }
    }

    class M_RND : RenderCMD
    {
        readonly double _l;
        internal override CMDType Type { get { return CMDType.State; } }
        public M_RND(double l) { _l = l; }
        internal override void Execute(IDraw draw)
        {
            draw.SetMiterLimit(_l);
        }
    }

    class w_RND : RenderCMD
    {
        public readonly double StrokeWidth;
        internal override CMDType Type { get { return CMDType.State; } }
        public w_RND(double lt) { StrokeWidth = lt; }
        internal override void Execute(IDraw draw)
        {
            draw.StrokeWidth = StrokeWidth;
        }
    }

    class gs_RND : RenderCMD
    {
        readonly PdfGState _gs;
        internal override CMDType Type { get { return CMDType.State; } }
        public override bool NeedResources { get { return true; } }
        public gs_RND(PdfGState gs) { _gs = gs; }
        internal override void Execute(IDraw draw)
        {
            draw.SetGState(_gs);
        }
    }

    class ri_RND : RenderCMD
    {
        readonly string _ri;
        internal override CMDType Type { get { return CMDType.State; } }
        public ri_RND(string ri) { _ri = ri; }
        internal override void Execute(IDraw draw)
        {
            draw.SetRI(_ri);
        }
    }

#endregion

#region Special graphics state

    /// <summary>
    /// Saves the state
    /// </summary>
    public class q_RND : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.State; } }
        internal override void Execute(IDraw draw)
        {
            draw.Save();
        }
    }

    /// <summary>
    /// Restores the state
    /// </summary>
    public class Q_RND : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.State; } }
        internal override void Execute(IDraw draw)
        {
            draw.Restore();
        }
    }

    internal class cm_RND : RenderCMD
    {
        readonly xMatrix _m;
        public xMatrix Matrix { get { return _m; } }
        internal override CMDType Type { get { return CMDType.State; } }
        public cm_RND(xMatrix m) { _m = m; }
        internal override void Execute(IDraw draw)
        {
            draw.PrependCM(_m);
        }
    }

#endregion

#region Color commands

    /// <summary>
    /// Command for setting a gray fill color
    /// </summary>
    class g_CMD : RenderCMD, TextureCMD
    {
        readonly double _gray;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return false; } }
        public g_CMD(double gray) { _gray = gray; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillColor(_gray);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_gray);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_gray);
        }
    }

    /// <summary>
    /// Command for setting a gray stroke color
    /// </summary>
    class G_CMD : RenderCMD, TextureCMD
    {
        readonly double _gray;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return true; } }
        public G_CMD(double gray) { _gray = gray; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokeColor(_gray);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_gray);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_gray);
        }
    }

    /// <summary>
    /// Command for setting a RGB fill color
    /// </summary>
    class rg_CMD : RenderCMD, TextureCMD
    {
        readonly double _blue, _green, _red;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return false; } }
        public rg_CMD(double blue, double green, double red)
        { _blue = blue; _green = green; _red = red; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillColor(_red, _green, _blue);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_red, _green, _blue);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_red, _green, _blue);
        }
    }

    /// <summary>
    /// Command for setting a RGB stroke color
    /// </summary>
    class RG_CMD : RenderCMD, TextureCMD
    {
        readonly double _blue, _green, _red;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return true; } }
        public RG_CMD(double blue, double green, double red)
        { _blue = blue; _green = green; _red = red; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokeColor(_red, _green, _blue);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_red, _green, _blue);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_red, _green, _blue);
        }
    }

    /// <summary>
    /// Command for setting a CMYK fill color
    /// </summary>
    class k_CMD : RenderCMD, TextureCMD
    {
        readonly double _black, _yellow, _magenta, _cyan;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return false; } }
        public k_CMD(double black, double yellow, double magenta, double cyan)
        { _yellow = yellow; _magenta = magenta; _cyan = cyan; _black = black; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillColor(_cyan, _magenta, _yellow, _black);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_cyan, _magenta, _yellow, _black);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_cyan, _magenta, _yellow, _black);
        }
    }

    /// <summary>
    /// Command for setting a CMYK stroke color
    /// </summary>
    class K_CMD : RenderCMD, TextureCMD
    {
        readonly double _black, _yellow, _magenta, _cyan;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return true; } }
        public K_CMD(double black, double yellow, double magenta, double cyan)
        { _yellow = yellow; _magenta = magenta; _cyan = cyan; _black = black; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokeColor(_cyan, _magenta, _yellow, _black);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_cyan, _magenta, _yellow, _black);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_cyan, _magenta, _yellow, _black);
        }
    }

    /// <summary>
    /// Command for setting a fill color space
    /// </summary>
    class cs_CMD : RenderCMD, TextureCMD
    {
        readonly IColorSpace _cs;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public override bool NeedResources { get { return true; } }
        public bool Stroke { get { return false; } }
        public cs_CMD(IColorSpace cs) { _cs = cs; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillCS(_cs);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_cs.DefaultColorAr, _cs);
        }
        public bool Equals(cBrush color)
        {
            return color.Equals(_cs);
        }
    }

    /// <summary>
    /// Command for setting a stroke color space
    /// </summary>
    class CS_CMD : RenderCMD, TextureCMD
    {
        readonly IColorSpace _cs;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public override bool NeedResources { get { return true; } }
        public bool Stroke { get { return true; } }
        public CS_CMD(IColorSpace cs) { _cs = cs; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokeCS(_cs);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_cs.DefaultColorAr, _cs);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_cs);
        }
    }

    /// <summary>
    /// Sets the fill color of the current colorspace
    /// </summary>
    /// <remarks>Not to be used with ICC</remarks>
    class sc_CMD : RenderCMD, TextureCMD
    {
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return false; } }
        public sc_CMD(double[] col) { _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillColorSC(_col);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_col, cs);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col);
        }
    }

    /// <summary>
    /// Sets the stroke color of the current colorspace
    /// </summary>
    /// <remarks>Not to be used with ICC</remarks>
    class SC_CMD : RenderCMD, TextureCMD
    {
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return true; } }
        public SC_CMD(double[] col) { _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokeColorSC(_col);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_col, cs);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col);
        }
    }

    /// <summary>
    /// Sets the fill color of the current colorspace
    /// </summary>
    class scn_CMD : RenderCMD, TextureCMD
    {
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return false; } }
        public scn_CMD(double[] col) { _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillColor(_col);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_col, cs);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col);
        }
    }

    /// <summary>
    /// Sets the stroke color of the current colorspace
    /// </summary>
    class SCN_CMD : RenderCMD, TextureCMD
    {
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return true; } }
        public SCN_CMD(double[] col) { _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokeColor(_col);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cColor(_col, cs);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col);
        }
    }

    /// <summary>
    /// Sets the fill pattern
    /// </summary>
    class scn_pattern_CMD : RenderCMD, TextureCMD
    {
        readonly PdfShadingPattern _pat;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public override bool NeedResources { get { return true; } }
        public bool Stroke { get { return false; } }
        public scn_pattern_CMD(PdfShadingPattern pat)
        {
            if (pat == null)
                throw new PdfReadException(ErrSource.Compiler, PdfType.Pattern, ErrCode.Missing);
            _pat = pat;
        }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillPattern(_pat);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cPattern(_pat);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_pat);
        }
    }

    /// <summary>
    /// Sets the stroke pattern
    /// </summary>
    class SCN_pattern_CMD : RenderCMD, TextureCMD
    {
        readonly PdfShadingPattern _pat;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public override bool NeedResources { get { return true; } }
        public bool Stroke { get { return true; } }
        public SCN_pattern_CMD(PdfShadingPattern pat)
        {
            if (pat == null)
                throw new PdfReadException(ErrSource.Compiler, PdfType.Pattern, ErrCode.Missing);
            _pat = pat;
        }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokePattern(_pat);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cPattern(_pat);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_pat);
        }
    }

    /// <summary>
    /// Sets the fill tile pattern
    /// </summary>
    class scn_tile_CMD : RenderCMD, TextureCMD
    {
        readonly CompiledPattern _pat;
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return false; } }
        public scn_tile_CMD(double[] col, CompiledPattern pat)
        { _pat = pat; _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillPattern(_col, _pat);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cPattern(_col, _pat);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col, _pat);
        }
    }
    class scn_raw_tile_CMD : RenderCMD, TextureCMD
    {
        readonly PdfTilingPattern _pat;
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return false; } }
        public scn_raw_tile_CMD(double[] col, PdfTilingPattern pat)
        { _pat = pat; _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetFillPattern(_col, _pat);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cPattern(_col, _pat);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col, _pat);
        }
    }

    /// <summary>
    /// Sets the stroke tile pattern
    /// </summary>
    class SCN_tile_CMD : RenderCMD, TextureCMD
    {
        readonly CompiledPattern _pat;
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return true; } }
        public SCN_tile_CMD(double[] col, CompiledPattern pat)
        { _pat = pat; _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokePattern(_col, _pat);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cPattern(_col, _pat);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col, _pat);
        }
    }
    class SCN_raw_tile_CMD : RenderCMD, TextureCMD
    {
        readonly PdfTilingPattern _pat;
        readonly double[] _col;
        internal override CMDType Type { get { return CMDType.Texture; } }
        public bool Stroke { get { return true; } }
        public SCN_raw_tile_CMD(double[] col, PdfTilingPattern pat)
        { _pat = pat; _col = col; }
        internal override void Execute(IDraw draw)
        {
            draw.SetStrokePattern(_col, _pat);
        }
        public cBrush MakeColor(IColorSpace cs)
        {
            return new cPattern(_col, _pat);
        }
        public bool Equals(cBrush color)
        {
            return color != null && color.Equals(_col, _pat);
        }
    }

#endregion

#region Shading patterns

    class sh_CMD : RenderCMD
    {
        readonly PdfShading _shading;
        internal override CMDType Type { get { return CMDType.Path; } }
        public override bool NeedResources { get { return true; } }
        public sh_CMD(PdfShading shading)
        {
            if (shading == null)
                throw new PdfReadException(ErrSource.Compiler, PdfType.Shading, ErrCode.Missing);
            _shading = shading;
        }
        internal override void Execute(IDraw draw)
        {
            draw.Shade(_shading);
        }
    }

#endregion

#region Inline images

    class BI_CMD : RenderCMD
    {
        readonly PdfImage _img;
        internal override CMDType Type { get { return CMDType.Image; } }
        public BI_CMD(PdfImage img) { _img = img; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawInlineImage(_img);
        }
    }

#endregion

#region XObjects

    class Do_CMD : RenderCMD
    {
        readonly PdfImage _img;
        internal PdfImage Image { get { return _img; } }
        internal override CMDType Type { get { return CMDType.Image; } }
        public override bool NeedResources { get { return true; } }
        public Do_CMD(PdfImage img) { _img = img; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawImage(_img);
        }
    }

    class Do_FORM : RenderCMD
    {
        readonly CompiledForm _form;
        internal override CMDType Type { get { return CMDType.Form; } }
        public override bool NeedResources { get { return true; } }
        public Do_FORM(CompiledForm form) { _form = form; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawForm(_form);
        }
    }

#endregion

#region Path commands

    class c_CMD : RenderCMD
    {
        readonly protected double _x1, _x2, _x3, _y1, _y2, _y3;
        internal override CMDType Type { get { return CMDType.Path; } }
        public c_CMD(double y3, double x3, double y2, double x2, double y1, double x1)
        { _x1 = x1; _x2 = x2; _x3 = x3; _y1 = y1; _y2 = y2; _y3 = y3; }
        internal override void Execute(IDraw draw)
        {
            draw.CurveTo(_x1, _y1, _x2, _y2, _x3, _y3);
        }
    }

    class v_CMD : RenderCMD
    {
        readonly protected double _x2, _x3, _y2, _y3;
        internal override CMDType Type { get { return CMDType.Path; } }
        public v_CMD(double y3, double x3, double y2, double x2)
        { _x2 = x2; _x3 = x3; _y2 = y2; _y3 = y3; }
        internal override void Execute(IDraw draw)
        {
            draw.CurveToV(_x2, _y2, _x3, _y3);
        }
    }

    class y_CMD : RenderCMD
    {
        readonly protected double _x1, _x3, _y1, _y3;
        internal override CMDType Type { get { return CMDType.Path; } }
        public y_CMD(double y3, double x3, double y1, double x1)
        { _x1 = x1; _x3 = x3; _y1 = y1; _y3 = y3; }
        internal override void Execute(IDraw draw)
        {
            draw.CurveTo(_x1, _y1, _x3, _y3);
        }
    }

    class h_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Path; } }
        internal override void Execute(IDraw draw)
        {
            draw.ClosePath();
        }
    }

    class l_CMD : RenderCMD
    {
        public readonly double X, Y;
        internal override CMDType Type { get { return CMDType.Path; } }
        public l_CMD(double y, double x) { X = x; Y = y; }
        internal override void Execute(IDraw draw)
        {
            draw.LineTo(X, Y);
        }
        public override string ToString()
        {
            return X + " - " + Y + " l\n";
        }
    }

    class m_CMD : RenderCMD
    {
        public readonly double X, Y;
        internal override CMDType Type { get { return CMDType.Path; } }
        public m_CMD(double y, double x) { X = x; Y = y; }
        internal override void Execute(IDraw draw)
        {
            draw.MoveTo(X, Y);
        }
        public override string ToString()
        {
            return X + " - " + Y + " m\n";
        }
    }

    class re_CMD : RenderCMD
    {
        readonly protected double _x, _y, _width, _height;
        internal double Width { get { return _width; } }
        internal double Height { get { return _height; } }
        internal double X { get { return _x; } }
        internal double Y { get { return _y; } }
        internal override CMDType Type { get { return CMDType.Path; } }
        public re_CMD(double height, double width, double y, double x)
        { _x = x; _y = y; _width = width; _height = height; }
        internal override void Execute(IDraw draw)
        {
            draw.RectAt(_x, _y, _width, _height);
        }
    }

#endregion

#region Path painting

    class BS_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathEO(false, true, true);
        }
    }

    class B_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathNZ(false, true, true);
        }
    }

    class b_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathNZ(true, true, true);
        }
    }

    class bS_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathEO(true, true, true);
        }
    }

    class f_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathNZ(false, false, true);
        }
    }

    class fs_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Path; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathEO(false, false, true);
        }
    }

    class s_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathNZ(true, true, false);
        }
    }

    class S_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathNZ(false, true, false);
        }
    }

    class n_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Paint; } }
        internal override void Execute(IDraw draw)
        {
            draw.DrawPathNZ(false, false, false);
        }
    }

#endregion

#region Clipping path commands

    class W_CMD : RenderCMD
    {
        readonly xFillRule _rule;
        internal override CMDType Type { get { return CMDType.State; } }
        public W_CMD(xFillRule rule) { _rule = rule; }
        internal override void Execute(IDraw draw)
        {
            draw.SetClip(_rule);
        }
    }

#endregion

#region Text state commands

    class Tc_CMD : RenderCMD
    {
        readonly double _tc;
        internal override CMDType Type { get { return CMDType.State; } }
        public Tc_CMD(double tc) { _tc = tc; }
        internal override void Execute(IDraw draw)
        {
            draw.SetCharacterSpacing(_tc);
        }
    }

    class Tw_CMD : RenderCMD
    {
        readonly double _tw;
        internal override CMDType Type { get { return CMDType.State; } }
        public Tw_CMD(double tw) { _tw = tw; }
        internal override void Execute(IDraw draw)
        {
            draw.SetWordSpacing(_tw);
        }
    }


#if DEBUG
public
#endif
    class Tf_CMD : RenderCMD
    {
        readonly PdfFont _font;
        
#if DEBUG
        public PdfFont Font => _font;
        public
#endif
        readonly double Size;

        internal override CMDType Type { get { return CMDType.State; } }
        public override bool NeedResources { get { return true; } }
        public Tf_CMD(double s, PdfFont f)
        {
            if (f == null)
                throw new PdfReadException(ErrSource.Compiler, PdfType.Font, ErrCode.Missing);
            _font = f;
            Size = s;
        }
        internal override void Execute(IDraw draw)
        {
            draw.SetFont(_font, Size);
        }
    }

    class Tf_Type3 : RenderCMD
    {
        readonly CompiledFont _font;
        readonly double Size;
        internal override CMDType Type { get { return CMDType.State; } }
        public override bool NeedResources { get { return true; } }
        public Tf_Type3(double s, CompiledFont f)
        {
            if (f == null)
                throw new PdfReadException(ErrSource.Compiler, PdfType.Font, ErrCode.Missing);
            _font = f;
            Size = s;
        }
        internal override void Execute(IDraw draw)
        {
            draw.SetFont(_font, Size);
        }
    }

    class Tr_CMD : RenderCMD
    {
        readonly xTextRenderMode _tm;
        internal override CMDType Type { get { return CMDType.State; } }
        public Tr_CMD(bool fill, bool stroke, bool clip)
        {
            if (fill)
            {
                if (stroke)
                {
                    if (clip)
                        _tm = xTextRenderMode.FillStrokeAndPath;
                    else
                        _tm = xTextRenderMode.FillAndStroke;
                }
                else
                    _tm = xTextRenderMode.Fill;
            }
            else
            {
                if (stroke)
                {
                    if (clip)
                        _tm = xTextRenderMode.StrokeAndPath;
                    else
                        _tm = xTextRenderMode.Stroke;
                }
                else
                {
                    if (clip)
                        _tm = xTextRenderMode.Path;
                    else
                        _tm = xTextRenderMode.Invisible;
                }
            }
        }
        public Tr_CMD(int tm) 
        { 
            _tm = (xTextRenderMode) tm;
            if (tm < 0 || tm > 7)
                throw new PdfLexerException(PdfType.Integer, ErrCode.OutOfRange);
        }
        internal override void Execute(IDraw draw)
        {
            draw.SetTextMode(_tm);
        }
        public static xTextRenderMode GetMode(bool fill, bool stroke, bool clip)
        {
            if (fill)
            {
                if (stroke)
                {
                    if (clip)
                        return xTextRenderMode.FillStrokeAndPath;
                    else
                        return xTextRenderMode.FillAndStroke;
                }
                else
                    return xTextRenderMode.Fill;
            }
            else
            {
                if (stroke)
                {
                    if (clip)
                        return xTextRenderMode.StrokeAndPath;
                    else
                        return xTextRenderMode.Stroke;
                }
                else
                {
                    if (clip)
                        return xTextRenderMode.Path;
                    else
                        return xTextRenderMode.Invisible;
                }
            }
        }
    }

    class Tz_CMD : RenderCMD
    {
        readonly double _th;
        internal override CMDType Type { get { return CMDType.State; } }
        public Tz_CMD(double th) { _th = th; }
        internal override void Execute(IDraw draw)
        {
            draw.SetHorizontalScaling(_th);
        }
    }

    class TL_CMD : RenderCMD
    {
        readonly double _tl;
        internal override CMDType Type { get { return CMDType.State; } }
        public TL_CMD(double tl) { _tl = tl; }
        internal override void Execute(IDraw draw)
        {
            draw.SetTextLeading(_tl);
        }
    }

    class Ts_CMD : RenderCMD
    {
        readonly double _tr;
        internal override CMDType Type { get { return CMDType.State; } }
        public Ts_CMD(double tl) { _tr = tl; }
        internal override void Execute(IDraw draw)
        {
            draw.SetTextRise(_tr);
        }
    }

#endregion

#region Text object commands

    class BT_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Markup; } }
        internal override void Execute(IDraw draw)
        {
            draw.BeginText();
        }
    }

    class ET_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Markup; } }
        internal override void Execute(IDraw draw)
        {
            draw.EndText();
        }
    }

#endregion

#region Text position commands

#if DEBUG
    public
#else
    internal
#endif
    class Tm_CMD : RenderCMD
    {
        readonly xMatrix _m;
        internal override CMDType Type { get { return CMDType.State; } }
        public Tm_CMD(xMatrix m) { _m = m; }
        internal override void Execute(IDraw draw)
        {
            draw.SetTM(_m);
        }
    }

    /// <summary>
    /// Command for moving the text position
    /// </summary>
    public sealed class Td_CMD : RenderCMD
    {
        readonly double _x, _y;
        internal override CMDType Type { get { return CMDType.State; } }
        public Td_CMD(double y, double x) { _y = y; _x = x; }
        internal override void Execute(IDraw draw)
        {
            draw.TranslateTLM(_x, _y);
        }
    }


    public sealed class TD_CMD : RenderCMD
    {
        readonly double _x, _y;
        internal override CMDType Type { get { return CMDType.State; } }
        public TD_CMD(double y, double x) { _y = y; _x = x; }
        internal override void Execute(IDraw draw)
        {
            draw.SetTlandTransTLM(_x, _y);
        }
    }

#if DEBUG
    public
#else
    internal
#endif
    class TS_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.State; } }
        internal override void Execute(IDraw draw)
        {
            draw.TranslateTLM();
        }
    }

#endregion

#region Text Showing

    internal interface ToUnicode
    {
        string[] Execute(rFont font);
        string[] Execute(rCMap map);
        string[] Execute(int[] map);
        double[] Execute();
    }

    public sealed class TJ_CMD : RenderCMD, ToUnicode
    {
        readonly object[] _text;
        internal override CMDType Type { get { return CMDType.Text; } }
        public TJ_CMD(object[] text) { _text = text; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawString(_text);
        }
        double[] ToUnicode.Execute()
        {
            var dbls = new List<double>(_text.Length);
            double last = 0;
            foreach (var str in _text)
            {
                if (str is PdfString)
                {
                    dbls.Add(last);
                    last = 0;
                }
                else if (str is double)
                    last += (double)str;
                else if (str is PdfItem)
                    last += ((PdfItem)str).GetReal();
            }
            return dbls.ToArray();
        }
        string[] ToUnicode.Execute(rFont font)
        {
            var l = new List<string>(_text.Length);
            foreach (var str in _text)
            {
                if (str is PdfString)
                    l.Add(font.GetUnicode(((PdfString)str).ByteString));
            }
            return l.ToArray();
        }
        string[] ToUnicode.Execute(rCMap map)
        {
            var l = new List<byte[]>(_text.Length);
            foreach (var str in _text)
            {
                if (str is PdfString)
                    l.Add(((PdfString)str).ByteString);
            }
            return TJ_CMD.Execute(map, l.ToArray());
        }
        string[] ToUnicode.Execute(int[] map)
        {
            var l = new List<byte[]>(_text.Length);
            foreach (var str in _text)
            {
                if (str is PdfString)
                    l.Add(((PdfString)str).ByteString);
            }
            return TJ_CMD.Execute(map, l.ToArray());
        }

        internal static string[] Execute(rCMap map, byte[][] strs)
        {
            var sb = new StringBuilder(100);
            ushort[] str;
            string[] ds = new string[strs.Length];
            for (int c = 0; c < strs.Length; c++)
            {
                map.Map(strs[c], out str);
                foreach (var us in str)
                    sb.Append((char)us);
                ds[c] = sb.ToString();
                sb.Length = 0;
            }
            return ds;
        }
        internal static string[] Execute(int[] map, byte[][] strs)
        {
            var sb = new StringBuilder(100);
            string[] ds = new string[strs.Length];
            for (int c = 0; c < strs.Length; c++)
            {
                foreach (var chr in strs[c])
                    sb.Append((char) map[chr]);
                ds[c] = sb.ToString();
                sb.Length = 0;
            }
            return ds;
        }
    }

    public sealed class Tj_CMD : RenderCMD, ToUnicode
    {
        readonly PdfString _text;

#if DEBUG
        public PdfString Text => _text;
#endif

        internal override CMDType Type { get { return CMDType.Text; } }
        public Tj_CMD(PdfString text) { _text = text; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawString(_text, false);
        }
        string[] ToUnicode.Execute(rFont font)
        {
            return new string[] { font.GetUnicode(_text.ByteString) };
        }
        string[] ToUnicode.Execute(rCMap map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.ByteString });
        }
        string[] ToUnicode.Execute(int[] map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.ByteString });
        }
        double[] ToUnicode.Execute()
        {
            return new double[] { 0 };
        }
    }

    class Tcr_CMD : RenderCMD, ToUnicode
    {
        readonly PdfString _text;
        internal override CMDType Type { get { return CMDType.Text; } }
        public Tcr_CMD(PdfString text) { _text = text; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawString(_text, true);
        }
        string[] ToUnicode.Execute(rFont font)
        {
            return new string[] { font.GetUnicode(_text.ByteString) };
        }
        string[] ToUnicode.Execute(rCMap map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.ByteString });
        }
        string[] ToUnicode.Execute(int[] map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.ByteString });
        }
        double[] ToUnicode.Execute()
        {
            return new double[] { 0 };
        }
    }

    class Tws_CMD : RenderCMD, ToUnicode
    {
        readonly PdfString _text;
        readonly double _ac, _aw;
        internal override CMDType Type { get { return CMDType.Text; } }
        public Tws_CMD(PdfString text, double ac, double aw)
        { _text = text; _ac = ac; _aw = aw; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawString(_text, _aw, _ac);
        }
        string[] ToUnicode.Execute(rFont font)
        {
            return new string[] { font.GetUnicode(_text.ByteString) };
        }
        string[] ToUnicode.Execute(rCMap map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.ByteString });
        }
        string[] ToUnicode.Execute(int[] map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.ByteString });
        }
        double[] ToUnicode.Execute()
        {
            return new double[] { 0 };
        }
    }

#endregion

#region Type3 fonts
    //These commands only appear in type 3 fonts

    internal class d0_CMD : RenderCMD
    {
        readonly double _wx, _wy;
        internal override CMDType Type { get { return CMDType.State; } }
        public d0_CMD(double wy, double wx) { _wy = wy; _wx = wx; }
        internal override void Execute(IDraw draw)
        {
            draw.SetT3Glyph(_wx, _wy);
        }
    }

    internal class d1_CMD : RenderCMD
    {
        readonly double _wx, _wy, _llx, _lly, _urx, _ury;
        internal override CMDType Type { get { return CMDType.State; } }
        public d1_CMD(double ury, double urx, double lly, double llx, double wy, double wx)
        { _ury = ury; _urx = urx; _lly = lly; _llx = llx; _wy = wy; _wx = wx; }
        internal override void Execute(IDraw draw)
        {
            draw.SetT3Glyph(_wx, _wy, _llx, _lly, _urx, _ury);
        }
    }

#endregion

#region Marked contents

    class MP_CMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.Markup; } }

        //Not always correct. The dict can be embeded in the command stream.
        public override bool NeedResources { get { return true; } }
        public MP_CMD(string property, PdfDictionary dict) { }

        internal override void Execute(IDraw draw)
        {
            //For now we do nothing with Marked Contents
        }
    }

    class BDC_CMD : RenderCMD
    {
        internal readonly RenderCMD[] _cmds;
        internal override CMDType Type { get { return CMDType.Special; } }

        //Not always correct. The dict can be embeded in the command stream.
        public override bool NeedResources { get { return true; } }
        public BDC_CMD(RenderCMD[] cmds, string property, PdfDictionary dict)
        {
            _cmds = cmds;
        }
        internal override void Execute(IDraw draw)
        {
            draw.Execute(_cmds);
        }
    }

#endregion

#region Compatibility

    internal class BX_CMD : RenderCMD
    {
        public BX_CMD() { }
        internal override CMDType Type { get { return CMDType.Markup; } }
        internal override void Execute(IDraw draw)
        {
            draw.BeginCompatibility();
        }
    }

    internal class EX_CMD : RenderCMD
    {
        public EX_CMD() { }
        internal override CMDType Type { get { return CMDType.Markup; } }
        internal override void Execute(IDraw draw)
        {
            draw.EndCompatibility();
        }
    }

#endregion

#region Interface

    public interface TextureCMD
    {
        /// <summary>
        /// Whenever this is a stroke command or not
        /// </summary>
        bool Stroke { get; }

        /// <summary>
        /// If this commands sets a color equal to imput
        /// </summary>
        /// <param name="color">A compose color to compare with</param>
        bool Equals(cBrush color);

        /// <summary>
        /// Makes a compose color
        /// </summary>
        /// <param name="cs">Colorspace for colors without colorspace</param>
        /// <returns>A compose color with color and colorspace</returns>
        cBrush MakeColor(IColorSpace cs);
    }

#endregion
}
