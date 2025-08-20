using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using System.Text;

namespace PdfLib.Compose.Layout.Internal
{
    /// <summary>
    /// Compose horizontal block Line wrapper
    /// 
    /// Modeled after chLineWrapper.
    /// </summary>
    class chbLineWrapper : IEnumerable<chbLineWrapper.LineMetrics>
    {
        /// <summary>
        /// This is where the block information in stored
        /// </summary>
        private readonly chbLine _line;

        /// <summary>
        /// Paragrap this line sits in
        /// </summary>
        private readonly chbParagraph _owner;

        /// <summary>
        /// Character position of the last line
        /// </summary>
        cNode _current_wrap_pos = null;

        /// <summary>
        /// Next line in a linked list of lines.
        /// </summary>
        internal chbLineWrapper NextLine;

        /// <summary>
        /// The metrics of the induvidual lines
        /// </summary>
        private LineMetrics First, Last;

        /// <summary>
        /// Width and height of this line
        /// </summary>
        private xSize _current_size;
        public xSize Size { get { return _current_size; } }

        /// <summary>
        /// Number of lines with vissible blocks in this wrapped line
        /// </summary>
        internal int VisLineCount
        {
            get
            {
                var first = First;
                int count = 0;
                while (first != null)
                {
                    if (first.HasVisBlocks)
                        count++;
                    first = First.Next;
                }
                return count;
            }
        }

        internal bool BlockLine { get { return _line.BlockLine; } }

        /// <summary>
        /// If line wrapping is complete.
        /// </summary>
        internal bool WrapDone { get { return _current_wrap_pos == null; } }

        /// <summary>
        /// If any blocks on this line depends on the size of the parent
        /// </summary>
        internal bool SizeDependsOnParent { get { return _line.SizeDependsOnParent; } }

        /// <summary>
        /// Called by children when SizeDependsOnParent changes
        /// </summary>
        /// <param name="new_val">The new value</param>
        internal void UpdateSizeDep(bool new_val)
        {
            _owner.NeedParentSize = new_val;
        }

        internal chbLineWrapper(cNode box, chbParagraph owner)
        {
            _line = new chbLine(box) { LineWrapper = this };
            _current_wrap_pos = _line.First;
            _owner = owner;
        }

        private chbLineWrapper(chbLine line, chbParagraph owner)
        {
            if (line.NextLine != null)
                throw new NotSupportedException("Can't have multiple lines");
            if (owner == null) throw new ArgumentNullException();
            _line = line;
            line.LineWrapper = this;
            _current_wrap_pos = _line.First;
            _owner = owner;
        }

        internal void Append(cNode block)
        {
            _line.Append(block);

            //Removes data for the last measured line.
            if (WrapDone)
            {
                _current_wrap_pos = Last.Start;

                //The last line must be remeasured, so we remove Last.
                //Should perhaps have a "before last" variable, but for
                //now we do a search.
                if (ReferenceEquals(First, Last))
                {
                    First = Last = null;
                    _current_size = new xSize();
                }
                else
                {
                    var next = First;
                    while (!ReferenceEquals(next.Next, Last))
                        next = next.Next;

                    //Shrinks the height down. Width is left alone,
                    //as we'd have to check every line to get the
                    //width correct.
                    _current_size = new xSize(_current_size.Width, _current_size.Height - Last.Height);

                    next.Next = null;
                    Last = next;
                }
            }
        }

        /// <summary>
        /// Cuts a line wrapper into up to 3 pieces
        /// </summary>
        /// <param name="block">Block to cut on</param>
        /// <returns>Last line wrapper</returns>
        internal chbLineWrapper Cut(cBox block)
        {
            var current = block.VisLine;
            if (!ReferenceEquals(this, current.LineWrapper))
                throw new PdfInternalException("Block not on this line");
            int nr = current.Cut(block);

            //We cut up the line wrapper into up to three line wrappers
            chbLine next = current.NextLine;
            chbLineWrapper wrap = this;
            while (next != null)
            {
                current.NextLine = null;
                current = next;
                next = next.NextLine;
                current.NextLine = null;
                wrap = wrap.NextLine = new chbLineWrapper(current, _owner);
            }

            return wrap;
        }

