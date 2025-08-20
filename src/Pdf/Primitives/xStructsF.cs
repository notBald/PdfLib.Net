using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.PostScript.Primitives;
using PdfLib.PostScript.Internal;
using PdfLib.PostScript;
using PdfLib.Render;
using PdfLib.Compose;
using System;

namespace PdfLib.Pdf.Primitives
{
    [DebuggerDisplay("PointF: {X},{Y}")]
    public struct xPointF
    {
        #region Variables and properties

        public /*readonly*/ float X, Y;

        #endregion

        #region Init

        public xPointF(xVectorF v) { X = v.X; Y = v.Y; }

        public xPointF(float x, float y) { X = x; Y = y; }

        public xPointF(xPoint p) { X = (float) p.X; Y = (float) p.Y; }

        public xPointF(PdfArray ar)
        {
            if (ar.Length != 2) throw new PdfReadException(PdfType.Array, ErrCode.IsCorrupt);
            X = (float)ar[0].GetReal();
            Y = (float)ar[1].GetReal();
        }

        #endregion

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return (int)X ^ (int)Y; }
        public override bool Equals(object obj)
        {
            if (obj is xPoint)
            {
                //Should perhaps use "is similar", but this function isn't actually used.
                var x = (xPoint)obj;
                return X == x.X && Y == x.Y;
            }
            return false;
        }
        public static bool operator ==(xPointF p1, xPointF p2)
        {
            return p1.X == p2.X && p1.Y == p2.Y;
        }
        public static bool operator !=(xPointF p1, xPointF p2)
        {
            return p1.X != p2.X || p1.Y != p2.Y;
        }
        public static xPointF operator +(xPointF p, xVectorF v)
        {
            return new xPointF(p.X + v.X, p.Y + v.Y);
        }
        public static xPointF operator -(xPointF p, xVectorF v)
        {
            return new xPointF(p.X - v.X, p.Y - v.Y);
        }
        public override string ToString()
        {
            return string.Format("PointF: {0},{1}", X, Y);
        }

        #endregion

        #region Helper methods

