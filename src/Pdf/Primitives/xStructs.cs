using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.PostScript.Primitives;
using PdfLib.PostScript.Internal;
using PdfLib.PostScript;
using PdfLib.Render;
using PdfLib.Compose;
using System;

// This file contains a collection of convenient structs.
//Todo: Finish "Overrides recommended for value types" for all structs.
namespace PdfLib.Pdf.Primitives
{
    internal static class Extensions
    {
        private const double Epsilon = 1e-10;
        private const float EpsilonF = 1e-8F;

        //This is a neat experiment, can safly be ditched and replaced with ==
        public static bool IsZero(this double d)
        {
            return Math.Abs(d) < Epsilon;
        }
        public static bool IsZero(this float d)
        {
            return Math.Abs(d) < EpsilonF;
        }
    }

    public enum xTextRenderMode
    {
        Fill,
        Stroke,
        FillAndStroke,
        Invisible,
        FillAndPath,
        StrokeAndPath,
        FillStrokeAndPath,
        Path
    }

    public enum xFillRule
    {
        EvenOdd = 0,
        Nonzero = 1
    }

    public enum xLineJoin
    {
        //Angled edge
        Miter = 0,
        //Rounded edge (determined by line width)
        Round = 1,
        //Capped
        Bevel = 2

        //Don't change these numbers
    }

    public enum xLineCap
    {
        Butt = 0,
        Round = 1,
        Square = 2
        //Don't change these numbers
    }

    /// <summary>
    /// An affine matrix
    /// </summary>
    /// <remarks>
    /// |M11 M12 OffX|
    /// |M21 M22 Offy|
    /// | 0   0    1 |
    /// </remarks>
    public struct xMatrix
    {
        #region Variables and properties

        public readonly double M11, M12, M21, M22, OffsetX, OffsetY;

        public bool IsIdentity
        {
            get
            {
                return M11 == 1 && M12 == 0 && M21 == 0 &&
                       M22 == 1 && OffsetX == 0 && OffsetY == 0;
            }
        }

        public double Determinant
        {
            get
            {
                return M11 * M22 - M12 * M21;
            }
        }

        public xMatrix Inverse
        {
            get
            {
                var inv = 1 / Determinant;
                return new xMatrix(
                     inv * M22,
                    -inv * M12,
                    -inv * M21,
                     inv * M11,
                     inv * (M21 * OffsetY - OffsetX * M22),
                     inv * (M12 * OffsetX - M11 * OffsetY)
                );
            }
        }

        public bool HasInverse
        {
            get { return Math.Abs(Determinant) > 2.22044604925031E-15; }
        }

        public static xMatrix Identity { get { return new xMatrix(1, 0, 0, 1, 0, 0); } }

        #endregion

        #region Init

        /// <summary>
        /// Creates a rotation matrix
        /// </summary>
        /// <param name="angle">Counter clockwise rotation (in degress)</param>
        public xMatrix(double angle)
        {
            angle %= 360;
            angle = Math.PI * angle / 180.0;
            M11 = Math.Cos(angle); M12 = Math.Sin(angle); M21 = -M12; M22 = M11;
            OffsetX = 0; OffsetY = 0;
        }

        /// <summary>
        /// Creates a rotation matrix
        /// </summary>
        /// <param name="angle">Counter clockwise rotation (in degress)</param>
        /// <param name="offx">X offset</param>
        /// <param name="offy">Y offset</param>
        public xMatrix(double angle, double offx, double offy)
        {
            angle %= 360;
            angle = Math.PI * angle / 180.0;
            M11 = Math.Cos(angle); M12 = Math.Sin(angle); M21 = -M12; M22 = M11;
            OffsetX = offx; OffsetY = offy;
        }

        /// <summary>
        /// Creates translation matrix
        /// </summary>
        /// <param name="offx">X offset</param>
        /// <param name="offy">Y offset</param>
        public xMatrix(double offx, double offy)
        {
            M11 = 1; M12 = 0; M21 = 0; M22 = 1;
            OffsetX = offx; OffsetY = offy;
        }

        public xMatrix(double m11, double m12, double m21, double m22, double offx, double offy)
        {
            M11 = m11;
            M12 = m12;
            M21 = m21;
            M22 = m22;
            OffsetX = offx;
            OffsetY = offy;
        }

        /// <summary>
        /// Creates this matrix as a product of the multiplcation a x b
        /// </summary>
        private xMatrix(xMatrix a, xMatrix b)
        {
            M11 = a.M11 * b.M11 + a.M12 * b.M21;
            M12 = a.M11 * b.M12 + a.M12 * b.M22;
            M21 = a.M21 * b.M11 + a.M22 * b.M21;
            M22 = a.M21 * b.M12 + a.M22 * b.M22;
            OffsetX = a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX;
            OffsetY = a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY;
        }

