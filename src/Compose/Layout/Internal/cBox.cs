#define OVERDRAW //Fixes thin white lines using overdraw, though it might be better to align the points to pixel bounderies.
#define QUADRACTIC_CURVES //Seems to work/look better than the semi-cubic

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Compose.Layout.Internal
{
    /// <summary>
    /// Base class for boxes
    /// </summary>
    /// <remarks>
    /// Event model:
    ///  - Not in tree:
    ///   - Does not fire events. By not having event listeners on shared objects, garbage collection will
    ///     work fine. Drawback is that various properties are unreliable.
    ///     
    ///     If a property must be read, call "Style.Resolve(property_name)" first.
    ///  - Put into tree:
    ///   - Avoids firing "style changed" events, instead you must override "AddListeners", call the base
    ///     function and the return bool tells you if any changes has been made to the style.
    ///  - While in tree:
    ///   - Calls "StyleChanged" every time a style property changes. Override the method to pick these up.
    ///   
    /// About curve drawing midpoint:
    ///  - When two borders with different colors meet, they got to meet somewhere. The current implementation
    ///    handles this by using the mathematical midpoint (0.5) of a quadratic curve. Unfortunatly the
    ///    matematical midpoint is not the same as the visual midpoint.
    ///  - For borders of even thickness, the visual midpoint corresponds with the mathematical midpoints. For
    ///    borders with the same color, it does not matter.
    ///  - How to find the visual midpoint? I'm not sure, but my current idea is to have a square formed around
    ///    the curve. Then form a diagonal line throught said square. Where the quadratic curve and the line
    ///    meets, might be the visual midpoint of the curve
    ///    
    ///     l1--------------------c2
    ///      |\           * * * * |     l1 = c1.x and c2.y  (I'm not using the coords of the control point here, but
    ///      |  \       *         |     l2 = c2.x and c1.y   perhaps using the CP/mirrored(1) CP accross the c1/c2 line 
    ///      |    \   *           |                          for l1, l2 would work better)
    ///      |      M             |     How to find M?
    ///      |     *  \           |     
    ///      |   *      \         |     First get the formula for the curve (2). Then solve that equation for the line 
    ///      | *          \       |     formula (3).
    ///      | *            \     |     
    ///      |*               \   |     Once you got the number, there's no need for anything else. Just plug it in place 
    ///      |*                 \ |     instead of that hardcoded 0.5.
    ///     c1--------------------l2    
    ///     
    ///     However, this is a significant chunk of coding for a feature that will never be used.
    ///     (1) https://stackoverflow.com/questions/3306838/algorithm-for-reflecting-a-point-across-a-line
    ///     (2) https://math.stackexchange.com/questions/1360891/find-quadratic-bezier-curve-equation-based-on-its-control-points
    ///     (3) https://stackoverflow.com/questions/27664298/calculating-intersection-point-of-quadratic-bezier-curve
    /// </remarks>
    public abstract class cBox : cNode, IEnumerable<cNode>
    {
        #region Variables and properties

        private cStyle.Resolver.ChangeHandler _style_changed = new cStyle.Resolver.ChangeHandler(StyleChanged);

        /// <summary>
        /// Used to store absolutly positioned children.
        /// </summary>
        internal List<cBox> _anchor_list = null;

        /// <summary>
        /// If this box takes up an entire line
        /// </summary>
        public override bool IsBlock => Style.Display == eDisplay.Block;

        #region Position

        /// <summary>
        /// If set, the box will have a position.
        /// </summary>
        public xPoint Offset
        {
            get; set;
        }

        //Todo: Consider changing this with a reference to cRoot
        //      that way we can see if we're added to the same tree
        //
        //      Or, prevent adding boxes that has this true, as then
        //      it's already in a tree. Might not need to know what
        //      tree IOW.
        //
        protected bool _in_visual_tree = false;

        #endregion

        #region Appearance

        private readonly cStyle.Resolver _style;
        public cStyle.Resolver Style
        {
            get { return _style; }
        }

        /// <summary>
        /// How much to overdraw border curves to prevent white lines
        /// </summary>
        private const double Overdraw_curve = 0.003;

        /// <summary>
        /// How much to overdraw border corners to prevent white lines
        /// </summary>
        private const double Overdraw_corner = 0.2;

        #endregion

        #region Size

        public eDisplay Display
        {
            get
            {
                if (!HasListeners)
                    _style.Resolve("Display");
                return _style.Display;
            }
        }

        #endregion

        #region State

        private bool _layout_content;
        internal bool DoLayoutContent
        {
            get { return _layout_content; }
            set
            {
                if (value)
                {
                    SetLayoutContent();

                    //Needs to be set false since BottomY may have to be recalculated.
                    // (BottomY = Parent.VisContentHeight - PosY - VisContentHeight)
                    //if (_has_position)
                    //{
                    //    _has_position = false;
                    //    if (_parent != null)
                    //        _parent.Reposition = true;
                    //}
                }
            }
        }
        private void SetLayoutContent()
        {
            if (!_layout_content)
            {
                _layout_content = true;

                //Quickfix
                //
                //Problem: content must be "reflowed" when size of the content changes.
                //         this in part because BottomY must be updated. _has_position only
                //         need to be set false when the content changes size _and_ this is
                //         the root node. Otherwise we can set Reposition = true.
                //         (_has_position might also need being set false when this box changes
                //          size. Not 100% sure either way)
                //
                //For now we just set this false. This means a bunch of needless calculations
                //will be done, but we don't need to test this implementation, it will work.
                _has_position = false;

                if (Parent != null)
                    Parent.SetLayoutContent();
            }
        }

        /// <summary>
        /// This box has position. Either relative to the parent, or the anchor.
        /// </summary>
        /// <remarks>
        /// By keeping position related calculations relative, we don't have to
        /// recalculate position just because the parent changes position.
        /// </remarks>
        private bool _has_position;
        internal bool HasPosition { get { return _has_position; } }

        /// <summary>
        /// When true, all children in the tree has position. 
        /// </summary>
        /// <remarks>
        /// Note, when false, the flags _childs_dep_anchor and _has_abs_children
        /// can be considered invalid. 
        /// </remarks>
        private bool _child_has_position;

        /// <summary>
        /// Tells children that they must recalc their positions.
        /// </summary>
        private bool _reposition;

        /// <summary>
        /// Tells a child that this anchor has changed its layout. Which means
        /// it must reposition itself
        /// </summary>
        internal bool Reposition 
        {
            get { return _reposition; }
            private set
            {
                _reposition = value;
                if (value)
                    ChildNeedPosition();
            }
        }

        /// <summary>
        /// Position of this block, relevant to the "VisWrapLine".
        /// </summary>
        internal double PosX, PosY;

        /// <summary>
        /// Position from the bottom of the "VisWrapLine" to the bottom of the box
        /// </summary>
        internal double BottomY;

        private void ChildNeedPosition()
        {
            if (_child_has_position)
            {
                _child_has_position = false;
                var par = Parent;
                while (par != null)
                {
                    par._child_has_position = false;
                    par = par.Parent;
                }
            }
        }

        protected bool _has_listeners;
        internal override bool HasListeners { get { return _has_listeners; } }

        /// <summary>
        /// Tracks whenever this box has a layout already
        /// </summary>
        private bool _has_layout;
        internal override bool HasLayout { get { return _has_layout && !(_layout_content && SizeDependsOnContent); } }

        /// <summary>
        /// Remeber to check HasListeners before calling this method
        /// 
        /// Keep in mind that all children will have the same HasListeners value,
        /// so only the parent's value needs to be checked.
        /// </summary>
        /// <returns>True if style has changed</returns>
        internal override bool AddListeners()
        {
            _has_listeners = true;
            _has_position = false;
            bool ret;
            if (_has_layout)
            {
                ret = _style.ReAddListeners();
                if (SizeDependsOnParent)
                    _has_layout = false;
                else 
                    _has_layout = !ret;
            }
            else
            {
                _style.AddListeners();
                ret = true;
            }

            if (PositionDependsOnAnchor)
                Parent.SetChildrenDependsOnAnchor(this);

            return ret;
        }

        /// <summary>
        /// Remeber to check HasListeners before calling this method
        /// </summary>
        internal override void RemoveListeners()
        {
            _has_listeners = false;
            _style.RemoveListeners();
        }

        /// <summary>
        /// Tells if children depends on the anchor in any way.
        /// </summary>
        private bool _childs_dep_anchor;

        /// <summary>
        /// This flag is kept up to date, but besides that it's used
        /// for nothing. 
        /// </summary>
        private bool _has_abs_children;

        //Todo "remove children dep on anchor
        /// <summary>
        /// Sets the flag that this container has children that depends on an anchor
        /// </summary>
        /// <param name="child">A child, not nesseseraly a direct child of this box</param>
        private void SetChildrenDependsOnAnchor(cBox child)
        {
            _childs_dep_anchor = true;

            //If a child is absolutly positioned, we may have to notify
            //a parent. Note, the parent will then assume that this box is
            //static.
            if (child.Style.Position == ePosition.Absolute
                && Style.Position == ePosition.Static)
            {
                _has_abs_children = true;
                Parent.SetChildrenDependsOnAnchor(child);
            }
        }

        /// <summary>
        /// The actual size of the content. Can be smaller/bigger than visual content size.
        /// </summary>
        public abstract xSize ContentSize { get; }

        /// <summary>
        /// The actual minimum content size.
        /// </summary>
        public virtual xSize MinContentSize { get { return ContentSize; } }

        /// <summary>
        /// Box no longer considers itself layed out
        /// </summary>
        internal override void InvalidateLayout()
        {
            _has_layout = false;
        }

        #endregion

        #endregion

        #region Public properties

        /// <summary>
        /// Height of the box, excluding margins.
        /// </summary>
        public double Height {  get { return VisHeight; } }

        /// <summary>
        /// Width of the box, excluding margins.
        /// </summary>
        public double Width { get { return VisWidth; } }

        /// <summary>
        /// Space avalible to content
        /// </summary>
        public xSize ContentSpace
        {
            get { return new xSize(VisContentWidth, VisContentHeight); }
        }

        #endregion

        #region Visual variables and properties

        /// <summary>
        /// When false, content width is not useful for calculating the size of children.
        /// </summary>
        internal bool HasWidth { get { return !double.IsNaN(VisContentWidth); } }

        /// <summary>
        /// When false, content height is not useful for calculating the size of children.
        /// </summary>
        internal bool HasHeight { get { return !double.IsNaN(VisContentHeight); } }

        /// <summary>
        /// If this block has size in both dimensions.
        /// </summary>
        internal bool HasSize { get { return HasWidth && HasHeight; } }

        /// <summary>
        /// If this block takes up space in the layout
        /// </summary>
        internal override bool TakesUpSpace => Display != eDisplay.None && Style.Position != ePosition.Absolute;

        /// <summary>
        /// Width including margin
        /// </summary>
        internal override double VisFullWidth { get { return VisMarginLeft + VisWidth + VisMarginRight; } }

        /// <summary>
        /// Height including margin
        /// </summary>
        internal override double VisFullHeight { get { return VisMarginTop + VisHeight + VisMarginBottom; } }

        /// <summary>
        /// Minimum width including margin
        /// </summary>
        internal override double VisMinFullWidth { get { return VisMarginLeft + VisMinWidth + VisMarginRight; } }

        /// <summary>
        /// Minimum height including margin
        /// </summary>
        internal override double VisMinFullHeight { get { return VisMarginTop + VisMinHeight + VisMarginBottom; } }

        /// <summary>
        /// Width and height of the content.
        /// </summary>
        internal double VisContentWidth, VisContentHeight;

        /// <summary>
        /// Total minimum width and height
        /// </summary>
        internal double VisMinWidth, VisMinHeight;

        /// <summary>
        /// Minumim width and height of the content.
        /// </summary>
        internal double VisMinContentWidth, VisMinContentHeight;

        /// <summary>
        /// Width and height of the borders;
        /// </summary>
        internal double VisBorderLeft, VisBorderRight, VisBorderTop, VisBorderBottom;

        /// <summary>
        /// Width and height of the padding
        /// </summary>
        internal double VisPaddingLeft, VisPaddingRight, VisPaddingTop, VisPaddingBottom;

        /// <summary>
        /// Width and height of the margin
        /// </summary>
        internal double VisMarginLeft, VisMarginRight, VisMarginTop, VisMarginBottom;

        /// <summary>
        /// If this box uses the parent's size.
        /// </summary>
        internal override bool SizeDependsOnParent => _style.HasPrecentSize;

        /// <summary>
        /// Anchor is the element this box is relativly positioned towards.
        /// </summary>
        internal bool PositionDependsOnAnchor
        {
            //Not 100% correct, correct behavior:
            // Abs: true if Bottom or right set, or if precentage in left/top
            // Rel: true if precentage in top/left, right/bottom has no meanning.
            //However, this will only err on the positive side, so no biggie as it just means needless extra calcs.
            get { return _style.Position != ePosition.Static && (_style.HasPrecentPosition || !float.IsNaN(_style.Right.Value) || !float.IsNaN(_style.Bottom.Value)); }
        }

        /// <summary>
        /// If the size of this box depends on the size of the child boxses
        /// </summary>
        internal bool SizeDependsOnContent
        {
            get { return _style.HasAutoSize; }
        }

        /// <summary>
        /// If the size of the content depends on size of the partent.
        /// 
        /// In this case, content need to be layed out when size changes.
        /// </summary>
        internal abstract bool VariableContentSize { get; }

        /// <summary>
        /// Visual border radious
        /// </summary>
        internal double VisBRadUL, VisBRadUR, VisBRadLL, VisBRadLR;

        /// <summary>
        /// Visual positions
        /// </summary>
        internal double VisLeft, VisRight, VisTop, VisBottom;

        internal cBrush BorderColorLeft { get { return _style.BorderColorLeft; } }
        internal cBrush BorderColorRight { get { return _style.BorderColorRight; } }
        internal cBrush BorderColorTop { get { return _style.BorderColorTop; } }
        internal cBrush BorderColorBottom { get { return _style.BorderColorBottom; } }
        internal xDashStyle? DashLeft { get { return _style.DashLeft; } }
        internal xDashStyle? DashRight { get { return _style.DashRight; } }
        internal xDashStyle? DashTop { get { return _style.DashTop; } }
        internal xDashStyle? DashBottom { get { return _style.DashBottom; } }

        #endregion

        #region Init

        protected cBox() : this(new cStyle())
        { }
        protected cBox(cStyle style)
        {
            _style = new cStyle.Resolver(style, this);
            _style.StyleChanged += _style_changed;
        }

        #endregion

        #region Layout

        /// <summary>
        /// Tries calculating the size of the block. Only for use one nodes that are not
        /// in a document.
        /// </summary>
        /// <returns>A calculated size, or null if calculation isn't possible.</returns>
        /// <remarks>
        /// This will mess up position flags, which is why it's restricted to only be used
        /// when _has_listeners is false. 
        /// </remarks>
        public xSize? TryLayout()
        {
            if (this._has_listeners)
                throw new NotSupportedException("Only for use on a node that is outside a document");

            this._style.ResolveAll();
            if (SizeDependsOnParent)
                return null;

            Layout(this);
            var size = new xSize(Width, Height);
            InvalidateLayout();
            return size;
        }

        protected abstract void BlockLayoutChanged(cBox child);

        private static void StyleChanged(cStyle.Resolver res, cStyle.ChangeType ct, string property)
        {
            var box = res.Owner;

            if (ct == cStyle.ChangeType.Size)
            {
                box._has_layout = false;
                var parent = box.Parent;
                if (parent == null) return;

                //Updates "size depends on parent"
                var vl = box.VisLine;
                if (vl != null)
                    vl.SizeDependsOnParent = box.SizeDependsOnParent;

                //Tells the parent to lay out its content
                parent.DoLayoutContent = true;

                //Informs the parent of any needed structural changes.
                if (property == "Display")
                {
                    //A change in disp alters the structure of the layout.
                    //The parent must be informed of this.
                    parent.BlockLayoutChanged(box);
                }
            }
            else if (ct == cStyle.ChangeType.Position)
            {
                box._has_position = false;
                box.ChildNeedPosition();
            }

            box.StyleChanged(ct, property);
        }

        protected virtual void StyleChanged(cStyle.ChangeType ct, string property)
        {

        }

        /// <summary>
        /// Lays out the box if there is a reason to.
        /// </summary>
        internal void LayoutIfChanged(cBox anchor)
        {                
            //Do layout if layout is dirty, or if parent size has changed and our size depends on the parent
            if (!_has_layout || Parent != null && _parent_size_count != Parent._my_size_count && SizeDependsOnParent)
            {
                CalcBorders();
                DoLayout(Style.Width, Style.Height, anchor);                
            }
            else if (_layout_content)
            {
                //We layout content when content has changed,
                //but also lays out ourself if our size depends
                //on said conent.
                if (SizeDependsOnContent)
                {
                    CalcBorders();
                    DoLayout(Style.Width, Style.Height, anchor);
                }
                else
                {
                    LayoutContent(_style.Position == ePosition.Static ? anchor : this);
                    _layout_content = false;
                }
            }
        }

        /// <summary>
        /// Lays out the box if there is a reason to.
        /// </summary>
        protected void LayoutIfChangedNoCalc(cBox anchor)
        {
            //Do layout if layout is dirty, or if parent size has changed and our size depends on the parent
            if (!_has_layout || Parent != null && _parent_size_count != Parent._my_size_count && SizeDependsOnParent)
            {
                DoLayout(Style.Width, Style.Height, anchor);
            }
            else if (_layout_content)
            {
                //We layout content when content has changed,
                //but also lays out ourself if our size depends
                //on said conent.
                if (SizeDependsOnContent)
                {
                    DoLayout(Style.Width, Style.Height, anchor);
                }
                else
                {
                    LayoutContent(_style.Position == ePosition.Static ? anchor : this);
                    _layout_content = false;
                }
            }
        }

        protected void Layout(cBox anchor)
        {
            LayoutIfChanged(_style.Position == ePosition.Static ? this : anchor);

            if (!_has_position)
                CalculatePosition(this);
            else if (!_child_has_position)
            {
                CalcContentPosition(this);
                _child_has_position = true;
            }
            else if (_childs_dep_anchor && Reposition)
            {
                CalcContentPosition(this);
            }
        }

        /// <summary>
        /// Lays out to the given size
        /// </summary>
        /// <param name="width">Width, or nan for style width</param>
        /// <param name="height">Height, or nan for style height</param>
        protected void LayoutNoCalc(double width, double height, cBox anchor)
        {
            DoLayout(double.IsNaN(width) ? Style.Width : new cSize(width),
                double.IsNaN(height) ? Style.Height : new cSize(height), anchor);
        }

        ///// <summary>
        ///// This box has been layed out before. We do not nessesarily need
        ///// to redo all calculations.
        ///// </summary>
        //protected void ResumeLayout()
        //{
        //    if (SizeDependsOnContent)
        //    {
        //        //This is an unsupported scenario, so we won't bother
        //        //optimizing for it.
        //        if (VariableContentSize)
        //            DoLayout();
        //        else
        //        {
        //            //Since we don't support VariableContentSize, there's no need to
        //            //inform children of the change in size of this container. 
        //            ResumeContentLayout();

        //            double w = VisContentWidth, h = VisContentHeight;
        //            VisContentWidth = ContentSize.Width;
        //            VisContentHeight = ContentSize.Height;

        //            if (w != VisContentWidth || h != VisContentHeight)
        //            {
        //                UpdateSize();

        //                //Notify parent if size changed... problem: Parent might want to inform children of size changes. Ergo, don't do the stupid here.
        //                // - Perhaps check if parent has SizeDependsOnContent + VariableContentSize
        //                if (_parent != null)
        //                    _parent.ChildSizeChanged(this);
        //            }
        //        }
        //    }
        //    else
        //    {
        //        //This containers size will not changed based on the children, so we just continue the layout.
        //        ResumeContentLayout();
        //    }
        //}

        ///// <summary>
        ///// Updates size. Only relevant for containers where VariableContentSize is true
        ///// </summary>
        //private void UpdateSize()
        //{
        //    VisWidth = VisContentWidth + VisPaddingLeft + VisPaddingRight + VisBorderLeftWidth + VisBorderRightWidth;
        //    VisHeight = VisContentHeight + VisPaddingTop + VisPaddingBottom + VisBorderTopHeight + VisBorderBottomHeight;
        //}

        /// <summary>
        /// Does the box layout, then calls methods for laying out children
        /// </summary>
        /// <param name="layout_children">If false, children will not be layed out</param>
        /// <remarks>
        /// Known issues
        ///  - Min width calculations are wrong when Padding/Margin is given in %.
        ///    This because these values are calcualted using un_adj_width.
        ///    
        ///    To fix this, introduce a un_adj_min_width and do all Padding/Margin
        ///    calcualtions using that value. 
        ///    
        ///    Though, it might not be fully nessesary. un_adj_min_width is only
        ///    different from min_width when un_adj_min_width equals un_adj_width. 
        ///    So the extra calcs can be done using "min width" or "un_adj_width"
        ///    
        /// About:
        ///  - Borders
        ///  
        ///    Border size is calculated before this method is called. This can be done since border
        ///    size is independent from layout.
        ///    
        ///    The reason it's not done in this function is since tables wants to oversteer these calculations
        ///    when collapsing borders. 
        /// </remarks>
        private void DoLayout(cSize w, cSize h, cBox anchor)
        {
            //Informs children that they must recalculate their positions
            Reposition = true;

            if (ContainsText)
                UpdateTextStyle();

            //From where to fetch size information. 
            var parent = _style.Position == ePosition.Absolute ? anchor : Parent;

            //Updates variables used to detect layout changes
            if (parent != null)
                _parent_size_count = parent._my_size_count;

            //The current calculated size of this container
            double width, min_width, height, min_height;
            bool run_layout = _layout_content || VariableContentSize, 
                layout_children = run_layout;

            //What values width is to contain.
            eBoxSizing sizing_w = _style.Sizing, sizing_h = sizing_w;

            //Resets content values. 
            VisContentWidth = VisContentHeight = double.NaN;

            //Padding and margins are to be calculated using this value, to function
            //like browsers.
            double un_adj_width;

            //Determines width
            if (w.Unit == cUnit.Precentage && parent != null && parent.HasWidth)
            {
                if (float.IsNaN(w.Value))
                {
                    //NaN percent is stand in for how html lays out boxes without with
                    //as if width is = 100% - Margin. 
                    un_adj_width = width = CalculateSize(parent.VisContentWidth, new cSize(100, cUnit.Precentage));

                    //Adjusts for margins.
                    // - Note, browsers also calculates margins given in % this way
                    VisMarginLeft = CalculateSize(width, _style.MarginLeft);
                    VisMarginRight = CalculateSize(width, _style.MarginRight);
                    width -= VisMarginLeft + VisMarginRight;

                    min_width = width;
                }
                else
                {
                    //We know that width is always set, since SizeDependsOnParent is true
                    un_adj_width = width = min_width = CalculateSize(parent.VisContentWidth, w);
                }
            }
            else if (w.Unit != cUnit.Auto && w.Unit != cUnit.Precentage)
            {
                //Width is explicitly set, with no dependancy on children nor parent.
                un_adj_width = width = min_width = SetSize(w);
            }
            else
            {
                //Width og box is the width of the children + padding etc
                if (run_layout)
                {
                    unchecked { _my_size_count++; }
                    layout_children = LayoutContent(_style.Position == ePosition.Static ? anchor : this);
                    run_layout = false;
                }
                un_adj_width = width = ContentSize.Width;
                min_width = MinContentSize.Width;

                //The width value only covers the content of the box, not the padding nor borders.
                sizing_w = eBoxSizing.ContentBox;
            }

            //Calculates borders, margins and padding widths. See CSS 2.1 spesification for where
            //I drew inspiration + Microsoft Edge for how to deal with undefined behavior

            if (sizing_w > eBoxSizing.BorderBox)
            {
                //We add the border width to the total
                double add = VisBorderLeft + VisBorderRight;
                width += add;
                min_width += add;
                un_adj_width += add;
            }

            //Then we calculate the padding. Here width may depend on the parent,
            //or in some cases the children (undefined behavior). Note, heights
            //are calculated using the same values as width - as css avoids height
            //dependensies than would complicate the algorithim.
            VisPaddingLeft = CalculateSize(un_adj_width, _style.PaddingLeft);
            VisPaddingRight = CalculateSize(un_adj_width, _style.PaddingRight);
            VisPaddingTop = CalculateSize(un_adj_width, _style.PaddingTop);
            VisPaddingBottom = CalculateSize(un_adj_width, _style.PaddingBottom);

            //Margins are calculated the same way, though in css it also supports an "auto"
            //property. That has not been implemented.
            VisMarginLeft = CalculateSize(un_adj_width, _style.MarginLeft);
            VisMarginRight = CalculateSize(un_adj_width, _style.MarginRight);
            VisMarginTop = CalculateSize(un_adj_width, _style.MarginTop);
            VisMarginBottom = CalculateSize(un_adj_width, _style.MarginBottom);


            if (sizing_w > eBoxSizing.PaddingBox)
            {
                double add = VisPaddingLeft + VisPaddingRight;;
                width += add;
                min_width += add;
            }

            //We set the content width
            double sub = VisPaddingLeft + VisPaddingRight + VisBorderLeft + VisBorderRight;
            VisContentWidth = width - sub;
            VisMinContentWidth = width - sub;

            //Sets the height
            if (h.Unit == cUnit.Precentage && parent != null && parent.HasHeight)
            {
                if (parent == null || !parent.HasSize)
                {
                    height = ContentSize.Height;
                    min_height = MinContentSize.Height;
                }
                else
                {
                    height = parent.VisContentHeight;
                    min_height = parent.VisMinContentHeight;
                }
                height = CalculateSize(height, h);
                min_height = CalculateSize(min_height, h);
            }
            else if (h.Unit != cUnit.Auto)
            {
                //Height is explicitly set, with no dependancy on children nor parent.
                height = min_height = SetSize(h);
            }
            else
            {
                //Width is the width of the children
                if (run_layout)
                {
                    unchecked { _my_size_count++; }
                    layout_children = LayoutContent(_style.Position == ePosition.Static ? anchor : this);
                    run_layout = false;
                }
                height = ContentSize.Height;
                min_height = MinContentSize.Height;

                //The height value only covers the content of the box, not the padding nor borders.
                sizing_h = eBoxSizing.ContentBox;
            }

            //We set the content height and adjust to content box
            VisContentHeight = height;
            VisMinContentHeight = min_height;
            if (sizing_h < eBoxSizing.ContentBox)
            {
                sub = VisPaddingTop + VisPaddingBottom;
                VisContentHeight -= sub;
                VisMinContentHeight -= sub;

                if (sizing_h < eBoxSizing.PaddingBox)
                {
                    sub = VisBorderTop + VisBorderBottom;
                    VisContentHeight -= sub;
                    VisMinContentHeight -= sub;
                }
            }
            else
            {
                double add = VisPaddingTop + VisPaddingBottom;
                height += add;
                min_height += add;

                if (sizing_h > eBoxSizing.PaddingBox)
                {
                    add = VisBorderTop + VisBorderBottom;
                    height += add;
                    min_height += add;
                }
            }

            //Children are layed out, potentially again. This also sets the VisContentWidth/Height
            //if (run_layout) //<-- If this is uncommented, layout is only ever run once
            if (layout_children) //<-- Reruns layout when children depend on the parents size, and the parent depends on the childrens size
            {
                //Lays out the content, potentially again.
                //Note, two layouts only happends when children depends on the parents size, and
                //the parent depends on the children's size. 
                unchecked { _my_size_count++; }
                LayoutContent(_style.Position == ePosition.Static ? anchor : this);
                //The case of "parents depends on child, child depends on parent".
                //
                //Basic problem:
                // - Child is 75% of this box
                // - This box is sized to the child
                // 1. Layout is run. Content size itself to its full size since this box does not have a size yet.
                // 2. Nothing more is done. 
                //
                // This doesn't really make sense. Browser do something like this:
                // 1. Layout is run. Content size itself to its full size since this box does not have a size yet.
                // 2. Layout is run again, this time the child sizes itself to 75% of this box's width.
                //
                // Better perhaps, but this is undefined anyway and differs
                // between browsers, so I'm leaving things as is.
                //
                // To activate a second layout pass (for this situation), use  if (layout_children) above.
                // I've disabled this feature, as I don't want to bother testing "what happens" with multiple
                // boxes that sizes itself to the parent. The end result might end up a mess. 

                ////Adjusts the width values
                //Well, on second thought. Size should by now be in stone. Commenting out all adjustment
                //code.
                //if (w.Unit == cUnit.Auto)
                //{
                //    width = VisContentWidth = ContentSize.Width;
                //    if (sizing_w < cStyle.BoxSizing.ContentBox)
                //    {
                //        width += VisPaddingLeft + VisPaddingRight;
                //        if (sizing_w < cStyle.BoxSizing.PaddingBox)
                //            width += VisBorderLeftWidth + VisBorderRightWidth;
                //    }
                //}

                ////Adjusts the height values
                //if (h.Unit == cUnit.Auto)
                //{
                //    height = VisContentHeight = ContentSize.Height;
                //    if (sizing_h < cStyle.BoxSizing.ContentBox)
                //    {
                //        height += VisPaddingTop + VisPaddingBottom;
                //        if (sizing_h < cStyle.BoxSizing.PaddingBox)
                //            height += VisBorderTopHeight + VisBorderBottomHeight;

                //    }
                //}
            }
            

            //Sets the size of this box, excluding margins.
            VisHeight = height; // VisContentHeight + VisPaddingTop + VisPaddingBottom + VisBorderTopHeight + VisBorderBottomHeight;
            VisWidth = width;//VisContentWidth + VisPaddingLeft + VisPaddingRight + VisBorderLeftWidth + VisBorderRightWidth;
            VisMinHeight = min_height;
            VisMinWidth = min_width;
            _has_layout = true;
            _layout_content = false;

            Debug.Assert(VisContentHeight != double.NaN && VisContentWidth != double.NaN);
        }

        protected void CalcBorders()
        {
            VisBorderLeft = SetSize(_style.BorderLeft);
            VisBorderTop = SetSize(_style.BorderTop);
            VisBorderRight = SetSize(_style.BorderRight);
            VisBorderBottom = SetSize(_style.BorderBottom);
        }

        private void UpdateTextStyle()
        {
            //Sets these styles, assuming the style has these values.
            var s = Style;
            _forground_color = s.Color;
            _font_size = s.FontSize;
            _font = s.Font;

            //Note, these can/will be overriden later by the UpdateTextParams function.
            //
            //One annoyance:
            //
            //If the style has color/font/etc, it does not mean that any child will use them.
            //A workaround is to check if this is a "text object" and only set these parameters
            //if that is the case. "ContainsText" does this on the first layout, but will be true
            //for containers that has text on the second layout.
            //
            //This may result in ignored/uneeded text commands being emited if layout is done 
            //multiple times.
        }

        /// <summary>
        /// Lay out content of the box
        /// </summary>
        /// <param name="anchor">Used for size caluclations</param>
        /// <returns>True if child size depends on anchor size</returns>
        protected abstract bool LayoutContent(cBox anchor);

        /*protected abstract void ResumeContentLayout();*/
       
        private static double CalcContentWidth(eBoxSizing s, double width, double padding, double border, double margin, out double full_size)
        {
            switch (s)
            {
                default:
                case eBoxSizing.BorderBox:
                    full_size = width + margin;
                    return width - padding - border;
                case eBoxSizing.ContentBox:
                    full_size = width + padding + border + margin;
                    return width;
                case eBoxSizing.PaddingBox:
                    full_size = width + border + margin;
                    return width - padding;
            }
        }

        private static double CalculateSize(double ref_size, cSize val)
        {
            switch (val.Unit)
            {
                case cUnit.Precentage:
                    return ref_size * val.Value / 100;
            }

            return SetSize(val);
        }

        #endregion

        #region Position

        private void CalculateSizes()
        {
            //Note, css calculates border radius based on both height and width. 
            //
            //Modify CreatePoints and Adjust functions to add support this. 
            //This needs 8 numbers instead of 4, where each corner is CalculateSize for
            //both width and height.
            var s = Style;
            VisBRadUL = CalculateSize(VisWidth, s.BorderRadiusUL);
            VisBRadUR = CalculateSize(VisWidth, s.BorderRadiusUR);
            VisBRadLL = CalculateSize(VisWidth, s.BorderRadiusLL);
            VisBRadLR = CalculateSize(VisWidth, s.BorderRadiusLR);
        }

        private void CalculatePosition(cBox anchor)
        {
            _childs_dep_anchor = false;
            _has_abs_children = false;

            if (Style.Position != ePosition.Static)
            {
                var s = Style;
                VisLeft = CalculateSize(anchor.VisWidth, s.Left);
                VisRight = CalculateSize(anchor.VisWidth, s.Right);
                VisTop = CalculateSize(anchor.VisHeight, s.Top);
                VisBottom = CalculateSize(anchor.VisHeight, s.Bottom);

                CalculateSizes();
                FlowContent(this);
            }
            else
            {
                CalculateSizes();
                FlowContent(anchor);
            }
            _has_position = _child_has_position = true;
            _reposition = false;
        }

        protected abstract void FlowContent(cBox anchor);

        private void CalcContentPosition(cBox anchor)
        {
            _childs_dep_anchor = false;
            _has_abs_children = false;

            foreach (var node in this)
            {
                if (node is cBox)
                {
                    var child = (cBox)node;

                    child.SetPosition(anchor);
                    if (child.PositionDependsOnAnchor)
                        _childs_dep_anchor = true;

                    var pos = child.Style.Position;
                    if (pos == ePosition.Static)
                    {
                        if (child._has_abs_children)
                            _has_abs_children = true;
                        if (child._childs_dep_anchor)
                            _childs_dep_anchor = true;
                    }
                    else if (pos == ePosition.Absolute)
                        _has_abs_children = true;
                }
            }

            _reposition = false;
        }

        /// <summary>
        /// Update position flags in children and parent
        /// </summary>
        internal void UpdatePosition(cBox anchor)
        {
            SetPosition(anchor);

            if (Parent != null)
            {
                var pos = Style.Position;
                if (pos == ePosition.Static)
                {
                    if (_has_abs_children)
                        Parent._has_abs_children = true;
                    if (_childs_dep_anchor)
                        Parent._childs_dep_anchor = true;
                }
                else
                {
                    if (pos == ePosition.Absolute)
                        Parent._has_abs_children = true;

                    if (PositionDependsOnAnchor)
                        Parent._childs_dep_anchor = true;
                }
            }
        }

        /// <summary>
        /// Called when position has been set.
        /// </summary>
        private void SetPosition(cBox anchor)
        {
            if (!_has_position)
            {
                _has_position = true;

                CalculatePosition(anchor);
            }
            else
            {
                if (Reposition)
                {
                    _childs_dep_anchor = false;
                    _has_abs_children = false;
                    FlowContent(Style.Position != ePosition.Static ? this : anchor);
                    _reposition = false;
                }
                else if (anchor.Reposition)
                {
                    if (PositionDependsOnAnchor)
                        CalculatePosition(anchor);
                    else if (_childs_dep_anchor || !_child_has_position)
                        CalcContentPosition(Style.Position != ePosition.Static ? this : anchor);
                }
                else if (!_child_has_position)
                {
                    CalcContentPosition(Style.Position != ePosition.Static ? this : anchor);
                }
            }

            _child_has_position = true;
        }

        #endregion

        #region Events

        internal void RemoveChild(cNode child)
        {
            //Note, the childs properties are not reliable after "RemoveChildImpl",
            //so we take pain to only read properties before running this function.

            //While layout need not nesseseraly be done, we will need to reflow since we
            //don't know if the removal of this block causes other blocks to move.
            Reposition = true;

            if (child is cBox)
            {
                var box = (cBox)child;

                if (box.Style.Position != ePosition.Static)
                {
                    if (_childs_dep_anchor)
                    {
                        if (box.PositionDependsOnAnchor)
                        {
                            RemoveChildImpl(box);

                            //Updates the _childs_dep_anchor and _has_abs_children flags
                            UpdateAbsFlags();
                            return;
                        }
                    }
                    else if (_has_abs_children && (box.Style.Position == ePosition.Absolute ||
                        box._has_abs_children && box.Style.Position == ePosition.Static))
                    {
                        RemoveChildImpl(box);

                        //Updates the _has_abs_children flags
                        UpdateAbsFlag();
                        return;
                    }
                }
            }

            RemoveChildImpl(child);
        }

        private void UpdateAbsFlags()
        {
            _childs_dep_anchor = false;
            _has_abs_children = false;
            foreach (var node in this)
            {
                if (node is cBox)
                {
                    var child = (cBox)node;

                    var pos = child.Style.Position;
                    if (child._has_abs_children && pos == ePosition.Static || pos == ePosition.Absolute)
                        _has_abs_children = true;
                    if (child.PositionDependsOnAnchor || child._childs_dep_anchor && pos == ePosition.Static)
                        _childs_dep_anchor = true;
                }
            }
        }

        private void UpdateAbsFlag()
        {
            _has_abs_children = false;
            foreach (var node in this)
            {
                if (node is cBox)
                {
                    var child = (cBox)node;

                    var pos = child.Style.Position;
                    if (child._has_abs_children && pos == ePosition.Static || pos == ePosition.Absolute)
                    {
                        _has_abs_children = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Never call this method directly. Use "Remove()"
        /// </summary>
        /// <param name="child">Child to remove</param>
        protected abstract void RemoveChildImpl(cNode child);

        #endregion

        #region Render

        /// <summary>
        /// Renders the  box. State will be saved and restored.
        /// </summary>
        /// <param name="draw">Render target</param>
        /// <param name="state">Render state</param>
        public void Render(IDraw draw, ref cRenderState state)
        {
            if (state.GS != GS.Page)
                throw new NotSupportedException("State must be page");

            //Todo: Detect if a different anchor is used inside
            //      LayoutIfChanged without mucking up garbage
            //      collection with a reference
            //Then again, this is a very minor bug. Will only matter
            //if you use a box in a "document", then call Render on the
            //box. The box will then get the layout it had in the document
            //instead of its unbounded layout. Note that it is not
            //possible to use a box in two documents at the same time.
            LayoutIfChanged(this);

            draw.Save();
            state.Save();            

            Render(draw, ref state, this);

            draw.Restore();
            state = state.Restore();
        }

        /// <summary>
        /// Renders / Draws the box and all its children
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="state">Render state</param>
        /// <param name="anchor">Anchor for absolute positioning</param>
        /// <remarks>
        /// Remeber, PDF draws from bottom to top. While we lay out from top to bottom (like html).
        /// 
        /// I've decided not to push a transformation to change this, as that will be
        /// a headace for my text rutines. Since drawing boxes is much easier than text
        /// I just go with the pdf way.
        /// </remarks>
        internal virtual void Render(IDraw draw, ref cRenderState state, cBox anchor)
        {
            Render(draw, ref state, anchor, true);
        }

        /// <summary>
        /// Renders / Draws the box and all its children
        /// </summary>
        /// <param name="draw">Target</param>
        /// <param name="state">Render state</param>
        /// <param name="anchor">Anchor for absolute positioning</param>
        /// <remarks>
        /// Remeber, PDF draws from bottom to top. While we lay out from top to bottom (like html).
        /// 
        /// I've decided not to push a transformation to change this, as that will be
        /// a headace for my text rutines. Since drawing boxes is much easier than text
        /// I just go with the pdf way.
        /// </remarks>
        protected void Render(IDraw draw, ref cRenderState state, cBox anchor, bool draw_border)
        {
            xPoint[] clip = null, stroke = null;
            var style = Style;

            //Convenient sizes
            double inner_width = VisPaddingLeft + VisContentWidth + VisPaddingRight;
            double inner_height = VisPaddingTop + VisContentHeight + VisPaddingBottom;
            double border_height = inner_height + VisBorderBottom + VisBorderTop;
            double min_prec = MinPrec;

            if (style.RotationAngle != 0)
            {
                //Rotate the box around its center point
                var rm = xMatrix.Identity.Rotate(style.RotationAngle, VisWidth * style.RotationPoint.X, -VisHeight * (1 - style.RotationPoint.Y));

                //Moves the box after rotation. 
                rm = rm.Translate(VisMarginLeft, VisMarginTop);

                draw.PrependCM(rm);
            }
            else if (VisMarginLeft != 0 || VisMarginBottom != 0)
            {
                state.PrependCTM(new xMatrix(VisMarginLeft, VisMarginBottom), draw);
            }

            //Sets clip path for curved borders
            bool has_clip = false;
            if (style.HasBorderRadius(min_prec))
            {
                draw.Save();
                state.Save();

                //Creates the stroke geometry
                // (Note, divides by two since we draw "inside" the geometry)
                stroke = CreatePoints(
                    VisBorderLeft / 2,
                    VisBorderBottom / 2,
                    inner_width + VisBorderLeft / 2 + VisBorderRight / 2,
                    inner_height + VisBorderBottom / 2 + VisBorderTop / 2);

                //Creates the inner geometry.
                clip = CreatePoints(VisBorderLeft, VisBorderBottom, inner_width, inner_height);

                //Adjusts it to the stroke geometry
                clip = Adjust(stroke, clip, min_prec);

                PdfLib.Compose.Text.chTextBox.DrawRoundRect(clip, draw);
                draw.ClosePath();
                draw.SetClip(xFillRule.Nonzero);
                draw.DrawPathNZ(false, false, false);
                has_clip = true;
            }

            //Renders the background
            if (style.BackgroundColor != null)
            {
                var fc = state.fill_color;
                style.BackgroundColor.SetColor(state, true, draw);

                draw.RectAt(VisBorderLeft, VisBorderBottom, inner_width, inner_height);
                draw.DrawPathNZ(false, false, true);

                fc.SetColor(state, true, draw);
            }

            
            if (style.Clip)
            {
                if (!has_clip)
                    state.Save(draw);
                double move_x = VisBorderLeft + VisPaddingLeft,
                       move_y = VisBorderBottom + VisPaddingBottom;
                
                //Add clip path for padding.
                cDraw.Clip(draw, new xRect(move_x, move_y, move_x + VisContentWidth, move_y + VisContentHeight));

                DrawAllContent(draw, ref state, anchor);

                //Removes clip path
                if (!has_clip)
                    state = state.Restore(draw);
            }

            if (has_clip)
            {
                //Removes the clipping
                state = state.Restore(draw);
            }

            if (style.HasBorder && draw_border)
            {
                //If children is to be drawn after the border, we save/restore since we
                //adjust the drawing state
                if (!style.Clip)
                    state.Save(draw);

                if (style.BorderUniform)
                {
                    //Simple square or rounded border
                    if (style.BorderColorLeft != null)
                        style.BorderColorLeft.SetColor(state, false, draw);
                    if (style.DashLeft != null)
                        draw.SetStrokeDashAr(style.DashLeft.Value);
                    draw.StrokeWidth = VisBorderLeft;

                    if (style.HasBorderRadius(min_prec))
                    {
                        PdfLib.Compose.Text.chTextBox.DrawRoundRect(stroke, draw);
                        draw.ClosePath();
                    }
                    else
                        draw.RectAt(VisBorderLeft / 2,
                                    VisBorderBottom / 2,
                                    inner_width + VisBorderLeft / 2 + VisBorderRight / 2,
                                    inner_height + VisBorderBottom / 2 + VisBorderTop / 2);
                    draw.DrawPathNZ(false, true, false);
                }
                else
                {
                    //Draws the borders in four sections, each with its own stroke, color, tickness, and such.
                    if (clip == null)
                    {
                        //Creates the stroke geometry
                        stroke = CreatePoints(
                            VisBorderLeft / 2,
                            VisBorderBottom / 2,
                            inner_width + VisBorderLeft / 2 + VisBorderRight / 2,
                            inner_height + VisBorderBottom / 2 + VisBorderTop / 2);

                        //Creates the inner geometry.
                        clip = CreatePoints(VisBorderLeft, VisBorderBottom, inner_width, inner_height);

                        //Adjusts it to the stroke geometry
                        clip = Adjust(stroke, clip, min_prec);
                    }

                    //Creates the outer geometry
                    var outer = CreatePoints(
                        0,
                        0,
                        inner_width + VisBorderLeft + VisBorderRight,
                        inner_height + VisBorderBottom + VisBorderTop);
                    outer = Adjust(stroke, outer, min_prec);

                    {
                        //How the points are layed out:
                        //       -P1---------P2
                        //      /              \
                        //     P0              P3
                        //     |               |
                        //     P7              P4
                        //      \              /
                        //       -P6--------P5-
                        //This code draws the borders as four independant strokes. To allow for borders of different
                        //ticknesses and corner rounding the code first creates a clip path, then strokes with the
                        //largest stroke width.
                        //
                        //Note that when corners are uncurved then the corner points (say, P1 and P0) are exactly the
                        //same. This is a special case where it's impossible to draw a curve between the points.
                        //
                        //To compensate for this the algorithm skips on drawing curves when this is the case. Do note
                        //that while the Ajust(..) function makes sure such corner points are exactly the same,
                        //while CreatePoints( .. ) does not. I.e. one can not compare points that comes from CreatePoints
                        //for equality using xPoint.Equals(..)
                        //
                        //To work around this we instead test the BorderRadius porperties directly, i.e:
                        // "!Util.Real.Same(BorderRadiusUL, 0, _min_prec)" instead of "stroke[0] != stroke[1]"
                        //
                        //The only drawback of this workaround is that this algorithm now depends on these properties,
                        //also the padding isn't entierly right. (I.e. "draw.LineTo(stroke[2].X + BorderRight, stroke[2].Y);"
                        //should be "draw.LineTo(outer[2].X, stroke[2].Y);").
                        //
                        //But I find the former more readable, and any excess padding is clipped away.
                        //
                        //Issues:
                        //
                        // 1. Thin white line
                        //    There may be a thin white line between the borders. I don't know any good way to fix this.
                        //    For now one have to use overdraw.
                        //
                        // 2. Midpoint of curves.
                        //    The current method of finding midpoints are by using hardcoded values. This seems to work
                        //    well enough when lines are of uniform tickness, not so much when lines are of non-uniform
                        //    tichness. 

                        var largest_stroke = Math.Max(VisBorderBottom, Math.Max(VisBorderRight, Math.Max(VisBorderLeft, VisBorderTop)));
                        state.SetStrokeWidth(largest_stroke, draw);

                        //Creates right geometry
                        if (VisBorderRight > 0)
                        {
                            state.Save(draw);
                            if (style.DashRight != null)
                                state.SetStrokeDashAr(style.DashRight.Value, draw);
                            if (style.BorderColorRight != null)
                                style.BorderColorRight.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawVBorder(outer[2], outer[3], outer[4], outer[5],
                                      clip[2], clip[3], clip[4], clip[5],
                                      draw
#if OVERDRAW
, IsSimilar(style.BorderColorRight, style.BorderColorTop),
                                      IsSimilar(style.BorderColorBottom, style.BorderColorRight), min_prec
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the right line
                            if (!Util.Real.Same(VisBRadUR, 0, min_prec))
                            {
                                draw.MoveTo(stroke[2].X, stroke[2].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[2], stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#else
                                draw.CurveTo(stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[2].X, stroke[2].Y + VisBorderTop);
                            if (!Util.Real.Same(VisBRadLR, 0, min_prec))
                            {
                                draw.LineTo(stroke[4].X, stroke[4].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[4], stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#else
                                draw.CurveTo(stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[4].X, stroke[4].Y - VisBorderBottom);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }

                        //Creates left geometry
                        if (VisBorderLeft > 0)
                        {
                            state.Save(draw);
                            if (style.DashLeft != null)
                                state.SetStrokeDashAr(style.DashLeft.Value, draw);
                            if (style.BorderColorLeft != null)
                                style.BorderColorLeft.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawVBorder(outer[6], outer[7], outer[0], outer[1],
                                      clip[6], clip[7], clip[0], clip[1],
                                      draw
#if OVERDRAW
, IsSimilar(style.BorderColorLeft, style.BorderColorBottom),
                                      IsSimilar(style.BorderColorLeft, style.BorderColorTop), min_prec
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the left line
                            if (!Util.Real.Same(VisBRadLL, 0, min_prec))
                            {
                                draw.MoveTo(stroke[6].X, stroke[6].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[6], stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#else
                                draw.CurveTo(stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[6].X, stroke[6].Y - VisBorderTop);
                            if (!Util.Real.Same(VisBRadUL, 0, min_prec))
                            {
                                draw.LineTo(stroke[0].X, stroke[0].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[0], stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#else
                                draw.CurveTo(stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[0].X, stroke[0].Y + VisBorderTop);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }

                        //Creates the top geometry
                        if (VisBorderTop > 0)
                        {
                            state.Save(draw);
                            if (style.DashTop != null)
                                state.SetStrokeDashAr(style.DashTop.Value, draw);
                            if (style.BorderColorTop != null)
                                style.BorderColorTop.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawHBorder(outer[0], outer[1], outer[2], outer[3],
                                      clip[0], clip[1], clip[2], clip[3],
                                      draw
#if OVERDRAW
, IsSimilar(style.BorderColorLeft, style.BorderColorTop),
                                      IsSimilar(style.BorderColorTop, style.BorderColorRight), min_prec
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the top line
                            if (!Util.Real.Same(VisBRadUL, 0, min_prec))
                            {
                                draw.MoveTo(stroke[0].X, stroke[0].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[0], stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#else
                                draw.CurveTo(stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[0].X - VisBorderLeft, stroke[0].Y);
                            if (!Util.Real.Same(VisBRadUR, 0, min_prec))
                            {
                                draw.LineTo(stroke[2].X, stroke[2].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[2], stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#else
                                draw.CurveTo(stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[2].X + VisBorderRight, stroke[2].Y);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }

                        //Creates bottom geometry
                        if (VisBorderBottom > 0)
                        {
                            state.Save(draw);
                            if (style.DashBottom != null)
                                state.SetStrokeDashAr(style.DashBottom.Value, draw);
                            if (style.BorderColorBottom != null)
                                style.BorderColorBottom.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawHBorder(outer[4], outer[5], outer[6], outer[7],
                                      clip[4], clip[5], clip[6], clip[7],
                                      draw
#if OVERDRAW
, IsSimilar(style.BorderColorRight, style.BorderColorBottom),
                                      IsSimilar(style.BorderColorBottom, style.BorderColorLeft), min_prec
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the bottom line
                            if (!Util.Real.Same(VisBRadLR, 0, min_prec))
                            {
                                draw.MoveTo(stroke[4].X, stroke[4].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[4], stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#else
                                draw.CurveTo(stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[4].X + VisBorderRight, stroke[4].Y);
                            if (!Util.Real.Same(VisBRadLL, 0, min_prec))
                            {
                                draw.LineTo(stroke[6].X, stroke[6].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[6], stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#else
                                draw.CurveTo(stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[6].X - VisBorderLeft, stroke[6].Y);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }
                    }
                }

                if (!style.Clip)
                    state = state.Restore(draw);
            }

            if (!style.Clip)
            {
                DrawAllContent(draw, ref state, anchor);
            }
        }

        private void DrawAllContent(IDraw draw, ref cRenderState state, cBox anchor)
        {
            if (ContainsText)
            {
                if (_forground_color != null)
                    _forground_color.SetColor(state, true, draw);
                if (_font_size != null || _font != null)
                    state.SetFont(_font, _font_size.HasValue ? (double?) SetSize(_font_size.Value) : null, draw);
            }

            if (Style.Position == ePosition.Static)
                DrawContent(draw, ref state, anchor);
            else
            {
                //Draws absolute content on this box
                _anchor_list = new List<cBox>();
                DrawContent(draw, ref state, this);

                //Note, the anchor list can't grow during the loop as absolute
                //      elements acts as new anchors. 
                for (int c = 0; c < _anchor_list.Count; c++)
                {
                    var box = _anchor_list[c];
                    state.Save(draw);

                    //Position
                    double mx = 0, my = 0;
                    if (!double.IsNaN(box.VisLeft))
                        mx = box.VisLeft;
                    else if (!double.IsNaN(box.VisRight))
                        mx = VisWidth - box.VisRight - box.VisFullWidth;
                    if (!double.IsNaN(box.VisTop))
                        my = box.VisTop;
                    else if (!double.IsNaN(box.VisBottom))
                        my = VisFullHeight - box.VisBottom - box.VisFullHeight;

                    //my is the "offset from top", so we need to transform it into "offset from bottom"
                    my = VisHeight - my - box.VisFullHeight;

                    draw.PrependCM(new xMatrix(mx, my));
                    
                    box.Render(draw, ref state, this);
                    state = state.Restore(draw);
                }
                _anchor_list = null;
            }
        }
        protected abstract void DrawContent(IDraw draw, ref cRenderState state, cBox anchor);

        /// <summary>
        /// Creates the 8 points in a curved border
        /// </summary>
        /// <remarks>
        ///       -P1---------P2
        ///      /              \
        ///     P0              P3
        ///     |               |
        ///     P7              P4
        ///      \              /
        ///       -P6--------P5-
        /// </remarks>
        private xPoint[] CreatePoints(double x, double y, double width, double height)
        {
            Debug.Assert(width >= 0 && height >= 0);
            var pts = new xPoint[8];
            //Limiting border radius to half width/height. One could allow for more but
            //one would then have to check if it overlaps the other side.
            double w2 = width / 2, h2 = height / 2;
            pts[0] = new xPoint(x, y + height - Math.Min(h2, VisBRadUL));
            pts[1] = new xPoint(x + Math.Min(w2, VisBRadUL), y + height);
            pts[2] = new xPoint(x + width - Math.Min(w2, VisBRadUR), y + height);
            pts[3] = new xPoint(x + width, y + height - Math.Min(h2, VisBRadUR));
            pts[4] = new xPoint(x + width, y + Math.Min(h2, VisBRadLR));
            pts[5] = new xPoint(x + width - Math.Min(w2, VisBRadLR), y);
            pts[6] = new xPoint(x + Math.Min(w2, VisBRadLL), y);
            pts[7] = new xPoint(x, y + Math.Min(h2, VisBRadLL));
            return pts;
        }

        /// <summary>
        /// When stroking, the clip outline needs to have
        /// it's coorindates adjusted so that the curves
        /// align. 
        /// </summary>
        private xPoint[] Adjust(xPoint[] stroke, xPoint[] outline, double min_prec)
        {
            var pts = new xPoint[8];
            if (Util.Real.Same(VisBRadUL, 0, min_prec))
            {
                pts[0] = outline[0];
                pts[1] = outline[0];
            }
            else
            {
                pts[0] = new xPoint(outline[0].X, stroke[0].Y);
                pts[1] = new xPoint(stroke[1].X, outline[1].Y);
            }
            if (Util.Real.Same(VisBRadUR, 0, min_prec))
            {
                pts[2] = outline[2];
                pts[3] = outline[2];
            }
            else
            {
                pts[2] = new xPoint(stroke[2].X, outline[2].Y);
                pts[3] = new xPoint(outline[3].X, stroke[3].Y);
            }
            if (Util.Real.Same(VisBRadLR, 0, min_prec))
            {
                pts[4] = outline[4];
                pts[5] = outline[4];
            }
            else
            {
                pts[4] = new xPoint(outline[4].X, stroke[4].Y);
                pts[5] = new xPoint(stroke[5].X, outline[5].Y);
            }
            if (Util.Real.Same(VisBRadLL, 0, min_prec))
            {
                pts[6] = outline[6];
                pts[7] = outline[6];
            }
            else
            {
                pts[6] = new xPoint(stroke[6].X, outline[6].Y);
                pts[7] = new xPoint(outline[7].X, stroke[7].Y);
            }
            return pts;
        }

#if QUADRACTIC_CURVES
        /// <summary>
        /// Converts a quardatic curve to a cubic curve
        /// </summary>
        /// <param name="start">Start point</param>
        /// <param name="ctrl1">Control point, and control point 1 of the cubic curve</param>
        /// <param name="end_ctrl2">End point and control point 2 of the cubic curve</param>
        private void QuadraticToCubic(xPoint start, ref xPoint ctrl1, ref xPoint end_ctrl2)
        {
            double x3 = end_ctrl2.X, y3 = end_ctrl2.Y;

            end_ctrl2.X = ctrl1.X + (end_ctrl2.X - ctrl1.X) / 3;
            end_ctrl2.Y = ctrl1.Y + (end_ctrl2.Y - ctrl1.Y) / 3;
            ctrl1.X = start.X + 2 * (ctrl1.X - start.X) / 3;
            ctrl1.Y = start.Y + 2 * (ctrl1.Y - start.Y) / 3;
        }
#endif

        private void DrawVBorder(xPoint outer_p0, xPoint outer_p1, xPoint outer_p2, xPoint outer_p3,
                                        xPoint inner_p0, xPoint inner_p1, xPoint inner_p2, xPoint inner_p3,
                                        IDraw draw
#if OVERDRAW
                                , bool overdraw_start, bool overdraw_end, double _min_prec
#endif
)
        {
#if QUADRACTIC_CURVES
            const double t_mid_ul = 0.50, t_mid_ur = 0.5;
#else
            const double t_mid_ul = 0.40, t_mid_ur = 0.3;
#endif
#if OVERDRAW
            double od2 = Math.Max(_min_prec, Overdraw_corner),
                overdraw_s = overdraw_start ? Overdraw_curve : 0,
                overdraw_e = overdraw_end ? Overdraw_curve : 0;
            if (outer_p0.X > inner_p0.X) od2 *= -1;
#else
            const double overdraw_s = 0, overdraw_e = 0;
#endif
#if QUADRACTIC_CURVES
            xPoint ctrl1, ctrl2;
#endif
            xPoint[] start;
            if (outer_p0 != outer_p1)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p1.X, outer_p0.Y);
                ctrl2 = outer_p1;
                QuadraticToCubic(outer_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(outer_p0, ctrl1, ctrl2, outer_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(outer_p0, new xPoint(outer_p1.X, outer_p0.Y), outer_p1, outer_p1, t_mid_ul - overdraw_s);
#endif
                draw.MoveTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, outer_p1.X, outer_p1.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                {
                    draw.MoveTo(outer_p0.X + od2, outer_p0.Y);
                    draw.LineTo(outer_p0.X, outer_p0.Y);
                }
                else
                    draw.MoveTo(outer_p0.X, outer_p0.Y);
#else
                draw.MoveTo(outer_p0.X, outer_p0.Y);
#endif
            }
            draw.LineTo(outer_p2.X, outer_p2.Y);
            if (outer_p2 != outer_p3)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p2.X, outer_p3.Y);
                ctrl2 = outer_p3;
                QuadraticToCubic(outer_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(outer_p2, ctrl1, ctrl2, outer_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(outer_p2, new xPoint(outer_p2.X, outer_p3.Y), outer_p3, outer_p3, t_mid_ur + overdraw_e);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
#if OVERDRAW
            else if (overdraw_end)
            {
                draw.LineTo(outer_p2.X + od2, outer_p2.Y);
            }
#endif
            if (inner_p3 != inner_p2)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p2.X, inner_p3.Y);
                ctrl2 = inner_p3;
                QuadraticToCubic(inner_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(inner_p2, ctrl1, ctrl2, inner_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(inner_p2, new xPoint(inner_p2.X, inner_p3.Y), inner_p3, inner_p3, t_mid_ur + overdraw_e);
#endif
                draw.LineTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, inner_p2.X, inner_p2.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_end)
                    draw.LineTo(inner_p3.X, inner_p3.Y + od2);
                else
                    draw.LineTo(inner_p3.X, inner_p3.Y);
#endif
                draw.LineTo(inner_p3.X, inner_p3.Y);
            }
            if (inner_p1 != inner_p0)
            {
                draw.LineTo(inner_p1.X, inner_p1.Y);
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p1.X, inner_p0.Y);
                ctrl2 = inner_p1;
                QuadraticToCubic(inner_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(inner_p0, ctrl1, ctrl2, inner_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(inner_p0, new xPoint(inner_p1.X, inner_p0.Y), inner_p1, inner_p1, t_mid_ul - overdraw_s);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                    draw.LineTo(inner_p1.X, inner_p1.Y - od2);
                else
                    draw.LineTo(inner_p1.X, inner_p1.Y);
#else
                draw.LineTo(inner_p1.X, inner_p1.Y);
#endif
            }
        }

        private void DrawHBorder(xPoint outer_p0, xPoint outer_p1, xPoint outer_p2, xPoint outer_p3,
                                xPoint inner_p0, xPoint inner_p1, xPoint inner_p2, xPoint inner_p3,
                                IDraw draw
#if OVERDRAW
                                , bool overdraw_start, bool overdraw_end, double _min_prec
#endif
)
        {
#if QUADRACTIC_CURVES
            const double t_mid_ul = 0.50, t_mid_ur = 0.5;
#else
            const double t_mid_ul = 0.30, t_mid_ur = 0.4;
#endif
#if OVERDRAW
            double od2 = Math.Max(_min_prec, Overdraw_corner), 
                overdraw_s = overdraw_start ? Overdraw_curve : 0,
                overdraw_e = overdraw_end ? Overdraw_curve : 0;
            if (outer_p0.Y < inner_p0.Y) od2 *= -1;
#else
            const double overdraw_e = 0, overdraw_s = 0;
#endif
#if QUADRACTIC_CURVES
            xPoint ctrl1, ctrl2;
#endif
            xPoint[] start;
            if (outer_p0 != outer_p1)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p0.X, outer_p1.Y);
                ctrl2 = outer_p1;
                QuadraticToCubic(outer_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(outer_p0, ctrl1, ctrl2, outer_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(outer_p0, new xPoint(outer_p0.X, outer_p1.Y), outer_p1, outer_p1, t_mid_ul - overdraw_s);
#endif
                draw.MoveTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, outer_p1.X, outer_p1.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                {
                    draw.MoveTo(outer_p0.X, outer_p0.Y - od2);
                    draw.LineTo(outer_p0.X, outer_p0.Y);
                }
                else
                    draw.MoveTo(outer_p0.X, outer_p0.Y);
#else
                draw.MoveTo(outer_p0.X, outer_p0.Y);
#endif
            }
            draw.LineTo(outer_p2.X, outer_p2.Y);
            if (outer_p2 != outer_p3)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p3.X, outer_p2.Y);
                ctrl2 = outer_p3;
                QuadraticToCubic(outer_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(outer_p2, ctrl1, ctrl2, outer_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(outer_p2, new xPoint(outer_p3.X, outer_p2.Y), outer_p3, outer_p3, t_mid_ur + overdraw_e);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
#if OVERDRAW
            else
            {
                if (overdraw_end)
                    draw.LineTo(outer_p2.X, outer_p2.Y - od2);
            }
#endif
            if (inner_p3 != inner_p2)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p3.X, inner_p2.Y);
                ctrl2 = inner_p3;
                QuadraticToCubic(inner_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(inner_p2, ctrl1, ctrl2, inner_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(inner_p2, new xPoint(inner_p3.X, inner_p2.Y), inner_p3, inner_p3, t_mid_ur + overdraw_e);
#endif
                draw.LineTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, inner_p2.X, inner_p2.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_end)
                    draw.LineTo(inner_p3.X + od2, inner_p3.Y);
                else
                    draw.LineTo(inner_p3.X, inner_p3.Y);
#endif
                draw.LineTo(inner_p3.X, inner_p3.Y);
            }
            if (inner_p1 != inner_p0)
            {
                draw.LineTo(inner_p1.X, inner_p1.Y);
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p0.X, inner_p1.Y);
                ctrl2 = inner_p1;
                QuadraticToCubic(inner_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(inner_p0, ctrl1, ctrl2, inner_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(inner_p0, new xPoint(inner_p0.X, inner_p1.Y), inner_p1, inner_p1, t_mid_ul - overdraw_s);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                    draw.LineTo(inner_p1.X - od2, inner_p1.Y);
                else
                    draw.LineTo(inner_p1.X, inner_p1.Y);
#else
                draw.LineTo(inner_p1.X, inner_p1.Y);
#endif
            }
        }

        /// <summary>
        /// Splits a Bezier curve. Returns the second half.
        /// </summary>
        private xPoint[] SplitBezierLast(xPoint start, xPoint ctrl1, xPoint ctrl2, xPoint end, double t)
        {
            return SplitBezierFirst(end, ctrl2, ctrl1, start, 1 - t);
        }

        /// <summary>
        /// Splits a Bezier curve. Returns the first half.
        /// </summary>
        /// <remarks>
        /// https://stackoverflow.com/questions/8369488/splitting-a-bezier-curve?noredirect=1&lq=1
        /// </remarks>
        private xPoint[] SplitBezierFirst(xPoint start, xPoint ctrl1, xPoint ctrl2, xPoint end, double t)
        {
            //First we find the midpoint, which will also serve as a start/end point
            //const double t = 0.5;

            double x1 = start.X, y1 = start.Y,
                   x2 = ctrl1.X, y2 = ctrl1.Y,
                   x3 = ctrl2.X, y3 = ctrl2.Y,
                   x4 = end.X, y4 = end.Y;

            var x12 = (x2 - x1) * t + x1;
            var y12 = (y2 - y1) * t + y1;

            var x23 = (x3 - x2) * t + x2;
            var y23 = (y3 - y2) * t + y2;

            var x34 = (x4 - x3) * t + x3;
            var y34 = (y4 - y3) * t + y3;

            var x123 = (x23 - x12) * t + x12;
            var y123 = (y23 - y12) * t + y12;

            var x234 = (x34 - x23) * t + x23;
            var y234 = (y34 - y23) * t + y23;

            var x1234 = (x234 - x123) * t + x123;
            var y1234 = (y234 - y123) * t + y123;

            return new xPoint[] { new xPoint(x12, y12), new xPoint(x123, y123), new xPoint(x1234, y1234) };

            //[(x1,y1),(x12,y12),(x123,y123),(x1234,y1234),(x234,y234),(x34,y34),(x4,y4)] 
        }

        #endregion

        #region Text

        /// <summary>
        /// Adds text related statistics from this box.
        /// </summary>
        /// <param name="stats"></param>
        internal override void AddStats(cTextStat stats)
        {
            //There's no need to take into account how many times these
            //colors are used by children, as these parameters will be
            //set here anyway.

            stats.AddColor(_forground_color ?? cColor.BLACK);
            stats.AddFont(_font ?? cFont.Times, _font_size ?? new cSize(10));
        }

        /// <summary>
        /// Updates text related parameters
        /// </summary>
        /// <param name="stats">Values</param>
        protected void UpdateTextParams(cTextStat stats)
        {
            _forground_color = stats.Color;
            _font_size = stats.FontSize;
            _font = stats.Font;
        }

        private cBrush _forground_color;
        private cFont _font;
        private cSize? _font_size;

        //Not 100% happy to have defaults in multiple places.
        //Since these values "should always be here" when needed, perhaps forgo defaults.
        protected double _FontSize
        {
            get { return _font_size.HasValue ? SetSize(_font_size.Value) : 10; }
        }
        protected cFont _Font { get { return _font; } }

        /// <summary>
        /// The maximum text decent of text inside this node.
        /// </summary>
        internal abstract double MaxDescent { get; }

        #endregion

        #region IEnumerable

        IEnumerator<cNode> IEnumerable<cNode>.GetEnumerator()
        {
            return GetEnumeratorImpl();
        }

        protected virtual IEnumerator<cNode> GetEnumeratorImpl()
        {
            yield break;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<cBox>)this).GetEnumerator();
        }

        #endregion

        public override string ToString()
        {
            return string.Format("{0}, {1} - {2}, {3}", VisWidth, VisHeight, VisContentWidth, VisContentHeight);
        }

        bool IsSimilar(cColor c1, cColor c2)
        {
            if (ReferenceEquals(c1, c2))
                return true;
            if (c1 == null || c2 == null)
                return false;
            var y1 = new YUVColor(c1.Color);
            var y2 = new YUVColor(c2.Color);
            var dist = y1.Distance(y2);
            return dist < Overdraw_corner;
        }

        internal protected enum Size
        {
            /// <summary>
            /// Determine the minimum size
            /// </summary>
            Minimum,

            /// <summary>
            /// Determine the normal size
            /// </summary>
            Normal
        }
    }
}
