using System;

namespace PdfLib.Pdf.ColorSpace
{
    public abstract class PdfColor
    {
        static PdfColor _black;
        public static PdfColor BLACK { get { if (_black == null) _black = new DblColor(0,0,0); return _black; } }

        public abstract double Red { get; }
        public abstract double Green { get; }
        public abstract double Blue { get; }

        public abstract byte[] ToARGB();
        public abstract DblColor ToDblColor();
        public abstract double[] ToArray();
        public virtual bool IsGray
        {
            get
            {
                var col = ToDblColor();
                return col.R == col.B && col.R == col.G;
            }
        }

        /// <summary>
        /// If the colors are close enough to be equal, uses minimum precision
        /// </summary>
        public static bool AreClose(PdfColor c1, PdfColor c2)
        {
            const double MIN = 1.1e-07d;

            var red = Math.Abs(c1.Red - c2.Red);
            var green = Math.Abs(c1.Green - c2.Green);
            var blue = Math.Abs(c1.Blue - c2.Blue);
            return red < MIN && green < MIN && blue < MIN;
        }

        /// <summary>
        /// If the colors are similar enough to be considered equal, uses color distance
        /// </summary>
        public static bool AreSimilar(PdfColor c1, PdfColor c2)
        {
            var y1 = YUVColor.Create(c1);
            var y2 = YUVColor.Create(c2);

            var dist = Math.Sqrt(Math.Pow(y1.U - y2.U, 2) + Math.Pow(y1.V - y2.V, 2));

            return dist < 0.02;
        }

        public static bool Equals(PdfColor col1, PdfColor col2)
        {
            if (col1 == null)
                return col2 == null;
            return col2 != null &&
                col1.Red == col2.Red &&
                col1.Green == col2.Green &&
                col1.Blue == col2.Blue;
        }
    }
}