        public static xPointF Min(xPointF p1, xPointF p2)
        {
            return new xPointF(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y));
        }

        public static xPointF Max(xPointF p1, xPointF p2)
        {
            return new xPointF(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
        }

        #endregion
    }

    [DebuggerDisplay("Size: {Width},{Height}")]
    public struct xSizeF
    {
        public readonly float Width;
        public readonly float Height;
        public xSizeF(float width, float height) { Width = width; Height = height; }
        public xSizeF(xSize size) { Width = (float) size.Width; Height = (float) size.Height; }
    }

    [DebuggerDisplay("VectorF: {X},{Y}")]
    public struct xVectorF
    {
        #region Variables and properties

        public readonly float X, Y;

        /// <summary>
        /// Length of the vector
        /// </summary>
        public float Length { get { return (float) Math.Sqrt(X * X + Y * Y); } }

        /// <summary>
        /// Angel in degrees, to horizontal (X) plane
        /// </summary>
        public float Angle { get { return (float) (Math.Atan2(Y, X) * 180 / Math.PI); } }

        /// <summary>
        /// Normalized unit vector
        /// </summary>
        public xVectorF Unit { get { float L = Length; return new xVectorF(X / L, Y / L); } }

        /// <summary>
        /// Vector that points in the opposite direction
        /// </summary>
        public xVectorF Inverse { get { return new xVectorF(-X, -Y); } }

        #endregion

        #region Init

        public xVectorF(float x, float y) { X = x; Y = y; }
        public xVectorF(xPointF p) { X = p.X; Y = p.Y; }

        /// <summary>
        /// Creates a vector from two points
        /// </summary>
        /// <param name="start">Starting point</param>
        /// <param name="end">Ending point</param>
        public xVectorF(xPointF start, xPointF end) { X = end.X - start.X; Y = end.Y - start.Y; }

        #endregion

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return (int)X ^ (int)Y; }
        public override bool Equals(object obj)
        {
            return (obj is xVectorF x) && (X - x.X).IsZero() && (Y - x.Y).IsZero();
        }
        public static bool operator ==(xVectorF v1, xVectorF v2)
        {
            return v1.Equals(v2);
        }
        public static bool operator !=(xVectorF v1, xVectorF v2)
        {
            return !v1.Equals(v2);
        }
        public static xVectorF operator +(xVectorF v1, xVectorF v2)
        {
            return new xVectorF(v1.X + v2.X, v1.Y + v2.Y);
        }
        public static xVectorF operator -(xVectorF v1, xVectorF v2)
        {
            return new xVectorF(v1.X - v2.X, v1.Y - v2.Y);
        }
        public static float operator *(xVectorF v1, xVectorF v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y;
        }
        public static xVectorF operator *(xVectorF v, float mul)
        {
            return new xVectorF(v.X * mul, v.Y * mul);
        }

        public override string ToString()
        {
            return string.Format("VectorF: {0},{1}", X, Y);
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// Creates a new vector with the desired length
        /// </summary>
        /// <param name="len">Length of the new vector</param>
        /// <returns>New vector with the given length</returns>
        public xVectorF NewLength(float len)
        {
            return Unit * len;
        }

        /// <summary>
        /// Creates a new vector rotated arround zero
        /// </summary>
        public xVectorF Rotate(float degrees)
        {
            return RotateVector(this, (float) (degrees / 180 * Math.PI));
        }

        /// <summary>
        /// Creates a new vector, rotated arround the center point
        /// </summary>
        public xVectorF Rotate(float degrees, xPointF center)
        {
            return RotateVector(this, center, (float) (degrees / 180 * Math.PI));
        }

        /// <summary>
        /// The dot product of two vectors
        /// </summary>
        public double Dot(xVectorF two)
        {
            return two.X * X + two.Y * Y;
        }

        /// <summary>
        /// The dot product of two vectors
        /// </summary>
        public static double Dot(xVectorF one, xVectorF two)
        {
            return two.X * one.X + two.Y * one.Y;
        }

        /// <summary>
        /// Cross product of two vectors
        /// </summary>
        public double Cross(xVectorF two)
        {
            return X * two.Y - Y * two.X;
        }

        /// <summary>
        /// Returns the angle in degrees bewteen two unit vectors
        /// </summary>
        public static float AngleBetween(xVectorF one, xVectorF two)
        {
            //Dot product
            var dot = two.X * one.X + two.Y * one.Y;

            return (float) (Math.Acos(dot) * 180 / Math.PI);
        }

        public static xVectorF RotateVector(xVectorF one, float radians)
        {
            float x = (float) (one.X * Math.Cos(radians) - one.Y * Math.Sin(radians));
            float y = (float) (one.X * Math.Sin(radians) + one.Y * Math.Cos(radians));
            return new xVectorF(x, y);
        }

        public static xVectorF RotateVector(xVectorF one, xPointF center, float radians)
        {
            float x = (float) ((one.X - center.X) * Math.Cos(radians) - (one.Y - center.Y) * Math.Sin(radians) + center.X);
            float y = (float) ((one.X - center.X) * Math.Sin(radians) + (one.Y - center.Y) * Math.Cos(radians) + center.Y);
            return new xVectorF(x, y);
        }

        #endregion
    }

    [DebuggerDisplay("LineF: {Start.X},{Start.Y} -> {End.X},{End.Y}")]
    public struct xLineF
    {
        public readonly xPointF Start, End;
        public xRectF Bounds
        {
            get
            {
                float llx, lly, urx, ury;
                if (Start.X < End.X)
                {
                    llx = Start.X;
                    urx = End.X;
                }
                else
                {
                    llx = End.X;
                    urx = Start.X;
                }
                if (Start.Y < End.Y)
                {
                    lly = Start.Y;
                    ury = End.Y;
                }
                else
                {
                    lly = End.Y;
                    ury = Start.Y;
                }
                return new xRectF(llx, lly, urx, ury);
            }
        }

        /// <summary>
        /// Length of the line
        /// </summary>
        public float Length { get { return (float) Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2)); } }

        /// <summary>
        /// Vector from the Start point to the End point
        /// </summary>
        public xVectorF Vector
        {
            get { return new xVectorF(End.X - Start.X, End.Y - Start.Y); }
        }
        /// <summary>
        /// The middle point of the line.
        /// </summary>
        public xPointF MidPoint
        {
            get { return new xPointF((Start.X + End.X) / 2, (Start.Y + End.Y) / 2); }
        }
        public xLineF(xPointF start, xPointF end)
        { Start = start; End = end; }
        public xLineF(float x1, float y1, float x2, float y2)
        { Start = new xPointF(x1, y1); End = new xPointF(x2, y2); }

        /// <summary>
        /// Calculates a point on the line.
        /// </summary>
        /// <param name="distance_from_start">Distance from the starting point of the line</param>
        /// <returns>Point on the line</returns>
        public xPointF PointOnLine(float distance_from_start)
        {
            var ratio = distance_from_start / Length;

            return new xPointF(ratio * End.X + (1 - ratio) * Start.X, ratio * End.Y + (1 - ratio) * Start.Y);
        }

        /// <summary>
        /// Creates a line that goes pendicular through the start point of this line.
        /// </summary>
        /// <param name="distance">Length of the new line</param>
        /// <param name="center">Whenever the line is to be centered on the point</param>
        /// <returns>Line pendicular to this line</returns>
        public xLineF PendicularStart(float distance, bool center)
        {
            if (center)
                return PendicularCenter(Start.X, Start.Y, End.X, End.Y, -distance);
            else
                return Pendicular(Start.X, Start.Y, End.X, End.Y, -distance);
        }

        /// <summary>
        /// Creates a line that goes pendicular through the end point of this line.
        /// </summary>
        /// <param name="distance">Length of the new line</param>
        /// <param name="center">Whenever the line is to be centered on the point</param>
        /// <returns>Line pendicular to this line</returns>
        public xLineF PendicularEnd(float distance, bool center)
        {
            if (center)
                return PendicularCenter(End.X, End.Y, Start.X, Start.Y, distance);
            else
                return Pendicular(End.X, End.Y, Start.X, Start.Y, distance);
        }

        /// <summary>
        /// Finds a pendicular line with length "distance" that goes through x1, y1
        /// </summary>
        /// <param name="x1">Point 1.X</param>
        /// <param name="y1">Point 1.Y</param>
        /// <param name="x2">Point 2.X</param>
        /// <param name="y2">Point 2.Y</param>
        /// <param name="distance">Length of the new line</param>
        /// <returns>A line pendicular to the old line</returns>
        private xLineF PendicularCenter(float x1, float y1, float x2, float y2, float distance)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            var dist = (float) Math.Sqrt(dx * dx + dy * dy);
            dx /= dist;
            dy /= dist;
            distance /= 2;
            return new xLineF(x1 + distance * dy, y1 - distance * dx, x1 - distance * dy, y1 + distance * dx);
        }

        /// <summary>
        /// Finds a pendicular line with length "distance" that goes through x1, y1
        /// </summary>
        /// <param name="x1">Point 1.X</param>
        /// <param name="y1">Point 1.Y</param>
        /// <param name="x2">Point 2.X</param>
        /// <param name="y2">Point 2.Y</param>
        /// <param name="distance">Length of the new line</param>
        /// <returns>A line pendicular to the old line</returns>
        private xLineF Pendicular(float x1, float y1, float x2, float y2, float distance)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            var dist = (float) Math.Sqrt(dx * dx + dy * dy);
            dx /= dist;
            dy /= dist;
            return new xLineF(x1, y1, x1 - (distance) * dy, y1 + (distance) * dx);
        }
    }

    public struct xRectF
    {
        #region Variables and properties

        public bool IsEmpty => Width == 0 || Height == 0;

        public float Left
        {
            get { return LowerLeft.X < UpperRight.X ? LowerLeft.X : UpperRight.X; }
        }
        public float Right
        {
            get { return LowerLeft.X > UpperRight.X ? LowerLeft.X : UpperRight.X; }
        }
        public float Top
        {
            get { return LowerLeft.Y > UpperRight.Y ? LowerLeft.Y : UpperRight.Y; }
        }
        public float Bottom
        {
            get { return LowerLeft.Y < UpperRight.Y ? LowerLeft.Y : UpperRight.Y; }
        }

        public readonly xPointF LowerLeft, UpperRight;

        public float Width { get { return UpperRight.X - LowerLeft.X; } }
        public float Height { get { return UpperRight.Y - LowerLeft.Y; } }
        public float X { get { return LowerLeft.X; } }
        public float Y { get { return LowerLeft.Y; } }

        /// <summary>
        /// Rectangle where LowerLeft is garnateeded to be the smallest (or most negative)
        /// </summary>
        public xRectF Normalized
        {
            get
            {
                float xmin, ymin, xmax, ymax;
                if (LowerLeft.X < UpperRight.X)
                {
                    xmin = LowerLeft.X;
                    xmax = UpperRight.X;
                }
                else
                {
                    xmax = LowerLeft.X;
                    xmin = UpperRight.X;
                }
                if (LowerLeft.Y < UpperRight.Y)
                {
                    ymin = LowerLeft.Y;
                    ymax = UpperRight.Y;
                }
                else
                {
                    ymax = LowerLeft.Y;
                    ymin = UpperRight.Y;
                }
                return new xRectF(xmin, ymin, xmax, ymax);
            }
        }

        #endregion

        #region Init

        public xRectF(PdfRectangle rect)
        {
            LowerLeft = new xPointF((float) rect.LLx, (float) rect.LLy);
            UpperRight = new xPointF((float) rect.URx, (float) rect.URy);
        }

        public xRectF(xRect rect)
        {
            LowerLeft = new xPointF(rect.LowerLeft);
            UpperRight = new xPointF(rect.UpperRight);
        }

        public xRectF(xSizeF size)
            : this(0, 0, size.Width, size.Height)
        {

        }

        public xRectF(xPointF location, xSizeF size)
            : this(location.X, location.Y, location.X + size.Width, location.Y + size.Height)
        {

        }

        public xRectF(float llx, float lly, float urx, float ury)
        {
            LowerLeft = new xPointF(llx, lly);
            UpperRight = new xPointF(urx, ury);
        }

        /// <summary>
        /// Creates a xRect
        /// </summary>
        /// <param name="ll">LowerLeft</param>
        /// <param name="ur">UpperRight</param>
        public xRectF(xPointF ll, xPointF ur)
        {
            LowerLeft = ll;
            UpperRight = ur;
        }

        #endregion

        #region Utility

        public bool IsOn(xPointF p)
        {
            return LowerLeft.X <= p.X && p.X <= UpperRight.X &&
                   LowerLeft.Y <= p.Y && p.Y <= UpperRight.Y;
        }

        public bool IntersectsWith(xRectF r)
        {
            return LowerLeft.X < r.UpperRight.X && UpperRight.X > r.LowerLeft.X &&
                    UpperRight.Y > r.LowerLeft.Y && LowerLeft.Y < r.UpperRight.Y;
        }

        /// <summary>
        /// Creates a rectangle that encompasses the two rectanges
        /// </summary>
        public xRectF Enlarge(xRectF r)
        {
            r = r.Normalized;
            var t = Normalized;
            return new xRectF(
                Math.Min(t.LowerLeft.X, r.LowerLeft.X),
                Math.Min(t.LowerLeft.Y, r.LowerLeft.Y),
                Math.Max(t.UpperRight.X, r.UpperRight.X),
                Math.Max(t.UpperRight.Y, r.UpperRight.Y)
            );
        }

        public override string ToString()
        {
            return string.Format("RectF: {0:0};{1:0};{2:0};{3:0}", X, Y, Width, Height);
        }

        #endregion
    }
}
