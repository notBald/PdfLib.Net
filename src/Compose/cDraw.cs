using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Render;
using PdfLib.Render.PDF;
using PdfLib.Render.Commands;
using PdfLib.Compile;
using PdfLib.Compose.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Primitives;
using PdfLib.Util;
using PdfLib.Pdf;
using System.Diagnostics;

namespace PdfLib.Compose
{
    /// <summary>
    /// This is intended to be a more user friendly drawing class than IDraw,
    /// with methods like "DrawCircle(size, color, .. ) and "DrawString".
    /// 
    /// There will be two ways of utilizing this class.
    /// 
    /// One is by calling static draw functions. The other will be to
    /// supply an IDraw object to a instance of this class.
    /// 
    /// When using static calls, this class will not keep track of state (I.e.
    /// it will not automatically skip uneeded commands.) And one has to
    /// close paths manually. 
    /// </summary>
    /// <remarks>
    /// Todo: Set Group on PdfPage objects when transparency is detected. 
    ///       Allow _fill_to_append/_stroke_to_append in text mode
    ///       ^ Or go a little more sophisticated
    ///         1. Use DrawCMD
    ///         2. Cache command sequences from calls like DrawString( xxx, color, font)
    ///         3. If a new command sequences comes next
    ///             3.a add a q and submit the cache, along with any further command sequences.
    ///             3.b If a non command sequences comes, do a Q
    ///         4. If a non-command sequences comes, cache a few commands (4 maybe) to see if it overwrites
    ///            color, font, whatever was changed in the command sequence.
    ///            4.a if colors were changed, submit the command sequence and the few cached commands
    ///            4.b if not, submit q/Q around the command sequence
    ///            
    ///         This solution won't need to cahce more than ~10 commands and will prevent streams filled with
    ///         spurios color changes or q/Q well enough. It's not perfect, but not too complex to be a 
    ///         headace.
    /// </remarks>
    public class cDraw : IDisposable
    {
        #region State

        /// <summary>
        /// Current state
        /// </summary>
        private State _cs = new State();
        Stack<State> _state_stack = new Stack<State>();
        cBrush _fill_to_append = null;
        cBrush _stroke_to_append = null;

        private cRenderState _rs;

        #endregion

        #region Variables
        private IDraw _draw;
        private bool _my_draw; //my_draw true just means that the IDRaw was created by this class. 
        private Merge _merge;
        private xPoint _current_point;
        private bool _close_current_path;
        private IWPage _page;
        private Util.WeakCache<IPage, IForm> _cached_forms;

        private bool _draw_down;

        #endregion

        #region Properties

        public bool DrawDownwards
        {
            get { return _draw_down; }
            set 
            {
                if (_page is PdfPage)
                {
                    _draw_down = value;
                    if (value)
                    {
                        _cs.Offset_y = ((PdfPage)_page).MediaBox.Height;
                        var iCTM = 1 / _rs.CTM.M22;
                        _cs.Offset_y *= iCTM;
                    }
                }
            }
        }

        public IDraw RenderTarget { get { return _draw; } }

        public xPoint CurrentPoint { get { return _current_point; } }

        public cBrush FillColor
        {
            get { return _fill_to_append == null ? _rs.fill_color : _fill_to_append; }
            set
            {
                if (value != null)
                {
                    if (_rs.GS == GS.Page)
                        _fill_to_append = value;
                    else if (_rs.GS == GS.Text) //<-- Not yet supported in text mode
                        value.SetColor(_rs, true, _draw);
                    else
                        throw new PdfNotSupportedException("Illegal color change");
                }
            }
        }

        public bool BreakWord
        {
            get { return _rs.BreakWord; }
            set
            {
                if (_rs.GS == GS.Page)
                    _rs.BreakWord = value;
                else
                    throw new PdfNotSupportedException("Illegal break word change");
            }
        }

        public bool SimpleBreakWord
        {
            get { return _rs.SimpleBreakWord; }
            set
            {
                if (_rs.GS == GS.Page)
                    _rs.SimpleBreakWord = value;
                else
                    throw new PdfNotSupportedException("Illegal break word change");
            }
        }

        public cBrush StrokeColor
        {
            get { return _stroke_to_append == null ? _rs.stroke_color : _stroke_to_append; }
            set
            {
                //One can't clear colors in PDF. One could throw a PdfNotSupported exception,
                //but I opt to silently ignore. 
                if (value != null)
                {
                    if (_rs.GS == GS.Page)
                        _stroke_to_append = value;
                    else if (_rs.GS == GS.Text)
                        value.SetColor(_rs, false, _draw);
                    else
                        throw new PdfNotSupportedException("Illegal color change");
                }
            }
        }

        #endregion

        #region Init and dispose

        public cDraw(IDraw draw)
        {
            _draw = draw;
            _rs = new cRenderState();
        }

        public cDraw(List<RenderCMD> cmds)
            : this(new DrawCMD(cmds))
        { _my_draw = true; }

        public cDraw(IWPage page)
            : this(page, Merge.Append)
        { }

