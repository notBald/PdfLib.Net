using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf;
using PdfLib.Read;
using PdfLib.Compile;
using PdfLib.Render.Commands;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Compose;
using PdfLib.Compose.Text;

namespace PdfLib.Render.PDF
{
    /// <summary>
    /// This class is used when creating the content streams of PDF files.
    /// </summary>
    /// <remarks>
    /// Should probably set this class internal and use a "frendly" public
    /// wrapper class instead, that class would then keep track of state
    /// and output sutable errors when commands are missused.
    /// 
    /// At the moment a public DrawPage is only needed/used to create testfiles.
    /// </remarks>
    public class DrawPage : IDraw
    {
        #region Variables and properties

        /// <summary>
        /// Precition of real strings
        /// </summary>
        private string _precision = "0.###"; //"0.########"

        /// <summary>
        /// Precition of real strings
        /// </summary>
        public int Precision
        {
            get { return _precision.Length - 2; }
            set
            {
                if (value != _precision.Length - 2)
                {
                    _precision = "0.";
                    for (int c = 0; c < value; c++)
                        _precision += "#";
                }
            }
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

        List<Contents> _contents;
        byte[] _content;
        int _content_pos;
        StringBuilder _sb = new StringBuilder(1024);
        IPage _page;
        XObjectElms _xobject;
        FontElms _font;
        PatternElms _pattern;
        Dictionary<IForm, string> _form_cache = null;

        //public string DebugStr { get { return Read.Lexer.GetString(_content); } }

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

        public byte[] RawContents
        {
            get
            {
                int total_size = 0;
                for (int c = 0; c < _contents.Count; c++)
                    total_size += _contents[c].length;

                byte[] data = new byte[total_size + _content_pos];
                total_size = 0;
                for (int c = 0; c < _contents.Count; c++)
                {
                    Contents cc = _contents[c];
                    Buffer.BlockCopy(cc.contents, 0, data, total_size, cc.length);
                    total_size += cc.length;
                }
                Buffer.BlockCopy(_content, 0, data, total_size, _content_pos);

                return data;
            }
        }

        public string DebugContents { get { return Read.Lexer.GetString(RawContents); } }

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

        internal DrawPage()
        { }

        /// <summary>
        /// Create a draw page object
        /// </summary>
        /// <param name="page">Page one wants to draw to</param>
        public DrawPage(IWPage page)
        {
            _page = page;
            Init();
        }

        public void Dispose()
        {
            if (_content != null && _content_pos > 0) Commit(false);
            _content = null;
            _xobject = null;
            _page = null;
            _font = null;
            _form_cache = null;
        }

        private void Init()
        {
            Running = true;
            _contents = new List<Contents>(1);
            _content = new byte[32768];
            _content_pos = 0;
            _xobject = null;
            _font = null;
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

        #region Render related methods

        /// <summary>
        /// Converts a compiled page into a pdf page
        /// </summary>
        internal void DrawPdfPage(PdfPage page, CompiledPage cpage)
        {
            Init();
            var cmds = cpage.Commands;
            _page = page;

            for (int c = 0; c < cmds.Length && Running; c++)
                cmds[c].Execute(this);

            _contents.Add(new Contents(_content, _content_pos));
            _content = null;

            Commit(false);
                
            //Cleans.
            _contents = null;
            _xobject = null;
            _page = null;
            _font = null;

            //Must clean the form cache since it can't know
            //what document the cached objects bellong to.
            //Perhaps have a query posted on XObjectElems of
            //"do you own this object" kind, but more data is
            //needed to be cached for that.
            _form_cache = null;
        }

        /// <summary>
        /// Commits commands to the PDF page
        /// </summary>
        /// <param name="first">Commits the data before existing data, only relevant for pages</param>
        public void Commit(bool first)
        {
            if (_content != null)
            {
                _contents.Add(new Contents(_content, _content_pos));
                _content = new byte[32768];
                _content_pos = 0;
            }

            if (_page is PdfPage)
            {
                var cont = ((PdfPage)_page).Contents;
                for (int c = 0; Running && c < _contents.Count; c++)
                {
                    Contents cc = _contents[c];

                    byte[] addar = new byte[cc.length];
                    Buffer.BlockCopy(cc.contents, 0, addar, 0, cc.length);

                    if (first)
                        cont.PrependContents(addar, c);
                    else
                        cont.AddContents(addar);
                }
            }
            else if (_page is IWSPage)
                ((IWSPage)_page).SetContents(RawContents);

            _contents.Clear();
        }

        /// <summary>
        /// Appens a string to the content. Assumes the content
        /// can be closed if more is needed.
        /// </summary>
        /// <param name="str">String to append</param>
        void Append(string str)
        {
            if (_content_pos + str.Length >= _content.Length)
                CloseContent(str.Length);

            for (int c = 0; c < str.Length; c++)
                _content[_content_pos++] = (byte)str[c];
        }

        /// <summary>
        /// Appends a formated string to the string builder
        /// </summary>
        void SBAppendFormat(string format, params object[] args)
        {
            //To prevent numbers from being saved with scientific notation
            for (int c = 0; c < args.Length; c++)
            {
                object o = args[c];
                if (o is double)
                {
                    o = ((double)o).ToString(_precision, CultureInfo.InvariantCulture);
                    args[c] = o;
                }
            }

            _sb.AppendFormat(format, args);
        }

        void AppendFormat(string format, params object[] args)
        {
            //To prevent numbers from being saved with scientific notation
            for (int c = 0; c < args.Length; c++)
            {
                object o = args[c];
                if (o is double)
                {
                    o = ((double)o).ToString(_precision, CultureInfo.InvariantCulture);
                    args[c] = o;
                }
            }

            _sb.Length = 0;
            _sb.AppendFormat(format, args);

            if (_content_pos + _sb.Length >= _content.Length)
                CloseContent(_sb.Length);

            //Using StringBuilder.ToString() first may actually be faster. Have not tested.
            for (int c = 0; c < _sb.Length; c++)
                _content[_content_pos++] = (byte)_sb[c];
        }

        /// <summary>
        /// Appends the data in the string builder
        /// </summary>
        void SBAppend()
        {
            if (_content_pos + _sb.Length >= _content.Length)
                CloseContent(_sb.Length);

            //Using StringBuilder.ToString() first may actually be faster. Have not tested.
            for (int c = 0; c < _sb.Length; c++)
                _content[_content_pos++] = (byte)_sb[c];
        }

        /// <summary>
        /// Close the current content stream and create a
        /// new stream.
        /// </summary>
        /// <remarks>
        /// Note that the end of a content stream serves as a delimiter.
        /// I.e. one could remove whitespace at the end of the stream.
        /// 
        /// Also note that streams have to end on a token boundary, 
        /// which is why DrawPage only commits whole tokens at a time. 
        /// 
        /// Drawpage also tries to keeep the command and its parameters
        /// in the same stream, but this isn't needed. A stream can end
        /// in the middle of a command sequence.
        /// </remarks>
        void CloseContent(int min_length)
        {
            // Note, the length limitation is an arbitrary choice. It seems like a good
            // idea to split up large contents streams, but it's not required.
            _contents.Add(new Contents(_content, _content_pos));
            _content = new byte[Math.Max(262144, min_length)];
            _content_pos = 0;
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
            set { AppendFormat("{0} w\n", value); }
        }

        public void SetFlatness(double i)
        {
            AppendFormat("{0} i\n", i);
        }

        public void SetLineJoinStyle(xLineJoin style)
        {
            AppendFormat("{0} j\n", (int)style);
        }

        public void SetLineCapStyle(xLineCap style)
        {
            AppendFormat("{0} J\n", (int)style);
        }

        public void SetMiterLimit(double limit)
        {
            AppendFormat("{0} M\n", limit);
        }

        public void SetStrokeDashAr(xDashStyle ds)
        {
            _sb.Length = 0;
            _sb.Append("[");
            int count = 0;
            if (ds.Dashes.Length > 0)
            {
                while (true)
                {
                    SBAppendFormat("{0}", ds.Dashes[count++]);

                    if (count == ds.Dashes.Length)
                        break;
                    _sb.Append(" ");
                }
            }
            SBAppendFormat("] {0} d\n", ds.Phase);
            SBAppend();
        }

        /// <summary>
        /// Sets the graphic state
        /// </summary>
        /// <param name="gstate">The graphic state</param>
        public void SetGState(PdfGState gstate)
        {
            AppendFormat("/{0} gs\n", _page.Resources.ExtGState.Add(gstate));
        }

        public void SetRI(string ri)
        {
            AppendFormat("/{0} ri\n", ri);
        }

        #endregion

        #region Special graphics state

        public void Save()
        {
            Append("q\n");
            _cs.dc_pos++;
        }

        public void Restore()
        {
            if (_cs.dc_pos > _restore_threshold)
            {
                _cs.dc_pos--;
                Append("Q\n");
            }
        }

        public void PrependCM(xMatrix m)
        {
            AppendFormat("{0} {1} {2} {3} {4} {5} cm\n",
                m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);
        }

        #endregion

        #region Color

        public void SetFillColor(double cyan, double magenta, double yellow, double black)
        {
            AppendFormat("{0} {1} {2} {3} k\n", cyan, magenta, yellow, black);
        }
        public void SetFillColor(double red, double green, double blue)
        {
            AppendFormat("{0} {1} {2} rg\n", red, green, blue);
        }
        public void SetFillColor(double gray)
        {
            AppendFormat("{0} g\n", gray);
        }
        public void SetFillColorSC(double[] color)
        {
            for (int c = 0; c < color.Length; c++)
                AppendFormat("{0} ", color[c]);
            Append("sc\n");
        }
        public void SetFillColor(double[] color)
        {
            for (int c = 0; c < color.Length; c++)
                AppendFormat("{0} ", color[c]);
            Append("scn\n");
        }

        public void SetStrokeColor(double cyan, double magenta, double yellow, double black)
        {
            AppendFormat("{0} {1} {2} {3} K\n", cyan, magenta, yellow, black);
        }
        public void SetStrokeColor(double red, double green, double blue)
        {
            AppendFormat("{0} {1} {2} RG\n", red, green, blue);
        }
        public void SetStrokeColor(double gray)
        {
            AppendFormat("{0} G\n", gray);
        }
        public void SetStrokeColorSC(double[] color)
        {
            for (int c = 0; c < color.Length; c++)
                AppendFormat("{0} ", color[c]);
            Append("SC\n");
        }
        public void SetStrokeColor(double[] color)
        {
            for (int c = 0; c < color.Length; c++)
                AppendFormat("{0} ", color[c]);
            Append("SCN\n");
        }

        public void SetFillCS(IColorSpace cs)
        {
            if (cs is PdfColorSpace)
                AppendFormat("{0} cs\n", cs.ToString());
            else
            {
                if (cs is PatternCS)
                {
                    var pat = (PatternCS)cs;
                    if (pat.UnderCS == null)
                    {
                        AppendFormat("{0} cs\n", cs.ToString());
                        return;
                    }
                }

                AppendFormat("/{0} cs\n", _page.Resources.ColorSpace.Add(cs));
            }
        }

        public void SetStrokeCS(IColorSpace CS)
        {
            if (CS is PdfColorSpace)
                AppendFormat("{0} CS\n", CS.ToString());
            else
            {
                if (CS is PatternCS)
                {
                    var pat = (PatternCS)CS;
                    if (pat.UnderCS == null)
                    {
                        AppendFormat("{0} CS\n", CS.ToString());
                        return;
                    }
                }
                
                AppendFormat("/{0} CS\n", _page.Resources.ColorSpace.Add(CS));
            }
        }

        public void SetFillPattern(PdfShadingPattern pat)
        {
            if (_pattern == null) _pattern = _page.Resources.Pattern;
            AppendFormat("/{0} scn\n", _pattern.Add(pat));
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
            if (_form_cache == null) _form_cache = new Dictionary<IForm, string>(4);
            string name;
            if (!_form_cache.TryGetValue(pat, out name))
            {
                if (_pattern == null) _pattern = _page.Resources.Pattern;
                var form_draw = new DrawPage();
                form_draw.Precision = Precision;
                form_draw.Init();
                PdfTilingPattern pattern;
                name = _pattern.CreatePattern(pat.BBox, pat.Matrix, 
                    pat.XStep, pat.YStep, pat.PaintType, pat.TilingType, out pattern);
                form_draw._page = pattern;
                form_draw._form_cache = _form_cache;
                //No need to Q/q/clip as no state is tracked.
                ((IDraw)this).Executor.Execute(pat.Commands, form_draw);

                pattern.SetContents(form_draw.RawContents);
                _form_cache.Add(pat, name);
            }

            if (pat.PaintType == PdfPaintType.Uncolored)
                for (int c = 0; c < color.Length; c++)
                    AppendFormat("{0} ", color[c]);
            
            AppendFormat("/{0} scn\n", name);
        }

        /// <summary>
        /// Sets a uncompiled tiling pattern.
        /// </summary>
        public void SetFillPattern(double[] color, PdfTilingPattern pat)
        {
            if (_form_cache == null) _form_cache = new Dictionary<IForm, string>(4);
            string name;
            if (!_form_cache.TryGetValue(pat, out name))
            {
                if (_pattern == null) _pattern = _page.Resources.Pattern;
                name = _pattern.Add(pat);
            }
            if (pat.PaintType == PdfPaintType.Uncolored)
                for (int c = 0; c < color.Length; c++)
                    AppendFormat("{0} ", color[c]);

            AppendFormat("/{0} scn\n", name);
        }

        public void SetStrokePattern(PdfShadingPattern pat)
        {
            if (_pattern == null) _pattern = _page.Resources.Pattern;
            AppendFormat("/{0} SCN\n", _pattern.Add(pat));
        }

        public void SetStrokePattern(double[] color, CompiledPattern pat)
        {
            if (_form_cache == null) _form_cache = new Dictionary<IForm, string>(4);
            string name;
            if (!_form_cache.TryGetValue(pat, out name))
            {
                if (_pattern == null) _pattern = _page.Resources.Pattern;
                var form_draw = new DrawPage();
                form_draw.Precision = Precision;
                form_draw.Init();
                PdfTilingPattern pattern;
                name = _pattern.CreatePattern(pat.BBox, pat.Matrix,
                    pat.XStep, pat.YStep, pat.PaintType, pat.TilingType, out pattern);
                form_draw._page = pattern;
                form_draw._form_cache = _form_cache;
                //No need to Q/q/clip as no state is tracked.
                ((IDraw)this).Executor.Execute(pat.Commands, this);

                pattern.SetContents(form_draw.RawContents);
                _form_cache.Add(pat, name);
            }

            if (pat.PaintType == PdfPaintType.Uncolored)
                for (int c = 0; c < color.Length; c++)
                    AppendFormat("{0} ", color[c]);

            AppendFormat("/{0} SCN\n", name);
        }

        /// <summary>
        /// Sets a uncompiled tiling pattern.
        /// </summary>
        public void SetStrokePattern(double[] color, PdfTilingPattern pat)
        {
            if (_form_cache == null) _form_cache = new Dictionary<IForm, string>(4);
            string name;
            if (!_form_cache.TryGetValue(pat, out name))
            {
                if (_pattern == null) _pattern = _page.Resources.Pattern;
                name = _pattern.Add(pat);
            }
            if (pat.PaintType == PdfPaintType.Uncolored)
                for (int c = 0; c < color.Length; c++)
                    AppendFormat("{0} ", color[c]);

            AppendFormat("/{0} SCN\n", name);
        }

        #endregion

        #region Shading patterns

        public void Shade(PdfShading shading)
        {
            AppendFormat("/{0} sh\n", _page.Resources.Shading.Add(shading));
        }

        #endregion

        #region Inline images

        public void DrawInlineImage(PdfImage img)
        {
            var ms = new MemoryStream(512);
            var writer = new PdfWriter(ms, SaveMode.Compressed, PM.None, CompressionMode.None, PdfVersion.V12);
            img.WriteInline(writer, _page.Resources);
            var new_bytes = ms.ToArray();
            ms.Close();
            if (_content_pos + new_bytes.Length >= _content.Length)
                CloseContent(new_bytes.Length);
            Buffer.BlockCopy(new_bytes, 0, _content, _content_pos, new_bytes.Length);
            _content_pos += new_bytes.Length;
            Append("\n");
        }

        #endregion

        #region XObjects

        public void DrawImage(PdfImage img)
        {
            if (_xobject == null)
                _xobject = _page.Resources.XObject;
            AppendFormat("/{0} Do\n", _xobject.AddImg(img));
        }

        /// <summary>
        /// Draws a form. 
        /// </summary>
        /// <param name="form">Form to draw</param>
        /// <returns>True if the form was drawn, false if this function is unsuported</returns>
        public bool DrawForm(PdfForm form)
        {
            if (_xobject == null) _xobject = _page.Resources.XObject;
            if (_form_cache == null) _form_cache = new Dictionary<IForm, string>();
            string name;
            if (!_form_cache.TryGetValue(form, out name))
            {
                name = _xobject.AddForm(form);
                _form_cache.Add(form, name);
            }
            AppendFormat("/{0} Do\n", name);
            return true;
        }

        public void DrawForm(CompiledForm img)
        {
            if (_xobject == null) _xobject = _page.Resources.XObject;
            if (_form_cache == null) _form_cache = new Dictionary<IForm, string>(4);
            string name;
            if (!_form_cache.TryGetValue(img, out name))
            {
                var form_draw = new DrawPage();
                form_draw.Precision = Precision;
                form_draw.Init();
                PdfForm form;
                name = _xobject.CreateForm(new PdfRectangle(img.BBox), img.Matrix, img.Group, out form);
                form_draw._page = form;
                form_draw._form_cache = _form_cache;
                //No need to Q/q/clip as no state is tracked.
                ((IDraw)this).Executor.Execute(img.Commands, this);

                form.SetContents(form_draw.RawContents);
                _form_cache.Add(img, name);
            }
            AppendFormat("/{0} Do\n", name);
        }

        #endregion

        #region Path construction

        public void MoveTo(double x, double y) { AppendFormat("{0} {1} m ", x, y); }
        public void LineTo(double x, double y) { AppendFormat("{0} {1} l ", x, y); }
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            AppendFormat("{0} {1} {2} {3} {4} {5} c ",
                x1, y1, x2, y2, x3, y3);
        }
        public void CurveTo(double x1, double y1, double x3, double y3)
        {
            AppendFormat("{0} {1} {2} {3} y ",
                x1, y1, x3, y3);
        }
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            AppendFormat("{0} {1} {2} {3} v ",
                x2, y2, x3, y3);
        }
        public void RectAt(double x, double y, double width, double height)
        {
            AppendFormat("{0} {1} {2} {3} re ",
                x, y, width, height);
        }

