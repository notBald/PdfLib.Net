using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Read;
using PdfLib.Read.Parser;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Render;
using PdfLib.Render.PDF;
using PdfLib.Render.Commands;
using PdfLib.Pdf.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Annotation;
using PdfLib.Compose;
using PdfLib.Compose.Text;

namespace PdfLib.Compile
{
    /// <summary>
    /// Compiles a PdfDocument into a command set the renderer
    /// and saver can understand
    /// </summary>
    /// <remarks>
    /// Only pages intended to be displayed needs to be compiled. 
    /// </remarks>
    public class PdfCompiler
    {
        #region Variables

        /// <remarks>
        /// This stack contains parameters for commands 
        /// </remarks>
        private Stack<object> _stack;

        /// <remarks>
        /// This stack contains marked contents information 
        /// </remarks>
        private Stack<MCInfo> _mc_stack;

        /// <summary>
        /// List of cmds, needed for the marked contents
        /// implementation.
        /// </summary>
        private List<RenderCMD> _cmds;

        /// <summary>
        /// Used for parsing objects.
        /// </summary>
        private IIParser _parse;

        /// <summary>
        /// Used for parsing command keywords.
        /// </summary>
        private Lexer _snize;

        /// <summary>
        /// Resources for the page being compiled
        /// </summary>
        private PdfResources _res;

        /// <summary>
        /// Whenever unrecognized commands should cause erors or not.
        /// </summary>
        private int _comp_mode;

        /// <summary>
        /// Current graphic state
        /// </summary>
        private GS _gs;

        /// <summary>
        /// Used for generating error messages and recovering
        /// from exceptions.
        /// </summary>
        private WorkState _ws;

        /// <summary>
        /// Current compiler state
        /// </summary>
        private CompilerState _cs;
        private Stack<CompilerState> _state_stack;

        private Util.WeakCache<IPage, IForm> _cached_forms;

        /// <summary>
        /// Precision detected during compiling
        /// </summary>
        private int _detected_precision;

        /// <summary>
        /// The preicision needed to save these commands without
        /// loss of precision.
        /// </summary>
        //public int DetectedPrecision { get { return _detected_precision; } }

        #endregion

        #region Properties

        /// <summary>
        /// Number of pages in the document
        /// </summary>
        //public int NumPages { get { return _doc.NumPages; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="doc">Document intended to be compiled</param>
        public PdfCompiler()
        {
            //_compiled_doc = new PdfDocument();
            _cached_forms = null;
        }

        internal PdfCompiler(Util.WeakCache<IPage, IForm> forms)
        {
            _cached_forms = forms;
        }

        /// <summary>
        /// Readies the compiler
        /// </summary>
        private void Start()
        {
            //The commands that renders the document
            _cmds = new List<RenderCMD>(256);

            //These objects contain persistant state info.
            _stack = new Stack<object>(12);
            _mc_stack = new Stack<MCInfo>(1);
            _comp_mode = 0;
            _gs = GS.Page;
            _ws = WorkState.Compile;
            _state_stack = new Stack<CompilerState>(4);
            _cs = new CompilerState();
            _cs.cs = DeviceGray.Instance;
            _cs.CS = DeviceGray.Instance;
        }

        /// <summary>
        /// Cleans. Not really needed, but the compiler object can
        //  sit arround and this avoids it holding any pages in memory.
        /// </summary>
        private void Clean()
        {
            _stack = null;
            _mc_stack = null;
            _state_stack = null;
            _cs = new CompilerState();
        }

        #endregion

        /// <summary>
        /// Compiles a page
        /// </summary>
        /// <param name="page">Page to compile</param>
        /// <param name="ignore_annotations">
        /// If set true annotations will not be compiled</param>
        /// <remarks>This function is not reentrant.</remarks>
        public CompiledPage Compile(PdfPage page, bool ignore_annotations, bool disconnect)
        {
            _detected_precision = 0;

            //Disconnects the page from the document.
            if (disconnect)
                ((ICRef)page).LoadResources(new HashSet<object>());

            //The page's content, which will be parsed
            var content = page.Contents;

            //Resources are needed for parsing.
            _res = page.Resources;

            var start = DateTime.Now;
            Start();

            //content can be null, in which case the page is blank
            if (content != null)
            {
                Parse(content, _cmds);

                if (_gs != GS.Page)
                {
                    //Repport: Open command.
                }
            }

            //Handles page annotations
            // Files to test: form12a_filled.pdf (pluss others in that directory)
            var annots = page.Annots;
            CompiledAnnotation[] c_annots = null;
            if (!ignore_annotations && annots != null)
                c_annots = Compile(annots);

            Clean();
            var end = DateTime.Now;

            Debug.WriteLine("Compile Time: " + (end - start).TotalSeconds.ToString());

            return new CompiledPage(page, _cmds.ToArray(), c_annots, _detected_precision);
        }

        /// <summary>
        /// Compiles a contents object
        /// </summary>
        /// <remarks>
        /// Forms are similar to pages, 
        /// </remarks>
        internal RenderCMD[] Compile(PdfContent content, PdfResources res)
        {
            _res = res;
            Start();
            Parse(new PdfContentArray(content, false, null), _cmds);

            if (_gs != GS.Page)
            {
                //Repport: Open command.
            }
            if (_state_stack.Count != 0)
            {
                //Repport: State open
            }
            Clean();

            return _cmds.ToArray();
        }

        /// <summary>
        /// Compiles a pattern
        /// </summary>
        /// <remarks>
        /// Forms are similar to pages, 
        /// </remarks>
        public CompiledPattern Compile(PdfTilingPattern pat)
        {
            var content = pat.Contents;
            _res = pat.Resources;
            Start();
            Parse(new PdfContentArray(content, false, null), _cmds);

            if (_gs != GS.Page)
            {
                //Repport: Open command.
            }
            if (_state_stack.Count != 0)
            {
                //Repport: State open
            }
            Clean();

            return new CompiledPattern(_cmds.ToArray(), pat.BBox, pat.Matrix, 
                pat.XStep, pat.YStep, pat.PaintType, pat.TilingType);
        }

        private CompiledForm Compile(PdfForm xform)
        {
            if (_cached_forms == null)
                _cached_forms = new Util.WeakCache<IPage, IForm>(4);
            IForm cf;
            if (!_cached_forms.TryGetValue(xform, out cf))
            {
                PdfResources res = xform.GetResources();
                if (res == null)
                {
                    //If the resources dictionary is omitted, the page's resource
                    //dictionary is implied.
                    res = _res;
                }
                return new CompiledForm(new PdfCompiler(_cached_forms).Compile(xform.Contents, res), xform.BBox, xform.Matrix, xform.Group); ;
            }
            return (CompiledForm)cf;
        }

        internal CompiledAnnotation Compile(PdfMarkupAnnotation annot)
        {
            return Compile(new AnnotationElms(annot))[0];
        }