        public cDraw(IWPage page, Merge merge)
            : this(new DrawPage(page))
        { _my_draw = true; _merge = merge; _page = page; }

        public void Dispose()
        {
            ET();

            if (_my_draw)
            {
                if (_merge == Merge.Prepend && _draw is DrawPage)
                    ((DrawPage)_draw).Commit(true);
                
                _draw.Dispose();
            }
        }

        #endregion

        #region dynamic methods

        #region CTM

        public PdfRectangle Transform(PdfRectangle r)
        {
            return new PdfRectangle(Transform(new xRect(r)));
        }

        public xRect Transform(xRect r)
        {
            if (_draw_down)
            {
                var rect = new xRect(Transform(r.LowerLeft), Transform(r.UpperRight)).Normalized;
                var ll = new xPoint(rect.LowerLeft.X, rect.UpperRight.Y - rect.Height);
                return new xRect(ll, rect.UpperRight);
            }
            return _rs.CTM.Transform(r);
        }

        public xPoint Transform(xPoint p)
        {
            if (_draw_down)
                p.Y = _cs.Offset_y - p.Y;
            return _rs.CTM.Transform(p);
        }

        public void PrependCTM(xMatrix m) 
        { 
            _rs.PrependCTM(m, _draw);
            var iCTM = 1 / m.M22;
            _cs.Offset_y *= iCTM;
        }

        #endregion

        #region Path creation

        public void MoveTo(xPoint pos)
        {
            MoveTo(pos.X, pos.Y);
        }

        public void MoveTo(double x, double y)
        {
            if (_rs.GS != GS.Path)
            {
                if (_rs.GS != GS.Page)
                {
                    EndModes();
                    _close_current_path = false;
                }

                GSModeChange();
                _rs.GS = GS.Path;
            }
            else if (_close_current_path)
            {
                _draw.ClosePath();
                _close_current_path = false;
            }

            _current_point = new xPoint(x, _draw_down ? _cs.Offset_y - y : y);
            _draw.MoveTo(_current_point.X, _current_point.Y);
        }

        /// <summary>
        /// Draws from the current point
        /// </summary>
        public void LineTo(xVector vec)
        {
            if (_draw_down)
                throw new NotImplementedException();
            LineTo(_current_point.X + vec.X, _current_point.Y + vec.Y);
        }

        /// <summary>
        /// Draws to the given point
        /// </summary>
        /// <param name="pos"></param>
        public void LineTo(xPoint pos)
        {
            LineTo(pos.X, pos.Y);
        }

        public void LineTo(double x, double y)
        {
            if (_rs.GS != GS.Path)
                throw new PdfNotSupportedException();

            if (_close_current_path)
            {
                _draw.ClosePath();
                _close_current_path = false;
            }

            _current_point = new xPoint(x, _draw_down ? _cs.Offset_y - y : y);
            _draw.LineTo(_current_point.X, _current_point.Y);
        }

        public void ClosePath()
        {
            if (_rs.GS != GS.Path)
                throw new PdfNotSupportedException();

            if (!_close_current_path)
                _close_current_path = true;
            else
            {
                _draw.ClosePath();
                _close_current_path = false;
            }
        }

        /// <summary>
        /// NonZero fill of the current path.
        /// </summary>
        public void FillAndStrokePath()
        {
            if (_rs.GS != GS.Path)
                throw new PdfNotSupportedException();
            _draw.DrawPathNZ(_close_current_path, true, true);
            _rs.GS = GS.Page;
        }

        /// <summary>
        /// NonZero fill of the current path.
        /// </summary>
        public void FillPath()
        {
            if (_rs.GS != GS.Path)
                throw new PdfNotSupportedException();
            if (_close_current_path)
                _draw.ClosePath();
            _draw.DrawPathNZ(false, false, true);
            _rs.GS = GS.Page;
        }

        /// <summary>
        /// NonZero stroke of the current path.
        /// </summary>
        public void StrokePath()
        {
            if (_rs.GS != GS.Path)
                throw new PdfNotSupportedException();
            _draw.DrawPathNZ(_close_current_path, true, false);
            _rs.GS = GS.Page;
        }

        #endregion

        #region Text

        public void SetFont(Pdf.Font.PdfFont font, double size)
        {
            SetFont(cFont.Create(font), size);
        }

        public void SetFont(cFont font, double size)
        {
            if (font == null)
                throw new ArgumentNullException();
            _rs.Tf = font;
            _rs.Tfs = size;
            _rs._has_font = false;
        }

        public chTextBox MakeTextBox(double width, double height)
        {
            SetFont();
            return new chTextBox(null, width, height, _rs);
        }

        public chTextBox MakeTextBox(xPoint pos, double width)
        {
            SetFont();
            return new chTextBox(null, width, _rs) { Position = pos };
        }

        public chTextBox MakeTextBox(double width)
        {
            SetFont();
            return new chTextBox(null, width, _rs);
        }

        public chTextBox MakeTextBox()
        {
            SetFont();
            return new chTextBox(null, null, _rs);
        }

