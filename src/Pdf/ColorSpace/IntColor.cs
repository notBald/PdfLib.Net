using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// Intended as a container for colors that are 
    /// "ready to be displayed". It's simply an integer
    /// value that can be used with WPF's Color.From.
    /// </summary>
    /// <remarks>This class is imutable</remarks>
    public sealed class IntColor : PdfColor
    {
        uint _color;

        /// <summary>
        /// The integer color
        /// </summary>
        public uint Color { get { return _color; } }

        /// <summary>
        /// Note that PDF files do not support alpha channels, so if this is set it will
        /// be ignored. However, it can still be usefull for non-pdf uses.
        /// </summary>
        public double Alpha { get { return (_color >> 24) / 255d; } }

        public override double Red { get { return ((_color >> 16) & 0xFF) / 255d; } }
        public override double Green { get { return ((_color >> 8) & 0xFF) / 255d; } }
        public override double Blue { get { return (_color & 0xFF) / 255d; } }

        private IntColor() { }
        public static IntColor FromInt(int argb) { var c = new IntColor(); c._color = (uint) argb; return c; }
        public static IntColor FromUInt(uint argb) { var c = new IntColor(); c._color = argb; return c; }

        /// <summary>
        /// Creates a gray color
        /// </summary>
        public IntColor(byte gray)
        {
            _color = 0xFF000000;
            _color |= (uint) ((gray << 16) | (gray << 8) | gray);
        }

        /// <summary>
        /// Creates a rgb color
        /// </summary>
        public IntColor(byte r, byte g, byte b)
        {
            _color = 0xFF000000;
            _color |= (uint)((r << 16) | (g << 8) | b);
        }

        /// <summary>
        /// Creates a argb color
        /// </summary>
        public IntColor(byte r, byte g, byte b, byte a)
        {
            _color = (uint)((a << 24) | (r << 16) | (g << 8) | b);
        }

        public override DblColor ToDblColor()
        {
            return new DblColor(Red, Green, Blue);
        }

        public override double[] ToArray()
        {
            return new double[] { Red, Green, Blue };
        }

        public override byte[] ToARGB()
        {
            byte[] ret = BitConverter.GetBytes(_color);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(ret);
            return ret;
        }
    }
}
