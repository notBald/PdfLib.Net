using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
using PdfLib.Compile;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// A rectangle
    /// </summary>
    /// <remarks>
    /// In the specs a PdfRectangle is just an array with four
    /// numeric values. This means int, real or a reference to
    /// an int or real.
    /// 
    /// [LowerLeft_x, LowerLeft_y, UpperRight_x, UpperRight_y] 
    /// </remarks>
    public sealed class PdfRectangle : PdfArray
    {
        #region Properties

        /// <summary>
        /// PdfType.Rectangle
        /// </summary>
        internal override PdfType Type { get { return PdfType.Rectangle; } }

        /// <summary>
        /// LowerLeft X
        /// </summary>
        public double LLx { get { return _items[0].GetReal(); } }

        /// <summary>
        /// LowerLeft Y
        /// </summary>
        public double LLy { get { return _items[1].GetReal(); } }

        /// <summary>
        /// UpperRight X
        /// </summary>
        public double URx { get { return _items[2].GetReal(); } }

        /// <summary>
        /// UpperRight Y
        /// </summary>
        public double URy { get { return _items[3].GetReal(); } }

        /// <summary>
        /// Width of the rectangle
        /// </summary>
        public double Width 
        { get { return Math.Abs(_items[0].GetReal() - _items[2].GetReal()); } }

        /// <summary>
        /// Height of the rectangle
        /// </summary>
        public double Height 
        { get { return Math.Abs(_items[1].GetReal() - _items[3].GetReal()); } }

        public override bool IsWritable { get { return false; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructors
        /// </summary>
        public PdfRectangle(PdfItem[] ar)
            : base(ar)
        {
            if (ar.Length != 4) // Wrong Array Length
                throw new PdfCastException(ErrSource.General, Type, ErrCode.IsCorrupt);
        }

        /// <summary>
        /// Creates a PdfRectangle from point and size
        /// </summary>
        /// <param name="pos">Position</param>
        /// <param name="width">Width of the rectangle</param>
        /// <param name="height">Height of the rectange</param>
        public PdfRectangle(xPoint pos, double width, double height)
            : this(pos.X, pos.Y, pos.X + width, pos.Y + height)
        { }

        /// <summary>
        /// Creates a PdfRectangle from doubles
        /// </summary>
        /// <param name="llx">Lower Left X</param>
        /// <param name="lly">Lower Left Y</param>
        /// <param name="urx">Upper Right X</param>
        /// <param name="ury">Upper Right Y</param>
        public PdfRectangle(double llx, double lly, double urx, double ury)
            : base(new PdfItem[4])
        {
            _items[0] = new PdfReal(llx);
            _items[1] = new PdfReal(lly);
            _items[2] = new PdfReal(urx);
            _items[3] = new PdfReal(ury);
        }
        public PdfRectangle(int llx, int lly, int urx, int ury)
            : base(new PdfItem[4])
        {
            _items[0] = new PdfInt(llx);
            _items[1] = new PdfInt(lly);
            _items[2] = new PdfInt(urx);
            _items[3] = new PdfInt(ury);
        }
        public PdfRectangle(xRect rect)
            : this(rect.LowerLeft.X, rect.LowerLeft.Y, rect.UpperRight.X, rect.UpperRight.Y)
        { }

        #endregion

        /// <summary>
        /// Insures that this array contains only numeric values and
        /// no references
        /// </summary>
        public PdfRectangle Flatten()
        {
            var ret = FlattenNumeric();
            if (ret == _items) return this;
            return new PdfRectangle(ret);
        }

        /// <summary>
        /// Creates a new rectangle where these two rectangles overlap
        /// </summary>
        /// <remarks>
        /// This function is only for use for making the CropBox. It
        /// assumes that the boxes coordinates go from ll to ur.
        /// Todo: Test Media and CropBoxes where urx is less than llx
        /// </remarks>
        internal PdfRectangle Intersect(PdfRectangle r)
        {
            double llx = Math.Max(LLx, r.LLx);
            double lly = Math.Max(LLy, r.LLy);
            double urx = Math.Min(URx, r.URx);
            double ury = Math.Min(URy, r.URy);
            return new PdfRectangle(llx, lly, urx, ury);
        }

        /// <summary>
        /// Used when moving the array to a new document
        /// </summary>
        protected override PdfArray MakeCopy(PdfItem[] data, ResTracker t)
        {
            return new PdfRectangle(data);
        }

        public override string ToString()
        {
            return string.Format("Rect ( {0} , {1} ) ( {2} , {3} )", _items[0], _items[1], _items[2], _items[3]);
        }
    }

    /// <summary>
    /// A integer rectangle (Not currently used anywhere, and impl. is out of date now)
    /// 
    /// In the PDF file it is just an array in the form:
    /// [LowerLeft_x, LowerLeft_y, UpperRight_x, UpperRight_y] 
    /// </summary>
    [DebuggerDisplay("iRect ( {LowerLeft.X} , {LowerLeft.Y} ) ( {UpperRight.X} , {UpperRight.Y} )")]
    public class PdfIntRectangle : PdfObject
    {
        /// <summary>
        /// Points that defines the rectangle
        /// </summary>
        public readonly xIntPoint LowerLeft, UpperRight;

        /// <summary>
        /// PdfType.IntRectangle
        /// </summary>
        internal override PdfType Type { get { return PdfType.IntRectangle; } }

        /// <summary>
        /// Constructors
        /// </summary>
        public PdfIntRectangle(PdfItem[] ar)
        {
            if (ar.Length != 4)
                throw new PdfReadException(ErrSource.General, Type, ErrCode.IsCorrupt);
            LowerLeft = new xIntPoint(ar[0].GetInteger(), ar[1].GetInteger());
            UpperRight = new xIntPoint(ar[2].GetInteger(), ar[3].GetInteger());
        }

        /// <summary>
        /// PdfRectangle is immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        internal override void Write(Write.Internal.PdfWriter write)
        {
            throw new NotImplementedException("This object can not be saved");
        }

        public override string ToString()
        {
            return string.Format("iRect ( {0} , {1} ) ( {2} , {3} )", LowerLeft.X, LowerLeft.Y, UpperRight.X, UpperRight.Y);
        }
    }
}