        public void DrawTextBox(chTextBox tb)
        {
            if (_rs.GS == GS.Text)
                ET();
            SetFont();
            tb.Layout();
            tb.Render(_draw, ref _rs);
        }

        /// <summary>
        /// Measures a single string. 
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public LineMeasure MeassureString(string str)
        {
            if (str.IndexOf('\n') != -1)
                throw new ArgumentException("String contains newlines");
            SetFont();
            return new chLine(str).Measure(_rs, double.MaxValue);
        }

        public void DrawString(double x, double y, string str, cFont font, double font_size, cBrush color)
        {
            var col = FillColor;
            FillColor = color;
            SetFont(font, font_size);
            DrawString(x, y, str);
            FillColor = col;
        }

        public void DrawString(double x, double y, string str, cFont font, double font_size)
        {
            SetFont(font, font_size);
            DrawString(x, y, str);
        }

        public void DrawString(double x, double y, string str)
        {
            if (_draw_down)
                DrawStringImpl(x, _cs.Offset_y - y, str);
            else
                DrawStringImpl(x, y, str);
        }

        private void DrawStringImpl(double x, double y, string str)
        {
            if (_rs.GS != GS.Text)
                GSModeChange();
            SetFont();
            var tb = new chTextLayout(new chDocument(str), null, null, _rs);
            tb.Layout();
            BT();
            double dx = x - _rs.Tm.OffsetX, dy = y - _rs.Tm.OffsetY;
            if (!Real.IsZero(dx) || !Real.IsZero(dx))
            {
                _rs.TranslateTLM(_draw, x - _rs.Tlm.OffsetX, y - _rs.Tlm.OffsetY);
            }
            tb.RenderText(_draw, _rs, false);
        }

        /// <summary>
        /// Draws a string that wraps on width
        /// </summary>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="width">Wrap position</param>
        /// <param name="str">String to draw</param>
        public void DrawString(double x, double y, double width, string str)
        {
            SetFont();
            var tb = new chTextLayout(new chDocument(str), width, null, _rs);
            tb.Layout();
            BT();
            double dx = x - _rs.Tm.OffsetX, dy = y - _rs.Tm.OffsetY;
            if (!Real.IsZero(dx) || !Real.IsZero(dx))
                _rs.TranslateTLM(_draw, x - _rs.Tlm.OffsetX, y - _rs.Tlm.OffsetY);
            tb.RenderText(_draw, _rs, false);
        }

        #endregion

        #region Image

        public void DrawImage(double x, double y, PdfImage img, double width, double height)
        {
            EndModes();
            _draw.Save();
            _draw.PrependCM(new xMatrix(width, 0, 0, height, x, y));
            _draw.DrawImage(img);
            _draw.Restore();
        }

        public void DrawImage(double x, double y, PdfImage img)
        {
            DrawImage(x, y, img, img.Width, img.Height);
        }

        public void DrawForm(double x, double y, PdfForm form)
        {
            Save();
            if (_draw_down)
                _rs.PrependCTM(new xMatrix(x, _cs.Offset_y - y - form.BBox.Height), _draw);
            else
                _rs.PrependCTM(new xMatrix(x, y), _draw);
            DrawForm(form);
            Restore();
        }

        public void DrawForm(double x, double y, PdfForm form, double width, double height)
        {
            Save();
            if (_draw_down)
            {
                double b_height = form.BBox.Height, scale_y = height / b_height;
                _rs.PrependCTM(new xMatrix(width / form.BBox.Width, 0, 0, scale_y, x, _cs.Offset_y - y - b_height * scale_y), _draw);
            }
            else
                _rs.PrependCTM(new xMatrix(width / form.BBox.Width, 0, 0, height / form.BBox.Height, x, y), _draw);
            DrawForm(form);
            Restore();
        }

        private void DrawForm(PdfForm form)
        {
            if (!_draw.DrawForm(form))
            {
                if (_cached_forms == null)
                    _cached_forms = new Util.WeakCache<IPage, IForm>(4);
                IForm cf;
                if (!_cached_forms.TryGetValue(form, out cf))
                {
                    var res = form.GetResources();
                    if (res == null)
                        res = (_page == null) ? form.Resources : _page.Resources;
                    cf = new CompiledForm(new PdfCompiler(_cached_forms).Compile(form.Contents, res), form.BBox, form.Matrix, form.Group);
                }
                _draw.DrawForm((CompiledForm)cf);
            }
        }

        #endregion

        #region Save/Restore

        public void Save()
        {
            EndModes();
            if (_rs.GS != GS.Page)
                throw new PdfNotSupportedException();

            _rs.Save(_draw);
            _state_stack.Push(_cs);
        }

        public void Restore()
        {
            EndModes();
            if (_rs.GS != GS.Page)
                throw new PdfNotSupportedException();

            _rs = _rs.Restore(_draw);
            _cs = _state_stack.Pop();
        }

        /// <summary>
        /// Should be called before using "RenderTarget", though not needed after Save() or Restore()
        /// </summary>
        public void Flush()
        {
            EndModes();
        }

        #endregion

