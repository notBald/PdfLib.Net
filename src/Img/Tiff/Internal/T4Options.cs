namespace PdfLib.Img.Tiff.Internal
{
    /// <summary>
    /// Options for G3 encoded images
    /// </summary>
    public class T4Options
    {
        public const ulong G32D = 1;

        #region Variables and properties

        readonly uint _value;
        readonly bool _fill_order;

        /// <summary>
        /// If this image use 2D coding
        /// </summary>
        /// <remarks>K value should be a multiple of n rows on 2D coding</remarks>
        public bool TwoDimensional { get { return (_value & 1) == 1; } }

        /// <summary>
        /// If uncompressed mode is used
        /// </summary>
        public bool Uncompessed { get { return (_value & 2) == 2; } }

        /// <summary>
        /// If the rows are byte aligned
        /// </summary>
        public bool ByteAligned { get { return (_value & 4) == 4; } }

        /// <summary>
        /// True reverses black and white
        /// </summary>
        public bool FillOrder { get { return _fill_order; } }

        /// <summary>
        /// The raw T4 option value
        /// </summary>
        public uint Value { get { return _value; } }

        #endregion

        #region Init

        internal T4Options(uint value, bool fill_order) { _value = value; _fill_order = fill_order; }

        #endregion
    }
}
