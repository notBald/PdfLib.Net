using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// Color with red, green, blue components stored as double values
    /// </summary>
    public class DblColor : PdfColor
    {
        #region Variables and properties

        public readonly double R;
        public readonly double G;
        public readonly double B;

        public sealed override double Red { get { return R; } }
        public sealed override double Green { get { return G; } }
        public sealed override double Blue { get { return B; } }
        public double[] RGB { get { return new double[] { R, G, B }; } }

        #endregion

        #region Init

        public DblColor(double r, double g, double b)
        { R = r; G = g; B = b; }

        #endregion

        public override DblColor ToDblColor()
        {
            return this;
        }

        public override byte[] ToARGB()
        {
            return new byte[] { (byte) 0xFF, (byte)(R * byte.MaxValue), (byte)(G * byte.MaxValue), (byte)(B * byte.MaxValue) };
        }

        public override double[] ToArray() { return new double[] { R, G, B }; }
    }

    /// <summary>
    /// Color with alpha, red, green and blue components, stored as double values
    /// </summary>
    public sealed class ADblColor : DblColor
    {
        #region Variables and properties

        public readonly double A;

        #endregion

        #region Init

        public ADblColor(double r, double g, double b, double a)
            : base(r, g, b)
        { A = a; }

        #endregion

        public override byte[] ToARGB()
        {
            return new byte[] { (byte)(A * byte.MaxValue), (byte)(R * byte.MaxValue), (byte)(G * byte.MaxValue), (byte)(B * byte.MaxValue) };
        }
    }

    /// <summary>
    /// Contains YUV color
    /// </summary>
    public sealed class YUVColor : PdfColor
    {
        #region Variables and properties

        public readonly double Y, U, V;

        public override double Red { get { return ToDblColor().R; } }
        public override double Green { get { return ToDblColor().G; } }
        public override double Blue { get { return ToDblColor().B; } }

        #endregion

        #region Init

        public YUVColor(DblColor c)
        {
            var v = RGBtoYUV.Transform(new x3Vector(c.R, c.G, c.B));
            Y = v.X; U = v.Y; V = v.Z;
        }

        public YUVColor(double Y, double U, double V)
        { this.Y = Y; this.U = U; this.V = V; }

        #endregion

        #region Required overrides

        public override DblColor ToDblColor()
        {
            var v = YUVtoRGB.Transform(new x3Vector(Y, U, V));
            return new DblColor(v.X, v.Y, v.Z);
        }

        public override double[] ToArray()
        {
            return new double[] { Y, U, V };
        }
        
        public override byte[] ToARGB()
        {
            return ToDblColor().ToARGB();
        }

        #endregion

        #region Utility

        public double Distance(YUVColor c)
        {
            return Math.Sqrt(Math.Pow(U - c.U, 2) + Math.Pow(V - c.V, 2));
        }

        public static YUVColor Create(double red, double green, double blue)
        { return new YUVColor(new DblColor(red, green, blue)); }
        public static YUVColor Create(PdfColor c)
        {
            if (c is YUVColor) return (YUVColor)c;
            return new YUVColor(c.ToDblColor());
        }

        private static x3x3Matrix YUVtoRGB = new x3x3Matrix(
            1,  0,        1.13983,
            1, -0.39465, -0.58060,
            1,  2.03211,  0
        );

        private static x3x3Matrix RGBtoYUV = new x3x3Matrix(
            0.299,    0.587,    0.114,
           -0.14713, -0.28886,  0.436,
            0.615,   -0.51499, -0.10001
        );

        #endregion
    }

    public sealed class CMYKColor : PdfColor
    {
        #region Variables and properties

        public readonly double[] CMYK;

        public override double Red { get { return ToDblColor().R; } }
        public override double Green { get { return ToDblColor().G; } }
        public override double Blue { get { return ToDblColor().B; } }
        public double Cyan { get { return CMYK[0]; } }
        public double Magenta { get { return CMYK[1]; } }
        public double Yellow { get { return CMYK[2]; } }
        public double Black { get { return CMYK[3]; } }

        #endregion

        #region Init

        public CMYKColor(DblColor c)
        {
            CMYK = DeviceCMYK.Instance.Converter.MakeColor(c);
        }

        public CMYKColor(double C, double M, double Y, double K)
        { CMYK = new double[] { C, M, Y, K }; }

        #endregion

        #region Required overrides

        public override DblColor ToDblColor()
        {
            return DeviceCMYK.Instance.Converter.MakeColor(CMYK);
        }

        public override double[] ToArray()
        {
            return CMYK;
        }

        public override byte[] ToARGB()
        {
            return ToDblColor().ToARGB();
        }

        #endregion
    }
}