        /// <summary>
        /// Fills a rectangle with the supplied color. The state will
        /// be modified to this color.
        /// </summary>
        /// <param name="x">Horizontal position</param>
        /// <param name="y">Vertical position</param>
        /// <param name="width">Widht</param>
        /// <param name="height">Height</param>
        /// <param name="fill">Optional color</param>
        public void FillRectangle(double x, double y, double width, double height, cBrush fill)
        {
            EndModes();
            var col = FillColor;
            FillColor = fill;
            GSModeChange();
            if (_draw_down)
                _draw.RectAt(x, _cs.Offset_y - y - height, width, height);
            else
                _draw.RectAt(x, y, width, height);
            _draw.DrawPathNZ(false, false, true);
            FillColor = col;
        }

        /// <summary>
        /// Fills a rectangle with the current color. The state will
        /// be modified to this color.
        /// </summary>
        /// <param name="x">Horizontal position</param>
        /// <param name="y">Vertical position</param>
        /// <param name="width">Widht</param>
        /// <param name="height">Height</param>
        public void FillRectangle(double x, double y, double width, double height)
        {
            EndModes();
            GSModeChange();
            if (_draw_down)
                _draw.RectAt(x, _cs.Offset_y - y - height, width, height);
            else
                _draw.RectAt(x, y, width, height);
            _draw.DrawPathNZ(false, false, true);
        }

        public void FillCircle(double center_x, double center_y, double radious, cBrush fill)
        {
            EndModes();
            var col = FillColor;
            FillColor = fill;
            GSModeChange();
            var diameter = radious * 2;
            cDraw.EllipseAt(_draw, center_x - radious, center_y - radious, diameter, diameter);
            _draw.DrawPathNZ(false, false, true);
            FillColor = col;
        }

        public void FillCircle(double center_x, double center_y, double radious)
        {
            EndModes();
            GSModeChange();
            var diameter = radious * 2;
            cDraw.EllipseAt(_draw, center_x - radious, center_y - radious, diameter, diameter);
            _draw.DrawPathNZ(false, false, true);
        }

        /// <summary>
        /// Fills and strokes an ellipse
        /// </summary>
        public void DrawEllipse(double x, double y, double width, double height)
        {
            EndModes();
            GSModeChange();
            if (_draw_down)
                EllipseAt(_draw, x, _cs.Offset_y - y - height, width, height);
            else
                EllipseAt(_draw, x, y, width, height);
            _draw.DrawPathNZ(false, true, true);
        }

        /// <summary>
        /// Fills and strokes an ellipse
        /// </summary>
        public void DrawRectangle(double x, double y, double width, double height)
        {
            EndModes();
            GSModeChange();
            if (_draw_down)
                _draw.RectAt(x, _cs.Offset_y - y - height, width, height);
            else
                _draw.RectAt(x, y, width, height);
            _draw.DrawPathNZ(false, true, true);
        }

        #endregion

        #region Helper methods



        #endregion

        #region private methods

        //Must be called before any page => mode change, and TODO: also before any
        //text draw command as you can change color during text rendering
        void GSModeChange()
        {
            Debug.Assert(_rs.GS == GS.Page, "This method must only be called in pagemode");

            if (_rs.GS == GS.Page)
            {
                if (_fill_to_append != null)
                {
                    _fill_to_append.SetColor(_rs, true, _draw);
                    _fill_to_append = null;
                }
                if (_stroke_to_append != null)
                {
                    _stroke_to_append.SetColor(_rs, false, _draw);
                    _stroke_to_append = null;
                }
            }
        }

        void BT()
        {
            if (_rs.GS == GS.Page)
            {
                GSModeChange();
                _rs.BeginText(_draw);
            }
            else if (_rs.GS != GS.Text)
                throw new NotSupportedException("Incorrect state. Must be page or text");
        }

        void EndModes()
        {
            if (_rs.GS == GS.Text)
                ET();
        }

        void ET()
        {
            if (_rs.GS == GS.Text)
                _rs.EndText(_draw);
            else if (_rs.GS != GS.Page)
                throw new NotSupportedException("Incorrect state. Must be page or text");
        }

        void SetFont()
        {
            if (!_rs._has_font)
            {
                if (_rs.Tf == null)
                {
                    _rs.Tf = cFont.Create("Helvetica");
                    _rs.Tfs = 12;
                }
                _draw.SetFont(_rs.Tf, _rs.Tfs);
                _rs._has_font = true;
            }
        }

        #endregion

        #region #state support

        struct State
        {
            public double Offset_y;
        }

        #endregion

        #region Static methods

        /// <summary>
        /// Sets the clip rectangle
        /// </summary>
        public static void Clip(IDraw draw, xRect rect)
        {
            RectAt(draw, rect);
            draw.SetClip(xFillRule.Nonzero);
            draw.DrawPathNZ(false, false, false);
        }

