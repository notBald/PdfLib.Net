//For NChannel I only have two test file (Rapport_TA_ENG_web.pdf and issue 113b). 
//They work, but if this feature fails to work right on other files a
//quick fix is to comment out this define (Which makes the code fall back
//to the DeviceN color space)
#define NCHANNEL_SUPPORT
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
    /// DeviceN is pretty much the same as a Seperation color space with
    /// multiple colorants.
    /// 
    /// NChannel is basically that instead of applying a tint function over
    /// all colorants, one apply it on a colorant to colorant basis. I.e.
    /// each colorant has its own seperation color space which I assume is
    /// blended together with simple additions. 
    /// </summary>
    [PdfVersion("1.2")]
    public abstract class DeviceNCS : ItemArray, IColorSpace
    {
        #region Variables and properties

        internal sealed override PdfType Type { get { return PdfType.ColorSpace; } }

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public PdfCSType CSType { get { return PdfCSType.Special; } }

        public PdfColor DefaultColor
        {
            get
            {
                var ar = new Double[NComponents];
                for (int c = 0; c < ar.Length; c++)
                    ar[c] = 1;
                return Converter.MakeColor(ar);
            }
        }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr 
        { 
            get 
            {
                var ar = new Double[NComponents];
                for (int c = 0; c < ar.Length; c++)
                    ar[c] = 1;
                return ar;
            } 
        }

        /// <summary>
        /// Number of components in this color space's raw data,
        /// not the underlying base colorspace (like in BAUpload)
        /// </summary>
        public int NComponents { get { return Names.Length; } }

        /// <summary>
        /// Standar decode values.
        /// </summary>
        public double[] DefaultDecode 
        { 
            get 
            {
                var ar = new Double[NComponents*2];
                for (int c = 1; c < ar.Length; c += 2)
                    ar[c] = 1;
                return ar;
            } 
        }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public abstract ColorConverter Converter { get; }

        /// <summary>
        /// For use on non-substractive output devices
        /// </summary>
        public IColorSpace AlternateSpace { get { return (IColorSpace)_items.GetPdfTypeEx(2, PdfType.ColorSpace); } }

        /// <summary>
        /// Colerant names
        /// </summary>
        public string[] Names { get { return PdfArray.ToNameArray(_items.GetArrayEx(1)); } }

        public PdfFunction TintTransform { get { return (PdfFunction)_items.GetPdfTypeEx(3, PdfType.Function); } }

        #endregion

        #region Init

        protected DeviceNCS(PdfArray items)
            : base(items)
        {
            if (items.Length != 4 && items.Length != 5) 
                throw new PdfCastException(ErrSource.ColorSpace, Type, ErrCode.CorruptToken);
            Debug.Assert("DeviceN".Equals(items[0].GetString()));
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

        #endregion

        internal static DeviceNCS Create(PdfArray items)
        {
            if (items.Length == 5)
            {
                //This can be a DeviceN or NChannel color space depending
                //one the value of the subtype in the attributes dictionary
                var temp = new DeviceN4(items);

                var attribs = temp.Attributes;

                var sub = attribs.SubType;
                if ("DeviceN".Equals(sub)) return temp;

                //If it's not DeviceN, it must be NChannel
                if (!"NChannel".Equals(sub))
                    throw new PdfCastException(ErrSource.ColorSpace, PdfType.DNCSAttrib, ErrCode.WrongType);

#if NCHANNEL_SUPPORT

                //Comment out if the NChannel implementation is problematic
                return new NChannel(items);

#endif
            }

            return new DeviceN4(items);
        }

        internal class ProcessDictionary : Elements
        {
            #region Properties

            internal override PdfType Type { get { return PdfType.ProcessDictionary; } }

            /// <summary>
            /// ColorSpace for the process colors
            /// </summary>
            public IColorSpace ColorSpace { get { return (IColorSpace) _elems.GetPdfTypeEx("ColorSpace", PdfType.ColorSpace); } }

            /// <summary>
            /// Colerant names
            /// </summary>
            public string[] Components { get { return PdfArray.ToNameArray(_elems.GetArrayEx("Components")); } }

            #endregion

            #region Init

            internal ProcessDictionary(PdfDictionary dict)
                : base(dict) { }

            #endregion

            #region Required overrides

            protected override Elements MakeCopy(PdfDictionary elems)
            {
                return new ProcessDictionary(elems);
            }

            #endregion
        }

        internal class ColorantsDictionary : Elements
        {
            #region Properties

            internal override PdfType Type { get { return PdfType.ColorantsDict; } }

            public SeparationCS this[string colorant_id]
            {
                get
                {
                    var ret = _elems.GetPdfType(colorant_id, PdfType.ColorSpace);
                    if (!(ret is SeparationCS) && ret != null) throw new PdfReadException(PdfType.ColorSpace, ErrCode.Wrong);
                    return (SeparationCS)ret;
                }
            }

            #endregion

            #region Init

            internal ColorantsDictionary(PdfDictionary dict)
                : base(dict) { }

            #endregion

            #region Required overrides

            protected override Elements MakeCopy(PdfDictionary elems)
            {
                return new ColorantsDictionary(elems);
            }

            #endregion
        }

        internal class AttributesDictionary : Elements
        {
            internal override PdfType Type
            {
                get { return PdfType.DNCSAttrib; }
            }

            public string SubType
            {
                get
                {
                    var st = _elems.GetName("Subtype");
                    if (st == null) return "DeviceN";
                    return st;
                }
            }

            public ColorantsDictionary Colorants
            {
                get
                {
                    return (ColorantsDictionary)_elems.GetPdfType("Colorants", PdfType.ColorantsDict);
                }
            }

            internal ProcessDictionary Process
            {
                get
                {
                    return (ProcessDictionary)_elems.GetPdfType("Process", PdfType.ProcessDictionary);
                }
            }

            public AttributesDictionary(PdfDictionary dict)
                : base(dict)
            { }

            protected override Elements MakeCopy(PdfDictionary elems)
            {
                return new AttributesDictionary(elems);
            }
        }
    }

    /// <summary>
    /// Implements DeviceN
    /// 
    /// There's no real need for an abstract DeviceNCS class, since NChannel
    /// ended up inheriting this class (for the fallback mechanism)
    /// </summary>
    /// <remarks>
    /// There's an optional atttribute dictionary that is usefull for a varity
    /// of color transformations. PdfLib ignores this.
    /// </remarks>
    internal class DeviceN4 : DeviceNCS
    {
        #region Properties

        /// <summary>
        /// DeviceN ColorSpace Attributes
        /// </summary>
        public AttributesDictionary Attributes
        { get { return _items.Length == 5 ? (AttributesDictionary)_items.GetPdfTypeEx(4, PdfType.DNCSAttrib) : null; } }

        public override ColorConverter Converter
        {
            get 
            {
                var colors = MakeColorantList(Names);

                //When there's non RGB colors in the list we
                //have to use the tint converter
                if (colors == null)
                    return new SeparationCS.TintConverter(TintTransform, AlternateSpace);

                //A DeviceN colour space whose component colorant names are all None 
                //shall always discard its output
                if (colors.Length == 0)
                    return new SeparationCS.NoneConverter();

                //Paints colorants directly to the screen. Tint function is not used and
                //all "none" channels are to be ignored/discarted.
                return new ColorantConverter(colors);
            }
        }

        #endregion

        #region Init

        internal DeviceN4(PdfArray ar)
            : base(ar)
        { }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new DeviceN4(array);
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// Makes a list of colerant names
        /// </summary>
        /// <param name="names">Colerant names</param>
        /// <returns>
        /// A return value of null denotes that there are
        /// non-RGB colorants in the list
        /// 
        /// A return value of a empty array denotes that
        /// there was no drawable colerants in the list
        /// </returns>
        /// <remarks>
        /// Unlike seperation the "all" colorant name 
        /// is not to be used.
        /// </remarks>
        static RGBColorants[] MakeColorantList(string[] names)
        {
            int nColerants = 0;
            RGBColorants[] cols = new RGBColorants[names.Length];
            for (int c = 0; c < names.Length; c++)
            {
                var name = names[c];
                if ("None".Equals(name))
                    cols[c] = RGBColorants.None;
                else
                {
                    if ("Red".Equals(name))
                        cols[c] = RGBColorants.Red;
                    else if ("Green".Equals(name))
                        cols[c] = RGBColorants.Green;
                    else if ("Blue".Equals(name))
                        cols[c] = RGBColorants.Blue;
                    else
                        return null;
                    nColerants++;
                }
            }
            if (nColerants == 0) return new RGBColorants[0];
            return cols;
        }

        #endregion

        /// <summary>
        /// Applies a known colerants directly
        /// </summary>
        class ColorantConverter : ColorConverter
        {
            readonly RGBColorants[] _colors;
            public ColorantConverter(RGBColorants[] cols)
            {
                _colors = cols;
            }
            public override DblColor MakeColor(double[] comps)
            {
                var n = new double[DeviceRGB.Instance.NComponents];
                for (int c = 0; c < _colors.Length; c++)
                {
                    //The component names shall all be different from one another, 
                    //except for the name None
                    //
                    //However if a name appears multiple times, the last one is
                    //used (result of the (int) color bit).
                    var color = _colors[c];
                    if (color != RGBColorants.None)
                        n[(int)color] = comps[c];
                }
                return DeviceRGB.RGBConverter.Instance.MakeColor(n);
            }
            public override double[] MakeColor(PdfColor col)
            {
                throw new NotImplementedException();
            }
        }
    }

#if NCHANNEL_SUPPORT

    /// <summary>
    /// The ChannelN defines seperate color spaces for each channel. 
    /// </summary>
    /// <remarks>
    /// If this implementation is faulty, one can always fall back to
    /// "DeviceN4"
    /// </remarks>
    [PdfVersion("1.6")]
    internal sealed class NChannel : DeviceN4
    {
        #region Properties

        public override ColorConverter Converter
        {
            get
            {
                var attribs = this.Attributes;
                var colors = Names;
                var pd = attribs.Process;
                var cl = attribs.Colorants;
                ColorConverter[] converters;
                if (pd != null)
                {
                    //Issue 113b - Function Type 4 has this
                    var proc = new ProcessConverter(pd, colors);
                    if (cl == null) return proc;

                    converters = CreateSpotConverter(colors, cl);
                    if (converters == null) return base.Converter;
                    return new NChannelConverter(converters, proc);
                }

                converters = CreateSpotConverter(colors, cl);

                //When there's non RGB colors in the list that we
                //can't handle we use the tint converter
                if (converters == null)
                    return base.Converter;

                return new SpotConverter(converters);
            }
        }

        #endregion

        #region Init

        internal NChannel(PdfArray ar)
            : base(ar)
        { }

        #endregion

        #region Methods

        private ColorConverter[] CreateSpotConverter(string[] colors, ColorantsDictionary colorants)
        {
            if (colorants == null) return null;

            var converters = new ColorConverter[colors.Length];
            for (int c = 0; c < colors.Length; c++)
            {
                string colorant = colors[c];
                switch (colorant)
                {
                    //None colors are ignored. Process colors will be set to none so that
                    //they are ignored too.
                    case "None":
                        converters[c] = new SeparationCS.NoneConverter(); break;
                    default:

                        //For spot colors. Spot colors are unlike Cyan, Magenta,
                        //Red, Green, etc. They must have a SeparationCS
                        SeparationCS cs = colorants[colorant];
                        if (cs == null)
                        {
                            //If we encounter something we can't handle we fall
                            //back to deviceN
                            Debug.Assert(false, "Unknown spot color");
                            return null;
                        }

                        converters[c] = cs.Converter;

                        break;
                }
            }

            return converters;
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new NChannel(array);
        }

        #endregion

        #region Helper clases

        class NChannelConverter : ColorConverter
        {
            readonly SpotConverter _spot;
            readonly ProcessConverter _proc;

            public NChannelConverter(ColorConverter[] spot, ProcessConverter proc)
            { _spot = new SpotConverter(spot); _proc = proc; }

            public override DblColor MakeColor(double[] comps)
            {
                var col1 = _spot.MakeColor(comps);
                var col2 = _proc.MakeColor(comps);

                return new DblColor(col1.R + col2.R, col1.G + col2.G, col1.B + col2.B);
            }
            public override double[] MakeColor(PdfColor col)
            {
                throw new NotSupportedException();
            }
        }

        class SpotConverter : ColorConverter
        {
            readonly ColorConverter[] _converters;

            public SpotConverter(ColorConverter[] converters)
            { _converters = converters; }

            public override DblColor MakeColor(double[] comps)
            {
                //Convertes each comp to RGB sepperatly
                var comp = new double[1];

                double red = 0, green = 0, blue = 0;

                for(int c=0; c < comps.Length; c++)
                {
                    comp[0] = comps[c];
                    var dblcol = _converters[c].MakeColor(comp);
                    
                    //Additive blend
                    red += dblcol.R;
                    green += dblcol.G;
                    blue += dblcol.B;
                }

                return new DblColor(red, green, blue);
            }
            public override double[] MakeColor(PdfColor col)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Process colors are colors such as Red, Green, Cyan, etc. 
        /// </summary>
        class ProcessConverter : ColorConverter
        {
            readonly ColorConverter _cs;
            readonly int[] _index;
            readonly double[] _color;

            public ProcessConverter(ProcessDictionary pd, string[] names)
            {
                var cmps = pd.Components;
                var index = new int[names.Length];

                for (int c = 0; c < names.Length; c++)
                {
                    var idx = Array.IndexOf(cmps, names[c]);
                    if (idx != -1)
                    { 
                        //Makes spot colors ignore this color
                        names[c] = "None";
                    }
                    index[c] = idx;
                }

                var cs = pd.ColorSpace;
                _cs = cs.Converter;
                _color = new double[cmps.Length];
                _index = index;
                Debug.Assert(cmps.Length == cs.NComponents);
            }

            public override DblColor MakeColor(double[] comps)
            {
                for (int c = 0; c < _index.Length; c++)
                {
                    var index = _index[c];
                    if (index != -1)
                        _color[index] = comps[c];
                }
                return _cs.MakeColor(_color);
            }
            public override double[] MakeColor(PdfColor col)
            {
                throw new NotSupportedException();
            }
        }

        #endregion
    }

#endif
}