        private CompiledAnnotation Compile(PdfMarkupAnnotation annot, List<RenderCMD> commands, DrawCMD idraw, cRenderState crs)
        {
            //This is the full size (and position) of the annotation
            var clip = new xRect(annot.Rect);

            #region Appernace stream

            var ap = annot.AP;
            if (ap != null)
            {
                //Compiles all apperance streams
                var ca = new CompiledAppearance(
                    Compile(ap.N_Form, ap.N_Dict),
                    Compile(ap.R_Form, ap.R_Dict),
                    Compile(ap.D_Form, ap.D_Dict),
                    annot.AS);

                return new CompiledAnnotation(ca, annot.F, clip, annot.Subtype);
            }

            #endregion

            //Determines how the annot is to be painted
            PdfAnnotBorder border = annot.Border;

            #region PdfTextAnnot

            if (annot is PdfTextAnnot)
            {
                var text = (PdfTextAnnot)annot;
                var pos = text.Rect;
                var rect = new xRect(pos.LLx, pos.URy - 20, pos.LLx + 20 * 0.77, pos.URy);
                cDraw.SetFillColor(idraw, text.C);
                cDraw.NoteIconAt(idraw, rect);
                idraw.DrawPathNZ(false, true, text.C != null);

                return new CompiledAnnotation(commands.ToArray(), annot.F | AnnotationFlags.NoZoom | AnnotationFlags.NoRotate, clip, rect, annot.Subtype);
            }
            #endregion
            #region PdfSquareAnnot and PdfCircleAnnot
            else if (annot is PdfSquareAnnot || annot is PdfCircleAnnot)
            {
                var square = (PdfGeometricAnnot)annot;

                //Notes:
                // - In PDF 1.5 it's possible to create "cloudy" borders. This is not supported.
                // - Adobe Reader XI does not appear to respect the "Border Radius" setting.

                //Defines the border style.
                bool draw_border = border != null && border.BorderWidth > 0 && square.C != null && square.C.Length > 0;
                double h_radius = 0, v_radius = 0;
                var padding = (annot is PdfSquareAnnot) ? ((PdfSquareAnnot)square).RD : ((PdfCircleAnnot)square).RD;
                var rect = padding == null ? clip : new xRect(clip.X + padding.LLx, clip.Y + padding.LLy, clip.Right - padding.URx, clip.Top - padding.URy);

                //Sets border properties
                if (draw_border)
                {
                    if (border.Dashstyle != null)
                        idraw.SetStrokeDashAr(border.Dashstyle.Value);
                    var bw = border.BorderWidth;
                    idraw.StrokeWidth = bw;
                    cDraw.SetStrokeColor(idraw, annot.C);
                    h_radius = border.HorizontalRadius;
                    v_radius = border.VerticalRadius;
                    bw /= 2;
                    rect = new xRect(rect.X + bw, rect.Y + bw, rect.Right - bw, rect.Top - bw);
                }

                //Sets background properties
                var internal_color = square.IC;
                cDraw.SetFillColor(idraw, internal_color);

                //Draws
                if (annot is PdfSquareAnnot)
                {
                    if (h_radius > 0 || v_radius > 0)
                    {
                        cDraw.RoundRectAt(idraw, rect, h_radius, v_radius);
                        cDraw.DrawPath(idraw, true, internal_color != null, draw_border, true);
                    }
                    else
                    {
                        cDraw.RectAt(idraw, rect);
                        cDraw.DrawPath(idraw, false, internal_color != null, draw_border, true);
                    }
                }
                else
                {
                    cDraw.EllipseAt(idraw, rect.X, rect.Y, rect.Width, rect.Height);
                    cDraw.DrawPath(idraw, true, internal_color != null, draw_border, true);
                }

                return new CompiledAnnotation(commands.ToArray(), annot.F, clip, rect, annot.Subtype);
            }
            #endregion
            #region PdfFreeTextAnnot
            else if (annot is PdfFreeTextAnnot)
            {
                var text = (PdfFreeTextAnnot)annot;
                var cs = crs.Copy();
                try
                {
                    var da = text.DA;
                    var comp = new PdfCompiler();
                    comp.Start();
                    var cmds = comp.Parse(Lexer.GetBytes(text.DA));
                    var sd = new Render.WPF.StateDraw(idraw, cs);
                    sd.Execute(cmds.ToArray());
                }
                catch (PdfReadException)
                {
                    //This is a required property, but Adobe renders the
                    //text anyway.
                    idraw.SetFont(cs.Font, cs.FontSize);
                }

                var cl = text.CL;
                var rect = new xRect(text.Rect);
                if (border.BorderWidth > 0)
                {
                    var col = annot.C;
                    if (col != null)
                        cDraw.SetStrokeColor(idraw, annot.C);
                    else if (cs.FillColor != null)
                        cs.FillColor.SetColor(cs, false, idraw);

                    var bw = border.BorderWidth;
                    idraw.StrokeWidth = bw;

                    if (border.Dashstyle != null)
                        idraw.SetStrokeDashAr(border.Dashstyle.Value);   

                    if (cl != null)
                    {
                        var pos = cl.Start;
                        idraw.MoveTo(pos.X, pos.Y);
                        var knee = cl.Knee;
                        if (knee != null)
                            idraw.LineTo(knee.Value.X, knee.Value.Y);
                        pos = cl.End;
                        idraw.LineTo(pos.X, pos.Y);

                        //idraw.DrawPathNZ(false, true, false);
                    }              
                        
                    var h_radius = border.HorizontalRadius;
                    var v_radius = border.VerticalRadius;
                    if (h_radius > 0 || v_radius > 0)
                    {
                        idraw.SetClip(xFillRule.Nonzero);
                        cDraw.RoundRectAt(idraw, rect, h_radius, v_radius);
                    }
                    else
                        cDraw.RectAt(idraw, rect);
                    cDraw.DrawPath(idraw, false, false, true, true);
                    rect = new xRect(rect.X + bw, rect.Y + bw, rect.Right - bw * 2, rect.Top - bw * 2);
                }

                var tb = new oldTextBox((chDocument)null, rect.Width, rect.Height, cs);
                tb.PositionOver = true;
                if (cs.Font is Compose.Font.cBuiltInFont)
                    tb.DefaultLineHeight = 1.4; //Built in fonts have smalish line heights
                tb.StripSideBearings = false;
                tb.Position = rect.LowerLeft;
                tb.Document = new chDocument(text.Contents);
                //tb.Layout();
                tb.Render(idraw, ref cs);

                return new CompiledAnnotation(commands.ToArray(), annot.F, clip, rect, annot.Subtype);
            }
            #endregion
            #region PdfLineAnnot
            else if (annot is PdfLineAnnot)
            {
                //A single line. Can have text, arrows and such.
                var line = (PdfLineAnnot)annot;

                //The line's metrix
                xLine lm = line.L;

                //Border determines the stokewidth and dash style
                if (border != null)
                {
                    if (border.Dashstyle != null)
                        idraw.SetStrokeDashAr(border.Dashstyle.Value);
                    var bw = border.BorderWidth;
                    idraw.StrokeWidth = border.BorderWidth;
                }

                //Color to draw with
                var color = annot.C;
                if (color != null)
                    cDraw.SetStrokeColor(idraw, color);

                //The value in LL moves the line.
                var ll = line.LL;
                if (ll != 0)
                    lm = new xLine(lm.PendicularStart(ll, false).End, lm.PendicularEnd(ll, false).End);

                //Lines with cap == true is to have the text of the Contents displayed
                if (line.Cap && line.Contents != null)
                {
                    //Creates a document from the contents string
                    var content = new chDocument(line.Contents);
                    foreach (var par in content)
                        par.Alignment = chParagraph.Alignement.Middle;

                    //Sets up the text state
                    idraw.SetFont(crs.Tf, crs.Tfs);

                    //This textbox will automatically size itself to fit the text
                    var text_box = new oldTextBox(content, null, crs) { PositionOver = true, ParagraphGap = 0.5 };
                    //text_box.BorderColor = cColor.GREEN;
                    //text_box.BorderTichness = 4;

                    //Text is to be drawn over the center of the line.                        
                    var text_pos_on_line = lm.MidPoint;

                    //Line segment from start to midpoint
                    var mid_line = new xLine(lm.Start, text_pos_on_line);

                    //Rotates the text
                    text_box.RotationAngle = lm.Vector.Angle;
                    text_box.RotationPoint = new xPoint();

                    //The full width of the text
                    //text_box.Layout();
                    var text_width = text_box.Width;
                    var text_height = text_box.Height;

                    //Offsets the text (Note that co isn't a point, it's distances)
                    //Seems to override "inline" when set.
                    var co = line.CO;

                    if (line.CP == PdfLineAnnot.CaptionPositioning.Top || co.X != 0 || co.Y != 0)
                    {
                        var Y = Math.Max(0, Math.Abs(co.Y) - 5) * ((co.Y < 0) ? -1 : 1); //Distances under 5 does not appear to do anything in Adobe
                        bool has_Y = Y != 0;
                        if (Y < 0)
                            Y -= (2 + text_height);
                        else
                            Y -= text_height / 2;
                        if (co.X != 0 || has_Y)
                        {
                            //Start to offset point distance
                            var s_to_x = mid_line.Length + co.X - text_width / 2;

                            //Line segment from start to offset point
                            var ls = new xLine(lm.Start, lm.PointOnLine(s_to_x));

                            //Finds the line going to the offset point.
                            ls = ls.PendicularEnd(Y, false);

                            //Sets the new position for the text
                            text_pos_on_line = ls.End;
                        }
                        else
                        {
                            //Centers the text
                            var distance = mid_line.Length - text_width / 2;
                            text_pos_on_line = lm.PointOnLine(distance);
                        }

                        //Text is to be black
                        idraw.SetFillColor(0);

                        //Draws the text
                        text_box.Position = text_pos_on_line;
                        text_box.Render(idraw, ref crs);

                        //The line is to be drawn on top of the text. Adobe does
                        //not draw the line if the color isn't set. (Though it
                        //draws the text)
                        if (color != null)
                        {
                            //cDraw.SetStrokeColor(idraw, color);
                            cDraw.Draw(idraw, lm);
                        }

                        //Draws annotation line.
                        if (color != null && Y != 0)
                        {
                            //Height is adjusted depending on the text's position.
                            var text_line = new xLine(text_pos_on_line, mid_line.PendicularEnd(Y, false).End);

                            //Checks the direction of the text line. If the text is going in the same
                            //direction as the line, it means that the text is passed the mid point, thus
                            //it can be assumed to be "not over".
                            bool text_over;
                            bool same_direction = Util.Real.Same(text_line.Vector.Angle, mid_line.Vector.Angle);
                            if (text_line.Length <= 3)
                                text_over = true;
                            else if (!same_direction)
                                text_over = false;
                            else
                            {
                                text_over = text_width + 3 >= text_line.Length;
                            }

                            if (text_over)
                            {
                                if (Y < 0)
                                {
                                    //Adjusts Y so that it doesn't overlap the text
                                    Y += text_height;
                                }
                            }
                            else
                            {
                                //Adjust the height so that the line reaches up to the half-point of the text
                                if (Y < 0)
                                    Y += text_height / 2;
                                else
                                    Y -= text_height / 2;
                            }

                            //The annotation line extends from the mid_point and is pendicular to the line.
                            var annot_line = mid_line.PendicularEnd(Y, false);
                            cDraw.Draw(idraw, annot_line);

                            //Adobe draws an additional line when the text is "too far away".
                            if (!text_over)
                            {
                                var parralell_line = new xLine(annot_line.End, mid_line.PendicularStart(Y, false).End);
                                var annot_segment = new xLine(parralell_line.Start, parralell_line.PointOnLine(same_direction ? text_line.Length - (text_width + 1) : -text_line.Length));
                                cDraw.Draw(idraw, annot_segment);
                            }
                        }
                    }
                    else
                    {
                        //Text is inside the line.
                        if (!Util.Real.IsZero(text_width))
                        {
                            //Pads the text a little bit.
                            text_box.PaddingRight = 2;
                            text_box.PaddingLeft = 2;
                            text_width += 4;

                            //Centers the text
                            var distance = mid_line.Length - text_width / 2;
                            text_pos_on_line = lm.PointOnLine(distance);

                            //Adjust the text so that it appears in the middle of the line.
                            var text_pos_off_line = new xLine(text_pos_on_line, lm.End).PendicularStart(text_height, true).Start;

                            //Draws the text
                            text_box.Position = text_pos_off_line;
                            text_box.Render(idraw, ref crs);

                            //Draws the line (if the color is set)
                            if (color != null && lm.Length > text_width)
                            {
                                //Draws from start of line to start of text
                                var lm_to_text = new xLine(lm.Start, text_pos_on_line);
                                cDraw.Draw(idraw, lm_to_text);

                                //Draws from end of text to end of line
                                var text_to_lm = new xLine(lm.PointOnLine(lm_to_text.Length + text_width), lm.End);
                                cDraw.Draw(idraw, text_to_lm);
                            }
                        }
                        else
                        {
                            //Draws the line, as is, when text width is zero.
                            cDraw.Draw(idraw, lm);
                        }
                    }
                }
                else
                {
                    //Draws the line
                    cDraw.Draw(idraw, lm);
                }

                //Draws additional annotation lines
                if (ll != 0 && color != null)
                {
                    cDraw.Draw(idraw, lm.PendicularStart(-ll, false));
                    cDraw.Draw(idraw, lm.PendicularEnd(-ll, false));

                    var lle = line.LLE;
                    if (lle > 0)
                    {
                        cDraw.Draw(idraw, lm.PendicularStart(lle, false));
                        cDraw.Draw(idraw, lm.PendicularEnd(lle, false));
                    }
                }

                //Draw line endings
                var le = line.LE;
                if (le.Start != PdfLineAnnot.EndStyle.None || le.End != PdfLineAnnot.EndStyle.None)
                {
                    var ic = line.IC;
                    if (ic != null)
                        cDraw.SetFillColor(idraw, ic);
                    var c = line.C;
                    if (c != null)
                        cDraw.SetStrokeColor(idraw, c);
                    bool draw = cDraw.LineEndAt(idraw, border.BorderWidth, lm.Start, new xVector(lm.End, lm.Start), le.Start);
                    if (cDraw.LineEndAt(idraw, border.BorderWidth, lm.End, new xVector(lm.Start, lm.End), le.End) || draw)
                        idraw.DrawPathNZ(false, c != null, ic != null);
                }

                return new CompiledAnnotation(commands.ToArray(), annot.F, clip, clip, annot.Subtype);
            }
            #endregion
            #region PdfPolyAnnot
            else if (annot is PdfPolyAnnot)
            {
                var polygon = (PdfPolyAnnot)annot;

                var verticles = polygon.Vertices;
                if (verticles.Length > 1)
                {
                    if (annot is PdfPolygonAnnot)
                        cDraw.SetFillColor(idraw, polygon.IC);
                    cDraw.SetStrokeColor(idraw, polygon.C);
                    idraw.MoveTo(verticles[0].X, verticles[0].Y);
                    for (int c = 1; c < verticles.Length; c++)
                        idraw.LineTo(verticles[c].X, verticles[c].Y);

                    if (annot is PdfPolygonAnnot)
                        idraw.DrawPathNZ(true, true, true);
                    else
                        idraw.DrawPathNZ(false, true, false);
                }

                return new CompiledAnnotation(commands.ToArray(), annot.F, clip, clip, annot.Subtype);
            }
            #endregion
            #region PdfStampAnnot
            else if (annot is PdfStampAnnot)
            {
                var stamp = (PdfStampAnnot)annot;

                bool draw_border = border != null && border.BorderWidth > 0 && stamp.C != null && stamp.C.Length > 0;

                //Sets border properties
                var rect = new xRect(stamp.Rect);
                if (draw_border)
                {
                    if (border.Dashstyle != null)
                        idraw.SetStrokeDashAr(border.Dashstyle.Value);
                    var bw = border.BorderWidth;
                    idraw.StrokeWidth = bw;
                    cDraw.SetStrokeColor(idraw, annot.C);
                    double h_radius = border.HorizontalRadius;
                    double v_radius = border.VerticalRadius;
                    bw /= 2;
                    rect = new xRect(rect.X + bw, rect.Y + bw, rect.Right - bw, rect.Top - bw);

                    if (h_radius > 0 || v_radius > 0)
                    {
                        cDraw.RoundRectAt(idraw, rect, h_radius, v_radius);
                        cDraw.DrawPath(idraw, true, false, true, true);
                    }
                    else
                    {
                        cDraw.RectAt(idraw, rect);
                        cDraw.DrawPath(idraw, false, false, true, true);
                    }
                }

                //Sets up the text state
                //idraw.SetFont(crs.Tf, crs.Tfs);
                //cDraw.SetFillColor(idraw, annot.C);

                //Finds the width when height = 1
                crs.Tfs = 1;
                var tb = new oldTextBox(new chDocument(stamp.Name), crs);
                tb.Document[0].Alignment = chParagraph.Alignement.Middle;
                tb.PositionOver = true;
                tb.Position = rect.LowerLeft;

                double max_width = rect.Width * .9;

                //Scales up the text size.
                var font_height = max_width / tb.Width;

                //Checks if bound by height
                double max_height = rect.Height * 0.95;
                if (font_height > max_height)
                    font_height = max_height;

                tb.DefaultFontSize = font_height;

                //Sets up the text state
                idraw.SetFont(crs.Tf, crs.Tfs);
                cDraw.SetFillColor(idraw, annot.C);

                //Centers the text horizontally
                tb.Width = rect.Width;

                //Don't know the true height until now.
                //Centers vertically
                if (font_height < max_height)
                {
                    var padd = ((rect.Height - tb.Height) / 2) * .9;
                    tb.PaddingBottom = (float) padd;
                }

                //tb.Layout();
                tb.Render(idraw, ref crs);

                return new CompiledAnnotation(commands.ToArray(), annot.F, clip, clip, annot.Subtype);
            }
            #endregion

            return null;
        }

