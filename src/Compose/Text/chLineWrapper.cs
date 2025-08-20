//Single line mode (note: non-single line mode removed)
//This makes this class store its internal data in a single chLine object, instead
//of splitting it over multiple. Layout should as a result be faster, while
//rendering will be a little bit slower as more searching will be done in the
//linked style, word and font lists. 
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf;
using PdfLib.Compile;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// Compose horizontal Line wrapper: A line wrapping class.
    /// 
    /// This class allows for wrapping lines to a wanted length. Each line
    /// must be wrapped induvidually, which allows for the various lines
    /// to have different lengths. It's recommended to use the Paragraph
    /// class instead of using this directly.
    /// </summary>
    /// <remarks>
    /// Tip: Perform styling before generating lines.
    /// 
    /// Future enchanments (historical comment, no longer relevant)
    ///  - There's lots of room for improvment when it comes to the speed
    ///    of line wrapping. The split and join functions could be replaced
    ///    by, say, divide and combine function that don't clone all line
    ///    related data.
    ///    
    ///    Or one could forgo splitting and joining lines altogether. It
    ///    is possible to render only a section of a line, so splitting up
    ///    lines is not actually needed. Note that clients of this class
    ///    do not have direct access to the underlying chLine object, so
    ///    they need not know whenever they truly are broken up or not.
    /// </remarks>
    public class chLineWrapper
    {
        #region Variables and properties

        /// <summary>
        /// This is where the text and styling information in stored
        /// </summary>
        private readonly chLine _line;

        /// <summary>
        /// Character position of the last line
        /// </summary>
        int _last_line_pos = 0;

        /// <summary>
        /// The number of lines in this textbox
        /// </summary>
        public int LineCount { get { return _lines.Count + (_last_line_pos >= _line.Length ? 0 : 1); } }

        /// <summary>
        /// Length of this line.
        /// </summary>
        public int Length { get { return _line.Length; } }

        /// <summary>
        /// The metrics of the induvidual lines
        /// </summary>
        private List<LineMetrics> _lines = new List<LineMetrics>(5);

        /// <summary>
        /// The ascent of the first line of this line
        /// </summary>
        public double Ascent { get { return GetLineAscent(0); } }

        /// <summary>
        /// The true Ascent of the first line of this line
        /// </summary>
        public double YMax { get { return GetLineYMax(0); } }

        /// <summary>
        /// The true Ascent of the first line of this line
        /// </summary>
        public double YMin { get { return GetLineYMin(_lines.Count - 1); } }

        /// <summary>
        /// The descent of the last lone of this wrapped line
        /// </summary>
        public double Descent { get { return GetLineDescent(_lines.Count - 1); } }

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
                if (_lines.Count == 0) return Length == 0;
                var last = _lines[_lines.Count - 1];

                return last.LastCharIndex == Length - 1;
            }
        }

        #endregion

        #region Init

        public chLineWrapper(string text)
            : this(new chLine(text))
        { }

        public chLineWrapper(chLine line)
        {
            _line = line;

        }

        #endregion

        #region Measure text

        /// <summary>
        /// Sets the width of a line.
        /// </summary>
        /// <param name="width">The width</param>
        /// <param name="line_height">Value used for height calculation, null uses the font's line height</param>
        /// <param name="state">Current state</param>
        /// <returns>Height of the line or -1 if there is no such line</returns>
        public LineVMetrics SetLineWidth(double width, double? line_height, cRenderState state)
        {
            return SetLineWidth(width, line_height, state, _last_line_pos, int.MaxValue);
        }

        /// <summary>
        /// Sets the width of a line.
        /// </summary>
        /// <param name="width">The width</param>
        /// <param name="line_height">Value used for height calculation, null uses the font's line height</param>
        /// <param name="state">Current state</param>
        /// <param name="start">Start character index</param>
        /// <param name="end">End character index</param>
        /// <returns>Returns null when there are no more lines to set</returns>
        public LineVMetrics SetLineWidth(double width, double? line_height, cRenderState state, int start, int end)
        {
            end = Math.Min(_line.Length - 1, end);

            if (end == -1)
            {
                //Special casing empty lines.
                if (start == 0)
                {
                    _last_line_pos = 1;
                    var empty_line = new LineMetrics(0, line_height, _line.Measure(0, -1, state, width), _line);
                    _lines.Add(empty_line);
                    return new LineVMetrics(empty_line);
                }
                return null;
            }
            if (start > end)
                return null;
            var w = _line.Measure(start, end, state, width);
            if (w.LastCharIndex < start)
                throw new PdfInternalException();
            var lm = new LineMetrics(start, line_height, w, _line);
            _last_line_pos = w.LastCharIndex + 1;
            _lines.Add(lm);
            lm.Width = width;
            return new LineVMetrics(lm);
        }

        /// <summary>
        /// For when one wish to measure a line again without having the line wrap, 
        /// can cause issues if the line becomes larger than the set width
        /// </summary>
        public LineVMetrics Remeasure(int nr, cRenderState state)
        {
            if (nr >= _lines.Count) return null;
            var lm = _lines[nr];
            var w = _line.Measure(lm.Range.Start, lm.Range.End, state, double.MaxValue);
            lm._lm = w;
            lm.LineHeight = w.Font.LineHeight;
            return new LineVMetrics(lm);
        }

        public LineVMetrics GetVMetrics(int nr)
        {
            if (nr >= _lines.Count) return null;
            return new LineVMetrics(_lines[nr]);
        }

        /// <summary>
        /// Number of lines of the set height, count includes the pos
        /// </summary>
        /// <param name="pos">Position from where to count from</param>
        /// <param name="height">The height to compare against</param>
        /// <param name="delta">Preicision delta</param>
        /// <returns>Number of lines of the given height</returns>
        public int NumLineHeights(int pos, double height, double delta)
        {
            int count = _lines.Count, num = 0;
            for (int c = pos; c < count; c++)
            {
                if (!Util.Real.Same(_lines[c].Height, height, delta))
                    break;
                num++;
            }
            return num;
        }

        #endregion

        #region RenderText

        /// <summary>
        /// Renders a line, without underline on trialing whitespace
        /// </summary>
        /// <param name="nr">Line to render</param>
        /// <param name="text_renderer">Renderstate</param>
        /// <returns>Compiled line</returns>
        public CompiledLine RenderLine(int nr, TextRenderer text_renderer)
        {
            if (!RenderLine(text_renderer, nr))
                return null;

            return text_renderer.Make();
        }

        public bool RenderLine(TextRenderer text_renderer, int nr)
        {
            if (nr >= _lines.Count) return false;
            var lm = _lines[nr];
            text_renderer.Init();
            lm.Render(text_renderer);
            return true;
        }

        public double GetLineHeight(int nr)
        {
            if (nr >= _lines.Count) return 0;
            return _lines[nr].LineHeight;
        }

        public double GetFontHeight(int nr)
        {
            if (nr >= _lines.Count) return 0;
            return _lines[nr].FontHeight;
        }

        /// <summary>
        /// Returns a line's actual width
        /// </summary>
        public double GetLineWidth(int nr)
        {
            if (nr >= _lines.Count) return 0;
            return _lines[nr].ActualWidth;
        }

        /// <summary>
        /// Sets the height of the line. Note that this value is overwritten when
        /// one set the width of a line.
        /// </summary>
        /// <param name="nr">Index of line to set the height of</param>
        /// <param name="height">The new height</param>
        public void SetLineHeight(int nr, double height)
        {
            _lines[nr].LineHeight = height;
        }

        public double GetLineCapHeight(int nr)
        {
            if (nr >= _lines.Count) return 0;
            return _lines[nr].CapHeight;
        }

        /// <summary>
        /// Gets space above the line
        /// </summary>
        /// <param name="nr">Line to fetch data from</param>
        /// <returns>The space aboive the cap height</returns>
        /// <remarks>
        /// Using CapHeight instead of Ascent, as the former is
        /// consistent. Ascent ultimatly depend on the other
        /// characters in the font, so the same Times font can
        /// have different ascents depending on what non-latin
        /// characters the font contains. 
        /// </remarks>
        public double GetSpaceAbove(int nr)
        {
            if (nr >= _lines.Count) return 0;
            var line = _lines[nr];
            return line.FontHeight - line.CapHeight;
        }

        public double GetLineAscent(int nr)
        {
            if (nr >= _lines.Count) return 0;
            return _lines[nr].Ascent;
        }

        public double GetLineYMax(int nr)
        {
            if (nr >= _lines.Count) return 0;
            return _lines[nr].YMax;
        }

        public double GetLineYMin(int nr)
        {
            if (nr >= _lines.Count || nr < 0) return 0;
            return _lines[nr].YMin;
        }

        public double GetLineDescent(int nr)
        {
            if (nr >= _lines.Count || nr < 0) return 0;
            return _lines[nr].Descent;
        }

        public LineHMetrics GetLine(int nr)
        {
            if (nr < 0 || nr >= _lines.Count) return null;
            return new LineHMetrics(_lines[nr]);
        }

        /// <summary>
        /// Start character position and end character position
        /// </summary>
        public xIntRange GetRange(int nr)
        {
            if (nr < 0 || nr >= _lines.Count) return new xIntRange();
            return _lines[nr].Range.xRange;
        }

        #endregion

        #region Text

        /// <summary>
        /// Appends text to the last line
        /// </summary>
        public void Append(string text)
        {
            _line.Append(text);
        }

        /// <summary>
        /// Appends image to the last line
        /// </summary>
        public void Append(PdfImage img, double height)
        {
            _line.Append(img, height);
        }

        public void Append(PdfImage img, double width, double height, BlockAlign align)
        {
            _line.Append(img, width, height, align);
        }

        public string GetText(int start, int end)
        {
            return _line.GetText(start, end);
        }

        #endregion

        #region Styling

        /// <summary>
        /// Sets a section of text as underlined.
        /// </summary>
        public void SetUnderline(int start, int end, double? underline)
        {
            _line.SetUnderline(start, end, underline);
        }

        /// <summary>
        /// Sets font on a section of text
        /// </summary>
        public void SetFont(int start, int end, cFont font, double? size)
        {
            _line.SetFont(start, end, font, size);
            ResetLayout();
        }

        /// <summary>
        /// Sets color on a section of text
        /// </summary>
        public void SetColor(int start, int end, cColor fill, cColor stroke)
        {
            _line.SetColor(start, end, fill, stroke);
        }

        /// <summary>
        /// Sets color on a section of text
        /// </summary>
        public void SetRenderMode(int start, int end, xTextRenderMode? mode)
        {
            _line.SetRenderMode(start, end, mode);
        }

        public void InsertImg(int pos, PdfImage img)
        {
            _line.Insert(pos, img, null, null, null);
            ResetLayout();
        }

        public void InsertImg(int pos, PdfImage img, double height)
        {
            _line.Insert(pos, img, null, height, null);
            ResetLayout();
        }

        public void InsertImg(int pos, PdfImage img, double height, double width)
        {
            _line.Insert(pos, img, width, height, null);
            ResetLayout();
        }

        public void InsertImg(int pos, PdfImage img, double? width, double? height, BlockAlign? align)
        {
            _line.Insert(pos, img, width, height, align);
            ResetLayout();
        }

        #endregion

        #region Layout

        /// <summary>
        /// Sets back the line to how it was before being layed out
        /// </summary>
        public void ResetLayout()
        {
            _lines.Clear();
            _last_line_pos = 0;
        }

        #endregion

        #region Indexing

        private int GetLineIndex(int pos)
        {
            var count = _lines.Count;
            for (int c = 0; c < count; c++)
                if (_lines[c].contain(pos))
                    return c;
            throw new IndexOutOfRangeException();
        }

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
            
            var count = _lines.Count;
            for (int c = 0; c < count; c++)
            {
                var line = _lines[c];
                if (line.contain(start))
                {
                    r.Start = c;
                    while (!line.contain(end))
                    {
                        c++;
                        if (c == count)
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

        #endregion

        #region Helper classes

        /// <summary>
        /// Intended for when measuring height
        /// </summary>
        public class LineVMetrics
        {
            private LineMetrics _lm;

            /// <summary>
            /// Used to store how many characters this line is to count for. This
            /// number includes line endings.
            /// </summary>
            /// <remarks>
            /// Note, +1 is added for \n even when stopping in the middle of a line. This works
            /// since when one stop in the middle of a line, it's becase one don't want to continue.</remarks>
            internal int CharCount;

            /// <summary>
            /// Either from the font or manualy set using "setLineHeight"
            /// </summary>
            public double Height 
            { 
                get { return _lm.Height; } 
                set { _lm.Height = value; } 
            }

            /// <summary>
            /// Height of the line, as set by the tallest font
            /// </summary>
            public double FontHeight { get { return _lm.FontHeight; } }
            public double CapHeight { get { return _lm.CapHeight; } }
            public double Ascent { get { return _lm.Ascent; } }
            public double Descent { get { return _lm.Descent; } }
            public double YMax { get { return _lm.YMax; } }
            public double YMin { get { return _lm.YMin; } }
            public double ContentWidth { get { return _lm.ActualWidth; } }

            /// <summary>
            /// Index of the last measured character on the underlying whole line, that this
            /// measurment is valid for.
            /// </summary>
            public int LastCharIndex { get { return _lm._lm.LastCharIndex; } }

            internal LineVMetrics(LineMetrics line)
            { _lm = line; }

            public double GetContentWidth(bool strip_sidebearings, bool rtl)
            {
                return strip_sidebearings ? _lm.ActualWidth - (rtl ? _lm.FirstRSB + _lm.LastLSB : _lm.FirstLSB + _lm.LastRSB) : _lm.ActualWidth;
            }
        }

        /// <summary>
        /// Horizotal line metrics
        /// </summary>
        public class LineHMetrics
        {
            private LineMetrics _line;
            public double Width { get { return _line.Width; } }
            public double ActualWidth { get { return _line.ActualWidth; } }
            public double LTRTrailWhitespace { get { return _line.TrailingWhitespace + _line.LastRSB; } }
            public double RTLTrailWhitespace { get { return _line.PreceedingWhitespace + _line.FirstRSB; } }
            public double RTLPrecWhitespace { get { return _line.TrailingWhitespace + _line.LastLSB; } }
            public double FirstRSB { get { return _line.FirstRSB; } }
            public double FirstLSB { get { return _line.FirstLSB; } }
            public double LastRSB { get { return _line.LastRSB; } }
            public double LastLSB { get { return _line.LastLSB; } }
            public double TrailingWhitespace { get { return _line.TrailingWhitespace; } }
            public double PreceedingWhitespace { get { return _line.PreceedingWhitespace; } }
            public int Length { get { return _line.Range.End - _line.Range.Start + 1; } }
            public int LastWordIndex { get { return _line.LastCharIndex; } }
            public string Text { get { return _line.Line.Text; } }
            internal bool HasAppend { get { return _line._lm.AppendChar != null; } }

            //public chLine.TWInfo GetInfo()
            //{
            //    var info = _line.Line.GetInfo(_line.Range.Start, _line.Range.End);
            //    return new chLine.TWInfo(info.LastTabIndex != -1 ? info.LastTabIndex - _line.Range.Start : -1, info.Stop - _line.Range.Start, info.NumSpace);
            //}

            internal chLine.TWInfo GetInfoWithAppend()
            {
                var info = _line.Line.GetInfo(_line.Range.Start, _line.Range.End);
                if (_line._lm.AppendChar != null)
                    return new chLine.TWInfo(info.LastTabIndex != -1 ? info.LastTabIndex - _line.Range.Start : -1, _line.Range.End + 1 - _line.Range.Start, info.NumSpace);
                else
                    return new chLine.TWInfo(info.LastTabIndex != -1 ? info.LastTabIndex - _line.Range.Start : -1, info.Stop - _line.Range.Start, info.NumSpace);
            }

            public void SetText(TextRenderer tr)
            {
                var txt = _line.Line.Text;
                tr.SetText(txt, txt.Length - 1);
            }

            public void SetRtlText(TextRenderer tr)
            {
                int start = _line.Range.Start, length = _line.Range.End - start;
                var txt = _line.Line.Text.Substring(start, length + 1);
                txt = chLine.ReverseGraphemeClusters(txt);
                tr.SetText(txt, length);
            }

            /// <summary>
            /// Sets the text with the AppenChar character appended.
            /// </summary>
            internal void SetText(TextRenderer tr, int end)
            {
                var txt = _line.Line.Text;

                if (_line._lm.AppendChar != null)
                    tr.SetText(txt.Substring(0, _line.Range.Start + end) + _line._lm.AppendChar, _line.Range.Start + end);
                else
                    tr.SetText(txt, txt.Length - 1);
            }

            /// <summary>
            /// Sets the text with the AppenChar character appended.
            /// </summary>
            internal void SetRtlText(TextRenderer tr, int end)
            {
                int start = _line.Range.Start, length = _line.Range.End - start;
                var txt = _line.Line.Text.Substring(start, length + 1);
                if (_line._lm.AppendChar != null)
                {
                    length++;
                    txt = chLine.ReverseGraphemeClusters(txt + _line._lm.AppendChar);
                }
                else
                    txt = chLine.ReverseGraphemeClusters(txt);
                tr.SetText(txt, length);
            }

            /// <summary>
            /// Renders a line and crops off underlines
            /// </summary>
            /// <param name="tr">Current state</param>
            /// <returns>Rendered line</returns>
            internal void RenderLine(TextRenderer tr)
            {
                _line.Render(tr);
            }

            /// <summary>
            /// Renders a line, but does not crop off underlines
            /// </summary>
            /// <param name="state">Current state</param>
            /// <returns>Rendered line</returns>
            internal void RenderLine(TextRenderer tr, int start, int stop)
            {
                _line.Line.RenderImpl(tr, start + _line.Range.Start, stop + _line.Range.Start);
            }

            internal LineHMetrics(LineMetrics line)
            { _line = line; }
        }

        internal class LineMetrics
        {
            public chLine Line;

            /// <summary>
            /// Line measuremeants
            /// </summary>
            internal LineMeasure _lm;

            /// <summary>
            /// Width is the set width of the line
            /// </summary>
            /// <remarks>
            /// It's perfectly possible that one wish to create a document structure where each line has its unique width,
            /// opposed to using the paragraph's width.</remarks>
            public double Width;

            /// <summary>
            /// The true width of the line
            /// </summary>
            public double ActualWidth { get { return _lm.Width; } }

            /// <summary>
            /// Whitespace after the last character
            /// </summary>
            public double TrailingWhitespace { get { return _lm.Trailing; } }

            /// <summary>
            /// Whitespace before the first character
            /// </summary>
            public double PreceedingWhitespace { get { return _lm.Preceeding; } }

            /// <summary>
            /// Right side bearing of the last character
            /// </summary>
            public double LastRSB { get { return _lm.LastRSB; } }

            /// <summary>
            /// Left side bearing of the last character
            /// </summary>
            public double LastLSB { get { return _lm.LastLSB; } }

            /// <summary>
            /// Right side bearing of the first character
            /// </summary>
            public double FirstRSB { get { return _lm.FirstRSB; } }

            /// <summary>
            /// Left side bearing of the first character
            /// </summary>
            public double FirstLSB { get { return _lm.FirstLSB; } }

            /// <summary>
            /// Line height, as set in the range 0-1 = 0 -> 100%
            /// 
            /// This value can come from the font, the paragraph or the document
            /// </summary>
            public double LineHeight;

            /// <summary>
            /// The actual height of the line. Typically, this is FontHeight but
            /// it can be YMax and also manually set using "setLineHeight"
            /// </summary>
            public double Height;

            /// <summary>
            /// Height of the line, as set by the tallest font
            /// </summary>
            public double FontHeight { get { return LineHeight * _lm.Font.Size; } }

            /// <summary>
            /// Textrise of the tallest font
            /// </summary>
            public double TextRise { get { return _lm.Font.TextRise; } }

            /// <summary>
            /// Line metrics from the font.
            /// </summary>
            public double CapHeight { get { return _lm.Font.CapHeight * _lm.Font.Size; } }
            public double Ascent { get { return _lm.Font.LineAscent * _lm.Font.Size; } }
            public double Descent { get { return _lm.Font.LineDescent * _lm.Font.Size; } }

            /// <summary>
            /// The true LineAscent / Descent
            /// </summary>
            public double YMax { get { return _lm.YMax; } }
            public double YMin { get { return _lm.YMin; } }

            public int LastCharIndex
            {
                get { return Line.GetLastCharIndex(Range.Start, Range.End); }
            }

            public int Length { get { return Range.Length; } }

            /// <summary>
            /// Start character position and end character position
            /// </summary>
            public IntRange Range;

            public LineMetrics(int start, double? lh, LineMeasure lm, chLine line)
            {
                Range.End = lm.LastCharIndex;
                _lm = lm;
                LineHeight = lh == null ? lm.Font.LineHeight : lh.Value;
                Line = line;
                Range.Start = start;
                Height = Math.Max(FontHeight, YMax);
            }

            public bool contain(int index)
            {
                return Range.Start <= index && index <= Range.End;
            }

            public IntRange Clip(int start, int end)
            {
                IntRange r;
                //if (start > Range.End || end < Range.Start) throw new IndexOutOfRangeException();
                r.Start = Math.Max(start - Range.Start, Range.Start);
                r.End = Math.Min(Range.End - Range.Start, end);
                return r;
            }

            public void Render(TextRenderer text_render)
            {
                int l = LastCharIndex, start = Range.Start, end = Range.End;

                if (l == -1 || l == end)
                {
                    if (_lm.AppendChar != null)
                        Line.Render(text_render, start, end, _lm.AppendChar.Value);
                    else
                        Line.Render(text_render, start, end);
                }
                else
                    Line.Render(text_render, start, l, end);
            }

            public override string ToString()
            {
                if (_lm.AppendChar != null)
                    return Line.Text.Substring(Range.Start, Range.End - Range.Start + 1) + _lm.AppendChar.Value;
                return Line.Text.Substring(Range.Start, Range.End - Range.Start + 1);
            }
        }

        #endregion
    }
}
