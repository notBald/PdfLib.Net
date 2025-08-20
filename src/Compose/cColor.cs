using PdfLib.Pdf.ColorSpace;
using System.Text;
using System;

namespace PdfLib.Compose
{
    /// <summary>
    /// Compose Color
    /// 
    /// Convenience class that encapsulates every color variation, including color space, that
    /// PDF supports. 
    /// </summary>
    public sealed class cColor : cBrush
    {
        #region Static

        private static cColor _red, _green, _blue, _black, _gray, _white, _yellow;

        public static cColor RED { get { if (_red == null) _red = new cColor(1d, 0, 0); return _red; } }
        public static cColor GREEN { get { if (_green == null) _green = new cColor(0, .5d, 0); return _green; } }
        public static cColor BLUE { get { if (_blue == null) _blue = new cColor(0, 0, 1d); return _blue; } }
        public static cColor BLACK { get { if (_black == null) _black = new cColor(0); return _black; } }
        public static cColor GRAY { get { if (_gray == null) _gray = new cColor(.5); return _gray; } }
        public static cColor WHITE { get { if (_white == null) _white = new cColor(1d); return _white; } }
        public static cColor YELLOW { get { if (_yellow == null) _yellow = new cColor(1d, 1, 0); return _yellow; } }

        /// <summary>
        /// Creates a cColor from an array of doubles.
        /// </summary>
        /// <param name="col">Colorspace is selected by the number of componets (Gray, RGB and CMYK)</param>
        /// <returns></returns>
        public static cColor CreateFromArray(double[] col)
        {
            if (col == null || col.Length == 0) return null;
            if (col.Length == 1)
                return new Compose.cColor(col, DeviceGray.Instance);
            if (col.Length == 3)
                return new Compose.cColor(col, DeviceRGB.Instance);
            if (col.Length == 4)
                return new Compose.cColor(col, DeviceCMYK.Instance);
            return null;
        }

        #endregion

        #region Init

        public cColor(double[] color, IColorSpace cs)
            : base(cs)
        {
            if (cs.NComponents != color.Length)
                throw new PdfInternalException("Incorect number of components");
            _color = color;
            _pattern = null;
        }
        public cColor(PdfColor color, IColorSpace cs)
            : this(color.ToArray(), cs)
        { }
        public cColor(PdfColor color)
            : this(color.ToDblColor().ToArray(), DeviceRGB.Instance)
        { }
        public cColor(double cyan, double magenta, double yellow, double black)
            : this(new double[] { cyan, magenta, yellow, black }, DeviceCMYK.Instance)
        { }
        public cColor(double red, double green, double blue)
            : this(new double[] { red, green, blue }, DeviceRGB.Instance)
        { }
        public cColor(int red, int green, int blue)
            : this(new double[] { red / 255d, green / 255d, blue / 255d }, DeviceRGB.Instance)
        { }
        public cColor(double gray)
            : this(new double[] { gray }, DeviceGray.Instance)
        { }
        public cColor(int rgb)
            : this(IntColor.FromInt(rgb).ToArray(), DeviceRGB.Instance)
        { }
        public cColor(string color)
            : base(DeviceRGB.Instance)
        {
            if (color == null || color.Length == 0 || color[0] != '#')
                throw new NotSupportedException("Only hex colors are supported");

            if (color.Length == 4)
            {
                //Shorthand form
                var sb = new StringBuilder(6);
                for (int c = 1; c < color.Length; c++)
                {
                    sb.Append(color[c]);
                    sb.Append(color[c]);
                }
                color = sb.ToString();
            }
            else color = color.Substring(1);

            int value = Convert.ToInt32(color, 16);
            _color = IntColor.FromInt(value).ToArray();
            _pattern = null;
        }

        #endregion

        public double[] ToArray() { return _color; }

        public DblColor ToRGBColor() { return MyColorSpace.Converter.MakeColor(_color); }

        /// <summary>
        /// Conver this color to another color space. Note,
        /// won't work with the pattern color space.
        /// </summary>
        /// <param name="cs">The color space to convert this color into</param>
        /// <returns>The same color or a new color with the given color space</returns>
        public cColor ConvertTo(IColorSpace cs)
        {
            if (MyColorSpace.Equals(cs))
                return this;
            if (cs == null) throw new ArgumentNullException();
            var dbl = MyColorSpace.Converter.MakeColor(_color);
            var col = cs.Converter.MakeColor(dbl);
            return new cColor(col, cs);
        }
    }
}