        //Used when creating a matrix of a stack with parameters in reverse
        internal static xMatrix Create(double offy, double offx, double m22, double m21, double m12, double m11)
        {
            return new xMatrix(m11, m12, m21, m22, offx, offy);
        }

        internal xMatrix(RealArray ar)
        {
            if (ar.Length != 6) throw new PdfCastException(ErrSource.General, PdfType.RealArray, ErrCode.IsCorrupt);
            M11 = ar[0];
            M12 = ar[1];
            M21 = ar[2];
            M22 = ar[3];
            OffsetX = ar[4];
            OffsetY = ar[5];
        }

        internal xMatrix(PSValArray<double, PSItem> ar)
        {
            if (ar.Length != 6) throw new PSCastException("Corrupt PostScript matrix");
            M11 = ar[0];
            M12 = ar[1];
            M21 = ar[2];
            M22 = ar[3];
            OffsetX = ar[4];
            OffsetY = ar[5];
        }

        #endregion

        #region Math

        /// <summary>
        /// Rotate the matrix
        /// </summary>
        /// <param name="angle">Angle in degrees</param>
        /// <returns>A rotated matrix</returns>
        public xMatrix Rotate(double angle)
        {
            return new xMatrix(this, new xMatrix(angle));
        }

        /// <summary>
        /// Rotates the matrix around a origin point
        /// </summary>
        /// <param name="angle">Angle in degrees</param>
        /// <param name="center_x">Point to rotate arround</param>
        /// <param name="center_y">Point to rotate arround</param>
        /// <returns></returns>
        public xMatrix Rotate(double angle, double center_x, double center_y)
        {
            angle %= 360;
            angle = Math.PI * angle / 180.0;
            var M11 = Math.Cos(angle); var M12 = Math.Sin(angle); var M21 = -M12; var M22 = M11;
            var offsetX = center_x * (1.0 - M11) + center_y * M12;
            var offsetY = center_y * (1.0 - M11) - center_x * M12;
            return new xMatrix(this, new xMatrix(M11, M12, M21, M22, offsetX, offsetY));
        }

        /// <summary>
        /// Transforms the four points in the Quadrilateral
        /// </summary>
        public xQuadrilateral Transform(xQuadrilateral q)
        {
            return new xQuadrilateral(Transform(q.P1), Transform(q.P2), Transform(q.P3), Transform(q.P4));
        }

        /// <summary>
        /// Transforms a rectangle
        /// </summary>
        public xRect Transform(xRect r)
        {
            return new xRect(Transform(r.LowerLeft), Transform(r.UpperRight));
        }

        public PdfRectangle Transform(PdfRectangle r)
        {
            return new PdfRectangle(Transform(new xRect(r)));
        }

        /// <summary>
        /// Transforms a point
        /// 
        ///    [ M11 M21 Ox ]   [ x ]   [ x' ]
        ///    [ M12 M22 Oy ] X [ y ] = [ y' ]
        ///    [  0   0   1 ]   [ 1 ]   [ 1  ]
        /// </summary>
        public xPoint Transform(xPoint p)
        {
            //Rows are multiplied with columns.
            return new xPoint(M11 * p.X + M21 * p.Y + OffsetX,
                              M12 * p.X + M22 * p.Y + OffsetY);
        }

        /// <summary>
        /// Transforms a size
        /// 
        ///    [ M11 M21 Ox ]   [ x ]   [ x' ]
        ///    [ M12 M22 Oy ] X [ y ] = [ y' ]
        ///    [  0   0   1 ]   [ 1 ]   [ 1  ]
        /// </summary>
        public xSize Transform(xSize p)
        {
            //Rows are multiplied with columns.
            return new xSize(M11 * p.Width + M21 * p.Height + OffsetX,
                             M12 * p.Width + M22 * p.Height + OffsetY);
        }

        /// <summary>
        /// Translates this matrix
        /// </summary>
        public xMatrix Translate(double x, double y)
        {
            return new xMatrix(this, new xMatrix(x, y));
        }

        /// <summary>
        /// Translates this matrix
        /// </summary>
        public xMatrix TranslatePrepend(double x, double y)
        {
            xMatrix trans = new xMatrix(x, y);

            return new xMatrix(trans, this);
        }

        public xMatrix Append(xMatrix m)
        {
            return new xMatrix(this, m);
        }

        public xMatrix Prepend(xMatrix m)
        {
            return new xMatrix(m, this);
        }

        public xMatrix Scale(double scaleX, double scaleY)
        {
            return Append(new xMatrix(scaleX, 0, 0, scaleY, 0, 0));
        }

        public xMatrix Scale(double scaleX, double scaleY, double center_x, double center_y)
        {
            return Append(new xMatrix(scaleX, 0, 0, scaleY, center_x - scaleX * center_x, center_y - scaleY * center_y));
        }