        public void ClosePath()
        {
            Append("h\n");
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
                        Append("b\n");
                    else
                        Append("B\n");
                }
                else
                {
                    if (close)
                        Append("s\n");
                    else
                        Append("S\n");
                }
            }
            else
            {
                if (fill)
                {
                    if (close)
                        throw new PdfNotSupportedException("Can't close and fill");
                    else
                        Append("f\n");
                }
                else
                {
                    if (close)
                        throw new PdfNotSupportedException("Use ClosePath instead");
                    else
                        Append("n\n");
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
                        Append("b*\n");
                    else
                        Append("B*\n");
                }
                else
                {
                    if (close)
                        Append("s\n");
                    else
                        Append("S\n");
                }
            }
            else
            {
                if (fill)
                {
                    if (close)
                        throw new PdfNotSupportedException("Can't close and fill");
                    else
                        Append("f*\n");
                }
                else
                {
                    if (close)
                        throw new PdfNotSupportedException("Use ClosePath instead");
                    else
                        Append("n\n");
                }
            }
        }

        #endregion

        #region Clipping path

        public void SetClip(xFillRule rule)
        {
            if (rule == xFillRule.Nonzero)
                Append("W ");
            else
                Append("W* ");
        }

        #endregion

        #region Text Objects

        public void BeginText() { Append("BT\n"); }
        public void EndText() { Append("ET\n"); }

        #endregion

        #region Text State

        public void SetCharacterSpacing(double tc)
        {
            AppendFormat("{0} Tc\n", tc);
        }

        public void SetWordSpacing(double s)
        {
            AppendFormat("{0} Tw\n", s);
        }

        public void SetFont(cFont font, double size)
        {
            SetFont(font.MakeWrapper(), size);
        }

        public void SetFont(PdfFont font, double size)
        {
            if (_font == null) _font = _page.Resources.Font;
            AppendFormat("/{0} {1} Tf\n", _font.Add(font), size);
        }

        public void SetFont(CompiledFont font, double size)
        {
            //Cheats a bit. Should really execute the font in
            //case something has been changed.
            //
            //Look into DrawDC.SetFont and the DrawPage.DrawForm
            //for how execution is to be done, however PdfLib
            //does not yet support modifying Type3 fonts so this 
            //is safe for now.
            SetFont(font.Font, size);
        }

        public void SetTextMode(xTextRenderMode mode)
        {
            AppendFormat("{0} Tr\n", (int) mode);
        }

        /// <summary>
        /// The percentage of normal width (default 100)
        /// </summary>
        public void SetHorizontalScaling(double th)
        {
            AppendFormat("{0} Tz\n", th);
        }

        public void SetTextLeading(double lead)
        {
            AppendFormat("{0} TL\n", lead);
        }

        public void SetTextRise(double tr)
        {
            AppendFormat("{0} Ts\n", tr);
        }

        #endregion

        #region Text positioning

        public void SetTM(xMatrix m)
        {
            AppendFormat("{0} {1} {2} {3} {4} {5} Tm\n",
                m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);
        }

        public void TranslateTLM(double x, double y)
        {
            AppendFormat("{0} {1} Td\n", x, y);
        }

        public void TranslateTLM()
        {
            Append("T*\n");
        }

        public void SetTlandTransTLM(double x, double y)
        {
            AppendFormat("{0} {1} TD\n", x, y);
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
            if (text.IsHex)
                AppendFormat("{1} {2} <{0}> \"\n", Lexer.GetString(text.RawString), aw, ac);
            else
                AppendFormat("{1} {2} ({0}) \"\n", Lexer.GetString(text.RawString), aw, ac);
        }

        public void DrawString(PdfString text, bool cr)
        {
            if (cr)
            {
                if (text.IsHex)
                    AppendFormat("<{0}> \'\n", Lexer.GetString(text.RawString));
                else
                    AppendFormat("({0}) \'\n", Lexer.GetString(text.RawString));
            }
            else
            {
                if (text.IsHex)
                    AppendFormat("<{0}> Tj\n", Lexer.GetString(text.RawString));
                else
                    AppendFormat("({0}) Tj\n", Lexer.GetString(text.RawString));
            }
        }

        public void DrawString(object[] text)
        {
            Append("[");
            int count = 0;
            if (text.Length > 0)
            {
                while (true)
                {
                    var o = text[count++];
                    if (o is double)
                        AppendFormat("{0}", (double)o);
                    else if (o is int)
                        AppendFormat("{0}", (int)o);
                    else
                    {
                        var str = (PdfString)o;
                        if (str.IsHex)
                            AppendFormat("<{0}>", Lexer.GetString(str.RawString));
                        else
                            AppendFormat("({0})", Lexer.GetString(str.RawString));
                    }

                    if (count == text.Length)
                        break;
                    Append(" ");
                }
            }
            Append("] TJ\n");
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
            AppendFormat("{0} {1} d0\n", wx, wy);
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
            AppendFormat("{0} {1} {2} {3} {4} {5} d1\n", wx, wy, llx, lly, urx, ury);
        }

        #endregion

        #region Compatibility

        public void BeginCompatibility() { Append("BX\n"); }
        public void EndCompatibility() { Append("EX\n"); }

        #endregion

        #region Structs

        struct Contents 
        {
            public readonly byte[] contents;
            public readonly int length;
            public Contents(byte[] cont, int pos) { contents = cont; length = pos; }
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
        }

        #endregion
    }
}
