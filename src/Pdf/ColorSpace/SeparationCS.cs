using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Function;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// This color space allows one to use a "tint" function to correct
    /// colors when targeting a different type of device.
    /// 
    /// That will say when displaying on screen one use tint functions
    /// for CMYK colors, and not for RGB colors. Opposite when printing.
    /// 
    /// I.e. if you have the color "red". Then that color can be sent
    /// directly to the screen as screens support red.
    /// 
    /// then with cyan one use the "tint" funtcion to transform the colorant
    /// into the alternate color space. Which is then used to convert to
    /// RGB colors.
    /// </summary>
    [PdfVersion("1.2")]
    public sealed class SeparationCS : ItemArray, IColorSpace
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.ColorSpace; } }
        public PdfCSType CSType { get { return PdfCSType.Special; } }

        public PdfColor DefaultColor 
        { 
            get 
            {
                return Converter.MakeColor(new double[] { 1 });
            } 
        }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return new double[] { 1 }; } }

        /// <summary>
        /// Number of components in this color space's raw data,
        /// not the underlying base colorspace (like in BAUpload)
        /// </summary>
        public int NComponents { get { return 1; } }

        /// <summary>
        /// Standar decode values.
        /// </summary>
        public double[] DefaultDecode { get { return new double[] { 0, 1 }; }  }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public ColorConverter Converter 
        { 
            get 
            {
                var name = Name;

                //Monitors support RGB. 
                if ("Red".Equals(name))
                    return new ColorantConverter(RGBColorants.Red);
                if ("Green".Equals(name))
                    return new ColorantConverter(RGBColorants.Green);
                if ("Blue".Equals(name))
                    return new ColorantConverter(RGBColorants.Blue);

                //All simply means that the tint transform is to be run regardless
                //of the output device. 
                if ("All".Equals(name))
                    return new TintConverter(TintTransform, AlternateSpace);
                if ("None".Equals(name))
                    return new NoneConverter();
                return new TintConverter(TintTransform, AlternateSpace);  
            } 
        }

        /// <summary>
        /// For use on non-substractive output devices
        /// </summary>
        public IColorSpace AlternateSpace { get { return (IColorSpace)_items.GetPdfTypeEx(2, PdfType.ColorSpace); } }

        /// <summary>
        /// Names All and None are special, others can be ignored.
        /// 
        /// However, names "Green", "Red" and "Blue" can be drawn
        /// directly to the screen. No need to use the tint transform
        /// with those names.
        /// </summary>
        public string Name { get { return _items.GetNameEx(1); } }

        /// <summary>
        /// Tint function to use when displaying CMYK colors or printing RGB colors
        /// </summary>
        public PdfFunction TintTransform { get { return (PdfFunction)_items.GetPdfTypeEx(3, PdfType.Function); } }

        #endregion

        #region Init

        internal SeparationCS(PdfArray items)
            : base(items)
        {
            if (items.Length != 4)
                throw new PdfCastException(ErrSource.ColorSpace, Type, ErrCode.CorruptToken);
            Debug.Assert("Separation".Equals(items[0].GetString()));
        }

        #endregion

        #region Required overrides

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            return Equivalent(cs);
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is SeparationCS)
            {
                var cs = (SeparationCS) obj;
                return Name == cs.Name &&
                    AlternateSpace.Equals(cs.AlternateSpace) &&
                    TintTransform.Equivalent(cs.TintTransform);
            }
            return false;
        }

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new SeparationCS(array);
        }

        #endregion

        /// <summary>
        /// Applies a colorant after running it through a supplied tint function
        /// </summary>
        internal sealed class TintConverter : ColorConverter
        {
            readonly FCalcualte _func;
            readonly ColorConverter _cc;
            readonly double[] _col;

            public TintConverter(PdfFunction func, IColorSpace alt)
            { _func = func.Init(); _cc = alt.Converter; _col = new double[alt.NComponents]; }

            public override DblColor MakeColor(double[] comps)
            {
                _func.Calculate(comps, _col);
                return _cc.MakeColor(_col);
            }

            public override double[] MakeColor(PdfColor col)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Applies a known colerant directly
        /// </summary>
        class ColorantConverter : ColorConverter
        {
            readonly RGBColorants _color;
            public ColorantConverter(RGBColorants col) 
            { 
                _color = col;
            }
            public override DblColor MakeColor(double[] comps)
            {
                var n = new double[DeviceRGB.Instance.NComponents];
                n[(int) _color] = comps[0];
                return DeviceRGB.RGBConverter.Instance.MakeColor(n);
            }
            public override double[] MakeColor(PdfColor col)
            {
                var ra = new double[1];
                ra[0] = DeviceRGB.RGBConverter.Instance.MakeColor(col)[(int)_color];
                return ra;
            }
        }

        /// <summary>
        /// I honestly don't see the point of this. Why bother
        /// drawing nothing?
        /// </summary>
        /// <remarks>
        /// The special colorant name None shall not produce any visible output. 
        /// 
        /// Painting operations in a Separation space with this colorant name 
        /// shall have no effect on the current page. 
        /// </remarks>
        internal class NoneConverter : ColorConverter
        {
            public override DblColor MakeColor(double[] comps)
            {
                return new ADblColor(0, 0, 0, 0);
            }

            public override double[] MakeColor(PdfColor col)
            {
                throw new NotImplementedException();
            }
        }
    }
}
