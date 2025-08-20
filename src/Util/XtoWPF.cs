using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Util
{
    /// <summary>
    /// For converting beteen x structs and WPF equivalents
    /// </summary>
    public static class XtoWPF
    {
        public static Matrix ToMatrix(xMatrix m)
        { return new Matrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY); }

        public static xMatrix ToMatrix(Matrix m)
        { return new xMatrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY); }

        public static Point ToPoint(xPoint p)
        { return new Point(p.X, p.Y); }

        public static xPoint ToPoint(Point p)
        { return new xPoint(p.X, p.Y); }

        public static Rect ToRect(PdfRectangle rect)
        {
            double llx = rect.LLx;
            double urx = rect.URx;
            if (urx < llx) { double temp = urx; urx = llx; llx = temp; }
            double lly = rect.LLy;
            double ury = rect.URy;
            if (ury < lly) { double temp = ury; ury = lly; lly = temp; }
            return new Rect(llx, lly, urx - llx, ury - lly); 
        }
        public static Rect ToRect(xRect rect)
        {
            double llx = rect.LowerLeft.X;
            double urx = rect.UpperRight.X;
            if (urx < llx) { double temp = urx; urx = llx; llx = temp; }
            double lly = rect.LowerLeft.Y;
            double ury = rect.UpperRight.Y;
            if (ury < lly) { double temp = ury; ury = lly; lly = temp; }
            return new Rect(llx, lly, urx - llx, ury - lly);
        }

        public static PdfColor ToColor(Color c)
        {
            return new DblColor(c.R / 255d, c.G / 255d, c.B / 255d);
        }

        public static Color ToColor(PdfColor c)
        {
            return Color.FromRgb((byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255));
        }
    }
}