        /// <summary>
        /// Quadratic CurveTo
        /// </summary>
        /// <remarks>
        /// See: http://web.archive.org/web/20020209100930/http://www.icce.rug.nl/erikjan/bluefuzz/beziers/beziers/node2.html
        /// </remarks>
        public static void CurveTo(IDraw draw, xPoint cp, double x1, double y1, double x2, double y2)
        {
            double x3 = x2, y3 = y2;

            x2 = x1 + (x2 - x1) / 3;
            y2 = y1 + (y2 - y1) / 3;
            x1 = cp.X + 2 * (x1 - cp.X) / 3;
            y1 = cp.Y + 2 * (y1 - cp.Y) / 3;

            draw.CurveTo(x1, y1, x2, y2, x3, y3);
        }

        /// <summary>
        /// Draws an xLine.
        /// </summary>
        public static void Draw(IDraw draw, xLine line)
        {
            draw.MoveTo(line.Start.X, line.Start.Y);
            draw.LineTo(line.End.X, line.End.Y);
            draw.DrawPathNZ(false, true, false);
        }

        public static bool LineEndAt(IDraw draw, double size, xPoint pos, xVector dir, Pdf.Annotation.PdfLineAnnot.EndStyle style)
        {
            dir = dir.Unit;
            if (size <= 0) size = 1;

            switch (style)
            {
                case Pdf.Annotation.PdfLineAnnot.EndStyle.Circle:
                    {
                        double radius = size * 3;
                        cDraw.CenteredCircleAt(draw, pos.X, pos.Y, radius);
                    }
                    return true;
                case Pdf.Annotation.PdfLineAnnot.EndStyle.ClosedArrow:
                    {
                        double side = size * 4;
                        double len = side * 1.8;
                        xPoint start = pos + dir.Inverse * len;
                        dir = dir * side;
                        xPoint left = start + dir.Rotate(90);
                        xPoint right = start + dir.Rotate(-90);
                        draw.MoveTo(left.X, left.Y);
                        draw.LineTo(pos.X, pos.Y);
                        draw.LineTo(right.X, right.Y);
                        draw.ClosePath();
                    }
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Draws an icon that looks like a note
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="rect">Size and position</param>
        public static void NoteIconAt(IDraw draw, xRect rect)
        {
            RoundRectAt(draw, rect, 1);
            draw.ClosePath();
            var line_w = rect.Width * 0.7;
            var line_off = (rect.Width - line_w) / 2;
            var line_gap = rect.Height * 0.15;
            var x = rect.LowerLeft.X + line_off;
            var y = rect.UpperRight.Y - line_gap / 1.5;
            line_off = x + line_w;

            for (int c = 0; c < 4; c++)
            {
                y -= line_gap;
                draw.MoveTo(x, y);
                draw.LineTo(line_off, y);
            }
        }

        /// <summary>
        /// Draws an xRect.
        /// </summary>
        public static void Draw(IDraw draw, xRect rect)
        {
            draw.RectAt(rect.LowerLeft.X, rect.LowerLeft.Y, rect.Width, rect.Height);
            draw.DrawPathNZ(false, true, false);
        }

        /// <summary>
        /// Draws a compiled line
        /// </summary>
        /// <param name="line">Line to draw</param>
        public static void Draw(IDraw draw, CompiledLine line)
        {
            draw.Execute(line.Text);
        }

        /// <summary>
        /// Draws the raw page commands. Does not set clibbox or anything like that
        /// </summary>
        /// <param name="page">Page to draw</param>
        public static void Draw(IDraw draw, CompiledPage page)
        {
            draw.Execute(page.Commands);
        }

        /// <summary>
        /// Draws blocks and underline
        /// </summary>
        /// <param name="line">Line to that is to be drawn</param>
        public static void DrawBlock(IDraw draw, CompiledLine line)
        {
            DrawBlock(draw, line, DefaultStart());
        }

        /// <summary>
        /// Draws blocks and underline
        /// </summary>
        /// <param name="line">Line to that is to be drawn</param>
        public static void DrawBlock(IDraw draw, CompiledLine line, cRenderState start_state)
        {
            DrawBlock(draw, new cBlock[][] { line.Blocks }, start_state);
        }

        public static void DrawBlock(IDraw draw, List<CompiledLine> lines)
        {
            DrawBlock(draw, lines, DefaultStart());
        }

        public static void DrawBlock(IDraw draw, List<CompiledLine> lines, cRenderState start_state)
        {
            var blocks = new List<cBlock[]>(lines.Count);
            foreach (var line in lines)
                if (line.Blocks != null && line.Blocks.Length > 0)
                    blocks.Add(line.Blocks);
            if (blocks.Count > 0)
                DrawBlock(draw, blocks.ToArray(), start_state);
        }

        public static void DrawBlock(IDraw draw, cBlock[][] blocks_ar, cRenderState org_state)
        {
            if (blocks_ar == null) return;

            int capacity = 2;
            for (int c = 0; c < blocks_ar.Length; c++)
            {
                var blocks = blocks_ar[c];
                for (int i = 0; i < blocks.Length; i++)
                    capacity += blocks[i].Commands.Length + 1;
            }
            if (capacity == 2) return;

            var cmds = new List<RenderCMD>(capacity);

            //We modify the state, and use Tm as if it was Cm
            var state = org_state.Copy();
            cmds.Add(new q_RND());
            if (blocks_ar.Length > 0)
            {
                var blocks = blocks_ar[0];
                if (blocks.Length > 0)
                {
                    var block = blocks[0];
                    state.SetTM(block.Matrix);
                    if (!block.Matrix.IsIdentity)
                        cmds.Add(new cm_RND(block.Matrix));
                }
            }

            //Stroke width is not part of the text state, so we always set it.
            bool has_stroke_width = false;
            double stroke_width = 0;

            for (int c = 0; c < blocks_ar.Length; c++)
            {
                var blocks = blocks_ar[c];
                for (int i = 0; i < blocks.Length; i++)
                {
                    var block = blocks[i];
                    double offx, offy;

                    //We try to avoid chaning the metrix if possible. If the CTM metrix is
                    //identical besides different OffX/Y then we can instead offset the draw
                    //commands and leave the current metrix as is.
                    if (state.Tm.ScaleAndSkewEquals(block.Matrix) && state.Tm.HasInverse)
                    {
                        //Sustracts "to offset" with what has already been offsetted.        
                        if (block.Matrix.OffsetX != state.Tm.OffsetX || block.Matrix.OffsetY != state.Tm.OffsetY)
                        {
                            var point_zero = new xPoint(0, 0);

                            //Correct position.
                            var correct_position = block.Matrix.Transform(point_zero);

                            //Inverted matrixes
                            var iTm = state.Tm.Inverse;

                            //Inverted point
                            var iOffset = iTm.Transform(correct_position);

                            offx = iOffset.X;
                            offy = iOffset.Y;
                        }
                        else
                        {
                            offy = offx = 0;
                        }
                    }
                    else
                    {
                        cmds.Add(new Q_RND());
                        state = org_state.Copy();
                        has_stroke_width = false;
                        cmds.Add(new q_RND());
                        state.SetTM(block.Matrix);
                        cmds.Add(new cm_RND(block.Matrix));
                        offx = 0; offy = 0;
                    }

                    var commands = block.Commands;
                    for (int k = 0; k < commands.Length; k++)
                    {
                        var cmd = commands[k];

                        if (cmd is TextureCMD)
                        {
                            var texture = (TextureCMD)cmd;
                            if (texture.Stroke)
                            {
                                if (!texture.Equals(state.stroke_color))
                                {
                                    state.stroke = true;
                                    state.stroke_color = texture.MakeColor(state.stroke_color == null ? null : state.stroke_color.MyColorSpace);
                                    cmds.Add(cmd);
                                }
                            }
                            else
                                cmds.Add(cmd);
                        }
                        else
                        {
                            if (cmd is cm_RND)
                            {
                                var m = ((cm_RND)cmd).Matrix;

                                //Note, translating will not give the correct result.
                                m = new xMatrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX + offx, m.OffsetY + offy);
                                cmds.Add(new cm_RND(m));
                            }
                            else if (cmd is w_RND)
                            {
                                var w = (w_RND)cmd;
                                if (!has_stroke_width || w.StrokeWidth != stroke_width)
                                {
                                    has_stroke_width = true;
                                    stroke_width = w.StrokeWidth;
                                    cmds.Add(w);
                                }
                            }
                            else if (cmd is m_CMD)
                            {
                                var m = (m_CMD)cmd;
                                cmds.Add(new m_CMD(m.Y + offy, m.X + offx));
                            }
                            else if (cmd is l_CMD)
                            {
                                var l = (l_CMD)cmd;
                                cmds.Add(new l_CMD(l.Y + offy, l.X + offx));
                            }
                            else
                            {
                                cmds.Add(cmd);
                            }
                        }
                    }
                }
            }

            cmds.Add(new Q_RND());
            draw.Execute(cmds.ToArray());
        }

        private static cRenderState DefaultStart()
        {
            cRenderState r = new cRenderState();
            r.fill = false;
            return r;
        }

        /*Simple implementation
        public void DrawBlock(cBlock[][] blocks_ar, cRenderState org_state)
        {
            if (blocks_ar == null) return;

            for (int c = 0; c < blocks_ar.Length; c++)
            {
                var blocks = blocks_ar[c];
                for (int i = 0; i < blocks.Length; i++)
                {
                    Save();

                    var block = blocks[i];
                    PrependCM(block.Matrix);
                    _executor.Execute(block.Commands, this);

                    Restore();
                }
            }
        }         
        */

        /// <summary>
        /// Draws a circle where x,y is at the center
        /// </summary>
        /// <param name="x">Center coordinate</param>
        /// <param name="y">Center coordinate</param>
        /// <param name="radius">Distance from center to edge</param>
        public static void CenteredCircleAt(IDraw draw, double x, double y, double radius)
        {
            CircleAt(draw, x - radius, y - radius, radius + radius);
        }

        /// <summary>
        /// Creates a round circle
        /// </summary>
        public static void CircleAt(IDraw draw, double x, double y, double width)
        {
            EllipseAt(draw, x, y, width, width);
        }

        /// <summary>
        /// Creates an elipse where the coordinate is in the center of the elipse.
        /// </summary>
        /// <param name="x">Center coordinate</param>
        /// <param name="y">Center coordinate</param>
        public static void CenteredEllipseAt(IDraw draw, double x, double y, double width, double height)
        {
            EllipseAt(draw, x - width / 2, y - height / 2, width, height);
        }

        /// <summary>
        /// Makes an elipse (but does not draw or close it)
        /// </summary>
        public static void EllipseAt(IDraw draw, double x, double y, double width, double height)
        {
            //Kappa. This number was obtained from:
            //http://www.whizkidtech.redprince.net/bezier/circle/
            const double κ = 0.5522847498;

            //The circle will be draw by four curves. This is a
            //approximation of a circle, and not a perfec circle.
            //We calculate the needed control points for one arc
            //in the circle, then reuse those calulations for the
            //other three arcs.
            double ox = (width / 2),  //Offset for the horizontal control point
                   oy = (height / 2), //Offset for the vertical control point
                   x_end = x + width,
                   y_end = y + height,
                   x_middle = x + ox,
                   y_middle = y + oy;
            ox *= κ;
            oy *= κ;

            draw.MoveTo(x, y_middle);
            draw.CurveTo(x, y_middle - oy, x_middle - ox, y, x_middle, y);
            draw.CurveTo(x_middle + ox, y, x_end, y_middle - oy, x_end, y_middle);
            draw.CurveTo(x_end, y_middle + oy, x_middle + ox, y_end, x_middle, y_end);
            draw.CurveTo(x_middle - ox, y_end, x, y_middle + oy, x, y_middle);
        }

        /// <summary>
        /// Draws a piecewise "Oval" (A.K.A Elipse) that can be rotated around its center. 
        /// Expcect large files if using this method
        /// </summary>
        /// <param name="center_x">Center coordinate of the oval</param>
        /// <param name="center_y">Center coordinate of the oval</param>
        /// <param name="radius_x">Distance from center to horixontal edge</param>
        /// <param name="radius_y">Distance from center to vertical edge</param>
        /// <param name="rotation">Rotate the oval around its own axis, in radians</param>
        public static void OvalAt(IDraw draw, double center_x, double center_y, double radius_x, double radius_y, double rotation)
        {
            double sin_c = Math.Sin(0), rad_x_sinc = radius_y * sin_c, sin_rot = Math.Sin(rotation * Math.PI);
            double cos_c = Math.Cos(0), rad_y_cosc = radius_x * cos_c, cos_rot = Math.Cos(rotation * Math.PI);
            double rad_x_cos_rot = radius_y * cos_rot, rad_y_cos_rot = radius_x * cos_rot;
            double rad_x_sin_rot = radius_y * sin_rot, rad_y_sin_rot = radius_x * sin_rot;
            var xPos = center_x - sin_c * rad_x_sin_rot + cos_c * rad_y_cos_rot;
            var yPos = center_y + cos_c * rad_y_sin_rot + sin_c * rad_x_cos_rot;
            draw.MoveTo(xPos, yPos);

            for (double c = 0.01; c < (2 * Math.PI); c += 0.01)
            {
                sin_c = Math.Sin(c);
                cos_c = Math.Cos(c);
                xPos = center_x - sin_c * rad_x_sin_rot + cos_c * rad_y_cos_rot;
                yPos = center_y + cos_c * rad_y_sin_rot + sin_c * rad_x_cos_rot;

                draw.LineTo(xPos, yPos);
            }
        }

        /// <summary>
        /// Places a rounded rectangle
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="x">x position</param>
        /// <param name="y">y position</param>
        /// <param name="width">Width</param>
        /// <param name="height">Height</param>
        /// <param name="v_radius">Vertical radius</param>
        /// <param name="h_radius">Horizontal radius</param>
        public static void RoundRectAt(IDraw draw, double x, double y, double width, double height, double corner_radius)
        {
            //Reusing the chTextBox's rounded rect draw function.
            var points = CreatePoints(x, y, width, height, corner_radius, corner_radius);
            chTextBox.DrawRoundRect(points, draw);
        }

        /// <summary>
        /// Places a rounded rectangle
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="x">x position</param>
        /// <param name="y">y position</param>
        /// <param name="width">Width</param>
        /// <param name="height">Height</param>
        /// <param name="v_radius">Vertical radius</param>
        /// <param name="h_radius">Horizontal radius</param>
        public static void RoundRectAt(IDraw draw, double x, double y, double width, double height, double h_radius, double v_radius)
        {
            //Reusing the chTextBox's rounded rect draw function.
            var points = CreatePoints(x, y, width, height, v_radius, h_radius);
            chTextBox.DrawRoundRect(points, draw);
        }

        /// <summary>
        /// Places a rounded rectangle
        /// </summary>
        /// <param name="rect">Rectangle</param>
        /// <param name="corner_radius">Rounding radius</param>
        public static void RoundRectAt(IDraw draw, PdfRectangle rect, double corner_radius)
        {
            RoundRectAt(draw, rect.LLx, rect.LLy, rect.Width, rect.Height, corner_radius);
        }

        /// <summary>
        /// Places a rounded rectangle
        /// </summary>
        /// <param name="rect">Rectangle</param>
        /// <param name="corner_radius">Rounding radius</param>
        public static void RoundRectAt(IDraw draw, xRect rect, double corner_radius)
        {
            RoundRectAt(draw, rect.X, rect.Y, rect.Width, rect.Height, corner_radius);
        }

        /// <summary>
        /// Places a rounded rectangle
        /// </summary>
        /// <param name="rect">Rectangle</param>
        /// <param name="v_radius">Vertical radius</param>
        /// <param name="h_radius">Horizontal radius</param>
        public static void RoundRectAt(IDraw draw, PdfRectangle rect, double h_radius, double v_radius)
        {
            RoundRectAt(draw, rect.LLx, rect.LLy, rect.Width, rect.Height, h_radius, v_radius);
        }

        /// <summary>
        /// Places a rounded rectangle
        /// </summary>
        /// <param name="rect">Rectangle</param>
        /// <param name="v_radius">Vertical radius</param>
        /// <param name="h_radius">Horizontal radius</param>
        public static void RoundRectAt(IDraw draw, xRect rect, double h_radius, double v_radius)
        {
            RoundRectAt(draw, rect.X, rect.Y, rect.Width, rect.Height, h_radius, v_radius);
        }

        /// <summary>
        /// Places a rectangle. (Note, there's no need to close the path)
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="rect">Rectangle</param>
        public static void RectAt(IDraw draw, PdfRectangle rect)
        {
            draw.RectAt(rect.LLx, rect.LLy, rect.Width, rect.Height);
        }

        /// <summary>
        /// Places a rectangle. (Note, there's no need to close the path)
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="rect">Rectangle</param>
        public static void RectAt(IDraw draw, xRect rect)
        {
            draw.RectAt(rect.X, rect.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Sets the stroke color.
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="color">Null, Gray, RGB or CMYK</param>
        public static void SetStrokeColor(IDraw draw, double[] color)
        {
            if (color == null) return;
            if (color.Length == 1)
                draw.SetStrokeColor(color[0]);
            if (color.Length == 3)
                draw.SetStrokeColor(color[0], color[1], color[2]);
            if (color.Length == 4)
                draw.SetStrokeColor(color[0], color[1], color[2], color[3]);
        }

        /// <summary>
        /// Sets the stroke color.
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="color">Null, Gray, RGB or CMYK</param>
        public static void SetFillColor(IDraw draw, double[] color)
        {
            if (color == null) return;
            if (color.Length == 1)
                draw.SetFillColor(color[0]);
            if (color.Length == 3)
                draw.SetFillColor(color[0], color[1], color[2]);
            if (color.Length == 4)
                draw.SetFillColor(color[0], color[1], color[2], color[3]);
        }

        /// <summary>
        /// Draws a path
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="close_path">Whenever to close the current path</param>
        /// <param name="fill">Fills the path with the current fill color</param>
        /// <param name="stroke">Strokes the path with the current fill color</param>
        /// <param name="non_zero">Fill rule to use</param>
        public static void DrawPath(IDraw draw, bool close_path, bool fill, bool stroke, bool non_zero)
        {
            if (close_path)
            {
                if (fill || !stroke)
                {
                    close_path = false;
                    draw.ClosePath();
                }
            }
            if (non_zero)
                draw.DrawPathNZ(close_path, stroke, fill);
            else
                draw.DrawPathEO(close_path, stroke, fill);
        }

        #endregion

        #region Private

        /// <summary>
        /// Creates the 8 points in a rounded rectangle
        /// </summary>
        /// <remarks>
        ///       -P1---------P2
        ///      /              \
        ///     P0              P3
        ///     |               |
        ///     P7              P4
        ///      \              /
        ///       -P6--------P5-
        /// </remarks>
        private static xPoint[] CreatePoints(double x, double y, double width, double height, double h_radius, double v_radius)
        {
            var pts = new xPoint[8];
            //Limiting border radius to half width/height. One could allow for more but
            //one would then have to check if it overlaps the other side.
            double w2 = Math.Min(width / 2, h_radius), h2 = Math.Min(height / 2, v_radius);
            pts[0] = new xPoint(x, y + height - h2);
            pts[1] = new xPoint(x + w2, y + height);
            pts[2] = new xPoint(x + width - w2, y + height);
            pts[3] = new xPoint(x + width, y + height - h2);
            pts[4] = new xPoint(x + width, y + h2);
            pts[5] = new xPoint(x + width - w2, y);
            pts[6] = new xPoint(x + w2, y);
            pts[7] = new xPoint(x, y + h2);
            return pts;
        }

        #endregion

        #region Enums

        /// <summary>
        /// How the data is to be added to the stream
        /// </summary>
        public enum Merge
        {
            /// <summary>
            /// Put ahead of the stream
            /// </summary>
            Prepend,

            /// <summary>
            /// Put behind the stream
            /// </summary>
            Append
        }

        #endregion
    }
}
