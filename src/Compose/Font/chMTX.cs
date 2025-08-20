using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Helper class for creating a PDF CID width array
    /// </summary>
    internal class chMTX
    {
        #region Variables and properties

        List<wRange> _ranges = new List<wRange>(100);

        #endregion

        #region Init

        #endregion

        /// <summary>
        /// Creates an array of widths trunctuacted to interger size
        /// </summary>
        public PdfArray CreateInt()
        {
            //There are two formats. One for when each charater has it's own
            //range, and the other for when they share ranges. Currently only
            //the former is used. 
            var items = new PdfItem[2 * _ranges.Count];
            int items_pos = 0;
            foreach (var range in _ranges)
            {
                items[items_pos++] = new PdfInt(range.Start);
                var widths = new PdfItem[range.Length];
                int widths_pos = 0;
                foreach (var w in range.Widths)
                    widths[widths_pos++] = new PdfInt((int)(w * 1000));
                items[items_pos++] = new RealArray(widths);
            }

            return new TemporaryArray(items);
        }

        /// <summary>
        /// Creates an array of widths
        /// </summary>
        public PdfArray CreateReal(uint prec)
        {
            //There are two formats. One for when each charater has it's own
            //range, and the other for when they share ranges. Currently only
            //the former is used. 
            var items = new PdfItem[2 * _ranges.Count];
            int items_pos = 0;
            double mul = Math.Pow(10, prec);
            foreach (var range in _ranges)
            {
                items[items_pos++] = new PdfInt(range.Start);
                var widths = new PdfItem[range.Length];
                int widths_pos = 0;
                foreach (var w in range.Widths)
                    widths[widths_pos++] = new PdfReal(Math.Truncate(w * 1000 * mul) / mul);
                items[items_pos++] = new RealArray(widths);
            }

            return new TemporaryArray(items);
        }


        /// <summary>
        /// Adds a character to be mapped
        /// </summary>
        /// <param name="ch">Character to map</param>
        /// <param name="w">Width of the character</param>
        public void Add(int ch, double w)
        {
            int count = _ranges.Count;
            if (count == 0)
                _ranges.Add(new wRange(ch, w));
            else
            {
                var last = _ranges[count - 1];
                if (last.End + 1 == ch)
                    last.Add(ch, w);
                else
                    _ranges.Add(new wRange(ch, w));
            }
        }

        class wRange
        {
            public int Start, End;
            public List<double> Widths;
            public int Length { get { return End - Start + 1; } }
            public wRange(int start, double width)
            { Start = End = start; Widths = new List<double>(16) { width }; }
            public void Add(int ch, double w)
            {
                End = ch;
                Widths.Add(w);
            }
            public override string ToString()
            {
                if (Length == 1)
                    return string.Format("{0} ({1}): {2}", (char) Start, End, Widths[0]);
                else
                    return string.Format("{0} - {1} ({2} - {3})", (char)Start, (char) End, Start, End);
            }
        }
    }
}
