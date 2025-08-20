using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// A destination is a pointer towards a page
    /// (Except when the destination is inside a Remote Go to Action dictionary)
    /// </summary>
    public class PdfDestination : ItemArray
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.Destination; } }

        /// <summary>
        /// Page this destination points at. Note, can be null.
        /// </summary>
        /// <remarks>
        /// This property is referenced during the saving step. Because of this, we
        /// do a Try/catch. 
        /// 
        /// Todo:
        /// Problem: I got one file in Random PDF files, that has destinations that point
        /// at numbers. I'm thinking, those numbers represents the page index. Maybe. It's
        /// something to investigate
        /// </remarks>
        public PdfPage Page 
        { 
            get 
            {
                try { return (PdfPage)_items.GetPdfType(0, PdfType.Page, IntMsg.NoMessage, null); }
                catch (PdfCastException) { return null; }
            } 
        }

        #endregion

        #region Init

        internal PdfDestination(PdfArray ar)
            : base(ar)
        {
            if (ar.Length < 2)
                throw new PdfReadException(PdfType.Array, ErrCode.Invalid);
        }

        public PdfDestination(PdfPage dest_page, DestType dest)
            : base(new TemporaryArray(dest.Size + 2))
        {
            _items.SetItem(0, dest_page, true);
            _items.Set(1, dest.Type);
            dest.FillArray(_items);
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, Write.Internal.ResTracker tracker)
        {
            return new PdfDestination(array);
        }

        #endregion

        #region Types

        public abstract class DestType
        {
            #region Variables and properties

            public abstract string Type { get; }

            /// <summary>
            /// The number of parameters
            /// </summary>
            internal abstract int Size { get; }

            #endregion

            #region Init

            internal abstract void FillArray(PdfArray ar);

            #endregion
        }

        /// <summary>
        /// Displays the page with XY coordinates and a zoom factor.
        /// </summary>
        public class XYZ : DestType
        {
            #region Variables and properties

            public override string Type { get { return "XYZ"; } }

            internal override int Size { get { return 3; } }

            /// <summary>
            /// Distance from the left of the screen. Null means "unchanged"
            /// </summary>
            public readonly double? X;

            /// <summary>
            /// Distance from the top of the screen. Null means "unchanged"
            /// </summary>
            public readonly double? Y;

            /// <summary>
            /// Zoom factor. 0 means unchanged.
            /// </summary>
            public readonly double Zoom;

            #endregion

            #region Init

            /// <summary>
            /// Creates a XYZ destination
            /// </summary>
            /// <param name="x">Distance from the left of the screen. Null means "unchanged"</param>
            /// <param name="y">Distance from the top of the screen. Null means "unchanged"</param>
            /// <param name="zoom">Zoom factor. 0 means unchanged.</param>
            public XYZ(double? x, double? y, double zoom)
            { X = x; Y = y; Zoom = zoom; }

            internal override void FillArray(PdfArray ar)
            {
                ar[2] = X != null ? new PdfReal(X.Value) : (PdfItem)PdfNull.Value;
                ar[3] = Y != null ? new PdfReal(Y.Value) : (PdfItem)PdfNull.Value;
                ar[4] = new PdfReal(Zoom);
            }

            #endregion
        }

        /// <summary>
        /// Display the page with its contents magnified just enough to fit 
        /// the entire page within the window both horizontally and vertically.
        /// </summary>
        public class Fit : DestType
        {
            #region Variables and properties

            public override string Type { get { return "Fit"; } }

            internal override int Size { get { return 0; } }

            #endregion

            #region Init

            internal override void FillArray(PdfArray ar)
            {

            }

            #endregion
        }

        /// <summary>
        /// Display the page designated by page, with the vertical coordinate top 
        /// positioned at the top edge of the window and the contents of the page 
        /// magnified just enough to fit the entire width of the page within the 
        /// window.
        /// </summary>
        public class FitH : DestType
        {
            #region Variables and properties

            public override string Type { get { return "FitH"; } }

            internal override int Size { get { return 1; } }

            /// <summary>
            /// Distance from top. Null means unchanged
            /// </summary>
            public readonly double? Top;

            #endregion

            #region Init

            /// <summary>
            /// Creates a FitH destination
            /// </summary>
            /// <param name="top">Distance from top. Null means unchanged</param>
            public FitH(double? top)
            { Top = top; }

            internal override void FillArray(PdfArray ar)
            {
                ar[2] = Top != null ? new PdfReal(Top.Value) : (PdfItem)PdfNull.Value;
            }

            #endregion
        }

        /// <summary>
        /// Display the page designated by page, with the horizontal coordinate
        /// left positioned at the left edge of the window and the contents of the
        /// page magnified just enough to fit the entire height of the page within
        /// the window.
        /// </summary>
        public class FitV : DestType
        {
            #region Variables and properties

            public override string Type { get { return "FitV"; } }

            internal override int Size { get { return 1; } }

            /// <summary>
            /// Distance from left. Null means unchanged
            /// </summary>
            public readonly double? Left;

            #endregion

            #region Init

            /// <summary>
            /// Creates a FitV destination
            /// </summary>
            /// <param name="left">Distance from left. Null means unchanged</param>
            public FitV(double? left)
            { Left = left; }

            internal override void FillArray(PdfArray ar)
            {
                ar[2] = Left != null ? new PdfReal(Left.Value) : (PdfItem)PdfNull.Value;
            }

            #endregion
        }

        /// <summary>
        /// Display the page designated by page, with its contents magnified just
        /// enough to fit the rectangle specified by the coordinates left, bottom,
        /// right, and top entirely within the window both horizontally and vertically.
        /// </summary>
        public class FitR : DestType
        {
            #region Variables and properties

            public override string Type { get { return "FitR"; } }

            internal override int Size { get { return 4; } }

            /// <summary>
            /// Distance from left.
            /// </summary>
            public readonly double Left;

            /// <summary>
            /// Distance from bottom.
            /// </summary>
            public readonly double Bottom;

            /// <summary>
            /// Distance from right.
            /// </summary>
            public readonly double Right;

            /// <summary>
            /// Distance from top.
            /// </summary>
            public readonly double Top;

            #endregion

            #region Init

            /// <summary>
            /// Creates a FitV destination
            /// </summary>
            /// <param name="left">Distance from left.</param>
            /// <param name="bottom">Distance from bottom.</param>
            /// <param name="right">Distance from right.</param>
            /// <param name="top">Distance from top.</param>
            public FitR(double left, double bottom, double right, double top)
            { Left = left; Bottom = bottom; Right = right; Top = top; }

            internal override void FillArray(PdfArray ar)
            {
                ar[2] = new PdfReal(Left);
                ar[3] = new PdfReal(Bottom);
                ar[4] = new PdfReal(Right);
                ar[5] = new PdfReal(Top);
            }

            #endregion
        }

        /// <summary>
        /// Display the page designated by page, with its contents magnified 
        /// just enough to fit its bounding box entirely within the window
        /// both horizontally and vertically.
        /// </summary>
        [PdfVersion("1.1")]
        public class FitB : DestType
        {
            #region Variables and properties

            public override string Type { get { return "FitB"; } }

            internal override int Size { get { return 0; } }

            #endregion

            #region Init

            internal override void FillArray(PdfArray ar)
            {

            }

            #endregion
        }

        /// <summary>
        /// Display the page designated by page, with the vertical coordinate
        /// top positioned at the top edge of the window and the contents
        /// of the page magnified just enough to fit the entire width of its
        /// bounding box within the window.
        /// </summary>
        [PdfVersion("1.1")]
        public class FitBH : DestType
        {
            #region Variables and properties

            public override string Type { get { return "FitBH"; } }

            internal override int Size { get { return 1; } }

            /// <summary>
            /// Distance from top. Null means unchanged
            /// </summary>
            public readonly double? Top;

            #endregion

            #region Init

            /// <summary>
            /// Creates a FitBH destination
            /// </summary>
            /// <param name="top">Distance from top. Null means unchanged</param>
            public FitBH(double? top)
            { Top = top; }

            internal override void FillArray(PdfArray ar)
            {
                ar[2] = Top != null ? new PdfReal(Top.Value) : (PdfItem)PdfNull.Value;
            }

            #endregion
        }

        /// <summary>
        /// Display the page designated by page, with the horizontal coordinate
        /// left positioned at the left edge of the window and the contents of the
        /// page magnified just enough to fit the entire height of the page within
        /// the window.
        /// </summary>
        [PdfVersion("1.1")]
        public class FitBV : DestType
        {
            #region Variables and properties

            public override string Type { get { return "FitBV"; } }

            internal override int Size { get { return 1; } }

            /// <summary>
            /// Distance from left. Null means unchanged
            /// </summary>
            public readonly double? Left;

            #endregion

            #region Init

            /// <summary>
            /// Creates a FitBV destination
            /// </summary>
            /// <param name="left">Distance from left. Null means unchanged</param>
            public FitBV(double? left)
            { Left = left; }

            internal override void FillArray(PdfArray ar)
            {
                ar[2] = Left != null ? new PdfReal(Left.Value) : (PdfItem)PdfNull.Value;
            }

            #endregion
        }

        #endregion
    }

    public class PdfDestinationDict : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.Destination; } }

        public PdfDestination D 
        { 
            get 
            { 
                var dest = _elems.GetPdfTypeEx("D", PdfType.Destination);
                //Dest can potentially be another PdfDestinationDict
                if (dest is PdfDestination)
                    return (PdfDestination)dest;
                throw new PdfCastException(PdfType.Destination, ErrCode.Wrong);
            } 
        }

        #endregion

        #region Init

        internal PdfDestinationDict(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfDestinationDict(elems); 
        }

        #endregion
    }

    public sealed class PdfNamedDests : PdfNameTree<PdfDestination>
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.NamedDests; } }

        #endregion

        #region Init

        public PdfNamedDests(PdfDictionary dict)
            : base(dict, PdfType.Destination)
        { }

        #endregion

        #region Index

        protected override PdfDestination Cast(PdfItem item)
        {
            if (item is PdfDestination)
                return (PdfDestination)item;
            return ((PdfDestinationDict) item).D;
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfNamedDests(elems);
        }

        #endregion
    }
}
