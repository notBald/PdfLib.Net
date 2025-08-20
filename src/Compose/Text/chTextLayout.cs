#define DIRECT_DRAW //Draws directly to the target, instead of building a list of commands to execute. If a list is desired, use DrawCMD as the target.

using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// Used to implement text layout in chTextBox and cTextArea. 
    /// </summary>
    internal class chTextLayout
    {
        public delegate void LayoutChangedFunc();

        public event LayoutChangedFunc LayoutChanged;

        private bool _need_layout, _need_height_layout;

        private void FireLayoutChanged(bool height)
        {
            if (height)
                _need_height_layout = true;
            else
                _need_layout = true;
            if (LayoutChanged != null)
                LayoutChanged();
        }

        /// <summary>
        /// When the document is changed, layout on the affected
        /// paragraph is preformed.
        /// </summary>
        public bool UpdateLayoutOnChange { get; set; }

        #region State

        private cRenderState _org_state = new cRenderState();

        #endregion

        #region Init

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Document to display</param>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        /// <param name="content_height">Height of the text box's content. Padding comes in addition to this.</param>
        /// <param name="initial_state">
        /// Various parameters relating to how the text is to be displayed. The text
        /// box will use these parameters to lay out the text. When calling the normal
        /// Render function, the supplied state will be adjusted to conform to the
        /// text box's initial state.
        /// </param>
        public chTextLayout(bool break_word = true)
        {
            _change_handler = new chDocument.ParagraphChangedHandler(_doc_OnParagraphChanged);
            _org_state = new cRenderState();
            _org_state.BreakWord = break_word;
            _org_state.BreakWordCharacter = '-';
        }

        internal chTextLayout(chDocument doc, double? content_width, double? content_height, cRenderState initial_state)
        {
            _change_handler = new chDocument.ParagraphChangedHandler(_doc_OnParagraphChanged);
            if (initial_state == null)
                throw new ArgumentNullException();
            _org_state = initial_state.Copy();
            _org_state.BreakWordCharacter = '-';
            if (content_width != null)
            {
                _content_width = content_width.Value;
                has_width = true;
            }
            _height = content_height;
            Document = doc;
        }

        void _doc_OnParagraphChanged(chParagraph par, int index, bool added, bool layout)
        {
            if (added)
            {
                throw new NotImplementedException();
            }

            if (layout)
            {
                var cur_state = _org_state.Copy();
                int start = GetParStartPos(index);
                xIntRange par_range = new xIntRange(start, start + par.Length);
                if (par_range.Overlaps(_range))
                {
                    //Updates the paragraph metrics, though it's probably only the 
                    //char range for the lines that needs to be updated.
                    var pm = _par_metrics[index] = par.CreateMetrics();

                    if (UpdateLayoutOnChange)
                    {
                        Layout(par, pm, cur_state, GetHeight(index), index == 0 ? 0 : _paragraph_gap, Math.Max(par_range.Min, _range.Min) - par_range.Min, Math.Min(par_range.Max, _range.Max) - par_range.Min);
                        if (LayoutChanged != null)
                            LayoutChanged();
                    }
                    else FireLayoutChanged(false);
                }
            }
        }

        #endregion

        #region Textbox size

        private double _content_width;
        /// <summary>
        /// If the textbox has a fixed content width
        /// </summary>
        private bool has_width;
        private double? _height;
        private bool _smart_height;

        /// <summary>
        /// Width of the text
        /// </summary>
        public double Width
        {
            get { return _content_width; }
            set
            {
                _content_width = value;
                has_width = true;
                FireLayoutChanged(false);
            }
        }

        public void RemoveWidth()
        {
            has_width = false;
            FireLayoutChanged(false);
        }

        /// <summary>
        /// Height of the text
        /// </summary>
        public double Height
        {
            set { _height = value; }
            get
            {
                if (_doc == null || _par_metrics.Length == 0)
                    return 0;
                double height = _par_metrics[0].Height;
                for (int c = 1; c < _par_metrics.Length; c++)
                {
                    height += _par_metrics[c].Height;
                    //height += _par_metrics[c].ParGap * _paragraph_gap;
                }
                if (_smart_height)
                    height -= Math.Min(_par_metrics[_par_metrics.Length - 1].YMin, LastLineDescent);
                return height;
            }
        }

        /// <summary>
        /// Descent for the last line in the document
        /// </summary>
        public double LastLineDescent
        {
            get
            {
                if (_doc == null || _par_metrics.Length == 0)
                    return 0;
                return _par_metrics[_par_metrics.Length - 1].LastDescent;
            }
        }

        /// <summary>
        /// Free space above the text
        /// </summary>
        public double FreeSpaceAbove
        {
            get
            {
                if (_doc == null || _par_metrics.Length == 0)
                    return 0;
                return _par_metrics[0].GetSpaceAbove(0);
            }
        }

        /// <summary>
        /// Line height calculations equivalent to the text box class.
        /// </summary>
        public bool SmartLineHeight
        {
            get { return _smart_height; }
            set
            {
                if (value != _smart_height)
                {
                    _smart_height = value;
                    FireLayoutChanged(true);
                }
            }
        }

        #endregion

        #region Apperance

        /// <summary>
        /// How the textbox handles text that overflows its boundaries.
        /// </summary>
        public chTextBox.Overdraw Overflow = chTextBox.Overdraw.Hidden;

        /// <summary>
        /// Whenever sidebearings is to be stripped
        /// </summary>
        private bool _strip_sb = false;

        /// <summary>
        /// Whenever or not to strip the side bearings from the text when
        /// calculating content width. Also note, when true, left and right aligned
        /// text is postitioned perfectly at the edge.
        /// </summary>
        public bool StripSideBearings
        {
            get { return _strip_sb; }
            set
            {
                if (value != _strip_sb)
                {
                    _strip_sb = value;

                    //A complete layout isn't actually needed, since only the content
                    //content width needs recalcing. That code can be added to "HeightLayout".
                    //(e.g. go through each line and call GetContentWidth(_strip_sb, rtl or ltr)
                    FireLayoutChanged(false);
                }
            }
        }

        /// <summary>
        /// Font used if no font is defined.
        /// </summary>
        public cFont DefaultFont
        {
            get { return _org_state.Tf; }
            set
            {
                if (!ReferenceEquals(value, _org_state.Tf))
                {
                    _org_state.Tf = value;
                    FireLayoutChanged(false);
                }
            }
        }

        /// <summary>
        /// Standar size for fonts
        /// </summary>
        public double DefaultFontSize
        {
            get { return _org_state.Tfs; }
            set
            {
                if (value != _org_state.Tfs)
                {
                    _org_state.Tfs = value;
                    FireLayoutChanged(false);
                }
            }
        }

        /// <summary>
        /// Color of non-outline fonts
        /// </summary>
        public cBrush DefaultFontColor
        {
            get { return _org_state.FillColor; }
            set { _org_state.FillColor = value; }
        }

        /// <summary>
        /// Color of outlined text
        /// </summary>
        public cBrush DefaultOutlineColor
        {
            get { return _org_state.StrokeColor; }
            set { _org_state.StrokeColor = value; }
        }

        /// <summary>
        /// Thickness of outlines
        /// </summary>
        public double DefaultOutlineWidth
        {
            get { return _org_state.StrokeWidth; }
            set { _org_state.StrokeWidth = value; }
        }

        /// <summary>
        /// Space between characters
        /// </summary>
        public double CharacterSpacing
        {
            get { return _org_state.Tc; }
            set { _org_state.Tc = value; }
        }

        /// <summary>
        /// Space between words
        /// </summary>
        public double WordSpacing
        {
            get { return _org_state.Tw; }
            set { _org_state.Tw = value; }
        }

        /// <summary>
        /// Horizontal scaling of text
        /// </summary>
        public double HorizontalScale
        {
            get { return _org_state.HorizontalScaling; }
            set { _org_state.HorizontalScaling = value; }
        }

        /// <summary>
        /// Text rise above the baseline
        /// </summary>
        public double TextRise
        {
            get { return _org_state.TextRise; }
            set { _org_state.TextRise = value; }
        }

        public bool BreakWord
        {
            get { return _org_state.BreakWord; }
            set
            {
                if (_org_state.BreakWord != value)
                {
                    _org_state.BreakWord = value;
                    FireLayoutChanged(false);
                }
            }
        }

        /// <summary>
        /// Character used for breaking words. Use '\0' for no character
        /// </summary>
        public char BreakWordCharacter
        {
            get { return _org_state.BreakWordCharacter != null ? _org_state.BreakWordCharacter.Value : '\0'; }
            set
            {
                if (value == '\0')
                    _org_state.BreakWordCharacter = null;
                else
                    _org_state.BreakWordCharacter = value;
                if (_org_state.BreakWord)
                    FireLayoutChanged(false);
            }
        }

        /// <summary>
        /// Sets right to left text flow.
        /// </summary>
        public bool RightToLeft
        {
            get { return _org_state.RightToLeft; }
            set { _org_state.RightToLeft = value; }
        }

        #endregion

        #region Document

        private chDocument _doc;
        private chParagraph.ParMetrics[] _par_metrics;
        private readonly chDocument.ParagraphChangedHandler _change_handler;
        private xIntRange _range = new xIntRange(0, int.MaxValue);

        private int _line_count = 0;

        private int _last_displayed_character = -1;

        public int LastDisplayedCharacter { get { return _last_displayed_character; } }

        /// <summary>
        /// Number of lines handled by the text box. If Overflow = hidden and height is set, then
        /// this will be the number of lines displayed.
        /// </summary>
        public int LineCount { get { return _line_count; } }

        /// <summary>
        /// First and last character to show from the document.
        /// </summary>
        public xIntRange DocumentRange
        {
            get { return _range; }
            set
            {
                if (value != _range)
                {
                    _range = value;
                    FireLayoutChanged(false);
                }
            }
        }

        public chDocument Document
        {
            get { return _doc; }
            set
            {
                if (_doc != null)
                    _doc.OnParagraphChanged -= _change_handler;
                if (value != null)
                    value.OnParagraphChanged += _change_handler;
                SetDocument(value);
            }
        }
        internal void SetDocument(chDocument doc)
        {
            _doc = doc;
            if (_doc != null)
            {
                _par_metrics = new chParagraph.ParMetrics[_doc.ParagraphCount];
                int count = 0;
                foreach (var par in _doc)
                    _par_metrics[count++] = par.CreateMetrics();
                FireLayoutChanged(false);
            }
        }

        #endregion

        #region Document size

        private double _paragraph_gap = 1;

        /// <summary>
        /// Distance between paragraphs. The distance is multiplied
        /// with the lineheight of first line in the next paragraph.
        /// </summary>
        public double ParagraphGap
        {
            get { return _paragraph_gap; }
            set
            {
                if (value != _paragraph_gap)
                {
                    _paragraph_gap = value;
                    FireLayoutChanged(true);
                }
            }
        }

        /// <summary>
        /// Standar width between lines.
        /// </summary>
        private double? _def_line_height;

        public double? DefaultLineHeight
        {
            get { return _def_line_height; }
            set
            {
                if (value != _def_line_height)
                {
                    _def_line_height = value;
                    FireLayoutChanged(true);
                }
            }
        }

        #endregion

        #region Layout

        /// <summary>
        /// For when only the height has changed
        /// </summary>
        private void HeightLayout()
        {
            if (_height.HasValue && Overflow == chTextBox.Overdraw.Hidden)
            {
                //Heightlayout hasn't been updated to handle documents
                //where there may be lines that has yet to be layed out.
                //
                //So we call for a full layout.
                FireLayoutChanged(false);
                return;
            }

            if (_doc == null) return;

            int c = 0;
            xIntRange char_range = new xIntRange();
            foreach (var par in _par_metrics)
            {
                char_range.Max = char_range.Min + par.CharCount;

                if (char_range.Overlaps(_range))
                    HeightLayout(par, c++);
                else if (_range.Max < char_range.Min)
                    break;

                char_range.Min = char_range.Max + 1;
            }

            _need_height_layout = false;
        }

        /// <summary>
        /// For when only the height has changed
        /// </summary>
        private void HeightLayout(chParagraph.ParMetrics par, int index)
        {
            int line_count = _par_metrics[index].LineCount;
            double height, full_height;
            chLineWrapper.LineVMetrics line, prev = null;

            line = par.GetVMetrics(0);
            if (line == null)
            {
                _par_metrics[index].ParGap = 0;
                _par_metrics[index].Height = 0;
                return;
            }
            //Sets the paragraph gap to the height of the first line.
            //The line can be taller than this, but this is intended
            //to be a sensible number.
            //
            //Do note, FontHeight adds "text rise" which might not be
            //ideal.
            _par_metrics[index].ParGap = line.FontHeight;

            //YMax can extend above "FontHeight" when a block
            //is on the line.
            if (_smart_height)
                full_height = Math.Max(line.FontHeight, line.YMax);
            else
                full_height = line.FontHeight;
            line.Height = full_height;

            prev = line;
            for (int count = 1; count < line_count; count++)
            {
                line = par.GetVMetrics(count);
                if (line == null)
                    break;

                if (_smart_height)
                    height = Math.Max(line.FontHeight, line.YMax);
                else
                    height = line.FontHeight;
                line.Height = height;
                full_height += height;
                prev = line;
            }
            _par_metrics[index].Height = full_height;
            _par_metrics[index].LastDescent = prev.Descent;
        }

        public void Layout()
        {
            if (_need_layout)
                LayoutImpl();
            else if (_need_height_layout)
                HeightLayout();
        }

        private void LayoutImpl()
        {
            _need_height_layout = _need_layout = false;
            if (_doc == null) return;

            var cur_state = _org_state.Copy();
            if (cur_state.Tf == null)
                throw new NotSupportedException("Default font required");

            //If the width isn't set, it needs to be calculated
            if (!has_width)
                _content_width = 0;

            double full_height = 0;
            xIntRange par_range = new xIntRange();
            bool first = true;
            _last_displayed_character = -1;
            for (int index = 0; index < _doc.ParagraphCount; index++)
            {
                var par = _doc[index];
                var par_length = par.Length;
                par_range.Max = par_range.Min + par_length;
                _par_metrics[index].CharCount = par_length;

                if (par_range.Overlaps(_range))
                {
                    var pm = _par_metrics[index];
                    int end = Math.Min(_range.Max, par_range.Max) - par_range.Min;
                    int layout_end = Layout(
                        _doc[index],
                        pm, cur_state,
                        full_height,
                        first ? 0 : _paragraph_gap,
                        Math.Max(_range.Min, par_range.Min) - par_range.Min,
                        end
                        );
                    full_height += pm.Height;
                    if (end != layout_end)
                    {
                        _last_displayed_character = par_range.Min + layout_end;
                        break;
                    }

                    first = false;
                }
                else if (_range.Max < par_range.Min)
                    break;

                par_range.Min = par_range.Max + 1;
            }
        }

        /// <summary>
        /// Lays out a paragraph
        /// </summary>
        /// <param name="par">Paragraph to lay out</param>
        /// <param name="par_metrics">Metrics for this paragraph will be stored in this object</param>
        /// <param name="current_height">Current height of the text</param>
        /// <param name="paragraph_gap">Gap before the paragraph is multiplied with this value. Set 0 for no gap</param>
        /// <param name="start_cr">First character in the document</param>
        /// <param name="end_chr">Last character in the document</param>
        /// <returns>Last character layed out</returns>
        private int Layout(chParagraph par, chParagraph.ParMetrics par_metrics, cRenderState cur_state, double current_height, double paragraph_gap, int start_cr, int end_chr)
        {
            //Width of this paragraph's content
            var content_width = has_width ? _content_width : double.MaxValue;

            //Number of lines in this paragraph
            _line_count -= par_metrics.LineCount;

            //Resets layout data for this paragraph
            par_metrics.ResetLayout();
            double height, full_height = 0;
            chLineWrapper.LineVMetrics line, prev = null;
            line = par_metrics.SetLineWidth(content_width, DefaultLineHeight, cur_state, start_cr, end_chr);

            //Null means there's nothing in the paragraph. Not even a line. If such a paragraph pops up,
            //it will be completly ignored.
            if (line == null)
            {
                par_metrics.ParGap = 0;
                par_metrics.Height = 0;
                par_metrics.LineCount = 0;

                return start_cr - 1;
            }
            int count = 1; //<-- counts nr. lines

            //Sets the paragraph gap to the height of the first line.
            //The line can be taller than this, but this is intended
            //to be a sensible number.
            //
            //Do note, FontHeight adds "text rise" which might not be
            //ideal.
            par_metrics.ParGap = line.FontHeight;

            //Height of the first line
            if (_smart_height)
                full_height = Math.Max(line.FontHeight, line.YMax);
            else
                full_height = line.FontHeight;
            line.Height = full_height;

            full_height += par_metrics.ParGap * paragraph_gap;

            //There may not be room for the first line of the paragraph. 
            if (Overflow == chTextBox.Overdraw.Hidden && _height != null && current_height + full_height > _height.Value)
            {
                par_metrics.ParGap = 0;
                par_metrics.Height = 0;
                par_metrics.LineCount = 0;
                par_metrics.CharCount = 0;

                return start_cr - 1;
            }

            //Line is adjusted with FirstLineIndent, so we have to add that value
            double max_width = line.GetContentWidth(_strip_sb, cur_state.RightToLeft) + (start_cr == 0 ? par.FirstLineIndent : 0);
            start_cr += line.CharCount;
            prev = line;

            while (start_cr <= end_chr)
            {
                line = par_metrics.SetLineWidth(content_width, DefaultLineHeight, cur_state, start_cr, end_chr);
                if (line == null)
                {
                    end_chr = start_cr - 1;
                    break;
                }

                max_width = Math.Max(line.GetContentWidth(_strip_sb, cur_state.RightToLeft), max_width);
                if (_smart_height)
                    height = Math.Max(line.FontHeight, line.YMax);
                else
                    height = line.FontHeight;
                line.Height = height;
                full_height += height;

                if (Overflow == chTextBox.Overdraw.Hidden && _height != null && current_height + full_height > _height.Value)
                {
                    full_height -= height;
                    end_chr = start_cr - 1;
                    break;
                }

                start_cr += line.CharCount; //<-- Will be +1 too big when reaching end_chr (unless end_chr is the true end of the line).
                prev = line;
                count++;
            }
            par_metrics.Height = full_height;
            par_metrics.LineCount = count;
            par_metrics.LastDescent = prev.Descent;
            _line_count += count;

            if (!has_width)
                _content_width = Math.Max(_content_width, max_width);

            return end_chr;
        }

        #endregion

        #region Position

        public double OffsetX { get; set; }
        public double OffsetY { get; set; }

        #endregion

        #region Render

        /// <summary>
        /// Renders the text box. State will be saved and restored.
        /// </summary>
        /// <param name="draw">Render target</param>
        /// <param name="state">Render state</param>
        public void Render(IDraw draw, ref cRenderState state)
        {
            if (state.GS != GS.Page)
                throw new NotSupportedException("State must be page");

            if (_doc != null)
            {
                double move_x = OffsetX,
                       move_y = OffsetY;

                state.BeginText(draw);
                if (move_x != 0 || move_y != 0)
                {
                    state.TranslateTLM(move_x, move_y);
                    draw.TranslateTLM(move_x, move_y);
                }
                var cls = RenderText(draw, state, false);
                state.EndText(draw);

                //Draws images and underlines
                cDraw.DrawBlock(draw, cls, state);
            }
        }

        /// <summary>
        /// Draws just the text, makes no adjustmet to the position.
        /// </summary>
        /// <param name="draw">Surface to draw to</param>
        /// <param name="state">Current render state</param>
        /// <param name="move_text">
        /// Whenever to move the first line down by its height.
        /// If true, the first line will be drawn above the position,
        /// then the rest are drawn below. 
        /// </param>
        public cBlock[][] RenderText(IDraw draw, cRenderState state, bool move_text)
        {
            if (state.GS != GS.Text)
                throw new NotSupportedException("State must be text");

            cBlock[] blocks;

            if (state.Tf == null || !ReferenceEquals(state.Tf, _org_state.Tf) || state.Tfs != _org_state.Tfs)
            {
                state.Tf = _org_state.Tf;
                state.Tfs = _org_state.Tfs;
                if (state.Tf == null)
                    throw new NotSupportedException("Default font required");
                draw.SetFont(state.Tf, state.Tfs);
            }
            if (_org_state.fill_color != null && state.fill_color != _org_state.fill_color)
                _org_state.fill_color.SetColor(state, true, draw);
            if (_org_state.Tc != state.Tc)
            {
                state.Tc = _org_state.Tc;
                draw.SetCharacterSpacing(_org_state.Tc);
            }
            if (_org_state.Tw != state.Tw)
            {
                state.Tw = _org_state.Tw;
                draw.SetWordSpacing(_org_state.Tw);
            }
            if (_org_state.Th != state.Th)
            {
                state.Th = _org_state.Th;
                draw.SetHorizontalScaling(_org_state.HorizontalScaling);
            }

#if DIRECT_DRAW
            var text_renderer = new TextRenderer(state, draw);
#else
            var text_renderer = new TextRenderer(state);
            CompiledLine cl;
#endif
            //Setting reset state false, saves us some needless Tw commands when column aligning the
            //text. 
            text_renderer.LsbAdjustText = _strip_sb;
            text_renderer.ResetState = false;
            text_renderer.ResetState_tw = state.Tw;
            var compiled_lines = new List<cBlock[]>(_line_count);
            int line_nr = 0;
            double move = 0, current_height = 0;
            double min_prec = 1 / Math.Pow(10, draw.Precision);

            //When has_width = true it means that the induvidual lines has width. When
            //false, we have to supply a width to use for text alignment.
            double? render_width;
            if (has_width)
                render_width = null;
            else
                render_width = _content_width;

#if DONT_MOVE_OPTIMIZATION
            //To avoid a needless Td, we special case this particular condition.
            move_text = move_text || !(PositionOver && !HasBorder && BackgroundColor == null);
#endif
            xIntRange par_range = new xIntRange();
            for (int index = 0; index < _par_metrics.Length; index++)
            {
                var par = _par_metrics[index];
                par_range.Max = par_range.Min + par.CharCount;
                if (!par_range.Overlaps(_range))
                {
                    if (_range.Max < par_range.Min)
                        break;
                    par_range.Min = par_range.Max + 1;
                    continue;
                }

                //Since ResetState = false, tw must be reset manually when not doing column rendering, in case the previous paragraph was rendered with
                //column rendering (as column rendering modify Tw and don't reset it back).
                if (!text_renderer.ResetState && par.Par.Alignment < chParagraph.Alignement.Column && text_renderer.State.Tw != text_renderer.ResetState_tw)
                    text_renderer.SetWordGap(text_renderer.ResetState_tw);

                int par_count = par.LineCount;
                if (par_count == 0) continue;

                var par_line = par.GetLine(0);
                //Note that par_line.Range is the range within a paragraph, not the range from the starting point of the document. Thus, if there's
                //more than one paragraph, this assert alwyas triggers. 
                //Debug.Assert(new xIntRange(par_line.Range.Min + par_range.Min, Math.Max(par_line.Range.Max, 0) + par_range.Min).Overlaps(_range), "Likely a Layout() bug.");

                if (line_nr != 0)
                {
                    //Move the paragraph down, except the first paragraph. The first move
                    //is the height of the first line in the paragrap. Then the gap between
                    //paragraphs are added to this.
                    move = -par_line.LineHeight;
                    move = move - par.ParGap * _paragraph_gap;

                }
                else
                {
                    //Moves down the height of the very first line.
                    //Using FontHeight as one should not include "Default height" in this.
                    move = -par_line.LineHeight;
                }

                if (Overflow == chTextBox.Overdraw.Hidden && _height != null)
                {
                    current_height -= move;
                    if (current_height > _height.Value)
                        break;
                }

                line_nr++;

                if (!move_text)
                    move_text = true;
                else
                {
                    //We could try to be clever and use SetTlandTransTLM when we got a series
                    //of single line paragraphs, but I'm unsure if that's a common scenario.
                    state.TranslateTLM(0, move);
                    draw.TranslateTLM(0, move);
                }

                for (int c = 0; ; )
                {
                    //Renders the line
                    if (!par_line.Render(text_renderer, render_width))
                        break;

                    if (!(++c < par_count))
                    {
                        //Draws the line
#if DIRECT_DRAW
                        blocks = text_renderer.MakeBlocks();
                        if (blocks != null)
                            compiled_lines.Add(blocks);
#else
                        cl = text_renderer.Make();
                        if (cl.Blocks != null) compiled_lines.Add(cl.Blocks);
                        draw.Draw(cl);
#endif

                        break;
                    }
                    //Gets the height of the next line.
                    par_line = par.GetLine(c);
                    move = -par_line.LineHeight;

                    //Draws the line
#if DIRECT_DRAW
                    blocks = text_renderer.MakeBlocks();
                    if (blocks != null)
                        compiled_lines.Add(blocks);
#else
                    cl = text_renderer.Make();
                    if (cl.Blocks != null) compiled_lines.Add(cl.Blocks);
                    draw.Draw(cl);
#endif

                    if (Overflow == chTextBox.Overdraw.Hidden && _height != null)
                    {
                        current_height -= move;
                        if (current_height > _height.Value)
                            break;
                    }

                    //Moves down a line.
                    if (Util.Real.Same(state.Tl, -move, min_prec))
                    {
                        //We can use this shorten command, as the Tl is the distance we need to move
                        state.TranslateTLM();
                        draw.TranslateTLM();
                    }
                    else if (par.NumLineHeights(c + 1, -move, min_prec) > 1 || state.Tl == 0)
                    {
                        //Since more than one line has the same line height, we set tl to the new line height
                        state.SetTlandTransTLM(0, move);
                        draw.SetTlandTransTLM(0, move);
                    }
                    else
                    {
                        //We move without setting the tl.
                        state.TranslateTLM(0, move);
                        draw.TranslateTLM(0, move);
                    }
                }

                par_range.Min = par_range.Max + 1;
            }

            state.BreakWordCharacter = null;
            return compiled_lines.Count > 0 ? compiled_lines.ToArray() : null;
        }

        #endregion

        /// <summary>
        /// Get the height of the line, not including the descender
        /// </summary>
        /// <param name="par_nr">Paragraph</param>
        /// <param name="line_nr">Line in the paragraph</param>
        /// <returns>Height of the line, not including descender</returns>
        public double GetLineHeight(int par_nr, int line_nr)
        {
            if (_par_metrics == null || _par_metrics.Length <= par_nr)
                return 0;
            chParagraph.ParMetrics par = _par_metrics[par_nr];
            if (par.LineCount <= line_nr)
                return 0;
            var line = par.GetLine(line_nr);
            return line.LineHeight;
        }

        private double GetHeight(int paragraph_count)
        {
            double height = _par_metrics[0].Height;
            for (int c = 1; c < paragraph_count; c++)
            {
                height += _par_metrics[c].Height;
                height += _par_metrics[c].ParGap * _paragraph_gap;
            }
            return height;
        }

        private int GetParStartPos(int index)
        {
            int schar = 0, c;
            for (c = 0; c < index; c++)
            {
                schar += _par_metrics[index].CharCount;
            }
            return schar + c;
        }
    }
}