        public xMatrix ScalePrepend(double scaleX, double scaleY)
        {
            return Prepend(new xMatrix(scaleX, 0, 0, scaleY, 0, 0));
        }

        public xMatrix ScalePrepend(double scaleX, double scaleY, double center_x, double center_y)
        {
            return Prepend(new xMatrix(scaleX, 0, 0, scaleY, center_x - scaleX * center_x, center_y - scaleY * center_y));
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// If this metrix is equal, with the exception of offsets.
        /// </summary>
        public bool ScaleAndSkewEquals(xMatrix m)
        {
            return M11 == m.M11 && M12 == m.M12 && M21 == m.M21 && M22 == m.M22;
        }

        internal RealArray ToArray()
        {
            return new RealArray(M11, M12, M21, M22, OffsetX, OffsetY);
        }

        #endregion

        #region Overrides recommended for value types

        public override int GetHashCode() 
        { 
            return M11.GetHashCode() + 
                   M12.GetHashCode() + 
                   M21.GetHashCode() + 
                   M22.GetHashCode() + 
                   OffsetX.GetHashCode() + 
                   OffsetY.GetHashCode(); 
        }
        public override bool Equals(object obj) => obj is xMatrix m && Equals(m);
        public bool Equals(xMatrix x)
        {
            return M11 == x.M11 && M12 == x.M12 && M21 == x.M21 && M22 == x.M22 && OffsetX == x.OffsetX && OffsetY == x.OffsetY;
        }

        public static bool operator ==(xMatrix m1, xMatrix m2)
        {
            return m1.Equals(m2);
        }
        public static bool operator !=(xMatrix m1, xMatrix m2)
        {
            return !m1.Equals(m2);
        }
        public override string ToString()
        {
            return string.Format("[{0} {1} {2} {3} {4} {5}]", M11, M12, M21, M22, OffsetX, OffsetY);
        }

        #endregion
    }

    public struct x3x3Matrix
    {
        #region Variables and properties

        public readonly double M11, M12, M13, M21, M22, M23, M31, M32, M33;

        public bool IsIdentity
        {
            get
            {
                return M11 == 1 && M12 == 0 && M13 == 0 && 
                       M21 == 0 && M22 == 1 && M23 == 0 && 
                       M31 == 0 && M32 == 0 && M33 == 1;
            }
        }

        public static x3x3Matrix Identity { get { return new x3x3Matrix(1, 0, 0, 0, 1, 0, 0, 0, 1); } }

        #endregion

        #region Init

        public x3x3Matrix(double m11, double m12, double m13, double m21, double m22, double m23, double m31, double m32, double m33)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M31 = m31;
            M32 = m32;
            M33 = m33;
        }

        internal x3x3Matrix(RealArray ar)
        {
            if (ar.Length != 9) throw new PdfCastException(ErrSource.General, PdfType.RealArray, ErrCode.IsCorrupt);
            M11 = ar[0];
            M12 = ar[1];
            M13 = ar[2];
            M21 = ar[3];
            M22 = ar[4];
            M23 = ar[5];
            M31 = ar[6];
            M32 = ar[7];
            M33 = ar[8];
        }

        #endregion

        #region Math

        public x3Vector Transform(x3Vector v)
        {
            return new x3Vector
            (
                M11 * v.X + M12 * v.Y + M13 * v.Z,
                M21 * v.X + M22 * v.Y + M23 * v.Z,
                M31 * v.X + M22 * v.Y + M33 * v.Z
            );
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// Converts xMatrix to real array
        /// </summary>
        internal RealArray ToArray()
        {
            return new RealArray(M11, M12, M13, M21, M22, M23, M31, M32, M33);
        }

        //Not bothering with the other recomended overrides
        public override string ToString()
        {
            return string.Format("[{0} {1} {2} {3} {4} {5} {6} {7} {8}]", M11, M12, M13, M21, M22, M23, M31, M32, M33);
        }

        #endregion
    }

    [DebuggerDisplay("Point: {X},{Y}")]
    public struct xPoint
    {
        #region Variables and properties

        public /*readonly*/ double X, Y;

        #endregion

        #region Init

        public xPoint(xVector v) { X = v.X; Y = v.Y; }

        public xPoint(double x, double y) { X = x; Y = y; }

        public xPoint(PdfArray ar)
        {
            if (ar.Length != 2) throw new PdfReadException(PdfType.Array, ErrCode.IsCorrupt);
            X = ar[0].GetReal();
            Y = ar[1].GetReal();
        }

        #endregion

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return (int) X ^ (int) Y; }
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
        public static bool operator ==(xPoint p1, xPoint p2)
        {
            return p1.X == p2.X && p1.Y == p2.Y;
        }
        public static bool operator !=(xPoint p1, xPoint p2)
        {
            return p1.X != p2.X || p1.Y != p2.Y;
        }
        public static xPoint operator +(xPoint p, xVector v)
        {
            return new xPoint(p.X + v.X, p.Y + v.Y);
        }
        public static xPoint operator -(xPoint p, xVector v)
        {
            return new xPoint(p.X - v.X, p.Y - v.Y);
        }
        public override string ToString()
        {
            return string.Format("Point: {0},{1}", X, Y);
        }

