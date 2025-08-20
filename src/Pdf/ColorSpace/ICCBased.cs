using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Filter;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// Since ICC correct colors isn't a priority this class is just a thin wrapper on
    /// Device* (as appropriate)
    /// </summary>
    /// <remarks>
    /// Todo:
    /// I've not looked into how the "Range" parameter affects the Alt Colorspace,
    /// if at all.
    /// 
    /// See http://www.normankoren.com/color_management_2.html for ICC profile info
    /// </remarks>
    [PdfVersion("1.3")]
    public sealed class ICCBased : ItemArray, IColorSpace
    {
        /// <summary>
        /// Colorspace this profile wraps arround.
        /// </summary>
        private readonly PdfColorSpace _cs;

        #region Properties

        internal override PdfType Type { get { return PdfType.ColorSpace; } }
        public PdfCSType CSType { get { return PdfCSType.CIE; } }

        public int NComponents { get { return _cs.NComponents; } }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public ColorConverter Converter { get { return _cs.Converter; } }

        /// <summary>
        /// The alternate colorspace
        /// </summary>
        public PdfColorSpace Alternate { get { return _cs; }  }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode { get { return _cs.DefaultDecode; } }

        /// <summary>
        /// The ICC profile
        /// </summary>
        public IStream Stream { get { return (IStream)_items[1].Deref(); } }

        public PdfColor DefaultColor 
        { 
            get 
            { 
                //Default colors for ICC based is "0" in all components. 
                var col = new double[Alternate.NComponents];

                //Unless it falls outside of "Range", though
                //for now "Range" is ignored
                return Alternate.Converter.MakeColor(col);
            } 
        }

        public double[] DefaultColorAr
        {
            get
            {
                //Default colors for ICC based is "0" in all components. 
                var col = new double[Alternate.NComponents];

                //Unless it falls outside of "Range", though
                //for now "Range" is ignored
                return col;
            }
        }

        #endregion

        #region Init

        public ICCBased(PdfColorSpace cs, byte[] icc_profile, bool compressed)
            : base(new TemporaryArray(2))
        {
            if (cs == null || icc_profile == null)
                throw new ArgumentNullException();

            _items[0] = new PdfName("ICCBased");
            //var dict = new TemporaryDictionary();
            var ws = compressed ? new WritableStream(icc_profile, new FilterArray(new PdfFlateFilter())) : new WritableStream(icc_profile);
            ws.Elements.SetInt("N", cs.NComponents);
            _items.SetNewItem(1, ws, true);
            _cs = cs;
        }

        internal ICCBased(PdfArray itms) : base(itms) 
        {
            //System.IO.File.WriteAllBytes(@"C:\temp\test.icc", Stream.DecodedStream);
            var r = _items[1].Deref();
            if (!(r is PdfStream))
                throw new PdfInternalException("corrupt object");
            var dict = ((PdfStream)r).Elements;
            int elm = dict.GetUIntEx("N");

            //Todo: Use the alternative color space if specified.
            //IColorSpace alt = (IColorSpace)dict.GetItem("Alternate").ToType(PdfType.ColorSpace);

            if (elm == 4)
                _cs = DeviceCMYK.Instance;
            else if (elm == 3)
                _cs = DeviceRGB.Instance;
            else if (elm == 1)
                _cs = DeviceGray.Instance;
            else
                throw new PdfNotSupportedException();
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
            if (obj is ICCBased)
            {
                var cs = (ICCBased)obj;
                var stream = Stream;
                return NComponents == cs.NComponents &&
                       PdfStream.Equals(stream, cs.Stream);
            }
            return false;
        }
         

        protected override ItemArray MakeCopy(PdfArray data, ResTracker tracker)
        {
            return new ICCBased(data);
        }

        #endregion
    }
}