        internal void Join(chbLineWrapper line)
        {
            _line.Join(line._line);
        }

        /// <summary>
        /// Do not call this method directly, doesn't remove listeners.
        /// </summary>
        internal void Remove(cNode block)
        {
            _line.Remove(block);

            //Updates size calculations as needed. Note, could ignore "absolute" and "none" blocks, as they do not have size.
            if (block.HasLayout)
            {
                var wl = block.VisWrapLine;
                block.VisWrapLine = null;
                if (wl == null)
                    throw new PdfInternalException("Has layout, but no visual line.");

                double line_height = wl.Height, block_height = block.VisHeight,
                       line_width = wl.Width;
                //We may have to reflow, but only if:
                // - There's only one block on the line, which means its size becomes 0, 0
                // - Line height equals block height, which means this block might be the tallest one on the line
                // - The line's width is equal to the container's width, which means this might be the widest line in the container
                if (wl.SingleBlock)
                {
                    //Removes the visual wrap line (in the First to Last linked list)
                    var prev = Prev(wl);
                    if (prev == null)
                    {
                        First = wl.Next;
                        if (First == null)
                            Last = null;
                    }
                    else
                        prev = wl.Next;

                    CalcSize();
                }
                else if (line_height == block_height || line_width == Size.Width)
                {
                    //Removes just the block
                    wl.Remove(block);

                    //Remeasures the size
                    CalcSize();
                }
                else
                {
                    wl.Remove(block);
                }
            }
        }

        private void CalcSize()
        {
            double width = 0, height = 0;
            var next = First;
            while (next != null)
            {
                width = Math.Max(width, next.Width);
                height += next.Height;
                next = next.Next;
            }

            _current_size = new xSize(width, height);
        }

        private LineMetrics Prev(LineMetrics lm)
        {
            LineMetrics next = First, cur = null;
            while (next != null)
            {
                if (ReferenceEquals(next, lm))
                    return cur;
                next = next.Next;
            }

            throw new PdfInternalException("metrics not in wrapper");
        }

        /// <summary>
        /// Sets the width of a line, but allows for overflow.
        /// </summary>
        /// <param name="width">The width</param>
        /// <param name="state">Current state</param>
        /// <returns>Height of the line or -1 if there is no such line</returns>
        public LineMetrics FlowLineWidth(double width, cTextStat stats, cBox anchor, out cNode overflow)
        {
            return FlowLineWidth(width, _current_wrap_pos, _line.Last, stats, anchor, out overflow, false);
        }

        /// <summary>
        /// Tries to set the width of a line, allows for overflow.
        /// </summary>
        /// <param name="width">The width</param>
        /// <param name="state">Current state</param>
        /// <returns>Height of the line or null if it was unable to get a line</returns>
        public LineMetrics TryFlowLineWidth(double width, cTextStat stats, cBox anchor, out cNode overflow)
        {
            return FlowLineWidth(width, _current_wrap_pos, _line.Last, stats, anchor, out overflow, true);

            //Eh. The Linked List "First and Last" needs to be modified. So I added a param to FlowLineWidth
            //that just returns before any modifications are done.
            //var hold = _current_wrap_pos;
            //var lm = FlowLineWidth(width, _current_wrap_pos, _line.Last, stats, anchor, out overflow, true);
            //if (lm.Width > width || lm.Width == 0)
            //{
            //    _current_wrap_pos = hold;
            //    return null;
            //}
            //return lm;
        }

        /// <summary>
        /// Sets the width of a line.
        /// </summary>
        /// <param name="width">The width</param>
        /// <param name="state">Current state</param>
        /// <returns>Height of the line or -1 if there is no such line</returns>
        public LineMetrics SetLineWidth(double width, cTextStat stats, cBox anchor)
        {
            return SetLineWidth(width, _current_wrap_pos, _line.Last, stats, anchor);
        }

        /// <summary>
        /// For when this line is to be line wrapped from scratch
        /// </summary>
        public void Reset()
        {
            _current_size = new xSize();
            _current_wrap_pos = _line.First;
            First = Last = null;
        }

