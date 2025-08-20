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
    /// <summary>
    /// Used for debugging
    /// </summary>
    internal class StateDraw : IDraw
    {
        #region State

        private IDraw _draw;
        private cRenderState _crs;
        private IColorSpace _cs, _CS;

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

        public StateDraw(IDraw draw, cRenderState crs) { _draw = draw; _crs = crs; }

        public void PrepForAnnotations(bool init)
        {
            _draw.PrepForAnnotations(init);
        }

        /// <summary>
        /// Clears cached data. Class can be used after disposal.
        /// </summary>
        public void Dispose()
        {
            _draw.Dispose();
        }

        #endregion

        #region General Graphic State

        /// <summary>
        /// Width lines will be stroked with.
        /// Will always return -1
        /// </summary>
        public double StrokeWidth
        {
            get { return _draw.StrokeWidth; }
            set
            {
                _crs.StrokeWidth = value;
                _draw.StrokeWidth = value;
            }
        }

        public void SetFlatness(double i)
        { _draw.SetFlatness(i); }

        /// <summary>
        /// Set how lines are joined togheter
        /// </summary>
        public void SetLineJoinStyle(xLineJoin style)
        {
            _draw.SetLineJoinStyle(style);
        }

        /// <summary>
        /// Set how lines are ended
        /// </summary>
        public void SetLineCapStyle(xLineCap style)
        {
            _draw.SetLineCapStyle(style);
        }

        /// <summary>
        /// Sets the miter limit
        /// </summary>
        public void SetMiterLimit(double limit)
        {
            _draw.SetMiterLimit(limit);
        }

        public void SetStrokeDashAr(xDashStyle ds)
        {
            _draw.SetStrokeDashAr(ds);
        }

        public void SetGState(PdfGState gstate)
        {
            _draw.SetGState(gstate);
        }

        public void SetRI(string ri)
        { _draw.SetRI(ri); }

        #endregion

        #region Special graphics state

        public void Save()
        {
            _draw.Save();
        }

        public void Restore()
        {
            _draw.Restore();
        }

        /// <summary>
        /// Prepend matrix to CTM
        /// </summary>
        public void PrependCM(xMatrix xm)
        {
            _draw.PrependCM(xm);
        }

        #endregion

        #region Color

        public void SetFillColor(double cyan, double magenta, double yellow, double black)
        {
            _CS = DeviceCMYK.Instance;
            _crs.FillColor = new cColor(cyan, magenta, yellow, black);
            _draw.SetFillColor(cyan, magenta, yellow, black); 
        }
        public void SetFillColor(double red, double green, double blue)
        {
            _CS = DeviceRGB.Instance;
            _crs.FillColor = new cColor(red, green, blue);
            _draw.SetFillColor(red, green, blue); 
        }
        public void SetFillColor(double gray)
        {
            _CS = DeviceGray.Instance;
            _crs.FillColor = new cColor(gray);
            _draw.SetFillColor(gray); 
        }
        public void SetFillColorSC(double[] color)
        {
            _crs.FillColor = new cColor(color, _CS);
            _draw.SetFillColorSC(color); 
        }
        public void SetFillColor(double[] color)
        {
            if (_CS is CSPattern)
                _crs.FillColor = new cPattern(color, (PdfPattern)_CS);
            else
                _crs.FillColor = new cColor(color, _CS);
            _draw.SetFillColor(color); 
        }
        public void SetStrokeColor(double cyan, double magenta, double yellow, double black)
        {
            _cs = DeviceCMYK.Instance;
            _crs.StrokeColor = new cColor(cyan, magenta, yellow, black);
            _draw.SetStrokeColor(cyan, magenta, yellow, black); 
        }
        public void SetStrokeColor(double red, double green, double blue)
        {
            _cs = DeviceRGB.Instance;
            _crs.StrokeColor = new cColor(red, green, blue);
            _draw.SetStrokeColor(red, green, blue); 
        }
        public void SetStrokeColor(double gray)
        {
            _cs = DeviceGray.Instance;
            _crs.StrokeColor = new cColor(gray);
            _draw.SetStrokeColor(gray); 
        }
        public void SetStrokeColorSC(double[] color)
        {
            _crs.StrokeColor = new cColor(color, _cs);
            _draw.SetStrokeColorSC(color); 
        }
        public void SetStrokeColor(double[] color)
        {
            if (_cs is CSPattern)
                _crs.StrokeColor = new cPattern(color, (PdfPattern)_cs);
            else
                _crs.StrokeColor = new cColor(color, _cs);
            _draw.SetStrokeColor(color); 
        }
        public void SetFillCS(IColorSpace cs)
        {
            _CS = cs;
            _draw.SetFillCS(cs); 
        }
        public void SetStrokeCS(IColorSpace cs)
        {
            _cs = cs;
            _draw.SetStrokeCS(cs); 
        }
        public void SetFillPattern(PdfShadingPattern pat)
        {
            
            _cs = new CSPattern();
            var csp = (CSPattern)_cs;
            csp.Pat = pat;
            csp.CPat = null;
            _crs.FillColor = new cPattern(pat);
            _draw.SetFillPattern(pat); 
        }
        public void SetFillPattern(double[] color, CompiledPattern pat)
        {
            _crs.FillColor = new cPattern(color, pat);
            _draw.SetFillPattern(color, pat); 
        }
        public void SetFillPattern(double[] color, PdfTilingPattern pat)
        {
            _crs.FillColor = new cPattern(color, pat);
            _draw.SetFillPattern(color, pat); 
        }
        public void SetStrokePattern(PdfShadingPattern pat)
        {
            _crs.StrokeColor = new cPattern(pat);
            _draw.SetStrokePattern(pat); 
        }
        public void SetStrokePattern(double[] color, CompiledPattern pat)
        {
            _crs.StrokeColor = new cPattern(color, pat);
            _draw.SetStrokePattern(color, pat); 
        }
        public void SetStrokePattern(double[] color, PdfTilingPattern pat)
        {
            _crs.StrokeColor = new cPattern(color, pat);
            _draw.SetStrokePattern(color, pat); 
        }

        #endregion

        #region Shading patterns

        public void Shade(PdfShading shading)
        {
            _draw.Shade(shading);
        }

        #endregion

        #region Inline images

        public void DrawInlineImage(PdfImage img)
        { _draw.DrawInlineImage(img); }

        #endregion

        #region XObjects

        /// <summary>
        /// Draws an image
        /// </summary>
        public void DrawImage(PdfImage img)
        {
            _draw.DrawImage(img);
        }

        /// <summary>
        /// Draws a form. 
        /// </summary>
        /// <param name="form">Form to draw</param>
        /// <returns>True if the form was drawn, false if this function is unsuported</returns>
        public bool DrawForm(PdfForm form)
        {
            return _draw.DrawForm(form);
        }

        public void DrawForm(CompiledForm img)
        {
            _draw.DrawForm(img);
        }

        #endregion

        #region Path construction

        /// <summary>
        /// Starts a path from the given point.
        /// </summary>
        public void MoveTo(double x, double y)
        {
            _draw.MoveTo(x, y);
        }

        /// <summary>
        /// Draws a line to the given point
        /// </summary>
        public void LineTo(double x, double y)
        {
            _draw.LineTo(x, y);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            _draw.CurveTo(x1, y1, x2, y2, x3, y3);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveTo(double x1, double y1, double x3, double y3)
        {
            _draw.CurveTo(x1, y1, x3, y3);
        }

        /// <summary>
        /// Draws a curve to the given point.
        /// </summary>
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            _draw.CurveToV(x2, y2, x3, y3);
        }

        /// <summary>
        /// Draws a rectangle as a new figure/subpath.
        /// </summary>
        public void RectAt(double x, double y, double width, double height)
        {
            _draw.RectAt(x, y, width, height);
        }

        public void ClosePath()
        {
            _draw.ClosePath();
        }

        public void DrawClip(xFillRule fr)
        {
            _draw.DrawClip(fr);
        }

        public void DrawPathNZ(bool close, bool stroke, bool fill)
        {
            _draw.DrawPathNZ(close, stroke, fill);
        }

        public void DrawPathEO(bool close, bool stroke, bool fill)
        {
            _draw.DrawPathEO(close, stroke, fill);
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            _draw.SetClip(rule);
        }

        #endregion

        #region Text objects

        /// <summary>
        /// Sets the TM back to identity
        /// </summary>
        public void BeginText()
        {
            _crs.BeginText();
            _draw.BeginText();
        }

        /// <summary>
        /// Ends text mode
        /// </summary>
        public void EndText()
        {
            _crs.EndText(_draw);
        }

        #endregion

        #region Text State

        public void SetCharacterSpacing(double tc)
        {
            _crs.Tc = tc;
            _draw.SetCharacterSpacing(tc);
        }

        /// <summary>
        /// Set the distance between words
        /// </summary>
        public void SetWordSpacing(double s)
        {
            _crs.Tw = s;
            _draw.SetWordSpacing(s);
        }

        public void SetFont(PdfFont font, double size)
        {
            if (font is PdfLib.Compose.Font.cWrapFont)
                _crs.Tf = ((PdfLib.Compose.Font.cWrapFont)font).CFont;
            else
                _crs.Tf = cFont.Create(font);
            _crs.Tfs = size;
            _draw.SetFont(font, size);
        }

        public void SetFont(cFont font, double size)
        {
            _crs.Tf = font;
            _crs.Tfs = size;
            _draw.SetFont(font, size);
        }

        public void SetFont(CompiledFont font, double size)
        {
            _crs.Tfs = size;
            _draw.SetFont(font, size);
        }

        /// <summary>
        /// Set text rendering mode
        /// </summary>
        public void SetTextMode(xTextRenderMode mode)
        {
            
            _draw.SetTextMode(mode);
        }

        /// <summary>
        /// Set the scaling of the text in horiontal direction
        /// </summary>
        public void SetHorizontalScaling(double th)
        {
            _crs.Th = th;
            _draw.SetHorizontalScaling(th);
        }

        /// <summary>
        /// Set text leading (distance between lines)
        /// </summary>
        public void SetTextLeading(double lead)
        {
            _crs.Tl = lead;
            _draw.SetTextLeading(lead);
        }

        public void SetTextRise(double tr)
        {
            _crs.Tr = tr;
            _draw.SetTextRise(tr);
        }

        #endregion

        #region Text positioning

        public void SetTM(xMatrix m)
        {
            _crs.SetTM(m);
            _draw.SetTM(m);
        }

        /// <summary>
        /// Translates the TML and sets it to TM
        /// </summary>
        public void TranslateTLM(double x, double y)
        {
            _crs.TranslateTLM(x, y);
            _draw.TranslateTLM(x, y);
        }

        /// <summary>
        /// Move down one line
        /// </summary>
        public void TranslateTLM()
        {
            _crs.TranslateTLM();
            _draw.TranslateTLM();
        }

        public void SetTlandTransTLM(double x, double y)
        {
            _crs.SetTlandTransTLM(x, y);
            _draw.SetTlandTransTLM(x, y);
        }

        #endregion

        #region Text showing

        public void DrawString(PdfItem[] str_ar)
        {
            _draw.DrawString(str_ar);
        }
        public void DrawString(BuildString str)
        {
            _draw.DrawString(str);
        }

        public void DrawString(PdfString text, double aw, double ac)
        {
            _draw.DrawString(text, aw, ac);
        }

        public void DrawString(PdfString text, bool cr)
        {
            _draw.DrawString(text, cr);
        }

        public void DrawString(object[] text)
        {
            _draw.DrawString(text);
        }

        #endregion

        #region Type3 fonts

        /// <summary>
        /// Sets a colored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        public void SetT3Glyph(double wx, double wy)
        { _draw.SetT3Glyph(wx, wy); }

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
        { _draw.SetT3Glyph(wx, wy, llx, lly, urx, ury); }

        #endregion

        #region Compatibility

        public void BeginCompatibility()
        { _draw.BeginCompatibility(); }
        public void EndCompatibility()
        { _draw.EndCompatibility(); }

        #endregion
    }
}
