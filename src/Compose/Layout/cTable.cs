using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Render;
using PdfLib.Pdf.Primitives;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// A table
    /// </summary>
    /// <remarks>
    /// Instead of supporting  style Display: table/row/cell, I break
    /// this out into independet classes.
    /// 
    /// Advantage: no need to support table/row/cell in odd places, nor
    /// can the user switch a cell from normal layout to table layout.
    /// </remarks>
    public class cTable : cBox
    {
        /// <summary>
        /// Linked list of row objects
        /// </summary>
        private cTR _FirstRow, _LastRow;
        public cTR FirstRow { get { return _FirstRow; } }
        public cTR LastRow { get { return _LastRow; } }

        /// <summary>
        /// A grid over occopied table cells
        /// </summary>
        private BlockList[] _Rows, _Columns;

        /// <summary>
        /// Distance between the cells
        /// </summary>
        private double VisCellSpacing;

        /// <summary>
        /// Used for collapsed borders
        /// </summary>
        private AllBorders _borders;
        
        bool _variable_content;

        private bool _contains_text;

        private xSize _content_size, _min_content_size;

        internal override bool ContainsText { get { return _contains_text; } }

        /// <summary>
        /// I've not investigated how tables handles descent. 
        /// </summary>
        internal override double MaxDescent => 0;

        public override xSize ContentSize
        {
            get { return _content_size; }
        }

        public override xSize MinContentSize
        {
            get { return _min_content_size; }
        }

        public IEnumerable<cTR> Rows
        {
            get
            {
                var next = _FirstRow;
                while (next != null)
                {
                    yield return next;
                    next = next.VisNext as cTR;
                }
            }
        }

        public IEnumerable<cTD> Children
        {
            get
            {
                if (_Rows == null)
                {
                    foreach (var tr in Rows)
                    {
                        foreach (var td in tr)
                        {
                            if (td is cTD)
                                yield return (cTD)td;
                        }
                    }
                }
                else
                {
                    for (int x = 0; x < _Columns.Length; x++)
                    {
                        var col = _Columns[x];

                        for (int y = 0; y < _Rows.Length; y++)
                        {
                            var cell = col[y];
                            if (cell != null && cell.StartColumn == x && cell.StartRow == y)
                                yield return cell;
                        }
                    }
                }
            }
        }

        #region Init

        public cTable()
            : this(new cStyle())
        { }
        public cTable(cStyle style)
            : base(style)
        { }

        #endregion

        #region Layout

        /// <summary>
        /// Builds up the "table" data structure
        /// </summary>
        private void MakeTable()
        {
            List<BlockList> columns = new List<BlockList>(4);
            List<BlockList> rows = new List<BlockList>(4);
            int col_nr = 0, row_nr = 0;
            _variable_content = false;

            foreach (var tr in Rows)
            {
                col_nr = 0;

                foreach (cTD td in tr.Columns)
                {
                    td.StartColumn = col_nr;

                    for (int c = 0; c < td.ColSpan; c++)
                    {
                        BlockList column;
                        if (col_nr == columns.Count)
                        {
                            column = new BlockList(4);
                            columns.Add(column);
                        }
                        else
                            column = columns[col_nr];

                        td.StartRow = row_nr;
                        column.add(row_nr, td);

                        for (int r = 0; r < td.RowSpan; r++)
                        {
                            BlockList row;
                            if (row_nr + r == rows.Count)
                            {
                                row = new BlockList(4);
                                rows.Add(row);
                            }
                            else
                                row = rows[row_nr + r];

                            row.add(col_nr, td);
                        }

                        col_nr++;
                    }
                }

                row_nr++;
            }

            _Rows = rows.ToArray();
            _Columns = columns.ToArray();

            foreach (var row in _Rows)
                row.Trim(col_nr);
            foreach (var col in _Columns)
                col.Trim(row_nr);
        }

        internal override bool VariableContentSize
        {
            get { return _variable_content; }
        }

        /// <summary>
        /// Get the width of the cols the TD span.
        /// 
        /// Note, "Normal" assumes that BorderOne and BorderTwo = 0
        /// </summary>
        private double GetColsWidth(cTD td)
        {
            double d = 0;
            int end = td.StartColumn + td.ColSpan;
            for (int c = td.StartColumn; c < end; c++)
            {
                var col = _Columns[c];
                d += col.MinSize + col.BorderOne + col.BorderTwo;
            }
            return d;
        }

        private double GetRowsHeight(cTD td)
        {
            double d = 0;
            int end = td.StartRow + td.RowSpan;
            for (int c = td.StartRow; c < end; c++)
                d += _Rows[c].MinSize;
            return d;
        }

        /// <summary>
        /// Tells each cell to lay itself out
        /// </summary>
        private void LayoutTD(cTextStat stats)
        {
            _variable_content = false;
            for (int x = 0; x < _Columns.Length; x++)
            {
                var col = _Columns[x];

                for (int y = 0; y < _Rows.Length; y++)
                {
                    var cell = col[y];
                    if (cell != null && cell.StartColumn == x && cell.StartRow == y)
                    {
                        cell.LayoutIfChanged(this);
                        if (cell.SizeDependsOnParent)
                            _variable_content = true;
                        cell.AddStats(stats);
                    }
                }
            }
        }

        /// <summary>
        /// Calculates all borders
        /// </summary>
        /// <remarks>
        /// Note, assumes that the "MakeTable" method has been called first.
        /// This means that to run this on a dirty table the BorderOne/BorderTwo
        /// values on _Columns must be reset. "_Rows" will be effectivly reset, so 
        /// they can be left dirty.
        /// </remarks>
        private void CalcBorderWidths()
        {
            //Outer border
            base.CalcBorders();
            if (_Rows == null || _Rows.Length == 0)
                return;
            _Rows[0].BorderOne = VisBorderTop;
            _Rows[_Rows.Length - 1].BorderTwo = VisBorderBottom;
            BlockList col_first = _Columns[0], col_last = _Columns[_Columns.Length - 1];
            col_first.BorderOne = VisBorderLeft;
            col_last.BorderTwo = VisBorderRight;

            int y = 0, max_y = _Rows.Length - 1, max_x = _Columns.Length - 1;
            foreach (var tr in Rows)
            {
                if (y >= _Rows.Length)
                    break;

                tr.CalcBorders();
                var row = _Rows[y];
                row.BorderOne = Math.Max(tr.VisBorderTop, row.BorderOne);
                row.BorderTwo = Math.Max(tr.VisBorderBottom, row.BorderTwo);
                col_first.BorderOne = Math.Max(tr.VisBorderLeft, col_first.BorderOne);
                col_last.BorderTwo = Math.Max(tr.VisBorderRight, col_last.BorderTwo);

                for (int x = 0; x < _Columns.Length; x++)
                {
                    var td = row[x];

                    if (td != null && td.StartColumn == x && td.StartRow == y)
                    {
                        td.CalcBorders();

                        row.BorderOne = Math.Max(td.VisBorderTop, row.BorderOne);
                        BlockList bottom_row = td.RowSpan != 1 ? _Rows[Math.Min(y + td.RowSpan - 1, max_y)] : row;
                        bottom_row.BorderTwo = Math.Max(td.VisBorderBottom, bottom_row.BorderTwo);

                        var col_left = _Columns[x];
                        col_left.BorderOne = Math.Max(td.VisBorderLeft, col_left.BorderOne);
                        var col_right = _Columns[Math.Min(x + td.ColSpan - 1, max_x)];
                        col_right.BorderTwo = Math.Max(td.VisBorderRight, col_right.BorderTwo);
                    }
                }
            }
        }

        /// <summary>
        /// Finds the max height of horizontal borders
        /// </summary>
        /// <remarks>
        /// CalcBorderWidths could have been implemented the same way, or simply do this
        /// in the CollapseBorders function.
        /// </remarks>
        private void CalcBorderHeights(LineStubs[] rows)
        {
#if PADD_CONTENT
            double cur = rows[0].MaxStrokeWidth, next = 0;
#else
            double cur = rows[0].MaxStrokeWidth/2, next = 0;
#endif
            for (int y = 0; y < _Rows.Length; y++)
            {
                next = rows[y + 1].MaxStrokeWidth / 2;
                var row = _Rows[y];
                row.BorderOne = cur;
                row.BorderTwo = next;
                cur = next;
            }
#if PADD_CONTENT
            _Rows[_Rows.Length - 1].BorderTwo += cur;
#endif
        }

        private void CollapseBorderWidths()
        {
            var last = _Columns[0];

#if !PADD_CONTENT
            //We must divide the borders along the edges by two, as the other
            //half is "on the table"
            last.BorderOne /= 2;
            _Columns[_Columns.Length - 1].BorderTwo /= 2;
#endif

            for (int x = 1; x < _Columns.Length; x++)
            {
                var current = _Columns[x];
                double w = Math.Max(current.BorderOne, last.BorderTwo) / 2;
                current.BorderOne = last.BorderTwo = w;
            }
        }

        /// <summary>
        /// Rules:
        ///  - Thickest border has priority
        ///  - Followed by what border comes first
        ///  
        /// So for instance, 
        /// </summary>
        /// <remarks>
        /// It can be tempting to modify "VisBorderLeft/Right/etc", but that will much up
        /// height calculation when height = border box
        /// 
        /// We also know that we got at least one row in the table, otherwise this method isn't called
        /// </remarks>
        private AllBorders CollapseBorders()
        {
            var all = new AllBorders(_Columns.Length, _Rows.Length);
            Type ctd = typeof(cTD);

            //Collapse rows
            {
                var borders = all.Rows;
                var tr = _FirstRow;

                //First row is special, as it does not have a row above it and it borders the parent's top border
                {
                    var top_stubs = MakeStubs(_Rows[0], 0, true, false, _Columns.Length, ctd, "Top", "ColSpan");
                    CombineStubs(top_stubs, new LineStub(tr.VisBorderTop, tr.BorderColorTop, tr.DashTop));
                    CombineStubs(top_stubs, new LineStub(VisBorderTop, BorderColorTop, DashTop));
                    borders[0] = top_stubs;
                }

                int end = _Rows.Length - 1;

                for (int row_nr = 1; row_nr <= end; row_nr++)
                {
                    var bottom_stubs = MakeStubs(_Rows[row_nr - 1], row_nr - 1, true, true, _Columns.Length, ctd, "Bottom", "ColSpan");
                    var top_stubs = MakeStubs(_Rows[row_nr], row_nr, true, false, _Columns.Length, ctd, "Top", "ColSpan");
                    CombineStubs(bottom_stubs, top_stubs);
                    CombineStubs(bottom_stubs, new LineStub(tr.VisBorderBottom, tr.BorderColorBottom, tr.DashBottom));
                    tr = (cTR)tr.VisNext;
                    CombineStubs(bottom_stubs, new LineStub(tr.VisBorderTop, tr.BorderColorTop, tr.DashTop));

                    borders[row_nr] = bottom_stubs;
                }

                //Bottom row is again special
                {
                    var bottom_stubs = MakeStubs(_Rows[end], end, true, true, _Columns.Length, ctd, "Bottom", "ColSpan");
                    System.Diagnostics.Debug.Assert(ReferenceEquals(tr, _LastRow));
                    CombineStubs(bottom_stubs, new LineStub(tr.VisBorderBottom, tr.BorderColorBottom, tr.DashBottom));
                    CombineStubs(bottom_stubs, new LineStub(VisBorderBottom, BorderColorBottom, DashBottom));
                    borders[_Rows.Length] = bottom_stubs;
                }
            }

            //Collapse columns
            {
                var borders = all.Columns;

                //First column
                {
                    var left_stubs = MakeStubs(_Columns[0], 0, false, false, _Rows.Length, ctd, "Left", "RowSpan");
                    LineStubs row_stubs = new LineStubs(_Rows.Length);
                    int count = 0;
                    foreach (var tr in Rows)
                        row_stubs[count++] = new LineStub(tr.VisBorderLeft, tr.BorderColorLeft, tr.DashLeft);
                    CombineStubs(left_stubs, row_stubs);
                    CombineStubs(left_stubs, new LineStub(VisBorderLeft, BorderColorLeft, DashLeft));
                    borders[0] = left_stubs;
                }

                int end = _Columns.Length - 1;

                for (int col_nr = 1; col_nr <= end; col_nr++)
                {
                    var right_stubs = MakeStubs(_Columns[col_nr - 1], col_nr - 1, false, true, _Rows.Length, ctd, "Right", "RowSpan");
                    var left_stubs = MakeStubs(_Columns[col_nr], col_nr, false, false, _Rows.Length, ctd, "Left", "RowSpan");
                    CombineStubs(right_stubs, left_stubs);

                    borders[col_nr] = right_stubs;
                }

                //Last column
                {
                    var right_stubs = MakeStubs(_Columns[end], end, false, true, _Rows.Length, ctd, "Right", "RowSpan");
                    LineStubs row_stubs = new LineStubs(_Rows.Length);
                    int count = 0;
                    foreach (var tr in Rows)
                        row_stubs[count++] = new LineStub(tr.VisBorderRight, tr.BorderColorRight, tr.DashRight);
                    CombineStubs(right_stubs, row_stubs);
                    CombineStubs(right_stubs, new LineStub(VisBorderRight, BorderColorRight, DashRight));

                    borders[_Columns.Length] = right_stubs;
                }
            }

            //Handle crossings. The points where borders meet.
            {
                LineStubs[] cols = all.Columns;
                LineStubs[] rows = all.Rows;

                //Top row
                {
                    LineStubs row = rows[0];

                    //Top/left corner
                    {
                        var stub_right = row[0];
                        var stub_bottom = cols[0][0];
                        if (stub_right.StrokeWidth >=  stub_bottom.StrokeWidth)
                            stub_right.StartGap = stub_bottom.StartGap = stub_right;
                        else
                            stub_right.StartGap = stub_bottom.StartGap = stub_bottom;

                        stub_right.Sub = (cols[0].MaxStrokeWidth - stub_bottom.StrokeWidth) / 2;
                        stub_bottom.Sub = (row.MaxStrokeWidth - stub_right.StrokeWidth) / 2;
                    }

                    //Top row
                    {
                        for (int x = 1; x < row.Length; x++)
                        {
                            var stub_left = row[x - 1];
                            var stub_right = row[x];
                            var stub_bottom = cols[x][0];
                            LineStub choosen;
                            double hrz_width;
                            if (stub_left.StrokeWidth >= stub_right.StrokeWidth)
                            {
                                hrz_width = stub_left.StrokeWidth;
                                choosen = stub_left.StrokeWidth >= stub_bottom.StrokeWidth ? stub_left : stub_bottom;
                            }
                            else
                            {
                                hrz_width = stub_right.StrokeWidth;
                                choosen = stub_right.StrokeWidth >= stub_bottom.StrokeWidth ? stub_right : stub_bottom;
                            }
                            stub_right.StartGap = stub_bottom.StartGap = choosen;

                            stub_left.Add = stub_right.Sub = (cols[x].MaxStrokeWidth - stub_bottom.StrokeWidth) / 2;
                            stub_bottom.Sub = (row.MaxStrokeWidth - hrz_width) / 2;
                        }
                    }

                    //Top/right corner
                    {
                        var stub_left = row[row.Length - 1];
                        var stub_bottom = cols[row.Length][0];
                        if (stub_left.StrokeWidth >= stub_bottom.StrokeWidth)
                            row.EndGap = stub_bottom.StartGap = stub_left;
                        else
                            row.EndGap = stub_bottom.StartGap = stub_bottom;

                        stub_left.Add = (cols[row.Length].MaxStrokeWidth - stub_bottom.StrokeWidth) / 2;
                        stub_bottom.Sub = (row.MaxStrokeWidth - stub_left.StrokeWidth) / 2;
                    }
                }


                int end_y = rows.Length - 1;

                //Middle rows
                for (int y = 1; y < end_y; y++)
                {
                    var row = rows[y];

                    //Left corner
                    {
                        var stub_right = row[0];
                        var stub_top = cols[0][y - 1];
                        var stub_bottom = cols[0][y];
                        LineStub choosen;
                        if (stub_right.StrokeWidth >= stub_top.StrokeWidth)
                            choosen = stub_right.StrokeWidth >= stub_bottom.StrokeWidth ? stub_right : stub_bottom;
                        else
                            choosen = stub_top.StrokeWidth >= stub_bottom.StrokeWidth ? stub_top : stub_bottom;
                        stub_right.StartGap = stub_bottom.StartGap = choosen;

                        double v_width = stub_top.StrokeWidth >= stub_bottom.StrokeWidth ? stub_top.StrokeWidth : stub_bottom.StrokeWidth;
                        stub_right.Sub = (cols[0].MaxStrokeWidth - v_width) / 2;
                        stub_top.Add = stub_bottom.Sub = (row.MaxStrokeWidth - stub_right.StrokeWidth) / 2;
                    }

                    //Middle row
                    {
                        for (int x = 1; x < row.Length; x++)
                        {
                            var stub_left = row[x - 1];
                            var stub_right = row[x];
                            var stub_top = cols[x][y - 1];
                            var stub_bottom = cols[x][y];
                            LineStub choosen;
                            if (stub_left.StrokeWidth >= stub_right.StrokeWidth)
                            {
                                if (stub_left.StrokeWidth >= stub_top.StrokeWidth)
                                    choosen = stub_left.StrokeWidth >= stub_bottom.StrokeWidth ? stub_left : stub_bottom;
                                else
                                    choosen = stub_top.StrokeWidth >= stub_bottom.StrokeWidth ? stub_top : stub_bottom;
                            }
                            else
                            {
                                if (stub_right.StrokeWidth >= stub_top.StrokeWidth)
                                    choosen = stub_right.StrokeWidth >= stub_bottom.StrokeWidth ? stub_right : stub_bottom;
                                else
                                    choosen = stub_top.StrokeWidth >= stub_bottom.StrokeWidth ? stub_top : stub_bottom;
                            }
                            stub_right.StartGap = stub_bottom.StartGap = choosen;

                            double v_width = stub_top.StrokeWidth >= stub_bottom.StrokeWidth ? stub_top.StrokeWidth : stub_bottom.StrokeWidth;
                            stub_left.Add = stub_right.Sub = (cols[x].MaxStrokeWidth - v_width) / 2;
                            double hrz_width = stub_left.StrokeWidth >= stub_right.StrokeWidth ? stub_left.StrokeWidth : stub_right.StrokeWidth;
                            stub_top.Add = stub_bottom.Sub = (row.MaxStrokeWidth - hrz_width) / 2;
                        }
                    }

                    //right corner
                    {
                        var stub_left = row[row.Length - 1];
                        var stub_top = cols[row.Length][y - 1];
                        var stub_bottom = cols[row.Length][y];
                        LineStub choosen;
                        if (stub_left.StrokeWidth >= stub_top.StrokeWidth)
                            choosen = stub_left.StrokeWidth >= stub_bottom.StrokeWidth ? stub_left : stub_bottom;
                        else
                            choosen = stub_top.StrokeWidth >= stub_bottom.StrokeWidth ? stub_top : stub_bottom;
                        row.EndGap = stub_bottom.StartGap = choosen;

                        double v_width = stub_top.StrokeWidth >= stub_bottom.StrokeWidth ? stub_top.StrokeWidth : stub_bottom.StrokeWidth;
                        stub_left.Add = (cols[row.Length].MaxStrokeWidth - v_width) / 2;
                        double hrz_width = stub_left.StrokeWidth;
                        stub_top.Add = stub_bottom.Sub = (row.MaxStrokeWidth - hrz_width) / 2;
                    }
                }

                //Bottom row
                {
                    var row = rows[end_y];

                    //Bottom/Left corner
                    {
                        var stub_right = row[0];
                        var stub_top = cols[0][end_y - 1];

                        LineStub choosen;
                        if (stub_right.StrokeWidth >= stub_top.StrokeWidth)
                            choosen = stub_right;
                        else
                            choosen = stub_top;
                        stub_right.StartGap = cols[0].EndGap = choosen;

                        double v_width = stub_top.StrokeWidth;
                        stub_right.Sub = (cols[0].MaxStrokeWidth - v_width) / 2;
                        double hrz_width = stub_right.StrokeWidth;
                        stub_top.Add = (row.MaxStrokeWidth - hrz_width) / 2;
                    }

                    //Bottom row
                    {
                        for (int x = 1; x < row.Length; x++)
                        {
                            var stub_left = row[x - 1];
                            var stub_right = row[x];
                            var stub_top = cols[x][end_y - 1];

                            LineStub choosen;
                            if (stub_left.StrokeWidth >= stub_right.StrokeWidth)
                            {
                                if (stub_left.StrokeWidth >= stub_top.StrokeWidth)
                                    choosen = stub_left;
                                else
                                    choosen = stub_top;
                            }
                            else
                            {
                                if (stub_right.StrokeWidth >= stub_top.StrokeWidth)
                                    choosen = stub_right;
                                else
                                    choosen = stub_top;
                            }
                            stub_right.StartGap = cols[x].EndGap = choosen;

                            double v_width = stub_top.StrokeWidth;
                            stub_left.Add = stub_right.Sub = (cols[x].MaxStrokeWidth - v_width) / 2;
                            double hrz_width = stub_left.StrokeWidth >= stub_right.StrokeWidth ? stub_left.StrokeWidth : stub_right.StrokeWidth;
                            stub_top.Add = (row.MaxStrokeWidth - hrz_width) / 2;
                        }
                    }

                    //right corner
                    {
                        var stub_left = row[row.Length - 1];
                        var stub_top = cols[row.Length][end_y - 1];
                        LineStub choosen;
                        if (stub_left.StrokeWidth >= stub_top.StrokeWidth)
                            choosen = stub_left;
                        else
                            choosen = stub_top;
                        row.EndGap = cols[row.Length].EndGap = choosen;

                        double v_width = stub_top.StrokeWidth;
                        stub_left.Add = (cols[row.Length].MaxStrokeWidth - v_width) / 2;
                        double hrz_width = stub_left.StrokeWidth;
                        stub_top.Add = (row.MaxStrokeWidth - hrz_width) / 2;
                    }
                }
            }

            return all;
        }

        /// <summary>
        /// Merges bottom into top
        /// </summary>
        private void CombineStubs(LineStubs tops, LineStubs bottoms)
        {
            for (int c = 0; c < tops.Length; c++)
            {
                LineStub top = tops[c], bottom = bottoms[c];

                if (top == null || bottom != null && bottom.StrokeWidth > top.StrokeWidth)
                    tops[c] = bottom;
            }
        }

        /// <summary>
        /// Merges bottom into top
        /// </summary>
        private void CombineStubs(LineStubs top, LineStub bottom)
        {
            for (int c = 0; c < top.Length; c++)
            {
                var stub = top[c];
                if (stub == null || bottom.StrokeWidth > stub.StrokeWidth)
                    top[c] = bottom.Clone();
            }
        }

        private static LineStubs MakeStubs(BlockList list, int pos_nr, bool is_row, bool add_span, int length, Type ctd, string pos, string span_str)
        {
            var border = ctd.GetField("VisBorder" + pos, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var border_color = ctd.GetProperty("BorderColor" + pos, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var dash = ctd.GetProperty("Dash" + pos, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var span = ctd.GetProperty(span_str, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            LineStubs stubs = new LineStubs(length);

            if (is_row)
            {
                for (int x = 0; x < length; )
                {
                    var td = list[x];
                    if (td != null)
                    {
                        LineStub ls;

                        if ((td.StartColumn == x) &&
                            (add_span ? td.StartRow + td.RowSpan - 1 == pos_nr : td.StartRow == pos_nr))
                        {
                            ls = new LineStub((double)border.GetValue(td),
                                              (cBrush)border_color.GetValue(td, null),
                                              (xDashStyle?)dash.GetValue(td, null));
                        }
                        else
                        {
                            //Inserts an empty linestub, to simplify later algorithms
                            ls = new LineStub();
                        }

                        int span_count = (int)span.GetValue(td, null);
                        stubs[x++] = ls;
                        for (int c = 1; c < span_count; c++)
                            stubs[x++] = ls.Clone();
                    }
                }
            }
            else
            {
                for (int x = 0; x < length; )
                {
                    var td = list[x];
                    if (td != null)
                    {
                        LineStub ls;

                        if ((add_span ? td.StartColumn + td.ColSpan - 1 == pos_nr : td.StartColumn == pos_nr) &&
                            (td.StartRow == x))
                        {
                            ls = new LineStub((double)border.GetValue(td),
                                              (cBrush)border_color.GetValue(td, null),
                                              (xDashStyle?)dash.GetValue(td, null));
                        }
                        else
                        {
                            //Inserts an empty linestub, to simplify later algorithms
                            ls = new LineStub();
                        }

                        int span_count = (int)span.GetValue(td, null);
                        stubs[x++] = ls;
                        for (int c = 1; c < span_count; c++)
                            stubs[x++] = ls.Clone();
                    }
                }
            }

            return stubs;
        }

        protected override bool LayoutContent(cBox anchor)
        {
            _contains_text = false;
            if (_FirstRow == null) return false;
            var stats = new cTextStat();

            var ret = (Style.BorderCollapse == eBorderCollapse.collapse) ?
                LayoutContentCollapsed(stats) : LayoutContentNormal(stats);
            if (stats.HasText)
            {
                _contains_text = true;
                UpdateTextParams(stats);
            }
            return ret;
        }

        /// <summary>
        /// Normal borders
        /// </summary>
        private bool LayoutContentNormal(cTextStat stats)
        {
            //0. Calculate values
            {
                bool has_space = Style.BorderCollapse == eBorderCollapse.separate && Style.BorderSpacing.Value != 0;
                VisCellSpacing = has_space ? SetSize(Style.BorderSpacing) : 0;
            }

            //1. Sets up the structure, and finds mimumim width/height for all cells.
            MakeTable();
            LayoutTD(stats);
            double table_width, total_cell_space_w, free_space, min_table_width;

            if (Style.TableLayout == eTableLayout.Fixed && HasWidth)
            {
                //Todo: LayoutTD is not needed. Also, height needs to be handled.
                // ++ deal with borders, and more.

                total_cell_space_w = VisCellSpacing * (_Columns.Length + 1);
                min_table_width = table_width = VisContentWidth - total_cell_space_w;
                double w = table_width / _Columns.Length;
                for(int c = 0; c < _Columns.Length; c++)
                {
                    _Columns[c].VisSize = w;
                }
            }
            else
            {
                //2. For each column, determine the width for cells that does not span multiple columns
                for (int c = 0; c < _Columns.Length; c++)
                {
                    var col = _Columns[c];
                    col.SetColWidth(c);
                }

                //3. Adjust column widths so that they at least cover cells that span multiple columns
                min_table_width = 0;
                double max_table_width = 0;
                for (int c = 0; c < _Columns.Length; c++)
                {
                    var col = _Columns[c];
                    if (col.HasSpan)
                    {
                        var td = col.GetMinSpanTD(c);
                        var cols_w = GetColsWidth(td);
                        if (cols_w < td.VisMinFullWidth)
                        {
                            //Adjusts the size of the columns.
                            double add = (td.VisMinFullWidth - cols_w) / td.ColSpan;
                            for (int i = 0; i < td.ColSpan; i++)
                            {
                                var col2 = _Columns[td.StartColumn + i];
                                col2.MinSize += add;
                                if (col2.MaxSize < col2.MinSize)
                                    col2.MaxSize = col2.MinSize;
                            }
                        }
                    }
                    min_table_width += col.MinSize;
                    max_table_width += col.MaxSize;
                }

                //4. Determine the width of the table.
                total_cell_space_w = VisCellSpacing * (_Columns.Length + 1);
                if (HasWidth)
                {
                    if (VisContentWidth > min_table_width)
                    {
                        table_width = VisContentWidth;
                    }
                    else
                    {
                        table_width = min_table_width;
                    }
                }
                else
                {
                    table_width = max_table_width;
                }

                //5. Determine the width of the columns.
                free_space = table_width - min_table_width - total_cell_space_w;
                if (Util.Real.IsZero(free_space) || free_space < 0)
                {
                    //Set columns to min with
                    foreach (var col in _Columns)
                        col.VisSize = col.MinSize;
                }
                else if (table_width <= VisContentWidth)
                {
                    //Todo: Preliminary implementation. We just divide the free space on the columns and hand it out.
                    var space = (free_space + min_table_width) / _Columns.Length;
                    foreach (var col in _Columns)
                        col.VisSize = space;
                }
            }

            //6. Reflow
            BlockList.LayoutW(_Columns, this);

            //7. For each row, determine the minimum height for cells that does not span multiple rows
            // - Use final height, not minimum
            for (var c = 0; c < _Rows.Length; c++)
                _Rows[c].SetRowHeight(c);

            //8. Adjust row height as in step 3.
            double min_table_height = 0, max_table_height = 0;
            for (int c = 0; c < _Rows.Length; c++)
            {
                var row = _Rows[c];
                if (row.HasSpan)
                {
                    var td = row.GetMinRSpanTD(c);
                    var rows_h = GetRowsHeight(td);
                    if (rows_h < td.VisMinFullHeight)
                    {
                        //Adjusts the size of the columns.
                        double add = td.VisMinFullHeight - rows_h;
                        for (int i = 0; i < td.RowSpan; i++)
                        {
                            var col2 = _Rows[td.StartRow + i];
                            col2.MinSize += col2.MinSize / rows_h * add;
                            if (col2.MaxSize < col2.MinSize)
                                col2.MaxSize = col2.MinSize;
                        }
                    }
                }
                min_table_height += row.MinSize;
                max_table_height += row.MaxSize;
            }

            //9. Determine the height of the table.
            double table_height, total_cell_space_h = VisCellSpacing * (_Rows.Length + 1);
            if (HasHeight)
            {
                if (VisContentHeight > min_table_height + total_cell_space_h)
                {
                    if (VisContentHeight < max_table_height + total_cell_space_h)
                        table_height = VisContentHeight - total_cell_space_h;
                    else
                        table_height = max_table_height;
                }
                else
                {
                    table_height = min_table_height;
                }
            }
            else
            {
                table_height = max_table_height;
            }

            _content_size = new xSize(table_width + total_cell_space_w, table_height + total_cell_space_h);
            _min_content_size = new xSize(min_table_width + total_cell_space_w, min_table_height + total_cell_space_h);

            //10. Determine the height of the rows.
            free_space = table_height - min_table_height;
            if (Util.Real.IsZero(free_space) || free_space < 0)
            {
                //Set rows to min with
                foreach (var row in _Rows)
                    row.VisSize = row.MinSize;
            }
            else
            {
                throw new NotImplementedException("Distribute height on rows");
            }

            //11. Reflow
            return BlockList.Layout(_Columns, _Rows, this);
        }

        /// <summary>
        /// Borders are collapsed into eachother
        /// </summary>
        /// <remarks>
        /// Todo: Should send along anchor parameter. (LayoutTD should take an anchor paramterer)
        /// </remarks>
        private bool LayoutContentCollapsed(cTextStat stats)
        {
            //0. Calculate values
            {
                bool has_space = Style.BorderCollapse == eBorderCollapse.separate && Style.BorderSpacing.Value != 0;
                VisCellSpacing = has_space ? SetSize(Style.BorderSpacing) : 0;
            }

            //1. Sets up the structure, and finds mimumim width/height for all cells.
            MakeTable();
            LayoutTD(stats);
            CalcBorderWidths();
            CollapseBorderWidths();
            double table_width, free_space, min_table_width;

            if (Style.TableLayout == eTableLayout.Fixed && HasWidth)
            {
                //Todo: LayoutTD is not needed. Also, height needs to be handled.
                // ++ deal with borders, and more.

                min_table_width = table_width = VisContentWidth;
                double w = table_width / _Columns.Length;
                for (int c = 0; c < _Columns.Length; c++)
                {
                    _Columns[c].VisSize = w;
                }
            }
            else
            {
                //2. For each column, determine the width for cells that does not span multiple columns
                for (int c = 0; c < _Columns.Length; c++)
                {
                    var col = _Columns[c];
                    col.SetColBWidth(c);
                }

                //3. Adjust column widths so that they at least cover cells that span multiple columns
                min_table_width = 0;
                double max_table_width = 0;
                for (int c = 0; c < _Columns.Length; c++)
                {
                    BlockList col = _Columns[c], col2;
                    if (col.HasSpan)
                    {
                        //Fetches the td with the largest min width
                        var td = col.GetMinSpanTD(c);

                        //Gets the column widths of the td, with the borders subtracted
                        col2 = _Columns[td.StartColumn + td.ColSpan - 1];
                        double cols_w = GetColsWidth(td) - col.BorderOne - col2.BorderTwo;

                        //The width of the cell, with the borders substracted
                        double td_min_width = td.VisMinFullWidth - td.VisBorderLeft - td.VisBorderRight;

                        //If the content width of the columns are smaller than the content width of
                        //the cell, then the width needs to be adjusted.
                        if (cols_w < td_min_width)
                        {
                            //Adjusts the size of the columns.
                            double add = td_min_width - cols_w;
                            for (int i = 0; i < td.ColSpan; i++)
                            {
                                col2 = _Columns[td.StartColumn + i];
                                col2.MinSize += col2.MinSize / cols_w * add;
                                if (col2.MaxSize < col2.MinSize)
                                    col2.MaxSize = col2.MinSize;
                            }
                        }
                    }

                    //Calculates the total min/max width of the table
                    double w = col.BorderOne + col.BorderTwo;
                    min_table_width += col.MinSize + w;
                    max_table_width += col.MaxSize + w;
                }


                //4. Determine the width of the table.
                if (HasWidth)
                {
                    if (VisContentWidth > min_table_width)
                    {
                        if (VisContentWidth < max_table_width)
                            table_width = VisContentWidth;
                        else
                            table_width = max_table_width;
                    }
                    else
                    {
                        table_width = min_table_width;
                    }
                }
                else
                {
                    table_width = max_table_width;
                }

                //5. Determine the width of the columns.
                free_space = table_width - min_table_width;
                if (Util.Real.IsZero(free_space) || free_space < 0 || VisContentWidth <= min_table_width)
                {
                    //Set columns to min with
                    foreach (var col in _Columns)
                        col.VisSize = col.MinSize;
                }
                else if (table_width <= VisContentWidth)
                {
                    //Set columns to max with
                    foreach (var col in _Columns)
                        col.VisSize = col.MaxSize;
                }
                else
                {
                    throw new NotImplementedException("Distribute width on columns");
                }
            }

            //6. Reflow
            var borders = _borders = CollapseBorders();
            BlockList.LayoutW(_Columns, this, borders);

            //7. For each row, determine the minimum height for cells that does not span multiple rows
            // - Use final height, not minimum
            for (var c = 0; c < _Rows.Length; c++)
                _Rows[c].SetRowBHeight(c);

            //8. Adjust row height as in step 3.
            CalcBorderHeights(borders.Rows);
            double min_table_height = 0, max_table_height = 0;
            for (int c = 0; c < _Rows.Length; c++)
            {
                var row = _Rows[c];
                if (row.HasSpan)
                {
                    //Fetches the td with the largest min height
                    var td = row.GetMinRSpanTD(c);

                    //Gets the row heights of the td, with the borders subtracted
                    var col2 = _Rows[td.StartRow + td.RowSpan - 1];
                    var rows_h = GetRowsHeight(td) - col2.BorderOne - col2.BorderTwo;

                    //The height of the cell, with the borders substracted
                    double td_min_height = td.VisMinFullHeight - td.VisBorderTop - td.VisBorderBottom;

                    if (rows_h < td_min_height)
                    {
                        //Adjusts the size of the columns.
                        double add = td_min_height - rows_h;
                        for (int i = 0; i < td.RowSpan; i++)
                        {
                            col2 = _Rows[td.StartRow + i];
                            col2.MinSize += col2.MinSize / rows_h * add;
                            if (col2.MaxSize < col2.MinSize)
                                col2.MaxSize = col2.MinSize;
                        }
                    }
                }

                //Calculates the total min/max height of the table. 
                double h = row.BorderOne + row.BorderTwo;
                min_table_height += row.MinSize + h;
                max_table_height += row.MaxSize + h;
            }

#if PADD_CONTENT
            //A collapsed border can extend beyond the border of the table. Since we're doing the border drawing, all
            //we need to do is to make sure the content is big enough to contain this extra border space.
            //
            //Once issue with this solution is that children that size themselves to the tables size might be off
            //a little. I've not tested how webbrowsers deals with this issue*. 
            // * Now tested, and they ignore this issue. 
            double padd_h = ((_Rows[0].BorderOne - VisBorderTop) + (_Rows[_Rows.Length - 1].BorderTwo - VisBorderBottom)) / 2,
                   padd_w = ((_Columns[0].BorderOne - VisBorderLeft) + (_Columns[_Columns.Length - 1].BorderTwo - VisBorderRight) / 2);


            //We remove the table's border and overflowing border from the table height.
            double sub = VisBorderTop + VisBorderBottom + padd_h;

            min_table_height -= sub;
            max_table_height -= sub;
#else
            //Turns out MS Edge sets the table's borders to half the maximum stroke width of the tichest border one the
            //row or column, regadless of what border tichnes has been set on the table itself.
            //
            //This actually makes sense, in that "half the border" is on the table, and "half the border" is on
            //the cell. Just like how half the border is divided between adjacent cells.
            //
            //Firefox does the same thing, just less buggy when dealing with overdraw hidden.
            VisBorderTop = borders.Rows[0].MaxStrokeWidth / 2;
            VisBorderBottom = borders.Rows[_Rows.Length].MaxStrokeWidth / 2;
            VisBorderLeft = borders.Columns[0].MaxStrokeWidth / 2;
            VisBorderRight = borders.Columns[_Columns.Length].MaxStrokeWidth / 2;

            //Note, min_table_height/width contains "half" the table border. I have not tested this throughtly in
            //web browsers. There is certainly room for error here.
#endif

            //9. Determine the height of the table.
            double table_height;
            if (HasHeight)
            {
#if PADD_CONTENT
                if (VisContentHeight + padd_h > min_table_height)
                {
                    if (VisContentHeight + padd_h < max_table_height)
#else
                if (VisContentHeight > min_table_height)
                {
                    if (VisContentHeight < max_table_height)
#endif
                        table_height = VisContentHeight;
                    else
                        table_height = max_table_height;
                }
                else
                {
                    table_height = min_table_height;
                }
            }
            else
            {
                table_height = max_table_height;
            }

#if PADD_CONTENT
            _content_size = new xSize(table_width + padd_w, table_height + padd_h);
            _min_content_size = new xSize(min_table_width + padd_w, min_table_height + padd_h);
#else
            _content_size = new xSize(table_width, table_height);
            _min_content_size = new xSize(min_table_width, min_table_height);
#endif

            //10. Determine the height of the rows.
            free_space = table_height - min_table_height;
            if (Util.Real.IsZero(free_space) || free_space < 0)
            {
                //Set rows to min with
                foreach (var row in _Rows)
                    row.VisSize = row.MinSize;
            }
            else
            {
                throw new NotImplementedException("Distribute height on rows");
            }

            //11. Reflow
            return BlockList.Layout(_Columns, _Rows, this, borders);
        }

        protected override void BlockLayoutChanged(cBox child)
        {
            //Do nothing as tables are not effected by "inline/block" changes
        }

        #endregion

        #region Position

        protected override void FlowContent(cBox anchor)
        {
            if (_Rows == null) return;

            if (Style.BorderCollapse == eBorderCollapse.collapse)
                FlowContentCollapsed(anchor);
            else
                FlowContentNormal(anchor);
        }

        private void FlowContentNormal(cBox anchor)
        {
            double pos_x, pos_y = VisCellSpacing, height = _content_size.Height;

            for (int row_nr = 0; row_nr < _Rows.Length; row_nr++)
            {
                var row = _Rows[row_nr];
                pos_x = VisCellSpacing;

                for (int col_nr = 0; col_nr < _Columns.Length; col_nr++)
                {
                    var td = row[col_nr];
                    if (td != null && td.StartRow == row_nr && td.StartColumn == col_nr)
                    {
                        td.PosX = pos_x;
                        td.PosY = pos_y;
                        td.BottomY = height - (pos_y + row.VisSize);
                        td.UpdatePosition(anchor);
                    }

                    pos_x += _Columns[col_nr].VisSize + VisCellSpacing;
                }

                pos_y += row.VisSize + VisCellSpacing;
            }
        }

        private void FlowContentCollapsed(cBox anchor)
        {
            double pos_x, pos_y, height = _content_size.Height;

            //First row
            pos_y = 0;

            for (int row_nr = 0; row_nr < _Rows.Length; row_nr++)
            {
                var row = _Rows[row_nr];
                pos_x = 0;
                var top_borders = _borders.Rows[row_nr];
                var bottom_borders = _borders.Rows[row_nr + 1];

                for (int col_nr = 0; col_nr < _Columns.Length; col_nr++)
                {
                    var left_borders = _borders.Columns[col_nr];

                    var col = _Columns[col_nr];
                    var td = row[col_nr];
                    if (td != null && td.StartRow == row_nr && td.StartColumn == col_nr)
                    {
                        var left_border = left_borders[row_nr];
                        var top_border = top_borders[col_nr];
                        td.PosX = pos_x + left_border.StrokeWidth / 2;
#if PADD_CONTENT
                        if (col_nr == 0)
                            td.PosX += (col.BorderOne - left_border.StrokeWidth) / 2 - td.VisBorderLeft;
                        else
                            td.PosX += col.BorderOne - td.VisBorderLeft;
#else
                        //Since we don't alter VisBorderLeft on the cell, we need to move the position
                        //"the cell's border width." This since the cell will position its content
                        //inside the border.
                        //
                        //Consider changing how td renders its content so that this does not happen.
                        //
                        //Next, we add half the stroke width, as that is the true width of the cell's
                        //border. (Note, the - adds since we're using -=)
                        td.PosX -= td.VisBorderLeft;// -left_border.StrokeWidth / 2;
#endif
                        td.PosY = pos_y + row.BorderOne - (top_borders.MaxStrokeWidth - top_border.StrokeWidth) / 2;
#if PADD_CONTENT
                        if (row_nr == 0)
                            td.PosY += -(row.BorderOne - top_border.StrokeWidth) / 2 - td.VisBorderTop;
                        else
                            td.PosY += -(row.BorderOne - top_border.StrokeWidth / 2) - td.VisBorderTop;
#else
                        //Since we don't alter VisBorderTop on the cell, we need to move the position
                        //"down the cell's border height." This since the cell will position its content
                        //inside the border.
                        //
                        //Consider changing how td renders its content so that this does not happen.
                        //
                        //Next, we add half the stroke width, as that is the true width of the cell's
                        //border.
                        td.PosY -= td.VisBorderTop;// -top_border.StrokeWidth / 2;
#endif
                        //Reverses the coordinates. Not using VisFullHeight as margin is ignored.
                        td.BottomY = height - (td.PosY + td.VisHeight);

                        td.UpdatePosition(anchor);
                    }

                    pos_x += col.VisSize + col.BorderOne + col.BorderTwo;
                }

                pos_y += row.VisSize + row.BorderOne + row.BorderTwo;
            }
        }

        #endregion

        #region Render

        internal override void Render(IDraw draw, ref cRenderState state, cBox anchor)
        {
            Render(draw, ref state, anchor, Style.BorderCollapse == eBorderCollapse.separate || _FirstRow == null);
        }

        protected override void DrawContent(IDraw draw, ref cRenderState state, cBox anchor)
        {
            if (_Rows == null) return;

            if (Style.BorderCollapse == eBorderCollapse.collapse)
                DrawBorders(draw, ref state);

            double move_x = VisBorderLeft + VisPaddingLeft, mx,
                   move_y = VisBorderBottom + VisPaddingBottom, my;
            bool draw_border = Style.BorderCollapse == eBorderCollapse.separate;

            for (int row_nr = 0; row_nr < _Rows.Length; row_nr++)
            {
                var row = _Rows[row_nr];
                for (int col_nr = 0; col_nr < _Columns.Length; col_nr++)
                {
                    var td = row[col_nr];
                    if (td != null && td.StartRow == row_nr && td.StartColumn == col_nr)
                    {
                        state.Save(draw);
                        mx = move_x + td.PosX;
                        my = move_y + td.BottomY;
                        if (mx != 0 || my != 0)
                            state.PrependCTM(new xMatrix(mx, my), draw);
                        td.Render(draw, ref state, anchor, draw_border);
                        state = state.Restore(draw);
                    }
                }
            }
        }

        private static void SetStyle(LineStub stub, cFigureDraw figure)
        {
            figure.StrokeColor = stub.StrokeColor;
            figure.StrokeStyle = stub.StrokeStyle;
            figure.StrokeWidth = stub.StrokeWidth;
        }

        /// <summary>
        /// Draws the table's borders when in "border collapse" mode
        /// </summary>
        /// <param name="idraw">Target surface</param>
        /// <param name="state">Render state</param>
        /// <remarks>
        /// This quick implementation overdraws (bad for alpha) and
        /// ignores border radius
        /// </remarks>
        private void DrawBorders(IDraw idraw, ref cRenderState state)
        {
            using (var draw = new cFigureDraw(idraw, state))
            {
                //Draws rows
                {
                    double bottom_y = VisHeight - VisBorderTop;
                    for (int row_nr = 0; true; row_nr++)
                    {
                        var line = _borders.Rows[row_nr];
                        if (!Util.Real.IsZero(line.MaxStrokeWidth))
                        {
                            //Note, draw_x can sometimes be "too big", but that's fine.
                            double pos_x = VisBorderLeft, drawn_x = -1;

                            for (int col_nr = 0; col_nr < line.Length; col_nr++)
                            {
                                var col = _Columns[col_nr];
                                var stub = line[col_nr];
                                if (stub == null)
                                {
                                    pos_x += col.BorderOne + col.VisSize;
                                }
                                else
                                {
                                    //Note, assumes that BorderOne > 0 when BorderTwo > 0. I.e. (col.BorderOne > 0 || col.BorderTwo > 0)
                                    if (col.BorderOne > 0)
                                    {
                                        //Figures out hte distance to draw and what style would be used if style was horizontal.
                                        double distance = col.BorderOne;
                                        LineStub style_stub;
                                        if (col_nr > 0)
                                        {
                                            distance += _Columns[col_nr - 1].BorderTwo;
                                            style_stub = line[col_nr - 1];
                                            if (stub.StrokeWidth > style_stub.StrokeWidth)
                                                style_stub = stub;
                                        }
                                        else
                                        {
                                            style_stub = stub;

                                            //Draws to the edge
                                            distance += pos_x;
                                            pos_x = 0;
                                        }

                                        //Draws only horizontal styles. This is done by checking if StartGap is a horizontal style.
                                        if (ReferenceEquals(style_stub, stub.StartGap))
                                        {
                                            SetStyle(style_stub, draw);

                                            //Centers the stroke position
#if PADD_CONTENT
                                            var line_y = bottom_y - (style_stub.StrokeWidth / 2 + (line.MaxStrokeWidth - style_stub.StrokeWidth) / 2);
#else
                                            var line_y = bottom_y;
#endif

                                            if (!draw.HasStartPoint || pos_x > drawn_x || line_y != draw.LastPos.Y)
                                                draw.MoveTo(pos_x, line_y);
                                            pos_x += distance;
                                            drawn_x = pos_x;
                                            draw.LineTo(pos_x, line_y);
                                        }
                                        else
                                            pos_x += distance;

                                    }

                                    if (!Util.Real.IsZero(stub.StrokeWidth))
                                    {
                                        SetStyle(stub, draw);

                                        //Centers the stroke position
#if PADD_CONTENT
                                        var line_y = bottom_y - (stub.StrokeWidth / 2 + (line.MaxStrokeWidth - stub.StrokeWidth) / 2);
#else
                                        var line_y = bottom_y;
#endif
                                        var line_x = pos_x - stub.Sub;

                                        if (!draw.HasStartPoint || line_x > drawn_x || line_y != draw.LastPos.Y)
                                            draw.MoveTo(line_x, line_y);
                                        pos_x += col.VisSize;
                                        line_x = pos_x;
                                        drawn_x = line_x;
                                        draw.LineTo(line_x, line_y);
                                    }
                                    else
                                        pos_x += col.VisSize;
                                }
                            }

                            //Draws rightmost edge. This information is stored in line.EndGap instead of stub.StartGap, so doing
                            //this outside the for loop.
                            if (ReferenceEquals(line.EndGap, line[line.Length - 1]))
                            {
                                var dist = _Columns[_Columns.Length - 1].BorderTwo + VisBorderRight;
                                if (dist > 0)
                                {
                                    var stub = line.EndGap;
                                    SetStyle(stub, draw);

                                    //Centers the stroke position
#if PADD_CONTENT
                                    var line_y = bottom_y - (stub.StrokeWidth / 2 + (line.MaxStrokeWidth - stub.StrokeWidth) / 2);
#else
                                    var line_y = bottom_y;
#endif

                                    if (!draw.HasStartPoint || pos_x > drawn_x || line_y != draw.LastPos.Y)
                                        draw.MoveTo(pos_x, line_y);
                                    pos_x += dist;
                                    drawn_x = pos_x;
                                    draw.LineTo(pos_x, line_y);
                                }
                            }
                        }

                        if (row_nr == _Rows.Length)
                            break;

                        //Moves down one row, except the lower border which is moved down the next loop.
#if PADD_CONTENT
                        if (row_nr > 0)
                            bottom_y -= _Rows[row_nr - 1].BorderTwo;
                        var row = _Rows[row_nr];
                        bottom_y -= row.BorderOne + row.VisSize;
#else
                        var row = _Rows[row_nr];
                        bottom_y -= row.BorderOne + row.VisSize + row.BorderTwo;
#endif
                    }


                }

                //Draws columns
                {
                    double pos_x = VisBorderLeft;
                    for (int col_nr = 0; true; col_nr++)
                    {
                        var line = _borders.Columns[col_nr];
                        if (!Util.Real.IsZero(line.MaxStrokeWidth))
                        {
                            double pos_y = VisBorderTop, drawn_y = -1;

                            for (int row_nr = 0; row_nr < line.Length; row_nr++)
                            {
                                var row = _Rows[row_nr];
                                var stub = line[row_nr];
                                if (stub == null)
                                {
                                    pos_y += row.BorderOne + row.VisSize;
                                }
                                else
                                {
                                    //Note, assumes that BorderOne > 0 when BorderTwo > 0. I.e. (col.BorderOne > 0 || col.BorderTwo > 0)
                                    if (row.BorderOne > 0)
                                    {
                                        //Figures out hte distance to draw and what style would be used if style was horizontal.
                                        double distance = row.BorderOne;
                                        LineStub style_stub;
                                        if (row_nr > 0)
                                        {
                                            distance += _Rows[row_nr - 1].BorderTwo;
                                            style_stub = line[row_nr - 1];
                                            if (stub.StrokeWidth > style_stub.StrokeWidth)
                                                style_stub = stub;
                                        }
                                        else
                                        {
                                            style_stub = stub;

                                            //Draws to the edge
                                            distance += pos_y;
                                            pos_y = 0;
                                        }

                                        //Draws only horizontal styles. This is done by checking if StartGap is a horizontal style.
                                        if (ReferenceEquals(style_stub, stub.StartGap))
                                        {
                                            SetStyle(style_stub, draw);

                                            //Alters coordinate to draw as PDF does
                                            var line_y = VisHeight - pos_y;

                                            if (!draw.HasStartPoint || pos_y > drawn_y || pos_x != draw.LastPos.X)
                                                draw.MoveTo(pos_x, line_y);
                                            pos_y += distance;
                                            line_y -= distance;
                                            drawn_y = pos_y;
                                            draw.LineTo(pos_x, line_y);
                                        }
                                        else
                                            pos_y += distance;

                                    }

                                    if (!Util.Real.IsZero(stub.StrokeWidth))
                                    {
                                        SetStyle(stub, draw);

                                        //Centers the stroke position
                                        //Alters coordinate to draw as PDF does
                                        var line_y = VisHeight - pos_y + stub.Sub;

                                        if (!draw.HasStartPoint || pos_y > drawn_y || pos_x != draw.LastPos.X)
                                            draw.MoveTo(pos_x, line_y);
                                        pos_y += row.VisSize;
                                        line_y -= row.VisSize + stub.Sub + stub.Add;
                                        drawn_y = pos_y;
                                        draw.LineTo(pos_x, line_y);
                                    }
                                    else
                                        pos_y += row.VisSize;
                                }
                            }

                            //Draws downmost edge. This information is stored in line.EndGap instead of stub.StartGap, so doing
                            //this outside the for loop.
                            if (ReferenceEquals(line.EndGap, line[line.Length - 1]))
                            {
                                var dist = _Rows[_Rows.Length - 1].BorderTwo + VisBorderBottom;
                                if (dist > 0)
                                {
                                    var stub = line.EndGap;
                                    SetStyle(stub, draw);

                                    //Centers the stroke position
                                    //Alters coordinate to draw as PDF does
                                    var line_y = VisHeight - pos_y;

                                    if (!draw.HasStartPoint || pos_y > drawn_y || pos_x != draw.LastPos.X)
                                        draw.MoveTo(pos_x, line_y);
                                    pos_y += dist;
                                    line_y -= dist;
                                    drawn_y = pos_y;
                                    draw.LineTo(pos_x, line_y);
                                }
                            }
                        }

                        if (col_nr == _Columns.Length)
                            break;

                        //Moves down one row, except the lower border which is moved down the next loop.
                        var col = _Columns[col_nr];
                        pos_x += col.BorderOne + col.VisSize + col.BorderTwo;
                    }
                }

                state = draw.State;
            }

         /*   state.Save(draw);

            //Draws rows
            {
                double bottom_y = 0, border_one = 0;
                for (int row_nr = _Rows.Length - 1; true; row_nr--)
                {
                    var line = _borders.Rows[row_nr + 1];
                    int col_nr = 0;
                    double pos_x = 0;

                    //This can be fetched from _Rows, but then we need to use
                    //BorderTwo on all lines but the last, who needs BorderOne.
                    // i.e. max_width = (last) ? _Rows[row_nr].BorderOne : _Rows[row_nr].BorderTwo
                    double max_width = line.GetMaxWidth();

                    while (line != null)
                    {
                        draw.StrokeWidth = line.StrokeWidth;
                        if (line.StrokeColor != null)
                            line.StrokeColor.SetColor(state, false, draw);
                        else
                            cColor.BLACK.SetColor(state, false, draw);
                        if (line.StrokeStyle != null)
                            throw new NotImplementedException("Must track state so that it can be reset when needed.");
                        //draw.SetStrokeDashAr(line.StrokeStyle.Value);

                        //Centers the stroke position
                        var line_y = bottom_y + line.StrokeWidth / 2 + (max_width - line.StrokeWidth) / 2;

                        //Moves to the start point
                        draw.MoveTo(pos_x, line_y);

                        for (int c = 0; c < line.Span; c++)
                        {
                            var col = _Columns[col_nr++];
                            pos_x += col.BorderOne + col.VisSize + col.BorderTwo;
                        }

                        draw.LineTo(pos_x, line_y);
                        draw.DrawPathEO(false, true, false);

                        line = line.Next;
                    }

                    if (row_nr == -1) break;
                    var row = _Rows[row_nr];
                    bottom_y += border_one + row.VisSize + row.BorderTwo;
                    border_one = row.BorderOne;
                }
            }

            //Draws column borders
            {
                double pos_x = 0, border_one = 0, top = VisContentHeight + VisBorderBottom + VisBorderTop;
                for (int col_nr = 0; true; col_nr++)
                {
                    var line = _borders.Columns[col_nr];
                    int row_nr = 0;
                    double bottom_y = top;

                    //Maximum stroke width of the line
                    double max_width = line.GetMaxWidth();

                    while (line != null)
                    {
                        draw.StrokeWidth = line.StrokeWidth;
                        if (line.StrokeColor != null)
                            line.StrokeColor.SetColor(state, false, draw);
                        else
                            cColor.BLACK.SetColor(state, false, draw);
                        if (line.StrokeStyle != null)
                            throw new NotImplementedException("Must track state so that it can be reset when needed.");
                        //draw.SetStrokeDashAr(line.StrokeStyle.Value);

                        //Centers the stroke position
                        var line_x = pos_x + line.StrokeWidth / 2 + (max_width - line.StrokeWidth) / 2;

                        //Moves to the start point
                        draw.MoveTo(line_x, bottom_y);

                        for (int c = 0; c < line.Span; c++)
                        {
                            var row = _Rows[row_nr++];
                            bottom_y -= row.BorderOne + row.VisSize + row.BorderTwo;
                        }

                        draw.LineTo(line_x, bottom_y);
                        draw.DrawPathEO(false, true, false);

                        line = line.Next;
                    }

                    if (col_nr == _Columns.Length) break;

                    var col = _Columns[col_nr];
                    pos_x += border_one + col.VisSize + col.BorderTwo;
                    border_one = col.BorderOne;
                }
            }

            state = state.Restore(draw);*/
        }

        #endregion

        public cTable AppendChild(cTR child)
        {
            if (child == null) return this;
            child.Parent = this;

            if (_FirstRow == null)
                _FirstRow = _LastRow = child;
            else
            {
                _LastRow.VisNext = child;
                _LastRow = child;
            }

            if (HasListeners)
                child.AddListeners();

            return this;
        }

        protected override void RemoveChildImpl(cNode node)
        {
            if (node is cBox)
            {
                var child = (cBox)node;

                var prev = FindPrev(child);
                if (prev == null)
                {
                    if (!ReferenceEquals(child, _FirstRow))
                        throw new PdfInternalException("Child not in table");
                    _FirstRow = _LastRow = null;
                }
                else
                {
                    if (ReferenceEquals(child, _LastRow))
                    {
                        _LastRow = prev;
                        prev.VisNext = null;
                    }
                    else
                    {
                        prev.VisNext = child.VisNext;
                    }
                }
            }
        }

        private cTR FindPrev(cBox child)
        {
            cTR next = _FirstRow, current = null;
            while (next != null)
            {
                if (ReferenceEquals(next, child))
                    return current;
                current = next;
                next = next.VisNext as cTR;
            }
            return null;
        }

        internal override bool AddListeners()
        {
            var ret = base.AddListeners();
            foreach (var child in Rows)
            {
                if (child.AddListeners())
                    DoLayoutContent = true;
            }
            return ret;
        }

        internal override void RemoveListeners()
        {
            base.RemoveListeners();
            foreach (var child in Rows)
                child.RemoveListeners();
        }

        #region IEnumerable

        protected override IEnumerator<cNode> GetEnumeratorImpl()
        {
            return Rows.GetEnumerator();
        }

        #endregion

        [System.Diagnostics.DebuggerDisplay("LineStub - Width: {StrokeWidth}")]
        private class LineStub
        {
            public readonly double StrokeWidth;
            public readonly cBrush StrokeColor;
            public readonly xDashStyle? StrokeStyle;

            /// <summary>
            /// What color and dash stub.
            /// </summary>
            /// <remarks>
            /// StrokeWidth is determined by which linestub has the biggest stroke width.
            /// Dashstyle is only to be used if the StartGap's style and StrokeWidth
            /// above is identical with lines on either side. Otherwise use a solid
            /// color.
            /// 
            /// Firefox deals with this in a similar manner. Edge can drop drawing a dashed
            /// border altogehter if it gets confused. 
            /// </remarks>
            public LineStub StartGap;

            /// <summary>
            /// Space from startgap to the line, and also space above start gap
            /// </summary>
            public double Sub;

            /// <summary>
            /// Space after the line to the next gap, and also space after the next gap
            /// </summary>
            /// <remarks>
            /// This value can be fetched by looking into the next linestub.Sub, so it
            /// might be redundant
            /// </remarks>
            public double Add;

            /// <summary>
            /// To prevent overdraw, there's a need to know the width of the line. While
            /// it's possible to figure this out by looking in the, say, column data when
            /// drawing rows, it's cumbersome. 
            /// </summary>
            /// <remarks>
            /// Microsoft Edge does overdraw when there's different tichnesses over and
            /// under a border. (I.e. a column border is ticher under a row border than
            /// over a row border).
            /// 
            /// If this is acceptable, we could "always" fetch this data from the "above",
            /// which would make the extra work of filling out this value needless.
            /// </remarks>
            //public double StartWidth;

            public LineStub(double sw, cBrush sc, xDashStyle? ss)
            {
                StrokeWidth = sw;
                StrokeColor = sc == null ? cColor.BLACK : sc;
                StrokeStyle = ss;
            }

            internal LineStub()
            {
                StrokeWidth = 0;
                StrokeColor = null;
                StrokeStyle = null;
            }

            //public LineStub Add(LineStub stub)
            //{
            //    if (Next != null)
            //        throw new PdfInternalException();

            //    if (StrokeWidth == stub.StrokeWidth &&
            //        StrokeColor == stub.StrokeColor &&
            //        xDashStyle.Equals(StrokeStyle, stub.StrokeStyle))
            //    {
            //        Span += stub.Span;
            //        return this;
            //    }
            //    else
            //    {
            //        Next = stub;
            //        return stub;
            //    }
            //}

            public LineStub Clone()
            {
                return (LineStub)MemberwiseClone();
            }
        }

        /// <summary>
        /// Contains information about borders around cells. 
        /// </summary>
        private class AllBorders
        {
            /// <summary>
            /// Borders on top/bottom of cells
            /// </summary>
            internal readonly LineStubs[] Rows;

            /// <summary>
            /// Borders on left/right of cells
            /// </summary>
            internal readonly LineStubs[] Columns;

            public AllBorders(int n_columns, int n_rows)
            {
                Rows = new LineStubs[n_rows + 1];
                Columns = new LineStubs[n_columns + 1];
            }
        }

        /// <summary>
        /// Contains a row or colum of line stubs
        /// </summary>
        private class LineStubs
        {
            private readonly LineStub[] _stubs;
            public double MaxStrokeWidth { get; private set; }
            public int Length { get { return _stubs.Length; } }

            /// <summary>
            /// This is the style for the right border edge of the last cell.
            /// </summary>
            /// <remarks>
            /// The StartGap is stored on the LineStubs themselves. To get
            /// the end gap of any cell you just look at the start gap of the
            /// next cell, except the last, then it's stored here.
            /// </remarks>
            public LineStub EndGap;

            public LineStub this[int index]
            {
                get { return _stubs[index]; }
                set
                {
                    System.Diagnostics.Debug.Assert(_stubs[index] == null || value != null && value.StrokeWidth >= _stubs[index].StrokeWidth);
                    _stubs[index] = value;
                    if (value != null && value.StrokeWidth > MaxStrokeWidth)
                        MaxStrokeWidth = value.StrokeWidth;
                }
            }

            public LineStubs(int capacity)
            {
                _stubs = new LineStub[capacity];
            }
        }

        private class BlockList
        {
            cTD[] _cells;

            /// <summary>
            /// The maximum width of the cells
            /// </summary>
            internal double MaxSize;

            /// <summary>
            /// The minimum maximum width of the cells
            /// </summary>
            internal double MinSize;

            /// <summary>
            /// Final width of the column
            /// </summary>
            internal double VisSize;

            /// <summary>
            /// Has a spanning cell
            /// </summary>
            internal bool HasSpan;

            /// <summary>
            /// Width of the border between cells
            /// </summary>
            internal double BorderOne, BorderTwo;

            public cTD this[int index] { get { return _cells[index]; } }

            public BlockList(int initial_capacity)
            {
                _cells = new cTD[initial_capacity];
            }

            /// <summary>
            /// Adds a block to the list
            /// </summary>
            /// <param name="pos">Position of the block</param>
            /// <param name="cell">Cell to add</param>
            public void add(int pos, cTD cell)
            {
                if (pos >= _cells.Length)
                    Array.Resize<cTD>(ref _cells, _cells.Length * 2);
                _cells[pos] = cell;
            }

            public void Trim(int size)
            {
                Array.Resize<cTD>(ref _cells, size);
            }

            public void SetColWidth(int index)
            {
                double max = 0, min = 0;

                for (int c = 0; c < _cells.Length; c++)
                {
                    var cell = _cells[c];
                    if (cell != null)
                    {
                        if (cell.ColSpan == 1)
                        {
                            max = Math.Max(max, cell.VisWidth);
                            min = Math.Max(min, cell.VisMinWidth);
                        }
                        else
                            HasSpan = cell.StartColumn == index;
                    }
                }
                MinSize = min;
                MaxSize = max;
            }

            /// <summary>
            /// Sets column width, but ignores the cells borders
            /// </summary>
            /// <param name="index">Current colum index</param>
            public void SetColBWidth(int index)
            {
                double max = 0, min = 0;

                for (int c = 0; c < _cells.Length; c++)
                {
                    var cell = _cells[c];
                    if (cell != null)
                    {
                        if (cell.ColSpan == 1)
                        {
                            //The width of borders is handled on it's own, so we only
                            //want to know the width of the content + padding. We
                            //therefore subtract borders from VisWidth. Alt. We
                            //can add padding to VisContentWidth
                            double b = cell.VisBorderLeft + cell.VisBorderRight;
                            max = Math.Max(max, cell.VisWidth - b);
                            min = Math.Max(min, cell.VisMinWidth - b);
                        }
                        else
                            HasSpan = cell.StartColumn == index;
                    }
                }
                MinSize = min;
                MaxSize = max;
            }

            public void SetRowHeight(int index)
            {
                double max = 0, min = 0;
                for (int c = 0; c < _cells.Length; c++)
                {
                    var cell = _cells[c];
                    if (cell != null)
                    {
                        if (cell.RowSpan == 1)
                        {
                            max = Math.Max(max, cell.VisHeight);
                            min = Math.Max(min, cell.VisMinHeight);
                        }
                        else
                            HasSpan = cell.StartRow == index;
                    }
                }
                MinSize = min;
                MaxSize = max;
            }

            /// <summary>
            /// Sets row height, but ignores the cells borders
            /// </summary>
            /// <param name="index">Current row index</param>
            public void SetRowBHeight(int index)
            {
                double max = 0, min = 0;
                for (int c = 0; c < _cells.Length; c++)
                {
                    var cell = _cells[c];
                    if (cell != null)
                    {
                        if (cell.RowSpan == 1)
                        {
                            double b = cell.VisBorderTop + cell.VisBorderBottom;
                            max = Math.Max(max, cell.VisHeight - b);
                            min = Math.Max(min, cell.VisMinHeight - b);
                        }
                        else
                            HasSpan = cell.StartRow == index;
                    }
                }
                MinSize = min;
                MaxSize = max;
            }

            public static void LayoutW(BlockList[] cols, cBox anchor)
            {
                for (int x = 0; x < cols.Length; x++)
                {
                    var col = cols[x];
                    for (int y = 0; y < col._cells.Length; y++)
                    {
                        var cell = col._cells[y];

                        if (cell != null && cell.StartColumn == x && cell.StartRow == y)
                        {
                            double size = col.VisSize;
                            if (cell.ColSpan > 1)
                            {
                                for (int c = 1; c < cell.ColSpan; c++)
                                    size += cols[cell.StartColumn + c].VisSize;
                            }
                            cell.Layout(size, double.NaN, anchor);
                        }
                    }
                }
            }

            /// <summary>
            /// Lays out width for when borders are collapsed
            /// </summary>
            public static void LayoutW(BlockList[] cols, cBox anchor, AllBorders borders)
            {
                int end_x = cols.Length - 1;
                for (int x = 0; x < cols.Length; x++)
                {
                    var col = cols[x];
                    var left_borders = borders.Columns[x];
                    var right_borders = borders.Columns[x + 1];

                    for (int y = 0; y < col._cells.Length; y++)
                    {
                        var cell = col._cells[y];

                        if (cell != null && cell.StartColumn == x && cell.StartRow == y)
                        {
                            var left_border = left_borders[y];
                            LineStub right_border = right_borders[y], rb = right_border;
                            if (cell.ColSpan > 1)
                            {
                                rb = borders.Columns[x + cell.ColSpan][y];
                            }

                            double size = col.VisSize, col_one = col.BorderOne;
                            for (int c = 1; c < cell.ColSpan; c++)
                            {
                                col = cols[cell.StartColumn + c];
                                size += col.VisSize + col.BorderOne;
                            }
                            
                            //Add "missing border" size. That is to say, when a cell's border is smaller than the
                            //maximum stroke width of the border along the column/row, then the cell is effecitvly
                            //wider than cells with ticher borders. We make sure this extra space is passed along
                            //to the layout routine. 
#if PADD_CONTENT
                            //In padd mode, the first and last borders are twice the width of every other border
                            //so we need to check for that.
                            size += (x == 0) ? (col_one - left_border.StrokeWidth) / 2 : col_one - left_border.StrokeWidth / 2;
                            size += (x == end_x) ? col.BorderTwo - rb.StrokeWidth : col.BorderTwo - rb.StrokeWidth / 2;
#else
                            size += col_one - left_border.StrokeWidth / 2;
                            size += col.BorderTwo - rb.StrokeWidth / 2;
#endif

                            //We add the cell's borders. This because "size" is now the empty space
                            //between column borders, while the layout rutine expects the size to include
                            //the borders.
                            //
                            //Since we never modify VisBorderLeft/Right to be zero, we have to add them to 
                            //the size so that the borders end up outside the content.
                            //
                            //Nulling border widths is another option, but I prefer not to touch those values
                            //as they might possibly be useful later. It's also possible I'll want to set
                            //these to their "true" values, in which case they still need to be added.
                            cell.Layout(size + cell.VisBorderLeft + cell.VisBorderRight, double.NaN, anchor);
                        }
                    }
                }
            }

            /// <summary>
            /// Lays out for when borders are collapsed
            /// </summary>
            public static bool Layout(BlockList[] cols, BlockList[] rows, cBox anchor, AllBorders borders)
            {
                //Keeps track of "SizeDependsOnParent"
                bool need = false;

                //Boundaries
                int end_x = cols.Length - 1, end_y = rows.Length - 1;

                //Goes through from top to bottom, left to right
                for (int x = 0; x < cols.Length; x++)
                {
                    //Gets the colum, and the left/right border stubs
                    var col = cols[x];
                    var left_borders = borders.Columns[x];
                    var right_borders = borders.Columns[x + 1];

                    //Goes from top of the column, to the bottom of the column
                    for (int y = 0; y < col._cells.Length; y++)
                    {
                        var cell = col._cells[y];

                        if (cell != null && cell.StartColumn == x && cell.StartRow == y)
                        {
                            var left_border = left_borders[y];
                            LineStub right_border = right_borders[y], rb = right_border;

                            double height = 0, width = col.VisSize;
                            if (cell.ColSpan > 1)
                            {
                                rb = borders.Columns[x + cell.ColSpan][y];
                            }

                            double col_one = col.BorderOne;
                            for (int c = 1; c < cell.ColSpan; c++)
                            {
                                col = cols[cell.StartColumn + c];
                                width += col.VisSize;
                            }

                            //Add "missing border" size. I.e. when a row have borders of multiple tichnesses,
                            //the thinner borders will have "extra content space". This is calculated here
#if PADD_CONTENT
                            //At boundaries, StrokeWidth and BorderOne/Two are their true sizes. We therefore have to calculate the total free space
                            //and divide by two. When not at a boundary, BorderOne/Two are half the width. We therefor get half the free space by only
                            //dividing StrokeWidth.
                            width += (x == 0) ? (col_one - left_border.StrokeWidth) / 2 : col_one - left_border.StrokeWidth / 2;
                            width += (x == end_x) ? col.BorderTwo - rb.StrokeWidth : col.BorderTwo - rb.StrokeWidth / 2;
#else
                            width += col_one - left_border.StrokeWidth / 2;
                            width += col.BorderTwo - rb.StrokeWidth / 2;
#endif

                            //Fetches row line stubs
                            var row = rows[y];
                            var top_border = borders.Rows[y][x];
                            var bottom_border = borders.Rows[y + 1][x];

                            for (int c = 0; c < cell.RowSpan; c++)
                            {
                                row = rows[y + c];
                                height += row.VisSize;
                            }

                            //Add "missing border" size.
#if PADD_CONTENT
                            //At boundaries, StrokeWidth and BorderOne/Two are their true sizes. We therefore have to calculate the total free space
                            //and divide by two. When not at a boundary, BorderOne/Two are half the width. We therefor get half the free space by only
                            //dividing StrokeWidth.
                            height += (y == 0) ? (rows[y].BorderOne - top_border.StrokeWidth) / 2 : rows[y].BorderOne - top_border.StrokeWidth / 2;
                            height += (y == end_y) ? (row.BorderTwo - bottom_border.StrokeWidth) / 2 : row.BorderTwo - bottom_border.StrokeWidth / 2;
#else
                            height += rows[y].BorderOne - top_border.StrokeWidth / 2;
                            height += row.BorderTwo - bottom_border.StrokeWidth / 2;
#endif


                            //We add the cell's borders. This because "size" is now the empty space
                            //between column borders. Since we never modify VisBorderLeft/Right to be
                            //zero, we have to add them to the size so that the borders end up outside
                            //the content.
                            //
                            //Nulling border widths is another option, but I prefer not to touch those values
                            //as they might possibly be useful later. It's also possible I'll want to set
                            //these to their "true" values, in which case they still need to be added.
                            //
                            //Todo: deal with boxsizing somehow. 
                            cell.Layout(width + cell.VisBorderLeft + cell.VisBorderRight, height + cell.VisBorderTop + cell.VisBorderBottom, anchor);
                            
                            if (cell.SizeDependsOnParent)
                                need = true;
                        }
                    }
                }
                return need;
            }

            public static bool Layout(BlockList[] cols, BlockList[] rows, cBox anchor)
            {
                bool need = false;
                for (int x = 0; x < cols.Length; x++)
                {
                    var col = cols[x];

                    for (int y = 0; y < col._cells.Length; y++)
                    {
                        var cell = col._cells[y];

                        if (cell != null && cell.StartColumn == x && cell.StartRow == y)
                        {
                            double height = 0, width = col.VisSize;
                            if (cell.ColSpan > 1)
                            {
                                for (int c = 1; c < cell.ColSpan; c++)
                                    width += cols[cell.StartColumn + c].VisSize;
                            }
                            for (int c = 0; c < cell.RowSpan; c++)
                                height += rows[cell.StartRow + c].VisSize;
                            cell.Layout(width, height, anchor);
                            if (cell.SizeDependsOnParent)
                                need = true;
                        }
                    }
                }
                return need;
            }

            /// <summary>
            /// Fetches the largest min width cell
            /// </summary>
            public cTD GetMinSpanTD(int index)
            {
                double w = 0;
                cTD td = null;
                for (int c = 0; c < _cells.Length; c++)
                {
                    var cell = _cells[c];
                    if (cell != null && cell.ColSpan != 1 && cell.StartColumn == index)
                    {
                        double cw = cell.VisMinFullWidth;
                        if (w < cw)
                        {
                            w = cw;
                            td = cell;
                        }
                    }
                }
                return td;
            }

            public cTD GetMinRSpanTD(int index)
            {
                double h = 0;
                cTD td = null;
                for (int c = 0; c < _cells.Length; c++)
                {
                    var cell = _cells[c];
                    if (cell != null && cell.RowSpan != 1 && cell.StartRow == index)
                    {
                        double ch = cell.VisMinFullHeight;
                        if (h < ch)
                        {
                            h = ch;
                            td = cell;
                        }
                    }
                }
                return td;
            }
        }
    }
}
