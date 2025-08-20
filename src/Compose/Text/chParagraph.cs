//#define ALT_SPACE_COLUMN_HANDELING //Must also be defined in chLine
// ^ When doing column rendering (and this is defined) consecutive space characters are treated
//   as if they are one single space character.
//#define OLD_CODE
using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Compile;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// Compose Horizontal Paragraph
    /// 
    /// Contains one or more chLineWrappers and handles line alignement (Right/Left/etc).
    /// </summary>
    public class chParagraph
    {
        #region Variables and properties

        private List<chLine> _lines = new List<chLine>(1);

        #region Client variables
        //These variables aren't used by the paragraph itsef

        ///// <summary>
        ///// If true then lines will not have their height adjusted to prevent overlapping.
        ///// </summary>
        //public bool ForceLineHeight = false;

        #endregion

        /// <summary>
        /// How to align the text in this paragraph
        /// </summary>
        /// <remarks>
        /// If you change alignment between rendering lines, remember to set "Tw" back
        /// to it's original state for non-column lines.
        /// </remarks>
        public Alignement Alignment = Alignement.Left;

        /// <summary>
        /// Number of lines in the paragraph
        /// </summary>
        public int LineCount { get { return _lines.Count; } }

        /// <summary>
        /// How much to move the first line in the paragraph
        /// </summary>
        private double _first_line_indent = 0;

        /// <summary>
        /// Indents the first line in the paragraph
        /// </summary>
        /// <remarks>
        /// A LeftIndent, RightIndent feature should be added. (Which is basically a form of padding)
        /// Since all lines are to be padded equally, the easiest way to implement this it to:
        ///  - All lines are measured/rendered with width - right/left indent
        ///  - All lines are shifted, before rendering, by the left indent (also for right to left write direction),
        ///    except when rendering with alignment.right.
        ///    - This can be done through "SetOffset or SetPending, which ever of those don't move tab position)
        ///    - Alternativly, one can move the text through another means, but if so, tab position must align
        ///      with non-indented paragraphs.
        ///    - Column rendering might give some trouble, or it might not, just remeber to test it.
        ///    - Align.right is sure to give trouble, as it will position the line at the rigth edge. The
        ///      fix may be to SetOffset(-RightAlign).
        ///  - When the width of the paragraps is to be determined, by chTextBox, Left/Right indent must be added.
        ///    - If useing "SetOffset" I belive the left indent will be added. At least, chTextbox correctly finds
        ///      the size when drawing a border arround a single line with indent.
        ///  - There's no need to worry about whenever to add indent when rendering a subsection of the paragraph.
        ///    Unlike with _fist_line_indent, the indent is always to be added. This because subsection rendering
        ///    is used to flow the text accross multiple text boxes, not to start/stop rendering piecewise.
        ///     - Though, you can check if rendering start/stop at the first/last character of a line (after line 
        ///       wrapping), if piecewise rendering is to be supported. Not worth the bother, I'd say.
        ///  - Note that lines are rendered within the "ParagraphLine" datastructure. It already got an Indent parameter,
        ///    that will work fine for implementing LeftIndent, so, IOW, a right indent parameter is needed.
        /// </remarks>
        public double FirstLineIndent
        {
            get { return _first_line_indent; }
            set
            {
                if (_first_line_indent != value)
                {
                    _first_line_indent = value;
                    if (OnParagraphChanged != null)
                        OnParagraphChanged(this, true);
                }
            }
        }

        /// <summary>
        /// Length of this paragraph
        /// </summary>
        public int Length
        {
            get
            {
                int length = 0;
                foreach (var line in _lines)
                    length += line.Length;
                return length - 1 + _lines.Count;
            }
        }

        /// <summary>
        /// Heights of lines in this paragraph
        /// </summary>
        private double? _line_height;

        /// <summary>
        /// Heights of lines in this paragraph
        /// </summary>
        public double? LineHeight
        {
            get { return _line_height; }
            set
            {
                if (value != _line_height)
                {
                    _line_height = value;
                    if (OnParagraphChanged != null)
                        OnParagraphChanged(this, true);
                }
            }
        }
        
        #endregion

        #region Init

        /// <summary>
        /// Creates an empty paragraph
        /// </summary>
        public chParagraph()
        {
        }

        /// <summary>
        /// Creates a paragraph with text
        /// </summary>
        /// <param name="text">Text for the paragraph, note that newlines are respected</param>
        public chParagraph(string text)
        {
            foreach (var line in text.Split('\n'))
                AddLine(line);
        }

        #endregion

        #region Events

        /// <summary>
        /// For handeling document changes
        /// </summary>
        /// <param name="par">Paragraph that was changed</param>
        /// <param name="index">Index of the paragraph in the document</param>
        /// <param name="added">If this paragraph was newly added</param>
        /// <param name="layout">If paragraph layout changed</param>
        public delegate void ParagraphChangedHandler(chParagraph par, bool layout);

        public event ParagraphChangedHandler OnParagraphChanged;

        #endregion

        #region Text

        public void InsertImg(int pos, PdfImage img)
        {
            InsertImg(pos, img, null, null, null);
        }

        public void InsertImg(int pos, PdfImage img, double height)
        {
            InsertImg(pos, img, null, height, null);
        }

        public void InsertImg(int pos, PdfImage img, double? width, double? height, BlockAlign? align)
        {
            int start;
            var line = GetLine(pos, out start);
            line.Insert(pos - start, img, width, height, align);
            if (OnParagraphChanged != null)
                OnParagraphChanged(this, true);
        }

        /// <summary>
        /// Appends text to the last line
        /// </summary>
        public void Append(string text)
        {
            var lines = text.Split('\n');
            if (_lines.Count == 0)
                AddLine(lines[0]);
            else
                _lines[_lines.Count - 1].Append(lines[0]);
            for (int c = 1; c < lines.Length; c++)
                AddLine(lines[c]);
            if (OnParagraphChanged != null)
                OnParagraphChanged(this, true);
        }

        /// <summary>
        /// Appends text to the last line
        /// </summary>
        public void Append(PdfImage img, double height)
        {
            if (_lines.Count == 0)
                AddLine("");
            _lines[_lines.Count - 1].Append(img, height);
            if (OnParagraphChanged != null)
                OnParagraphChanged(this, true);
        }

        public void Append(PdfImage img, double width, double height, BlockAlign align)
        {
            if (_lines.Count == 0)
                AddLine("");
            _lines[_lines.Count - 1].Append(img, width, height, align);
            if (OnParagraphChanged != null)
                OnParagraphChanged(this, true);
        }

        /// <summary>
        /// Adds a new line of text
        /// </summary>
        /// <param name="line">Line to add</param>
        public void AddLine(string line)
        {
            AddLine(new chLine(line));
        }

        /// <summary>
        /// Adds a new line of text
        /// </summary>
        /// <param name="line">Line to add</param>
        public void AddLine(chLine line)
        {
            _lines.Add(line);
            if (OnParagraphChanged != null)
                OnParagraphChanged(this, true);
        }

        public string GetText(int start, int end)
        {
            int line_end = 0, line_start, count = _lines.Count;
            var sb = new StringBuilder(end - start + 1);
            for (int c = 0; c < count; c++)
            {
                var line = _lines[c];
                line_start = line_end;
                line_end += line.Length;
                var clip = ClipAndShift(start, end, line_start, line_end);
                if (clip.Length > 0)
                    sb.Append(line.GetText(clip.Start, clip.End));
                else if (end < line_start)
                    break;
            }
            return sb.ToString();
        }

        private IntRange ClipAndShift(int start, int end, int range_start, int range_end)
        {
            IntRange r;
            if (end < range_start || range_start > range_end) return new IntRange(0, -1);
            //if (start > Range.End || end < Range.Start) throw new IndexOutOfRangeException();
            r.Start = Math.Max(start, range_start);
            r.End = Math.Min(end, range_end);
            r.Start -= range_start;
            r.End -= range_start;
            return r;
        }

        private chLine GetLine(int pos, out int start)
        {
            int end = 0;
            start = 0;
            foreach (var line in _lines)
            {
                start = end;
                end += line.Length + 1;
                if (pos < end)
                    return line;
            }
            return _lines[_lines.Count - 1];
        }

        #endregion

        #region Width and height

        public ParMetrics CreateMetrics()
        {
            var lines = new LineMetrics[_lines.Count];
            int count = 0, start = 0;
            foreach(var line in _lines)
            {
                lines[count++] = new LineMetrics(start, new chLineWrapper(line));
                start += line.Length + 1; //+1 is \n
            }
            return new ParMetrics(this, lines);
        }

        #endregion

        #region Styling

        private delegate void StyleFunction(int start, int end, chLine ch);

        /// <summary>
        /// Styles a section of text using the supplied function.
        /// </summary>
        private void SetStyle(int start, int end, bool layout, StyleFunction set)
        {
            int line_end = 0, line_start = 0, count = _lines.Count;
            for (int c = 0; c < count; c++)
            {
                var line = _lines[c];
                line_end += line.Length - 1;
                var clip = ClipAndShift(start, end, line_start, line_end);
                if (clip.Length > 0)
                    set(clip.Start, clip.End, line);
                else if (end < line_start)
                    break;
                //Line_end is the charindex before the "endline" character. By adding two, we
                //get the first character of the next line.
                line_end += 2;
                line_start = line_end;
            }
            if (OnParagraphChanged != null)
                OnParagraphChanged(this, layout);
        }

        /// <summary>
        /// Sets a section of text as underlined.
        /// </summary>
        public void SetUnderline(int start, int end, double? underline)
        {
            SetStyle(start, end, false, (s, e, l) => { l.SetUnderline(s, e, underline); });
        }

        /// <summary>
        /// Sets font on a section of text
        /// </summary>
        public void SetTextRise(int start, int end, double? size, double? text_rise)
        {
            SetStyle(start, end, false, (s, e, l) => { l.SetTextRise(s, e, size, text_rise); });
        }

        /// <summary>
        /// Sets font on a section of text
        /// </summary>
        public void SetFont(int start, int end, cFont font, double? size)
        {
            SetStyle(start, end, true, (s, e, l) => { l.SetFont(s, e, font, size); });
        }

        /// <summary>
        /// Sets font on a section of text
        /// </summary>
        public void SetFont(int start, int end, cFont font, double? size, double? text_rise)
        {
            SetStyle(start, end, true, (s, e, l) => { l.SetFont(s, e, font, size, text_rise); });
        }

        /// <summary>
        /// Sets color on a section of text
        /// </summary>
        public void SetColor(int start, int end, cBrush fill, cBrush stroke)
        {
            SetStyle(start, end, false, (s, e, l) => { l.SetColor(s, e, fill, stroke); });
        }

        /// <summary>
        /// Sets the color of the entire paragraph
        /// </summary>
        public void SetColor(cBrush fill, cBrush stroke)
        {
            SetColor(0, Length, fill, stroke);
        }

        /// <summary>
        /// Sets font on the entire paragraph
        /// </summary>
        public void SetFont(cFont font, double? size)
        {
            SetFont(0, Length, font, size);
        }

        /// <summary>
        /// Sets render mode on a section of text
        /// </summary>
        public void SetRenderMode(int start, int end, xTextRenderMode? mode)
        {
            SetStyle(start, end, false, (s, e, l) => { l.SetRenderMode(s, e, mode); });
        }

        #endregion

        #region Helper classes

        /// <summary>
        /// How the contents of a paragraph is to be aligned.
        /// </summary>
        public enum Alignement
        {
            Left,
            Middle,
            Right,
            Column,

            /// <summary>
            /// Aligns to the writing direction. Also, for left to
            /// right text, glyphs are aligned to the start of
            /// the character, not Left Side Bearings (as is the
            /// case with Left alignement)
            /// </summary>
            WriteDirection
        }

        /// <summary>
        /// A paragraph is to be split into lines, for line wrapping. This helper class
        /// assists with this.
        /// </summary>
        /// <remarks>
        /// These methods were originaly part of chParagraph, but I've decided to take
        /// display related data out of the main document structure. This way one can
        /// give the document to any textbox, without worrying about them stepping on
        /// eachother's toes.
        /// </remarks>
        public class ParMetrics
        {
            #region Variables and properties

            /// <summary>
            /// The paragrah these metrics are for
            /// </summary>
            public readonly chParagraph Par;

            /// <summary>
            /// Metrics over each wrapped line. 
            /// </summary>
            LineMetrics[] _lines;

            /// <summary>
            /// The last known line in the paragraph
            /// </summary>
            /// <remarks>
            /// Used to detect the last line in the paragraph when column rendering, so to
            /// give it WriteDirection alignement.
            /// </remarks>
            private int _last_line_nr = 0;

            #region Client variables

            //ParMetric was originaly a helper class for chTextBox. This information
            //is stored for layout purposes. 

            /// <summary>
            /// Number of lines in the paragraph
            /// </summary>
            public int LineCount;

            /// <summary>
            /// Number of characters in the paragraph
            /// </summary>
            public int CharCount;

            /// <summary>
            /// Paragraph gap. This is the gap up to the previous paragraph. 
            /// Usually set to the font height of the first line.
            /// </summary>
            public double ParGap;

            /// <summary>
            /// Height of the paragraph. Does not include gap
            /// </summary>
            public double Height;

            /// <summary>
            /// Descent of the last line of the paragraph
            /// </summary>
            /// <remarks>Might be an idea to drop this value in favor of simply using "Descent"</remarks>
            public double LastDescent;

            #endregion

            #region First line properties
            //The first line in a paragraph needs special treatment. So for convinience,
            //these properties are made avalible.

            /// <summary>
            /// The ascent of the first line of this paragraph
            /// </summary>
            public double Ascent { get { return GetLineAscent(0); } }

            /// <summary>
            /// The true Ascent of the first line of this paragraph
            /// </summary>
            public double YMax { get { return GetLineYMax(0); } }

            /// <summary>
            /// The true descent of the last line of this paragraph
            /// </summary>
            public double YMin { get { return _lines[_lines.Length - 1].Line.YMin; } }

            /// <summary>
            /// The descent of the last line of this paragraph, note this value is also avalible
            /// through "LastDescent". 
            /// </summary>
            public double Descent { get { return _lines[_lines.Length - 1].Line.Descent; } }

            #endregion

            /// <summary>
            /// Number of lines in the paragraph
            /// </summary>
            /// <remarks>The normal LineCount has to be set/kept up to date, by the client</remarks>
            public int TrueLineCount
            {
                get
                {
                    int count = 0;
                    foreach (var line in _lines)
                        count += line.LineCount;
                    return count;
                }
            }

            /// <summary>
            /// If all the lines in this paragraph has ben set
            /// </summary>
            /// <remarks>
            /// I'm setting this one internal. 
            /// 
            /// The problem is I don't know how this value will work in all situations. It does
            /// work if read right after calling SetLineWidth.</remarks>
            internal bool AllLinesSet
            {
                get
                {
                    if (_lines.Length == 0) return true;

                    return _lines[_lines.Length - 1].Line.AllLinesSet;
                }
            }

            #endregion

            #region Init

            internal ParMetrics(chParagraph par, LineMetrics[] l)
            { Par = par; _lines = l; }

            #endregion

            #region render

            /// <summary>
            /// Renders a line. Note, that state.Tw should potentially be save/restored as it may
            /// be modified.
            /// </summary>
            /// <returns>True if the line was rendered</returns>
            public bool RenderLine(int nr, TextRenderer text_renderer)
            {
                text_renderer.Init();

                var line = GetLine(nr);
                if (line == null) return false;
                return line.Render(text_renderer, null);
            }

            #endregion

            #region Fetch line metrics

            public ParagraphLine GetLine(int nr)
            {
                for (int c = 0; c < _lines.Length; c++)
                {
                    LineMetrics wrapline = _lines[c];
                    if (wrapline.containLine(nr))
                    {
                        //Indent is only set if:
                        // - We're on the first line (nr == 0)
                        // - That we're rendering from the beginning of the paragraph (wrapline.Line.GetRange(nr).Min == 0)
                        // - With a Par._first_line_indent != 0 check to avoid a needles method call.
                        //Subtlety: There's no need to substract wrapline.LineNr.Start from nr when calling "Line.GetRange(nr).Min" as
                        //          wrapline.LineNr.Start will always be 0
                        double indent = c == 0 && Par._first_line_indent != 0 && wrapline.Line.GetRange(nr).Min == 0 ? Par._first_line_indent : 0;
                        if (Par.Alignment == Alignement.Column && nr >= _last_line_nr)
                            return new ParagraphLine(wrapline.Line, Alignement.WriteDirection, nr - wrapline.LineNr.Start, indent);
                        return new ParagraphLine(wrapline.Line, Par.Alignment, nr - wrapline.LineNr.Start, indent);
                    }
                }
                return null;
            }

            public chLineWrapper.LineVMetrics GetVMetrics(int nr)
            {
                int count = _lines.Length;
                for (int c = 0; c < count; c++)
                {
                    var line = _lines[c];
                    if (line.containLine(nr))
                        return line.Line.GetVMetrics(nr - line.LineNr.Start);
                }
                return null;
            }

            public double GetLineHeight(int nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                        return line.Line.GetLineHeight(nr - line.LineNr.Start);
                return 0;
            }

            public double GetFontHeight(int nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                        return line.Line.GetFontHeight(nr - line.LineNr.Start);
                return 0;
            }

            internal double GetLineWidth(int nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                    {
                        //Indent is only set if:
                        // - We're on the first line (nr == 0)
                        // - That we're rendering from the beginning of the paragraph (wrapline.Line.GetRange(nr).Min == 0)
                        // - With a Par._first_line_indent != 0 check to avoid a needles method call.
                        double indent = nr == 0 && Par._first_line_indent != 0 && line.Line.GetRange(nr).Min == 0 ? Par._first_line_indent : 0;
                        return line.Line.GetLineWidth(nr - line.LineNr.Start);
                    }
                return 0;
            }

            public double GetSpaceAbove(int line_nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(line_nr))
                        return line.Line.GetSpaceAbove(line_nr - line.LineNr.Start);
                return 0;
            }

            public double GetLineAscent(int nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                        return line.Line.GetLineAscent(nr - line.LineNr.Start);
                return 0;
            }

            public double GetLineYMax(int nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                        return line.Line.GetLineYMax(nr - line.LineNr.Start);
                return 0;
            }

            public double GetLineYMin(int nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                        return line.Line.GetLineYMin(nr - line.LineNr.Start);
                return 0;
            }

            public double GetLineDescent(int nr)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                        return line.Line.GetLineDescent(nr - line.LineNr.Start);
                return 0;
            }

            /// <summary>
            /// Number of lines of the set height, count includes the pos
            /// </summary>
            /// <param name="pos">Line position from where to count from</param>
            /// <param name="height">The height to compare against</param>
            /// /// <param name="delta">Preicision delta</param>
            /// <returns>Number of lines of the given height</returns>
            public int NumLineHeights(int pos, double height, double delta)
            {
                int count = _lines.Length;
                for (int c = 0; c < count; c++)
                {
                    var line = _lines[c];
                    if (line.containLine(pos))
                    {
                        int num = line.Line.NumLineHeights(pos++ - line.LineNr.Start, height, delta);
                        for (c++; c < count; c++, pos++)
                        {
                            line = _lines[c];
                            if (!line.containLine(pos))
                                break;
                            int add = line.Line.NumLineHeights(pos, height, delta);
                            num += add;
                            if (add + pos < line.LineCount)
                                break;
                        }
                        return num;
                    }
                }
                return 0;
            }

            #endregion

            #region Layout

            /// <summary>
            /// Sets the width of a line.
            /// </summary>
            /// <param name="line_height">Default line height. If a paragraph has line height set, this parameter is ignored</param>
            /// <returns>Returns null when there are no more lines to set</returns>
            public chLineWrapper.LineVMetrics SetLineWidth(double width, double? line_height, cRenderState state)
            {
                if (Par._line_height != null)
                    line_height = Par._line_height;

                int count = _lines.Length;
                for (int c = 0; c < count; c++)
                {
                    var line = _lines[c];
                    if (!line.AllLinesSet)
                    {
                        chLineWrapper.LineVMetrics ret;
                        if (c == 0 && Par._first_line_indent != 0)
                            ret = line.Line.SetLineWidth(Math.Max(0, width - Par._first_line_indent), line_height, state);
                        else
                            ret = line.Line.SetLineWidth(width, line_height, state);
                        if (ret == null)
                        {
                            line.AllLinesSet = true;
                            if (c + 1 < count)
                            {
                                var line2 = _lines[c + 1];
                                line2.LineNr.Start = line.LineNr.End + 1;
                                line2.LineNr.End = line.LineNr.End;
                            }
                        }
                        else
                        {
                            line.LineNr.End++;
                            _last_line_nr = line.LineNr.End;
                            return ret;
                        }
                    }
                }
                return null;
            }

            /// <summary>
            /// Sets the width of a line.
            /// </summary>
            /// <param name="line_height">Default line height. If a paragraph has line height set, this parameter is ignored</param>
            /// <param name="start">Start character index</param>
            /// <param name="end">End character index</param>
            /// <returns>Returns null when there are no more lines to set</returns>
            public chLineWrapper.LineVMetrics SetLineWidth(double width, double? line_height, cRenderState state, int start, int end)
            {
                if (Par._line_height != null)
                    line_height = Par._line_height;

                int count = _lines.Length, line_start, line_end;
                chLineWrapper.LineVMetrics ret;
                for (int c = 0; c < count; c++)
                {
                    var line = _lines[c];
                    if (end < line.Range.Start)
                        break;
                    if (c > 0 && line.LineNr.Start == 0)
                    {
                        //Notes the start lnr of this whole line. (When the line is wrapped,
                        //it will cover several lines)
                        var end_nr = _lines[c - 1].LineNr.End;
                        if (end_nr != -1)
                        {
                            line.LineNr.End = end_nr;
                            line.LineNr.Start = line.LineNr.End + 1;
                        }
                    }

                    //Special cases empty lines
                    if (line.Range.Length == 0)
                    {
                        if (start != line.Range.Start)
                            continue;

                        ret = line.Line.SetLineWidth(width, line_height, state, 0, -1);
                        ret.CharCount = 1; // the 1 is the \n

                        line.LineNr.End++;
                        _last_line_nr = line.LineNr.End;

                        return ret;
                    }

                    if (start > line.Range.End)
                        continue;

                    line_start = Math.Max(start, line.Range.Start) - line.Range.Start;
                    line_end = Math.Min(end, line.Range.End) - line.Range.Start;

                    if (c == 0 && Par._first_line_indent != 0 && start == 0)
                        ret = line.Line.SetLineWidth(Math.Max(0, width - Par._first_line_indent), line_height, state, line_start, line_end);
                    else
                        ret = line.Line.SetLineWidth(width, line_height, state, line_start, line_end);
                    if (ret != null)
                    {
                        line.LineNr.End++;
                        _last_line_nr = line.LineNr.End;

                        int last_index = ret.LastCharIndex;
                        //Testing last_index against line_end is not 100% correct. One must test against the line's
                        //true end. But, it matters little, as chTextBox won't care for this off by one error.
                        ret.CharCount = last_index - line_start + (line_end == last_index ? 2 : 1);
                    }

                    return ret;
                }
                return null;
            }

            /// <summary>
            /// Sets the height of the line. Note that this value is overwritten when
            /// one set the width of a line.
            /// </summary>
            /// <param name="nr">Index of line to set the height of</param>
            /// <param name="height">The new height</param>
            public void SetLineHeight(int nr, double height)
            {
                foreach (var line in _lines)
                    if (line.containLine(nr))
                    {
                        line.Line.SetLineHeight(nr - line.LineNr.Start, height);
                        break;
                    }
            }

            /// <summary>
            /// Measure a line again with a new state. Note, no linewrapping will occur.
            /// </summary>
            /// <param name="nr">The line number to set</param>
            /// <param name="state">Text state</param>
            /// <returns>New measures</returns>
            public chLineWrapper.LineVMetrics Remeasure(int nr, cRenderState state)
            {
                int count = _lines.Length;
                for (int c = 0; c < count; c++)
                {
                    var line = _lines[c];
                    if (line.containLine(nr))
                        return line.Line.Remeasure(nr - line.LineNr.Start, state);
                }
                return null;
            }

            /// <summary>
            /// Sets the paragraphs layout back to its original state
            /// </summary>
            public void ResetLayout()
            {
                _last_line_nr = 0;
                for (int c = 0; c < _lines.Length; c++)
                {
                    var lm = _lines[c];
                    lm.AllLinesSet = false;
                    lm.LineNr = new IntRange(0, -1);
                    lm.Line.ResetLayout();
                }
            }

            #endregion

            #region Indexing

            /// <summary>
            /// Indexes of affected lines
            /// </summary>
            /// <remarks>
            /// To avoid calling methods on unaffected lines.
            /// </remarks>
            private IntRange GetLineIndexes(int start, int end)
            {
                IntRange r;
                r.Start = int.MaxValue;
                r.End = 0;

                for (int c = 0; c < _lines.Length; c++)
                {
                    var line = _lines[c];
                    if (line.contain(start))
                    {
                        r.Start = c;
                        while (!line.contain(end))
                        {
                            c++;
                            if (c == _lines.Length)
                            {
                                c--;
                                break;
                            }
                            line = _lines[c];
                        }
                        r.End = c;
                        break;
                    }
                }
                return r;
            }

            private int GetLineIndex(int pos)
            {
                for (int c = 0; c < _lines.Length; c++)
                {
                    var line = _lines[c];
                    if (line.contain(c))
                        return c;
                }
                return -1;
            }

            private LineMetrics GetMetrics(int pos)
            {
                for (int c = 0; c < _lines.Length; c++)
                {
                    var line = _lines[c];
                    if (line.contain(c))
                        return line;
                }
                return _lines[_lines.Length - 1];
            }

            #endregion
        }

        /// <summary>
        /// Actual rendering of a paragraph's line is handled by this class. 
        /// </summary>
        /// <remarks>
        /// I forgot the reason for putting this in its own class
        /// 
        /// Presumably, it has to do with the meta data. The caller might wish
        /// to inspect various properties of the line before rendering it. 
        /// </remarks>
        public class ParagraphLine
        {
            #region Variables and properties

            readonly chLineWrapper Line;
            public Alignement Alignment; //Intentially miss spelled :)

            /// <summary>
            /// Line number in the chLineWrapper
            /// </summary>
            readonly int Nr;

            /// <summary>
            /// Left indent of the line. It will be shifted by this amount.
            /// </summary>
            readonly double Indent;

            /// <summary>
            /// Size of the line in device units. Not the same as chParagraph.LineHeight, 
            /// which is realtive to the fontsize
            /// 
            /// Either calculated or manualy set using "setLineHeight"
            /// </summary>
            public double LineHeight
            {
                get
                {
                    var v = Line.GetVMetrics(Nr);
                    if (v == null) return 0;
                    return v.Height;
                }
            }

            /// <summary>
            /// Returns the line height with the decent added.
            /// </summary>
            public double FullLineHeight
            {
                get
                {
                    var v = Line.GetVMetrics(Nr);
                    if (v == null) return 0;
                    return v.Height - v.Descent;
                }
            }

            /// <summary>
            /// Height of the line, as set by the tallest font
            /// </summary>
            public double FontHeight
            {
                get
                {
                    var v = Line.GetVMetrics(Nr);
                    if (v == null) return 0;
                    return v.FontHeight;
                }
            }

            /// <summary>
            /// Start character position and end character position
            /// </summary>
            public xIntRange Range { get { return Line.GetRange(Nr); } }

            #endregion

            #region Init

            internal ParagraphLine(chLineWrapper line, Alignement align, int nr, double indent)
            { Line = line; Alignment = align; Nr = nr; Indent = indent; }

            #endregion

            #region Render

            /// <summary>
            /// Renders the text in the paragraph line
            /// </summary>
            /// <param name="text_renderer">Target</param>
            /// <param name="paragraph_width">
            /// Width used for aligment calculations is set on a line by line basis, to override this
            /// supply a paragraph_width to use instead.
            /// </param>
            /// <returns>True if the line was rendered</returns>
            public bool Render(TextRenderer text_renderer, double? paragraph_width)
            {
                text_renderer.Init();
                chLineWrapper.LineHMetrics h_mertics = Line.GetLine(Nr);
                if (h_mertics == null) return false;
                double shift;

                //Sets indent on the line when columnm, middle or left aligning the line
                if (Indent != 0 && Alignment != Alignement.Right && (!text_renderer.State.RightToLeft || Alignment != Alignement.WriteDirection))
                    text_renderer.SetOffset(Indent);

                switch (Alignment)
                {
                    case Alignement.WriteDirection:
                        //It never hurts to reset the Tw (word width) parameter, beyond bloating the output file's size. 
                        //
                        //Write direction is used in conjunction with column rendering, so we
                        //reset becase colum rendering sets Tw between lines.
                        if (!text_renderer.ResetState && text_renderer.State.Tw != text_renderer.ResetState_tw)
                            text_renderer.SetWordGap(text_renderer.ResetState_tw);

                        if (text_renderer.State.RightToLeft)
                            goto case Alignement.Right;
                        else
                            goto case Alignement.Left;
                    case Alignement.Left:
                        if (text_renderer.State.RightToLeft)
                        {
                            //Todo: Should adjust for LSB/RSB. (As done for LeftToRight)
                            //      Beware that for non-breaking space characters, the
                            //      RSB is equal to the character's width. 

                            text_renderer.SetOffset(-h_mertics.RTLPrecWhitespace);
                            h_mertics.RenderLine(text_renderer);
                        }
                        else
                        {
                            //If there's no preceeding whitespace, we position the glyph to start
                            //exactly at the edge. I.e. we move back with the left side bearings
                            //of that glyph.
                            if (h_mertics.PreceedingWhitespace == 0 && text_renderer.LsbAdjustText)
                                //using SetPending for the sake of tab calculation. 
                                //If SetOffset is used, the tabs will shift a little (if I recall correctly).
                                text_renderer.SetPending(-h_mertics.FirstLSB);
                            h_mertics.RenderLine(text_renderer);
                        }
                        return true;
                    case Alignement.Right:
                        //Todo: Should make RSB/LSB adjustments to align the text perfectly with the edge (see code above for how)
                        if (text_renderer.State.RightToLeft)
                            shift = (paragraph_width == null ? h_mertics.Width : paragraph_width.Value) - h_mertics.ActualWidth + h_mertics.FirstRSB;
                        else
                        {
                            shift = (paragraph_width == null ? h_mertics.Width : paragraph_width.Value) - h_mertics.ActualWidth + h_mertics.LTRTrailWhitespace;
                        }
                        text_renderer.SetOffset(shift);
                        h_mertics.RenderLine(text_renderer);
                        return true;

                    case Alignement.Middle:
                        if (text_renderer.State.RightToLeft)
                            //Can look a little confusing, but the key here is that TrailingWhitespace 
                            //is at the start of the line.
                            shift = (paragraph_width == null ? h_mertics.Width : paragraph_width.Value) - (h_mertics.ActualWidth + h_mertics.TrailingWhitespace);
                        else
                            shift = (paragraph_width == null ? h_mertics.Width : paragraph_width.Value) - (h_mertics.ActualWidth - h_mertics.TrailingWhitespace);
                        text_renderer.SetOffset(shift / 2);
                        h_mertics.RenderLine(text_renderer);
                        return true;

                        //On tab rendering
                        //
                        //When doing column layout, only the text after the last tab gets "column layout". The text before the tab
                        //is layed out as if aligned.
                    case Alignement.Column:
                        var info = h_mertics.GetInfoWithAppend();
#if ALT_SPACE_COLUMN_HANDELING
                        int n_space = info.TotalNrSpace;
#else
                        int n_space = info.NumSpace;
#endif
                        if (n_space > 0)
                        {
                            int start = 0;
#if ALT_SPACE_COLUMN_HANDELING
                            n_space = info.NumSpace;
#endif

                            shift = (paragraph_width == null ? h_mertics.Width : paragraph_width.Value) - (h_mertics.ActualWidth - h_mertics.TrailingWhitespace);
                            if (shift < 0.0000001)
                            {
                                if (!text_renderer.ResetState && text_renderer.State.Tw != text_renderer.ResetState_tw)
                                    text_renderer.SetWordGap(text_renderer.ResetState_tw);

                                h_mertics.RenderLine(text_renderer);
                                return true;
                            }

                            if (text_renderer.State.RightToLeft)
                            {
                                //If not for the differences in how whitespace is handled, rtl rendering
                                //could have been done by simply reversing the whole line and render
                                //as normal.
                                //
                                //One issue with right to left rendering is calculating tab lengths. To
                                //get the correct tab size (when doing rtl) one have to know the full 
                                //length of the line.
                                //
                                //However, with column layout this is a problem since tabs change size
                                //depending on where the word is placed, and we're moving the words around.
                                //
                                //The work around for this is to have the measure function
                                //fill in the tab stops, those measurments are then automatically prefered
                                //by the renderer.

                                if (text_renderer.LsbAdjustText)
                                {
                                    //Note that Preeceding whitespace on a rtl line is on the right of the line,
                                    //not the left. FirstRSB is then technically the LastRSB
                                    if (h_mertics.PreceedingWhitespace == 0)
                                        shift += h_mertics.FirstRSB;

                                    //Similar here, Last LSB is technically FirstLSB
                                    shift += h_mertics.LastLSB;
                                }
                                double adjust_space = shift / n_space / text_renderer.State.Th;

                                //Shifts start to be after the last tabulator on the line
                                int tab_start = start;
                                start = info.Stop + 1;

                                //Aligns the line so that the first character to render is positioned at the
                                //left edge.
                                text_renderer.SetOffset(-(h_mertics.TrailingWhitespace + (text_renderer.LsbAdjustText ? h_mertics.LastLSB : 0)));

                                //Sets the trailing whitespace as the text to be rendered. Note that the trailing
                                //whitespace is at the end of the line.
                                h_mertics.SetRtlText(text_renderer, info.Stop);

                                //Writes out trailing whitespace. Not trully needed, but included for completness sake.
                                if (start < h_mertics.Length)
                                {
                                    if (!text_renderer.ResetState && text_renderer.State.Tw != text_renderer.ResetState_tw)
                                        text_renderer.SetWordGap(text_renderer.ResetState_tw);

                                    text_renderer.SetText(0, h_mertics.Length - start - 1);
                                    h_mertics.RenderLine(text_renderer, start, h_mertics.Length - 1);

                                    //We can assume that "Blocks" only contains underlines, so
                                    //we remove them.
                                    text_renderer.SetBlocks(null);
                                }

                                double current_tw = text_renderer.ResetState ? text_renderer.State.Tw : text_renderer.ResetState_tw;
                                start = info.LastTabIndex + 1;

                                //Writes out line before trailing whitespace and after last tab. This text gets "column layout".
                                if (start <= info.Stop)
                                {
                                    if (n_space != 0 && text_renderer.State.Tw != current_tw + adjust_space)
                                        text_renderer.SetWordGap(current_tw + adjust_space);

                                    if (h_mertics.HasAppend)
                                        text_renderer.SetText(h_mertics.Length + 1 - info.Stop - 1, info.Stop - start);
                                    else
                                        text_renderer.SetText(h_mertics.Length - info.Stop - 1, info.Stop - start);
                                    h_mertics.RenderLine(text_renderer, start, info.Stop);
                                    start = info.Stop + 1;
                                }

                                //Makes the line before tab. This text do not get column layout.
                                if (info.LastTabIndex >= 0)
                                {
                                    if (text_renderer.State.Tw != current_tw)
                                        text_renderer.SetWordGap(current_tw);

                                    //Subtlety: This code will never execute for lines that has 
                                    //          \t at the end. This means that there's no need 
                                    //          to cut off underlines here.
                                    if (h_mertics.HasAppend)
                                        text_renderer.SetText(h_mertics.Length + 1 - info.LastTabIndex - 1, info.LastTabIndex - tab_start);
                                    else
                                        text_renderer.SetText(h_mertics.Length - info.LastTabIndex - 1, info.LastTabIndex - tab_start);
                                    h_mertics.RenderLine(text_renderer, tab_start, info.LastTabIndex);
                                }

                                if (text_renderer.ResetState && text_renderer.State.Tw != current_tw)
                                    text_renderer.SetWordGap(current_tw);

                                //Merges into one line.
                                return true;
                            }
                            else
                            {
                                if (text_renderer.LsbAdjustText)
                                {
                                    //If there's no preceeding whitespace, we position the glyph to start
                                    //exactly at the edge. I.e. we move back with the left side bearings
                                    //of that glyph.
                                    if (h_mertics.PreceedingWhitespace == 0)
                                    {
                                        shift += h_mertics.FirstLSB;

                                        //Set offset is done for the sake of tab calculation. 
                                        text_renderer.SetPending(-h_mertics.FirstLSB);
                                    }

                                    //Shift is the length that the line has to fill out. I.e. how much whitespace
                                    //we must add between the words. We add "LastRSB" since we want to position the
                                    //end glyph just as the edge. Note, trailing whitespace is ignored.
                                    shift += h_mertics.LastRSB;
                                }

                                //The text renderer need to know what text to render. We instruct the line
                                //to set its text into the text renderer
                                h_mertics.SetText(text_renderer, info.Stop);

                                //Makes the line before tab
                                if (info.LastTabIndex >= 0)
                                {
                                    //Subtlety: This code will never execute for lines that has 
                                    //          \t at the end. This means that there's no need 
                                    //          to cut off underlines here.
                                    h_mertics.RenderLine(text_renderer, start, info.LastTabIndex);
                                    start = info.LastTabIndex + 1;
                                }

#if ALT_SPACE_COLUMN_HANDELING
                                double adjust_space = shift / info.TotalNrSpace / state.Th;

                                //Writes out lines between multiple space characters
                                if (info.Spaces != null)
                                {
                                    for (int c = 0; c < info.Spaces.Length; c++)
                                    {
                                        var sp = info.Spaces[c];

                                        if (start < sp.Start - 1)
                                        {
                                            if (n_space != 0 && state.Tw != adjust_space)
                                            {
                                                clines.Add(new Tw_CMD(adjust_space));
                                                state.Tw = adjust_space;
                                            }

                                            cl = h_metrix.RenderLine(state, start, sp.Start - 1);
                                            clines.Add(cl);
                                        }

                                        state.Tw = adjust_space / (sp.Length);
                                        clines.Add(new Tw_CMD(state.Tw));
                                        cl = h_metrix.RenderLine(state, sp.Start, sp.End);
                                        clines.Add(cl);
                                        start = sp.End + 1;
                                        n_space = sp.nSpace;
                                    }
                                }
#else
                                double adjust_space = shift / n_space / text_renderer.State.Th;
#endif

                                double current_tw = text_renderer.ResetState ? text_renderer.State.Tw : text_renderer.ResetState_tw;

                                //Writes out line up to trailing whitespace
                                if (start <= info.Stop)
                                {
                                    if (n_space != 0 && text_renderer.State.Tw != current_tw + adjust_space)
                                        text_renderer.SetWordGap(current_tw + adjust_space);

                                    h_mertics.RenderLine(text_renderer, start, info.Stop);
                                    start = info.Stop + 1;
                                }

                                //Resets the tw paramater
                                if (text_renderer.ResetState)
                                    text_renderer.SetWordGap(current_tw);

                                //Writes out trailing whitespace
                                if (start < h_mertics.Length)
                                {
                                    text_renderer.CloseUnderline();
                                    var block = text_renderer.NumBlocks;
                                    if (text_renderer.IncludeTrailWhite)
                                        h_mertics.RenderLine(text_renderer, start, h_mertics.Length - 1);
                                    //We can assume that "Blocks" only contains underlines, so
                                    //we leave them off.
                                    text_renderer.CloseUnderline();
                                    text_renderer.SetBlocks(block);
                                }

                                return true;
                            }
                        }
                        else
                        {
                            goto case Alignement.WriteDirection;
                        }
                }

                return false;
            }

            #endregion
        }

        /// <summary>
        /// Metrics for a collection of wrapped lines. Contains meta data, such as character range and line numbers
        /// </summary>
        internal class LineMetrics
        {
            #region Variables and properties

            public chLineWrapper Line;

            /// <summary>
            /// Line numbers claimed by this line
            /// </summary>
            public IntRange LineNr;

            /// <summary>
            /// Start character position and end character position
            /// </summary>
            public IntRange Range;

            /// <summary>
            /// True if all lines in the line wrapper has width
            /// </summary>
            public bool AllLinesSet;

            public int LineCount { get { return LineNr.End - LineNr.Start + 1; } }

            #endregion

            #region Init

            public LineMetrics(int start, chLineWrapper line)
            {
                Line = line;
                Range.Start = start;
                Range.End = line.Length + start - 1;
                LineNr.End = -1;
            }

            #endregion

            #region public methods

            public bool contain(int index)
            {
                return Range.Start <= index && index <= Range.End;
            }

            public bool containLine(int index)
            {
                return LineNr.Start <= index && index <= LineNr.End;
            }

            public void Append(PdfImage img, double height)
            {
                Range.End++;
                Line.Append(img, height);
            }

            public void Append(PdfImage img, double width, double height, BlockAlign align)
            {
                Range.End++;
                Line.Append(img, width, height, align);
            }

            public void Append(string str)
            {
                Range.End += str.Length;
                Line.Append(str);
            }

            public IntRange Clip(int start, int end)
            {
                IntRange r;
                //if (start > Range.End || end < Range.Start) throw new IndexOutOfRangeException();
                r.Start = Math.Max(start - Range.Start, 0);
                r.End = Math.Min(Range.End - Range.Start, end - Range.Start);
                return r;
            }

            public override string ToString()
            {
                return string.Format("{0} - {1}", LineNr.Start, LineNr.End);
            }

            #endregion
        }

        #endregion
    }
}
