using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using System.Text;

namespace PdfLib.Compose.Layout.Internal
{
    /// <summary>
    /// Compose Horizontal block Paragraph (Modeled after chParagraph)
    /// 
    /// Contains one ore more chbLineWrappers.
    /// 
    /// Handles the spliting and joining of lines that happen when blocks
    /// are changed between display "block" and display "inline".
    /// </summary>
    /// <remarks>
    /// Consider merging this class with cDiv. 
    /// </remarks>
    class chbParagraph
    {
        /// <summary>
        /// Lines in the paragraph
        /// </summary>
        chbLineWrapper First, Last, ResumeFrom;

        /// <summary>
        /// The total size of this paragraph
        /// </summary>
        internal xSize Size, MinSize, MaxSize;

        /// <summary>
        /// The descent of the last line in the paragraph
        /// </summary>
        internal double LastLineDescent { get; private set; }

        //Number of visible lines in the paragraph
        internal int VisLineCount
        {
            get
            {
                int count = 0;
                var first = First;
                while (first != null)
                {
                    count += first.VisLineCount;
                    first = first.NextLine;
                }
                return count;
            }
        }

        /// <summary>
        /// If any child needs the size of the parent
        /// </summary>
        private bool _need_parent_size;
        internal bool NeedParentSize 
        { 
            get { return _need_parent_size; }
            set
            {
                if (value != _need_parent_size)
                {
                    if (value)
                        _need_parent_size = true;
                    else
                    {
                        _need_parent_size = false;
                        var first = First;
                        while (first != null)
                        {
                            if (first.SizeDependsOnParent)
                            {
                                _need_parent_size = true;
                                break;
                            }
                            first = First.NextLine;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculates positions for children, in relation to the containers content box.
        /// </summary>
        /// <param name="style">Style to use</param>
        /// <param name="width">Content width of container</param>
        /// <param name="height">Content height of container</param>
        internal void CalcPositions(cStyle.Resolver style, double width, double height, cBox anchor)
        {            
            double start_pos, gap;

            //Determines how lines are to be positioned.
            switch (style.VerticalAlignemt)
            {
                default:
                case eVerticalPos.Top:
                    start_pos = 0;
                    gap = 0;
                    break;
                case eVerticalPos.Bottom:
                    //Can be negative. Moves bottom of content to align with bottom of container.
                    start_pos = height - Size.Height;
                    gap = 0;
                    break;
                case eVerticalPos.Center:
                    start_pos = (height - Size.Height) / 2;
                    gap = 0;
                    break;
                case eVerticalPos.Even:
                    start_pos = 0;
                    var lc = VisLineCount;
                    gap = (lc == 0) ? 0 : Math.Max(0, (height - Size.Height) / lc);
                    break;
            }

            double y_pos = start_pos;
            var next = First;
            while (next != null)
            {
                //Itterate over wraplines
                var next_enum = ((IEnumerable<chbLineWrapper.LineMetrics>)next).GetEnumerator();

                while (next_enum.MoveNext())
                {
                    var lm = next_enum.Current;

                    lm.OffsetY = y_pos;
                    lm.BottomY = height - y_pos - lm.Height;                   

                    //Aligns the blocks on the line itself.
                    double empty_width = width - lm.Width, hgap, x_pos;
                    switch (style.HorizontalAlignemt)
                    {
                        default:
                        case eHorizontalPos.Left:
                            x_pos = 0;
                            hgap = 0;
                            break;
                        case eHorizontalPos.Right:
                            x_pos = empty_width;
                            hgap = 0;
                            break;
                        case eHorizontalPos.Center:
                            x_pos = empty_width / 2;
                            hgap = 0;
                            break;
                        case eHorizontalPos.Column:
                            x_pos = 0;
                            var bc = lm.VisBlockCount;
                            hgap = (bc == 0) ? 0 : Math.Max(0, empty_width / bc);
                            break;
                    }
                    bool has_vis_blocks = false;

                 next_replaced:
                    foreach (var node in lm)
                    {
                        if (node is cBox)
                        {
                            var block = (cBox)node;

                            block.PosX = x_pos;
                            switch (style.LineAlignemt)
                            {
                                case eLinePos.Top:
                                    //PosY is at the top of the box, bottom y is the distance from the bottom of the
                                    //box to the bottom of the line. Both can therefore be zero.
                                    block.PosY = 0; 
                                    block.BottomY = lm.Height - block.VisFullHeight;
                                    break;
                                case eLinePos.Middle:
                                    block.PosY = (lm.Height - block.VisFullHeight) / 2;
                                    block.BottomY = block.PosY;
                                    break;
                                case eLinePos.Bottom:
                                    block.PosY = lm.Height - block.VisFullHeight;
                                    block.BottomY = 0;
                                    break;
                            }

                            if (block.TakesUpSpace)
                            {
                                has_vis_blocks = true;
                                x_pos += block.VisFullWidth + hgap;
                            }

                            block.UpdatePosition(anchor);

                            if (block.Style.Position == ePosition.Relative)
                            {
                                //Offsets from current point without affecting the layout
                                if (!double.IsNaN(block.VisLeft))
                                    block.PosX += block.VisLeft;
                                if (!double.IsNaN(block.VisTop))
                                {
                                    block.PosY += block.VisTop;
                                    block.BottomY -= block.VisTop;
                                }
                            }
                        }
                        else if (node is cText)
                        {
                            var text = (cText)node;

                            //All text nodes are visible.
                            has_vis_blocks = true;

                            Console.WriteLine("Positioning text");

                            //Sets the position of the first line.
                            var block = text.Parent;
                            double h, b;
                            switch (block.Style.LineAlignemt)
                            {
                                default:
                                case eLinePos.Top:
                                    h = 0;
                                    b = text.GetLineHeight(0) - text.VisFullHeight;
                                    break;
                                case eLinePos.Middle:
                                    h = (text.GetLineHeight(0) - text.VisFullHeight) / 2;
                                    b = h;
                                    break;
                                case eLinePos.Bottom:
                                    h = text.GetLineHeight(0) - text.VisFullHeight;
                                    b = 0;
                                    break;
                            }
                            text.SetPosition(0, x_pos, h, b);
                            double new_y_pos = y_pos + lm.Height + gap;

                            //Sets the position of the subsequent lines.
                            for (int c=1; c < text.Count; c++)
                            {
                                //Todo. This does not work, why?
                                var lh = text.GetLineHeight(c) + gap;
                                h -= lh; b -= lh; new_y_pos += lh;
                                text.SetPosition(c, 0, h, b);
                            }

                            //We've set the position of the "text box", we now tell said box to set the position
                            //of the text inside the box.
                            text.SetTextPosition();
  
                            //x_pos += text.VisFullWidth + hgap;
                            double llw = text.GetLastLineWidth();
                            x_pos = llw + hgap;

                            if (text.Attached)
                            {
                                if (!next_enum.MoveNext())
                                    throw new PdfInternalException("There must always be a line following an attache text box");

                                //The next line is to follow right on this text line
                                lm = next_enum.Current;
                                lm.Height = text.GetLineHeight(text.Count - 1); //<-- This height value is always correct, while lm.height may be too small.


                                //Got to adjust the ypos to match the current line, instead of the next line.
                                y_pos = new_y_pos - lm.Height - gap;

                                lm.OffsetY = y_pos;
                                lm.BottomY = height - y_pos - lm.Height;

                                goto next_replaced;
                            }
                            else if (text.Count > 1)
                            {
                                //The last line in the text box fills an entire line. We therefore got to adjust the y_pos and
                                //break out of this loop. Note that "- lm.Height - gap" is added after the loop, so that's why we remove it. 
                                y_pos = new_y_pos - lm.Height - gap;
                                //Note: lm.height will always be == the height of the last line in the block. This because we know
                                //      there are no other lines affecting the height of this line as is the case when text.attached

                                //Arguably, continue also works here as there should never be anymore blocks.
                                break;
                            }

                            //The next block is on the same y_pos, yet the text box is not attached. A bit confusing, but
                            //it happens when a text box fits snuggle on a single line and is then followed by another box.
                            continue;
                        }
                    }

                    y_pos += lm.Height;

                    if (has_vis_blocks)
                        y_pos += gap;
                }
                
                next = next.NextLine;
            }
        }

        /// <summary>
        /// If line wrapping is being preformed
        /// </summary>
        internal bool IsWrapping { get { return ResumeFrom != null; } }

        internal void Reset()
        {
            ResumeFrom = First;
            Size = MinSize = MaxSize = new xSize();
            if (ResumeFrom != null)
                ResumeFrom.Reset();
        }

        internal TryFlowResult TryFlowWidthOnNextLine(double max_width, cTextStat stats, cBox anchor, out cNode node)
        {
            if (ResumeFrom == null)
            {
                node = null;
                return TryFlowResult.DONE;
            }

            stats.ResetDecent();

            var lm = ResumeFrom.TryFlowLineWidth(max_width, stats, anchor, out node);
            if (lm == null)
                return TryFlowResult.FAIL;
            LastLineDescent = stats.MaxDecent;
            if (ResumeFrom.WrapDone)
            {
                ResumeFrom = ResumeFrom.NextLine;
                if (ResumeFrom != null)
                    ResumeFrom.Reset();
            }
            Size = new xSize(Math.Max(lm.Width, Size.Width), Size.Height + lm.Height);
            MinSize = new xSize(Math.Max(lm.MinBlockWidth, MinSize.Width), Math.Max(MinSize.Height, lm.Height));

            return TryFlowResult.OK;
        }

        internal enum TryFlowResult
        {
            OK,
            FAIL,
            DONE
        }

        internal chbLineWrapper.LineMetrics FlowWidthOnNextLine(double max_width, cTextStat stats, cBox anchor, out cNode node)
        {
            if (ResumeFrom == null)
            {
                node = null;
                return null;
            }

            var lm = ResumeFrom.FlowLineWidth(max_width, stats, anchor, out node);
            if (ResumeFrom.WrapDone)
            {
                ResumeFrom = ResumeFrom.NextLine;
                if (ResumeFrom != null)
                    ResumeFrom.Reset();
            }
            Size = new xSize(Math.Max(lm.Width, Size.Width), Size.Height + lm.Height);
            MinSize = new xSize(Math.Max(lm.MinBlockWidth, MinSize.Width), Math.Max(MinSize.Height, lm.Height));

            return lm;
        }

        internal chbLineWrapper.LineMetrics SetWidthOnNextLine(double max_width, cTextStat stats, cBox anchor)
        {
            if (ResumeFrom == null)
                return null;

            var lm = ResumeFrom.SetLineWidth(max_width, stats, anchor);
            if (ResumeFrom.WrapDone)
            {
                ResumeFrom = ResumeFrom.NextLine;
                if (ResumeFrom != null)
                    ResumeFrom.Reset();
            }
            Size = new xSize(Math.Max(lm.Width, Size.Width), Size.Height + lm.Height);
            MinSize = new xSize(Math.Max(lm.MinBlockWidth, MinSize.Width), Math.Max(MinSize.Height, lm.Height));

            return lm;
        }

        /// <summary>
        /// Do not call this method directly. 
        /// </summary>
        internal void Remove(cNode block)
        {
            //1. Alter the structure
            chbLine line = block.VisLine;
            if (line == null || line.LineWrapper == null)
                throw new PdfInternalException("Block not in paragraph");

            var wrap = line.LineWrapper;
            if (line.SingleBlock)
            {
                //Must remove the whole line;
                if (ReferenceEquals(wrap, ResumeFrom))
                    ResumeFrom = wrap.NextLine;
                var prev = FindPrevLine(line.LineWrapper);

                if (prev == null)
                    First = Last = null;
                else
                {
                    prev.NextLine = wrap.NextLine;
                    if (ReferenceEquals(Last, wrap))
                        Last = prev;
                }
            }
            else
                wrap.Remove(block);

            //2. Updates need parent's size
            if (block.SizeDependsOnParent)
            {
                //Size is now potentially set false
                _need_parent_size = false;
                var first = First;
                while (first != null)
                {
                    if (first.SizeDependsOnParent)
                    {
                        _need_parent_size = true;
                        break;
                    }
                    first = first.NextLine;
                }
            }
        }

        /// <summary>
        /// Adds a block to the last line
        /// </summary>
        /// <param name="block">Block that is to be added</param>
        internal void Append(cNode block)
        {
            if (Last == null)
            {
                //There is nothing in this paragraph, so we add the first block
                First = Last = ResumeFrom = new chbLineWrapper(block, this);
                _need_parent_size = First.SizeDependsOnParent;
            }
            else
            {
                if (Last.BlockLine || block.IsBlock)
                {
                    //Blocks are put on their own line
                    AddLine(new chbLineWrapper(block, this));
                }
                else
                {
                    //Otherwise put on the last avalible line
                    Last.Append(block);
                    if (ResumeFrom == null)
                    {
                        ResumeFrom = Last;

                        //Got to adjust height, so not have the height added multiple times.
                        Size = new xSize(Size.Width, Size.Height);
                    }
                    if (block.SizeDependsOnParent)
                        _need_parent_size = true;
                }
            }
        }

        private void AddLine(chbLineWrapper line)
        {
            Last.NextLine = line;
            Last = line;
            if (ResumeFrom == null)
                ResumeFrom = line;
            if (line.SizeDependsOnParent)
                _need_parent_size = true;
        }

        internal void BlockSizeChange(cBox child)
        {
            //1. Alter the structure
            var line = child.VisLine;
            if (line == null || line.LineWrapper == null)
            {
                if (!child.HasListeners)
                    return;

                throw new PdfInternalException("Block not in paragraph");
            }

            var wrap = line.LineWrapper;
            if (child.Style.Display == eDisplay.Block)
            {
                if (!line.SingleBlock)
                {
                    var last = wrap.Cut(child);
                    if (ReferenceEquals(wrap, Last))
                        Last = last;
                }
            }
            else if (line.SingleBlock)
            {
                var prev = FindPrevLine(wrap);
                if (prev != null && !prev.BlockLine)
                {
                    prev.Join(wrap);
                    prev.NextLine = wrap.NextLine;
                    if (ReferenceEquals(wrap, Last))
                        Last = prev;
                    wrap = prev;
                }
                var next = wrap.NextLine;
                if (next != null && !next.BlockLine)
                {
                    wrap.Join(next);
                    wrap.NextLine = next.NextLine;
                    if (ReferenceEquals(next, Last))
                        Last = wrap;
                }
            }
        }

        private chbLineWrapper FindPrevLine(chbLineWrapper line)
        {
            chbLineWrapper prev = null, current = First;
            while (!ReferenceEquals(line, current))
            {
                prev = current;
                current = current.NextLine;
                if (current == null)
                    throw new PdfInternalException("Failed to find line");
            }
            return prev;
        }
    }
}
