namespace PdfLib.Img.Tiff.Internal
{
    /// <summary>
    /// Common compression schemes used in tiff images. Tiff can also support other schemes.
    /// </summary>
    public enum COMPRESSION_SCHEME
    {
        UNDEFINED = -1,
        UNCOMPRESSED = 1,
        CCITT_MOD_HUFF_RLE,
        CCITT_G3,
        CCITT_G4,
        LZW,
        old_JPEG = 6,
        JPEG,
        ADOBE_DEFLATE,
        JBIG_BW,
        JBIG_C,
        JP_2000 = 34712,
        PACKBITS = 32773,
        DEFLATE = 32946,

        /// <summary>
        /// Not implemented, look here: https://www.cl.cam.ac.uk/~mgk25/jbigkit/
        /// </summary>
        JBIG = 34661,

        /// <summary>
        /// Seems to be an abandoned stardard. (https://datatracker.ietf.org/doc/html/draft-ietf-fax-tiff-fx-extension1-01)
        /// </summary>
        JBIG2 = 34715,

        /// <summary>
        /// A properitary tag. Have the same meaning as deflate.
        /// </summary>
        PIXTIFF = 50013,

        /// <summary>
        /// Only for internal libary use
        /// </summary>
        WHITE_CCITT_G3,
        WHITE_CCITT_G4
    }
}