        private CompiledAnnotation[] Compile(AnnotationElms annots)
        {
            var commands = new List<RenderCMD>(50);
            var idraw = new DrawCMD(commands);
            var c_annots = new CompiledAnnotation[annots.Count];
            int annot_nr = 0;

            //Default text renderin
            cRenderState crs = new cRenderState(cFont.Create("Helvetica"));
            crs.Tfs = 10;

            foreach (var annot in annots)
            {
                try
                {
                    c_annots[annot_nr] = Compile(annot, commands, idraw, crs);
                    commands.Clear();
                }
                catch (PdfLibException) { }
                annot_nr++;
            }

            return c_annots;
        }

        private CompiledForms Compile(PdfForm form, PdfASDict dict)
        {
            if (form != null)
                return new CompiledForms(Compile(form));

            if (dict != null)
            {
                //Named forms
                var names = new string[dict.Count];
                var forms = new CompiledForm[names.Length];
                int c = 0;
                foreach (var kp in dict)
                {
                    forms[c] = Compile(kp.Value);
                    names[c++] = kp.Key;
                }
                return new CompiledForms(forms, names);
            }

            return null;
        }

        private List<RenderCMD> Parse(byte[] content)
        {
            if (content == null) return new List<RenderCMD>();
            _snize = new Lexer(content);
            _parse = new Parser(null, _snize, null);
            var list = new List<RenderCMD>(content.Length / 8);
            Parse(list);
            return list;
        }

