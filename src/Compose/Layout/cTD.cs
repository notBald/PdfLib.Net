using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// Table Data
    /// </summary>
    public class cTD : cDiv
    {
        /// <summary>
        /// How many colums spanned by this cell
        /// </summary>
        private int _col_span = 1;

        /// <summary>
        /// How many rows spanned by this cell
        /// </summary>
        private int _row_span = 1;

        /// <summary>
        /// Index of column this cell starts on when spanning
        /// multiple rows/columns.
        /// </summary>
        /// <remarks>Using integer index, since it's easier to do math
        /// and such with numbers and we can get a reference to the parent
        /// table through Parent</remarks>
        internal int StartColumn, StartRow;

        /// <summary>
        /// How many colums spanned by this cell
        /// </summary>
        public int ColSpan
        {
            get { return _col_span; }
            set 
            {
                if (value != _col_span)
                {
                    _col_span = Math.Max(1, value);
                    DoLayoutContent = true;
                }
            }
        }

        /// <summary>
        /// How many rows spanned by this cell
        /// </summary>
        public int RowSpan
        {
            get { return _row_span; }
            set 
            {
                if (value != _row_span)
                {
                    _row_span = Math.Max(1, value);
                    DoLayoutContent = true;
                }
            }
        }

        #region Init

        public cTD()
            : this(new cStyle())
        { }
        public cTD(cStyle style)
            : base(style)
        {
            Style.AddDefault("Display", eDisplay.Inline);
        }

        #endregion

        internal new void Render(IDraw draw, ref cRenderState state, cBox anchor, bool draw_border)
        {
            base.Render(draw, ref state, anchor, draw_border);
        }

        internal new void LayoutIfChangedNoCalc(cBox anchor)
        {
            base.LayoutIfChangedNoCalc(anchor);
        }

        internal new void CalcBorders()
        {
            base.CalcBorders();
        }

        internal void Layout(double width, double height, cBox anchor)
        {
            base.LayoutNoCalc(width, height, anchor);
        }
    }
}
