using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Render.Commands;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Render;
using PdfLib.Render.Font;
using PdfLib.Pdf.Font;

namespace PdfLib.Compose.Text
{
    public enum BlockAlign
    {
        /// <summary>
        /// The item will be put on the baseline
        /// </summary>
        Baseline,

        /// <summary>
        /// The item will be but on the baseline,
        /// - decent.
        /// </summary>
        Bottom
    }

    /// <summary>
    /// Information about a block
    /// </summary>
    /// <remarks>If this class is made mutable it needs top be cloned in the "Clone" and "Append" function of chLine</remarks>
    class BlockItem
    {
        /// <summary>
        /// Item that will be drawn
        /// </summary>
        public readonly PdfXObject Item;

        /// <summary>
        /// Size of the item, exact sizing depends on Align
        /// </summary>
        public readonly double Width, Height;

        /// <summary>
        /// How this block is to be positioned
        /// </summary>
        public readonly BlockAlign Align;

        public BlockItem(PdfXObject item, double width, double height, BlockAlign align)
        { Item = item; Width = width; Height = height; Align = align; }
    }

    /// <summary>
    /// String that will encode it's data into one the font
    /// understands
    /// </summary>
    public class BuildString : PdfItem
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.String; } }

        /// <summary>
        /// The font that this string will be made compatible for
        /// </summary>
        private readonly cFont _font;

        /// <summary>
        /// The string that will be encoded.
        /// </summary>
        private readonly string _str;

        #endregion

        #region Init

        public BuildString(string str, cFont font)
        { _font = font; _str = str; }

        #endregion

        public PdfString MakeString()
        {
            return new PdfString(_font.Encode(_str), false, false);
        }

        #region Required overrides

        public override int GetInteger() { throw new NotSupportedException(); }
        public override double GetReal() { throw new NotSupportedException(); }

        //For this function one must first encode _str into raw bytes, as _str is
        //a unicode string and GetString() don't really expect that.
        public override string GetString() { throw new NotSupportedException(); }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public override PdfItem Deref() { return this; }

        /// <summary>
        /// Strings are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        /// <summary>
        /// Writes itself to the stream
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// String representation of the string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return _str;
        }

        #endregion
    }

    class TJ_BuildCMD : RenderCMD, ToUnicode
    {
        readonly PdfItem[] _text;
        internal override CMDType Type { get { return CMDType.Text; } }
        public TJ_BuildCMD(PdfItem[] text) { _text = text; }
        internal override void Execute(IDraw draw)
        {
            object[] text = new object[_text.Length];
            for (int c = 0; c < _text.Length; c++)
                if (_text[c] is BuildString)
                    text[c] = ((BuildString)_text[c]).MakeString();
                else
                    text[c] = _text[c].GetReal();

            draw.DrawString(text);
        }
        double[] ToUnicode.Execute()
        {
            var dbls = new List<double>(_text.Length);
            double last = 0;
            foreach (var str in _text)
            {
                if (str is PdfString)
                {
                    dbls.Add(last);
                    last = 0;
                }
                else 
                    last += str.GetReal();
            }
            return dbls.ToArray();
        }
        string[] ToUnicode.Execute(rFont font)
        {
            var l = new List<string>(_text.Length);
            foreach (var str in _text)
            {
                if (str is BuildString)
                    l.Add(font.GetUnicode(((BuildString)str).MakeString().ByteString));
            }
            return l.ToArray();
        }
        string[] ToUnicode.Execute(rCMap map)
        {
            var l = new List<byte[]>(_text.Length);
            foreach (var str in _text)
            {
                if (str is BuildString)
                    l.Add(((BuildString)str).MakeString().ByteString);
            }
            return TJ_CMD.Execute(map, l.ToArray());
        }
        string[] ToUnicode.Execute(int[] map)
        {
            var l = new List<byte[]>(_text.Length);
            foreach (var str in _text)
            {
                if (str is BuildString)
                    l.Add(((BuildString)str).MakeString().ByteString);
            }
            return TJ_CMD.Execute(map, l.ToArray());
        }
    }

    class Tj_BuildCMD : RenderCMD, ToUnicode
    {
        readonly BuildString _text;
        internal override CMDType Type { get { return CMDType.Text; } }
        public Tj_BuildCMD(BuildString text) { _text = text; }
        internal override void Execute(IDraw draw)
        {
            draw.DrawString(_text.MakeString(), false);
        }
        string[] ToUnicode.Execute(rFont font)
        {
            return new string[] { font.GetUnicode(_text.MakeString().ByteString) };
        }
        string[] ToUnicode.Execute(rCMap map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.MakeString().ByteString });
        }
        string[] ToUnicode.Execute(int[] map)
        {
            return TJ_CMD.Execute(map, new byte[][] { _text.MakeString().ByteString });
        }
        double[] ToUnicode.Execute()
        {
            return new double[] { 0 };
        }
    }

    /// <summary>
    /// Unlike the normal set font command this one will create a PdfFont
    /// out of a cFont.
    /// </summary>
    class Tf_BuildCMD : RenderCMD
    {
        internal override CMDType Type { get { return CMDType.State; } }
        private cFont _font;
        private double _size;
        public Tf_BuildCMD(cFont font, double size)
        { _font = font; _size = size; }
        internal override void Execute(Render.IDraw draw)
        {
            draw.SetFont(_font.MakeWrapper(), _size);
        }
    }

    /// <summary>
    /// Used to construct the space meta data
    /// </summary>
    struct SpacePosition
    {
        public readonly int CommandPos;
        public readonly int ArrayPos;
        public readonly int Count;
        public SpacePosition(int cmd, int pos, int count)
        { CommandPos = cmd; ArrayPos = pos; Count = count; }
    }

    public class CompiledLine : IExecutableImpl
    {
        internal readonly RenderCMD[] Text;
        internal readonly cBlock[] Blocks;

        internal CompiledLine(RenderCMD[] text, cBlock[] blocks)
        {
            Text = text;
            Blocks = blocks;
        }

        public CompiledLine Join(CompiledLine cl)
        {
            return new CompiledLine(
                Util.ArrayHelper.Join<RenderCMD>(Text, cl.Text),
                Util.ArrayHelper.Join<cBlock>(Blocks, cl.Blocks)
            );
        }

        internal static CompiledLine Join(IEnumerable<object> lines)
        {
            int size_cmds = 0, size_blocks = 0;
            foreach (var c in lines)
            {
                if (c is CompiledLine)
                {
                    var cl = (CompiledLine)c;
                    if (cl.Text != null)
                        size_cmds += cl.Text.Length;
                    if (cl.Blocks != null)
                        size_blocks += cl.Blocks.Length;
                }
                else if (c is RenderCMD)
                    size_cmds++;
            }

            var text = size_cmds > 0 ? new RenderCMD[size_cmds] : null;
            var blocks = size_blocks > 0 ? new cBlock[size_blocks] : null;
            size_cmds = 0;
            size_blocks = 0;

            foreach (var c in lines)
            {
                if (c is CompiledLine)
                {
                    var cl = (CompiledLine)c;
                    if (cl.Text != null)
                    {
                        Array.Copy(cl.Text, 0, text, size_cmds, cl.Text.Length);
                        size_cmds += cl.Text.Length;
                    }
                    if (cl.Blocks != null)
                    {
                        Array.Copy(cl.Blocks, 0, blocks, size_blocks, cl.Blocks.Length);
                        size_blocks += cl.Blocks.Length;
                    }
                }
                else if (c is RenderCMD)
                    text[size_cmds++] = (RenderCMD) c;
            }

            return new CompiledLine(text, blocks);
        }

        #region IExecutable

        RenderCMD[] IExecutableImpl.Commands { get { return Text; } }

        #endregion
    }

    /// <summary>
    /// This class contains meta informatino about the words or images of a string.
    /// </summary>
    /// <remarks>
    /// The curent implementation works like this:
    ///  - Words are meassured. This registeres characters in the font
    ///  - Measuring data is discarted
    ///  - When rendering all measurments are done again.
    ///  - PDF commands are outputted.
    ///  
    /// The problem with this aproach is that one may want quick reflowing of text.
    /// With the current model all work has to be redone when one reflow.
    /// 
    /// Changing this from a "class" to a "stuct" and including the width of the
    /// object (sans Tc, Tw) would make faster reflwoing possible. One could also
    /// forgo doing remeasurments when rendering then*.
    /// 
    /// * However underlines are currently only measured out when rendering so one
    ///   would have to figure out something clever there.
    ///   
    /// Note If measure data is to be cached here it needs to be invalidated whenever
    /// Start/End or Meta is changed.
    /// </remarks>
    internal class Word : ICloneable
    {
        /// <summary>
        /// First character index of the word
        /// </summary>
        public int Start;

        /// <summary>
        /// Last character index of the word
        /// </summary>
        public int End;

        /// <summary>
        /// The type of word
        /// </summary>
        public readonly WType Type;

        /// <summary>
        /// Metadata for this word
        /// </summary>
        /// <remarks>
        /// Only used for blocks and tabs.
        /// </remarks>
        public object Meta;

        /// <summary>
        /// Number of characters in the word
        /// </summary>
        public int Length { get { return End - Start + 1; } }

        public Word(int start, int end, WType type, object meta)
        { Start = start; End = end; Type = type; Meta = meta; }
        public Word(int start, int end, WType type)
        { Start = start; End = end; Type = type; Meta = null; }

        public bool Contain(int pos)
        {
            return Start <= pos && pos <= End;
        }

        public override bool Equals(object obj)
        {
            return obj is Word && Equals((Word) obj);
        }

        public bool Equals(Word w)
        {
            return w != null && Type == w.Type && ReferenceEquals(w.Meta, Meta);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// Shifts the position of the word
        /// </summary>
        internal void Shift(int n)
        { Start += n; End += n; }
        public override string ToString()
        {
            string type;
            if (Type == WType.SPACE)
                type = "Space";
            else if (Type == WType.WORD)
                type = "Word";
            else if (Type == WType.TAB)
                type = "Tab";
            else
                type = "Block";
            return string.Format("{0}: {1} - {2}", type, Start, End);
        }

        public object Clone()
        {
            return MemberwiseClone();
        }

        public enum WType
        {
            WORD,
            SPACE,
            TAB,
            BLOCK
        }
    }

    /// <summary>
    /// Used to pass information in a manner that easier to read
    /// </summary>
    /// <typeparam name="T">The desired stylerange</typeparam>
    internal class AffectedRanges<T>
    {
        /// <summary>
        /// Pointer to the first affected range
        /// </summary>
        public T First;

        /// <summary>
        /// Pointer to the last affected range
        /// </summary>
        public T Last;

        /// <summary>
        /// Pointer to the range pointintg at "First" 
        /// </summary>
        public T Ptr;

        public AffectedRanges(T first, T last, T ptr)
        {
            First = first;
            Last = last;
            Ptr = ptr;
        }
    }

    /// <summary>
    /// Can probably be deprecated in favor of xIntRange. 
    /// </summary>
    [DebuggerDisplay("{Start} - {End}")]
    internal struct IntRange
    {
        public int Start;
        public int End;
        public int Length { get { return End - Start + 1; } }
        public IntRange(int start, int end) { Start = start; End = end; }
        public xIntRange xRange { get { return new xIntRange(Start, End); } }
    }

    /// <summary>
    /// Measurments of a font
    /// </summary>
    public struct FontMeasure
    {
        /// <summary>
        /// Metrics
        /// </summary>
        public double LineHeight, CapHeight, LineAscent, LineDescent;

        /// <summary>
        /// The size when the measure was taken
        /// </summary>
        public double Size;

        /// <summary>
        /// Text rise when this measure was taken
        /// </summary>
        public double TextRise;
    }

    /// <summary>
    /// When text lines are measured this class is filled out
    /// with various measurments taken of that line.
    /// </summary>
    public class LineMeasure
    {
        /// <summary>
        /// Total width of the line
        /// </summary>
        public double Width;

        /// <summary>
        /// Index of the last character measured
        /// </summary>
        public int LastCharIndex;

        /// <summary>
        /// Maximum and minimum height, as measured
        /// </summary>
        public double YMax, YMin;

        /// <summary>
        /// Height information given by the font with the tallest
        /// character on the line
        /// </summary>
        public FontMeasure Font;
        
        /// <summary>
        /// Left and right side bearings of the first non-whitespace character on the line
        /// </summary>
        public double FirstLSB, FirstRSB;

        /// <summary>
        /// Left and right side bearings of the last non-whitespace character on the line
        /// </summary>
        public double LastLSB, LastRSB;

        /// <summary>
        /// Whitespace preceeding and trailing the line, does not include LSB/RSB
        /// </summary>
        public double Preceeding, Trailing;

        /// <summary>
        /// Append this character to the line.
        /// </summary>
        /// <remarks>
        /// Note that there's no need to do anything in the drawing code to handle this character. It's
        /// always appended "in the middle" of a "single styled" word, so style, color, etc. will effectivly
        /// be inherited by the append character without anything needed to be done.
        /// 
        /// Also note, simply appending a character without the above asumption may lead to very subtle
        /// bugs. 
        /// </remarks>
        public char? AppendChar;

        internal LineMeasure() { }

        /// <summary>
        /// Sets the line measurments
        /// </summary>
        /// <param name="width">Physical width</param>
        /// <param name="last_ch_idx">Index of the last character</param>
        /// <param name="ymax">Maximum height</param>
        /// <param name="ymin">Minimum height</param>
        /// <param name="line_height">Line height fetched from the font</param>
        /// <param name="text_rise">state.Tr</param>
        /// <param name="ascent">Maximum height from  the font</param>
        /// <param name="descent">Minimum height from the font</param>
        internal LineMeasure Set(double width, int last_ch_idx, FontMeasure font_measure)
        {
            Width = width;
            LastCharIndex = last_ch_idx;
            Font = font_measure;

            return this;
        }
    }
}
