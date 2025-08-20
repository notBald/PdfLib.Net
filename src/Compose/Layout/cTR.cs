using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Render;
using PdfLib.Pdf.Primitives;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// Table row
    /// </summary>
    public class cTR : cBox
    {
        /// <summary>
        /// Linear of children
        /// </summary>
        private List<cBox> _children = new List<cBox>(1);

        /// <summary>
        /// Size of the content
        /// </summary>
        private double _width, _height, _min_width, _min_height;

        /// <summary>
        /// Actual size of the content
        /// </summary>
        public override xSize ContentSize
        {
            get 
            {
                if (_nodes != null)
                    return new xSize(Math.Max(_nodes.Size.Width, _width), _nodes.Size.Height + _height);
                return new xSize(_width, _height); 
            }
        }

        public override xSize MinContentSize
        {
            get
            {
                if (_nodes != null)
                    return new xSize(Math.Max(_nodes.MinSize.Width, _min_width), _nodes.MinSize.Height + _min_height);
                return new xSize(_min_width, _min_height); 
            }
        }

        public IEnumerable<cTD> Columns
        {
            get
            {
                foreach (var child in _children)
                {
                    if (child is cTD)
                        yield return (cTD)child;
                }
            }
        }

        internal override double MaxDescent => 0;

        //Structured list of children
        private chbParagraph _nodes = null;

        /// <summary>
        /// If any children has content size that vary
        /// </summary>
        private bool _var_content_size;

        #region Init

        public cTR()
            : this(new cStyle())
        { }
        public cTR(cStyle style)
            : base(style)
        { }

        #endregion

        #region Layout

        internal override bool VariableContentSize
        {
            get { return _var_content_size; }
        }

        protected override bool LayoutContent(cBox anchor)
        {
            _var_content_size = false;
            if (_nodes != null)
            {
                _nodes.Reset();
                var stats = new cTextStat();
                while (_nodes.SetWidthOnNextLine(this.VisContentWidth, stats, this) != null)
                    ;
                _var_content_size = _nodes.NeedParentSize;
            }

            double w = 0, h = 0, min_w = 0, min_h = 0;

            //Lays them out on a line
            foreach (var child in _children)
            {
                if (child.VisLine == null)
                {
                    child.LayoutIfChanged(this);
                    if (child.Style.Position != ePosition.Absolute && child.Display != eDisplay.None)
                    {
                        w += child.VisFullWidth;
                        min_w = child.VisMinFullWidth;

                        h = Math.Max(h, child.VisFullHeight);
                        min_h = Math.Max(min_h, child.VisMinFullHeight);
                    }

                    if (child.SizeDependsOnParent)
                        _var_content_size = true;
                }
            }
            _width = w;
            _height = h;
            _min_width = min_w;
            _min_height = min_h;

            return _var_content_size;
        }

        protected override void BlockLayoutChanged(cBox child)
        {
            if (_nodes != null)
            {
                if (child.VisLine != null)
                    _nodes.BlockSizeChange(child);
            }
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

        #region Position

        protected override void FlowContent(cBox anchor)
        {
            double pos_x = 0, pos_y = 0;

            if (_nodes != null)
            {
                _nodes.CalcPositions(Style, VisContentWidth, VisContentHeight, anchor);
                pos_y = _nodes.Size.Height;
            }

            double line_height = _height;

            //Positions them out on a line
            foreach (var child in _children)
            {
                if (child.VisLine == null)
                {
                    child.PosX = pos_x;
                    child.PosY = pos_y;
                    child.BottomY = line_height - child.VisFullHeight;
                    child.UpdatePosition(anchor);

                    pos_x += child.VisFullWidth;
                }
            }
        }

        #endregion

        public cTR AppendChild(cBox child)
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

                    if (!(child is cTD))
                    {
                        if (_nodes == null)
                            _nodes = new chbParagraph();
                        _nodes.Append(child);
                    }
                }
            }

            return this;
        }

        protected override void RemoveChildImpl(cNode node)
        {
            if (node is cBox)
            {
                var box = (cBox)node;

                lock (_children)
                {
                    _children.Remove(box);
                    _nodes.Remove(box);
                }
            }
        }

        #region Render

        protected override void DrawContent(IDraw draw, ref cRenderState state, cBox anchor)
        {
            if (_children.Count == 0) return;

            if (_nodes != null)
            {
                double move_x = VisBorderLeft + VisPaddingLeft, mx,
                       move_y = VisBorderBottom + VisPaddingBottom, my;

                foreach (cBox box in _children)
                {
                    if (box.Style.Display != eDisplay.None)
                    {
                        if (box.VisLine != null)
                        {
                            switch (box.Style.Position)
                            {
                                case ePosition.Absolute:
                                    anchor._anchor_list.Add(box);
                                    break;

                                case ePosition.Relative:
                                    if (box.VisTop != float.NaN)
                                        move_y += box.VisTop;
                                    goto case ePosition.Static;

                                case ePosition.Static:
                                    state.Save(draw);
                                    mx = move_x + box.PosX;
                                    my = move_y + box.VisWrapLine.BottomY + box.BottomY;
                                    if (mx != 0 || my != 0)
                                        state.PrependCTM(new xMatrix(mx, my), draw);
                                    box.Render(draw, ref state, anchor);
                                    state = state.Restore(draw);
                                    break;
                            }
                        }
                        else
                        {
                            state.Save(draw);
                            mx = move_x + box.PosX;
                            my = move_y + box.BottomY;
                            if (mx != 0 || my != 0)
                                state.PrependCTM(new xMatrix(mx, my), draw);
                            box.Render(draw, ref state, anchor);
                            state = state.Restore(draw);
                        }
                    }
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

        internal new void CalcBorders()
        {
            base.CalcBorders();
        }

        public override string ToString()
        {
            return "<tr> { " + base.ToString() + " }";
        }
    }
}