        /// <summary>
        /// Parses a contents stream
        /// </summary>
        /// <remarks>This function is not reentrant</remarks>
        [DebuggerStepThrough()]
        internal void Parse(PdfContentArray content, List<RenderCMD> cmds)
        {
            _snize = new Lexer(content.Contents);
            _parse = new Parser(null, _snize, null);
            Parse(cmds);
        }

        /// <summary>
        /// Parses a contents stream
        /// </summary>
        /// <remarks>This function is not reentrant</remarks>
        private void Parse(List<RenderCMD> cmds)
        {
#if DEBUG
            //The cmds list can be modified outside this loop, so don't
            //depend on cmds.Length for anything.
            int count = 0;
#endif

            while (true)
            {
                try
                {
                    //I'm planning to have a "stop parsing" function
                    //Note that "Cast" exceptions and a few other exceptions
                    //can be thrown. In that case the last command is "iligal"
                    //and can be handeled as such. Though for now that is not
                    //what's going to happen.
                    RenderCMD cmd;

                    while ((cmd = parseCommand()) != null)
                    {
                        cmds.Add(cmd);
#if DEBUG
                        count++;
                        //if (count == 0xecc)
                        //    Console.WriteLine("HI");
#endif
                    }
                }
                catch (Exception e)
                {
                    #region Exception handeling

                    Debug.WriteLine(e);
                    switch (_ws)
                    {
                        case WorkState.Build:
                            //Rapport: There was an error building a PDF command. This indicates a flaw
                            //         in the command stream. 
                            break;
                        case WorkState.Exe:
                            //Rapport: Failed to execute PDF command. Command that failed to execute: XX
                            break;
                        case WorkState.Fetch:
                            //Rapport: There was an error fetching data from the PDF document. This
                            //         indicates a flaw in the PDF file's structure. Command that failed to execute was: Do
                            break;
                    }

                    //consume_more:
                    PdfType tok;
                    switch (_gs)
                    {
                        case GS.Text:
                            while ((tok = _snize.trySetNextToken()) != PdfType.EOF)
                            {
                                if (tok == PdfType.Keyword)
                                {
                                    var s = _snize.Token;
                                    if ("ET".Equals(s))
                                    {
                                        _gs = GS.Page;
                                        _cmds.Add(new ET_CMD());
                                        break;
                                    }
                                    Errorcheck(s);
                                }
                            }
                            break;

                        case GS.Path:
                            while ((tok = _snize.trySetNextToken()) != PdfType.EOF)
                            {
                                if (tok == PdfType.Keyword)
                                {
                                    var s = _snize.Token;
                                    if ("S".Equals(s) || "s".Equals(s) || "F".Equals(s) ||
                                        "f".Equals(s) || "B".Equals(s) || "b".Equals(s) ||
                                        "n".Equals(s) || "f*".Equals(s) || "b*".Equals(s))
                                    {
                                        _stack.Clear();
                                        _cmds.Add(CreateCommand());
                                        break;
                                    }
                                    Errorcheck(s);
                                }
                            }
                            break;
                    }

                    //if (_gs != GS.Page && cont_enum.MoveNext())
                    //{
                    //    //Consume commands in next contents stream. 
                    //    _snize = new Lexer(cont_enum.Current.Content);
                    //    _parse = new Parser(null, _snize, null);
                    //    goto consume_more;
                    //}

                    _stack.Clear();
                    continue;

                    #endregion
                }

                return;
            } 
        }

        /// <summary>
        /// Used when consuming commands after an error
        /// </summary>
        private void Errorcheck(string s)
        {
            if ("BMC".Equals(s) || "BDC".Equals(s))
                _mc_stack.Push(new MCInfo());
            else if ("EMC".Equals(s) && _mc_stack.Count > 0)
                _mc_stack.Pop();
            else if ("BX".Equals(s))
                _comp_mode++;
            else if ("EX".Equals(s))
                _comp_mode = Math.Max(0, _comp_mode - 1);
            else if ("Q".Equals(s) && _state_stack.Count > 0)
            {
                _cs = _state_stack.Pop();
                _cmds.Add(new Q_RND());
            }
            else if ("q".Equals(s))
            {
                _state_stack.Push(_cs);
                _cmds.Add(new q_RND());
            }
        }

