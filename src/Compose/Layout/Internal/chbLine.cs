using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLib.Compose.Layout.Internal
{
    /// <summary>
    /// A horisontal line of blocks(compose horisontal block line)
    /// 
    /// Contains one block or up to several inline blocks 
    /// </summary>
    internal class chbLine
    {
        /// <summary>
        /// Blocks on this line. Note, a line must always have at least one block
        /// </summary>
        internal cNode First, Last;

        /// <summary>
        /// How many blocks are on this line
        /// </summary>
        private int _count;

        public int Length { get { return _count; } }

        private bool _size_dep_on_par;
        internal bool SizeDependsOnParent 
        { 
            get { return _size_dep_on_par; }
            set
            {
                if (value != _size_dep_on_par)
                {
                    if (value)
                        _size_dep_on_par = true;
                    else
                        update_size_dep_par();
                    if (LineWrapper != null)
                        LineWrapper.UpdateSizeDep(_size_dep_on_par);
                }
            }
        }

        /// <summary>
        /// Next line in a linked list of lines.
        /// </summary>
        internal chbLine NextLine;

        /// <summary>
        /// The wrapper used to wrap this line
        /// </summary>
        internal chbLineWrapper LineWrapper;

        /// <summary>
        /// Blocklines has only one block, use with SingleBlock to make sure this is actually a blockline
        /// </summary>
        internal bool BlockLine
        {
            get { return First.IsBlock; }
        }

        /// <summary>
        /// If there's only a single block on this line
        /// </summary>
        internal bool SingleBlock
        {
            get { return ReferenceEquals(First, Last); }
        }

        internal chbLine(cNode block)
        {
            if (block == null) 
                throw new ArgumentNullException();
            _count = 1;
            block.VisLine = this;
            First = Last = block;
            _size_dep_on_par = block.SizeDependsOnParent;
            while (Last.VisNext != null)
            {
                Last = Last.VisNext;
                Last.VisLine = this;
                if (Last.SizeDependsOnParent)
                    _size_dep_on_par = true;
                _count++;
            }
        }

        internal void Append(cNode block)
        {
            if (block == null)
                throw new ArgumentNullException();
            if (block.SizeDependsOnParent)
                _size_dep_on_par = true;
            _count++;
            block.VisLine = this;
            Last.VisNext = block;
            Last = block;
        }

        private void update_size_dep_par()
        {
            var next = First;
            _size_dep_on_par = false;
            while (next != null)
            {
                if (next.SizeDependsOnParent)
                {
                    _size_dep_on_par = true;
                    break;
                }
                next = next.VisNext;
            }
        }

        /// <summary>
        /// Cuts the line up
        /// </summary>
        /// <param name="block">Block to cut on</param>
        /// <returns>How many lineparts there are</returns>
        internal int Cut(cNode block)
        {
            var prev = FindPrev(block);
            if (prev == null)
            {
                if (ReferenceEquals(block.VisLine, this))
                {
                    //Cutting off the first block
                    if (block.VisNext != null)
                    {
                        //Creates a new line for the other blocks
                        var next = new chbLine(block.VisNext);

                        //Cuts the current block off
                        block.VisNext = null;
                        Last = block;

                        //Attach our nextline to the new line's nextline
                        next.NextLine = NextLine.NextLine;

                        //Slot the new line after this one
                        NextLine = next;

                        //This is now a line with one single block, followed by a new line with
                        //the rest of the blocks
                        _count = 1;
                        if (_size_dep_on_par)
                            update_size_dep_par();
                        return 2;
                    }

                    //This is a line with a single block
                    return 1;
                }
                else
                    throw new PdfInternalException("Block not in list");
            }

            //Cuts away the block from this line
            prev.VisNext = null;
            Last = prev;

            //Creates a new line for the single block
            var n = block.VisNext;
            block.VisNext = null;
            block.VisLine = null;
            var next_line = NextLine;
            NextLine = new chbLine(block);
            _count--;

            if (n == null)
            {
                //There are no more blocks.
                NextLine.NextLine = next_line;
                if (_size_dep_on_par)
                    update_size_dep_par();
                return 2;
            }

            next_line = new chbLine(n) { NextLine = next_line };
            NextLine.NextLine = next_line;
            _count -= next_line.Length;
            if (_size_dep_on_par)
                update_size_dep_par();
            return 3;
        }

        internal void Join(chbLine line)
        {
            cNode first = line.First, next = first;
            if (first == null) return;
            do
            {
                _count++;
                next.VisLine = this;
                next = next.VisNext;
            } while (next != null);
            Last.VisNext = first;
            Last = line.Last;
            if (line.SizeDependsOnParent)
                _size_dep_on_par = true;
        }

        /// <summary>
        /// Do not call this method directly, doesn't remove listeners. (Also assumes length > 1)
        /// </summary>
        internal void Remove(cNode block)
        {
            var prev = FindPrev(block);
            if (prev == null)
                First = block.VisNext;
            else
            {
                var next = block.VisNext;
                prev.VisNext = next;
                if (next == null)
                    Last = prev;
            }
            if (block.SizeDependsOnParent && _size_dep_on_par)
                update_size_dep_par();
        }

        /// <summary>
        /// Supports text boxes that can flow from this line to the next.
        /// </summary>
        /// <param name="start">Start node</param>
        /// <param name="end">End node</param>
        /// <param name="max_length">The maximum length of the line</param>
        /// <param name="stats">Statistics for text</param>
        /// <param name="anchor">Anchor for absolutly positioned nodes</param>
        /// <returns>Measurments</returns>
        internal cLineMeasure FlowMeasure(cNode start, cNode end, double max_length, cTextStat stats, cBox anchor, out cNode overflow)
        {
            if (start == null)
                throw new ArgumentNullException();
            if (end == null)
                end = Last;
            if (!ReferenceEquals(start.VisLine, this) || !ReferenceEquals(end.VisLine, this))
                throw new PdfInternalException("Block not on line");
            cNode last = null;
            double width = 0, height = 0;
            int count = -1;
            bool need_parent_size = false;
            double max_width = 0;
            overflow = null;

            do
            {
                count++;
                if (start is cBox)
                {
                    var box = (cBox)start;
                    box.LayoutIfChanged(anchor);

                }
                else if (start is cText)
                {
                    //Text objects are a bit different, as they depend on how much space is left on the line.
                    //This means that the object can overflow to the next line.
                    double space_remaining = max_length - width;
                    System.Diagnostics.Debug.Assert(space_remaining > 0);
                    var text = (cText)start;

                    //When count is 0, we're the first block on the line. In that case
                    //we force the text to lay itself out even if there's not enough room.
                    var flow = text.LayoutIfChanged(space_remaining, count == 0);
                    if (flow == cText.FlowResult.OVERFLOW)
                    {
                        overflow = start;

                        //Since we got overflow, there's no space remaining. We therefore break out of the loop.
                        width = width + start.VisFullWidth;
                        height = Math.Max(height, start.VisFullHeight);
                        max_width = Math.Max(max_width, start.VisMinFullWidth);
                        last = start;
                        break;
                    }
                    else if(flow == cText.FlowResult.NONE)
                    {
                        //We push the cText down to the next line.
                        break;
                    }
                }

                //Asbolutely sized blocks and disp non blocks have size 0-0 in the layout system.
                if (start.TakesUpSpace)
                {
                    double new_width = width + start.VisFullWidth;
                    if (new_width > max_length && last != null)
                        break;
                    width = new_width;
                    height = Math.Max(height, start.VisFullHeight);

                    //Records the largest minimum width.
                    max_width = Math.Max(max_width, start.VisMinFullWidth);
                }


                if (start.SizeDependsOnParent)
                    need_parent_size = true;
                if (start.ContainsText)
                    start.AddStats(stats);

                last = start;
                start = start.VisNext;
            } while (!ReferenceEquals(last, end));

            return new cLineMeasure(last, width, count, height, need_parent_size, max_width);
        }

        /// <summary>
        /// Measures a line of boxes. Text is not supported. (Though cTextArea can be used)
        /// </summary>
        /// <param name="start">Start block</param>
        /// <param name="end">End block</param>
        /// <param name="max_length">Maximum length of the line</param>
        /// <param name="stats">Text statistics</param>
        /// <param name="anchor">Anchor for absolutly positioned text</param>
        /// <returns>Measurments</returns>
        internal cLineMeasure Measure(cNode start, cNode end, double max_length, cTextStat stats, cBox anchor)
        {
            if (start == null)
                throw new ArgumentNullException();
            if (end == null)
                end = Last;
            if (!ReferenceEquals(start.VisLine, this) || !ReferenceEquals(end.VisLine, this))
                throw new PdfInternalException("Block not on line");
            cNode last = null;
            double width = 0, height = 0;
            int count = -1;
            bool need_parent_size = false;
            double max_width = 0;

            do
            {
                count++;
                if (start is cBox)
                {
                    var box = (cBox)start;
                    box.LayoutIfChanged(anchor);

                    //Asbolutely sized blocks and disp non blocks have size 0-0 in the layout system.
                    if (start.TakesUpSpace)
                    {
                        double new_width = width + start.VisFullWidth;
                        if (new_width > max_length && last != null)
                            break;
                        width = new_width;
                        height = Math.Max(height, start.VisFullHeight);

                        //Records the largest minimum width.
                        max_width = Math.Max(max_width, start.VisMinFullWidth);
                    }


                    if (start.SizeDependsOnParent)
                        need_parent_size = true;
                    if (start.ContainsText)
                        start.AddStats(stats);
                }

                last = start;
                start = start.VisNext;
            } while (!ReferenceEquals(last, end));

            return new cLineMeasure(last, width, count, height, need_parent_size, max_width);
        }

        public cNode this[int index]
        {
            get 
            {
                if (index == _count - 1)
                    return Last;
                int count = 0;
                var first = First;
                while (count < index)
                {
                    count++;
                    first = first.VisNext;
                    if (first == null)
                        throw new IndexOutOfRangeException();
                }
                return first;
            }
        }

        private cNode FindPrev(cNode block)
        {
            var current = First;
            while (true)
            {
                var next = current.VisNext;
                if (next == null) return null;
                if (ReferenceEquals(next, block))
                    return current;
                current = next;
            }
        }
    }
}
