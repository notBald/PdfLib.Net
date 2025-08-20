using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    public sealed class DeviceRGB : PdfColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// Shared instance of this color space
        /// </summary>
        private static DeviceRGB _device_rgb;

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public override PdfCSType CSType { get { return PdfCSType.Device; } }

        /// <summary>
        /// This colorspace saves itself directly
        /// </summary>
        /// <remarks>
        /// Direct isn't required, but add up the bytes
        /// for the reference, object id and entery in
        /// the XRef table and it's better to simply
        /// save this direct.
        /// </remarks>
        internal override SM DefSaveMode { get { return SM.Direct; } }

        /// <summary>
        /// Color that will be set when this colorspace is selected
        /// </summary>
        public override PdfColor DefaultColor { get { return new IntColor(0,0,0); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public override double[] DefaultColorAr { get { return new double[] { 0, 0, 0 }; } }

        /// <summary>
        /// Gets an instance of this colorspace
        /// </summary>
        public static DeviceRGB Instance
        {
            get
            {
                if (_device_rgb == null)
                    _device_rgb = new DeviceRGB();
                return _device_rgb;
            }
        }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public override int NComponents { get { return 3; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public override double[] DefaultDecode
        {
            get { return new double[] { 0, 1, 0, 1, 0, 1}; }
        }

        #endregion

        #region Init

        private DeviceRGB() { }

        #endregion

        #region Overrides

        internal override void Write(Write.Internal.PdfWriter write)
        {
            write.WriteName("DeviceRGB");
        }

        public override string ToString()
        {
            return "/DeviceRGB";
        }

        public override ColorConverter Converter
        {
            get { return RGBConverter.Instance; }
        }

        #endregion

        internal sealed class RGBConverter : ColorConverter
        {
            public readonly static RGBConverter Instance = new RGBConverter();

            private RGBConverter() { }

            public override PdfColor MakeColor(byte[] raw)
            {
                Debug.Assert(raw.Length == 3);
                return new IntColor(raw[0], raw[1], raw[2]);
            }

            public override DblColor MakeColor(double[] comps)
            {
                return new DblColor(comps[0], comps[1], comps[2]);
            }

            public override double[] MakeColor(PdfColor col)
            {
                return new double[] { col.Red, col.Green, col.Blue };
            }
        }
    }

    /// <summary>
    /// Colerants for RGB, values denots channel number as well.
    /// </summary>
    internal enum RGBColorants
    {
        None = -1,
        Red = 0,
        Green,
        Blue 
    }
}