        /// <summary>
        /// Parses code into tokenized commands that can later be
        /// executed.
        /// </summary>
        /// <remarks>
        /// Using the lexer directly for most of the parsing. This
        /// because the parser returns "objects", while I pretty
        /// much just want raw values.
        /// </remarks>
        private RenderCMD parseCommand()
        {
            PdfType tok;
            _ws = WorkState.Build;
            while ((tok = _snize.SetNextToken()) != PdfType.EOF)
            {
                switch (tok)
                {
                    case PdfType.Keyword:
                        int tok_length = _snize.TokenLength;
                        if (tok_length >= 4)
                        {
                            var tok_str = _snize.Token;
                            if ("true".Equals(tok_str)) _stack.Push(true);
                            else if ("false".Equals(tok_str)) _stack.Push(null);
                            else if ("null".Equals(tok_str)) _stack.Push(null);
                            else goto default;
                        }
                        else
                        {
                            _ws = WorkState.Exe;
                            var cmd = CreateCommand();
                            if (cmd != null) return cmd;
                            _ws = WorkState.Build;
                        }
                        continue;

                    case PdfType.Integer:
                        _stack.Push(_snize.GetInteger());
                        continue;

                    case PdfType.Real:
                        _stack.Push(GetReal());
                        continue;

                    case PdfType.Name:
                        _stack.Push(new PdfName(_snize.GetName()));
                        continue;

                    case PdfType.String:
                        _stack.Push(new PdfString(_snize.RawToken, false));
                        continue;

                    case PdfType.HexString:
                        _stack.Push(new PdfString(_snize.RawToken, true));
                        continue;

                        //Not using the parser for reading arrays as only simple
                        //objects are allowed.
                    case PdfType.BeginArray:
                        //Should nested arrays be supported?
                        List<object> ar = new List<object>();
                        while ((tok = _snize.SetNextToken()) != PdfType.EndArray)
                        {
                            //Todo: Should I get integers as integers?
                            if (tok == PdfType.Integer || tok == PdfType.Real)
                                ar.Add(GetReal());
                            else if (tok == PdfType.String)
                                ar.Add(new PdfString(_snize.RawToken, false));
                            else if (tok == PdfType.HexString)
                                ar.Add(new PdfString(_snize.RawToken, true));
                            else if (tok == PdfType.Name)
                                ar.Add(new PdfName(_snize.GetName()));
                            else if (tok == PdfType.Keyword)
                            {
                                switch (_snize.Token)
                                {
                                    case "true":
                                        ar.Add(true); break;
                                    case "false":
                                        ar.Add(false); break;
                                    case "null":
                                        ar.Add(null); break;

                                    default:
                                        throw new PdfLogException(ErrSource.Compiler, PdfType.Keyword, ErrCode.Illegal);
                                }
                            }
                            else
                            {
                                throw new PdfLogException(ErrSource.Compiler, tok, ErrCode.Illegal);
                            }
                        }
                        _stack.Push(ar);
                        continue;

                    case PdfType.BeginDictionary:
                        _stack.Push(_parse.ReadItem(PdfType.BeginDictionary));
                        continue;

                    default:
                        throw new PdfLogException(ErrSource.Compiler, tok, ErrCode.Illegal);
                }
            }

            return null;
        }

        /// <summary>
        /// Parses commands
        /// </summary>
        /// <remarks>
        /// See 8.2 in the specs.
        /// </remarks>
        private RenderCMD CreateCommand()
        {
            PdfItem item;

            //Page 111 in the specs
            switch (_snize.Token)
            {
                #region General graphics state

                case "d": //Set stroke dash array
                    if (_stack.Count != 2) goto default;
                    //Command is allowed to execute in non-page/text contents,
                    //but a warning is sent.
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "d");
                    return new d_RND(PopDouble(), ToDoubleAr((List<object>)_stack.Pop()));

                case "i": //Set flatness tolerance
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "i");
                    return new i_RND(PopDouble());

                case "J": //Set line cap style
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "J");
                    return new J_RND(PopInt());

