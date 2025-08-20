using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    public sealed class DeviceGray : PdfColorSpace
    {
        #region Variables and properties

        /// <summary>
        /// Shared instance
        /// </summary>
        private static DeviceGray _device_gray;

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
        /// Color to set when selecting this color space
        /// </summary>
        public override PdfColor DefaultColor { get { return new IntColor(0); } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public override double[] DefaultColorAr { get { return new double[] { 0 }; } }

        /// <summary>
        /// Gets an instance of this colorspace
        /// </summary>
        public static DeviceGray Instance
        {
            get
            {
                if (_device_gray == null)
                    _device_gray = new DeviceGray();
                return _device_gray;
            }
        }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public override int NComponents { get { return 1; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public override double[] DefaultDecode
        {
            get { return new double[] { 0, 1 }; }
        }

        #endregion

        #region Init

        private DeviceGray() { }

        #endregion

        #region Overrides

        public override ColorConverter Converter
        {
            get { return new GrayConverter(); }
        }

        internal override void Write(Write.Internal.PdfWriter write)
        {
            write.WriteName("DeviceGray");
        }

        public override string ToString()
        {
            return "/DeviceGray";
        }

        #endregion

        internal static GrayColor GrayScaleColor(PdfColor col)
        {
            return new GrayColor(0.299 * col.Red + 0.587 * col.Green + 0.114 * col.Blue);
        }

        sealed class GrayConverter : ColorConverter
        {
            public override PdfColor MakeColor(byte[] raw)
            {
                Debug.Assert(raw.Length == 1);
                return new IntColor(raw[0]);
            }

            public override GrayColor MakeGrayColor(double[] comps)
            {
                return new GrayColor(comps[0]);
            }

            public override DblColor MakeColor(double[] comps)
            {
                var c0 = comps[0];
                //Todo: mul c0 with color offset values?
                // Note though that DeviceGray is used by ImageMask images. They assume current impl.
                return new DblColor(c0, c0, c0);
            }

            public override double[] MakeColor(PdfColor col)
            {
                return new double[] { 0.299 * col.Red + 0.587 * col.Green + 0.114 * col.Blue };
            }
        }
    }
}
