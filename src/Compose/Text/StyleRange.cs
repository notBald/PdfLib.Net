using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Compose.Text
{
    internal abstract class StyleRange<T>
        where T : StyleRange<T>, new()
    {
        #region Variables and properties

        /// <summary>
        /// The beginning and the end of this range
        /// </summary>
        public int Start, End;

        /// <summary>
        /// The next non-overlapping range
        /// </summary>
        public T Next;

        /// <summary>
        /// How many characters this range covers
        /// </summary>
        public int Length { get { return End - Start + 1; } }

        /// <summary>
        /// If this color range equals the the default range
        /// </summary>
        public abstract bool Default { get; }// { throw new NotSupportedException(); } }

        /// <summary>
        /// If this equals that other object
        /// </summary>
        public abstract bool Equals(T sr);// { throw new NotSupportedException(); }

        #endregion

        #region Init

        //public StyleRange() { }

        #endregion

        #region Position

        /// <summary>
        /// Shifts the position of this style
        /// </summary>
        public void Shift(int length)
        {
            Start += length;
            End += length;
        }

        /// <summary>
        /// Returns the last style in this chain
        /// </summary>
        public StyleRange<T> Last()
        {
            if (Next == null) return this;
            StyleRange<T> last = Next, next = last.Next;
            while (next != null)
            {
                last = next;
                next = next.Next;
            }
            return last;
        }

        /// <summary>
        /// Checks if this range intersects with the given range
        /// </summary>
        public bool Intersect(int start, int end)
        { return Start <= end && start <= End; }

        /// <summary>
        /// If this range is after end
        /// </summary>
        public bool After(int end) { return Start > end; }

        /// <summary>
        /// Tests if the next color range is right next to this range
        /// </summary>
        /// <param name="next">ColorRange after this range</param>
        /// <returns>Next is right after this range</returns>
        public bool Adjacent(T next)
        { return End + 1 == next.Start; }

        /// <summary>
        /// If this range is before start
        /// </summary>
        public bool Before(int start) { return End < start; }
        public bool OverlapsBefore(T range) { return Start <= range.Start; }

        /// <summary>
        /// If this range overlap the given range
        /// </summary>
        public bool Overlaps(T range)
        { return Start <= range.Start && range.End <= End; }

        #endregion

        #region Other

        /// <summary>
        /// Makes a copy of a range that can be modified
        /// </summary>
        public abstract T Copy(int start, int end);
        public T Copy() { return Copy(Start, End); }

        /// <summary>
        /// For debuging
        /// </summary>
        public override string ToString() { return ToString(0); }
        private string ToString(int r)
        {
            if (r == 5) return "...";
            return string.Format("[{0} - {1}] -> {2}", Start, End, (Next == null) ? "null" : Next.ToString(++r));
        }

        #endregion
    }

    internal sealed class FontRange : StyleRange<FontRange>
    {
        #region Variables and properties

        public cFont Font;

        /// <summary>
        /// Since one generally wish to change to font size along with the text rise,
        /// it's natural that TextRise is part of the font. This has a nice side
        /// effect of preventing kerning between risen and non-risen text.
        /// </summary>
        public double? Size, TextRise;

        public override bool Default
        {
            get { return Font == null && Size == null && TextRise == null; }
        }

        #endregion

        #region Init

        public FontRange() { }

        public FontRange(int start, int end, cFont font, double? size, double? text_rise)
        {
            Start = start; End = end; Font = font; Size = size; TextRise = text_rise;
        }

        #endregion

        #region Required overrides

        public override FontRange Copy(int start, int end)
        {
            return new FontRange(start, end, Font, Size, TextRise);
        }

        public override bool Equals(FontRange sr)
        {
            return ReferenceEquals(Font, sr.Font) && Size == sr.Size && TextRise == sr.TextRise;
        }

        #endregion
    }

    /// <summary>
    /// This class tracks color information, including clipping
    /// and underlining of text.
    /// </summary>
    /// <remarks>
    /// Color ranges are intended for linked list structures.
    /// </remarks>
    sealed class ColorRange : StyleRange<ColorRange>
    {
        #region Variables and properties

        /// <summary>
        /// Fill and stroke color in this color range
        /// </summary>
        public cBrush Fill, Stroke;

        /// <summary>
        /// If there's a underline stroke in this color range
        /// </summary>
        public double? Underline;

        /// <summary>
        /// Text rendering mode
        /// </summary>
        public xTextRenderMode? RenderMode;

        /// <summary>
        /// If there is a color set in this range
        /// </summary>
        public bool HasColor
        { get { return Fill != null || Stroke != null; } }

        /// <summary>
        /// If this color range equals the the default range
        /// </summary>
        public override bool Default
        { get { return Fill == null && Stroke == null && Underline == null && RenderMode == null; } }

        #endregion

        #region Init

        /// <summary>
        /// Creates a new range
        /// </summary>
        public ColorRange(int start, int end, cBrush fill, cBrush stroke, double? underline, xTextRenderMode? render_mode)
        { Fill = fill; Stroke = stroke; Start = start; End = end; Underline = underline; RenderMode = render_mode; }
        public ColorRange(int start, int end, cBrush fill, cBrush stroke)
        { Fill = fill; Stroke = stroke; Start = start; End = end; Underline = null; RenderMode = null; }
        public ColorRange(int start, int end, double? underline, xTextRenderMode? render_mode)
        { Fill = null; Stroke = null; Start = start; End = end; Underline = underline; RenderMode = render_mode; }
        public ColorRange(int start, int end, double? underline)
        { Fill = null; Stroke = null; Start = start; End = end; Underline = underline; RenderMode = null; }
        public ColorRange(int start, int end, xTextRenderMode? render_mode)
        { Fill = null; Stroke = null; Start = start; End = end; Underline = null; RenderMode = render_mode; }
        public ColorRange(int start, int end)
        { Fill = null; Stroke = null; Underline = null; RenderMode = null; }
        public ColorRange()
        { Fill = null; Stroke = null; Underline = null; RenderMode = null; }

        #endregion

        #region Equals

        /// <summary>
        /// True if the this range fill, stroke, clip and underline
        /// are equal
        /// </summary>
        public override bool Equals(ColorRange r)
        {
            return ReferenceEquals(r.Fill, Fill) && ReferenceEquals(r.Stroke, Stroke)
                && r.Underline == Underline && r.RenderMode == RenderMode;
        }

        /// <summary>
        /// Checks if two ranges have the same color
        /// </summary>
        public bool EqualsColor(ColorRange r)
        {
            return (ReferenceEquals(Fill, r.Fill) || Fill != null && Fill.Equals(r.Fill))
                && (ReferenceEquals(Stroke, r.Stroke) || Stroke != null && Stroke.Equals(r.Stroke));
        }

        #endregion

        #region Other

        /// <summary>
        /// Makes a copy of a range that can be modified
        /// </summary>
        public override ColorRange Copy(int start, int end)
        { return new ColorRange(start, end, Fill, Stroke, Underline, RenderMode); }

        #endregion
    }
}
