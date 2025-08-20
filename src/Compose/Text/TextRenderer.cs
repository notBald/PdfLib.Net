//If set then a space character will be output for each \t, however this
//is of questionable value as Adobe is smart enough to convert empty space
//into, well, space.
//#define TAB_TO_SPACE
//#define LOOP_STYLES //Must also be set in chLine.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf;
using PdfLib.Render.Commands;
using PdfLib.Render.PDF;
using PdfLib.Render;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// The text renderer is a state machine for that outputs PDF drawing
    /// commands.
    /// </summary>
    public class TextRenderer
    {
        #region Variables

        const int WIDTH = 0;
        const int KERN = 1;

        /// <summary>
        /// The state object
        /// </summary>
        internal cRenderState OriginalState;
        private cRenderState _state;

        /// <summary>
        /// If measurements are to be kerned
        /// </summary>
        private bool _kern;

        /// <summary>
        /// Rendertarget
        /// </summary>
        private IDraw _draw;

        /// <summary>
        /// The string being rendered
        /// </summary>
        internal string _str;

        /// <summary>
        /// Blocks must be rendered after the commands
        /// </summary>
        internal List<cBlock> Blocks = new List<cBlock>();

        /// <summary>
        /// The string being rendered
        /// </summary>
        public string Str { get { return _str; } }

        /// <summary>
        /// Whenever a string is being built
        /// </summary>
        public bool Open { get { return _open; } }

        /// <summary>
        /// The total length of the string being rendered
        /// </summary>
        public double TotalLength { get { return _total_length; } }

        /// <summary>
        /// The number of blocks
        /// </summary>
        public int NumBlocks { get { return Blocks.Count; } }

        /// <summary>
        /// Non-position related state parameters will be reset back to what
        /// they were.
        /// </summary>
        public bool ResetState = true;

        /// <summary>
        /// Used when rest state is false
        /// </summary>
        public double ResetState_tw = 0;

        /// <summary>
        /// Trailing whitespace is included in text output
        /// </summary>
        public bool IncludeTrailWhite = false;

        /// <summary>
        /// LeftSideBearing adjust text.
        /// 
        /// Text will be adjusted to start exactly
        /// where the drawing starts. 
        /// </summary>
        public bool LsbAdjustText = false;

        /// <summary>
        /// Whenever a string is being built
        /// </summary>
        private bool _open;

        /// <summary>
        /// Whenever a underline is being built
        /// </summary>
        private bool _underline_open;

        /// <summary>
        /// The total length of the string being rendered
        /// </summary>
        private double _total_length;

        /// <summary>
        /// Length of the current underline being built
        /// </summary>
        private double _underline_min_length, _underline_width,
            _underline_YMax, _underline_current_YMax,
            _underline_font_size, _underline_start_pos;
        private cFont _underline_font;
        private cBrush _underline_color;

#if TAB_TO_SPACE

        /// <summary>
        /// Flag that hints that a tab character exist in the string
        /// </summary>
        private bool HasTabulator;
#endif

        /// <summary>
        /// For when there's a need to adjust keerning
        /// </summary>
        private List<PdfItem> _string_array;

        /// <summary>
        /// Start of a string
        /// </summary>
        internal int Start;

        /// <summary>
        /// End of a string
        /// </summary>
        internal int End;

        /// <summary>
        /// Where the string ends for rendering
        /// </summary>
        private int _offset, _str_end;

        /// <summary>
        /// This is a distance we must move before applying any text
        /// </summary>
        /// <remarks>This distance is in relation to the current font</remarks>
        private double _must_move;

        /// <summary>
        /// Horizontal offset. Used to shift the position of the line without
        /// affecting tab positions.
        /// </summary>
        private double _hrz_offset;

        /// <summary>
        /// Offset that is to be commited after the next font change.
        /// </summary>
        private double _pending_offset;

        #endregion

        #region Current state variables

        #endregion

        #region Properties

        public cRenderState State { get { return _state; } }

        /// <summary>
        /// The current string chunk waiting to be closed
        /// </summary>
        public string TextChunk
        { get { return _str == null ? "" : (_str.Length > _offset && _open) ? _str.Substring(Start + _offset, End - Start + 1) : ""; } }

        public string Text
        { get { return _str == null ? "" : _str.Substring(_offset, _str_end + 1); } }

        public cFont Font
        {
            get { return _state.Tf; }
        }

        public double FontSize
        {
            get { return _state.Tfs; }
        }

        public void SetFont(cFont font, double size)
        {
            Debug.Assert(font != null);
            if (!ReferenceEquals(_state.Tf, font) || _state.Tfs != size)
            {
                Append();
                Move(); //<-- Todo: Add into "Close()"
                Close();
                _state.Tf = font;
                _state.Tfs = size;
                _draw.SetFont(font, size);
            }
            if (_pending_offset != 0)
            {
                SetOffset(_pending_offset);
                _pending_offset = 0;
            }
        }

        public void SetRise(double rise)
        {
            if (_state.Tr != rise)
            {
                Append();
                Move(); //<-- Todo: Add into "Close()"
                Close();
                _state.Tr = rise;
                _draw.SetTextRise(rise);
            }
            if (_pending_offset != 0)
            {
                SetOffset(_pending_offset);
                _pending_offset = 0;
            }
        }

        #endregion

        #region Init and cleanup

        public TextRenderer(cRenderState state)
        {
            _draw = new DrawCMD(new List<RenderCMD>(10));
            _string_array = new List<PdfItem>(10);
            OriginalState = state.Copy();
            _state = state;
            _kern = state.Kern;

            //Needed for correct tabulator size
            _hrz_offset = -(_state.Tm.OffsetX - _state.Tlm.OffsetX);
        }

        public TextRenderer(cRenderState state, IDraw draw)
        {
            _draw = draw;
            _string_array = new List<PdfItem>(10);
            OriginalState = state.Copy();
            _state = state;
            _kern = state.Kern;

            //Needed for correct tabulator size
            _hrz_offset = -(_state.Tm.OffsetX - _state.Tlm.OffsetX);
        }

        /// <summary>
        /// Prepares the text renderer for a new line. 
        /// </summary>
        internal void Init()
        {
            _total_length = 0;
            _hrz_offset = -(_state.Tm.OffsetX - _state.Tlm.OffsetX);
            _kern = State.Kern;
        }

        /// <summary>
        /// Sets Tw state
        /// </summary>
        /// <param name="gap">Distance between words</param>
        internal void SetWordGap(double gap)
        {
            InjectCMD(new Tw_CMD(gap));
            State.Tw = gap;
        }

        /// <summary>
        /// Sets the text to render
        /// </summary>
        /// <param name="str">Text</param>
        /// <param name="start">Offset</param>
        /// <param name="end">End of the string (Usualy length - 1)</param>
        internal void SetText(string str, int end)
        {
            _str = str;
            _str_end = end;
            _offset = 0;

            //Note that _str_end need not be entierly correct, it's only
            //used to set the color state and debugging aid. Setting it
            //to _str.Length - 1 is always "correct" enough.
            Debug.Assert(_str_end < _str.Length);
        }

        /// <summary>
        /// Sets the text to render
        /// </summary>
        /// <param name="start">Offset</param>
        /// <param name="length">Length of the string</param>
        internal void SetText(int start, int length)
        {
            _str_end = length;
            _offset = start;
            if (_open)
            {
                Start -= _offset;
                End -= _offset;
            }
            Debug.Assert(_str_end < _str.Length && start >= 0);
        }

        /// <summary>
        /// Warning: Don't call this method more than once.
        /// </summary>
        internal void Flush()
        {
            Move();
            Close();
            CloseUnderline();

            //Updates the TextMetrix
            _state.Tm = _state.Tm.TranslatePrepend(_total_length, 0);
        }

        public CompiledLine Make()
        {
            Flush();
            if (!(_draw is DrawCMD))
                throw new NotSupportedException();
            var ar = ((DrawCMD)_draw).ToArrayAndClear();
            var c = new CompiledLine(ar.Length > 0 ? ar : null, Blocks.Count > 0 ? Blocks.ToArray() : null);
            Blocks.Clear();
            return c;
        }

        public cBlock[] MakeBlocks()
        {
            Flush();
            var b = Blocks.Count > 0 ? Blocks.ToArray() : null;
            Blocks.Clear();
            return b;
        }

        /// <summary>
        /// Returns the current set of blocks, and removes them from this renderer
        /// </summary>
        internal cBlock[] FetchBlocks()
        {
            if (Blocks.Count == 0)
                return null;
            var r = Blocks.ToArray();
            Blocks.Clear();
            return r;
        }

        internal void SetBlocks(cBlock[] blocks)
        {
            Blocks.Clear();
            if (blocks != null)
                Blocks.AddRange(blocks);
        }

        internal void SetBlocks(int num)
        {
            Blocks.RemoveRange(num, Blocks.Count - num);
        }

        #endregion

        #region Text rendering related

        internal void InjectCMD(RenderCMD cmd)
        {
            Close();
            cmd.Execute(_draw);
        }

        internal void Add(int pos, double glyph_width)
        {
            Add(pos);
            _total_length += glyph_width;
            _underline_min_length = glyph_width;
        }

        /// <summary>
        /// Enlarges the current string.
        /// </summary>
        internal void Add(int pos)
        {
            End = pos;
            if (!_open)
            {
                Start = pos;
                _open = true;
                Move();
            }
        }

        /// <summary>
        /// Adds an image or PdfForm
        /// </summary>
        internal void Add(BlockItem block, double width, double true_width, double height, double y_pos)
        {
            //This positions the block from the origin point of the line.
            var cb = new cBlock(_state.Tm, 4);
            cb.Commands[0] = new q_RND();
            cb.Commands[1] = new cm_RND(new xMatrix(width, 0, 0, height, _total_length, y_pos));
            if (block.Item is PdfImage)
                cb.Commands[2] = new Do_CMD((PdfImage)block.Item);
            else
                throw new NotImplementedException("Can only add compiled forms right now. Should be an easy fix, just look at the code for adding uncompiled patterns");
            Blocks.Add(cb);
            cb.Commands[3] = new Q_RND();

            _underline_min_length = true_width;
            _total_length += true_width;
        }

        internal void AddSpace(int start, int end, ref ColorRange style)
        {
            //We assume that all space characters are the same, 
            //so we only measure the first one.
            double width;
            if (_state.Tf.Precision == 0)
                width = Math.Truncate(_state.Tf.GetGlyphMetrix(_str[start + _offset]).Width * 1000) / 1000d;
            else
            {
                width = Math.Pow(10, _state.Tf.Precision) * 1000;
                width = Math.Truncate((_state.Tf.GetGlyphMetrix(_str[start + _offset]).Width * width)) / width;
            }
            width = (width * _state.Tfs + _state.Tw + _state.Tc) * _state.Th;

            //Adds the widths one by one.
            while (start <= end)
            {
#if !LOOP_STYLES
                style = chLine.GetStyleRange(OriginalState, style, start, _str_end);
                SetState(style);
#endif
                Add(start++, width);
            }
        }

        internal void AddTab(int start, int end, ref ColorRange style, int pos, double[] tab_lengths)
        {
            _underline_min_length = 0;
            double start_length = _total_length, tab_length, length;
#if TAB_TO_SPACE
            HasTabulator = true;

            //While it is possible to output a tab character it does not work well
            //with Adobe's copy&paste (at least when the encoding is ANSI). So instead
            //we output space characters as a work around.
            //
            //We assume that all space characters are the same (since all these tabs is for the, 
            //same font).
            var space_width = (_font.GetGlyphMetrix(' ').Width * _org_state.Tfs + _org_state.Tw + _org_state.Tc) * _org_state.Th;
            double space_length = 0;
#endif

            //Using a simple scheme to find the length of the tab. I.e. adding
            //together tab stops until we get beyond the current total length.
            var line_length = _total_length -_hrz_offset;
            if (tab_lengths != null)
            {
                length = line_length;
                while (start <= end)
                {
                    length += tab_lengths[pos++];
#if !LOOP_STYLES
                    style = chLine.GetStyleRange(OriginalState, style, start, _str_end);
                    SetState(style);
#endif
                    line_length = length;
#if TAB_TO_SPACE
                    Add(start++);
#else
                    start++;
#endif
                    _total_length = line_length + _hrz_offset;
                }
            }
            else
            {
                //Calculating the size of a tab stop
                var tab_Stop = (_state._tab_stop * _state.Tfs + _state.Tc) * _state.Th;
                if (tab_Stop <= 0) throw new PdfInternalException("Negative tab stop");
                length = tab_Stop;
                while (start <= end)
                {
                    while (length <= line_length)
                        length += tab_Stop;
#if !LOOP_STYLES
                    //Setting the style of this tab
                    style = chLine.GetStyleRange(OriginalState, style, start, _str_end);
                    SetState(style);
#endif
                    line_length = length;
#if TAB_TO_SPACE
                    //Builds the string full of spaces and their length. I.e. how much
                    //adobe will move the position due to these spaces
                    space_length += space_width;

                    //Tab characters are changed into space characters when creating the
                    //TJ command
                    Add(start++);
#else
                    start++;
#endif
                    _total_length = line_length + _hrz_offset;
                }
            }

            //Total length the tab will move
            tab_length = length - (start_length - _hrz_offset);

#if TAB_TO_SPACE
            //Substracts the amount moved through the space character.
            Move(tab_length - space_length);
#else
            Move(tab_length);
#endif
            //Redundant ? Is done in the loop.
            _total_length = length + _hrz_offset;
        }

        /// <summary>
        /// Moves the current string into the array
        /// </summary>
        internal void Append()
        {
            if (_open)
            {
                string substr = _str.Substring(Start + _offset, End - Start + 1);
#if TAB_TO_SPACE
                if (HasTabulator)
                {
                    substr = substr.Replace('\t', ' ');
                    HasTabulator = false;
                }
#endif
                _string_array.Add(new BuildString(substr, _state.Tf));
                _open = false;
            }
        }

        /// <summary>
        /// Terminates the string being built
        /// </summary>
        internal void Close()
        {
            Append();
            var count = _string_array.Count;
            if (count == 0) return;

            //Creates string show command
            if (count == 1 && _string_array[0] is BuildString)
            {
                _draw.DrawString((BuildString)_string_array[0]);
            }
            else
                _draw.DrawString(_string_array.ToArray());
            _string_array.Clear();
        }

        /// <summary>
        /// Moves the position in the string
        /// </summary>
        /// <remarks>Note that one have to use SetState to affect underlines</remarks>
        internal void Move(double dist)
        {
            if (_string_array.Count > 0 || _open)
            {
                //If a string is being built, we simply add the move distance to it
                Append();
                _string_array.Add(new PdfReal((dist + _must_move) / (_state.Tfs * _state.Th) * -1000));
                _must_move = 0;
            }
            else
            {
                //If not, we wait a bit. Just so that we don't get
                //[0.1] TJ commands if we have to close right after this
                _must_move += dist;
            }
        }

        private void Move()
        {
            if (_must_move != 0)
            {
                double width = (_must_move) / (_state.Tfs * _state.Th) * 1000;
                if (Font.Precision == 0)
                    width = -Math.Round(width);
                else
                {
                    var prec = Math.Pow(10, Font.Precision) * 1000d;
                    width = -(Math.Truncate(width * prec) / prec);
                }
                if (width != 0)
                    _string_array.Add(new PdfReal(width));
                _must_move = 0;
            }
        }

        /// <summary>
        /// Do to precicion, a move might be rounded down to zero, and be needless.
        /// This funcion only appends if the move is zero
        /// </summary>
        private void AppendMove()
        {
            if (_must_move != 0)
            {
                double width = (_must_move) / (_state.Tfs * _state.Th) * 1000;
                if (Font.Precision == 0)
                    width = -Math.Round(width);
                else
                {
                    var prec = Math.Pow(10, Font.Precision) * 1000d;
                    width = -(Math.Truncate(width * prec) / prec);
                }
                if (width != 0)
                {
                    Append();
                    _string_array.Add(new PdfReal(width));
                }
                _must_move = 0;
            }
        }

        /// <summary>
        /// Moves the position in the string
        /// </summary>
        internal void Kern(double dist)
        {
            //Append();
            _must_move += dist;
            _total_length += _must_move;
            //Move();
            AppendMove();
        }

        /// <summary>
        /// This function emits the needed commands to switch
        /// color, as well as handeling underlining of text
        /// </summary>
        /// <remarks>
        /// Set state is called for each character. This includes tabs and
        /// images.
        ///  
        /// Quirks with underlineing
        ///  An underline tichness is determined by the tallest font. Not the
        ///  font with the greatest font size. (This is deliberate) 
        /// </remarks>
        internal void SetState(ColorRange style)
        {
            //Sets text render mode
            bool fill = style.Fill == null ? OriginalState.fill : true;
            bool stroke = style.Stroke == null ? OriginalState.stroke : true;
            bool mode = style.RenderMode == null ? OriginalState.render_mode != null : true;
            if (fill != _state.fill || stroke != _state.stroke || mode != (_state.render_mode != null))
            {
                //Note, render mode overrides stroke and fill.
                Close();
                if (mode)
                {
                    _state.render_mode = style.RenderMode;
                    _draw.SetTextMode(style.RenderMode.Value);

                    //Set false for sake of consistancy. I.e. when one set a render mode
                    //and then later set a color, the render mode will always terminate.
                    _state.fill = false;
                    _state.stroke = false;
                }
                else
                {
                    _draw.SetTextMode(Tr_CMD.GetMode(fill, stroke, false));
                    _state.fill = fill;
                    _state.stroke = stroke;
                    _state.render_mode = null;
                }
            }

            if (style.Underline != null)
            {
                if (_underline_open)
                {
                    //If the color, underline thickness changes or rendering of text is turned off 
                    //we have to close the underline.
                    if (_underline_width != style.Underline || style.RenderMode == xTextRenderMode.Invisible
                        || style.Fill != null && !cBrush.Equals(style.Fill, _state.fill_color)
                        || style.Fill == null && !cBrush.Equals(OriginalState.fill_color, _state.fill_color))
                    {
                        //We don't want the former underline to overlap the new underline, so
                        //min width is zeroed to avoid that
                        _underline_min_length = 0;
                        CloseUnderline();
                    }
                }
            }
            else if (_underline_open)
            {
                CloseUnderline();
            }

            //Sets color. Note that the color state isn't changed if the color
            //is null, instead that's handeled through the text rendering mode
            if (style.Fill != null)
            {
                if (!cBrush.Equals(style.Fill, _state.fill_color))
                {
                    Close();
                    style.Fill.MakeCMD(_state.fill_color, true, _draw);
                    _state.fill_color = style.Fill;
                }
            }
            else
            {
                //Sets the color back to the default color
                if (!cBrush.Equals(OriginalState.fill_color, _state.fill_color))
                {
                    Close();
                    OriginalState.fill_color.MakeCMD(_state.fill_color, true, _draw);
                    _state.fill_color = OriginalState.fill_color;
                }
            }
            if (style.Stroke != null)
            {
                if (!cBrush.Equals(style.Stroke, _state.stroke_color))
                {
                    Close();
                    style.Stroke.MakeCMD(_state.stroke_color, false, _draw);
                    _state.stroke_color = style.Stroke;
                }
            }
            else
            {
                //Sets the color back to the default color
                if (!cBrush.Equals(OriginalState.stroke_color, _state.stroke_color))
                {
                    Close();
                    OriginalState.stroke_color.MakeCMD(_state.stroke_color, false, _draw);
                    _state.stroke_color = OriginalState.stroke_color;
                }
            }

            if (style.Underline != null)
            {
                if (!_underline_open && style.RenderMode != xTextRenderMode.Invisible)
                {
                    _underline_open = true;
                    _underline_min_length = 0;
                    _underline_width = style.Underline.Value;
                    _underline_font = _state.Tf;
                    _underline_font_size = _state.Tfs;
                    _underline_YMax = _underline_current_YMax;
                    _underline_start_pos = _total_length;

                    //One could be smarted. Take stroke when _stroke is true, or
                    //something along those lines. For now underlines will have
                    //the fill color regardless.
                    _underline_color = _state.fill_color;
                }
            }
        }

        /// <summary>
        /// Sets the maximum height of characters in a font
        /// </summary>
        /// <param name="YMax">The maximum height</param>
        /// <remarks>
        /// This function is used to determine which font to use for
        /// underline width calculations. When there are multiple fonts
        /// being underlined by the same line, only one of them are used
        /// for width calculation in Word 2013. Going for a similar
        /// approach here.
        /// </remarks>
        internal void SetState(double YMax)
        {
            _underline_current_YMax = YMax;
            if (YMax > _underline_YMax)
            {
                _underline_YMax = YMax;
                _underline_font = _state.Tf;
                _underline_font_size = _state.Tfs;
            }
        }

        internal void CloseUnderline()
        {
            if (_underline_open)
            {
                //Make the underline command.
                //Subdelty: Underlines will always be drawn after the images they underline.
                //Note: Images currently qQ, so no problem there, but one should split these
                //collections up later.

                //Set color and width.
                List<RenderCMD> cmds = new List<RenderCMD>(6);
                if (_underline_color != null)
                    _underline_color.MakeCMD(null, false, cmds);
                cmds.Add(new w_RND(_underline_font.UnderlineThickness * _underline_font_size * _underline_width));

                var underline_length = Math.Max(_underline_min_length, _total_length - _underline_start_pos);

                //Daw the underline
                xPoint start_pos = new xPoint(_underline_start_pos, _underline_font.UnderlinePosition * _underline_font_size);
                xPoint end_pos = new xPoint(_underline_start_pos + underline_length, start_pos.Y);
                cmds.Add(new m_CMD(start_pos.Y, start_pos.X));
                cmds.Add(new l_CMD(end_pos.Y, end_pos.X));

                //fill, stroke, clip
                cmds.Add(new S_CMD());
                Blocks.Add(new cBlock(_state.Tm, cmds.ToArray()));

                _underline_open = false;
                _underline_current_YMax = 0;
            }
        }

        #endregion

        #region Measure

        internal void MeasureWord(int start, int end, double[,] measurments, out double height)
        {
            double length = 0, width, char_space = _state.Tc, hscale = _state.Th; int kern_pos = 0;
            height = 0;

            if (_kern)
            {
                chGlyph last = _state.Tf.GetGlyphMetrix(_str[start + _offset]);
                height = last.YMax;
                while (start < end)
                {
                    chGlyph next = _state.Tf.GetGlyphMetrix(_str[start + 1 + _offset]);
                    if (next.YMax > height) height = next.YMax;
                    if (_state.Tf.Precision == 0)
                        width = Math.Truncate(last.Width * 1000) / 1000d;
                    else
                    {
                        width = Math.Pow(10, _state.Tf.Precision) * 1000;
                        width = Math.Truncate((last.Width * width)) / width;
                    }
                    width = (width * _state.Tfs + char_space) * hscale;
                    length += width;
                    measurments[WIDTH, kern_pos] = width;

                    //The kern width is multiplied by "_font_size * hscale" in the renderer. So kern measurments
                    //are kept in the font's coordinate system.
                    width = _state.Tf.GetKerning(last.GID, next.GID);
                    measurments[KERN, kern_pos++] = width;

                    //Adds the kerning length, but not before sizing it to the font's size/scale.
                    length += width * _state.Tfs * hscale;
                    last = next;
                    start++;
                }
                width = (last.Width * _state.Tfs + char_space) * hscale;
                measurments[WIDTH, kern_pos] = width;
                measurments[KERN, kern_pos] = 0;
                length += width;
            }
            else
            {
                while (start <= end)
                {
                    chGlyph last = _state.Tf.GetGlyphMetrix(_str[start + _offset]);
                    if (last.YMax > height) height = last.YMax;
                    if (_state.Tf.Precision == 0)
                        width = Math.Truncate(last.Width * 1000) / 1000d;
                    else
                    {
                        width = Math.Pow(10, _state.Tf.Precision) * 1000;
                        width = Math.Truncate((last.Width * width)) / width;
                    }
                    width = (width * _state.Tfs + char_space) * hscale;
                    measurments[WIDTH, kern_pos] = width;
                    measurments[KERN, kern_pos++] = 0;
                    length += width;
                    start++;
                }
            }
            height *= _state.Tfs;
        }

        #endregion

        #region Position

        public CompiledLine TranslateTLM(double x, double y)
        {
            //Commands.Add(new T
            Flush();
            throw new NotImplementedException();
        }

        /// <summary>
        /// For shifting the line without affecting tab positions.
        /// </summary>
        /// <param name="hrz_dist">Distance to move</param>
        /// <remarks>Must be called before rendering starts</remarks>
        internal void SetOffset(double hrz_dist)
        {
            Move(hrz_dist);
            _hrz_offset += hrz_dist;
            _total_length = _hrz_offset;
        }

        /// <summary>
        /// Delays shifting to the next font change. Used to implement
        /// LSB adjust. 
        /// </summary>
        /// <param name="offset">Offset to shift.</param>
        internal void SetPending(double offset)
        {
            _pending_offset = offset;
        }

        #endregion
    }

    #region Block class

    /// <summary>
    /// Information needed to draw underlines and images is stored in blocks.
    /// </summary>
    public class cBlock
    {
        internal readonly RenderCMD[] Commands;
        public readonly xMatrix Matrix;

        /// <summary>
        /// If this block has a cm command first.
        /// </summary>
        //public bool IsCM { get { return Commands.Length > 0 && Commands[0] is cm_CMD; } }

        internal cBlock(xMatrix m, int capacity)
        {
            Matrix = m;
            Commands = new RenderCMD[capacity];
        }

        internal cBlock(xMatrix m, RenderCMD[] cmds)
        {
            Matrix = m;
            Commands =cmds;
        }
    }

    #endregion
}
