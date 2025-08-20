using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Render;
using PdfLib.Pdf.Primitives;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// A layout element that can contain other layout elements
    /// </summary>
    public class cDiv : cBox
    {
        /// <summary>
        /// Linear list of children
        /// </summary>
        private List<cNode> _children = new List<cNode>(1);

        //Structured list of children
        private chbParagraph _nodes = new chbParagraph();

        /// <summary>
        /// True size of the content
        /// </summary>
        public override xSize ContentSize { get { return _nodes.Size; } }

        public override xSize MinContentSize { get { return _nodes.MinSize; } }

        private bool _contains_text;

        #region Init

        public cDiv()
            : this(new cStyle())
        { }
        public cDiv(cStyle style)
            : base(style)
        { }

        #endregion

        #region Layout

        internal override bool VariableContentSize
        {
            get { return _nodes.NeedParentSize; }
        }

        /// <summary>
        /// Lays out the content of this div
        /// </summary>
        /// <param name="rs">Render state</param>
        /// <returns>If there is children whos size depends on the parent</returns>
        protected override bool LayoutContent(cBox anchor)
        {
            _nodes.Reset();
            var stats = new cTextStat();
            cNode node;
            while (_nodes.FlowWidthOnNextLine(this.VisContentWidth, stats, anchor, out node) != null)
            {
                if (node is cText && FlowTextToNextLine((cText)node, stats, anchor))
                {
                    //There is no more lines to lay out, as FlowTextToNextLine may call FlowWidthOnNextLine before it exits. 
                    break;
                }
            }

            if (stats.HasText)
            {
                _contains_text = true;
                UpdateTextParams(stats);
            }
            else
                _contains_text = false;

            return _nodes.NeedParentSize;
        }

        /// <summary>
        /// We set all remaning lines to "max_length". Later we might want to change this if
        /// flow left/right gets implemented. 
        /// </summary>
        /// <param name="text">Text node</param>
        /// <returns>If there is no more lines to flow</returns>
        private bool FlowTextToNextLine(cText text, cTextStat stats, cBox anchor)
        {
            //Recursion? And what about height. And how to restart normal layout. 
            //throw new NotImplementedException();
            while (true)
            {
                var size_pt = text.SetNextLineWidth(VisContentWidth);
                if (size_pt == null) break;
                //text must not change its size when doing "SetNextLineWidth"... or maybe it does need to do this.
                //Some thought/experimentation needed here.

                //I do belive increesing the _nodes size value can be done without needing to add dummy structures.
                _nodes.Size = new xSize(Math.Max(size_pt.Value.Width, _nodes.Size.Width), _nodes.Size.Height + size_pt.Value.Height);

                //Todo: Minimum size information is calculated by SetNextLineWidth, but the information isn't brought here. Fix this.
                //MinSize = new xSize(Math.Max(size_pt.Value.Width, MinSize.Width), Math.Max(MinSize.Height, lm.Height));

                //Positioning is done elsewhere, so worry not about positions just yet.
            }

            double width = VisContentWidth - text.GetLastLineWidth();
            if (width > 0)
            {
                double cur_height = _nodes.Size.Height;
                cNode node;
                switch(_nodes.TryFlowWidthOnNextLine(width, stats, anchor, out node))
                {
                    case chbParagraph.TryFlowResult.OK:
                        //Problem: The last line might get a different height. Another problem is that the height of the _nodes object
                        //         is adjusted with this new height. What we do is to set the height back, unless the new height is taller
                        //         in which case we adjust the height with this difference.
                        double new_height = _nodes.Size.Height, new_line_height = new_height - cur_height, old_line_height = text.GetLastLineHeight();
                        if (new_line_height > old_line_height)
                        {
                            cur_height = cur_height - old_line_height + new_line_height;

                            //The last text line must have the same height as the new line height. 
                            text.SetLastLineHeight(new_line_height);
                        }

                        //Directly sets the height.
                        _nodes.Size = new xSize(_nodes.Size.Width, cur_height);

                        //Notifies that this text box will be followed by a line
                        text.Attached = true;

                        if (node is cText)
                        {
                            //We use recursion. It's possible to eliminate recursion if we recode the caller a bit, but I find this
                            //code cleaner and recursion shouldn't be a problem. Only if there's a long series of text nodes that all 
                            //wraps will we run out of stack space.
                            return FlowTextToNextLine((cText)node, stats, anchor);
                        }

                        //There may be more lines, so we ret true. 
                        return true;

                        //Couldn't set line width, but there may be more lines.
                    case chbParagraph.TryFlowResult.FAIL:
                        text.Attached = false;
                        return false;

                        //We know there's nothing more to lay out.
                    case chbParagraph.TryFlowResult.DONE:
                        text.Attached = false;
                        return true;
                }
            }

            //We return true, since we haven't called _nodes.FlowWidthOnNextLine, thus we dont' know if there's more lines or not.
            text.Attached = false;
            return false;
        }

        ///// <summary>
        ///// Performs layout on children added since last time layout was run.
        ///// </summary>
        //protected override void ResumeContentLayout()
        //{
        //    if (!_nodes.IsWrapping)
        //        return;

        //    //Note: When resuming, the first line may be the last line of the
        //    //      previous layout pass. For this implementation, it does not
        //    //      matter, but something to keep in mind at least.
        //    while (_nodes.SetWidthOnNextLine(this.VisContentWidth) != null)
        //        ;
        //}

        protected override void BlockLayoutChanged(cBox child)
        {
            _nodes.BlockSizeChange(child);
        }


        internal override bool AddListeners()
        {
            var ret = base.AddListeners();
            foreach (var child in _children)
            {
                if (child.AddListeners())
                    DoLayoutContent = true;
            }
            return ret;
        }

        internal override void RemoveListeners()
        {
            base.RemoveListeners();
            foreach (var child in _children)
                child.RemoveListeners();
        }

        #endregion

        #region Text

        internal override bool ContainsText { get { return _contains_text; } }

        internal override double MaxDescent => _nodes.LastLineDescent;

        #endregion

        #region Position

        protected override void FlowContent(cBox anchor)
        {
            _nodes.CalcPositions(Style, VisContentWidth, VisContentHeight, anchor);
        }
        
        #endregion

        public cDiv AppendChild(cBox child)
        {
            if (child != null)
            {
                
                //lock (_children)
                {
                    child.Parent = this;
                    
                    _children.Add(child);
                    if (HasListeners)
                        child.AddListeners();
                    else
                        //We tell the child to make the display parameter current
                        child.Style.Resolve("Display");
                    _nodes.Append(child);
                }
            }

            return this;
        }

        public cDiv AppendChild(cText child)
        {
            if (child != null)
            {
                //lock (_children)
                {
                    child.Parent = this;

                    _children.Add(child);
                    if (HasListeners)
                        child.AddListeners();
                    
                    _nodes.Append(child);
                }
            }

            return this;
        }

        public void RemoveChild(cBox child)
        {
            if (child != null && child.Parent == this)
                child.Remove();
        }

        public void RemoveChild(cText child)
        {
            if (child != null && child.Parent == this)
                child.Remove();
        }

        protected override void RemoveChildImpl(cNode child)
        {
            //lock (_children)
            {
                _children.Remove(child);
                _nodes.Remove(child);
            }
        }

        #region Render

        protected override void DrawContent(IDraw draw, ref cRenderState state, cBox anchor)
        {
            if (_children.Count == 0) return;

            double move_x = VisBorderLeft + VisPaddingLeft,
                   move_y = VisBorderBottom + VisPaddingBottom;

            foreach (cNode node in _children)
            {
                if (node is cBox)
                {
                    var box = (cBox)node;

                    if (box.Style.Display != eDisplay.None)
                    {
                        switch (box.Style.Position)
                        {
                            case ePosition.Absolute:
                                anchor._anchor_list.Add(box);
                                break;

                            case ePosition.Relative:
                                if (!double.IsNaN(box.VisTop))
                                    move_y += box.VisTop;
                                goto case ePosition.Static;

                            case ePosition.Static:
                                state.Save(draw);
                                double mx = move_x + box.PosX;
                                double my = move_y + box.VisWrapLine.BottomY + box.BottomY;
                                //my = VisBorderTopHeight + VisPaddingTop + VisContentHeight - box.VisWrapLine.OffsetY + box.VisWrapLine.Height - box.PosX - box.VisFullHeight * 2;

                                if (mx != 0 || my != 0)
                                    state.PrependCTM(new xMatrix(mx, my), draw);
                                box.Render(draw, ref state, anchor);
                                state = state.Restore(draw);
                                break;
                        }
                    }
                }
                else if (node is cText)
                {
                    var text = (cText)node;

                    Console.WriteLine("Rendering text");
                    text.Render(move_x, move_y + text.VisWrapLine.BottomY, draw, ref state);
                }
            }
        }

        #endregion

        #region IEnumerable

        protected override IEnumerator<cNode> GetEnumeratorImpl()
        {
            return _children.GetEnumerator();
        }

        #endregion

        public override string ToString()
        {
            return "<div> { "+base.ToString()+" }";
        }
    }
}
