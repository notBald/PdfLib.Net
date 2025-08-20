using System;
using PdfLib.Compile;
using PdfLib.Pdf;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Internal;
using PdfLib.Compose.Text;
using PdfLib.Compose;

namespace PdfLib.Render
{
    public interface IDraw : IDisposable
    {
        #region Execution

        GS GraphicState { get; }

        /// <summary>
        /// Is always set before executiuon
        /// </summary>
        IExecutor Executor { get; set; }

        /// <summary>
        /// Rendering precision
        /// </summary>
        int Precision { get; }

        /// <summary>
        /// Draws the raw commands.
        /// </summary>
        void Execute(object cmds);

        /// <summary>
        /// Draws the raw commands.
        /// </summary>
        void Draw(IExecutable cmds);

        /// <summary>
        /// Some renderers have to do extra work to support annotations
        /// </summary>
        /// <param name="init">True if the document has just inited. False if you're about to draw annotations</param>
        void PrepForAnnotations(bool init);

        #endregion

        #region General Graphic State

        /// <summary>
        /// Width lines will be stroked with
        /// </summary>
        /// <remarks>
        /// Implementors can return whatever they want. Just used for debugging.
        /// </remarks>
        double StrokeWidth { get; set; }

        void SetLineCapStyle(xLineCap style);
        void SetFlatness(double i);
        void SetLineJoinStyle(xLineJoin style);
        void SetMiterLimit(double limit);
        void SetStrokeDashAr(xDashStyle ds);
        void SetGState(PdfGState gstate);
        void SetRI(string ri);

        #endregion

        #region Special graphics state

        /// <summary>
        /// Push current state onto _stack
        /// </summary>
        void Save();

        /// <summary>
        /// Prepends the current transform matrix.
        /// </summary>
        void PrependCM(xMatrix m);

        /// <summary>
        /// Restore previous state from _stack
        /// </summary>
        void Restore();

        #endregion

        #region Color

        /// <summary>
        /// Set fill color
        /// </summary>
        void SetFillColor(double cyan, double magenta, double yellow, double black);
        void SetFillColor(double red, double green, double blue);
        void SetFillColor(double gray);

        /// <summary>
        /// Sets color for the current color space
        /// </summary>
        void SetFillColor(double[] color);
        void SetFillColorSC(double[] color);
        void SetStrokeColor(double[] color);
        void SetStrokeColorSC(double[] color);

        /// <summary>
        /// Set stroke color
        /// </summary>
        void SetStrokeColor(double cyan, double magenta, double yellow, double black);
        void SetStrokeColor(double red, double green, double blue);
        void SetStrokeColor(double gray);

        /// <summary>
        /// Set fill color space
        /// </summary>
        void SetFillCS(IColorSpace cs);

        void SetStrokeCS(IColorSpace cs);

        void SetFillPattern(PdfShadingPattern pat);
        void SetFillPattern(double[] color, CompiledPattern pat);
        void SetFillPattern(double[] color, PdfTilingPattern pat);
        void SetStrokePattern(PdfShadingPattern pat);
        void SetStrokePattern(double[] color, CompiledPattern pat);
        void SetStrokePattern(double[] color, PdfTilingPattern pat);

        #endregion

        #region Shading patterns

        void Shade(PdfShading shading);

        #endregion

        #region Inline images

        void DrawInlineImage(PdfImage img);

        #endregion

        #region XObjects

        void DrawImage(PdfImage img);
        /// <summary>
        /// Draws a form
        /// </summary>
        /// <param name="form">Form to draw</param>
        /// <returns>False if only Compiled Forms are supported</returns>
        bool DrawForm(PdfForm form);
        void DrawForm(CompiledForm img);

        #endregion

        #region Path construction

        void MoveTo(double x, double y);
        void LineTo(double x, double y);
        void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3);
        void CurveTo(double x1, double y1, double x3, double y3);
        void CurveToV(double x2, double y2, double x3, double y3);
        void RectAt(double x, double y, double width, double height);

        /// <summary>
        /// Closes the current path
        /// </summary>
        void ClosePath();

        /// <summary>
        /// Draws the current path with EvenOdd fill rule.
        /// </summary>
        void DrawPathEO(bool close, bool stroke, bool fill);

        /// <summary>
        /// Draws the current path with Nonzero fill rule.
        /// </summary>
        void DrawPathNZ(bool close, bool stroke, bool fill);

        /// <summary>
        /// Sets the clipping path.
        /// </summary>
        /// <remarks>What fillrule to use for the clip</remarks>
        void DrawClip(xFillRule fr);

        #endregion

        #region Clipping path

        void SetClip(xFillRule rule);

        #endregion

        #region Text Objects

        void BeginText();
        void EndText();

        #endregion

        #region Text State

        void SetCharacterSpacing(double tc);
        void SetWordSpacing(double s);
        void SetFont(PdfFont font, double size);
        void SetFont(CompiledFont font, double size);
        void SetFont(cFont font, double size);
        void SetTextMode(xTextRenderMode mode);
        void SetHorizontalScaling(double th);
        void SetTextLeading(double lead);
        void SetTextRise(double tr);

        #endregion

        #region Text positioning

        void SetTM(xMatrix m);
        void TranslateTLM(double x, double y);
        void TranslateTLM();
        void SetTlandTransTLM(double x, double y);
        #endregion

        #region Text showing

        void DrawString(PdfItem[] str_ar);
        void DrawString(BuildString str);
        void DrawString(PdfString text, double aw, double ac);
        void DrawString(PdfString text, bool cr);
        void DrawString(object[] text);

        #endregion

        #region Type3 fonts

        /// <summary>
        /// Sets a colored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        void SetT3Glyph(double wx, double wy);

        /// <summary>
        /// Sets uncolored T3 glyph
        /// </summary>
        /// <param name="wx">Width of the glyph</param>
        /// <param name="wy">Must be 0</param>
        /// <param name="llx">Lower left X</param>
        /// <param name="lly">Lower left Y</param>
        /// <param name="urx">Upper right X</param>
        /// <param name="ury">Upper right Y</param>
        void SetT3Glyph(double wx, double wy, double llx, double lly, double urx, double ury);

        #endregion

        #region Compatibility

        void BeginCompatibility();
        void EndCompatibility();

        #endregion
    }

    /// <summary>
    /// Optinaly used to track the graphic state of
    /// the renderer. See 8.2 in the specs
    /// </summary>
    public enum GS : sbyte
    {
        //Note, Unknown is used for detecting invalid
        //marked contents (not explicitly clear in the code)
        Unknown = -1,
        Page = 0, //<-- must be 0
        Path,
        Clipping,
        InLineImage,
        ExtObj,
        Shading,
        Text
    }

    /// <summary>
    /// For practical reasons some data is cached in the
    /// document structure. Such cached data may be render
    /// engine dependet, this enum is used for those 
    /// situations.
    /// </summary>
    public enum RenderEngine
    {
        /// <summary>
        /// Data is not depended on any rendering engine
        /// </summary>
        None,
        /// <summary>
        /// Data is depended on a custom rendering engine.
        /// </summary>
        Custom,
        /// <summary>
        /// Data is depended on the DrawPage rendering engine.
        /// </summary>
        DrawPage,
        /// <summary>
        /// Data is depended on the WPF rendering engine.
        /// </summary>
        DrawDC,
        /// <summary>
        /// Data is depended on the Cairo rendering engine.
        /// </summary>
        DrawCairo,
        /// <summary>
        /// Data is depended on the Info rendering engine.
        /// </summary>
        InfoPath
    }
}