        /// <summary>
        /// Sets the width of a line, but allows for overflow.
        /// </summary>
        /// <param name="width">The width</param>
        /// <param name="state">Current state</param>
        /// <param name="start">Start character index</param>
        /// <param name="end">End character index</param>
        /// <returns>Returns null when there are no more lines to set</returns>
        private LineMetrics FlowLineWidth(double width, cNode start_box, cNode end_box, cTextStat stats, cBox anchor, out cNode overflow, bool try_width)
        {
            if (start_box == null)
            {
                //Special casing empty lines.
                _current_wrap_pos = null;
                var empty_line = new LineMetrics(null, null, _line.Measure(null, null, width, stats, anchor), width);
                if (Last == null)
                    First = Last = empty_line;
                else
                    Last = Last.Next = empty_line;
                overflow = null;
                return empty_line;
            }
            var w = _line.FlowMeasure(start_box, end_box, width, stats, anchor, out overflow);
            if (try_width && (w.Width > width || w.Width == 0))
                return null;
            var lm = new LineMetrics(start_box, w.LastBlock, w, width);
            _current_wrap_pos = w.LastBlock.VisNext;
            if (Last == null)
                First = Last = lm;
            else
                Last = Last.Next = lm;
            _current_size = new xSize(Math.Max(_current_size.Width, lm.Width), _current_size.Height + lm.Height);
            return lm;
        }

        /// <summary>
        /// Sets the width of a line.
        /// </summary>
        /// <param name="width">The width</param>
        /// <param name="state">Current state</param>
        /// <param name="start">Start character index</param>
        /// <param name="end">End character index</param>
        /// <returns>Returns null when there are no more lines to set</returns>
        private LineMetrics SetLineWidth(double width, cNode start_box, cNode end_box, cTextStat stats, cBox anchor)
        {
            if (start_box == null)
            {
                //Special casing empty lines.
                _current_wrap_pos = null;
                var empty_line = new LineMetrics(null, null, _line.Measure(null, null, width, stats, anchor), width);
                if (Last == null)
                    First = Last = empty_line;
                else
                    Last = Last.Next = empty_line;
                return empty_line;
            }
            var w = _line.Measure(start_box, end_box, width, stats, anchor);
            var lm = new LineMetrics(start_box, w.LastBlock, w, width);
            _current_wrap_pos = w.LastBlock.VisNext;
            if (Last == null)
                First = Last = lm;
            else
                Last = Last.Next = lm;
            _current_size = new xSize(Math.Max(_current_size.Width, lm.Width), _current_size.Height + lm.Height);
            return lm;
        }

        #region IEnumerable

