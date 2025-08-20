using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Compose.Layout.Internal;
using PdfLib.Compose.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using System.Diagnostics;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// For wordwrapping text layout.
    /// </summary>
    /// <remarks>
    /// This class uses the styling information from the parent. 
    /// 
    /// Layout for this class don't follow a straight box model,
    /// because of this one have to code explicit support for 
    /// this class into container classes.
    /// 
    /// The goal is to have text work samey to html. Just like html you cannot style
    /// nor position text node directly. This has to be done on the parent.
    /// Keypoints: cText will not internally support word wrapping, like done
    /// with cTextArea. External support will be required for this. 
    /// 
    /// Future improvments:
    ///  - Whitespace handeling, normal, pre, etc, be like html.
    ///     - What to do with whitespace after lines? Investigate what
    ///       html does. I suspect it ignores it.
    ///  
    /// Bugs:
    ///  - Text is not currently alligned properly on the line. This is very noticable
    ///    when text with different font sizes sits on the same line.
    ///     - I suspect the bug stems from not adjusting with the "biggest decent" on a line.
    ///       I.e. once a line has all it's blocks laid out, another pass has to be made to 
    ///       find the biggest decent, then use that decent in all cText and cTextArea objects. 
    ///       (Potentially also chTextBox, but they live in their own world so maybe not.)
    ///       (This naturlally has to be implemented for all alignment modes)
    ///  - Only "Descent" alignment is implemented. See "SetPositions()"
    ///  - Tab handeling.
    ///     If whitespace pre is to be supported, tab handeling has not been given much thought. I don't know
    ///     how browsers do it. My impl. will calculate tab positions from the start of a text box, not
    ///     the start of a document nor div. It is certainly possible to adjust this behavior by adding a tab 
    ///     origin paramter to "TextRenderer" (and the AddTab function then needs to add that value to line_length). 
    ///     
    /// Ideas for Italic / Bold emulation
    ///  - Wait until the new system for caching fonts / glyps is implemented.
    ///  - Add a field on the appropritiate place that flags a font for needing bold or italic emulation.
    ///  - Bold can be emulated through using text rendering outline. Since that feature isn't supported by
    ///    cText, it won't cause any trouble. chTextBox may have more trouble, but perhaps disable text rendering
    ///    mode for such fonts, or do some "add togetehr text mode settings" magic. 
    ///  - Italic might be trickier, but should be possible to emulate using text matrice. Since the matrice is
    ///    also used to move the text, it must be possible to set it back to what it would have been without italic.
    ///    I don't think q/Q can be used during text mode, but the text mertric can fortunatly be fully set so it
    ///    shouldn't be a problem.
    /// </remarks>
    public class cText : cNode
    {
        #region Variables and properties

        /// <summary>
        /// Text with all whitespace intact
        /// </summary>
        private readonly string _original_text;

        /// <summary>
        /// Using a paragraph instead of chLineWrapper, since it supports text alignement.
        /// </summary>
        private chParagraph _par;
        private chParagraph.ParMetrics _metrics;
        private cRenderState _org_state = new cRenderState() { SimpleBreakWord = true };

        /// <summary>
        /// Tracks whenever this paragraph has a layout already
        /// </summary>
        private bool _has_layout;
        private double _max_space_on_first_line;
        private List<LineInfo> _lines = new List<LineInfo>(1);
        internal class LineInfo
        {
            public readonly chLineWrapper.LineVMetrics Line;
            public xPoint Pos;

            /// <summary>
            /// Position from the bottom of the "VisWrapLine" to the bottom of the box
            /// </summary>
            public double BottomY, OffsetY;
            public LineInfo(chLineWrapper.LineVMetrics line) { Line = line; }
        }
        internal enum FlowResult
        {
            /// <summary>
            /// Everything fit in the given space
            /// </summary>
            OK,

            /// <summary>
            /// Some text fit in the avalible space, 
            /// but some needs to be put on the next line
            /// </summary>
            OVERFLOW,

            /// <summary>
            /// No text fit in the avalible space
            /// </summary>
            NONE
        }

        public override bool IsBlock => false;

        /// <summary>
        /// Number of lines in this box.
        /// </summary>
        internal int Count { get { return _lines.Count; } }

        /// <summary>
        /// Flags that used by the positioning code, to see if the line that
        /// follows a text box is to be positioned after said text box.
        /// </summary>
        internal bool Attached = false;

        #region Position

        /// <summary>
        /// Box no longer considers itself layed out
        /// </summary>
        internal override void InvalidateLayout()
        {
            _has_layout = false;
            _lines.Clear();
            _metrics.ResetLayout();
        }

        #endregion

        #region State

        internal override bool HasListeners => false;

        internal override bool HasLayout => _has_layout;

        /// <returns>True if style has changed</returns>
        internal override bool AddListeners()
        {
            return false;
        }

        internal override void RemoveListeners()
        {

        }

        #endregion

        #endregion

        #region Visual variables and properties

        internal override bool TakesUpSpace => true;
        internal override double VisFullWidth => VisWidth;
        internal override double VisFullHeight => VisHeight;
        internal override double VisMinFullWidth => VisWidth;
        internal override double VisMinFullHeight => VisHeight;

        internal override bool SizeDependsOnParent => true;

        #endregion

        #region Init

        public cText(string text)
        {
            this._original_text = text ?? "";
            _par = new chParagraph(FilterText());
            _metrics = _par.CreateMetrics();
        }

        #endregion

        #region Layout

        internal double GetLineHeight(int line_nr)
        {
            return _lines[line_nr].Line.Height;
        }

        internal double GetLastLineWidth()
        {
            return _lines[_lines.Count - 1].Line.ContentWidth;
        }

        internal void SetLastLineHeight(double height)
        {
            _lines[_lines.Count - 1].Line.Height = height;
        }

        internal double GetLastLineHeight()
        {
            return _lines[_lines.Count - 1].Line.Height;
        }

        internal void SetPosition(int line_nr, double x_pos, double y_pos, double bottom_y)
        {
            Debug.Assert(_has_layout);
            var li = _lines[line_nr];
            li.Pos = new xPoint(x_pos, y_pos);
            li.BottomY = bottom_y;
        }

        internal void SetTextPosition()
        {
            switch (Parent.Style.TextAlignement)
            {
                case eTextAlignement.Descent:
                    foreach (LineInfo li in _lines)
                        li.OffsetY = -li.Line.Descent;
                    break;
            }
        }

        /// <summary>
        /// Lays of the text if there has been any changes
        /// </summary>
        /// <param name="remaning_space">Space remanining for the line layout</param>
        /// <param name="force_layout">Force the text to be layed out even if there's not enough space</param>
        /// <returns>If there is overflow</returns>
        internal FlowResult LayoutIfChanged(double remaning_space, bool force_layout)
        {
            if (_has_layout && (remaning_space != _max_space_on_first_line || Parent._my_size_count != _parent_size_count))
                InvalidateLayout();

            _max_space_on_first_line = remaning_space;
            
            if (!_has_layout)
            {
                //Gets style information from the parent
                var s = Parent.Style;
                _org_state.Tf = s.Font ?? cFont.Times;
                _org_state.Tfs = SetSize(s.FontSize);
                _org_state.CharacterSpacing = s.TextSpacing.CharacterSpacing;
                _org_state.HorizontalScaling = s.TextSpacing.HorizontalScale;
                _org_state.WordSpacing = s.TextSpacing.WordSpacing;
                _org_state.TextRise = s.TextSpacing.TextRise;

                //Todo: This is not the right spot for this. It needs to be done
                //      as part of the rendering phase
                _org_state.FillColor = s.Color;

                var state = _org_state.Copy();

                //Measures the text
                var lm = _metrics.SetLineWidth(remaning_space, s.LineHeight, state);

                //If the text size exceeds the avalible space, we return a none to
                //inform the caller that there's not enough room for the text
                if (!force_layout && lm.ContentWidth > remaning_space)
                {
                    //Got to remove the _metrics data that was created by the failed
                    //measure attempt.
                    _metrics.ResetLayout();

                    return FlowResult.NONE;
                }

                //This function does quite a bit, look at the LineInfo constructor
                _lines.Add(new LineInfo(lm));

                //Updating the size
                VisWidth = lm.ContentWidth;
                VisHeight = lm.Height;

                //Used to avoid having to do layout when it isn't needed.
                _has_layout = true;
                _parent_size_count = Parent._my_size_count;
                //No need to update _my_size_count, as text nodes has no children. 

                //Checks if there's more text on the line, if so returns the overflow
                return _metrics.AllLinesSet ? FlowResult.OK : FlowResult.OVERFLOW;
            }

            return FlowResult.OK;
        }

        /// <summary>
        /// For flowing text onto a new line
        /// </summary>
        /// <param name="width">Amount of space</param>
        /// <returns>Size of the new line</returns>
        internal xSize? SetNextLineWidth(double width)
        {
            var state = _org_state.Copy();
            var lm = _metrics.SetLineWidth(width, Parent.Style.LineHeight, state);
            if (lm != null)
            {
                _lines.Add(new LineInfo(lm));

                //The text box itself should not be increesed in size, as that confuses the positioning code.
                //VisWidth = Math.Max(VisWidth, lm.ContentWidth);
                //VisHeight += lm.Height;
                
                return new xSize(lm.ContentWidth, lm.Height);
            }

            return null;
        }

        #endregion

        #region Render

        internal void Render(double move_x, double move_y, IDraw draw, ref cRenderState state)
        {
            if (state.GS != GS.Page)
                throw new NotSupportedException("State must be page");

            if (!_has_layout)
                throw new NotSupportedException("Layout must be run before text rendering");

            draw.Save();
            state.Save();

            state.BeginText(draw);

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

            RenderText(move_x, move_y, draw, ref state);
            state.EndText(draw);

            draw.Restore();
            state = state.Restore();
        }

        private void RenderText(double move_x, double move_y, IDraw draw, ref cRenderState state)
        {
            var tr = new TextRenderer(state, draw);
            var compiled_lines = new List<cBlock[]>(_metrics.LineCount);

            int line_nr = 0;
            double last_mx = 0, last_my = 0;
            foreach (LineInfo line in _lines)
            {
                double mx = move_x + line.Pos.X;
                double my = move_y - line.BottomY + line.OffsetY;// + line.BottomY;

                if (mx != 0 || my != 0)
                {
                    if (line_nr > 0)
                    {
                        //Need to recalc mx, my from the current loc.
                        double dx = last_mx - mx, dy = last_my - my;
                        state.TranslateTLM(-dx, dy);
                        draw.TranslateTLM(-dx, dy);
                    }
                    else
                    {
                        state.TranslateTLM(mx, my);
                        draw.TranslateTLM(mx, my);
                    }

                    last_mx = mx;
                    last_my = my;
                }

                //Renders
                if (!_metrics.RenderLine(line_nr++, tr))
                    break;

                //Draws
                var blocks = tr.MakeBlocks();
                if (blocks != null)
                    compiled_lines.Add(blocks);
            }
        }

        #endregion

        #region Text

        internal override bool ContainsText => true;

        internal override void AddStats(cTextStat stats)
        {
            var s = Parent.Style;
            stats.AddColor(s.Color);
            stats.AddFont(s.Font, s.FontSize);
        }

        /// <summary>
        /// Removes whitespace
        /// </summary>
        /// <returns></returns>
        private string FilterText()
        {
            return string.Join<string>(" ", _original_text.Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        public override string ToString() { return _original_text; }

        #endregion
    }
}
