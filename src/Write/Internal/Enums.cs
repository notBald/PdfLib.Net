namespace PdfLib.Write.Internal
{
    public enum CompressionMode
    {
        /// <summary>
        /// All resources, with the sole exception
        /// of jpeg2000 compressed images, will be
        /// saved uncompressed.
        /// </summary>
        /// <remarks>
        /// The reason JPX files are not uncomressed is because they
        /// don't quite fit with the PDF specs, so simply decompressing
        /// and removing the JPXfilter isn't enough.
        /// </remarks>
        Uncompressed = -1,

        /// <summary>
        /// No changes will be made to the current document. 
        /// </summary>
        None,

        /// <summary>
        /// Uncompressed resources will be deflate compressed
        /// </summary>
        /// <remarks>This does not include resources that doesn't benefit from said compression</remarks>
        Normal,

        /// <summary>
        /// All resources will be deflate compressed, even those
        /// that are already compressed.
        /// </summary>
        /// <remarks>This does not include resources that doesn't benefit from said compression</remarks>
        Maximum,
    }

    /// <summary>
    /// How a writable document is to be saved
    /// </summary>
    public enum SaveMode
    {
        /// <summary>
        /// Save the document in a format readable
        /// for older PDF viewers
        /// </summary>
        Compatibility,
        /// <summary>
        /// Compress the document as much as possible.
        /// This is a PDF 1.5 feature
        /// </summary>
        Compressed,
    }

    public enum OutputMode
    {
        /// <summary>
        /// Will stamp the PDF document as binary
        /// </summary>
        AssumeBinary,

        /// <summary>
        /// Will not stamp the PDF document as binary
        /// </summary>
        AssumeText,

        /// <summary>
        /// Forces the output to comply with text
        /// </summary>
        ForceText,

        /// <summary>
        /// Check if there's binary data in any resource. (slow)
        /// </summary>
        Auto
    }

    /// <summary>
    /// How a document os padded.
    /// </summary>
    public enum PM
    {
        /// <summary>
        /// No padding
        /// </summary>
        None,

        /// <summary>
        /// A little padding. Intended for use with comp. mode.
        /// </summary>
        Some,

        /// <summary>
        /// Include information useful for debugging
        /// </summary>
        Verbose
    }

    /// <summary>
    /// Store Mode enum
    /// </summary>
    public enum SM
    {
        /// <summary>
        /// Relevant for documents opened straight into write mode. In that
        /// case the low level dictionaries do not know how they are allowed
        /// to be saved. 
        /// </summary>
        Unknown,

        /// <summary>
        /// Object is put in a compressed object stream
        /// </summary>
        /// <remarks>
        /// This is a PDF 1.5 feature. If saving for less than PDF 1.5
        /// then this will be the same as "Indirect".
        /// </remarks>
        Compressed,

        /// <summary>
        /// Object is stored at the outmost layer of the PDF file.
        /// </summary>
        Indirect,

        /// <summary>
        /// Will be stored directly in the object.
        /// 
        /// If an object is used multiple times, it will be written out
        /// multiple times in the PDF file
        /// </summary>
        Direct,

        /// <summary>
        /// Will set the objedt direct, indirect or compressed based on
        /// a paramater supplied during saving and ref counting.
        /// </summary>
        Auto,

        /// <summary>
        /// Prevents object from being saved.
        /// </summary>
        Ignore
    }


    /// <summary>
    /// How similar two objects are.
    /// </summary>
    internal enum Equivalence
    {
        /// <summary>
        /// Need not be the same object, but close enough
        /// </summary>
        Identical,

        /// <summary>
        /// There are differences, but perhaps they can be worked around
        /// </summary>
        Similar,

        /// <summary>
        /// The objects are different
        /// </summary>
        Different
    }
}
