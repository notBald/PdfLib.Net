using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

/// <summary>
/// For color reduction, if there's a need, consider: https://www.ece.mcmaster.ca/~xwu/cq.c or http://people.eecs.berkeley.edu/~dcoetzee/downloads/scolorq/
/// </summary>
namespace PdfLib.Pdf.ColorSpace
{
    [PdfVersion("1.1")]
    public sealed class IndexedCS : ItemArray, IColorSpace
    {
        #region Properties

        internal override PdfType Type { get { return PdfType.ColorSpace; } }
        public PdfCSType CSType { get { return PdfCSType.Special; } }

        public PdfColor DefaultColor { get { return Converter.MakeColor(new double[] { 0 }); } }

        public double[] DefaultColorAr { get { return new double[] { 0 }; } }

        public bool HasColor
        {
            get
            {
                var b = Base;
                if (b.NComponents == 1)
                    return false;
                if (b is DeviceRGB)
                {
                    var l = Lookup;
                    var count = Hival + 1;
                    if (l.Length == count * 3)
                    {
                        for(int c=0; c < l.Length; )
                        {
                            var r = l[c++];
                            if (r != l[c++] || r != l[c++])
                                return true;
                        }

                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Number of components in this color space's raw data,
        /// not the underlying base colorspace (like in BAUpload)
        /// </summary>
        public int NComponents { get { return 1; } }

        /// <summary>
        /// Standar decode values.
        /// </summary>
        public double[] DefaultDecode
        {
            get { /* return new double[] { 0, Math.Pow(2, bpc) - 1 }; */ throw new NotSupportedException("Need to know bits per component");  }
        }

        public double[] GetDefaultDecode(int bpc)
        {
            return new double[] { 0, Math.Pow(2, bpc) - 1 };
        }

        /// <summary>
        /// Underlying color space
        /// </summary>
        public IColorSpace Base
        {
            get 
            {
                var ret = (IColorSpace)_items.GetPdfTypeEx(1, PdfType.ColorSpace);
                if (ret is IndexedCS || ret is PatternCS)
                    throw new PdfReadException(PdfType.ColorSpace, ErrCode.OutOfRange);
                return ret;
            }
        }

        /// <summary>
        /// Hival is the maximum index value in the CLUT
        /// </summary>
        public int Hival
        {
            get
            {
                var ret = _items[2].GetInteger();
                if (ret < 0 || ret > 255)
                    throw new PdfReadException(ErrSource.Numeric, PdfType.Integer, ErrCode.OutOfRange);
                return ret;
            }
        }

        /// <summary>
        /// Raw color lookup table
        /// </summary>
        public byte[] Lookup
        {
            get 
            {
                var obj = _items[3].Deref();
                if (obj is PdfStream)
                    return ((PdfStream)obj).DecodedStream;
                if (obj is PdfString)
                    return ((PdfString)obj).ByteString;
                throw new PdfReadException(PdfType.String, ErrCode.UnexpectedToken);
            }
        }

        /// <summary>
        /// The CLUT as a list of PdfColors
        /// </summary>
        public DblColor[] Palette
        {
            get
            {
                var b = Base;
                var count = Hival + 1;
                var raw = Lookup;
                var palette = new DblColor[count];
                var nc = b.NComponents;

                if (raw.Length < nc * count)
                    throw new PdfReadException(ErrSource.ColorSpace, PdfType.ColorSpace, ErrCode.UnexpectedEOD);

                //Reads out each component. Note that all
                //components are "byte sized", even if the
                //color space expects something else.
                var comp = new double[nc];
                int raw_pos = 0;
                var conv = b.Converter;
                for (int c = 0; c < count; c++)
                {
                    for (int i = 0; i < nc; i++)
                        comp[i] = raw[raw_pos++] / (double) byte.MaxValue;

                    palette[c] = conv.MakeColor(comp);
                }


                return palette;
            }
        }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public ColorConverter Converter { get { return new IndexConverter(Palette, Hival); } }

        #endregion

        #region Init

        internal IndexedCS(PdfArray items)
            : base(items)
        {
            if (items.Length != 4)
                throw new PdfCastException(ErrSource.ColorSpace, Type, ErrCode.CorruptToken);
            Debug.Assert("Indexed".Equals(items[0].GetString()));
        }

        /// <summary>
        /// Constructs a Indexed RGB colorspace 
        /// </summary>
        /// <param name="colors">An array of bytes divisible by 3</param>
        public IndexedCS(byte[] colors)
            : base(new TemporaryArray(4))
        {
            if (colors == null || colors.Length == 0)
                throw new ArgumentNullException("Must have a palette");
            if (colors.Length % 3 != 0)
                throw new ArgumentException("Array must be divisible by three");
            _items[0] = new PdfName("Indexed");
            _items[1] = DeviceRGB.Instance;
            _items[2] = new PdfInt((colors.Length / 3) - 1);
            _items[3] = new PdfString(colors);
        }

        /// <summary>
        /// Constructs a Indexed colorspace 
        /// </summary>
        /// <param name="colors">An array of bytes divisible by 3</param>
        public IndexedCS(byte[] colors, IColorSpace cs)
            : base(new TemporaryArray(4))
        {
            if (colors == null || colors.Length == 0)
                throw new ArgumentNullException("Must have a palette");
            if (colors.Length % cs.NComponents != 0)
                throw new ArgumentException("Array must be divisible by " + cs.NComponents);
            if (cs == null || cs is IndexedCS)
                throw new ArgumentException("Invalid color space");
            _items[0] = new PdfName("Indexed");
            _items[1] = (PdfItem) cs;
            _items[2] = new PdfInt((colors.Length / cs.NComponents) - 1);
            _items[3] = new PdfString(colors);
        }

        #endregion

        #region IColorSpace

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
            if (obj is IndexedCS)
            {
                var cs = (IndexedCS)obj;
                return Base.Equals(cs.Base) && Hival == cs.Hival &&
                    //Room for performance improvment here: 
                    Util.ArrayHelper.ArraysEqual<byte>(Lookup, cs.Lookup);
            }
            return false;
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new IndexedCS(array);
        }

        #endregion

        sealed class IndexConverter : ColorConverter
        {
            //The color lookup table
            readonly DblColor[] _clut;
            readonly int _hival;

            public IndexConverter(DblColor[] CLUT, int hival)
            { _clut = CLUT; _hival = hival; }

            public override PdfColor MakeColor(byte[] raw)
            {
                Debug.Assert(raw.Length == 1);
                var i = raw[0];
                if (i < 0) return _clut[0];
                if (i > _hival) return _clut[_hival];
                return _clut[raw[0]];
            }

            public override DblColor MakeColor(double[] comps)
            {
                var i = (int) comps[0]; //<- No rounding seems to be the correct behavior
                if (i < 0) return _clut[0];
                if (i > _hival) return _clut[_hival];
                return _clut[i];
            }

            public override double[] MakeColor(PdfColor col)
            {
                throw new NotImplementedException();
            }
        }
    }
}