                case "j": //Set line join style
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "j");
                    return new j_RND(PopInt());

                case "M": //Set miter limits
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "M");
                    return new M_RND(PopDouble());

                case "w": //Set stroke width
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "w");
                    return new w_RND(PopDouble());

                case "gs": //Set graphic State
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "gs");
                    _ws = WorkState.Fetch;
                    return MakeGS(_res.ExtGState[PopName()]);

                case "ri": //Rendering intent
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "ri");
                    return new ri_RND(PopName());

                #endregion

                #region Special graphics state

                case "q": //Push state
                    if (_stack.Count != 0 || _gs != GS.Page)
                    {//q/Q is too significant to skip.
                        _stack.Clear();
                        Log.Warn(_gs, "q");
                    }
                    _state_stack.Push(_cs);
                    return new q_RND();

                case "Q": //Pop state
                    if (_state_stack.Count == 0) goto default;
                    if (_stack.Count != 0 || _gs != GS.Page)
                    {//q/Q is too significant to skip.
                        _stack.Clear();
                        Log.Warn(_gs, "Q");
                    }
                    _cs = _state_stack.Pop();
                    return new Q_RND();

                case "cm": //Prepend matrix to CTM
                    if (_stack.Count != 6 || _gs != GS.Page && _gs != GS.Text) goto default;
                    //I'm allowing CM in text mode. It's common enough and relativly
                    //harmless. 
                    return new cm_RND(xMatrix.Create(PopDouble(), PopDouble(),
                        PopDouble(), PopDouble(), PopDouble(), PopDouble()));

                #endregion

                #region Color

                case "G": //Set gray level for stroking operations
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "G");
                    return new G_CMD(PopDouble());

                case "g": //Set gray level for non stroking operations
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "g");
                    _cs.cs = DeviceGray.Instance;
                    return new g_CMD(PopDouble());

                case "rg": //Set rgb color for non stroking operations
                    if(_stack.Count != 3) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "rg");
                    _cs.cs = DeviceRGB.Instance;
                    return new rg_CMD(PopDouble(), PopDouble(), PopDouble());

                case "RG": //Set rgb color for stroking operations
                    if(_stack.Count != 3) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "RG");
                    return new RG_CMD(PopDouble(), PopDouble(), PopDouble());

                case "k": //Set cmyk color for non stroking operations
                    if (_stack.Count != 4) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "k");
                    _cs.cs = DeviceCMYK.Instance;
                    return new k_CMD(PopDouble(), PopDouble(), PopDouble(), PopDouble());

                case "K": //Set cmyk color for stroking operations
                    if (_stack.Count != 4) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "K");
                    _cs.CS = DeviceCMYK.Instance;
                    return new K_CMD(PopDouble(), PopDouble(), PopDouble(), PopDouble());

                case "cs": //Set colorspace for non-stroking operations
                    _cs.cs = null;
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "cs");
                    _ws = WorkState.Fetch;
                    _cs.cs = _res.ColorSpace.GetColorSpace(PopName());
                    return new cs_CMD(_cs.cs);

                case "CS": //Set colorspace for stroking operations
                    _cs.CS = null;
                    if (_stack.Count != 1) goto default;
                    if (_gs != GS.Page && _gs != GS.Text) Log.Warn(_gs, "CS");
                    _ws = WorkState.Fetch;
                    _cs.CS = _res.ColorSpace.GetColorSpace(PopName());
                    return new CS_CMD(_cs.CS);

                case "sc":
                    if (_stack.Count != _cs.cs.NComponents) goto default;
                    return new sc_CMD(PopDouble(_cs.cs.NComponents));

                case "SC":
                    if (_stack.Count != _cs.CS.NComponents) goto default;
                    return new SC_CMD(PopDouble(_cs.CS.NComponents));

                case "scn": //Set color for the non-stroking operation
                    if (_cs.cs is PatternCS)
                    {
                        var pcs = (PatternCS)_cs.cs;
                        var ucs = pcs.UnderCS;
                        if (ucs != null) 
                        {
                            if (_stack.Count != 1 + ucs.NComponents)
                                goto default;
                        }else
                            if (_stack.Count < 1) 
                                goto default;
                        _ws = WorkState.Fetch;
                        var pattern = _res.Pattern[PopName()];
                        if (pattern is PdfShadingPattern)
                        {
                            if (ucs != null) throw new PdfNotSupportedException();
                            return new scn_pattern_CMD((PdfShadingPattern)pattern);
                        }
                        else
                        {
                            //Tiling patterns must be compiled.
                            IForm cp;
                            var tile = (PdfTilingPattern)pattern;
                            if (_cached_forms == null)
                                _cached_forms = new Util.WeakCache<IPage, IForm>(4);
                            if (!_cached_forms.TryGetValue(tile, out cp))
                                cp = new PdfCompiler(_cached_forms).Compile(tile);

                            return new scn_tile_CMD((ucs == null) ? null : PopDouble(ucs.NComponents), (CompiledPattern)cp);
                        }
                    }
                    else
                    {
                        if (_stack.Count != _cs.cs.NComponents) 
                            goto default;
                        return new scn_CMD(PopDouble(_cs.cs.NComponents));
                    }

                case "SCN": //Set color for the stroking operation
                    if (_cs.CS is PatternCS)
                    {
                        var pcs = (PatternCS)_cs.CS;
                        var ucs = pcs.UnderCS;
                        if (ucs != null)
                        {
                            if (_stack.Count != 1 + ucs.NComponents)
                                goto default;
                        }
                        else
                            if (_stack.Count < 1) goto default;
                        _ws = WorkState.Fetch;
                        var pattern = _res.Pattern[PopName()];
                        if (pattern is PdfShadingPattern)
                        {
                            if (ucs != null) throw new PdfNotSupportedException();
                            return new SCN_pattern_CMD((PdfShadingPattern)pattern);
                        }
                        else
                        {
                            //Tiling patterns must be compiled.
                            IForm cp;
                            var tile = (PdfTilingPattern)pattern;
                            if (_cached_forms == null)
                                _cached_forms = new Util.WeakCache<IPage, IForm>(4);
                            if (!_cached_forms.TryGetValue(tile, out cp))
                                cp = new PdfCompiler(_cached_forms).Compile(tile);

                            return new SCN_tile_CMD((ucs == null) ? null : PopDouble(ucs.NComponents), (CompiledPattern)cp);
                        }
                    }
                    else
                    {
                        if (_stack.Count != _cs.CS.NComponents) goto default;
                        return new SCN_CMD(PopDouble(_cs.CS.NComponents));
                    }

                #endregion

                #region Shading patterns

                case "sh":
                    //Adobe allows this to run in GS.Path mode
                    if (_stack.Count != 1 || _gs != GS.Page) goto default;
                    return new sh_CMD(_res.Shading[PopName()]);
                    
                #endregion

                #region Inline images

                case "BI":
                    if (_stack.Count != 0) goto default;
                    return new BI_CMD(MakeBI());

                #endregion

                #region XObjects

                case "Do": //Draw Object
                    if(_stack.Count != 1 || _gs != GS.Page) goto default;
                    string named_object = PopName();
                    _ws = WorkState.Fetch;
                    var xobject = _res.XObject[named_object];
                    if (xobject is PdfImage)
                        return new Do_CMD((PdfImage)xobject);
                    else if (xobject is PdfForm)
                    {
                        IForm cf;
                        var xform = (PdfForm)xobject;
                        if (_cached_forms == null)
                            _cached_forms = new Util.WeakCache<IPage, IForm>(4);
                        if (!_cached_forms.TryGetValue(xform, out cf))
                        {
                            PdfResources res = xform.GetResources();
                            if (res == null)
                            {
                                //If the resources dictionary is omitted, the page's resource
                                //dictionary is implied.
                                res = _res;
                            }
                            cf = new CompiledForm(new PdfCompiler(_cached_forms).Compile(xform.Contents, res), xform.BBox, xform.Matrix, xform.Group);
                        }
                        return new Do_FORM((CompiledForm) cf);
                    }
                    throw new PdfReadException(ErrSource.Compiler, PdfType.XObject, ErrCode.Missing);

                #endregion

                #region Path construction

                case "m": //MoveTo new subpath
                    if (_stack.Count != 2 || _gs != GS.Page && _gs != GS.Path)
                    { _gs = GS.Path; goto default; }
                    _gs = GS.Path;
                    return new m_CMD(PopDouble(), PopDouble());

                case "c": //CurveTo
                    if (_stack.Count != 6 || _gs != GS.Path) goto default;
                    return new c_CMD(PopDouble(), PopDouble(), PopDouble(), PopDouble(), PopDouble(), PopDouble());

                case "v": //CurveTo
                    if (_stack.Count != 4 || _gs != GS.Path) goto default;
                    return new v_CMD(PopDouble(), PopDouble(), PopDouble(), PopDouble());

                case "h": //Close path
                    if (_stack.Count != 0 || _gs != GS.Path) goto default;
                    return new h_CMD();

                case "l": //LineTo
                    if (_stack.Count != 2 || _gs != GS.Path) goto default;
                    return new l_CMD(PopDouble(), PopDouble());

                case "y": //CurveTo
                    if (_stack.Count != 4 || _gs != GS.Path) goto default;
                    return new y_CMD(PopDouble(), PopDouble(), PopDouble(), PopDouble());

                case "re": //Rectangle
                    if (_stack.Count != 4 || _gs != GS.Page && _gs != GS.Path)
                    { _gs = GS.Path; goto default; }
                    _gs = GS.Path;
                    return new re_CMD(PopDouble(), PopDouble(), PopDouble(), PopDouble());

                #endregion

                #region Path painting

                case "b": //Close, fill and stroke path (using non-zero)
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new b_CMD();

                case "b*": //Close, fill and stroke path (using even-odd)
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new bS_CMD();

                case "B": //Fill and stroke path (using even-odd)
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new B_CMD();

                case "B*": //Fill and stroke path (using even-odd)
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new BS_CMD();

                case "F": //Included for compability
                case "f": //Fill path
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new f_CMD();

                case "f*": //Fill path using even-odd rule
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new fs_CMD();

                case "s": //Stroke and fill path
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new s_CMD();

                case "S": //Stroke path
                    if (_stack.Count != 0 || _gs != GS.Path) { _gs = GS.Page; goto default; }
                    _gs = GS.Page;
                    return new S_CMD();

                case "n": //End path without filling or stroking
                    if (_stack.Count != 0 || _gs != GS.Path) 
                    {
                        Log.Warn(_gs, "n");
                        _gs = GS.Page; 
#if DEBUG
                        //Exceptions are slow in debug mode and this error is quite common
                        _stack.Clear();
                        return null;
#else
                        goto default; 
#endif
                    }
                    _gs = GS.Page;
                    return new n_CMD();

                #endregion

                #region Clipping path

                case "W":
                    if (_stack.Count != 0 || _gs != GS.Path && _gs != GS.Page) goto default;
                    return new W_CMD(xFillRule.Nonzero);

                case "W*":
                    if (_stack.Count != 0 || _gs != GS.Path && _gs != GS.Page) goto default;
                    return new W_CMD(xFillRule.EvenOdd);

                #endregion

                #region Text objects

                case "BT": //Begin text.
                    if (_stack.Count != 0 || _gs != GS.Page && _gs != GS.Text)
                    {
                        _gs = GS.Text;
                        goto default;
                    }
                    _gs = GS.Text;
                    return new BT_CMD();

                case "ET": //End text.
                    if (_stack.Count != 0 || _gs != GS.Text && _gs != GS.Page)
                    {
                        _gs = GS.Page;
                        goto default;
                    }
                    _gs = GS.Page;
                    return new ET_CMD();

                #endregion

                #region Text state

                case "Tc": //Set character spacing
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tc_CMD(PopDouble());

                case "Tw": //Set word spacing
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tw_CMD(PopDouble());

                case "Tf": //Text font
                    if (_stack.Count != 2 || _gs != GS.Text && _gs != GS.Page) goto default;
                    _ws = WorkState.Fetch;
                    var fnt_size = PopDouble();
                    if (_res != null)
                    {
                        var fnt = _res.Font[PopName()];
                        if (fnt.SubType == Pdf.Font.FontType.Type3)
                            return new Tf_Type3(fnt_size, FetchT3((PdfType3Font)fnt));
                        else
                            return new Tf_CMD(fnt_size, fnt);
                    }
                    else
                    {
                        //To support default apperance in annotations
                        var cfont = cFont.Create(PopName());
                        return new Tf_CMD(fnt_size, cfont.MakeWrapper());
                    }

                case "Tr": //Text rendering mode
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tr_CMD(PopInt());

                case "Tz": //Set horizontal text scaling
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tz_CMD(PopDouble());

                case "TL": //Distance between lines
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new TL_CMD(PopDouble());

                case "Ts": //Set Text rise
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Ts_CMD(PopDouble());

                #endregion

                #region Text positioning

                case "Tm"://Sets the Text line metrixs and the Text metrics
                    if (_stack.Count != 6 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tm_CMD(xMatrix.Create(PopDouble(), PopDouble(), 
                        PopDouble(), PopDouble(), PopDouble(), PopDouble()));

                case "Td": //Move text position
                    if (_stack.Count != 2 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Td_CMD(PopDouble(), PopDouble());

                case "TD": //Move text position
                    if (_stack.Count != 2 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new TD_CMD(PopDouble(), PopDouble());

                case "T*": //Move text position
                    if (_stack.Count != 0 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new TS_CMD();

                #endregion

                #region Text showing

                case "TJ": //Show text, allow induvidual glyph positioning
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new TJ_CMD(PopArray());

                case "Tj": //Show text
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tj_CMD(PopString());

                case "\'": //Show text and carriage return
                    if (_stack.Count != 1 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tcr_CMD(PopString());

                case "\"": //Show text and carriage return, with word spacing
                    if (_stack.Count != 3 || _gs != GS.Text && _gs != GS.Page) goto default;
                    return new Tws_CMD(PopString(), PopDouble(), PopDouble());

                #endregion

                #region Type3 fonts

                case "d0":
                    if (_stack.Count != 2) goto default;
                    return new d0_CMD(PopDouble(), PopDouble());

                case "d1":
                    if (_stack.Count != 6) goto default;
                    return new d1_CMD(PopDouble(), PopDouble(), PopDouble(), PopDouble(), PopDouble(), PopDouble());

                #endregion

                #region Marked content

                case "MP": //Marked Point
                    if (_stack.Count != 1 || _gs != GS.Page && _gs != GS.Text)
                        goto default;
                    return new MP_CMD(PopName(), null);

                case "DP":
                    if (_stack.Count != 2 || _gs != GS.Page && _gs != GS.Text)
                        goto default;
                    item = (PdfItem)_stack.Pop();
                    if (item is PdfName)
                        item = _res.Properties[item.GetString()].Deref();
                    return new MP_CMD(PopName(), (PdfDictionary) item);

                case "BMC":
                    if (_stack.Count != 1 || _gs != GS.Page && _gs != GS.Text)
                    {
                        _mc_stack.Push(new MCInfo());
                        goto default;
                    }
                    _mc_stack.Push(new MCInfo(PopName(), null, _cmds.Count, _gs));
                    break;

                case "BDC":
                    if (_stack.Count != 2 || _gs != GS.Page && _gs != GS.Text)
                    {
                        _mc_stack.Push(new MCInfo());
                        goto default;
                    }
                    item = (PdfItem) _stack.Pop();
                    if (item is PdfName)
                        item = _res.Properties[item.GetString()].Deref();
                    _mc_stack.Push(new MCInfo(PopName(), (PdfDictionary) item, _cmds.Count, _gs));
                    break;

                case "EMC":
                    if (_mc_stack.Count == 0) goto default;
                    //Mc info is pushed by BDC
                    var mc = _mc_stack.Pop();
                    //T-REC-T.4-199904.pdf page 4 starts BDC in "page" mode and ends it in "text" mode.
                    if (_stack.Count != 0 && mc.GState != GS.Page && mc.GState != GS.Text) //|| mc.GState != _gs)
                        goto default;
                    return new BDC_CMD(Pop<RenderCMD>(_cmds, mc.CmdPos, _cmds.Count - mc.CmdPos),
                        mc.Property, mc.Dict);

                #endregion

                #region Compatibility

                case "BX":
                    _comp_mode++;
                    return new BX_CMD();

                case "EX":
                    _comp_mode--;
                    if (_comp_mode < 0) _comp_mode = 0;
                    return new EX_CMD();

                #endregion

                default:
                    Log.GetLog().IgnoredCMD(_snize.Token);
                    //Debug.Assert(false);
                    //_stack.Clear(); 
                    if (_comp_mode == 0)
                        throw new CompileException();
                    break;
            }

            return null;
        }

        #region Helper functions

        private gs_RND MakeGS(PdfGState gs)
        {
            if (gs == null) throw new PdfReadException(PdfType.GState, ErrCode.Missing);
            var fnt = gs.Font as PdfType3Font;
            if (fnt != null) Compile(fnt);

            return new gs_RND(gs);
        }

        private CompiledFont FetchT3(PdfType3Font fnt)
        {
            //Gets font out of the cache
            IForm cfnt;
            if (_cached_forms == null) _cached_forms = new Util.WeakCache<IPage, IForm>(4);
            if (!_cached_forms.TryGetValue(fnt, out cfnt))
            {
                cfnt = Compile(fnt);
                _cached_forms[fnt] = cfnt;
            }

            return (CompiledFont) cfnt;
        }

        internal CompiledFont Compile(PdfType3Font fnt)
        {
            //Fetches the charstrings
            var chrs = fnt.CharProcs;
            PdfResources res = fnt.GetResources();
            var c_chrs = new Dictionary<string, RenderCMD[]>(chrs.Length);

            //If a font don't have resources, use the page's resources
            if (res == null) res = _res; ;

            //The compiler is not reentrant, so creating a new one.
            var comp = new PdfCompiler(_cached_forms);

            //Compiles each glyph
            for (int c = 0; c < chrs.Length; c++)
            {
                var ch = chrs[c];
                var cmds = comp.Compile(ch.Contents, res);
                c_chrs.Add(ch.Name, cmds);
            }

            //Adds the compiled glyphs to the font itself.
            return new CompiledFont(fnt, c_chrs);
        }

        /// <summary>
        /// Makes a inline image
        /// </summary>
        /// <remarks>
        /// This implementation always decodes the image, so as to find the ending.
        /// This means inline images are decode twice when displayed.
        /// 
        /// Note that the current implementation threats the content streams as a single
        /// stream. For BI images I belive they're not allowed overflow content streams,
        /// so it could be an idea to only fetch data from the current content stream.
        /// </remarks>
        private PdfImage MakeBI()
        {
            //This "readitem(type)" work-alike code bypasses caching in the parser.
            // (By not calling readitem(type) duh)
            //
            //However, if there's an indirect reference in the data
            //being read, the cache will be filled and there will be
            //a "PdfInternalException" next time the function is called
            //
            //This isn't a huge problem, as inline images are not 
            //allowed to have indirect references. The only nag is
            //that the upstack exception handeler is not able to clean
            //up after a failed "BI" command. 
            var type = _snize.SetNextToken();
            var cat = new Catalog();
            while (type != PdfType.Keyword && !"ID".Equals(_snize.Token))
            {
                if (type == PdfType.EOF)
                    throw new PdfReadException(PdfType.EOF, ErrCode.UnexpectedEOF);
                if (type != PdfType.Name)
                    throw new PdfReadException(ErrSource.Compiler, PdfType.Name, ErrCode.WrongType);

                var key = PdfImage.ExpandBIKeyName(_snize.GetName());

                type = _snize.SetNextToken();
                if (type == PdfType.EOF)
                    throw new PdfReadException(PdfType.EOF, ErrCode.UnexpectedEOF);
                //Small bug: Keywords can be placed in the dictionary. This bug may
                //           be present in the parser as well.

                PdfItem item;
                if ("Filter".Equals(key) || "ColorSpace".Equals(key))
                    item = PdfImage.ExpandBIName(_parse.ReadItem(type));
                else
                    item = _parse.ReadItem(type);

                //AddOrReplace the value. "null" keys are to be ignored
                if (item != PdfNull.Value)
                    cat[key] = item;

                type = _snize.SetNextToken();
            }

            //The dictionary of an inline image will always be sealed. The data of this
            //image is inside a compressed content stream, and can therefore not be edited
            //directly.
            var dict = new SealedDictionary(cat);

            //Fetches colorspace if needed
            IColorSpace cspace = null;
            bool imageMask = dict.GetBool("ImageMask", false);
            if (!imageMask)
            {
                
                if (dict["ColorSpace"] is PdfObject)
                {
                    //According to the specs there's some sort of special reduced indexed CS support,
                    cspace = (IColorSpace)dict.GetPdfTypeEx("ColorSpace", PdfType.ColorSpace);
                    if (!(cspace is IndexedCS))
                        throw new PdfReadException(PdfType.ColorSpace, ErrCode.Invalid);
                }
                else
                {
                    var cs = dict.GetNameEx("ColorSpace");
                    if (!"DeviceGray".Equals(cs) && !"DeviceRGB".Equals(cs) && !"DeviceCMYK".Equals(cs) && !"Indexed".Equals(cs))
                        cat["ColorSpace"] = (PdfItem)_res.ColorSpace[cs];
                    cspace = (IColorSpace)dict.GetPdfTypeEx("ColorSpace", PdfType.ColorSpace);
                }
            }

            //Reads out the binary data.
            //Problem: All stream implementations expects the length of the stream
            //to be a known quantity, but there's no way to know with absolute certainty
            //how big the stream really is. 
            //
            //So for this to be a proper implementation you will have to decode the image
            //to see how long the stream is, but at the same time, my filters need to know
            //how long the stream is before decoding the image. 
            //
            //What we do then is first to guess how long the image is, try to decode,
            //and if the decode fails "due to lack of data" we guess again.

            //Figures out the size of the decoded data
            int width = dict.GetUIntEx("Width");
            int height = dict.GetUIntEx("Height");
            int bpc, nComps;
            if (imageMask)
            {
                bpc = 1;
                nComps = 1;
            }
            else
            {
                bpc = dict.GetUIntEx("BitsPerComponent");
                nComps = cspace.NComponents;
            }
            int stride = (width * bpc * nComps + 7) / 8;
            int size = stride * height;

            //There's required to be at least one whitespace character after id.
            //(Potentially two in case of \r\n)
            _snize.SkipWhite(1, true);

            //Tries finding the ending "quickly" first.
            WritableStream stream = WritableStream.SearchForEnd(size, dict, cat, _snize);

            //Tries to find the end of the stream. Note that "EOF" is treated as 
            //a valid way to end the stream.
            _snize.ReadToEI();
            //Should perhaps throw if EI isn't found. Adobe does not render BI without EI
            //so it would be consistant with that.
            //
            //Perhaps also check if there's "Junk data" from current to EI, and emit warning
            //if there is.

            if (stream == null)
            {
                var img_data = _snize.RawToken;

                //Despite the name, this stream isn't writable because the dict inside is
                //sealed.
                cat.Add("Length", new PdfInt(img_data.Length));
                stream = new WritableStream(img_data, dict, false);
                bool eod = false;

                while (true)
                {
                    //Tries to decode the data. It's possible that the decode fails due to
                    //junk data the filter can't handle. There will almost always be a litte
                    //whitespace junk, though I belive that all the filters can handle that.
                    int decoded_data_length;
                    try
                    {
                        //CCITT encoded images are a bit tricky. They can spit out a valid image
                        //even when there's not enough data decoded.
                        //
                        //The current workaround is to have the filter notify the caller if a
                        //EOD was the reason decoding ended.
                        //
                        //For some filters EOD can be found through simpler means:
                        //  Asci85: Search for ~>
                        //  RunLengthEncode: Search for byte value 128
                        //  HexFilter: Never encodes EI
                        //  No filter: Size of image data
                        //  JBig2: Not allowed inline
                        //  JP2K: Not allowed inline
                        //
                        //For Zip and LZW it's probably possible to find the ending in a
                        //quicker way too. 

                        decoded_data_length = stream.DecodeStream(out eod).Length;
                    }
                    //Some of the filters may throw at trunctuated data. Unfortunatly, swallowing
                    //these exceptions will result in the rest of the content stream not being 
                    //rendered if it throws for any other reason than data trunctuation.
                    catch (Exception) { decoded_data_length = 0; }

                    //Checks if the final size is agreeable. 
                    if (decoded_data_length >= size)
                    {
                        //CCITT may give the desired size even when the image isn't fully
                        //decoded. So we check if the decoding ended with EOD, and if
                        //there's data left then continue decoding.
                        //
                        //atec-2008-01.pdf demonstrates this quite well.
                        if (!eod || _snize.IsEOF)
                            break;

                        //Tries decoding CCITT stream again, as decoding ended
                        //in the middle of a row.
                        Debug.Assert(size == decoded_data_length);
                    }

                    //One can alternativly pad the image data instead of throwing this exception
                    if (_snize.IsEOF)
                        throw new PdfReadException(ErrSource.Compiler, PdfType.XObject, ErrCode.UnexpectedEOD);

                    //Tries to find "EI" again.
                    _snize.ReadToEI();

                    //Combines the data arrays.
                    img_data = Util.ArrayHelper.Concat(img_data, _snize.RawToken);

                    //If raw data is four times the size of expected output data it's
                    //in all likelihood due to some bug in the decode filter, so
                    //we break of the search.
                    if (img_data.Length > size && img_data.Length > 4 * size)
                        throw new PdfFilterException(ErrCode.Invalid);

                    //And gives it another go
                    cat["Length"] = new PdfInt(img_data.Length);
                    stream = new WritableStream(img_data, dict, false);
                }
            }

            //Removes "EI"
            _snize.SetNextToken();

            //It's conceivable that there will be junk data beyond this point, though
            //highly unlikely. It should only happen if an image got junk data with a 
            //"EI" combination, and there's not really any good way to deal with that.

            return new PdfImage(stream, dict);
        }

        /// <summary>
        /// Pops an array of objects off the stack
        /// </summary>
        private object[] PopArray()
        {
            return ((List<object>)_stack.Pop()).ToArray();
        }

        /// <summary>
        /// Pops a string object off the stack
        /// </summary>
        private PdfString PopString()
        {
            return (PdfString)_stack.Pop();
        }

        /// <summary>
        /// Pops a int off the stack
        /// </summary>
        [DebuggerStepThrough()]
        private int PopInt()
        {
            return (int) _stack.Pop();
        }

        /// <summary>
        /// Pops a double off the stack
        /// </summary>
        [DebuggerStepThrough()]
        private double PopDouble()
        {
            var o = _stack.Pop();
            if (o is double) return (double)o;
            return (int)o;
        }

        [DebuggerStepThrough()]
        private double[] PopDouble(int n)
        {
            var da = new double[n];
            for (int c = da.Length - 1; c >= 0; c--)
            {
                var o = _stack.Pop();
                if (o is double)
                    da[c] = (double) o;
                else da[c] = (int) o;
            }
            return da;
        }

        /// <summary>
        /// Pops a name off the stack
        /// </summary>
        [DebuggerStepThrough()]
        private string PopName()
        {
            var o = _stack.Pop();
            if (o is PdfName) return ((PdfName)o).Value;
            throw new PdfReadException(ErrSource.Compiler, PdfType.Name, ErrCode.WrongType);
        }

        T[] Pop<T>(List<T> list, int offset, int count)
        {
            T[] ret = new T[count];
            list.CopyTo(offset, ret, 0, count);
            list.RemoveRange(offset, count);
            return ret;
        }

        private double GetReal()
        {
            var token = _snize.Token;
            var i = token.IndexOf('.');
            if (i != -1)
            {
                var dp = token.Length - i - 1;
                if (dp > _detected_precision)
                    _detected_precision = dp;
            }
            return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            //return _snize.GetReal();
        }

        #endregion

        #region Structs and enums

        /// <summary>
        /// For tracking needed state information.
        /// </summary>
        struct CompilerState
        {
            /// <summary>
            /// Current color space for fills
            /// </summary>
            public IColorSpace cs;

            /// <summary>
            /// Current color space for strokes
            /// </summary>
            public IColorSpace CS;
        }

        /// <summary>
        /// Used to collect Marked Contents data
        /// </summary>
        struct MCInfo
        {
            public string Property;
            public PdfDictionary Dict;
            public int CmdPos;
            public GS GState;
            public MCInfo(string n, PdfDictionary d, int p, GS g)
            { Property = n; Dict = d; CmdPos = p; GState = g; }
        }

        enum WorkState
        {
            Compile,
            Build,
            Exe,
            Fetch
        }

        #endregion

        #region Static helper functions

        /// <summary>
        /// Converts an array of numbered objects into doubles.
        /// </summary>
        internal static double[] ToDoubleAr(List<object> o)
        {
            var ret = new double[o.Count];
            for (int c = 0; c < o.Count; c++)
            {
                var q = o[c];
                ret[c] = (q is int) ? (int)q : (double)q;
            }
            return ret;
        }

        #endregion
    }
}
