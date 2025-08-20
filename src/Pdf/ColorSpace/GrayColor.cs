using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Pdf.ColorSpace
{
    public sealed class GrayColor : PdfColor
    {
        #region Variables and properties

        public readonly double Gray;

        public sealed override double Red { get { return Gray; } }
        public sealed override double Green { get { return Gray; } }
        public sealed override double Blue { get { return Gray; } }

        #endregion

        #region Init

        public GrayColor(double gray)
        { Gray = gray; }

        #endregion

        public override DblColor ToDblColor()
        {
            return new DblColor(Red, Green, Blue);
        }

        public override double[] ToArray()
        {
            return new double[] { Gray };
        }

        public override byte[] ToARGB()
        {
            var g = (byte) (Gray * Byte.MaxValue);
            return new byte[] { (byte)0xFF, g, g, g };
        }
    }
}
