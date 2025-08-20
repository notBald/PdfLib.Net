using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Compose.Layout.Internal
{
    /// <summary>
    /// This is the base class for all DOM objects. 
    /// </summary>
    /// <remarks>
    /// It's needed to support cText classes, which are a bit different in how they are layed out.
    /// </remarks>
    public abstract class cNode
    {
        #region Variables and properties

        /// <summary>
        /// Blocks can not share space, and takes up an entiere line. 
        /// </summary>
        public abstract bool IsBlock { get; }

        #region Position

        private cBox _parent;
        public cBox Parent
        {
            get { return _parent; }
            internal set
            {
                if (value == null)
                {
                    if (_parent != null)
                    {
                        _parent.RemoveChild(this);
                        VisNext = null;
                        VisLine = null;
                        VisWrapLine = null;
                        if (HasListeners)
                            RemoveListeners();
                        _parent = null;
                    }
                }
                else
                {
                    if (_parent != null)
                        throw new cLayoutException("Box is already in a tree. Remove before adding to another tree");
                    _parent = value;
                    _parent.DoLayoutContent = true;
                    _parent_size_count = 0;

                    if (SizeDependsOnParent)
                        InvalidateLayout();
                }
            }
        }

        /// <summary>
        /// Removes this box from the document model
        /// </summary>
        public void Remove()
        {
            Parent = null;
        }

        internal abstract void InvalidateLayout();

        #endregion

        #region State

        /// <summary>
        /// Keeps track of the id of the parent layout. This so
        /// we can detect when a parent changes size.
        /// </summary>
        internal int _parent_size_count, _my_size_count;

        protected virtual double MinPrec
        {
            get { return _parent != null ? _parent.MinPrec : 0.0001; }
        }

        internal abstract bool HasListeners { get; }

        /// <summary>
        /// Tracks whenever this box has a layout already
        /// </summary>
        internal abstract bool HasLayout { get; }

        internal abstract bool AddListeners();

        internal abstract void RemoveListeners();

        #endregion

        #endregion

        #region Visual variables and properties

        /// <summary>
        /// Width including margin
        /// </summary>
        internal abstract double VisFullWidth { get; }

        /// <summary>
        /// Height including margin
        /// </summary>
        internal abstract double VisFullHeight { get; }

        /// <summary>
        /// Minimum width including margin
        /// </summary>
        internal abstract double VisMinFullWidth { get; }

        /// <summary>
        /// Minimum height including margin
        /// </summary>
        internal abstract double VisMinFullHeight { get; }

        /// <summary>
        /// Total width and height, excluding margin
        /// </summary>
        internal double VisWidth, VisHeight;

        /// <summary>
        /// If this node takes up space in the layout.
        /// </summary>
        internal abstract bool TakesUpSpace { get; }

        /// <summary>
        /// Next visual in a linked list of visuals.
        /// </summary>
        internal cNode VisNext;

        /// <summary>
        /// The line this box sits on
        /// </summary>
        internal chbLine VisLine;

        /// <summary>
        /// A line can be sectioned up for line wrapping. This tag contains
        /// information used to reflow blocks efficently
        /// </summary>
        internal chbLineWrapper.LineMetrics VisWrapLine;

        /// <summary>
        /// If this box uses the parent's size.
        /// </summary>
        internal abstract bool SizeDependsOnParent { get; }

        #endregion

        #region Init

        internal cNode()
        { }

        #endregion

        #region Layout

        protected static double SetSize(cSize val)
        {
            switch (val.Unit)
            {
                case cUnit.Pixels:
                    return val.Value;

                case cUnit.Points:
                    return val.Value * 16d / 12;

                case cUnit.Milimeters:
                    return val.Value * 3.78;

                case cUnit.Centimeters:
                    return val.Value * 37.8;

                default:
                    throw new NotSupportedException();
            }
        }

        #endregion

        #region Text

        /*
         * It can be tempting to allow child boxes to inherit text properties,
         * in a similar style as to what's going on in html.
         * 
         * However, a change in text style would then nessesiate invalidating all
         * children's layout affected by said change. The layout system do not 
         * currently have any mechanisim for this.
         * 
         * Regardless, we prefer to set text state related properties as far up
         * the tree as possible, as to not have to set font/size/color etc for
         * every block of text.
         * 
         * This is what the code here is about.
         * 
         * The way this is done, is that during the layout phase, information
         * about the most commonly used text state settings used by children are 
         * determined and set here.
         * 
         * You still have to set those styles one each text object, but at least
         * the resulting PDF stream won't have a lot of needles qQ text changes.
         */

        /// <summary>
        /// This box contains text.
        /// </summary>
        internal virtual bool ContainsText { get { return false; } }

        /// <summary>
        /// Adds text related statistics from this box.
        /// </summary>
        /// <param name="stats"></param>
        internal abstract void AddStats(cTextStat stats);

        #endregion
    }
}
