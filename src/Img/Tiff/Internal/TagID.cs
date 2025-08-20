namespace PdfLib.Img.Tiff.Internal
{
    public enum TagID : ushort
    {
        PROGRESSIVE = 0,
        NewSubfileType = 254,
        SubfileType,
        ImageWidth = 256,
        ImageLength = 257,

        /// <summary>
        /// Number of bits per component
        /// </summary>
        BitsPerSample = 258,
        Compression = 259,
        /// <summary>
        /// WhiteIsZero 0
        /// BlackIsZero 1
        /// RGB 2
        /// RGB Palette 3
        /// Transparency mask 4
        /// CMYK 5
        /// YCbCr 6
        /// CIELab 8
        /// </summary>
        PhotometricInterpretation = 262,
        Threshholding,
        CellWidth = 264,
        CellLength = 265,

        /// <summary>
        /// The logical order of bits within a byte
        /// 
        /// True - LSB2MSB
        /// False - MSB2LSB
        /// </summary>
        FILLORDER = 266,
		DocumentName = 269,
        ImageDescription,
        Make,
        Model,
        StripOffsets = 273,
        Orientation = 274,
        SamplesPerPixel = 277,
        RowsPerStrip,
        StripByteCounts,
        MinSampleValue,
        MaxSampleValue = 281,
        XResolution = 282,
        YResolution,
        PlanarConfiguration,
        XPosition = 286,
        YPosition = 287,
        FreeOffsets = 288,
        FreeByteCounts = 289,
        GrayResponseUnit,
        GrayResponseCurve = 291,
        T4OPTIONS = 292,
        T6OPTIONS,
        ResolutionUnit = 296,
        PageNumber,

        Software = 305,
        DateTime = 306,
        Artist = 315,
        HostComputer,
        Predictor = 317,
        ColorMap = 320,
        TileWidth = 322,
        TileLength,
        TileOffsets,
        TileByteCounts,
        BadFaxLines = 326,
        CLEANFAXDATA = 327,
        ConsecutiveBadFaxLines,
        SubIFDs = 330,
        InkSet = 332,
        DotRange = 336,
        ExtraSamples = 338,
        SampleFormat,
        Indexed = 346,
        JPEGTables,
        YCbCrCoefficients = 529,
        YCbCrSubSampling = 530,
        YCbCrPositioning,
        ReferenceBlackWhite,
        XMP = 700,
        Copyright = 33432,
        Photoshop = 34377,
        ExifIFD = 34665
    }
}
