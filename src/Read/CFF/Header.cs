namespace PdfLib.Read.CFF
{
    /// <summary>
    /// Header of CFF data
    /// </summary>
    struct Header
    {
        /// <summary>
        /// Major version number.
        /// </summary>
        /// <remarks>Parsers should throw on unsuported versions</remarks>
        public byte major;

        /// <summary>
        /// Minor version number
        /// </summary>
        /// <remarks>Parsers should ignore discrepancies</remarks>
        public byte minor;

        /// <summary>
        /// Header size
        /// </summary>
        public byte hdrSize;

        /// <summary>
        /// Offset size
        /// </summary>
        public byte offSize;
    }
}