        #endregion

        #region Helper methods

        public static xPoint Min(xPoint p1, xPoint p2)
        {
            return new xPoint(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y));
        }

        public static xPoint Max(xPoint p1, xPoint p2)
        {
            return new xPoint(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
        }

        #endregion
    }

    

    [DebuggerDisplay("Vector: {X},{Y},{Z}")]
    public struct x3Vector
    {
        #region Variables and properties

        public readonly double X, Y, Z;

        /// <summary>
        /// Length of the vector
        /// </summary>
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }

        #endregion

        #region Init

        public x3Vector(double x, double y, double z) { X = x; Y = y; Z = z; }
        public x3Vector(x3Vector v) { X = v.X; Y = v.Y; Z = v.Z; }

        #endregion

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return ((int)X ^ (int)Y) ^ (int)Z; }
        public override bool Equals(object obj)
        {
            if (obj is x3Vector)
            {
                //Should perhaps use "is similar", but this function isn't actually used.
                var x = (x3Vector)obj;
                return X == x.X && Y == x.Y && Z == x.Z;
            }
            return false;
        }
        public static bool operator ==(x3Vector p1, x3Vector p2)
        {
            return p1.Equals(p2);
        }
        public static bool operator !=(x3Vector p1, x3Vector p2)
        {
            return !p1.Equals(p2);
        }
        public override string ToString()
        {
            return string.Format("Vector: {0},{1},{2}", X, Y, Z);
        }

        #endregion
    }

    [DebuggerDisplay("Vector: {X},{Y}")]
    public struct xVector
    {
        #region Variables and properties

        public readonly double X, Y;

        /// <summary>
        /// Length of the vector
        /// </summary>
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }

        /// <summary>
        /// Angel in degrees, to horizontal (X) plane
        /// </summary>
        public double Angle { get { return Math.Atan2(Y, X) * 180 / Math.PI; } }

        /// <summary>
        /// Normalized unit vector
        /// </summary>
        public xVector Unit { get { double L = Length; return new xVector(X / L, Y / L); } }

        /// <summary>
        /// Vector that points in the opposite direction
        /// </summary>
        public xVector Inverse { get { return new xVector(-X, -Y); } }

        #endregion

        #region Init

        public xVector(double x, double y) { X = x; Y = y; }
        public xVector(xPoint p) { X = p.X; Y = p.Y; }

        /// <summary>
        /// Creates a vector from two points
        /// </summary>
        /// <param name="start">Starting point</param>
        /// <param name="end">Ending point</param>
        public xVector(xPoint start, xPoint end) { X = end.X - start.X; Y = end.Y - start.Y; }

        #endregion

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return (int)X ^ (int)Y; }
        public override bool Equals(object obj)
        {
            return (obj is xVector x) && (X - x.X).IsZero() && (Y - x.Y).IsZero();
        }
        public static bool operator ==(xVector v1, xVector v2)
        {
            return v1.Equals(v2);
        }
        public static bool operator !=(xVector v1, xVector v2)
        {
            return !v1.Equals(v2);
        }
        public static xVector operator +(xVector v1, xVector v2)
        {
            return new xVector(v1.X + v2.X, v1.Y + v2.Y);
        }
        public static xVector operator -(xVector v1, xVector v2)
        {
            return new xVector(v1.X - v2.X, v1.Y - v2.Y);
        }
        public static double operator *(xVector v1, xVector v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y;
        }
        public static xVector operator *(xVector v, double mul)
        {
            return new xVector(v.X * mul, v.Y * mul);
        }
        
        public override string ToString()
        {
            return string.Format("Vector: {0},{1}", X, Y);
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// Creates a new vector with the desired length
        /// </summary>
        /// <param name="len">Length of the new vector</param>
        /// <returns>New vector with the given length</returns>
        public xVector NewLength(double len)
        {
            return Unit * len;
        }

        /// <summary>
        /// Creates a new vector rotated arround zero
        /// </summary>
        public xVector Rotate(double degrees)
        {
            return RotateVector(this, degrees / 180 * Math.PI);
        }

        /// <summary>
        /// Creates a new vector, rotated arround the center point
        /// </summary>
        public xVector Rotate(double degrees, xPoint center)
        {
            return RotateVector(this, center, degrees / 180 * Math.PI);
        }

        /// <summary>
        /// The dot product of two vectors
        /// </summary>
        public double Dot(xVector two)
        {
            return two.X * X + two.Y * Y;
        }

        /// <summary>
        /// The dot product of two vectors
        /// </summary>
        public static double Dot(xVector one, xVector two)
        {
            return two.X * one.X + two.Y * one.Y;
        }

        /// <summary>
        /// Cross product of two vectors
        /// </summary>
        public double Cross(xVector two)
        {
            return X * two.Y - Y * two.X;
        }

        /// <summary>
        /// Returns the angle in degrees bewteen two unit vectors
        /// </summary>
        public static double AngleBetween(xVector one, xVector two)
        {
            //Dot product
            var dot = two.X * one.X + two.Y * one.Y;

            return Math.Acos(dot) * 180 / Math.PI;
        }

        public static xVector RotateVector(xVector one, double radians)
        {
            double x = one.X * Math.Cos(radians) - one.Y * Math.Sin(radians);
            double y = one.X * Math.Sin(radians) + one.Y * Math.Cos(radians);
            return new xVector(x, y);
        }

        public static xVector RotateVector(xVector one, xPoint center, double radians)
        {
            double x = (one.X - center.X) * Math.Cos(radians) - (one.Y - center.Y) * Math.Sin(radians) + center.X;
            double y = (one.X - center.X) * Math.Sin(radians) + (one.Y - center.Y) * Math.Cos(radians) + center.Y;
            return new xVector(x, y);
        }

        #endregion
    }

    /// <summary>
    /// Integer based point
    /// </summary>
    [DebuggerDisplay("Int Point: {X},{Y}")]
    public struct xIntPoint
    {
        public readonly int X;
        public readonly int Y;
        public xIntPoint(int x, int y) { X = x; Y = y; }
        public xIntPoint(PdfArray ar)
        {
            if (ar.Length != 2) throw new PdfReadException(PdfType.Array, ErrCode.IsCorrupt);
            X = ar[0].GetInteger();
            Y = ar[1].GetInteger();
        }

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return X ^ Y; }
        public override bool Equals(object obj)
        {
            if (obj is xIntPoint)
            {
                //Should perhaps use "is similar", but this function isn't actually used.
                var x = (xIntPoint)obj;
                return X == x.X && Y == x.Y;
            }
            return false;
        }
        public static bool operator ==(xIntPoint p1, xIntPoint p2)
        {
            return p1.Equals(p2);
        }
        public static bool operator !=(xIntPoint p1, xIntPoint p2)
        {
            return !p1.Equals(p2);
        }
        public override string ToString()
        {
            return string.Format("Int Point: {0},{1}", X, Y);
        }

        #endregion
    }

    /// <remarks>
    /// Got quite a few Range structs now. Heh.
    /// </remarks>
    [DebuggerDisplay("[{Min} {Max}]")]
    public struct xRange
    {
        public readonly double Min;
        public readonly double Max;
        public xRange(double min, double max) { Min = min; Max = max; }

        /// <summary>
        /// True if this range is equal to the default range
        /// </summary>
        public bool Default { get { return Min == 0 && Max == 1; } }

        /// <summary>
        /// Clips a value to Min/Max
        /// </summary>
        public double Clip(double val)
        {
            if (val < Min) return Min;
            if (val > Max) return Max;
            return val;
        }

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return unchecked(((int) Max) ^ ((int) Min)); }
        public override bool Equals(object obj)
        {
            if (obj is xRange)
            {
                //Should perhaps use "is similar".
                return this == (xRange)obj;
            }
            return false;
        }
        public static bool operator ==(xRange p1, xRange p2)
        {
            return p1.Max == p2.Max && p1.Min == p2.Min;
        }
        public static bool operator !=(xRange p1, xRange p2)
        {
            return p1.Max != p2.Max || p1.Min != p2.Min;
        }
        public override string ToString()
        {
            return string.Format("[{0} {1}]", Min, Max);
        }

        #endregion

        internal static bool Compare(xRange[] range1, xRange[] range2)
        {
            if (ReferenceEquals(range1, range2)) return true;
            if (range1 == null) return range2 == null;
            if (range2 == null || range1.Length != range2.Length) return false;
            for (int c = 0; c < range1.Length; c++)
                if (range1[c] != range2[c]) return false;
            return true;
        }

        internal static xRange[] Create(RealArray ar)
        {
            var ret = new xRange[ar.Length / 2];
            for (int c = 0, pos = 0; c < ret.Length; c++)
                ret[c] = new xRange(ar[pos++], ar[pos++]);
            return ret;
        }

        public static xRange[] Create(double[] ar)
        {
            var ret = new xRange[ar.Length / 2];
            for (int c = 0, pos = 0; c < ret.Length; c++)
                ret[c] = new xRange(ar[pos++], ar[pos++]);
            return ret;
        }

        public static double[] ToArray(xRange[] ar)
        {
            var ret = new double[2 * ar.Length];
            for (int i = 0, c = 0; c < ret.Length; i++ )
            {
                var v = ar[i];
                ret[c++] = v.Min;
                ret[c++] = v.Max;
            }
            return ret;
        }

        /// <summary>
        /// Reverses the min/max values in a range
        /// </summary>
        /// <param name="r">Range to reverse</param>
        /// <returns>reversed ramges</returns>
        public static xRange Reverse(xRange r)
        { return new xRange(r.Max, r.Min); }

        /// <summary>
        /// Reverses the min/max values in a set of ranges
        /// </summary>
        /// <param name="ranges">Ranges to reverse</param>
        public static void Reverse(xRange[] ranges)
        {
            for (int c = 0; c < ranges.Length; c++)
                ranges[c] = Reverse(ranges[c]);
        }
    }

    /// <remarks>
    /// Got quite a few Range structs now. Heh.
    /// </remarks>
    [DebuggerDisplay("[{Min} {Max}]")]
    public struct xIntRange
    {
        public int Min;
        public int Max;
        public xIntRange(int min, int max) { Min = min; Max = max; }

        /// <summary>
        /// Clips a value to Min/Max
        /// </summary>
        public int Clip(int val)
        {
            if (val < Min) return Min;
            if (val > Max) return Max;
            return val;
        }

        public bool Contains(int val)
        {
            return Min <= val && val <= Max;
        }

        public bool Overlaps(xIntRange r)
        {
            return !(Min > r.Max || Max < r.Min);
        }

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return Max ^ Min; }
        public override bool Equals(object obj)
        {
            if (obj is xIntRange)
            {
                //Should perhaps use "is similar".
                return this == (xIntRange)obj;
            }
            return false;
        }
        public static bool operator ==(xIntRange p1, xIntRange p2)
        {
            return p1.Max == p2.Max && p1.Min == p2.Min;
        }
        public static bool operator !=(xIntRange p1, xIntRange p2)
        {
            return p1.Max != p2.Max || p1.Min != p2.Min;
        }
        public override string ToString()
        {
            return string.Format("[{0} {1}]", Min, Max);
        }

        #endregion

        internal static bool Compare(xIntRange[] range1, xIntRange[] range2)
        {
            if (ReferenceEquals(range1, range2)) return true;
            if (range1 == null) return range2 == null;
            if (range2 == null || range1.Length != range2.Length) return false;
            for (int c = 0; c < range1.Length; c++)
                if (range1[c] != range2[c]) return false;
            return true;
        }

        internal static xIntRange[] Create(IntArray ar)
        {
            var ret = new xIntRange[ar.Length / 2];
            for (int c = 0, pos = 0; c < ret.Length; c++)
                ret[c] = new xIntRange(ar[pos++], ar[pos++]);
            return ret;
        }

        public static xIntRange[] Create(int[] ar)
        {
            var ret = new xIntRange[ar.Length / 2];
            for (int c = 0, pos = 0; c < ret.Length; c++)
                ret[c] = new xIntRange(ar[pos++], ar[pos++]);
            return ret;
        }

        public static int[] ToArray(xIntRange[] ar)
        {
            var ret = new int[2 * ar.Length];
            for (int i = 0, c = 0; c < ret.Length; i++)
            {
                var v = ar[i];
                ret[c++] = v.Min;
                ret[c++] = v.Max;
            }
            return ret;
        }

        /// <summary>
        /// Reverses the min/max values in a range
        /// </summary>
        /// <param name="r">Range to reverse</param>
        /// <returns>reversed ramges</returns>
        public static xIntRange Reverse(xIntRange r)
        { return new xIntRange(r.Max, r.Min); }

        /// <summary>
        /// Reverses the min/max values in a set of ranges
        /// </summary>
        /// <param name="ranges">Ranges to reverse</param>
        public static void Reverse(xIntRange[] ranges)
        {
            for (int c = 0; c < ranges.Length; c++)
                ranges[c] = Reverse(ranges[c]);
        }
    }

    /// <summary>
    /// A polygon with four points
    /// </summary>
    public struct xQuadrilateral
    {
        #region Variables and properties

        public readonly xPoint P1, P2, P3, P4;

        /// <summary>
        /// Lowest X value
        /// </summary>
        public double Left
        {
            get { return Math.Min(P1.X, Math.Min(P2.X, Math.Min(P3.X, P4.X))); }
        }

        /// <summary>
        /// Highest X value
        /// </summary>
        public double Right
        {
            get { return Math.Max(P1.X, Math.Max(P2.X, Math.Max(P3.X, P4.X))); }
        }

        /// <summary>
        /// Highest Y value
        /// </summary>
        public double Top
        {
            get { return Math.Max(P1.Y, Math.Max(P2.Y, Math.Max(P3.Y, P4.Y))); }
        }

        /// <summary>
        /// Lowest Y value
        /// </summary>
        public double Bottom
        {
            get { return Math.Min(P1.Y, Math.Min(P2.Y, Math.Min(P3.Y, P4.Y))); }
        }

        public xPoint LowerLeft
        {
            get { return new xPoint(Left, Bottom); }
        }

        public xPoint UpperRight
        {
            get { return new xPoint(Right, Top); }
        }

        /// <summary>
        /// Bounding rectangle of this quadrilateral
        /// </summary>
        public xRect Bounds { get { return new xRect(LowerLeft, UpperRight); } }

        #endregion

        #region Init

        public xQuadrilateral(xPoint p1, xPoint p2, xPoint p3, xPoint p4)
        {
            P1 = p1; P2 = p2; P3 = p3; P4 = p4;
        }

        public xQuadrilateral(PdfRectangle rect)
        {
            P1 = new xPoint(rect.LLx, rect.LLy);
            P2 = new xPoint(P1.X, rect.URy);
            P3 = new xPoint(rect.URx, P2.Y);
            P4 = new xPoint(P3.X, P1.Y);
        }

        public xQuadrilateral(xRect rect)
        {
            P1 = rect.LowerLeft;
            P2 = new xPoint(rect.LowerLeft.X, rect.UpperRight.Y);
            P3 = rect.UpperRight;
            P4 = new xPoint(rect.UpperRight.X, rect.LowerLeft.Y);
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return string.Format("Quadrilateral: {0:0}-{1:0};{2:0}-{3:0};{4:0}-{5:0};{6:0}-{7:0}", P1.X, P1.Y, P2.X, P2.Y, P3.X, P3.Y, P4.X, P4.Y);
        }

        #endregion
    }

    public struct xRect
    {
        #region Variables and properties

        public bool IsEmpty => Width == 0 || Height == 0;

        public double Left
        {
            get { return LowerLeft.X < UpperRight.X ? LowerLeft.X : UpperRight.X; }
        }
        public double Right
        {
            get { return LowerLeft.X > UpperRight.X ? LowerLeft.X : UpperRight.X; }
        }
        public double Top
        {
            get { return LowerLeft.Y > UpperRight.Y ? LowerLeft.Y : UpperRight.Y; }
        }
        public double Bottom
        {
            get { return LowerLeft.Y < UpperRight.Y ? LowerLeft.Y : UpperRight.Y; }
        }

        public readonly xPoint LowerLeft, UpperRight;

        public double Width { get { return UpperRight.X - LowerLeft.X; } }
        public double Height { get { return UpperRight.Y - LowerLeft.Y; } }
        public double X { get { return LowerLeft.X; } }
        public double Y { get { return LowerLeft.Y; } }

        /// <summary>
        /// Rectangle where LowerLeft is garnateeded to be the smallest (or most negative)
        /// </summary>
        public xRect Normalized
        {
            get
            {
                double xmin, ymin, xmax, ymax;
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
                return new xRect(xmin, ymin, xmax, ymax);
            }
        }

        #endregion

        #region Init

        public xRect(PdfRectangle rect)
        {
            LowerLeft = new xPoint(rect.LLx, rect.LLy);
            UpperRight = new xPoint(rect.URx, rect.URy);
        }

        public xRect(xSize size)
            : this(0, 0, size.Width, size.Height)
        {

        }

        public xRect(xPoint location, xSize size)
            : this(location.X, location.Y, location.X + size.Width, location.Y + size.Height)
        {

        }

        public xRect(double llx, double lly, double urx, double ury)
        {
            LowerLeft = new xPoint(llx, lly);
            UpperRight = new xPoint(urx, ury);
        }

        /// <summary>
        /// Creates a xRect
        /// </summary>
        /// <param name="ll">LowerLeft</param>
        /// <param name="ur">UpperRight</param>
        public xRect(xPoint ll, xPoint ur)
        {
            LowerLeft = ll;
            UpperRight = ur;
        }

        #endregion

        #region Utility

        public bool IsOn(xPoint p)
        {
            return LowerLeft.X <= p.X && p.X <= UpperRight.X &&
                   LowerLeft.Y <= p.Y && p.Y <= UpperRight.Y;
        }

        public bool IntersectsWith(xRect r)
        {
            return LowerLeft.X < r.UpperRight.X && UpperRight.X > r.LowerLeft.X &&
                    UpperRight.Y > r.LowerLeft.Y && LowerLeft.Y < r.UpperRight.Y;
        }

        /// <summary>
        /// Creates a rectangle that encompasses the two rectanges
        /// </summary>
        public xRect Enlarge(xRect r)
        {
            r = r.Normalized;
            var t = Normalized;
            return new xRect(
                Math.Min(t.LowerLeft.X, r.LowerLeft.X),
                Math.Min(t.LowerLeft.Y, r.LowerLeft.Y),
                Math.Max(t.UpperRight.X, r.UpperRight.X),
                Math.Max(t.UpperRight.Y, r.UpperRight.Y)
            );
        }

        public override string ToString()
        {
            return string.Format("Rect: {0:0};{1:0};{2:0};{3:0}", X, Y, Width, Height);
        }

        #endregion
    }

    [DebuggerDisplay("Size: {Width},{Height}")]
    public struct xSize
    {
        public readonly double Width;
        public readonly double Height;
        public xSize(double width, double height) { Width = width; Height = height; }
    }

    [DebuggerDisplay("Line: {Start.X},{Start.Y} -> {End.X},{End.Y}")]
    public struct xLine
    {
        public readonly xPoint Start, End;
        public xRect Bounds
        {
            get 
            {
                double llx, lly, urx, ury;
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
                return new xRect(llx, lly, urx, ury);
            }
        }

        /// <summary>
        /// Length of the line
        /// </summary>
        public double Length { get { return Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2)); } }

        /// <summary>
        /// Vector from the Start point to the End point
        /// </summary>
        public xVector Vector
        {
            get { return new xVector(End.X - Start.X, End.Y - Start.Y); }
        }
        /// <summary>
        /// The middle point of the line.
        /// </summary>
        public xPoint MidPoint
        {
            get { return new xPoint((Start.X + End.X) / 2, (Start.Y + End.Y) / 2); }
        }
        public xLine(xPoint start, xPoint end)
        { Start = start; End = end; }
        public xLine(double x1, double y1, double x2, double y2)
        { Start = new xPoint(x1, y1); End = new xPoint(x2, y2); }

        /// <summary>
        /// Calculates a point on the line.
        /// </summary>
        /// <param name="distance_from_start">Distance from the starting point of the line</param>
        /// <returns>Point on the line</returns>
        public xPoint PointOnLine(double distance_from_start)
        {
            var ratio = distance_from_start / Length;

            return new xPoint(ratio * End.X + (1 - ratio) * Start.X, ratio * End.Y + (1 - ratio) * Start.Y);
        }

        /// <summary>
        /// Creates a line that goes pendicular through the start point of this line.
        /// </summary>
        /// <param name="distance">Length of the new line</param>
        /// <param name="center">Whenever the line is to be centered on the point</param>
        /// <returns>Line pendicular to this line</returns>
        public xLine PendicularStart(double distance, bool center)
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
        public xLine PendicularEnd(double distance, bool center)
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
        private xLine PendicularCenter(double x1, double y1, double x2, double y2, double distance)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            var dist = Math.Sqrt(dx*dx + dy*dy);
            dx /= dist;
            dy /= dist;
            distance /= 2;
            return new xLine(x1 + distance * dy, y1 - distance * dx,x1 - distance * dy, y1 + distance * dx);
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
        private xLine Pendicular(double x1, double y1, double x2, double y2, double distance)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            dx /= dist;
            dy /= dist;
            return new xLine(x1, y1, x1 - (distance) * dy, y1 + (distance) * dx);
        }
    }

    public struct xDashStyle
    {
        public readonly double Phase;
        public readonly double[] Dashes;
        public xDashStyle(double phase, params double[] dashes)
        { Phase = phase; Dashes = dashes; }
        public bool Equals(xDashStyle dash)
        {
            return Phase == dash.Phase && Util.ArrayHelper.ArraysEqual<double>(Dashes, dash.Dashes);
        }
        public void Set(IDraw draw, cRenderState state)
        {
            if (state.dash_style == null || !Equals(state.dash_style.Value))
            {
                draw.SetStrokeDashAr(this);
                state.dash_style = this;
            }
        }

        #region Overrides recommended for value types

        /// <summary>
        /// The GetHashCode formula is just a quick hax, if it's actually needed take a much closer look
        /// </summary>
        public override int GetHashCode() { return (int)Phase ^ (int)Dashes.Length; }
        public override bool Equals(object obj)
        {
            if (obj is xDashStyle)
            {
                var x = (xDashStyle)obj;
                return Phase == x.Phase && Util.ArrayHelper.ArraysEqual<double>(Dashes, x.Dashes);
            }
            return false;
        }
        public static bool operator ==(xDashStyle p1, xDashStyle p2)
        {
            return p1.Phase == p2.Phase && Util.ArrayHelper.ArraysEqual<double>(p1.Dashes, p2.Dashes);
        }
        public static bool operator !=(xDashStyle p1, xDashStyle p2)
        {
            return p1.Phase != p2.Phase && !Util.ArrayHelper.ArraysEqual<double>(p1.Dashes, p2.Dashes);
        }
        public override string ToString()
        {
            return string.Format("Dashes: {0}", Phase);
        }

        #endregion

        public static bool Equals(xDashStyle? dash1, xDashStyle? dash2)
        {
            // If both are null, or both are same instance, return true.
            if (ReferenceEquals(dash1, dash2))
                return true;

            // If one is null, but not both, return false.
            if (dash1 == null || dash2 == null)
                return false;

            return dash1.Value == dash2.Value;
        }
    }

    public static class xDashStyles
    {
        public static xDashStyle Solid => new xDashStyle();
    }
}