        IEnumerator<chbLineWrapper.LineMetrics> IEnumerable<chbLineWrapper.LineMetrics>.GetEnumerator()
        {
            var first = First;
            while (first != null)
            {
                yield return first;
                first = first.Next;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        { return ((IEnumerable<chbLineWrapper.LineMetrics>)this).GetEnumerator(); }

        #endregion

        /// <summary>
        /// Append, layout and wrap to the next line. 
        /// </summary>
        /// <param name="block">Block to append</param>
        /// <returns>True if there's any potential size changes</returns>
        //internal bool WrapAppend(cBox block)
        //{
        //    //1. Find the Line stub this block is on
        //    var stub = block.VisWrapLine;

        //    if (stub == null)
        //    {
        //        if (Last == null)
        //        {
        //            //Parent has no lines at all, so we must create a new wrap line.
        //            return true;
        //        }
        //        else
        //        {
        //            //This only happens when a single block is appended to a line, then
        //            //layed out with a call to this method.
        //            if (Last.End == null)
        //                Last.Start = Last.End = block;
        //            else
        //            {
        //                if (!ReferenceEquals(Last.End.VisNext, block))
        //                    throw new PdfInternalException("Block not on line.");
        //                Last.End = block;
        //            }
        //            block.VisWrapLine = Last;
        //        }
        //        stub = Last;
        //    }

        //    //2. DoLayout with "max width"
        //    var state = stub.StartState.Copy();
        //    cLineMeasure w = _line.Measure(stub.Start, stub.End, state, stub.MaxWidth);

        //    //3. Flow changes downwards, unless this is the last block of course
        //    if (!ReferenceEquals(stub.End, w.LastBlock))
        //    {
        //        //First, the stub must be shortened.
        //        stub.Set(w, w.LastBlock);

        //        //Must create new lines
        //        _last_line_pos = w.LastBlock;

        //        //Caller handles layout
        //        return true;
        //    }

        //    return false;
        //}

        /// <summary>
        /// Metrics for a part of the line
        /// </summary>
        internal class LineMetrics : IEnumerable<cNode>
        {
            internal cNode Start, End;
            internal double Width, MaxWidth, Height;

            /// <summary>
            /// The minimum width of the biggest block on the line.
            /// </summary>
            internal double MinBlockWidth;

            /// <summary>
            /// Offset from top of content container, to top of line.
            /// </summary>
            internal double OffsetY;

            /// <summary>
            /// Offset from bottom of container to bottom of line.
            /// </summary>
            /// <remarks>
            /// Since PDF goes from bottom up, this is a more convinient value.
            /// </remarks>
            internal double BottomY;

            internal LineMetrics Next;
            internal bool SizeDepOnParent;
            internal bool SingleBlock { get { return ReferenceEquals(Start, End); } }
            internal bool HasVisBlocks
            {
                get
                {
                    var next = Start;
                    if (next == null) return false;
                    while (true)
                    {
                        if (next.TakesUpSpace)
                            return true;
                        if (ReferenceEquals(next, End))
                            return false;
                        next = next.VisNext;
                    }
                }
            }

            internal int VisBlockCount
            {
                get
                {
                    int count = 0;
                    var next = Start;
                    if (next == null) return 0;
                    while (true)
                    {
                        if (next.TakesUpSpace)
                            count++;
                        if (ReferenceEquals(next, End))
                            return count;
                        next = next.VisNext;
                    }
                }
            }

            internal LineMetrics(cNode start, cNode end, cLineMeasure lm, double max_width)
            {
                Start = start;
                End = end;
                Set(lm);
                MaxWidth = max_width;
                if (start != null)
                {
                    start.VisWrapLine = this;
                    while (!ReferenceEquals(start, end))
                    {
                        start = start.VisNext;
                        start.VisWrapLine = this;
                    }
                }
            }
            private void Set(cLineMeasure lm)
            {
                
                Width = lm.Width;
                Height = lm.Height;
                SizeDepOnParent = lm.NeedParentSize;
                MinBlockWidth = lm.MinSingleBlockWidth;
            }
            internal void Remove(cNode block)
            {
                if (ReferenceEquals(block, Start))
                {
                    if (ReferenceEquals(block, End))
                        Start = End = null;
                    else
                        Start = block.VisNext;
                }
                else if (ReferenceEquals(Start, End))
                    throw new PdfInternalException("Block not on line");
                else
                {
                    cNode cur = Start, next = cur.VisNext;
                    while (!ReferenceEquals(next, block))
                    {
                        if (ReferenceEquals(next, End))
                            throw new PdfInternalException("Block not on line");
                        cur = next;
                        next = next.VisNext;
                    }
                    End = cur;
                }
                SetSize();
            }

            /// <summary>
            /// Calculates the current height of the line
            /// </summary>
            private void SetSize()
            {
                var next = Start;
                if (next == null)
                {
                    Width = Height = 0;
                    return;
                }
                double height = 0, width = 0;
                SizeDepOnParent = false;
                while (true)
                {
                    if (!next.HasLayout)
                    {
                        height = double.NaN;
                        width = double.NaN;
                    }
                    if (next.SizeDependsOnParent)
                        SizeDepOnParent = true;
                    if (next.TakesUpSpace)
                    {
                        height = Math.Max(height, next.VisHeight);
                        width += next.VisWidth;
                    }

                    if (ReferenceEquals(next, End))
                        break;
                    next = next.VisNext;
                }
                Height = height;
                Width = width;
            }

            #region IEnumerable

            IEnumerator<cNode> IEnumerable<cNode>.GetEnumerator()
            {
                var next = Start;
                if (next != null)
                {
                    yield return next;
                    while (!ReferenceEquals(next, End))
                    {
                        next = next.VisNext;
                        yield return next;
                    }
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            { return ((IEnumerable<cNode>)this).GetEnumerator(); }

            #endregion
        }
    }
}
