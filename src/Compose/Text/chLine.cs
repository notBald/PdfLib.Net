//#define WRAP_ON_SPACE
//#define ALT_SPACE_COLUMN_HANDELING //Must also be defined in chParagraph
//#define LOOP_STYLES //Must also be set in TextRenderer.cs
using System;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf;
using System.Globalization;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// For composing horizontal lines.
    /// 
    /// This class focuses on styling, measuring and kerning. Images and (not impl. 
    /// right now) forms can also be added to the line, in which case they're treated
    /// as if it they were single non-whitespace characters.
    /// </summary>
    /// <remarks>
    /// The way styling is applied to a line is like this:
    /// 
    /// There's "Font" and "Color" range style. These are ranges that go from 
    /// character at index 1 to character at index 2.
    /// 
    /// The FontRange is dominant. I.e. when messuring one measure from
    /// start fontrage to stop fontRange, then move to the next font.
    /// 
    /// The text is chopped up into words, spaces and blocks. (Blocks can be tabs 
    /// or images.) These items are measured individually, except when they cross a
    /// font range.
    /// 
    /// The color range is set for each character. If the color style changes the 
    /// text renderer will automatically emit the needed commands.
    /// 
    /// In practice this means that kerning can not cross font ranges, but can 
    /// cross color ranges. So styling that alter the shape of glyphs should 
    /// therefore go into font ranges, whereas those that don't should go into color 
    /// ranges.
    /// 
    /// There's no support for Italic or Bold, those settings must be set through 
    /// the font. It is possible emulate these features, say by shearing when
    /// doing italic and outline emboldening when doing bold, but that's non-trivial.
    /// 
    /// Nonbreaking space seems to work fine. It's just treated as a normal character.
    /// The only unusal carasteristic is RSB being = the characters width.
    /// 
    /// Known issues:
    ///  - Measuring parts of a string with tabulators:
    ///    When measuring a sub string with tabulators the tabulators will have 
    ///    get incorrect length.
    ///    
    ///    The renderer solves this by offsetting tabulators with 
    ///    "state.Tm.OffsetX - state.Tlm.OffsetX", though I haven't tested if
    ///    this is actually correct yet. Todo: Make a string, render the first
    ///    half (need not have tabs), then the second half, with a rotation
    ///    text metrix. 
    /// </remarks>
    public class chLine : ICloneable
    {
        #region Variables and properties

        /// <summary>
        /// This is a unicode encoded string. Fonts will decide how this
        /// data is encoded into a document.
        /// </summary>
        string _str;

        const int WIDTH = 0;
        const int KERN = 1;

        /// <summary>
        /// This is a datastructure over the words in the line
        /// </summary>
        Word[] _words;

        /// <summary>
        /// The line can also be colored
        /// </summary>
        /// <remarks>This is a linked list of color ranges</remarks>
        ColorRange _colors;

        /// <summary>
        /// Fonts used on this line
        /// </summary>
        FontRange _fonts;

        /// <summary>
        /// Images and forms can be placed inline with the text. They
        /// are stored here.
        /// </summary>
        BlockItem[] _blocks;

        /// <summary>
        /// Text in this line
        /// </summary>
        public string Text { get { return _str; } }

        /// <summary>
        /// The length of the string.
        /// </summary>
        /// <remarks>
        /// This is not the length the string will have in
        /// the final data. It's just the number of characters.
        /// </remarks>
        public int Length { get { return _str.Length; } }

        /// <summary>
        /// Provides a list over words in the string
        /// </summary>
        public string[] Words
        {
            get
            {
                if (_words == null)
                    _words = FindWords(_str, 0, _str.Length);
                int wcount = 0;
                for (int c = _words.Length; --c >= 0; )
                    if (_words[c].Type == Word.WType.WORD)
                        wcount++;
                var words = new string[wcount];
                for (int c = 0, p = 0; c < _words.Length; c++)
                {
                    var w = _words[c];
                    if (w.Type == Word.WType.WORD)
                        words[p++] = _str.Substring(w.Start, w.Length);
                }
                return words;
            }
        }

        /// <summary>
        /// A list of text string that have been colored
        /// </summary>
        public string[] ColoredText
        {
            get
            {
                return RangesAsText<ColorRange>(_colors, _str, has_color, equals_color);
            }
        }

        /// <summary>
        /// A list of text string that have been clipped
        /// </summary>
        public string[] ModeText
        {
            get
            {
                return RangesAsText<ColorRange>(_colors, _str, has_render_mode, equals_render_mode);
            }
        }

        /// <summary>
        /// A list of text string that have been underlined
        /// </summary>
        public string[] UnderlinedText
        {
            get
            {
                return RangesAsText<ColorRange>(_colors, _str, has_underline, equals_underline);
            }
        }

        /// <summary>
        /// Useful for debugging, shows the color ranges as an array
        /// </summary>
        private ColorRange[] ColorRanges
        {
            get
            {
                if (_colors == null) return null;
                //Counts up the number
                int count = 1;
                var current = _colors.Next;
                while (current != null)
                {
                    count++;
                    current = current.Next;
                }
                //Fetches the ranges
                var cr = new ColorRange[count];
                current = _colors; count = 0;
                do
                {
                    cr[count++] = current;
                    current = current.Next;
                } while (current != null);
                return cr;
            }
        }

        /// <summary>
        /// A list of text string that have font data
        /// </summary>
        public string[] FontText
        {
            get
            {
                return RangesAsText<FontRange>(_fonts, _str, has_font, equals_font);
            }
        }

        #endregion

        #region Init

        public chLine()
            : this(string.Empty, false)
        { }

        /// <summary>
        /// Constructs a line with initial text
        /// </summary>
        /// <param name="str">
        /// Line of text. Note that the space and tab characters will
        /// be used to split this line into words. Also note that \n
        /// is treated just like any other character
        /// </param>
        public chLine(string str)
            : this(str, true)
        { }

        /// <summary>
        /// Constructs a line with initial text
        /// </summary>
        /// <param name="str">The string data</param>
        /// <param name="mark_words">
        /// Whenever to find the words that make up
        /// this string. A number of functions depends
        /// on this though, so I'm not sure if it's
        /// usefull to allow this.
        /// </param>
        internal chLine(string str, bool mark_words)
        {
            if (str == null) str = string.Empty;
            _str = str;

            if (mark_words)
                _words = FindWords(str, 0, _str.Length);
        }

        private chLine(string str, Word[] words, FontRange fonts, ColorRange colors, BlockItem[] blocks)
        {
            _str = str;
            _words = words;
            _fonts = fonts;
            _colors = colors;
            _blocks = blocks;
        }


        #endregion

        #region Measure text

        /// <summary>
        /// Measures the text.
        /// </summary>
        /// <param name="state">
        /// State use for measurments, keep
        /// in mind that this state will be
        /// modified.
        /// </param>
        public LineMeasure Measure(cRenderState state, double max_length)
        {
            //if (_str.Length == 0)
            //    return Measure(1, 0, state, max_length);
            return Measure(0, _str.Length - 1, state, max_length);
        }


        /// <summary>
        /// Measures the text.
        /// </summary>
        /// <param name="state">
        /// State use for measurments, keep
        /// in mind that this state will be
        /// modified.
        /// </param>
        public xRect Measure(cRenderState state)
        {
            var lm = Measure(0, _str.Length - 1, state, double.MaxValue);
            return new xRect(0, lm.YMin, lm.Width, lm.YMax);
        }

        /// <summary>
        /// Measures a range of text
        /// </summary>
        /// <param name="start">
        /// The start position, note that if one start in
        /// the middle of a word this measurment will be wrong
        /// </param>
        /// <param name="end">
        /// Where to stop measuring. Note that if one end
        /// in the middle of a word this measurment will be wrong
        /// </param>
        /// <param name="state">
        /// State used for measurments. Note that the state will change
        /// </param>
        /// <param name="max_length">
        /// Note that (state.Tlm.OffsetX - state.Tm.OffsetX) must be sustracted from
        /// this value (it it's any other number than zero)
        /// </param>
        /// <returns>The bounding box</returns>
        /// <remarks>
        /// Note: Does not update state (font, font size) and much of anything such really,
        /// but that only matters for rendering.
        /// </remarks>
        internal LineMeasure Measure(int start, int end, cRenderState state, double max_length)
        {
            if (end >= _str.Length && start <= end)
                throw new IndexOutOfRangeException();

            //For RTL:
            // no Tc for trailing whitespace and LSB instead of RSB. Kern pairs must be switched.
            bool rtl = state.RightToLeft, kern = state.Kern;

            //We go through this range of fonts one by one
            var fonts = GetFontRange(state, _fonts, start);
            int font_end = Math.Min(end, fonts.End);

            //Used by the word finding algo
            int word_pos = 0, w_end; Word word;

            //Precision. To accuratly measure long stings one must know the precision the font will be
            //saved in. All length values are trunctuated to this precision.
            double prec;

            //How much to life the text from the baseline
            double text_rise;

            //For getting the maximum extend of Y
            var lm = new LineMeasure();
            lm.YMin = double.MaxValue;
            lm.YMax = double.MinValue;
            bool has_pre_measures = false;

            //Commonly used values
            double YMax, YMin, width, height;
            double size = fonts.Size == null ? state.Tfs : fonts.Size.Value;
            double hscale = state.Th, last_length;
            double total_length = 0, max_font_height = 0, cur_font_height, length;
            cFont font;

            //Values from the "tallest" font on the line. 
            FontMeasure fm = new FontMeasure();

            while (start <= end)
            {
                font = fonts.Font;
                prec = font.Precision;
                if (prec != 0)
                    prec = Math.Pow(10, prec) * 1000;
                text_rise = (fonts.TextRise != null ? fonts.TextRise.Value : state.Tr);

                while (start <= font_end)
                {
                    last_length = total_length;
                    word = GetWord(start, ref word_pos, _words);

                    switch (word.Type)
                    {
                        case Word.WType.BLOCK:
                            var block = (BlockItem) word.Meta;
                            if (block.Align == BlockAlign.Baseline)
                            {
                                height = font.CapHeight;
                                YMin = 0;
                            }
                            else
                            {
                                height = font.CapHeight - font.Descent;
                                YMin = -font.Descent;
                            }
                            YMax = height * size * block.Height + text_rise;
                            width = (height * size * block.Width + state.Tc) * hscale;
                            total_length += width;
                            if (total_length > max_length && last_length != 0)
                                //There's not enough room for the block
                                return lm.Set(last_length, start - 1, fm);
                            if (!has_pre_measures)
                            {
                                lm.FirstLSB = 0;
                                lm.FirstRSB = state.Tc * hscale;
                                has_pre_measures = true;
                                lm.Preceeding = lm.Trailing;
                            }
                            lm.LastLSB = 0;
                            lm.LastRSB = state.Tc * hscale;
                            lm.Trailing = 0;
                            start++;
                            break;
                        case Word.WType.WORD:
                            w_end = Math.Min(font_end, word.End);
                            length = 0;
                            chGlyph last;
                            last = font.GetGlyphMetrix(_str[start]);
                            if (!has_pre_measures)
                            {
                                lm.FirstLSB = last.LSB * size * hscale;
                                lm.FirstRSB = (last.RSB * size + state.Tc) * hscale;
                                has_pre_measures = true;
                                lm.Preceeding = lm.Trailing;
                            }
                            if (kern)
                            {
                                YMax = double.MinValue;
                                YMin = double.MaxValue;
                                while (start < w_end)
                                {
                                    chGlyph next = font.GetGlyphMetrix(_str[start + 1]);
                                    var kerning = rtl ? font.GetKerning(next.GID, last.GID) : font.GetKerning(last.GID, next.GID);
                                    if (prec == 0)
                                    {
                                        width = Math.Truncate(last.Width * 1000) / 1000d;
                                        width = (width * size + state.Tc) * hscale;
                                        width += Math.Round(kerning * size * hscale * 1000) / 1000d;
                                    }
                                    else
                                    {
                                        width = Math.Truncate((last.Width * prec)) / prec;
                                        width = (width * size + state.Tc) * hscale;
                                        width += Math.Truncate((kerning * size) * hscale * prec) / prec;
                                    }
                                    length += width;
                                    if (total_length + length > max_length && (last_length != 0 || length > width))
                                    {
                                        length -= width;
                                        goto break_word;
                                    }
                                    start++;

                                    YMin = Math.Min(last.YMin, YMin);
                                    YMax = Math.Max(last.YMax, YMax);
                                    last = next;
                                }
                                width = (last.Width * size + state.Tc) * hscale;
                                length += width;
                                if (total_length + length > max_length && (last_length != 0 || length > width))
                                {
                                    length -= width;
                                    goto break_word;
                                }
                                YMin = Math.Min(last.YMin, YMin) * size + text_rise;
                                YMax = Math.Max(last.YMax, YMax) * size + text_rise;
                            }
                            else
                            {
                                YMax = double.MinValue;
                                YMin = double.MaxValue;
                                while (true)
                                {
                                    if (font.Precision == 0)
                                        width = Math.Truncate(last.Width * 1000) / 1000d;
                                    else
                                    {
                                        width = Math.Pow(10, font.Precision) * 1000;
                                        width = Math.Truncate((last.Width * width)) / width;
                                    }
                                    width = (width * size + state.Tc) * hscale;
                                    length += width; // (last_length != 0 || length > width) == "Don't break on the very first character"
                                    if (total_length + length > max_length && (last_length != 0 || length > width))
                                        goto break_word;

                                    YMin = Math.Min(last.YMin, YMin);
                                    YMax = Math.Max(last.YMax, YMax);
                                    start++;
                                    if (start > w_end)
                                        break;
                                    last = font.GetGlyphMetrix(_str[start]);
                                }
                                YMax = YMax * size + text_rise;
                                YMin = YMin * size + text_rise;
                            }
                            total_length += length;
                            cur_font_height = font.LineHeight * size + text_rise;
                            if (max_font_height < cur_font_height)
                            {
                                max_font_height = cur_font_height;
                                fm.TextRise = text_rise;
                                fm.Size = size;
                                fm.LineHeight = font.LineHeight;
                                fm.CapHeight = font.CapHeight;
                                fm.LineAscent = font.Ascent;
                                fm.LineDescent = font.Descent;
                            }
                            lm.LastLSB = last.LSB * size * hscale;
                            lm.LastRSB = (last.RSB * size + state.Tc) * hscale;
                            lm.Trailing = 0;
                            start = w_end + 1;
                            break;
                        case Word.WType.SPACE:
                            w_end = Math.Min(font_end, word.End);
                            var metrix = font.GetGlyphMetrix(_str[start]);
                            YMax = metrix.YMax * size + text_rise;
                            YMin = metrix.YMin * size + text_rise;
                            if (font.Precision == 0)
                                width = Math.Truncate(metrix.Width * 1000) / 1000d;
                            else
                            {
                                width = Math.Pow(10, font.Precision) * 1000;
                                width = Math.Truncate((metrix.Width * width)) / width;
                            }
                            width = (width * size + state.Tw + state.Tc) * hscale;
                            length = 0d;
                            while (start <= w_end)
                            {
                                /*if (measurments != null)
                                {
                                    measurments[WIDTH, start] = width;
                                    measurments[KERN, start] = 0;
                                }*/
                                length += width;
#if WRAP_ON_SPACE
                                if (total_length + length > max_length && last_length != 0)
                                {
                                    lm.Trailing = length - width;
                                    total_length += lm.Trailing;
                                    if (line_height == 0)
                                    {
                                        //If the line only has space characters then we may end up with a line of
                                        //zero height. To avoid that we set the line height to the height of the
                                        //last font. 
                                        line_height = font.LineHeight * size + text_rise;
                                        line_ascent = font.Ascent * size + text_rise;
                                        line_descent = font.Descent * size + text_rise;
                                    }
                                    lm.YMax = Math.Max(lm.YMax, YMax);
                                    lm.YMin = Math.Min(lm.YMin, YMin);
                                    return lm.Set(total_length, start - 1, line_height, state.Tr, line_ascent, line_descent);
                                }
#endif
                                start++;
                            }
                            lm.Trailing += length;
                            total_length += length;
                            //Lineheight is ignored for spaces. Note that a space can have height, it's up to the font, but 
                            //it's quite rare and ignoring line_height will not outright break anything even then (as YMax is 
                            //still set).
                            start = w_end + 1;
                            break;
                        case Word.WType.TAB:
                            w_end = Math.Min(font_end, word.End);
                            YMax = text_rise;
                            YMin = text_rise;
                            var tab_Stop = (state._tab_stop * size + state.Tc) * hscale;
                            if (tab_Stop <= 0) throw new PdfInternalException("Negative tab stop");
                            length = tab_Stop;
                            var previous_length = total_length;

                            if (rtl)
                            {
                                //To store away measurments
                                var da = word.Meta as double[];
                                if (da == null || da.Length != word.Length)
                                    word.Meta = new double[word.Length];
                            }

                            //This is the distance that has already been rendered
                            double tab_offset = state.Tlm.OffsetX - state.Tm.OffsetX;
                            previous_length -= tab_offset;

                            while (start <= w_end)
                            {
                                while (length <= previous_length)
                                    length += tab_Stop;
#if WRAP_ON_SPACE
                                if (length > max_length)
                                {
                                    if (line_height == 0)
                                    {
                                        //If the line only has space characters then we may end up with a line of
                                        //zero height. To avoid that we set the line height to the height of the
                                        //last font. 
                                        line_height = font.LineHeight * size;
                                        line_ascent = font.Ascent * size;
                                        line_descent = font.Descent * size;
                                    }
                                    lm.Trailing = previous_length - total_length;
                                    if (_rtl)
                                    {
                                        //Saves the length of the tab, for correct right to left rendering. Since tabs are "small"
                                        //in width, I think we can get away by storing it in a int with 6 digit precision.
                                        word.Meta = (int)(lm.Trailing * 100000) + 1;
                                        lm.Trailing = (word.Meta - 1) / 100000;
                                        previous_length = lm.Trailing + total_length;
                                    }
                                    return lm.Set(previous_length, start - 1, line_height, state.Tr, line_ascent, line_descent);
                                }
#endif
                                if (rtl)
                                {
                                    //Note, values are placed in the reverse order. An alternate to this is to read out
                                    //in reverse order during rendering.
                                    ((double[]) word.Meta)[word.End - start] = length - previous_length;
                                }

                                previous_length = length;
                                start++;
                            }
                            length += tab_offset;
                            var tabs_length = length - total_length;
                            lm.Trailing += tabs_length;
                            total_length = length;
                            start = w_end + 1;
                            break;

                        default:
                            throw new NotImplementedException();
                    }
                    lm.YMax = Math.Max(lm.YMax, YMax);
                    lm.YMin = Math.Min(lm.YMin, YMin);
                }

                fonts = GetFontRange(state, fonts.Next, start);
                size = fonts.Size == null ? state.Tfs : fonts.Size.Value;
                font_end = Math.Min(end, fonts.End);
            }

            //Lineheight is the measure from baseline to baseline, while YMax is from
            //baseline to tallest point.
            if (max_font_height == 0)
            {
                //If the line only has space characters then we may end up with a line of
                //zero height. To avoid that we set the line height to the height of the
                //last font. (Idealy one ould use the tallest font)
                //
                //Note this code also executes for strings with 0 characters.
                size = fonts.Size == null ? state.Tfs : fonts.Size.Value;
                font = fonts.Font;
                fm.TextRise = 0;
                fm.Size = size;
                fm.LineHeight = font.LineHeight;
                fm.CapHeight = font.CapHeight;
                fm.LineAscent = font.Ascent;
                fm.LineDescent = font.Descent;
            }

            if (total_length == 0)
            {
                lm.YMin = 0;
                lm.YMax = 0;
                //line_height = 0;
            }

            return lm.Set(total_length, end, fm);

        break_word:
            if (state.SimpleBreakWord && state.BreakWordCharacter != null)
            {
                //Break words like in html documents. Note that a broken word always end with the - character,
                //it never starts on that character.

                //Amount of characters that fits on the line. If it's zero, then no more characters fits on the line.
                int stub_length = start - word.Start;

                //Look for the last BreakwordCharacter in the stub
                if (stub_length > 0)
                {
                    char end_ch = _str[start], st_ch = _str[start - stub_length];
                    var break_pos = _str.LastIndexOf(state.BreakWordCharacter.Value, start - 1, stub_length);
                    if (break_pos != -1)
                    {
                        //Remeasures the word up to and including the break word character.
                        //(We can safely assume that the whole word has the same font and font size)
                        MeasureWord(word.Start, break_pos, out length, font, size, _str, lm, out YMax, out YMin, state);

                        lm.YMax = Math.Max(lm.YMax, YMax);
                        lm.YMin = Math.Min(lm.YMin, YMin);
                        return lm.Set(last_length + length, break_pos, fm);
                    }
                }

                //Here we disable the "break up the last word on the line" feature. It's what HTML does,
                //so I'm sticking to that.
                if (last_length == 0) //< --This simply means we're dealing with the last word on the line.
                {
                    //We need to measure out the full word, and return the result. The easist way to do this
                    //is just to use the same measure function we're in. Trying to restart the measurer from
                    //where it broke off would be a pain. 
                    //
                    //Note that the "state" object is never altered by this function, so there's no need to
                    //restore it, and since last_length is zero there's no need to add anything to the result. 
                    return Measure(word.Start, word.End, state, double.MaxValue);
                }

                //Here we cut off the line right before the current word, eg. the character
                //before word.Start. Since last_length is > 0, we know that word.Start > 0.
                return lm.Set(last_length, word.Start - 1, fm);

                //If we don't return here, the algorithm below will see the that the "BreakWordCharacter"
                //has been set and start doing things. I can't imagine any scenario where we want to
                //combine two different word breaking algos, so we'll just end the show here.               
            }

            //If last length is zero then there's only one word on the line, thus it has to be broken.
            if (last_length != 0)
            {
                //First, check if the word has a break word character.
                if (state.BreakWord && state.BreakWordCharacter != null && word.Length >= 3)
                {
                    //Amount of characters left
                    int stub_length = start - word.Start;

                    //We need at least two character before meaninfully breaking a word
                    if (stub_length > 1)
                    {
                        //Search through the string, except for the first character.
                        var break_pos = _str.LastIndexOf(state.BreakWordCharacter.Value, start, stub_length - 1);
                        if (break_pos != -1)
                        {
                            //Breaks the word after the BreakWordCharacter

                            //Remeasures the word up to and including the break word character.
                            //(We can safely assume that the whole word has the same font and font size)
                            MeasureWord(word.Start, break_pos, out length, font, size, _str, lm, out YMax, out YMin, state);

                            lm.YMax = Math.Max(lm.YMax, YMax);
                            lm.YMin = Math.Min(lm.YMin, YMin);
                            return lm.Set(last_length + length, break_pos, fm);
                        }
                    }

                    //Words with break word characters, shouln't be broken elsewhere. They can end up looking odd.
                    bool has_breakcharacter = _str.IndexOf(state.BreakWordCharacter.Value, word.Start, word.Length) != -1;
                    if (has_breakcharacter)
                        return lm.Set(last_length, word.Start - 1, fm);
                }

                if (state.BreakWord && word.Length >= state.BreakWordMinLength)
                {
                    if (state.BreakWordSplit)
                    {
                        int min_length = word.Length / 2;
                        int stub_length = start - word.Start;
                        if (stub_length < min_length)
                        {
                            //To few characters left on the line to room half the word,
                            //so the whole word is moved down.
                            return lm.Set(last_length, word.Start - 1, fm);
                        }
                        if (stub_length > min_length)
                            min_length += word.Length % 2;
                        if (state.BreakWordCharacter != null)
                        {
                            lm.AppendChar = state.BreakWordCharacter;
                            char ch = state.BreakWordCharacter.Value;

                            //Adds a character to indicate that a word has been broken.
                            double trailing_whitespace = lm.Trailing, lsb = lm.LastLSB, rsb = lm.LastRSB;
                            MeasureWord(0, min_length, out length, font, size, _str.Substring(word.Start, min_length) + ch, lm, out YMax, out YMin, state);
                            if (last_length + length > max_length)
                            {
                                if (--min_length >= state.BreakWordMinLength / 2)
                                    MeasureWord(0, min_length, out length, font, size, _str.Substring(word.Start, min_length) + ch, lm, out YMax, out YMin, state);
                                if (last_length + length > max_length)
                                {
                                    //If the '-' dosn't fit, we move the whole word down.
                                    lm.AppendChar = null;
                                    lm.Trailing = trailing_whitespace;
                                    lm.LastLSB = lsb;
                                    lm.LastRSB = rsb;

                                    //YMax/Min need not be set. Is already correct
                                    return lm.Set(last_length, word.Start - 1, fm);
                                }
                            }
                        }
                        else
                        {
                            //Remeasures half the word. (We can safely assume that the whole word has the same font and font size)
                            MeasureWord(0, min_length - 1, out length, font, size, _str.Substring(word.Start, min_length), lm, out YMax, out YMin, state);
                        }
                        lm.YMax = Math.Max(lm.YMax, YMax);
                        lm.YMin = Math.Min(lm.YMin, YMin);
                        return lm.Set(last_length + length, word.Start + min_length - 1, fm);
                    }

                    if (state.BreakWordCharacter != null)
                    {
                        //Note. If break word split is false, then words are broken where-ever. That might be undesirable in case where you want to have
                        //words split only on the break word character. So I assume this behavior. I.e. by setting the BreakWordCharacter you loose the
                        //ability to have words broken on the last avalible character.
                        return lm.Set(last_length, word.Start - 1, fm);
                    }
                }
                else
                {
                    //This Word is not to be broken, so we move the whole word down to the next line.

                    //Note that "trailing_whitespace" is equal to the whitespace before
                    //this word, so since we're pushing this word wholly onto the new line
                    //we need not remeasure the whitespace.

                    //We shorten the line down to the start of the current word. "Total_length" points at
                    //the end of the last word, and since we know that this isn't the first word on the
                    //line we can assume that "end" equals on character before the current word
                    return lm.Set(last_length, word.Start - 1, fm);
                }
            }

            //Since we are using characters from this word we have to respect the font's line height. Do
            //note that we can safely assume that the whole word has the same font, so there no need to
            //try to find the font for the "last character", as it will always be the same.
            cur_font_height = font.LineHeight * size;
            if (max_font_height < cur_font_height)
            {
                fm.TextRise = text_rise;
                fm.Size = size;
                fm.LineHeight = font.LineHeight;
                fm.CapHeight = font.CapHeight;
                fm.LineAscent = font.Ascent;
                fm.LineDescent = font.Descent;
            }

            lm.YMax = Math.Max(lm.YMax, YMax * size);
            lm.YMin = Math.Min(lm.YMin, YMin * size);
            last_length += length;

            //Need to fetch the RSB of the last glyph on the line, and recalculate whitespace unless
            //it is whitespace.
            int pos = 0;
            var type = GetWord(start - 1, ref pos, _words).Type;
            if (type == Word.WType.WORD)
            {
                chGlyph g = font.GetGlyphMetrix(_str[start - 1]);
                lm.LastRSB = (g.RSB * size + state.Tc) * hscale;
                lm.LastLSB = g.LSB;
                lm.Trailing = 0;
            }
            else if (type == Word.WType.BLOCK)
            {
                lm.LastRSB = state.Tc * hscale;
                lm.LastLSB = 0;
                lm.Trailing = 0;
            }

            return lm.Set(last_length, start - 1, fm);
        }

        private void MeasureWord(int start, int w_end, out double length, cFont font, double size, string str, LineMeasure lm, out double YMax, out double YMin, cRenderState state)
        {
            length = 0;
            chGlyph last;
            last = font.GetGlyphMetrix(str[start]);
            double width, hscale = state.Th;
            bool rtl = state.RightToLeft, kern = state.Kern;
            double prec = font.Precision;

            if (kern)
            {
                YMax = double.MinValue;
                YMin = double.MaxValue;
                while (start < w_end)
                {
                    chGlyph next = font.GetGlyphMetrix(str[start + 1]);
                    var kerning = rtl ? font.GetKerning(next.GID, last.GID) : font.GetKerning(last.GID, next.GID);
                    if (prec == 0)
                    {
                        width = Math.Truncate(last.Width * 1000) / 1000d;
                        width = (width * size + state.Tc) * hscale;
                        width += Math.Round(kerning * size * hscale * 1000) / 1000d;
                    }
                    else
                    {
                        width = Math.Truncate((last.Width * prec)) / prec;
                        width = (width * size + state.Tc) * hscale;
                        width += Math.Truncate((kerning * size) * hscale * prec) / prec;
                    }
                    length += width;
                    start++;

                    YMin = Math.Min(last.YMin, YMin);
                    YMax = Math.Max(last.YMax, YMax);
                    last = next;
                }
                width = (last.Width * size + state.Tc) * hscale;
                length += width;
                YMin = Math.Min(last.YMin, YMin) * size;
                YMax = Math.Max(last.YMax, YMax) * size;
            }
            else
            {
                YMax = double.MinValue;
                YMin = double.MaxValue;
                while (true)
                {
                    if (font.Precision == 0)
                        width = Math.Truncate(last.Width * 1000) / 1000d;
                    else
                    {
                        width = Math.Pow(10, font.Precision) * 1000;
                        width = Math.Truncate((last.Width * width)) / width;
                    }
                    width = (width * size + state.Tc) * hscale;
                    length += width;

                    YMin = Math.Min(last.YMin, YMin);
                    YMax = Math.Max(last.YMax, YMax);
                    start++;
                    if (start > w_end)
                        break;
                    last = font.GetGlyphMetrix(str[start]);
                }
                YMax *= size;
                YMin *= size;
            }

            lm.LastLSB = last.LSB * size * hscale;
            lm.LastRSB = (last.RSB * size + state.Tc) * hscale;
            lm.Trailing = 0;
        }

        #endregion

        #region Render text

        /// <summary>
        /// Renders the line's text
        /// </summary>
        /// <param name="text_renderer">Target</param>
        /// <param name="start">Start of text</param>
        /// <param name="w_end">End of whitespace</param>
        /// <param name="stop">Stop of text</param>
        internal void Render(TextRenderer text_renderer, int start, int w_end, int stop)
        {
            if (start > stop) return;
            var state = text_renderer.State;
            if (state.RightToLeft)
            {
                //Calling "Substring" to support chLineWrapper SINGLE_LINE_MODE (which is now the only mode)
                text_renderer.SetText(ReverseGraphemeClusters(_str.Substring(start, stop - start + 1)), stop - w_end - 1);

                //Must render the whitespace portion first
                //
                //Do note that there's no actual need to keep the additional spaces, they're just here
                //for completness sake. Adobe reader does not care, not even when copying the text.
                //
                //The only issue with dropping spaces on rtl strings is that the rest of the string
                //must be moved the distance the T( ) makes up.
                RenderImpl(text_renderer, w_end + 1, stop);

                //Removes the underlines
                text_renderer.CloseUnderline();
                text_renderer.SetBlocks(null);

                //Renders the non-whitespace portion
                int sstop = stop - start, sw_end = w_end - start;
                text_renderer.SetText(sstop - sw_end, sstop - (sstop - sw_end));
                RenderImpl(text_renderer, start, w_end);
            }
            else
            {
                //Prepeares the text renderer
                text_renderer.SetText(_str, stop);

                //Renders the non-whitespace portion
                RenderImpl(text_renderer, start, w_end);

                //Finish underlines
                text_renderer.CloseUnderline();

                //Renders the whitespace
                if (w_end + 1 <= stop)
                {
                    //Underline information is stored in blocks, by fetching it
                    //we get only the underlines for the non-whitespace portion
                    var blocks = text_renderer.NumBlocks;
                    RenderImpl(text_renderer, w_end + 1, stop);

                    text_renderer.CloseUnderline();
                    text_renderer.SetBlocks(blocks);
                }
            }
        }

        /// <summary>
        /// Renders the text.
        /// </summary>
        /// <param name="state">Text state</param>
        /// <returns>The compiled commands</returns>
        public CompiledLine Render(cRenderState state)
        {
            var text_renderer = new TextRenderer(state);
            return Render(0, _str.Length - 1, text_renderer);
        }

        /// <summary>
        /// Renders text without initing the text renderer
        /// </summary>
        internal void Render(TextRenderer tr)
        {
            Render(tr, 0, _str.Length - 1);
        }

        /// <summary>
        /// Renders text without initing the text renderer
        /// </summary>
        /// <remarks>
        /// Internal because if one try to append a character to a line's "true" end, 
        /// you will get an index out of bounds. This because the line will not have
        /// a "word" structure for the append_character.
        /// </remarks>
        internal void Render(TextRenderer text_renderer, int start, int stop, char append_character)
        {
            if (start > stop) return;
            if (text_renderer.State.RightToLeft)
                text_renderer.SetText(ReverseGraphemeClusters(_str.Substring(0, stop + 1)+append_character), ++stop);
            else
                text_renderer.SetText(_str.Substring(0, stop + 1) + append_character, ++stop);
            RenderImpl(text_renderer, start, stop);
        }

        /// <summary>
        /// Renders text without initing the text renderer
        /// </summary>
        internal void Render(TextRenderer text_renderer, int start, int stop)
        {
            if (start > stop) return;
            if (text_renderer.State.RightToLeft)
                text_renderer.SetText(ReverseGraphemeClusters(_str.Substring(0, stop + 1)), stop);
            else
                text_renderer.SetText(_str, stop);
            RenderImpl(text_renderer, start, stop);
        }

        public CompiledLine Render(int start, int stop, TextRenderer text_renderer)
        {
            if (start > stop) return new CompiledLine(null, null);
            text_renderer.Init();
            if (text_renderer.State.RightToLeft)
                text_renderer.SetText(ReverseGraphemeClusters(_str.Substring(start, stop - start + 1)), stop);
            else
                text_renderer.SetText(_str, stop);
            RenderImpl(text_renderer, start, stop);
            return text_renderer.Make();
        }

        /// <summary>
        /// Renders a text into PDF commands
        /// </summary>
        /// <param name="column">
        /// When this parameter is true, metadata to adjust word
        /// position is compiled along. 
        /// </param>
        /// <remarks>
        /// This code does measurement as well as rendering. It's perfectly
        /// possible to store away the result of measurment during layout,
        /// but on the other hand, doing the measurments again is pretty 
        /// quick, and has the bonus of allowing the caller to adjust Tw,
        /// Tc and other such parameters safely without remeasuring.
        /// 
        /// Rendered lines do not have a concept of position, they
        /// don't care if they are placed right/center/left or middle.
        /// 
        /// Future enhancements
        ///  1. Make use of the minimum kerning table
        ///  
        ///     Some fonts supply kerning tables with minimum values.
        ///     I.e. values for positioning the glyphs closer together.
        ///     
        ///     There's currently no way to tell chLine to use that table
        ///     instead, besides manually changing the appropriate bool flag
        ///     in the MTTFont constructor and recompiling. (I.e. make it 
        ///     always fetch the minimum instead of the normal kern table).
        ///     
        ///  2. RSB/LSB ignore
        ///  
        ///     Currently, the chGlyph structure does not take along the
        ///     RSB/LSB values. (I.e. the whitespace at the side of the glyph)
        ///     If one wish to render glyphs right next to each other one could
        ///     add a render mode where RSB/LSB was ignored.
        ///     
        ///     RSB/LSB is currently fetched during keerning, (along with 
        ///     YMax/YMin), it just isn't stored in the chGlyph structure
        ///     
        ///     However while it's fine to store them (cache) them in chGlyph,
        ///     to actually get at them it's probably best to add a method
        ///     for that instead of relying on kerning for this feature.
        ///     
        ///  3. Fitting
        ///  
        ///     One may want to fit the text into a bounding box, even if it's
        ///     too long normally. For a feature such as this one must modify
        ///     the measure function to add together all the RSB/LSB values.
        ///     
        ///     That way one could know just how much the string can be shrunk.
        ///     Then add a parameter here that shrinks RSB/LSB with x %, where
        ///     0% would be just like the feature above and 100% would be normal
        ///     rendering.
        ///     
        ///     One could also add, say, 200% to move the characters further apart.
        ///     There's a "char space" value that does something similar to this
        ///     already, but it's tricky to use for fitting as it's just a fixed
        ///     distance.
        ///     
        ///  4. TextRise. This can be used to make subscripts. To add this
        ///     functionality, add it to the font range. It's also possible
        ///     to add it to the color range, but then the measure function
        ///     will not know about it (and thus give back an incorrect height
        ///     max)
        ///     
        ///     Adding text rise to the FontRange will prevent rise characters
        ///     from being keerned with non-rise characters, but that is probably
        ///     the behavior one wants so is instead a nice side effect.
        ///     
        /// Known issue
        ///  - For correct kerning, this function require that start and end land
        ///    on word bounderies.
        /// </remarks>
        internal void RenderImpl(TextRenderer text_renderer, int start, int end)
        {
            var state = text_renderer.State;
            bool kern = state.Kern;
            Word[] words;
            FontRange fonts;
            ColorRange style;
            if (state.RightToLeft)
            {
                /// Right to left writing is handled in a somewhat peculiar manner in PDF files. 
                /// For such writing one have to reverse the string, i.e. write "gnirts" if
                /// one want to write "string" rtl.
                /// 
                /// What we do here is reversing the string, along with style and word information,
                /// then render as normal.

                //Adjusts meta data to the substring
                fonts = CopyStyleRanges<FontRange>(start, end, _fonts);
                Shift<FontRange>(-start, fonts);
                style = CopyStyleRanges<ColorRange>(start, end, _colors);
                Shift<ColorRange>(-start, style);
                words = CopyWords(start, end, _words);
                Shift(words, -start);

                //Reverse the meta data direction
                int shift = end - start;
                fonts = ReverseRange<FontRange>(fonts, shift);
                style = ReverseRange<ColorRange>(style, shift);
                if (words != null)
                    words = ReverseWords(words, shift, 0, words.Length - 1);
                end -= start;
                start = 0;
            }
            else
            {
                words = _words;
                fonts = _fonts;
                style = _colors;
            }

            //Used by the word finding algo
            int word_pos = 0; Word word;

            //Line metrixs
            double[,] kern_buffer = new double[2, 10];

            //Commonly used values
            double width, move_width, height, YPos, YMax;
            int w_start, w_end;
            double hscale = state.Th;

            //Looping through all letters
            while (start <= end)
            {
                //We go through this range of fonts one by one
                fonts = GetFontRange(text_renderer.OriginalState, fonts, start);
                int font_end = Math.Min(end, fonts.End);
                double font_size;

                //Sets up font for text rendering
                text_renderer.SetFont(fonts.Font, fonts.Size.Value);
                text_renderer.SetRise(fonts.TextRise.Value);

                //Looping through all fonts
                while (start <= font_end)
                {
#if LOOP_STYLES
                    //We also go through the style range one by one
                    style = GetStyleRange(text_renderer.OriginalState, style, start, end);
                    int style_end = Math.Min(font_end, style.End);

                    //Sets up style for text rendering
                    text_renderer.SetState(style);

                    //Looping through all styles
                    while (start <= style_end)
#endif
                    {
                        //Measures word for word
                        word = GetWord(start, ref word_pos, words);

                        switch (word.Type)
                        {
                            //Blocks are images and other such objects placed on the line.
                            case Word.WType.BLOCK:
                                var block = (BlockItem) word.Meta;
                                font_size = text_renderer.FontSize;
                                if (block.Align == BlockAlign.Baseline)
                                {
                                    height = fonts.Font.CapHeight;
                                    YPos = state.Tr;
                                }
                                else
                                {
                                    height = fonts.Font.LineHeight;
                                    YPos = -fonts.Font.Descent * font_size + state.Tr;
                                }
                                width = height * block.Width * font_size * hscale;
                                height *= font_size * block.Height;

                                //Calculates the actual move width
                                move_width = width + state.Tc * hscale;

                                //Avoids the block character (\t) beeing inadvertedly included with a string.
                                //(This due to a optimization in text_renderer.Move)
                                text_renderer.Append();
#if !LOOP_STYLES
                                //SetState must be called for each character. It makes sure color commands are
                                //emitted as needed, it also handles underline.
                                style = GetStyleRange(text_renderer.OriginalState, style, start, end);
                                text_renderer.SetState(style);
#endif
                                //Adds the block rendering commands
                                text_renderer.Add(block, width, move_width, height, YPos);

                                //Blocks does not need to end the rendering of a string, as they
                                //are drawn after the "ET". But they have to make room in the
                                //string for themselves.
                                text_renderer.Move(move_width);

                                //All blocks are one character long
                                start++;
                                break;

                            //Words are text unbroken by whitespace. This needs to
                            //be measured together for kerning purposes.
                            case Word.WType.WORD:

                                //First we measure the text
                                if (word.Length > kern_buffer.GetLength(1))
                                    kern_buffer = new double[2, word.Length];
                                w_start = Math.Max(word.Start, start);
                                w_end = Math.Min(font_end, word.End);
                                text_renderer.MeasureWord(w_start, w_end, kern_buffer, out YMax);
#if !LOOP_STYLES
                                //Underline tickness is decided by the largetst font on the line, but
                                //the underline style can change. Because of this one have to set the
                                //state before SetState(YMax), in case the underline style has changed.
                                style = GetStyleRange(text_renderer.OriginalState, style, w_start, end);
                                text_renderer.SetState(style);
#endif
                                text_renderer.SetState(YMax);

                                //Then we itterate through all the values and adjust for kerning
                                //when there's a need for that.
                                for (int kern_pos = 0; w_start <= w_end; kern_pos++)
                                {
                                    //Fetches the metrixs for this glyph
                                    double kern_width = kern_buffer[KERN, kern_pos];
                                    double glyph_width = kern_buffer[WIDTH, kern_pos];

                                    //Fetches and sets the style for this glyph
                                    style = GetStyleRange(text_renderer.OriginalState, style, w_start, end);
                                    text_renderer.SetState(style);

                                    //Adds the character to the current string being built
                                    text_renderer.Add(w_start++, glyph_width);

                                    //Addjusts for kerning. Note, the adjustment is between this glyph
                                    //and the next glyph. 
                                    if (kern_width != 0)
                                        text_renderer.Kern(kern_width);
                                }

                                //Moves to the next word. This can move beyond the end, but all
                                //that happens is that a uneeded font range is created.
                                start = w_end + 1;
                                break;

                            //White space (tabs and space) breaks up words. PDF treats space as a
                            //special character, and allows one to adjust the length of the space
                            //through a word space character.
                            case Word.WType.SPACE:
                                w_start = Math.Max(word.Start, start);
#if LOOP_STYLES
                                w_end = Math.Min(style_end, word.End);
#else
                                w_end = Math.Min(font_end, word.End);
#endif
                                text_renderer.AddSpace(w_start, w_end, ref style);
                                start = w_end + 1;
                                break;
                            case Word.WType.TAB:
                                //Note that there's is no \t character placed in the string. Experimenting shows
                                //that viewers does not like this. I.e. tabs are just empty space.
                                w_start = Math.Max(word.Start, start);
#if LOOP_STYLES
                                w_end = Math.Min(style_end, word.End);
#else
                                w_end = Math.Min(font_end, word.End);
#endif
                                text_renderer.AddTab(w_start, w_end, ref style, w_start - word.Start, word.Meta as double[]);
                                start = w_end + 1;
                                break;

                            default:
                                throw new NotImplementedException();
                        }
                    }
                }
            }
        }

        //http://stackoverflow.com/questions/228038/best-way-to-reverse-a-string#228460
        //
        //This is overkill as my code does not support surrogate characters, in any case
        //this function reverses strings with respect to surrogate characters.
        private static IEnumerable<string> GraphemeClusters(string s)
        {
            var enumerator = StringInfo.GetTextElementEnumerator(s);
            while (enumerator.MoveNext())
            {
                yield return (string)enumerator.Current;
            }
        }
        internal static string ReverseGraphemeClusters(string s)
        {
            int pos = s.Length;
            var ca = new char[pos];
            foreach (var str in GraphemeClusters(s))
            {
                pos -= str.Length;
                for (int c = 0; c < str.Length; c++)
                    ca[pos + c] = str[c];
            }
            return new string(ca);
        }

        #endregion

        #region Modify text

        /// <summary>
        /// Inserts an image at a position in the string
        /// </summary>
        /// <param name="pos">Position to where to insert the image</param>
        /// <param name="img">The image to insert</param>
        /// <param name="height">Optional height</param>
        public void Insert(int pos, PdfImage img, double? width, double? height, BlockAlign? align)
        {
            var lines = Split(pos);
            var l = lines[0];
            _words = l._words;
            _fonts = l._fonts;
            _colors = l._colors;
            _blocks = l._blocks;
            _str = l._str;
            if (align != null)
                Append(img, height.Value, width.Value, align.Value);
            else if (width != null)
                Append(img, height.Value, width.Value, BlockAlign.Baseline);
            else if (height != null)
                Append(img, height.Value);
            else
                Append(img);
            Append(lines[1]);
        }

        /// <summary>
        /// Splits a line
        /// </summary>
        /// <param name="pos">Position where to split the line</param>
        public chLine[] Split(int pos)
        {
            string pre = _str.Substring(0, pos);
            string post = _str.Substring(pos);
            Word[] pre_words = null, post_words = null;
            FontRange pre_fonts = null, post_fonts = null;
            ColorRange pre_style = null, post_style = null;
            BlockItem[] pre_blocks = null, post_blocks = null;

            if (_words != null)
            {
                int word_index = GetWordIndex(pos);
                if (word_index == -1)
                {
                    //When splitting "beyond" the last character
                    if (pos < 0 || pos > _str.Length) throw new IndexOutOfRangeException();
                    pre_words = new Word[_words.Length];
                    for (int c = 0; c < _words.Length; c++)
                        pre_words[c] = (Word)_words[c].Clone();
                    post_words = new Word[0];
                }
                else
                {
                    var word = _words[word_index];

                    bool must_split = (word.Start != pos);
                    if (must_split)
                        word = (Word)word.Clone();

                    pre_words = new Word[word_index + ((must_split) ? 1 : 0)];
                    for (int c = 0; c < word_index; c++)
                        pre_words[c] = (Word)_words[c].Clone();

                    post_words = new Word[_words.Length - word_index];
                    for (int c = word_index, i = 0; c < _words.Length; c++)
                        post_words[i++] = (Word)_words[c].Clone();
                    Shift(post_words, -pos);

                    if (must_split)
                    {
                        post_words[0].Start = 0;
                        word.End = pos - 1;
                        pre_words[word_index] = word;
                    }
                }
            }

            if (pos > 0)
            {
                pre_fonts = CopyStyleRanges<FontRange>(0, pos - 1, _fonts);
                pre_style = CopyStyleRanges<ColorRange>(0, pos - 1, _colors);
            }
            post_fonts = CopyStyleRanges<FontRange>(pos, _str.Length - 1, _fonts);
            post_style = CopyStyleRanges<ColorRange>(pos, _str.Length - 1, _colors);
            Shift<FontRange>(-pos, post_fonts);
            Shift<ColorRange>(-pos, post_style);

            if (_blocks != null)
            {
                List<BlockItem> lb = new List<BlockItem>(_blocks.Length);
                CopyBlocks(pre_words, _blocks, lb);
                pre_blocks = lb.ToArray();
                lb.Clear();
                CopyBlocks(post_words, _blocks, lb);
                post_blocks = lb.ToArray();
            }

            return new chLine[] {
                new chLine(pre, pre_words, pre_fonts, pre_style, pre_blocks),
                new chLine(post, post_words, post_fonts, post_style, post_blocks)
            };
        }

        private static void CopyBlocks(Word[] words, BlockItem[] blocks, List<BlockItem> lb)
        {
            for (int c = 0; c < words.Length; c++)
            {
                var word = words[c];
                if (word.Type == Word.WType.BLOCK)
                {
                    var block = (BlockItem) word.Meta;
                    lb.Add(block);
                }
            }
        }

        /// <summary>
        /// Append an image or form to the line
        /// </summary>
        /// <param name="width">Width of the object, depends on alignement</param>
        /// <param name="height">Height of the object, depends on alignement</param>
        /// <param name="alignement">How the object is to be placed on the line</param>
        public void Append(PdfXObject xobject, double width, double height, BlockAlign alignement)
        {
            Debug.Assert(_words != null);
            if (_blocks == null)
                _blocks = new BlockItem[1];
            else
                Array.Resize<BlockItem>(ref _blocks, _blocks.Length + 1);
            Array.Resize<Word>(ref _words, _words.Length + 1);
            var bi = new BlockItem(xobject, width, height, alignement);
            _words[_words.Length - 1] = new Word(_str.Length, _str.Length, Word.WType.BLOCK, bi);
            _blocks[_blocks.Length - 1] = bi;
            _str += '\t';
        }
        public void Append(PdfImage image, double height)
        {
            Append(image, height * image.Width / image.Height, height, BlockAlign.Baseline);
        }
        public void Append(PdfImage image)
        {
            Append(image, image.Width, image.Height, BlockAlign.Baseline);
        }

        /// <summary>
        /// Adds a block item at the start of a line
        /// </summary>
        public void Prepend(PdfXObject xobject, double width, double height, BlockAlign alignement)
        {
            Debug.Assert(_words != null);
            if (_blocks == null)
                _blocks = new BlockItem[1];
            else
            {
                Array.Resize<BlockItem>(ref _blocks, _blocks.Length + 1);
                Array.Copy(_blocks, 0, _blocks, 1, _blocks.Length - 1);
            }
            var bi = new BlockItem(xobject, width, height, alignement);
            _blocks[0] = bi;
            ShiftStyles(1);
            _str = '\t' + _str;
            var word = new Word(0, 0, Word.WType.BLOCK, bi);
            _words = Combine(new Word[] { word }, _words);
        }
        public void Prepend(PdfImage image, double height)
        {
            Prepend(image, height * image.Width / image.Height, height, BlockAlign.Baseline);
        }
        public void Prepend(PdfImage image)
        {
            Prepend(image, image.Width, image.Height, BlockAlign.Baseline);
        }

        /// <summary>
        /// Appends a line at the end of this line
        /// </summary>
        /// <param name="line">Line to append</param>
        public void Append(chLine line)
        {
            if (line == null) throw new ArgumentNullException();
            line = (chLine)line.Clone();
            line.ShiftStyles(_str.Length);
            var lw = line._words;
            if (line._blocks != null)
            {
                var lb = line._blocks;
                if (_blocks == null)
                    _blocks = lb;
                else
                {
                    int shift = _blocks.Length;
                    Array.Resize<BlockItem>(ref _blocks, _blocks.Length + lb.Length);
                }
            }
            _str = _str + line._str;
            if (_words == null)
                _words = lw;
            else if (lw != null && _words.Length > 0 && lw.Length > 0)
            {
                Word last = _words[_words.Length - 1], first = lw[0];
                if (last.Equals(first))
                {
                    last.End = first.End;
                    int l = _words.Length;
                    Array.Resize<Word>(ref _words, _words.Length + lw.Length - 1);
                    Array.Copy(lw, 1, _words, l, lw.Length - 1);
                }
                else
                    _words = Util.ArrayHelper.Join<Word>(_words, lw);
            }
            else
                _words = Util.ArrayHelper.Join<Word>(_words, lw);

            if (_colors != null)
            {
                var last_color = (ColorRange)_colors.Last();
                last_color.Next = line._colors;
                if (last_color != null)
                    CombineEqualRanges<ColorRange>(last_color, last_color.Next);
            }
            else
                _colors = line._colors;

            if (_fonts != null)
            {
                var last_font = (FontRange)_fonts.Last();
                last_font.Next = line._fonts;
                if (last_font != null)
                    CombineEqualRanges<FontRange>(last_font, last_font.Next);
            }
            else
                _fonts = line._fonts;
        }

        /// <summary>
        /// Appends a string at the end of this line
        /// </summary>
        /// <param name="str">String to append</param>
        public void Append(string str)
        {
            if (str == "") return;
            int pos = _str.Length; _str += str;
            var words = FindWords(_str, pos, _str.Length);
            _words = Combine(_words, words);
        }

        /// <summary>
        /// Prepends a string at the beginning of this line
        /// </summary>
        /// <param name="str">String to prepend</param>
        public void Prepend(string str)
        {
            if (str == "") return;
            _str = str + _str;
            //Adjusts the positions in the old words array
            ShiftStyles(str.Length);

            var words = FindWords(str, 0, str.Length);
            _words = Combine(words, _words);
        }

        #endregion

        #region Colors, clipping and underline

        /// <summary>
        /// Gets the font range for the given position
        /// </summary>
        /// <param name="styles">
        /// It is assumed that this is tne next font in the queue
        /// </param>
        internal static ColorRange GetStyleRange(cRenderState org_state, ColorRange styles, int pos, int last_end)
        {
            //Skips to the next relevant style
            while (styles != null && pos > styles.End)
                styles = styles.Next;

            if (styles == null || pos < styles.Start)
            {
                //Returns the state style
                int end = (styles == null) ? last_end : styles.Start - 1;
                var sr = new ColorRange(pos, end, org_state.fill ? org_state.fill_color : null, org_state.stroke ? org_state.stroke_color : null, null, org_state.render_mode);
                sr.Next = styles;
                return sr;
            }
            else
            {
                //Note that color ranges always override the state. I.e. if the state say clip and stroke, and the color range say just fill
                //then the state is to be changed to this. This is unlike fonts that will fetch data it's missing from the state. 
                return styles;
            }
        }

        public void SetColor(int word_index, cBrush fill)
        {
            SetColor(word_index, fill, null);
        }

        public void SetColor(int word_index, cBrush fill, cBrush stroke)
        {
            var word = GetWord(word_index);
            SetColor(word.Start, word.End, fill, stroke);
        }

        public void SetColor(int start, int end, cBrush fill)
        { SetColor(start, end, fill, null); }

        /// <summary>
        /// Sets a color on a range
        /// </summary>
        /// <remarks>For invisible text, set TR mode to invisible instead of using this function</remarks>
        public void SetColor(int start, int end, cBrush fill, cBrush stroke)
        {
            SetColorStyle(start, end, new ColorStyle(fill, stroke));
        }

        /// <summary>
        /// Sets a color on a range
        /// </summary>
        /// <remarks>For invisible text, set TR mode to invisible instead of using this function</remarks>
        public void SetColorStyle(int start, int end, ColorStyle style)
        {
            //Starts by slicing up the ranges so that we get ranges
            //that go from start to end. 
            var ranges = CutStyleRanges<ColorRange>(start, end, _colors);

            //If there's no ranges, all that needs to be done is create a new one.
            bool has_value = style.HasValue;
            if (ranges == null)
            {
                //We insert a new range
                if (has_value)
                    AddStyle<ColorRange>(new ColorRange(start, end, style.Fill, style.Stroke, style.Underline, style.Mode), ref _colors);
                return;
            }

            //We go over the list and sets the colors
            var current = ranges.First;
            var end_range = ranges.Last;
            var stop = end_range.Next;
            var ptr = ranges.Ptr;

            if (has_value)
            {
                //We may have to create a range before and after the ranges. Since these ranges
                //don't overwrite existing ranges, we don't have to check the style.Set* values
                if (start < current.Start)
                    CreateGapRange(ptr, current, style.Fill, style.Stroke, style.Underline, style.Mode);

                //The last range
                if (end_range.End < end)
                {
                    if (stop != null)
                        CreateGapRange(end_range, stop, style.Fill, style.Stroke, style.Underline, style.Mode);
                    else
                    {
                        //If stop is null then we append a new range
                        stop = new ColorRange(end_range.Start + 1, end, style.Fill, style.Stroke, style.Underline, style.Mode);
                        end_range.Next = stop;
                    }
                }
            }

            while (true)
            {
                if (style.SetFill)
                    current.Fill = style.Fill;
                if (style.SetStroke)
                    current.Stroke = style.Stroke;
                if (style.SetRender)
                    current.RenderMode = style.Mode;
                var next = current.Next;
                if (ReferenceEquals(next, stop))
                    break;

                //We have to add ranges into the gaps
                if (has_value)
                    CreateGapRange(current, next, style.Fill, style.Stroke, style.Underline, style.Mode);

                current = next;
            }

            CombineEqualRanges<ColorRange>(ptr, stop, ref _colors);
        }

        public void RemoveColor(int word_index)
        {
            SetColor(word_index, null, null);
        }

        public void RemoveColor(int start, int end)
        {
            SetColor(start, end, null, null);
        }

        /// <summary>
        /// Sets the Text Rendering mode. This is usefull when one want invisible
        /// or clippted text. Note that in case of clipped text, no text will
        /// be drawn after clipping, say, the middle of a string.
        /// </summary>
        public void SetRenderMode(int start, int end, xTextRenderMode? mode)
        {
            SetColorStyle(start, end, new ColorStyle(mode));
        }

        /// <summary>
        /// Sets a word as a clipping region
        /// </summary>
        public void SetRenderMode(int word_index, xTextRenderMode? mode)
        {
            var word = GetWord(word_index);
            SetRenderMode(word.Start, word.End, mode);
        }

        /// <summary>
        /// Sets a section of text as underlined.
        /// </summary>
        public void SetUnderline(int start, int end, double? underline)
        {
            //Underline information is included in the color ranges.
            var ranges = CutStyleRanges<ColorRange>(start, end, _colors);
            if (ranges == null)
            {
                //We insert a new  range, but only if actually clipping.
                if (underline != null)
                    AddStyle<ColorRange>(new ColorRange(start, end, underline), ref _colors);
                return;
            }

            //We go over the list of color ranges and sets the underline
            var current = ranges.First;
            var end_range = ranges.Last;
            var stop = end_range.Next;
            var ptr = ranges.Ptr;
            if (underline != null)
            {
                //We may have to create a range before and after the ranges. Since
                //we are creating new ranges here we don't have to do this when underline
                //is null.
                if (start < current.Start)
                    CreateGapRange(ptr, current, null, null, underline, null);
                if (end_range.End < end)
                {
                    if (stop != null)
                        CreateGapRange(end_range, stop, null, null, underline, null);
                    else
                    {
                        stop = new ColorRange(end_range.End + 1, end, underline);
                        end_range.Next = stop;
                    }
                }
            }

            while (true)
            {
                current.Underline = underline;
                var next = current.Next;
                if (ReferenceEquals(next, stop))
                    break;

                //We have to add underline ranges into the gaps
                if (underline != null)
                    CreateGapRange(current, next, null, null, underline, null);

                current = next;
            }

            //Ranges that are now equal is combined into single ranges
            CombineEqualRanges<ColorRange>(ptr, stop, ref _colors);
        }

        public void SetUnderline(int word_index, double? underline)
        {
            var word = GetWord(word_index);
            SetUnderline(word.Start, word.End, underline);
        }

        /// <summary>
        /// For creating a range between other ranges.
        /// </summary>
        void CreateGapRange(ColorRange start, ColorRange end, cBrush fill, cBrush stroke, double? underline, xTextRenderMode? render_mode)
        {
            if (start == null)
            {
                //Null is always the very first range.
                Debug.Assert(end != null);
                if (_colors.Start > 0)
                {
                    var r = new ColorRange(0, end.Start - 1, fill, stroke, underline, render_mode);
                    r.Next = _colors;
                    _colors = r;
                }
            }
            else
            {
                if (!ReferenceEquals(start, end))
                {
                    int gap_start_pos = start.End + 1;
                    if (gap_start_pos < end.Start)
                    {
                        var gap = new ColorRange(gap_start_pos, end.Start - 1, fill, stroke, underline, render_mode);
                        start.Next = gap;
                        gap.Next = end;
                    }
                }
            }
        }

        #endregion

        #region Fonts

        /// <summary>
        /// Gets the font range for the given position
        /// </summary>
        /// <param name="org_state">
        /// The unmodified state object. 
        /// </param>
        /// <param name="fonts">
        /// It is assumed that this is the next font in the queue
        /// </param>
        FontRange GetFontRange(cRenderState org_state, FontRange fonts, int pos)
        {
            //Skips to the next relevant font
            while (fonts != null && pos > fonts.End)
                fonts = fonts.Next;

            if (fonts == null || pos < fonts.Start)
            {
                //Returns the state font
                int end = (fonts == null) ? _str.Length - 1 : fonts.Start - 1;
                var fr = new FontRange(pos, end, org_state.Tf, org_state.Tfs, org_state.Tr);
                fr.Next = fonts;
                return fr;
            }
            else
            {
                if (fonts.Font == null)
                {
                    Debug.Assert(fonts.Size != null);
                    var fr = new FontRange(fonts.Start, fonts.End, org_state.Tf, fonts.Size, fonts.TextRise == null ? org_state.Tr : fonts.TextRise);
                    fr.Next = fonts.Next;
                    fr.Size = fonts.Size.Value;
                    return fr;
                }
                else if (fonts.Size == null)
                {
                    var fr = new FontRange(fonts.Start, fonts.End, fonts.Font, org_state.Tfs, fonts.TextRise == null ? org_state.Tr : fonts.TextRise);
                    fr.Next = fonts.Next;
                    return fr;
                }
                else if (fonts.TextRise == null)
                {
                    var fr = new FontRange(fonts.Start, fonts.End, fonts.Font, fonts.Size, org_state.Tr);
                    fr.Next = fonts.Next;
                    return fr;
                }
                Debug.Assert(fonts.Font != null || fonts.Size != null);
                return fonts;
            }
        }

        public void SetTextRise(int start, int end, double? size, double? text_rise)
        {
            SetFontStyle(start, end, new FontStyle(size, text_rise));
        }

        /// <summary>
        /// Sets a font for a section of text.
        /// </summary>
        public void SetFont(int start, int end, cFont font, double? size)
        {
            SetFontStyle(start, end, new FontStyle(font, size));
        }

        /// <summary>
        /// Sets a font for a section of text.
        /// </summary>
        public void SetFont(int start, int end, cFont font, double? size, double? text_rise)
        {
            SetFontStyle(start, end, new FontStyle(font, size, text_rise));
        }

        /// <summary>
        /// Sets a font for a section of text.
        /// </summary>
        /// <param name="start">Start of range</param>
        /// <param name="end">End of range</param>
        /// <param name="style">A style object that lets you set, leave be and remove styling.</param>
        public void SetFontStyle(int start, int end, FontStyle style)
        {
            //Starts by slicing up the ranges so that we get ranges
            //that go from start to end. 
            var ranges = CutStyleRanges(start, end, _fonts);
            
            //If there's no ranges, all that needs to be done is create a new one.
            bool has_value = style.HasValue;
            if (ranges == null)
            {
                if (has_value)
                    AddStyle(new FontRange(start, end, style.Font, style.Size, style.Rise), ref _fonts);
                return;
            }

            //We go over the list and sets the values
            var current = ranges.First;
            var end_range = ranges.Last;
            var stop = end_range.Next;
            var ptr = ranges.Ptr;
           
            if (has_value)
            {
                //We may have to create a range before and after the ranges. Since these ranges
                //don't overwrite existing ranges, we don't have to check the style.Set* values
                if (start < current.Start)
                    CreateGapRange(ptr, current, style.Font, style.Size, style.Rise);
                if (end_range.End < end)
                {
                    if (stop != null)
                        CreateGapRange(end_range, stop, style.Font, style.Size, style.Rise);
                    else
                    {
                        stop = new FontRange(end_range.End + 1, end, style.Font, style.Size, style.Rise);
                        end_range.Next = stop;
                    }
                }
            }

            while (true)
            {
                if (style.SetFont)
                    current.Font = style.Font;
                if (style.SetSize)
                    current.Size = style.Size;
                if (style.SetRise)
                    current.TextRise = style.Rise;
                var next = current.Next;
                if (ReferenceEquals(next, stop))
                    break;

                //We have to add ranges into the gaps
                if (has_value)
                    CreateGapRange(current, next, style.Font, style.Size, style.Rise);

                current = next;
            }

            CombineEqualRanges(ptr, stop, ref _fonts);
        }
        public void SetFont(int start, int end, cFont font)
        { SetFont(start, end, font, null); }
        public void SetFont(int start, int end, double? size)
        { SetFont(start, end, null, size); }
        public void SetFont(int word_index, cFont font, double? size)
        {
            var word = GetWord(word_index);
            SetFont(word.Start, word.End, font, size);
        }
        public void SetFont(int word_index, double? size)
        { SetFont(word_index, null, size); }
        public void SetFont(int word_index, cFont font)
        { SetFont(word_index, font, null); }


        void CreateGapRange(FontRange start, FontRange end, cFont font, double? size, double? text_rise)
        {
            if (start == null)
            {
                //Null is always the very first range.
                Debug.Assert(end != null);
                if (_colors.Start > 0)
                {
                    var r = new FontRange(0, end.Start - 1, font, size, text_rise);
                    r.Next = _fonts;
                    _fonts = r;
                }
            }
            else
            {
                if (!ReferenceEquals(start, end))
                {
                    int gap_start_pos = start.End + 1;
                    if (gap_start_pos < end.Start)
                    {
                        var gap = new FontRange(gap_start_pos, end.Start - 1, font, size, text_rise);
                        start.Next = gap;
                        gap.Next = end;
                    }
                }
            }
        }

        #endregion

        #region Style ranges

        static T ReverseRange<T>(T styles, int shift)
            where T : StyleRange<T>, new()
        {
            if (styles == null) return null;
            styles = CopyStyleRanges<T>(styles);
            var last = styles;
            var next = styles.Next;
            styles.Next = null;
            last.Shift(shift - last.End - last.Start);
            while (next != null)
            {
                var next_next = next.Next;
                next.Next = last;
                last = next;
                next = next_next;
                last.Shift(shift - last.End - last.Start);
            }

            return last;
        }

        void ShiftStyles(int l)
        {
            Shift(_words, l);
            Shift<ColorRange>(l, _colors);
            Shift<FontRange>(l, _fonts);
        }

        static void Shift<T>(int l, T styles)
            where T : StyleRange<T>, new()
        {
            while (styles != null)
            {
                styles.Shift(l);
                styles = styles.Next;
            }
        }

        public void ClearStyle()
        { _colors = null; }

        /// <summary>
        /// Adds a color range to the linket list of colors
        /// 
        /// This function assumes that the ranges
        /// do not overlap at all.
        /// </summary>
        /// <returns>Color range pointing at the added range, can be null</returns>
        static T AddStyle<T>(T range, ref T styles)
            where T : StyleRange<T>, new()
        {
            if (styles == null)
            {
                styles = range;
                return null;
            }
            else if (range.End < styles.Start)
            {
                //Add to start
                range.Next = styles;
                styles = range;
                return null;
            }
            else
            {
                var current = styles;
                T last = null;
                while (true)
                {
                    if (current.End < range.Start)
                    {
                        if (current.Next != null && current.Next.End < range.Start)
                        {
                            current = current.Next;
                            continue;
                        }
                        range.Next = current.Next;
                        current.Next = range;
                        return last;
                    }
                    if (current.Next == null)
                        break;
                    last = current;
                    current = current.Next;
                }
                current.Next = range;
                return current;
            }
        }

        /// <summary>
        /// Combines equal ranges and prunes default ranges
        /// </summary>
        /// <param name="start">Start from, can be null</param>
        /// <param name="end">Stop at, can be null</param>
        static void CombineEqualRanges<T>(T start, T end, ref T styles)
            where T : StyleRange<T>, new()
        {
            Debug.Assert(start == null || !ReferenceEquals(start, end));
            if (start == null)
            {
                //It's possible that start is Default, so we remove
                //default starts. 
                start = styles;
                while (start.Default)
                {
                    styles = start.Next;
                    start = styles;
                    if (start == null) return;
                }

                //We can assume that _colors are never null
                Debug.Assert(start != null);
            }

            //We should be able to assume that start is never default, this since
            //start always points at the range that has been modified, except when
            //start == null
            Debug.Assert(!start.Default);

            while (!ReferenceEquals(start, end))
            {
                var next = start.Next;
                if (next == null) break; //<-- End is allowed to be null
                if (next.Default)
                {
                    //Default ranges are removed.
                    next = next.Next;
                    start.Next = next;
                }
                else if (start.Adjacent(next) && start.Equals(next))
                {
                    start.End = next.End;

                    if (ReferenceEquals(next, end))
                    {
                        //We just "removed" the ending, which means we're done
                        start.Next = next.Next;
                        break;
                    }

                    //We remove the next
                    start.Next = next.Next;

                    //We must check if the new start match with its new next
                    continue;
                }

                start = next;
            }
        }

        /// <summary>
        /// Combines equal ranges
        /// </summary>
        /// <param name="start">Start from, can be null</param>
        /// <param name="end">Stop at, can be null</param>
        static void CombineEqualRanges<T>(T start, T end)
            where T : StyleRange<T>, new()
        {
            Debug.Assert(start == null || !ReferenceEquals(start, end));
            if (start == null)
                return;

            while (!ReferenceEquals(start, end))
            {
                var next = start.Next;
                if (next == null) break; //<-- End is allowed to be null
                if (start.Adjacent(next) && start.Equals(next))
                {
                    start.End = next.End;

                    if (ReferenceEquals(next, end))
                    {
                        //We just "removed" the ending, which means we're done
                        start.Next = next.Next;
                        break;
                    }

                    //We remove the next
                    start.Next = next.Next;

                    //We must check if the new start match with its new next
                    continue;
                }

                start = next;
            }
        }

        /// <summary>
        /// Finds all ranges that overlap with start/end and
        /// cuts that range out (but doesn't remove it)
        /// </summary>
        /// <param name="create_new">
        /// If there are no overlapping ranges, 
        /// </param>
        static AffectedRanges<T> CutStyleRanges<T>(int start, int end, T _ranges)
            where T : StyleRange<T>, new()
        {
            //First we get all affected color ranges for the cut range
            T new_range, copy;
            var ranges = GetStyleRanges<T>(start, end, _ranges);

            //There need not be any affected ranges
            if (ranges == null)
            {
                //In which case we simply return null
                return null;
            }

            //If start and end is the same range we have to make sure
            //we don't modify both.
            var start_range = ranges.First;
            var end_range = ranges.Last;
            bool ranges_equal = ReferenceEquals(start_range, end_range);

            //If the start range starts before the cut range we have to
            //make a small range that goes from the start range to the
            //start of the cut range
            if (start_range.Start < start)
            {
                //However if the ranges equal it's possible that the cut
                //range is entierly enclosed inside the start range
                if (ranges_equal && start_range.End > end)
                {
                    //In which case we split the larger range into two
                    //smaller ranges and insert a new range between these
                    //two ranges.
                    new_range = start_range.Copy(start, end);
                    copy = start_range.Copy(end + 1, start_range.End);
                    start_range.End = start - 1;
                    new_range.Next = copy;
                    copy.Next = start_range.Next;
                    start_range.Next = new_range;
                    return new AffectedRanges<T>(new_range, new_range, start_range);
                }

                //We now create a range that goes from the cut start to the end of the
                //start range. 
                copy = start_range.Copy(start, start_range.End);

                //Then we shrink down the start_range and set it as the pointer.
                //(Previusly the pointer went to the start range, now we want
                // the pointer to point at the bit we cut away from the start)
                start_range.End = start - 1;
                ranges.Ptr = start_range;

                //And inserts the copy
                copy.Next = start_range.Next;
                start_range.Next = copy;
                start_range = copy;
                ranges.First = copy;


                //One could combine ranges here, but the caller will have to
                //combine ranges anyway
            }

            if (ranges_equal)
            {
                Debug.Assert(ReferenceEquals(end_range, start_range) ||
                             ReferenceEquals(end_range.Next, start_range));

                //If the ranges are equal we have to cut a little differently
                if (end < end_range.End)
                {
                    end_range.Next = end_range.Copy(end + 1, end_range.End);
                    end_range.End = end;
                }

                ranges.Last = start_range;
            }
            if (end < end_range.End)
            {
                //Here we have to make an adjustment to the end range, which means we
                //need to find the pointer to the end range.
                T current = start_range.Next, last = start_range;
                while (!ReferenceEquals(current, end_range))
                {
                    last = current;
                    current = current.Next;
                }
                //Last.Next now points at the end range.

                //We now create a range that goes from end_range.start to cut end
                //and insert that
                copy = end_range.Copy(end_range.Start, end);
                end_range.Start = end + 1;
                copy.Next = end_range;
                last.Next = copy;
                ranges.Last = copy;
            }
            Debug.Assert(ReferenceEquals(ranges.First, start_range) &&
                         (ranges.Ptr == null ||
                         ReferenceEquals(ranges.Ptr.Next, start_range)));
            return ranges;
        }

        /// <summary>
        /// Create a copy of the ranges going from start to end.
        /// </summary>
        /// <typeparam name="T">Type of ranges to copy</typeparam>
        /// <param name="start">Start of range, will be start of copied range</param>
        /// <param name="end">End of range, will be the end of the copied range</param>
        /// <param name="ranges">Ranges to copy</param>
        /// <returns>Copied ranges going from start to end</returns>
        static T CopyStyleRanges<T>(int start, int end, T ranges)
            where T : StyleRange<T>, new()
        {
            var affected = GetStyleRanges<T>(start, end, ranges);
            if (affected == null) return null;

            T current = affected.First, next = current.Next, stop = affected.Last;
            if (next == null || ReferenceEquals(current, stop))
                return current.Copy(Math.Max(start, current.Start), Math.Min(end, current.End));
            ranges = current.Copy(Math.Max(start, current.Start), current.End);
            current = ranges;

            while (!ReferenceEquals(next, stop))
            {
                current.Next = next.Copy();
                current = current.Next;
                next = next.Next;
            }

            current.Next = next.Copy(next.Start, Math.Min(end, next.End));
            return ranges;
        }

        static Word[] CopyWords(int start, int end, Word[] words)
        {
            int first_index = 0;
            while (true)
            {
                if (first_index == words.Length)
                    return null;

                if (words[first_index].Contain(start))
                    break;
                first_index++;
            }
                
                
            int last_index = first_index;
            while (true)
            {
                if (last_index == words.Length)
                {
                    last_index--;
                    break;
                }

                if (words[last_index].Contain(end))
                    break;
                last_index++;
            }
            var w = new Word[last_index - first_index + 1];
            for (int c = 0; c < w.Length; c++)
                w[c] = (Word) words[first_index++].Clone();

            w[0].Start = Math.Max(start, w[0].Start);
            w[w.Length - 1].End = Math.Min(end, w[w.Length - 1].End);

            return w;
        }

        /// <summary>
        /// Create a copy of the ranges
        /// </summary>
        /// <typeparam name="T">Type of ranges to copy</typeparam>
        /// <param name="ranges">Ranges to copy</param>
        /// <returns>Copied ranges going from start to end</returns>
        static T CopyStyleRanges<T>(T ranges)
            where T : StyleRange<T>, new()
        {
            if (ranges == null) return null;
            T start = ranges.Copy();
            T current = start;
            ranges = ranges.Next;
            while (ranges != null)
            {
                var copy = ranges.Copy();
                current.Next = copy;
                current = copy;
                ranges = ranges.Next;
            }
            return start;
        }

        /// <summary>
        /// Finds all ranges that overlap 
        /// </summary>
        /// <returns>The first affected range, the last affected range and a pointer to the first range</returns>
        static AffectedRanges<T> GetStyleRanges<T>(int start, int end, T ranges)
            where T : StyleRange<T>, new()
        {
            if (ranges == null) return null;
            T first = null, last = null, ptr = null, current = ranges;

            do
            {
                //We first look for a range that
                //may overlap
                if (!current.Before(start))
                {
                    if (current.After(end))
                        break;
                    if (current.Intersect(start, end))
                    {
                        if (first == null)
                        {
                            ptr = last;
                            first = current;
                        }
                        last = current;
                    }
                    else if (first != null)
                    {
                        //There will be no more overlapping ranges                        
                        break;
                    }
                }
                else
                    last = current;

                current = current.Next;
            } while (current != null);

            if (first == null) return null;
            return new AffectedRanges<T>(first, last, ptr);
        }

        #endregion

        #region Words

        /// <summary>
        /// Makes a copy of the word array and returns it reversed.
        /// </summary>
        /// <param name="words"></param>
        /// <param name="shift"></param>
        /// <returns></returns>
        static Word[] ReverseWords(Word[] words, int shift, int start_index, int end_index)
        {
            var wa = new Word[end_index - start_index + 1];
            for (int c = 0, k = end_index; c <= end_index; c++)
            {
                var w = words[c];
                int sh = shift - w.Start - w.End;
                wa[k--] = new Word(shift - w.End, shift - w.Start, w.Type, w.Meta);
            }
            return wa;
        }

        /// <summary>
        /// Gets the index of word covering a text position
        /// </summary>
        /// <param name="pos">A character position</param>
        /// <returns>The index of the word object</returns>
        private int GetWordIndex(int pos)
        {
            for (int c = 0; c < _words.Length; c++)
                if (_words[c].Contain(pos))
                    return c;
            return -1;
        }

        /// <summary>
        /// Fetches the word for the relevant text
        /// </summary>
        /// <remarks>The position of the word in the _word array</remarks>
        static Word GetWord(int pos, ref int search_from, Word[] words)
        {
            if (pos != words[search_from].Start)
            {
                if (pos > words[search_from].End)
                {
                    search_from++;
                    while (pos > words[search_from].End)
                        search_from++;
                }
                else if (pos < words[search_from].Start)
                {
                    search_from++;
                    while (pos < words[search_from].Start)
                        search_from++;
                }
            }
            return words[search_from];
        }

        Word GetWord(int index)
        {
            int c = 0;
            for (int w = 0; ; c++)
                if (_words[c].Type == Word.WType.WORD && w++ == index)
                    break;
            return _words[c];
        }

        /// <summary>
        /// Moves the position of the word array
        /// </summary>
        private static void Shift(Word[] words, int n)
        {
            if (words != null)
            {
                for (int c = words.Length; --c >= 0; )
                    words[c].Shift(n);
            }
        }

        private static string GetText(string str, Word[] words)
        {
            return words.Length == 0 ? "" : str.Substring(words[0].Start, GetLength(words));
        }

        /// <summary>
        /// Get the length of a collection of words
        /// </summary>
        private static int GetLength(Word[] words)
        {
            return words.Length == 0 ? 0 : words[words.Length - 1].End - words[0].Start + 1;
        }

        /// <summary>
        /// Finds the words in a string
        /// </summary>
        /// <param name="str">String to search through</param>
        /// <param name="offset">Position in the string from where to search</param>
        /// <param name="length">How far to search, note include the offset here</param>
        /// <returns>Words in the string</returns>
        private static Word[] FindWords(string str, int offset, int length)
        {
            var words = new Word[str.Length];
            int size = 0, start = offset, end;
            var whitespace = new char[] { ' ', '\t' };

            while (start < length)
            {
                end = str.IndexOfAny(whitespace, start);
                if (end == -1)
                {
                    //No whitespace characters found
                    words[size++] = new Word(start, str.Length - 1, Word.WType.WORD);
                    break;
                }
                if (start < end)
                    words[size++] = new Word(start, end - 1, Word.WType.WORD);
                var ch = str[end];
                start = end++;

                //Finds how many whitespace characters there are
                while (end < str.Length && str[end] == ch)
                    end++;
                words[size++] = new Word(start, end - 1, ch == ' ' ? Word.WType.SPACE : Word.WType.TAB);
                start = end;
            }
            Array.Resize<Word>(ref words, size);
            return words;
        }

        private static Word[] Combine(Word[] pre, Word[] post)
        {
            if (pre.Length == 0) return post;
            if (post.Length == 0) return pre;
            int end = pre.Length - 1;
            Word[] comb;
            if (pre[end].Type == post[0].Type)
            {
                pre[end].End = post[0].End;
                comb = new Word[pre.Length + post.Length - 1];
                Array.Copy(pre, comb, pre.Length);
                Array.Copy(post, 1, comb, pre.Length, post.Length - 1);
            }
            else
            {
                comb = new Word[pre.Length + post.Length];
                Array.Copy(pre, comb, pre.Length);
                Array.Copy(post, 0, comb, pre.Length, post.Length);
            }
            return comb;
        }

        #endregion

        #region Text

        public string GetText(int start, int end)
        {
            return _str.Substring(start, end - start + 1);
        }

        /// <summary>
        /// This deledate is for checking if a style has the data one want
        /// </summary>
        delegate bool style_has<T>(T style);
        delegate bool style_equals<T>(T last, T current);

        /// <summary>
        /// Tests if a color range has render mode set
        /// </summary>
        static bool has_render_mode(ColorRange r) { return r.RenderMode != null; }
        static bool equals_render_mode(ColorRange r, ColorRange current) { return r.RenderMode == current.RenderMode; }

        /// <summary>
        /// Tests if a color range has color
        /// </summary>
        static bool has_color(ColorRange r) { return r.HasColor; }
        static bool equals_color(ColorRange last, ColorRange current) { return last.EqualsColor(current); }

        /// <summary>
        /// Tests if a color range has underline
        /// </summary>
        static bool has_underline(ColorRange r) { return r.Underline != null; }
        static bool equals_underline(ColorRange last, ColorRange current) { return last.Underline != null; }

        /// <summary>
        /// Tests if a color range has a font
        /// </summary>
        static bool has_font(FontRange r) { return true; }
        static bool equals_font(FontRange last, FontRange current) { return ReferenceEquals(last.Font, current.Font) && last.Size == current.Size; }

        /// <summary>
        /// This function turns style ranges into a list of text strings
        /// </summary>
        /// <typeparam name="T">Type of style range</typeparam>
        /// <param name="styles">The style ranges</param>
        /// <param name="full_str">The string the ranges map to</param>
        /// <param name="has">
        /// A function that is used to test if
        /// the range has the type of data one want displayed</param>
        /// <param name="equals">
        /// Used for comparing with the last color, to determining if they
        /// are equal or not
        /// </param>
        static string[] RangesAsText<T>(T styles, string full_str, style_has<T> has, style_equals<T> equals)
            where T : StyleRange<T>, new()
        {
            if (styles == null) return null;
            int count = has(styles) ? 1 : 0;
            var current = styles.Next;
            var last = styles;
            while (current != null)
            {
                if (has(current) && (!equals(last, current) || !last.Adjacent(current)))
                    count++;
                last = current;
                current = current.Next;
            }
            var text = new string[count];
            current = styles; count = 0;
            do
            {
                if (has(current))
                {
                    var str = full_str.Substring(current.Start, current.Length); ;
                    if (equals(last, current) && last.Adjacent(current))
                        text[count - 1] += str;
                    else
                        text[count++] = str;
                }
                last = current;
                current = current.Next;
            } while (current != null);
            return text;
        }

        #endregion

        #region Information

        /// <summary>
        /// Index of the last non-whitespace word or block on the line, can be -1
        /// for lines with no words or only whitspace
        /// </summary>
        public int LastCharIndex
        {
            get
            {
                int c = _words.Length - 1;
                Word w = null;
                while (c >= 0)
                {
                    w = _words[c];
                    var t = w.Type;
                    if (t != Word.WType.TAB && t != Word.WType.SPACE)
                        break;

                    c--;
                }
                if (c == -1) return c;
                return w.End;
            }
        }

        internal int GetLastCharIndex(int start, int end)
        {
            int c = GetWordIndex(end);
            Word w = null;
            while (c >= 0)
            {
                w = _words[c];
                var t = w.Type;
                if (t != Word.WType.TAB && t != Word.WType.SPACE)
                    break;
                if (start > w.Start)
                    return -1;
                c--;
            }
            if (c == -1) return c;
            return Math.Min(end, w.End);
        }

        /// <summary>
        /// First non-whitespace character on the line, -1 for lines without
        /// characters or blocks.
        /// </summary>
        public int FirstCharIndex
        {
            get
            {
                int c = 0;
                Word w = null;
                for (; c < _words.Length; c++)
                {
                    w = _words[c];
                    var t = w.Type;
                    if (t != Word.WType.TAB && t != Word.WType.SPACE)
                        break;
                }
                if (c == _words.Length) return -1;
                return w.Start;
            }
        }

        public TWInfo GetInfo(int start, int end)
        {
            int last_tab = -1;
            int stop = -1;
            int n_space = 0;
            int idx_end = GetWordIndex(end);
            int idx_start = GetWordIndex(start);
            //const int idx_start = 0;
            Word w = null;
            Word.WType t;
#if ALT_SPACE_COLUMN_HANDELING
            var spaces = new List<TWInfo.TWSpace>();
#endif

            //Moves past the whitespace at the end of the line
            while (idx_end >= idx_start)
            {
                w = _words[idx_end];
                t = w.Type;
                if (t == Word.WType.TAB)
                {
                    //If there's a tab at the end of the line
                    last_tab = w.End;
                    return new TWInfo(last_tab, last_tab, 0);
                }
                if (t != Word.WType.SPACE)
                    break;
                if (start > w.Start)
                {
                    idx_end = -1;
                    break;
                }

                idx_end--;
            }

            //Lines can have only spaces
            if (idx_end < idx_start)
                return new TWInfo(last_tab, stop, 0);

            //Set the stop marker for where the column renderer
            //should stop.
            stop = Math.Min(end, w.End);

            //Counts up the number of white space characters.
            while (idx_end > idx_start)
            {
                w = _words[--idx_end];
                if (start > w.Start)
                    break;

                if (w.Type == Word.WType.TAB)
                {
                    last_tab = w.End;
                    break;
                }

                if (w.Type == Word.WType.SPACE)
                {
#if ALT_SPACE_COLUMN_HANDELING
                    n_space++;
                    if (w.Length > 1)
                    {
                        spaces.Add(new TWInfo.TWSpace(w.Start, w.End, n_space));
                        n_space = 0;
                    }
#else
                    n_space += w.Length;
#endif
                }
            }
#if ALT_SPACE_COLUMN_HANDELING
            return new TWInfo(last_tab, stop, n_space, spaces.ToArray());
#else
            return new TWInfo(last_tab, stop, n_space);
#endif
        }

        #endregion

        #region Overrides

        public object Clone()
        {
            BlockItem[] blocks = _blocks;
            //if (_blocks != null)
            //{
            //    blocks = new BlockItem[_blocks.Length];
            //    for (int c = 0; c < _blocks.Length; c++)
            //        blocks[c] = (BlockItem) _blocks[c].Clone();
            //}
            var fonts = CopyStyleRanges<FontRange>(_fonts);
            var colors = CopyStyleRanges<ColorRange>(_colors);
            Word[] words = null;
            if (_words != null)
            {
                words = new Word[_words.Length];
                for (int c = 0; c < _words.Length; c++)
                    words[c] = (Word)_words[c].Clone();
            }
            return new chLine(_str, words, fonts, colors, blocks);
        }

        /// <summary>
        /// Returns the unicode string, not including the 
        /// trailing line feed
        /// </summary>
        public override string ToString()
        {
            if (_blocks != null)
            {
                StringBuilder sb = new StringBuilder(_str.Length - _blocks.Length);
                for (int c = 0; c < _words.Length; c++)
                {
                    var word = _words[c];
                    if (word.Type != Word.WType.BLOCK)
                        sb.Append(_str.Substring(word.Start, word.Length));
                }
            }
            return _str;
        }

        #endregion

        #region Classes and structs

        /// <summary>
        /// Styling that alters the layout of the text
        /// </summary>
        public class FontStyle
        {
            public bool SetFont, SetSize, SetRise;
            public cFont Font;
            public double? Size, Rise;
            public bool HasValue { get { return Font != null || Size != null || Rise != null; } }
            public FontStyle(double? size, double? rise)
            {
                Rise = rise; Size = size;
                SetSize = SetRise = true;
            }
            public FontStyle(cFont font, double? size)
            {
                Font = font; Size = size;
                SetFont = SetSize = true;
            }
            public FontStyle(cFont font, double? size, double? rise)
            { 
                SetFont = SetSize = SetRise = true;
                Font = font; Size = size; Rise = rise;
            }
        }

        /// <summary>
        /// Styling that does not alter the layout of the text
        /// </summary>
        public class ColorStyle
        {
            public bool SetFill, SetStroke, SetRender, SetUnderline;
            public cBrush Fill, Stroke;
            public xTextRenderMode? Mode;
            public double? Underline;
            public bool HasValue { get { return Fill != null || Stroke != null || Mode != null || Underline != null; } }
            public ColorStyle(xTextRenderMode? mode)
            {
                Mode = mode;
                SetRender = true;
            }
            public ColorStyle(cBrush fill, cBrush stroke)
            {
                Fill = fill; Stroke = stroke;
                SetFill = SetStroke = true;
            }
            public ColorStyle(cBrush fill, cBrush stroke, xTextRenderMode? mode, double? underline)
            {
                SetFill = SetStroke = SetRender = SetUnderline = true;
                Fill = fill; Stroke = stroke; Mode = mode; Underline = underline;
            }
        }

        /// <summary>
        /// Information about tab and whitespace
        /// </summary>
        /// <remarks>
        /// This class is used to implement column rendering
        /// </remarks>
        public class TWInfo
        {
            /// <summary>
            /// The character position of the last tab
            /// </summary>
            public readonly int LastTabIndex;

            /// <summary>
            /// Where the line stops before the last whitespace
            /// </summary>
            public readonly int Stop;

            /// <summary>
            /// The number of space characters from LastTabIndex to Stop
            /// </summary>
            public readonly int NumSpace;
#if ALT_SPACE_COLUMN_HANDELING
            public int TotalNrSpace
            {
                get
                {
                    int ret = NumSpace;
                    if (Spaces != null)
                    {
                        for (int c = 0; c < Spaces.Length; c++)
                            ret += Spaces[c].nSpace;
                    }
                    return ret;
                }
            }
#endif

            public TWInfo(int last, int stop, int n)
            {
                LastTabIndex = last;
                Stop = stop;
                NumSpace = n;
#if ALT_SPACE_COLUMN_HANDELING
                Spaces = null;
#endif
            }
#if ALT_SPACE_COLUMN_HANDELING
            public readonly TWSpace[] Spaces;

            public TWInfo(int last, int stop, int n, TWSpace[] spaces)
            {
                LastTabIndex = last;
                Stop = stop;
                NumSpace = n;
                Spaces = spaces;
            }

            public class TWSpace
            {
                public readonly int Start, End;

                /// <summary>
                /// Number of spaces _after_ this stub
                /// </summary>
                public readonly int nSpace;

                /// <summary>
                /// Number of consecutive space chacters
                /// </summary>
                public int Length { get { return End - Start + 1; } }

                public TWSpace(int start, int end, int n)
                { Start = start; End = end; nSpace = n; }
            }
#endif
        }

        #endregion
    }
}