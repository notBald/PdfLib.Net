#define OpenJpeg
using System;
using System.Collections.Generic;
using System.Diagnostics;
#if OpenJpeg
using OpenJpeg;
using OpenJpeg.Internal;
#else
using OpenJpeg2;
using OpenJpeg2.Internal;
#endif
using PdfLib.Compose;
using PdfLib.Pdf;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Filter;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Img.Tiff.Internal;
using PdfLib.Util;
using PdfLib.Write.Internal;
using PdfLib.Res;
using static PdfLib.Img.ImageInfo.Tag;
using static PdfLib.Pdf.Internal.PdfDestination;
//using static PdfLib.Util.FlattenHierarchyProxy;
using System.Reflection;

namespace PdfLib.Img.Tiff
{
    public sealed class TiffImage : TiffIFD
    {
        #region Variables and properties

        private byte[][] _image_data;

        /// <summary>
        /// The raw/compressed data of this image
        /// </summary>
        public TiffRawContents RawContents 
        { 
            get 
            {
                if (_image_data != null)
                    return new TiffMemContents(this, _image_data);
                return new TiffFileContents(this, _file); 
            } 
        }

        /// <summary>
        /// All the compressed data of the image
        /// </summary>
        public TiffTile[,] RawData { get { return RawContents.RawData; } }

        /// <summary>
        /// This image represented as a PDF form, null if no representation is possible
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public PdfXObject AsPdfForm { get { return ConvertToXObject(false); } }

        /// <summary>
        /// Number of color components in the image.
        /// </summary>
        public int NComponents 
        { 
            get 
            {
                if (Indexed)
                {
                    switch (PhotometricInterpretation)
                    {
                        case Photometric.Separated:
                            if (InkSet == TiffInk.CMYK)
                                return 4;
                            else throw new TiffNotImplementedException("InkSet");
                        case Photometric.YCbCr:
                        case Photometric.CIELAB:
                        case Photometric.Palette:
                        case Photometric.RGB:
                            return 3;
                        case Photometric.BlackIsZero:
                        case Photometric.WhiteIsZero:
                        case Photometric.TransparencyMask:
                            return 1;
                    }
                }
                return SamplesPerPixel; 
            } 
        }

        /// <summary>
        /// Quick but imperfect check if  this is an valid image.
        /// </summary>
        public bool IsValid
        {
            get
            {
                return _tags.ContainsKey(TagID.ImageWidth) && _tags.ContainsKey(TagID.ImageLength) && _tags.ContainsKey(TagID.PhotometricInterpretation);
            }
        }

        #endregion

        #region Properties form the specs

        /// <summary>
        /// Person who created the image
        /// </summary>
        public string Artist { get { return GetASCI(TagID.Artist); } }

        /// <summary>
        /// Number of bits per component
        /// </summary>
        public ushort[] BitsPerSample 
        { 
            get 
            {
                ushort[] sa;
                var t = GetTag(TagID.BitsPerSample, DataType.SHORT);
                if (t == null)
                {
                    sa = new ushort[SamplesPerPixel];
                    for (int c = 0; c < sa.Length; c++)
                        sa[c] = 1;
                    return sa;
                }
                if (t.Count == 1)
                {
                    ushort? val = GetUShort(TagID.SamplesPerPixel);
                    if (val != null && val.Value != 1)
                    {
                        sa = new ushort[val.Value];
                        ushort v = (ushort) t.GetSingleValue();
                        for (int c = 0; c < sa.Length; c++)
                            sa[c] = (ushort) v;
                        return sa;
                    }

                    return new ushort[] { (ushort)t.GetSingleValue() };
                }
                
                sa = t.GetUShorts();
                if (sa == null)
                {
                    sa = new ushort[SamplesPerPixel];
                    for (int c = 0; c < sa.Length; c++)
                        sa[c] = 1;
                }
                return sa;
            } 
        }

        /// <summary>
        /// BitsPerComponent, returns zero if components are of different bit sizes
        /// </summary>
        public int BitsPerComponent
        {
            get
            {
                var bpc_ar = BitsPerSample;
                var val = bpc_ar[0];
                for (int c = 0; c < bpc_ar.Length; c++)
                {
                    var v = bpc_ar[c];
                    if (v != val)
                        return 0;
                }
                return val;
            }
        }

        public int BitsPerPixel
        {
            get
            {
                var bpc_ar = BitsPerSample;
                int bpc_val = 0;
                for (int c = 0; c < bpc_ar.Length; c++)
                {
                    var v = bpc_ar[c];
                    bpc_val += v;
                }
                return bpc_val;
            }
        }

        /// <summary>
        /// The length of the dithering or halftoning matrix used to create a dithered or
        /// halftoned bilevel file
        /// 
        /// Threshholding must be 2
        /// </summary>
        public ushort? CellLength { get { return GetUShort(TagID.CellLength); } }

        /// <summary>
        /// The width of the dithering or halftoning matrix used to create a dithered or
        /// halftoned bilevel file
        /// </summary>
        public ushort? CellWidth { get { return GetUShort(TagID.CellWidth); } }

        /// <summary>
        /// Specifies how to interpret each data sample in a pixel
        /// </summary>
        public SampleFormat[] SampleFormat 
        { 
            get 
            {
                int count = SamplesPerPixel;
                var t = GetTag(TagID.SampleFormat, DataType.SHORT, count);
                var ret = new SampleFormat[count];
                if (t == null)
                {
                    for (int c = 0; c < ret.Length; c++) ret[c] = Internal.SampleFormat.UINT;
                    return ret;
                }
                var val = t.GetUShorts();
                for (int c = 0; c < ret.Length; c++)
                    ret[c] = (SampleFormat)val[c];
                return ret; 
            } 
        }

        /// <summary>
        /// The number of components per pixel
        /// </summary>
        public ushort SamplesPerPixel
        {
            get
            {
                ushort? val = GetUShort(TagID.SamplesPerPixel);
                if (val != null) return (val.Value);
                var t = GetTag(TagID.BitsPerSample, DataType.SHORT);
                if (t != null)
                    return (ushort)t.Count;
                return 1;
            }
        }

        /// <summary>
        /// Denotes the number of 'bad' scan lines encountered by the facsimile device
        /// </summary>
        public ushort? BadFaxLines { get { return GetUShort(TagID.BadFaxLines); } }

        /// <summary>
        /// Indicates if 'bad' lines encountered during reception are stored in the data
        /// </summary>
        public ushort? CleanFaxData { get { return GetUShort(TagID.CLEANFAXDATA); } }

        /// <summary>
        /// The maximum number of consecutive 'bad' scanlines received
        /// </summary>
        public ushort? ConsecutiveBadFaxLines { get { return GetUShort(TagID.ConsecutiveBadFaxLines); } }

        /// <summary>
        /// A color map for palette color images
        /// </summary>
        public ushort[] ColorMap 
        { 
            get 
            { 
                var map = GetUShorts(TagID.ColorMap);
                if (map != null && map.Length != NComponents * (Math.Pow(2, Util.ArrayHelper.Max(BitsPerSample))))
                    return null;
                return map;
            } 
        }

        /// <summary>
        /// Compression scheme used on the image data
        /// </summary>
        public COMPRESSION_SCHEME Compression { get { return (COMPRESSION_SCHEME)GetUShort(TagID.Compression, 1); } }

        /// <summary>
        /// Copyright notice
        /// </summary>
        public string Copyright { get { return GetASCI(TagID.Copyright); } }

        /// <summary>
        /// Date and time of image creation
        /// </summary>
        public PdfDate DateTime
        {
            get
            {
                var str = GetASCI(TagID.DateTime);
                if (str == null || str.Length != 19)
                    return null;
                var date_time = str.Split(new char[]{ ' ', ':' });
                if (date_time.Length != 6) return null;
                var ints = new int[6];
                for (int c = 0; c < ints.Length; c++)
                {
                    int val;
                    if (!int.TryParse(date_time[c], out val))
                        return null;
                    ints[c] = val;
                }
                try { return new PdfDate(ints[0], ints[1], ints[2], ints[3], ints[4], ints[5], null, null); }
                catch (PdfCreateException) { return null; }
            }
        }

        public TiffExif ExifIFD { get { return TiffExif.Create(GetUInt(TagID.ExifIFD, 0), _file, _big); } }

        /// <summary>
        /// Description of extra components
        /// </summary>
        public SampleType[] ExtraSamples
        {
            get
            {
                var types = GetUShorts(TagID.ExtraSamples);
                if (types == null) return null;
                var ret = new SampleType[types.Length];
                for(int c = 0; c < types.Length; c++)
                    ret[c] = (SampleType) types[c];
                return ret;
            }
        }

        /// <summary>
        /// The logical order of bits within a byte
        /// </summary>
        public ushort FillOrder { get { return GetUShort(TagID.FILLORDER, 1); } }

        /// <summary>
        /// For each string of contiguous unused bytes in a TIFF file, the byte offset of the
        /// string.
        /// </summary>
        public uint[] FreeOffsets { get { return GetUInts(TagID.FreeOffsets); } }

        /// <summary>
        /// For each string of contiguous unused bytes in a TIFF file, the number of bytes in
        /// the string
        /// </summary>
        public uint[] FreeByteCounts { get { return GetUInts(TagID.FreeByteCounts); } }

        /// <summary>
        /// For grayscale data, the optical density of each possible pixel value
        /// </summary>
        public ushort[] GrayResponseCurve { get { return GetUShorts(TagID.GrayResponseCurve); } }

        /// <summary>
        /// The precision of the information contained in the GrayResponseCurve
        /// </summary>
        public ushort GrayResponseUnit { get { return GetUShort(TagID.GrayResponseUnit, 2); } }

        /// <summary>
        /// The computer and/or operating system in use at the time of image creation
        /// </summary>
        public string HostComputer { get { return GetASCI(TagID.HostComputer); } }

        /// <summary>
        /// A string that describes the subject of the image
        /// </summary>
        public string ImageDescription { get { return GetASCI(TagID.ImageDescription); } }

        /// <summary>
        /// If this image has a palette
        /// </summary>
        public bool Indexed { get { return GetUShort(TagID.Indexed, 0) == 1 || PhotometricInterpretation == Photometric.Palette; } }

        /// <summary>
        /// What type of ink is in a sepperated color space
        /// </summary>
        public TiffInk InkSet { get { return (TiffInk)GetUShort(TagID.InkSet, 1); } }

        /// <summary>
        /// Table to use for jpeg decompression
        /// </summary>
        public byte[] JPEGTables
        {
            get
            {
                var t = GetTag(TagID.JPEGTables, DataType.UNDEFINED);
                if (t == null || t.Count < 4) return null;
                return t.GetBytes();
            }
        }

        /// <summary>
        /// The scanner manufacturer
        /// </summary>
        public string Make { get { return GetASCI(TagID.Make); } }

        /// <summary>
        /// The maximum component value used
        /// </summary>
        public ushort MaxSampleValue 
        { get { return GetUShort(TagID.MaxSampleValue, (ushort) (Math.Pow(2, Util.ArrayHelper.Max(BitsPerSample)) - 1)); } }

        /// <summary>
        /// The minimum component value used
        /// </summary>
        public ushort MinSampleValue { get { return GetUShort(TagID.MinSampleValue, 0); } }

        /// <summary>
        /// The scanner model name or number
        /// </summary>
        public string Model { get { return GetASCI(TagID.Model); } }

        /// <summary>
        /// The number of rows of pixels in the image
        /// </summary>
        public int Height { get { return GetIntOrUShortEx(TagID.ImageLength); } }

        /// <summary>
        /// The number of columns in the image, i.e., the number of pixels per row
        /// </summary>
        public int Width { get { return GetIntOrUShortEx(TagID.ImageWidth); } }

        /// <summary>
        /// A general indication of the kind of data contained in this subfile
        /// </summary>
        public ImageType NewSubfileType 
        { 
            get { return (ImageType)GetUInt(TagID.NewSubfileType, 0); }
            set { TagList.Set(_tags, TagID.NewSubfileType, (ulong)value, BigEndian); }
        }

        /// <summary>
        /// The orientation of the image with respect to the rows and columns
        /// </summary>
        public Orientation Orientation 
        { 
            get { return (Orientation)GetUInt(TagID.Orientation, 1); }
            set { TagList.Set(_tags, TagID.Orientation, (ulong)value, BigEndian); }
        }

        /// <summary>
        /// The page number of the page from which this image was scanned
        /// </summary>
        public ushort[] PageNumber { get { return GetUShorts(TagID.PageNumber, 2); } }

        /// <summary>
        /// The color space of the image data
        /// </summary>
        public Photometric PhotometricInterpretation 
        { 
            get { return (Photometric)GetUShortEx(TagID.PhotometricInterpretation); }
            set { TagList.Set(_tags, TagID.PhotometricInterpretation, (ulong)value, BigEndian); }
        }

        /// <summary>
        /// How the components of each pixel are stored
        /// </summary>
        public PixelMode PlanarConfiguration { get { return (PixelMode)GetUShort(TagID.PlanarConfiguration, 1); } }

        /// <summary>
        /// Collection of Photoshop 'Image Resource Blocks'
        /// </summary>
        public byte[] Photoshop { get { return GetByteTag(TagID.Photoshop); } }

        /// <summary>
        /// Improves compression on some images
        /// </summary>
        public ushort Predictor { get { return GetUShort(TagID.Predictor, 1); } }

        /// <summary>
        /// Extremities
        /// </summary>
        public double[] ReferenceBlackWhite
        {
            get
            {
                var t = GetRationals(TagID.ReferenceBlackWhite);
                if (t != null && t.Length == 6)
                    return t;
                if (PhotometricInterpretation == Photometric.YCbCr)
                    return new double[] { 0, 255, 128, 255, 128, 255 };
                var NV = Math.Pow(2, BitsPerComponent) - 1;
                return new double[] { 0, NV, 0, NV, 0, NV };
            }
        }

        /// <summary>
        /// The unit of measurement for XResolution and YResolution
        /// </summary>
        public ResUnit ResolutionUnit { get { return (ResUnit)GetUShort(TagID.ResolutionUnit, 2); } }

        /// <summary>
        /// The number of rows per strip
        /// </summary>
        public uint RowsPerStrip { get { return GetUIntOrUShort(TagID.RowsPerStrip, uint.MaxValue); } }

        /// <summary>
        /// Number of strips in an image
        /// </summary>
        public int StripsPerImage { get { return (int) ((Height + RowsPerStrip - 1) / RowsPerStrip); } }

        /// <summary>
        /// Name and version number of the software package(s) used to create the image
        /// </summary>
        public string Software { get { return GetASCI(TagID.Software); } }

        /// <summary>
        /// For each strip, the number of bytes in the strip after compression
        /// </summary>
        public ulong[] StripByteCounts { get { return GetUIntsOrUShorts(TagID.StripByteCounts); } }

        /// <summary>
        /// For each strip, the byte offset of that strip
        /// </summary>
        public ulong[] StripOffsets { get { return GetUIntsOrUShorts(TagID.StripOffsets); } }

        /// <summary>
        /// This field is deprecated. The NewSubfileType field should be used instead
        /// </summary>
        public ImageType? SubfileType { get { return (ImageType?)GetUShort(TagID.SubfileType); } }

        /// <summary>
        /// Images relevant to this image. Can for instance be a thumbnail, or an smask.
        /// </summary>
        public TiffImage[] SubIFDs
        {
            get
            {
                var idfs_ar = GetIFDs(TagID.SubIFDs);
                if (idfs_ar == null) return null;
                TiffImage[] images = new TiffImage[idfs_ar.Length];
                if (idfs_ar is ulong[])
                {
                    var idfs = (ulong[])idfs_ar;
                    for (int c = 0; c < images.Length; c++)
                        images[c] = (TiffImage)TiffStream.ParseTiff((long)idfs[c], _big, _file, IFDType.Image);
                }
                else
                {
                    var oa = (object[]) idfs_ar;
                    for (int c = 0; c < images.Length; c++)
                        images[c] = (TiffImage) oa[c];
                }
                return images;
            }
            set
            {
                TagList.SetObjects(_tags, TagID.SubIFDs, value, BigEndian);
            }
        }

        /// <summary>
        /// Options for G3 compression
        /// </summary>
        public T4Options T4Options { get { return new T4Options(GetUInt(TagID.T4OPTIONS, 0), FillOrder == 1); } }

        /// <summary>
        /// Options for G4 compression
        /// </summary>
        public uint T6Options { get { return GetUInt(TagID.T6OPTIONS, 0); } }

        /// <summary>
        /// The technique used to convert from gray to black and white pixels
        /// </summary>
        public ushort Threshholding { get { return GetUShort(TagID.Threshholding, 1); } }

        /// <summary>
        /// Number of tiles that span the width of the image
        /// </summary>
        public int TilesAcross 
        { 
            get 
            {
                int tw = (int) TileWidth;
                if (tw == 0) return 0;
                return (Width + tw - 1) / tw; 
            } 
        }

        /// <summary>
        /// Number of tiles that span the height of the image
        /// </summary>
        public int TilesDown
        {
            get
            {
                int tl = (int) TileLength;
                if (tl == 0) return 0;
                return (Height + tl - 1) / tl;
            }
        }

        /// <summary>
        /// Number of tiles in the image
        /// </summary>
        public int TilesPerImage { get { return TilesAcross * TilesDown; } }

        /// <summary>
        /// The tile height in pixels. Must be a multiple of 16.
        /// </summary>
        public uint TileLength { get { return GetUIntOrUShort(TagID.TileLength, 0); } }

        /// <summary>
        /// For each tile, the byte offset of that tile
        /// </summary>
        public ulong[] TileOffsets { get { return GetUIntsOrUShorts(TagID.TileOffsets); } }

        /// <summary>
        /// For each tile, the number of (compressed) bytes in that tile
        /// </summary>
        public ulong[] TileByteCounts { get { return GetUIntsOrUShorts(TagID.TileByteCounts); } }

        /// <summary>
        /// The tile width in pixels
        /// </summary>
        public uint TileWidth { get { return GetUIntOrUShort(TagID.TileWidth, 0); } }

        /// <summary>
        /// The X offset in ResolutionUnits of the left side of the image, with respect to the left side of the page. 
        /// </summary>
        public double? XPosition { get { return GetRational(TagID.XPosition); } }

        /// <summary>
        /// The number of pixels per ResolutionUnit in the ImageWidth direction
        /// </summary>
        public double? XResolution 
        { 
            get { return GetRational(TagID.XResolution); }
            set
            {
                if (value != null)
                    TagList.Set(_tags, TagID.XResolution, new double[] { value.Value }, BigEndian);
                else
                    _tags.Remove(TagID.XResolution);
            }
        }

        /// <summary>
        /// XML packet containing XMP metadata 
        /// </summary>
        public byte[] XMP { get { return GetByteTag(TagID.XMP); } }

        /// <summary>
        /// The min max range of the pixel. Note, returns null in place of default.
        /// </summary>
        public xRange[] DotRange
        {
            get
            {
                var t = GetTag(TagID.DotRange);
                if (t == null || t.Type != DataType.BYTE && t.Type != DataType.SHORT || t.Count == 0) return null;
                ushort[] data = t.GetAsUShorts();

                var da = new xRange[data.Length / 2];
                if (da.Length == 1)
                {
                    double max = Math.Pow(2, BitsPerComponent) - 1;
                    da[0] = new xRange(Img.Internal.LinearInterpolator.Interpolate(data[0], 0, max, 0, 1),
                                       Img.Internal.LinearInterpolator.Interpolate(data[1], 0, max, 0, 1));
                }
                else
                {
                    var bpc = BitsPerSample;
                    if (bpc.Length != da.Length || da.Length * 2 != data.Length)
                        return null;
                    for (int c = 0, d = 0; d < da.Length; d++)
                    {
                        double max = Math.Pow(2, bpc[d]) - 1;
                        da[d] = new xRange(Img.Internal.LinearInterpolator.Interpolate(data[c++], 0, max, 0, 1),
                                           Img.Internal.LinearInterpolator.Interpolate(data[c++], 0, max, 0, 1));
                    }
                }

                return da;
            }
        }

        /// <summary>
        /// The transformation from RGB to YCbCr image data. (LumaRed, LumaGreen, LumaBlue)
        /// </summary>
        public double[] YCbCrCoefficients 
        { 
            get 
            {
                var t = GetRationals(TagID.YCbCrCoefficients);
                if (t != null && t.Length == 3)
                    return t;
                return new double[] { 299 / 1000d, 587 / 1000d, 114 / 1000d };
            }
        }

        /// <summary>
        /// Specifies the positioning of subsampled chrominance components relative to luminance samples
        /// </summary>
        public SubSamplingPosition YCbCrPositioning { get { return (SubSamplingPosition)GetUShort(TagID.YCbCrPositioning, 1); } }

        /// <summary>
        /// Specifies the subsampling factors used for the chrominance components of a YCbCr image. 
        /// </summary>
        public SubSampling YCbCrSubSampling
        {
            get
            {
                var t = GetTag(TagID.YCbCrSubSampling, DataType.SHORT, 2);
                if (t != null)
                {
                    var ra = t.GetUShorts();
                    if (ra != null) return new SubSampling(ra[0], ra[1]);
                }
                return new SubSampling(2, 2);
            }
        }

        /// <summary>
        /// The number of pixels per ResolutionUnit in the ImageLength direction
        /// </summary>
        public double? YPosition { get { return GetRational(TagID.YPosition); } }

        /// <summary>
        /// The number of pixels per ResolutionUnit in the ImageLength direction
        /// </summary>
        public double? YResolution 
        { 
            get { return GetRational(TagID.YResolution); }
            set
            {
                if (value != null)
                    TagList.Set(_tags, TagID.YResolution, new double[] { value.Value }, BigEndian);
                else
                    _tags.Remove(TagID.YResolution);
            }
        }

        #endregion

        #region Init

        public TiffImage(int width, int height, int bpc, int ncomps, Photometric color_space, byte[] data, bool big_endian, int dpi)
            :base(null, false)
        {
            var tl = new TagList(12, big_endian);
            tl.Set(TagID.ImageWidth, (ulong)width);
            tl.Set(TagID.ImageLength, (ulong)height);
            var bps = new ushort[ncomps];
            Util.ArrayHelper.Fill(bps, (ushort)bpc);
            tl.Set(TagID.BitsPerSample, bps);
            tl.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.UNCOMPRESSED);
            tl.Set(TagID.PhotometricInterpretation, (ulong)color_space);
            tl.Set(TagID.StripByteCounts, new ulong[] { (ulong)data.Length });
            tl.Set(TagID.StripOffsets, new ulong[] { 0 });
            _image_data = new byte[][] { data };
            tl.Set(TagID.Orientation, (ulong) Orientation.UpLeft);
            tl.Set(TagID.SamplesPerPixel, (ulong)ncomps);
            tl.Set(TagID.XResolution, new double[] { (double)dpi });
            tl.Set(TagID.YResolution, new double[] { (double)dpi });
            tl.Set(TagID.ResolutionUnit, (ulong)ResUnit.Inch);
            Tags = tl.Tags;
        }

        internal TiffImage(TiffStreamReader file, bool is_big_tiff)
            : base(file, is_big_tiff)
        { }

        internal TiffImage(TagList tags, byte[][] data)
            :base(null, false)
        {
            Tags = tags.Tags;
            _image_data = data;
        }

        /// <summary>
        /// The Repair method is designed to fix or update the internal state of a TIFF image’s metadata tags.
        /// 
        /// Ensures that the necessary tags for tile or strip offsets and byte counts are present and correctly configured. 
        /// It handles both tiled and stripped storage, and it calculates the byte counts for uncompressed images if needed.
        /// </summary>
        /// <remarks>
        /// Detailed Explanation
        ///  1. Check for TileWidth Tag:
        ///     - The method first checks if the _tags dictionary contains the TileWidth tag.This indicates that the image uses tiled storage.
        ///  2. Repair TileOffsets Tag:
        ///     - If the TileOffsets tag is missing, the method attempts to replace the StripOffsets tag with TileOffsets.
        ///     - It retrieves the StripOffsets tag, removes it, changes its ID to TileOffsets, and adds it back to the _tags dictionary.
        ///  3. Repair TileByteCounts Tag:
        ///     - If the TileByteCounts tag is missing, the method attempts to replace the StripByteCounts tag with TileByteCounts.
        ///     - It retrieves the StripByteCounts tag, removes it, changes its ID to TileByteCounts, and adds it back to the _tags dictionary.
        ///     - If the StripByteCounts tag is not present and the image is uncompressed, it calculates the byte counts for each tile and adds the TileByteCounts tag.
        ///       - It calculates the height (h) and width (w) of the tiles.
        ///       - It calculates the size of each tile and the total number of tiles.
        ///       - If the image uses planar configuration, it throws a TiffNotImplementedException.
        ///       - It adds the TileByteCounts tag with the calculated sizes.
        ///  4. Repair StripByteCounts Tag:
        ///     - If the image does not use tiled storage and the StripByteCounts tag is missing, the method calculates the byte counts for each strip and adds the StripByteCounts tag.
        ///       - It calculates the height of each strip.
        ///       - It calculates the size of each strip and the total number of strips.
        ///       - If the image uses planar configuration, it throws a TiffNotImplementedException.
        ///       - It adds the StripByteCounts tag with the calculated sizes.
        /// </remarks>
        /// <exception cref="TiffNotImplementedException">If the image uses planar configuration</exception>
        internal override void Repair()
        {
            Tag t;
            if (_tags.ContainsKey(TagID.TileWidth))
            {
                if (!_tags.ContainsKey(TagID.TileOffsets))
                {
                    if (_tags.TryGetValue(TagID.StripOffsets, out t))
                    {
                        _tags.Remove(TagID.StripOffsets);
                        t.ID = TagID.TileOffsets;
                        _tags[TagID.TileOffsets] = t; 
                    }
                }
                if (!_tags.ContainsKey(TagID.TileByteCounts))
                {
                    if (_tags.TryGetValue(TagID.StripByteCounts, out t))
                    {
                        _tags.Remove(TagID.StripByteCounts);
                        t.ID = TagID.TileByteCounts;
                        _tags[TagID.TileByteCounts] = t;
                    }
                    else if (Compression == COMPRESSION_SCHEME.UNCOMPRESSED)
                    {
                        int h = (int)Math.Min(TileLength, (uint)Height), w = (int)Math.Min(TileWidth, (uint) Width);
                        ulong size = (ulong) ((BitsPerPixel * w + 7) / 8 * h);
                        ulong count = (ulong) TilesPerImage;
                        if (PlanarConfiguration == PixelMode.Planar)
                            throw new TiffNotImplementedException("Adding planar counts");

                        if (count == 1)
                            _tags.Add(TagID.TileByteCounts, new MemTag(TagID.TileByteCounts, DataType.LONG, 1, size, BigEndian));
                        else
                        {
                            ulong[] sizes = new ulong[count];
                            for (int c = 0; c < sizes.Length; c++)
                                sizes[c] = size;
                            _tags.Add(TagID.TileByteCounts, new MemTag(TagID.TileByteCounts, DataType.LONG, count, sizes, BigEndian));
                        }
                    }
                }
            }
            else if (!_tags.ContainsKey(TagID.StripByteCounts) && Compression == COMPRESSION_SCHEME.UNCOMPRESSED)
            {
                int h = (int)Math.Min(RowsPerStrip, (uint)Height);
                ulong size = (ulong) ((BitsPerPixel * Width + 7) / 8 * h);
                ulong count = (ulong) ((Height + RowsPerStrip - 1) / RowsPerStrip);
                if (PlanarConfiguration == PixelMode.Planar)
                    throw new TiffNotImplementedException("Adding planar counts");

                if (count == 1)
                    _tags.Add(TagID.StripByteCounts, new MemTag(TagID.StripByteCounts, DataType.LONG, 1, size, BigEndian));
                else
                {
                    ulong[] sizes = new ulong[count];
                    for (int c = 0; c < sizes.Length; c++)
                        sizes[c] = size;
                    _tags.Add(TagID.StripByteCounts, new MemTag(TagID.StripByteCounts, DataType.LONG, count, sizes, BigEndian));
                }
            }
        }

        public static TiffImage FromPDF(PdfImage image)
        {
            bool rbg = false;
            return FromPDF(image, PDFtoTIFFOptions.FOR_DISK, ref rbg);
        }

        public static TiffImage FromPDF(PdfImage image, PDFtoTIFFOptions options, ref bool force_rgb)
        {
            #region Step 0. JP2 and JPEG
            TiffImage alpha = null;
            TiffImage thumbnail = null;

            var format = image.Format;
            if (format == ImageFormat.JPEG2000)
            {
                if ((options & PDFtoTIFFOptions.EMBED_JP2) != 0)
                    throw new NotImplementedException();

                //Jpeg 2000 images may lack vital stuff like color space, and BPC, etc, so
                //it needs TLC.
                throw new NotImplementedException();
            }

            if (format == ImageFormat.JPEG)
            {
               // throw new NotImplementedException();
            }
            #endregion

            #region Step 1. Determine what colorspace to use

            var cs = image.ColorSpace;
            Photometric pm;
            TiffInk ink = TiffInk.CMYK;
            if (cs is DeviceRGB || cs is CalRGBCS && ((options & PDFtoTIFFOptions.FORCE_RGB) != PDFtoTIFFOptions.FORCE_RGB))
            {
                pm = Photometric.RGB;
                force_rgb = false;
            }
            else if (cs is DeviceGray || cs is CalGrayCS && ((options & PDFtoTIFFOptions.FORCE_RGB) != PDFtoTIFFOptions.FORCE_RGB))
            {
                pm = Photometric.BlackIsZero;
                force_rgb = false;
            }
            else if (cs is DeviceCMYK)
            {
                pm = Photometric.Separated;
                if ((options & PDFtoTIFFOptions.FORCE_RGB) == PDFtoTIFFOptions.FORCE_RGB)
                    force_rgb = true;
            }
            else if (cs is LabCS)
            {
                pm = Photometric.CIELAB;
                if ((options & PDFtoTIFFOptions.FORCE_RGB) == PDFtoTIFFOptions.FORCE_RGB)
                    force_rgb = true;
            }
            else if (cs is ICCBased)
            {
                var icc = (ICCBased)cs;
                var alt = icc.Alternate;
                if (alt is DeviceCMYK)
                {
                    pm = Photometric.Separated;
                    if ((options & PDFtoTIFFOptions.FORCE_RGB) == PDFtoTIFFOptions.FORCE_RGB)
                        force_rgb = true;
                }
                else
                {
                    force_rgb = false;
                    pm = (alt is DeviceRGB) ? Photometric.RGB : Photometric.BlackIsZero;
                }
            }
            else
            {
                if (cs is IndexedCS && image.BitsPerComponent == 1)
                {
                    var cc = cs.Converter;
                    var col0 = cc.MakeColor(new byte[1]);
                    var col1 = cc.MakeColor(new byte[] { 1 });
                    var black = new DblColor(0,0,0);
                    var white = new DblColor(1,1,1);
                    if (PdfColor.Equals(col0, black) && PdfColor.Equals(col1, white))
                    {
                        //cs = DeviceGray.Instance;
                        pm = Photometric.BlackIsZero;
                    }
                    else if (PdfColor.Equals(col1, black) && PdfColor.Equals(col0, white))
                    {
                        //cs = DeviceGray.Instance;
                        pm = Photometric.WhiteIsZero;
                    }
                    else
                    {
                        //Tiff supports indexed colors, but doing RGB for now
                        force_rgb = true;
                        pm = Photometric.RGB;
                    }
                }
                else
                {
                    //Tiff supports indexed colors, but doing RGB for now
                    force_rgb = true;
                    pm = Photometric.RGB;
                }
            }

            if (force_rgb)
            {
                //Since the ICC profile won't match we remove any such
                //profile embeding
                options &= ~PDFtoTIFFOptions.EMBED_ICC;
            }

            #endregion

            #region Step 2. Handle alpha

            if ((options & PDFtoTIFFOptions.EMBED_SMASK) != 0)
            {
                PdfImage pdf_alpha = null;

                var smask = image.SMask;
                if (smask != null)
                {
                    var matte = image.Matte;
                    if (matte != null)
                    {
                        //Tiff supports PreMultimplied alpha, but I don't know what to do with the matte color
                        //itself. It's possible that nothing needs doing if all values are zero, but I don't know.
                        throw new NotImplementedException();
                    }

                    pdf_alpha = smask;
                }
                else
                {
                    var mask = image.Mask;
                    if (mask is int[])
                    {
                        pdf_alpha = CreateMask((int[])mask, image);
                    }
                    else if (mask is PdfImage)
                    {
                        pdf_alpha = (PdfImage)mask;
                    }
                }

                if (pdf_alpha != null)
                {
                    //This is probably not nessesary, but I don't know.
                    if (pdf_alpha.Width != image.Width || pdf_alpha.Height != image.Height)
                        pdf_alpha = PdfImage.ChangeSize(pdf_alpha, image.Width, image.Height);

                    //There are three ways to embed alpha data in tiff files (that I know off)
                    // As a SubImage, as a following reduced image or as a channel in the data.
                    // Since the last method require decompressing the data, we go for the subimage
                    // approach.
                    bool rgb = false;
                    alpha = FromPDF(pdf_alpha, PDFtoTIFFOptions.FOR_DISK, ref rgb);
                }
            }
            else
            {
                var matte = image.Matte;
                if (matte != null)
                {

                    throw new NotImplementedException();
                }
            }
            #endregion

            #region Step 3. Applies Decode array, if needed.
            xRange[] decode = image.Decode;

            //Normalize decode to 0 - 1 range.
            var def = (cs is IndexedCS) ? ((IndexedCS) cs).GetDefaultDecode(image.BitsPerComponent) : cs.DefaultDecode;
            for(int c=0, pos = 0; c < decode.Length; c++)
            {
                double min = def[pos++], max = def[pos++];
                if (min != 0) min = decode[c].Min / min;
                if (max != 0) max = decode[c].Max / max;
                decode[c] = new xRange(min, max);
            }

            //Should perhaps apply the decode without converting the colorspace.
            //There's a PdfImage.CreateIntDecodeTable function that would be useful
            //for such.
            for (int c = 0; c < decode.Length; c++)
            {
                if (decode[c].Min > decode[c].Max)
                    force_rgb = true;
            }
            //Note, in case of BlackIsZero images, all needed to be done is to
            //change the photometric to WhiteIsZero. Not bothering for now.
            //Need to test how reversing the decode array messes with things.

            #endregion

            #region Step 4. Prepares the image data
            var tags = new TagList(20, true);
            tags.Set(TagID.ImageWidth, (ulong)image.Width);
            tags.Set(TagID.ImageLength, (ulong)image.Height);

            TiffImage img;

            if (force_rgb)
            {
                tags.Set(TagID.PhotometricInterpretation, (ulong)Photometric.RGB);

                //This table converts colors into something my colorspaces understand. Using a lookuptable
                //effectivly limits this implementation to 16 bits per component on 32-bit systems, but that
                //is also the max for PDF 1.5. (Not that it stops Adobe from opening such files)
                double[,] dlut = image.DecodeLookupTable;

                throw new NotImplementedException();
            }
            else
            {
                tags.Set(TagID.PhotometricInterpretation, (ulong)pm);
                tags.Set(TagID.InkSet, (ulong)ink);
                var bps = new ushort[cs.NComponents];
                Util.ArrayHelper.Fill(bps, (ushort)image.BitsPerComponent);
                tags.Set(TagID.BitsPerSample, bps);
                tags.Set(TagID.SamplesPerPixel, (ulong)bps.Length);

                //Todo: set decode
                tags.Set(TagID.DotRange, DecodeToDotRange(decode, bps));

                byte[] ba;

                switch (format)
                {
                    case ImageFormat.CCITT:
                        PdfFaxParms fax = null;
                        var fa = image.Stream.DecodeParms;
                        for (int c = 0; c < fa.Length; c++)
                        {
                            if (fa[c] is PdfFaxParms)
                            {
                                fax = (PdfFaxParms)fa[c];
                                break;
                            }
                        }
                        if (fax == null) goto default;
                        ba = image.Stream.DecodeTo<PdfFaxFilter>();
                        if (fax.K < 0)
                            tags.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.CCITT_G4);
                        else
                        {
                            tags.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.CCITT_G3);
                            if (fax.K > 0)
                                tags.Set(TagID.T4OPTIONS, T4Options.G32D);
                        }
                        //Yes, this looks backwards. The specs say that when BlackIsZero and compression is
                        //CCITT, the colors are to be reversed. So technically BlackIsZero is BlackIs1 for CCITT
                        tags.Set(TagID.PhotometricInterpretation, (ulong)(fax.BlackIs1 ? Photometric.BlackIsZero : Photometric.WhiteIsZero));
                        break;
                    //case ImageFormat.JBIG2:
                    //    ba = image.Stream.DecodeTo<PdfJBig2Filter>();
                    //    tags.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.JBIG2);
                    //    break;
                    case ImageFormat.JPEG2000:
                        ba = image.Stream.DecodeTo<PdfJPXFilter>();
                        tags.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.JP_2000);
                        break;
                    case ImageFormat.JPEG:
                        ba = image.Stream.DecodeTo<PdfDCTFilter>();
                        tags.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.JPEG);
                        tags.Set(TagID.PhotometricInterpretation, (ulong)Photometric.YCbCr);
                        break;
                    default:
                        if (image.Stream.HasFilter<PdfLZWFilter>() &&
                            image.Stream.Filter.Length == 1)
                        {
                            var dp = image.Stream.DecodeParms;
                            ba = null;
                            if (dp != null && dp.Length > 0 && dp[0] is FlateParams fp)
                            {
                                if (!fp.EarlyChange || fp.Predictor != Pdf.Filter.Predictor.None && fp.Predictor != Pdf.Filter.Predictor.Tiff2)
                                {
                                    //We recompress it into a form tiff understands
                                    //Todo, use TiffPredicor
                                    ba = LZW.Encode(image.Stream.DecodedStream, true);
                                }
                                else if (fp.Predictor == Pdf.Filter.Predictor.Tiff2)
                                {
                                    tags.Set(TagID.Predictor, 2);
                                }
                            }

                            tags.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.LZW);
                            if (ba == null)
                                ba = image.Stream.RawStream;
                        }
                        else
                        {
                            //Todo:
                            //Tiff also got native support for:
                            // Deflate
                            // Tiff Predicor
                            tags.Set(TagID.Compression, (ulong)COMPRESSION_SCHEME.UNCOMPRESSED);
                            ba = image.Stream.DecodedStream;
                        }
                        break;
                }

                tags.Set(TagID.StripByteCounts, new ulong[] { (ulong)ba.Length });
                tags.Set(TagID.StripOffsets, new ulong[] { 0 });
                img = new TiffImage(tags, new byte[][] { ba });
            }

            if (alpha != null)
            {
                alpha.NewSubfileType = ImageType.Mask;
                img.SubIFDs = new TiffImage[] { alpha };
            }

            if (thumbnail != null)
                throw new NotImplementedException();

            #endregion

            return img;
        }

        internal static PdfImage CreateMask(int[] m, PdfImage image)
        {
            //Splits up the color keys into arrays, where each array represent one color
            int n_comps = image.ColorSpace.NComponents;
            int[][] cks = new int[n_comps][];
            for (int c = 0; c < cks.Length; c++)
                cks[c] = new int[] { m[c], m[c + n_comps] };

            //Creates the mask
            int img_width = image.Width, mask_stride = (img_width + 7) / 8;
            int img_height = image.Height;
            byte[] mask_data = new byte[mask_stride * img_height];
            var bw = new BitWriter(mask_data);
            int[][] pixels = image.SepPixelComponents;
            for (int row = 0, pos = 0; row < img_height; row++)
            {
                for (int col = 0; col < img_width; col++, pos++)
                {
                    bool transparent = true;

                    //Looks through all the colors
                    for (int j = 0; j < cks.Length; j++)
                    {
                        var color = pixels[j][pos];
                        var ck = cks[j];
                        if (color < ck[0] || color > ck[1])
                        {
                            transparent = false;
                            break;
                        }
                    }
                    bw.WriteBit(transparent ? 0 : 1);
                }

                //Start of rows must align to byte boundaries.
                bw.Align();
            }

            //No need due to bw.Align();
            //bw.Flush();

            return new PdfImage(mask_data, img_width, img_height);
        }

        /// <summary>
        /// Processes the decode and bps arrays to produce an array of ushort values representing the decoded range for each component. 
        /// Handles invalid ranges, scales the decoded values based on the bits per sample, and optimizes the result if all components 
        /// have the same bits per sample.
        /// 
        /// Basically converts from PDF decode to TIFF decode.
        /// </summary>
        private static ushort[] DecodeToDotRange(xRange[] decode, ushort[] bps)
        {
            if (decode.Length != bps.Length)
                throw new ArgumentException();
            var ret = new ushort[bps.Length * 2];
            ushort dmin = 0, dmax = 0;
            for (int c = 0, pos = 0; c < decode.Length; c++)
            {
                xRange d = decode[c];
                if (d.Min > d.Max)
                    return null;
                double max = (1 << bps[c]) - 1;
                dmin = ret[pos++] = (ushort)(d.Min * max);
                dmax = ret[pos++] = (ushort)(d.Max * max);
            }
            if (Util.ArrayHelper.Same(bps, bps[0]))
            {
                for (int c = 0; c < ret.Length; )
                {
                    if (ret[c++] != dmin || ret[c++] != dmax)
                        return ret;
                }
                return new ushort[] { dmin, dmax };
            }
            return ret;
        }

        internal override void LoadIntoMemory()
        {
            base.LoadIntoMemory();
            if (_image_data != null) return;

            _image_data = RawContents.GetRawData();
            bool is_tile = _tags.ContainsKey(TagID.TileByteCounts);
            var offsets = new ulong[_image_data.Length];
            TagList.Set(_tags, is_tile ? TagID.TileOffsets : TagID.StripOffsets, offsets, BigEndian);
            for (int c = 0; c < _image_data.Length; c++)
                offsets[c] = (ulong) _image_data[c].Length;
            TagList.Set(_tags, is_tile ? TagID.TileByteCounts : TagID.StripByteCounts, offsets, BigEndian);
        }

        public bool InMemory { get { return _image_data != null; } }
        public void LoadFromDisk() { LoadIntoMemory(); }

        #endregion

        #region Transform

        /// <summary>
        /// Creates a new TiffImage with a spesified pixel format
        /// </summary>
        /// <param name="format">Format to transform this image into</param>
        /// <returns>Tiff image in the new format</returns>
        /// <remarks>
        /// This method was made to test Tiff files with unusual pixel formats.
        /// In conclusion, viewers don't support this at all. Some assume that
        /// the ExtraChannel is alpha, others reads the extra channel as normal
        /// data. Also forget formats like those with 3bpc, or 5bpc.
        /// 
        /// Usage: To change to 5551, submit ushort[] { 5, 5, 5, 1 }. You can
        /// also make 5, 6, 5, or whatever bpc up to, I think, 32-bits. 
        /// </remarks>
        public static TiffImage ChangePixelFormat(TiffImage image, ushort[] format)
        {
            var bps = image.BitsPerSample;
            var raw = image.RawContents;
            var comp = image.Compression;
            byte[][] output;
            if (image.PlanarConfiguration == PixelMode.Planar)
            {
                output = new byte[format.Length * raw.NumberOfTiles][];
                
                for (int c = 0, pos = 0; c < format.Length; c++)
                {
                    int new_bpc = format[c];
                    int new_size = (new_bpc * raw.TileWidth + 7) / 8 * raw.TileHeight;

                    for (int t = 0; t < raw.NumberOfTiles; t++)
                    {
                        var tile = raw.GetTile(t);
                        if (c < bps.Length)
                        {
                            var ws = image.MakeStream(tile, c, comp, bps, 1);
                            int org_bpc = bps[c];
                            if (org_bpc == new_bpc)
                                output[pos++] = ws.DecodedStream;
                            else
                            {
                                var br = new BitStream64(ws.DecodedStream);
                                var ba = output[pos++] = new byte[new_size];
                                var bw = new BitWriter(ba);
                                int shift = new_bpc - org_bpc;
                                for (int y = 0; y < tile.Height; y++)
                                {
                                    for (int x = 0; x < raw.TileWidth; x++)
                                    {
                                        if (shift > 0)
                                            bw.Write(br.FetchBits(org_bpc) << shift, new_bpc);
                                        else
                                            bw.Write(br.FetchBits(org_bpc) >> -shift, new_bpc);
                                    }
                                    bw.Align();
                                    br.ByteAlign();
                                }
                            }
                        }
                        else
                        {
                            output[pos++] = new byte[new_size];
                        }
                    }
                }
            }
            else
            {
                output = new byte[raw.NumberOfTiles][];
                int[] read = new int[bps.Length];
                int[] shift = new int[format.Length];
                int[] write = new int[format.Length];
                ulong[] values = new ulong[Math.Max(read.Length, write.Length)];
                for (int c = 0; c < read.Length; c++)
                {
                    if (c < bps.Length)
                    {
                        int bpc = bps[c];
                        read[c] = bpc;
                        if (c < format.Length)
                        {
                            int bpc2 = format[c];
                            write[c] = bpc2;
                            shift[c] = bpc2 - bpc;
                        }
                    }
                    else if (c < format.Length)
                        write[c] = format[c];
                }

                int bpp = Util.ArrayHelper.Sum(format);
                int out_size = (bpp * raw.TileWidth + 7) / 8 * raw.TileHeight;
                for (int c = 0; c < raw.NumberOfTiles; c++)
                {
                    var data = image.MakeStream(raw.GetTile(c), 0, comp, bps, bps.Length).DecodedStream; ;
                    var out_data = output[c] = new byte[out_size];
                    var br = new BitStream64(data);
                    var bw = new BitWriter(out_data);
                    for (int y = 0; y < raw.TileHeight; y++)
                    {
                        for (int x = 0; x < raw.TileWidth; x++)
                        {
                            for (int k = 0; k < read.Length; k++)
                                values[k] = br.FetchBits(read[k]);
                            for (int k = 0; k < write.Length; k++)
                            {
                                int shift_val = shift[k];
                                if (shift_val > 0)
                                    bw.Write(values[k] << shift[k], write[k]);
                                else
                                    bw.Write(values[k] >> -shift[k], write[k]);
                            }
                        }
                        bw.Align();
                        br.ByteAlign();
                    }

                    //Not needed due to bw.Align()
                    //bw.Flush();
                }
            }

            var tags = new TagList(new SortedList<TagID, Tag>(image._tags), image.BigEndian);
            tags.Set(TagID.BitsPerSample, format);
            tags.Set(TagID.SamplesPerPixel, (ulong) format.Length);
            var sizes = new ulong[output.Length];
            for (int c = 0; c < sizes.Length; c++)
                sizes[c] = (ulong) output[c].Length;
            if (image.TileWidth != 0)
                tags.Set(TagID.TileByteCounts, sizes);
            else
                tags.Set(TagID.StripByteCounts, sizes);
            if (format.Length > bps.Length)
            {
                ushort[] es = new ushort[format.Length - bps.Length];
                for (int c = 0; c < es.Length; c++)
                    es[c] = (ushort)SampleType.Unknown;
                tags.Set(TagID.ExtraSamples, es);
            }
            else tags.Tags.Remove(TagID.ExtraSamples);
            return new TiffImage(tags, output);
        }

        private ColorInfo ResolveColorSpace()
        {
            ColorInfo cinfo = new ColorInfo();
            switch (PhotometricInterpretation)
            {
                case Photometric.TransparencyMask:
                case Photometric.BlackIsZero:
                case Photometric.WhiteIsZero:
                    cinfo.ColorSpace = DeviceGray.Instance;
                    cinfo.Jp2_cs = COLOR_SPACE.GRAY;
                    break;
                case Photometric.Palette:
                case Photometric.RGB:
                    cinfo.ColorSpace = DeviceRGB.Instance;
                    cinfo.Jp2_cs = COLOR_SPACE.sRGB;
                    break;
                case Photometric.Separated:
                    if (InkSet == TiffInk.CMYK)
                        cinfo.ColorSpace = DeviceCMYK.Instance;
                    else
                        throw new NotImplementedException("Sepperated color space");
                    break;
                case Photometric.YCbCr:
                    if (Compression == COMPRESSION_SCHEME.old_JPEG)
                    {
                        //Weird and rarely used.
                        throw new TiffIsOldJpegException();
                    }
                    //This colorspace is not supported nativly by PDF, but works with JP2K images
                    cinfo.ColorSpace = new JPXYCC();
                    cinfo.Decompress = true;
                    cinfo.C2P = new ToPlanarFunc(SepperateYCC);
                    cinfo.P2IC = new ToCompsFunc(SepperateYCC);
                    cinfo.Jp2_cs = COLOR_SPACE.sYCC;
                    break;
                case Photometric.CIELAB:
                    cinfo.ColorSpace = new LabCS(0.9643, 0.8251)
                    {
                        Range = new LabRange(-127, 127, -127, 127),
                        BlackPoint = new LabPoint(0.9643, 1, 0.8251)
                    };
                    cinfo.Decompress = true;
                    cinfo.C2P = new ToPlanarFunc(LabSgnComps);
                    cinfo.P2IC = new ToCompsFunc(LabSgnComps);
                    cinfo.Jp2_cs = COLOR_SPACE.CIELab;
                    cinfo.IncludeCS = true;
                    break;
                default:
                    throw new NotImplementedException("PhotometricInterpretation");
            }
            if (Indexed)
            {
                if (cinfo.ColorSpace is DeviceRGB)
                {
                    var map = ColorMap;
                    var cs_map = new byte[map.Length];
                    int higval = (int)Math.Pow(2, ArrayHelper.Max(BitsPerSample));
                    if (higval > 256)
                        //Pdf does not support this, and outside test cases files like these are rare.
                        throw new TiffNotImplementedException("Palette greater than 8-bit");
                    for (int r = 0, g = higval, b = higval * 2, pos = 0; r < higval; )
                    {
                        cs_map[pos++] = (byte)(map[r++] / 257);
                        cs_map[pos++] = (byte)(map[g++] / 257);
                        cs_map[pos++] = (byte)(map[b++] / 257);
                    }
                    cinfo.ColorSpace = new IndexedCS(cs_map);
                }
                else
                    throw new NotImplementedException("Palette on non-rgb images");
            }
            var dot_range = DotRange;
            if (PhotometricInterpretation == Photometric.WhiteIsZero)
            {
                var compression = Compression;
                if (compression < COMPRESSION_SCHEME.CCITT_MOD_HUFF_RLE || compression > COMPRESSION_SCHEME.CCITT_G4)
                {
                    if (dot_range != null)
                    {
                        for (int c = 0; c < dot_range.Length; c++)
                            dot_range[c] = new xRange(dot_range[c].Max, dot_range[c].Min);
                    }
                    else
                        dot_range = new xRange[] { new xRange(1, 0) };
                }
            }

            if (dot_range != null)
            {
                var def = cinfo.ColorSpace.DefaultDecode;
                if (dot_range.Length == 1 && def.Length > 2)
                {
                    Array.Resize<xRange>(ref dot_range, def.Length / 2);
                    double min = dot_range[0].Min, max = dot_range[0].Max;
                    for (int c = 1; c < dot_range.Length; c++)
                        dot_range[c] = new xRange(min, max);
                }

                if (ArrayHelper.ArraysEqual<double>(def, xRange.ToArray(dot_range)))
                    dot_range = null;
            }
            cinfo.Decode = dot_range;
            return cinfo;
        }

        #endregion

        #region ToPDF

        /// <summary>
        /// Converts this TiffImage into a PdfXObject
        /// </summary>
        /// <param name="force_image">Not fully implemented, can still ret PdfForm</param>
        /// <returns>A PdfImage or PdfForm</returns>
        public PdfXObject ConvertToXObject(bool force_image)
        {
            TiffRawContents img = RawContents;
            TiffTile[,] data = img.RawData;
            ColorInfo cinfo = ResolveColorSpace();

            PdfImage pdf_img;
            int bpc = BitsPerComponent;
            if (img.NumberOfTiles == 1)
                pdf_img = ConvertTileToPdfImage(data[0, 0], cinfo);
            else
            {
                if (bpc == 0 || cinfo.ColorSpace is JPXYCC)
                {
                    if (force_image)
                        return Decompress(true).ConvertToXObject(true);
                    return MakeForm(data, img, cinfo);
                }

                //Can be decompressed/recompressed
                int bpp = BitsPerPixel;
                int stride = (bpp * Width + 7) / 8;
                long total_length = stride * Height; //If bpp is bigger than ncomp, we probably got alpha. Code below can't handle that.
                if (total_length >= int.MaxValue || bpp > cinfo.ColorSpace.NComponents * bpc)
                {
                    if (force_image)
                        return Decompress(true).ConvertToXObject(true);
                    return MakeForm(data, img, cinfo);
                }

                switch (Compression)
                {
                    case COMPRESSION_SCHEME.PACKBITS:
                    case COMPRESSION_SCHEME.UNCOMPRESSED:
                    case COMPRESSION_SCHEME.CCITT_G3:
                    case COMPRESSION_SCHEME.CCITT_G4:
                    case COMPRESSION_SCHEME.CCITT_MOD_HUFF_RLE:
                        break;
                    default:
                        if (force_image)
                            break;
                        return MakeForm(data, img, cinfo);
                }
                xRange[] dot_range = cinfo.Decode;
                cinfo.Decode = null;

                var ba = new byte[total_length];
                if (img.TilesAcross == 1 && (bpp % 8 == 0 || (bpp * Width) % 8 == 0))
                {
                    for (int y = 0, pos = 0; y < img.TilesDown; y++)
                    {
                        var tile = data[0, y];
                        var pdf_image = ConvertTileToPdfImage(tile, cinfo);
                        byte[] source = pdf_image.Stream.DecodedStream;
                        if (source == null)
                        {
                            cinfo.Decode = dot_range;
                            return MakeForm(data, img, cinfo);
                        }
                        int l = (tile.Width * bpp + 7) / 8 * tile.Height;
                        Buffer.BlockCopy(source, 0, ba, pos, Math.Min(l, source.Length));
                        pos += l;
                    }
                }
                else
                {
                    var bw = new BitWriter(ba);
                    int read = (bpp > BitStream64.MAX) ? bpc : bpp;
                    var br = new BitStream64[img.TilesAcross];
                    var row_read = new int[img.TilesAcross, 2];

                    for (int tile_y = 0; tile_y < img.TilesDown; tile_y++)
                    {
                        //Creates bitstreams for one row of tiles
                        for (int tile_x = 0; tile_x < img.TilesAcross; tile_x++)
                        {
                            var tile = data[tile_x, tile_y];
                            int width = tile.Width * bpp;
                            int bits_to_read = read;
                            if (bits_to_read < 32 && width % 32 == 0)
                                bits_to_read = 32;

                            row_read[tile_x, 0] = width / bits_to_read;
                            row_read[tile_x, 1] = bits_to_read;
                            var pdf_image = ConvertTileToPdfImage(tile, cinfo);
                            byte[] source = pdf_image.Stream.DecodedStream;
                            if (source == null)
                            {
                                cinfo.Decode = dot_range;
                                return MakeForm(data, img, cinfo);
                            }
                            br[tile_x] = new BitStream64(source);
                        }

                        int height = data[0, tile_y].Height;

                        //Writes out one whole row at a time
                        for (int y = 0; y < height; y++)
                        {
                            for (int c = 0; c < br.Length; c++)
                            {
                                BitStream64 bs = br[c];
                                int max = row_read[c, 0], bread = row_read[c, 1];

                                for (int nr = 0; nr < max; nr++)
                                {
                                    if (!bs.HasBits(bread))
                                    {
                                        cinfo.Decode = dot_range;
                                        return MakeForm(data, img, cinfo);
                                    }
                                    bw.Write(bs.PeekBits(bread), bread);
                                    bs.ClearBits(bread);
                                }

                                bs.ByteAlign();
                            }

                            bw.Align();
                        }
                    }

                    //Not needed due to bw.Align();
                    //bw.Dispose();
                }

                pdf_img = new PdfImage(ba, cinfo.ColorSpace, Width, Height, bpc);
                if (dot_range != null)
                    pdf_img.Decode = dot_range;
            }
            if (bpc > 16)
                //Don't know what's going on, but 24 bits per pixel don't fly for some reason.
                // Pdglib runs out of memory, though it might work with 64-bit. Jpeg 2000 works
                // on PdfLib because it creates smaller lookuptables when decoding jp2.
                // Adobe throws an error and Sumatra shows nothing (but both works with jp2). 
                pdf_img = JPX.CompressPDF(pdf_img, 100);
            var sub_images = SubIFDs;
            if (sub_images != null)
            {
                foreach (var sub_img in sub_images)
                {
                    if ((sub_img.NewSubfileType & ImageType.Mask) != 0)
                    {
                        var mask = sub_img.ConvertToXObject(true);
                        if (mask is PdfImage)
                        {
                            var mask_img = (PdfImage)mask;
                            if (mask_img.BitsPerComponent == 1 && mask_img.ColorSpace == DeviceGray.Instance)
                            {
                                mask_img.ImageMask = true;
                                pdf_img.Mask = mask_img;
                            }
                            else
                                pdf_img.SMask = mask_img;
                        }
                    }
                }
            }

            return pdf_img;
        }

        /// <summary>
        /// Converts TiffImage into a PdfForm
        /// </summary>
        private PdfForm MakeForm(TiffTile[,] tiles, TiffRawContents img, ColorInfo cs)
        {
            var form = new PdfForm(Width, Height);
            using (var draw = new cDraw(form))
            {
                double x_pos = 0, y_pos = Height;
                for (int y = 0; y < img.TilesDown; y++)
                {
                    y_pos -= tiles[0, y].Height;

                    for (int x = 0; x < img.TilesAcross; x++)
                    {
                        var tile = tiles[x, y];
                        draw.DrawImage(x_pos, y_pos, ConvertTileToPdfImage(tile, cs));
                        x_pos += tile.Width;
                    }

                    x_pos = 0;
                }
            }
            return form;
        }

        /// <summary>
        /// Converts a tile to an image. Does not flip or turn it. 
        /// </summary>
        /// <param name="tile">Tile to convert</param>
        /// <returns>PdfImage</returns>
        private PdfImage ConvertTileToPdfImage(TiffTile tile, ColorInfo cinfo)
        {
            PdfImage pdf_image;

            if (tile.Data.Length > 1)
                return ConvertPlanarTileToPdfImage(tile, cinfo);

            IColorSpace cs = cinfo.ColorSpace;
            var comp = Compression;
            if (comp == COMPRESSION_SCHEME.JP_2000 || comp == COMPRESSION_SCHEME.JPEG)
            {
                byte[] data = tile.Data[0];

                var tables = JPEGTables;
                if (tables != null)
                {
                    //http://www.remotesensing.org/libtiff/TIFFTechNote2.html
                    //Quick and dirty attempt:
                    //int t_len = tables.Length - 2; //Removes the end marker
                    //Array.Resize<byte>(ref tables, t_len + data.Length - 2); //Makes room for data, except start marker
                    //Buffer.BlockCopy(data, 2, tables, t_len, data.Length - 2); //Copies data without start marker
                    //data = tables; //Does not work. Meh. Not in the mood to actually look into what's needed to be done.
                    throw new TiffNotImplementedException("JPEGTables");
                }

                //AFAIK, all the meta data is in the JP header, so we can ignore the tiff stuff
                var jp2_img = PdfImage.Create(data);
                if (cinfo.Decode != null) jp2_img.Decode = cinfo.Decode;
                return jp2_img;
            }

            var bpc_ar = BitsPerSample;
            int bpc = bpc_ar[0], bpp;
            bool non_uniform = !IsBPCUniform(bpc_ar, out bpp, bpc);

            if (!cinfo.Decompress)
            {
                cinfo.Decompress = non_uniform;

                if (comp == COMPRESSION_SCHEME.CCITT_MOD_HUFF_RLE ||
                    bpc > 8 && !_file.BigEndian && comp != COMPRESSION_SCHEME.JPEG)
                    cinfo.Decompress = true;

                if (bpc_ar.Length != cs.NComponents)
                {
                    cinfo.Decompress = true;
                }
            }
            WritableStream ws = MakeByteAlignedStream(tile, 0, comp, bpc_ar, bpc_ar.Length);

            PdfImage soft_mask = null;
            double[] matte = null;
            if (cinfo.Decompress)
            {
                byte[] data = ws.DecodedStream;

                if (bpc > 15 && !BigEndian)
                {
                    data = ByteSwap(data, tile.Width, tile.Height, bpc);
                }

                if (bpc_ar.Length > cs.NComponents)
                {
                    //Must extract the color channels.
                    int n_comp = cs.NComponents, alpha_channel = n_comp;
                    var extra = ExtraSamples;
                    for (int c = 0; c < extra.Length; c++)
                    {
                        var type = extra[c];
                        if (type == SampleType.Alpha || type == SampleType.PreMultimpiedAlpha)
                        {
                            alpha_channel += c;
                            break;
                        }
                    }
                    //Note, if no alpha channel is found, bytes for whatever other channel is present will be
                    //extracted instead, then discarted.
                    byte[] alpa_bytes = new byte[(bpc_ar[alpha_channel] * tile.Width + 7) / 8 * tile.Height];

                    int bits = 0;
                    for (int c = n_comp - 1; c >= 0; c--)
                        bits += bpc_ar[c];
                    byte[] color_bytes = new byte[(bits * tile.Width + 7) / 8 * tile.Height];

                    var br = new BitStream64(data);
                    var bw_color = new BitWriter(color_bytes);
                    var bw_alpha = new BitWriter(alpa_bytes);

                    for (int y = 0; y < tile.Height; y++)
                    {
                        for (int x = 0; x < tile.Width; x++)
                        {
                            for (int cp = 0; cp < bpc_ar.Length; cp++)
                            {
                                int current_bpc = bpc_ar[cp];
                                if (!br.HasBits(current_bpc))
                                    throw new TiffReadException("Unexpected end of data");
                                if (cp < n_comp)
                                    bw_color.Write(br.PeekBits(current_bpc), current_bpc);
                                else if (cp == alpha_channel)
                                    bw_alpha.Write(br.PeekBits(current_bpc), current_bpc);
                                br.ClearBits(current_bpc);
                            }
                        }
                    }

                    bw_color.Dispose();
                    bw_alpha.Dispose();

                    data = color_bytes;

                    if (extra[alpha_channel - n_comp] == SampleType.Alpha)
                        soft_mask = new PdfImage(alpa_bytes, DeviceGray.Instance, tile.Width, tile.Height, bpc_ar[alpha_channel]);
                    else if (extra[alpha_channel - n_comp] == SampleType.PreMultimpiedAlpha)
                    {
                        soft_mask = new PdfImage(alpa_bytes, DeviceGray.Instance, tile.Width, tile.Height, bpc_ar[alpha_channel]);
                        matte = new double[n_comp];
                    }
                }

                //Jpeg 2000 compress
                if (non_uniform)
                    throw new TiffNotImplementedException("Non uniform images");

                if (cinfo.C2P != null)
                {
                    var comps = cinfo.P2IC(tile, cinfo.C2P(tile, data));
                    var jpx_image = new JPXImage(0, tile.Width, 0, tile.Height, comps, cinfo.Jp2_cs);
                    byte[] ms = JPX.Compress(jpx_image, 100);
                    //System.IO.File.WriteAllBytes("C:/temp/test.jp2", ms);
                    pdf_image = PdfImage.Create(ms);
                    if (cinfo.IncludeCS)
                        pdf_image.ColorSpace = cinfo.ColorSpace;
                    goto add_mask;

                }
                else
                    ws = new WritableStream(data);
            }

            pdf_image = new PdfImage(ws, cs, null, tile.Width, tile.Height, bpc, PdfAlphaInData.None);
        add_mask:
            if (soft_mask != null)
            {
                pdf_image.SMask = soft_mask;
                if (matte != null)
                    pdf_image.Matte = matte;
            }
            if (cinfo.Decode != null)
                pdf_image.Decode = cinfo.Decode;
            return pdf_image;
        }

        private PdfImage ConvertPlanarTileToPdfImage(TiffTile tile, ColorInfo cinfo)
        {
            PdfImage pdf_image;
            IColorSpace cs = cinfo.ColorSpace;

            var comp = Compression;
            if (comp == COMPRESSION_SCHEME.JP_2000 || comp == COMPRESSION_SCHEME.JPEG)
                throw new TiffNotImplementedException("Planar jpeg file");

            var bpc_ar = BitsPerSample;
            int bpc = bpc_ar[0], bpp;
            bool non_uniform = !IsBPCUniform(bpc_ar, out bpp, bpc);          

            var data = new byte[bpc_ar.Length][];
            for (int c = 0; c < bpc_ar.Length; c++)
                data[c] = MakeByteAlignedStream(tile, c, comp, bpc_ar, 1).DecodedStream;

            if (bpc > 8 && !BigEndian)
            {
                for(int c=0; c < data.Length; c++)
                    data[c] = ByteSwap(data[c], tile.Width, tile.Height, bpc);
            }

            PdfImage soft_mask = null;
            double[] matte = null;
            if (bpc_ar.Length > cs.NComponents)
            {
                int n_comp = cs.NComponents, alpha_channel = n_comp;
                var extra = ExtraSamples;
                for (int c = 0; c < extra.Length; c++)
                {
                    var type = extra[c];
                    if (type == SampleType.Alpha || type == SampleType.PreMultimpiedAlpha)
                    {
                        alpha_channel += c;
                        break;
                    }
                }
                //Note, if no alpha channel is found, bytes for whatever other channel is present will be
                //extracted instead, then discarted.
                byte[] alpa_bytes = data[alpha_channel];

                byte[][] color_bytes = new byte[n_comp][];
                Array.Copy(data, color_bytes, n_comp);

                data = color_bytes;

                //Alternativly, include this in the jp2 data. 
                if (extra[alpha_channel - n_comp] == SampleType.Alpha)
                    soft_mask = new PdfImage(alpa_bytes, DeviceGray.Instance, tile.Width, tile.Height, bpc_ar[alpha_channel]);
                else if (extra[alpha_channel - n_comp] == SampleType.PreMultimpiedAlpha)
                {
                    soft_mask = new PdfImage(alpa_bytes, DeviceGray.Instance, tile.Width, tile.Height, bpc_ar[alpha_channel]);
                    matte = new double[n_comp];
                }
            }

            ImageComp[] comps;
            if (cinfo.P2IC != null)
            {
                var ia_data = new int[data.Length][];
                for(int c=0; c < ia_data.Length; c++)
                    ia_data[c] = baToia(data[c], bpc_ar[c], tile.Width, tile.Height);
                comps = cinfo.P2IC(tile, ia_data);
            }
            else
            {
                comps = new ImageComp[cs.NComponents];
                for (int c = 0; c < comps.Length; c++)
                    comps[c] = new ImageComp(bpc_ar[c], bpc_ar[c], false, 1, 1, tile.Width, tile.Height, baToia(data[c], bpc_ar[c], tile.Width, tile.Height));
            }

            if (non_uniform)
            {
                var jpx_image = new JPXImage(0, tile.Width, 0, tile.Height, comps, cinfo.Jp2_cs);
                byte[] ms = JPX.Compress(jpx_image, 100);
                //System.IO.File.WriteAllBytes("C:/temp/test.jp2", ms);
                pdf_image = PdfImage.Create(ms);

                if (cinfo.IncludeCS)
                    pdf_image.ColorSpace = cinfo.ColorSpace;
            }
            else
            {
                int stride_8 = (tile.Width * bpp + 7) / 8;
                byte[] chunky = new byte[stride_8 * tile.Height];
                var bw = new BitWriter(chunky);

                for(int y = 0, pos = 0; y < tile.Height; y++)
                {
                    for (int x=0; x < tile.Width; x++, pos++)
                    {
                        for(int c =0; c < comps.Length; c++)
                            bw.Write(comps[c].Data[pos], bpc);
                    }
                    bw.Align();
                }
                bw.Flush();
                pdf_image = new PdfImage(chunky, cinfo.ColorSpace, tile.Width, tile.Height, bpc);
            }

            if (soft_mask != null)
            {
                pdf_image.SMask = soft_mask;
                if (matte != null)
                    pdf_image.Matte = matte;
            }
            if (cinfo.Decode != null)
                pdf_image.Decode = cinfo.Decode;
            return pdf_image;
        }

        #endregion

        #region Compression

        /// <summary>
        /// Function for recompressing data in tiff files
        /// </summary>
        /// <param name="tile">Tile information</param>
        /// <param name="sample_nr">What sample the data covers, -1 if all samples</param>
        /// <param name="data">Data decompressor. The data is also in Tile.Data.</param>
        /// <returns>Compressed data</returns>
        public delegate byte[] CompressFunc(TiffTile tile, int sample_nr, IStream data);

        /// <summary>
        /// Compresses data using built in functions.
        /// </summary>
        /// <param name="format">Format to compress the data into</param>
        /// <param name="quality">Quality, if applicable (0-100)</param>
        /// <returns>Compressed image</returns>
        public TiffImage Compress(COMPRESSION_SCHEME format, int quality)
        {
            ColorInfo cinfo;
            TiffImage img, img2;
            switch (format)
            {
                case COMPRESSION_SCHEME.DEFLATE:
                    return Compress(format, (tile, samples, data) =>
                    {
                        return PdfFlateFilter.Encode(data.DecodedStream);
                    });
                case COMPRESSION_SCHEME.JPEG:
                    //Images with lots of strips compress badly, and common tiff
                    //viewers has trouble opening them. Therefore we flatten the
                    //data.
                    img = Decompress(true);
                    cinfo = ResolveColorSpace();
                    return img.Compress(format, (tile, samples, data) =>
                    {
                        var pdf_image = img.ConvertTileToPdfImage(tile, cinfo);
                        pdf_image = Jpeg.CompressPDF(pdf_image, quality, quality, false, false, true);
                        return pdf_image.Stream.RawStream;
                    });
                case COMPRESSION_SCHEME.JP_2000:
                    cinfo = ResolveColorSpace();
                    return Compress(format, (tile, samples, data) =>
                    {
                        var pdf_image = ConvertTileToPdfImage(tile, cinfo);
                        pdf_image = JPX.CompressPDF(pdf_image, quality, quality, JPX.PDF2JP2Image.FORCE);
                        return pdf_image.Stream.RawStream;
                    });
                case COMPRESSION_SCHEME.PACKBITS:
                    return Compress(format, (tile, samples, data) =>
                    {
                        return PdfRunLengthFilter.Encode(data.DecodedStream);
                    });
                case COMPRESSION_SCHEME.WHITE_CCITT_G3:
                case COMPRESSION_SCHEME.CCITT_G3:
                    if (BitsPerPixel != 1) throw new NotSupportedException("CCITT compression only works on 1bpp images");
                    if (Indexed || format == COMPRESSION_SCHEME.CCITT_G3)
                    {
                        img = Compress(COMPRESSION_SCHEME.CCITT_G3, (tile, samples, data) =>
                        {
                            return Img.CCITTEncoder.EncodeG31D(data.DecodedStream, tile.Width, tile.Height, false, false);
                        });
                        if (img.Indexed) return img;
                        img.PhotometricInterpretation = Photometric.BlackIsZero;
                    }
                    else img = null;
                    img2 = Compress(COMPRESSION_SCHEME.CCITT_G3, (tile, samples, data) =>
                    {
                        return Img.CCITTEncoder.EncodeG31D(data.DecodedStream, tile.Width, tile.Height, true, false);
                    });
                    img2.PhotometricInterpretation = Photometric.WhiteIsZero;
                    if (format == COMPRESSION_SCHEME.WHITE_CCITT_G3) return img2;
                    return img.RawContents.TotalSize <= img2.RawContents.TotalSize ? img : img2;
                case COMPRESSION_SCHEME.WHITE_CCITT_G4:
                case COMPRESSION_SCHEME.CCITT_G4:
                    if (BitsPerPixel != 1) throw new NotSupportedException("CCITT compression only works on 1bpp images");
                    if (Indexed || format == COMPRESSION_SCHEME.CCITT_G4)
                    {
                        img = Compress(COMPRESSION_SCHEME.CCITT_G4, (tile, samples, data) =>
                        {
                            return Img.CCITTEncoder.EncodeG4(data.DecodedStream, tile.Width, tile.Height, false, false, false);
                        });
                        if (img.Indexed) return img;
                        img.PhotometricInterpretation = Photometric.BlackIsZero;
                    }
                    else img = null;
                    img2 = Compress(COMPRESSION_SCHEME.CCITT_G4, (tile, samples, data) =>
                    {
                        return Img.CCITTEncoder.EncodeG4(data.DecodedStream, tile.Width, tile.Height, true, false, false);
                    });
                    img2.PhotometricInterpretation = Photometric.WhiteIsZero;
                    if (format == COMPRESSION_SCHEME.WHITE_CCITT_G4) return img2;
                    return img.RawContents.TotalSize <= img2.RawContents.TotalSize ? img : img2;
                case COMPRESSION_SCHEME.UNCOMPRESSED:
                    return Compress(format, (tile, samples, data) =>
                    {
                        return data.DecodedStream;
                    });
            }
            return this;
        }

        /// <summary>
        /// Decompresses the image
        /// </summary>
        /// <param name="flatten">Removes tiles and strips</param>
        /// <returns>Decompressed image</returns>
        public TiffImage Decompress(bool flatten)
        {
            var img = Compression != COMPRESSION_SCHEME.UNCOMPRESSED ? Compress(COMPRESSION_SCHEME.UNCOMPRESSED, 0) : this;
            byte[][] ba;
            if (flatten && (img.RowsPerStrip > 0 || img.TileWidth != 0))
            {
                var raw = img.RawContents;
                if (img.PhotometricInterpretation == Photometric.YCbCr)
                {
                    var sub = img.YCbCrSubSampling;
                    if (sub.Horizontal > SUBSAMPLING.None || sub.Vertical > SUBSAMPLING.None)
                    {
                        #region Converts to planar
                        if (img.PlanarConfiguration == PixelMode.Chunky)
                        {
                            var planar_ba = new byte[raw.NumberOfTiles * 3][];
                            for (int nr = 0; nr < raw.NumberOfTiles; nr++)
                            {
                                var tile = raw.GetTile(nr);
                                var planar = SepperateYCC(tile, tile.Data[0]);

                                //Todo: Needs to support BPP != 8.
                                planar_ba[nr] = Util.ArrayHelper.TransformToByte(planar[0]);
                                planar_ba[nr + raw.NumberOfTiles] = Util.ArrayHelper.TransformToByte(planar[1]);
                                planar_ba[nr + raw.NumberOfTiles * 2] = Util.ArrayHelper.TransformToByte(planar[2]);
                            }
                            img._image_data = planar_ba;
                            TagList.Set(img._tags, TagID.PlanarConfiguration, (ulong)PixelMode.Planar, img.BigEndian);
                            var offsets = new ulong[planar_ba.Length];
                            bool is_tile = img._tags.ContainsKey(TagID.TileOffsets);
                            TagList.Set(img._tags, is_tile ? TagID.TileOffsets : TagID.StripOffsets, offsets, img.BigEndian);
                            for (int c = 0; c < offsets.Length; c++)
                                offsets[c] = (ulong)planar_ba[c].Length;
                            TagList.Set(img._tags, is_tile ? TagID.TileByteCounts : TagID.StripByteCounts, offsets, img.BigEndian);
                            raw = img.RawContents;
                        }
                        #endregion

                        #region Combines tiles into one image

                        ba = new byte[raw.SamplesPerPixel][];

                        var tiles = raw.RawData;

                        #region Calculates size of YCC
                        var samples = YCbCrSubSampling;
                        int c_width = raw.ImageWidth;
                        if (samples.Horizontal > SUBSAMPLING.None)
                            c_width = c_width / (int)samples.Horizontal + (raw.ImageWidth % (int)samples.Horizontal != 0 ? 1 : 0);
                        int c_height = raw.ImageHeight;
                        if (samples.Vertical > SUBSAMPLING.None)
                            c_height = c_height / (int)samples.Vertical + (raw.ImageHeight % (int)samples.Vertical != 0 ? 1 : 0);
                        ba[0] = new byte[(raw.ImageWidth * raw.BitsPerSample[0] + 7) / 8 * raw.ImageHeight];
                        ba[1] = new byte[(c_width * raw.BitsPerSample[1] + 7) / 8 * c_height];
                        ba[2] = new byte[(c_width * raw.BitsPerSample[2] + 7) / 8 * c_height];
                        for (int c = 3; c < ba.Length; c++)
                            ba[c] = new byte[ba[0].Length];
                        int[] div_x = new int[ba.Length], div_y = new int[ba.Length];
                        Util.ArrayHelper.Fill(div_x, 1);
                        Util.ArrayHelper.Fill(div_y, 1);
                        div_x[1] = div_x[2] = (int)samples.Horizontal;
                        div_y[1] = div_y[2] = (int)samples.Vertical;
                        #endregion

                        #region Copies the data
                        for (int c = 0; c < raw.BitsPerSample.Length; c++)
                        {
                            var bytes = ba[c];
                            int bps = raw.BitsPerSample[c];

                            int down = raw.TilesDown - 1, divx = div_x[c], divy = div_y[c], pos = 0;
                            for (int y = 0; y < down; y++)
                            {
                                for (int x = 0; x < raw.TilesAcross; x++)
                                {
                                    var tile = tiles[x, y];
                                    var data = tile.Data[c];
                                    int add = tile.Height % divy != 0 ? tile.Height : 0;
                                    int tile_size = (tile.Width / divx * bps + 7) / 8 * tile.Height / divy + add;
                                    Buffer.BlockCopy(data, 0, bytes, pos, Math.Min(tile_size, data.Length));
                                    pos += tile_size;
                                }
                            }

                            for (int x = 0; x < raw.TilesAcross; x++)
                            {
                                var tile = tiles[x, down];
                                var data = tile.Data[c];
                                int add_y = tile.Height % divy != 0 ? 1 : 0, add_x = tile.Height % divy != 0 ? 1 : 0;
                                int tile_size = ((tile.Width / divx * bps + 7) / 8 + add_x) * (tile.Height / divy + add_y);
                                Buffer.BlockCopy(data, 0, bytes, pos, Math.Min(tile_size, data.Length));
                                pos += tile_size;
                            }
                        }
                        #endregion

                        #endregion

                        goto end;
                    }
                }

                if (img.PlanarConfiguration == PixelMode.Chunky)
                {
                    var bytes = new byte[(raw.BitsPerPixel * raw.ImageWidth + 7) / 8 * raw.ImageHeight];
                    for (int y = 0, nr = 0, pos = 0; y < raw.TilesDown; y++)
                    {
                        for (int x = 0; x < raw.TilesAcross; x++)
                        {
                            var tile = raw.GetTile(nr++);
                            var data = tile.Data[0];
                            int tile_size = (tile.Width * raw.BitsPerPixel + 7) / 8 * tile.Height;
                            Buffer.BlockCopy(data, 0, bytes, pos, Math.Min(tile_size, data.Length));
                            pos += tile_size;
                        }
                    }
                    ba = new byte[][] { bytes };
                }
                else
                {
                    ba = new byte[raw.SamplesPerPixel][];
                    var tiles = raw.RawData;
                    for(int c=0; c < raw.BitsPerSample.Length; c++)
                    {
                        var bytes = ba[c] = new byte[(raw.BitsPerSample[c] * raw.ImageWidth + 7) / 8 * raw.ImageHeight];

                        for (int y = 0, pos = 0; y < raw.TilesDown; y++)
                        {
                            for (int x = 0; x < raw.TilesAcross; x++)
                            {
                                var tile = tiles[x, y];
                                var data = tile.Data[c];
                                int tile_size = (tile.Width * raw.BitsPerSample[c] + 7) / 8 * tile.Height;
                                Buffer.BlockCopy(data, 0, bytes, pos, Math.Min(tile_size, data.Length));
                                pos += tile_size;
                            }
                        }
                    }
                }

            end:
                img._image_data = ba;
                img._tags.Remove(TagID.TileByteCounts);
                img._tags.Remove(TagID.TileOffsets);
                img._tags.Remove(TagID.TileWidth);
                img._tags.Remove(TagID.TileLength);
                img._tags.Remove(TagID.RowsPerStrip);
                ulong[] bc = new ulong[ba.Length];
                for (int c = 0; c < ba.Length; c++)
                    bc[c] = (ulong)ba[c].Length;
                TagList.Set(img._tags, TagID.StripByteCounts, bc, img.BigEndian);
                TagList.Set(img._tags, TagID.StripOffsets, new ulong[ba.Length], img.BigEndian);
            }
            return img;
        }

        public TiffImage Compress(COMPRESSION_SCHEME format, CompressFunc compressor)
        {
            var raw = RawContents;
            byte[][] data;
            var comp = Compression;
            if (PlanarConfiguration == PixelMode.Chunky)
            {
                data = new byte[raw.NumberOfTiles][];

                for (int c = 0; c < data.Length; c++)
                {
                    var tile = raw.GetTile(c);
                    data[c] = compressor(tile, -1, MakeStream(tile, 0, comp, raw.BitsPerSample, raw.SamplesPerPixel));
                }
            }
            else
            {
                data = new byte[raw.NumberOfTiles * raw.SamplesPerPixel][];

                for (int c = 0, pos = 0; c < raw.NumberOfTiles; c++)
                {
                    var tile = raw.GetTile(c);
                    for(int nr=0; nr < raw.SamplesPerPixel; nr++)
                        data[pos++] = compressor(tile, nr, MakeStream(tile, nr, comp, raw.BitsPerSample, 1));
                }
            }

            var tags = new TagList(new SortedList<TagID, Tag>(_tags), BigEndian);
            var counts = new ulong[data.Length];
            for (int c = 0; c < data.Length; c++)
                counts[c] = (ulong)data[c].Length;
            tags.Set(TagID.StripByteCounts, counts);
            tags.Set(TagID.StripOffsets, new ulong[data.Length]);
            tags.Set(TagID.Compression, (ulong) format);
            switch (Compression)
            {
                case COMPRESSION_SCHEME.DEFLATE:
                case COMPRESSION_SCHEME.ADOBE_DEFLATE:
                case COMPRESSION_SCHEME.LZW:
                    tags.Tags.Remove(TagID.Predictor);
                    break;
                case COMPRESSION_SCHEME.JPEG:
                    tags.Tags.Remove(TagID.JPEGTables);
                    if (SamplesPerPixel == 1)
                        tags.Set(TagID.PhotometricInterpretation, (ulong)Photometric.BlackIsZero);
                    else
                        tags.Set(TagID.PhotometricInterpretation, (ulong)Photometric.RGB);
                    break;
                case COMPRESSION_SCHEME.CCITT_G3:
                case COMPRESSION_SCHEME.CCITT_G4:
                    tags.Tags.Remove(TagID.FILLORDER);
                    tags.Tags.Remove(TagID.T4OPTIONS);
                    tags.Tags.Remove(TagID.T6OPTIONS);
                    tags.Set(TagID.PhotometricInterpretation, (ulong)Photometric.BlackIsZero);
                    break;
            }
            switch (format)
            {
                case COMPRESSION_SCHEME.JPEG:
                    if (SamplesPerPixel == 1)
                        tags.Set(TagID.PhotometricInterpretation, (ulong)Photometric.BlackIsZero);
                    else
                        tags.Set(TagID.PhotometricInterpretation, (ulong)Photometric.YCbCr);
                    //LibJpeg supports only 8 bpc. (Jpeg supports 8 and 12)
                    tags.Set(TagID.BitsPerSample, new ushort[] { 8, 8, 8 });
                    break;
            }
            return new TiffImage(tags, data);
        }

        #endregion

        #region Utility

        /// <summary>
        /// Converts a byte array to an integer array based on bits per component (bpc), width, and height.
        /// </summary>
        /// <param name="bytes">The byte array to convert.</param>
        /// <param name="bpc">Bits per component.</param>
        /// <param name="width">The width of the image.</param>
        /// <param name="height">The height of the image.</param>
        /// <returns>An integer array representing the image data.</returns>
        private int[] baToia(byte[] bytes, int bpc, int width, int height)
        {
            int row_bits = (width * bpc);
            int total_ints = (row_bits * height + (bpc - 1)) / bpc;
            int[] ints = new int[total_ints];

            if (bpc == 8)
                Array.Copy(bytes, ints, ints.Length);
            else
            {
                var bs = new BitStream64(bytes);
                for (int c = 0; c < ints.Length; )
                {
                    for (int r = 0; r < width && c < ints.Length; r++)
                        ints[c++] = (int)bs.FetchBits(bpc);
                    bs.ByteAlign();
                }
            }

            return ints;
        }

        /// <summary>
        /// Checks if the bits per component (bpc) array is uniform and calculates the total bits per pixel (bpp).
        /// </summary>
        /// <param name="bpc_ar">Array of bits per component values.</param>
        /// <param name="bpp">Output parameter for the total bits per pixel.</param>
        /// <param name="bpc">Expected bits per component value.</param>
        /// <returns>True if the bpc array is uniform, otherwise false.</returns>
        private bool IsBPCUniform(ushort[] bpc_ar, out int bpp, int bpc)
        {
            bpp = 0;
            for (int c = 0; c < bpc_ar.Length; c++)
            {
                int bpc_val = bpc_ar[c];
                bpp += bpc_val;
                if (bpc_val != bpc)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Creates a writable stream for a given TIFF tile using the specified compression scheme.
        /// The writable stream is intended to be used in a PDF document, allowing PDF viewers to handle the data.
        /// </summary>
        /// <param name="tile">The TIFF tile.</param>
        /// <param name="nr">The index of the data within the tile.</param>
        /// <param name="comp">The compression scheme that was used.</param>
        /// <param name="bpc_ar">Array of bits per component values.</param>
        /// <param name="n_comps">The number of components in the image.</param>
        /// <returns>A WritableStream object representing the compressed data for use in a PDF document.</returns>
        /// <exception cref="NotImplementedException">Thrown when the specified compression scheme is not implemented.</exception>
        internal WritableStream MakeStream(TiffTile tile, int nr, COMPRESSION_SCHEME comp, ushort[] bpc_ar, int n_comps)
        {
            byte[] data = tile.Data[nr];
            WritableStream ws;
            FilterArray fa;
            FilterParmsArray fp;
            switch (comp)
            {
                case COMPRESSION_SCHEME.UNCOMPRESSED:
                    ws = new WritableStream(data);
                    break;
                case COMPRESSION_SCHEME.PIXTIFF:
                case COMPRESSION_SCHEME.DEFLATE:
                case COMPRESSION_SCHEME.ADOBE_DEFLATE:
                    fa = new FilterArray(new PdfFlateFilter());
                    fp = Predictor == 2 ? new FilterParmsArray(fa, new FlateParams(Pdf.Filter.Predictor.Tiff2, n_comps, bpc_ar[nr], tile.Width)) : null;
                    ws = new WritableStream(data, fa, fp);
                    break;
                case COMPRESSION_SCHEME.CCITT_G3:
                    if (FillOrder == 2)
                    {
                        data = (byte[])data.Clone();
                        ArrayHelper.ReverseData(data);
                    }
                    var t4 = T4Options;
                    fa = new FilterArray(new PdfFaxFilter());
                    ws = new WritableStream(data, fa,
                        new FilterParmsArray(new PdfItem[] {
                                new PdfFaxParms(t4.TwoDimensional ? tile.Height : 0, 
                                                tile.TileWidth,
                                                tile.Height,
                                                //ByteAligned is ignored on G31D by LibTiff. Guess End Of Line markers are good enough.
                                                //fax2d.tif is a case of a file which has this flag set, yet is not byte aligned.
                                                t4.TwoDimensional && t4.ByteAligned,
                                                false, false, 
                                                
                                                //This looks to be incorrect, but works correctly.
                                                PhotometricInterpretation == Photometric.BlackIsZero,
                                                null)
                            },
                            fa));
                    break;
                case COMPRESSION_SCHEME.CCITT_G4:
                    if (FillOrder == 2)
                    {
                        data = (byte[])data.Clone();
                        ArrayHelper.ReverseData(data);
                    }
                    fa = new FilterArray(new PdfFaxFilter());
                    ws = new WritableStream(data, fa,
                        new FilterParmsArray(new PdfItem[] {
                                new PdfFaxParms(-1, 
                                                tile.TileWidth,
                                                tile.Height,
                                                false, false, false, 
                                                
                                                //This looks to be incorrect, but works correctly.
                                                PhotometricInterpretation == Photometric.BlackIsZero,
                                                null)
                            },
                            fa));
                    break;
                //case COMPRESSION_SCHEME.JBIG2:
                //    ws = new WritableStream(data, new FilterArray(new PdfJBig2Filter()));
                //    break;
                case COMPRESSION_SCHEME.JPEG:
                    ws = new WritableStream(data, new FilterArray(new PdfDCTFilter()));
                    break;
                case COMPRESSION_SCHEME.LZW:
                    fa = new FilterArray(new PdfLZWFilter());
                    fp = Predictor == 2 ? new FilterParmsArray(fa, new FlateParams(Pdf.Filter.Predictor.Tiff2, n_comps, bpc_ar[nr], tile.Width)) : null;
                    ws = new WritableStream(data, fa, fp);
                    break;
                case COMPRESSION_SCHEME.PACKBITS:
                    ws = new WritableStream(data, new FilterArray(new PdfRunLengthFilter()));
                    break;

                default: throw new NotImplementedException("TIFF compression mode: "+ comp);
            }

            return ws;
        }

        private WritableStream MakeByteAlignedStream(TiffTile tile, int nr, COMPRESSION_SCHEME comp, ushort[] bpc_ar, int n_comps)
        {
            WritableStream ws = MakeStream(tile, nr, comp, bpc_ar, n_comps);

            if (tile.StrideAr != null)
            {
                //Changes stride to 8 byte aligned.
                int bpp = bpc_ar[nr];
                if (n_comps != 1 && nr == 0)
                {
                    for (int c = 1; c < n_comps; c++)
                        bpp += bpc_ar[c];
                }
                int stride = (tile.Width * bpp + 7) / 8;
                int tile_stride = tile.StrideAr[nr];
                byte[] dest = new byte[stride * tile.Height];
                byte[] data = data = ws.DecodedStream;
                for (int data_pos = 0, dest_pos = 0; dest_pos < dest.Length; )
                {
                    Buffer.BlockCopy(data, data_pos, dest, dest_pos, stride);
                    data_pos += tile_stride;
                    dest_pos += stride;
                }

                ws = new WritableStream(dest);
            }

            if (_tags.ContainsKey(TagID.SampleFormat))
            {
                var format = tile.Format[nr];
                if (format != Internal.SampleFormat.UINT)
                    throw new TiffNotImplementedException("Unnsuported sample format: "+format.ToString());
            }

            return ws;
        }

        private int[][] LabSgnComps(TiffTile tile, byte[] data)
        {
            return tile.ChunkyToPlanar(data);
        }

        /// <summary>
        /// Processes the components of a TIFF tile in the Lab color space, adjusting the sign of the components as needed.
        /// </summary>
        /// <param name="tile">The TIFF tile containing the image data.</param>
        /// <param name="comps">The array of image components.</param>
        /// <returns>An array of ImageComp objects representing the processed image components.</returns>
        private ImageComp[] LabSgnComps(TiffTile tile, int[][] comps)
        {
            // Get the bits per sample for each component.
            var bps = tile.BitsPerSample;

            // Iterate over each component starting from the second one (index 1).
            for (int c = 1; c < comps.Length; c++)
            {
                // Get the current component.
                var comp = comps[c];

                // Calculate the maximum value and the maximum mask for the current component.
                uint max = (uint)(2 << (bps[c] - 2)), max_mask = (max << 1) - 1;

                // Iterate over each value in the component.
                for (int x = 0; x < comp.Length; x++)
                {
                    // Get the current value and cast it to an unsigned integer.
                    uint val = unchecked((uint)comp[x]);

                    // Adjust the sign of the value based on the maximum value.
                    if ((val & max) != 0)
                        val = (uint)(max + (int)val) & max_mask;
                    else
                        val |= max;

                    // Update the component with the adjusted value.
                    comp[x] = (int)(val);
                }
            }
            return new ImageComp[]
            {
                new ImageComp(bps[0], bps[0], false, 1, 1, tile.Width, tile.Height, comps[0]),
                new ImageComp(bps[1], bps[1], false, 1, 1, tile.Width, tile.Height, comps[1]),
                new ImageComp(bps[2], bps[2], false, 1, 1, tile.Width, tile.Height, comps[2])
            };
        }

        /// <summary>
        /// Splits up the YCC data into sepperate planes
        /// </summary>
        /// <param name="data">Data to sepperate</param>
        /// <param name="tile">Tile for that data</param>
        /// <returns>Sepperated components. Y can be a little padded, but as long as the data is only used for J2k compression it does not matter</returns>
        private int[][] SepperateYCC(TiffTile tile, byte[] data)
        {
            var samples = YCbCrSubSampling;
            System.Diagnostics.Debug.Assert(BitsPerComponent == 8, "Code need refactoring to support other bit sizes");

            int c_width = tile.Width;
            if (samples.Horizontal > SUBSAMPLING.None)
                c_width = c_width / (int)samples.Horizontal + (tile.Width % (int)samples.Horizontal != 0 ? 1 : 0);
            int c_height = tile.Height;
            if (samples.Vertical > SUBSAMPLING.None)
                c_height = c_height / (int)samples.Vertical + (tile.Height % (int)samples.Vertical != 0 ? 1 : 0);

            int[] Cb = new int[c_width * c_height];
            int[] Cr = new int[Cb.Length];

            if (tile.Width * tile.Height + Cb.Length + Cr.Length > data.Length)
                throw new TiffReadException("Innsuficient data for uppsampeling");

            //Y is made a little too large, but this will have no effect on the final
            //filesize. At least as long as the data is fed throught the j2k compressor.
            //
            //The extra size is so that Y[x_pos + x + offset] won't go out of bounds
            //on non power of two images. 
            int[] Y = new int[(tile.Width /*+ ((int)samples.Horizontal - 1)*/) * (tile.Height + ((int)samples.Vertical - 1))];

            int rows_to_read = (int)samples.Vertical;
            int columns_to_read = (int)samples.Horizontal;
            int packets_to_read = Cr.Length;//Y.Length / (int)samples.Horizontal / (int)samples.Vertical;

            for (int c = 0, data_pos = 0, x_pos = 0, col_pos = 0; c < packets_to_read; c++)
            {
                col_pos += columns_to_read;
                if (col_pos < tile.Width)
                {
                    for (int y = 0, offset = 0; y < rows_to_read; y++)
                    {
                        for (int x = 0; x < columns_to_read; x++)
                                Y[x_pos + x + offset] = data[data_pos++];
                        offset += tile.Width;
                    }

                    x_pos += columns_to_read;
                }
                else
                {
                    int max_columns_to_read = tile.Width - (col_pos - columns_to_read);
                    if (max_columns_to_read > 0)
                    {
                        for (int y = 0, offset = 0; y < rows_to_read; y++)
                        {
                            for (int x = 0; x < max_columns_to_read; x++, data_pos++)
                                    Y[x_pos + x + offset] = data[data_pos];
                            data_pos += columns_to_read - max_columns_to_read;
                            offset += tile.Width;
                        }
                    }

                    col_pos = 0;
                    x_pos += tile.Width * (rows_to_read - 1) + max_columns_to_read;
                }

                Cb[c] = data[data_pos++];
                Cr[c] = data[data_pos++];
            }

            //Scales up Cb and Cr. OpenJpeg2.Internal.MCT.Encode(int[] c0, int[] c1, int[] c2, int n) can't handle
            //images where the resolution is halved. I don't know why. Perhaps I'm just not setting up the components
            //correctly. 
            //
            //Tuns out the fix is to not use the MCT. Easy.
            //Cb = OpenJpeg2.Util.Scaler.Rezise(Cb, tile.Width / columns_to_read, tile.Height / rows_to_read, tile.Width, tile.Height);
            //Cr = OpenJpeg2.Util.Scaler.Rezise(Cr, tile.Width / columns_to_read, tile.Height / rows_to_read, tile.Width, tile.Height);

            return new int[][] { Y, Cb, Cr };
        }

        private ImageComp[] SepperateYCC(TiffTile tile, int[][] data)
        {
            var samples = YCbCrSubSampling;
            int c_width = tile.Width;
            if (samples.Horizontal > SUBSAMPLING.None)
                c_width = c_width / (int)samples.Horizontal + (tile.Width % (int)samples.Horizontal != 0 ? 1 : 0);
            int c_height = tile.Height;
            if (samples.Vertical > SUBSAMPLING.None)
                c_height = c_height / (int)samples.Vertical + (tile.Height % (int)samples.Vertical != 0 ? 1 : 0);
            int rows_to_read = (int)samples.Vertical;
            int columns_to_read = (int)samples.Horizontal;

            var bpc = BitsPerComponent;
            return new ImageComp[]
            {
                new ImageComp(bpc, bpc, false, 1, 1, tile.Width, tile.Height, data[0]),
                //new ImageComp(8, 8, false, 1, 1, tile.Width, tile.Height, Cb),
                //new ImageComp(8, 8, false, 1, 1, tile.Width, tile.Height, Cr)
                new ImageComp(bpc, bpc, false, columns_to_read, rows_to_read, c_width, c_height, data[1]),
                new ImageComp(bpc, bpc, false, columns_to_read, rows_to_read, c_width, c_height, data[2])
            };
        }

        #endregion

        #region Utility classes and delegates

        /// <summary>
        /// Chunky to planar delegate
        /// </summary>
        /// <param name="tile">Tile information with raw data</param>
        /// <param name="decompressed_data">The decompressed data</param>
        /// <returns>Sepperate components</returns>
        private delegate int[][] ToPlanarFunc(TiffTile tile, byte[] decompressed_data);

        /// <summary>
        /// Converts the data to image components
        /// </summary>
        private delegate ImageComp[] ToCompsFunc(TiffTile tile, int[][] comps);

        private class ColorInfo
        {
            public IColorSpace ColorSpace;
            public COLOR_SPACE Jp2_cs;
            public bool IncludeCS;
            public xRange[] Decode;
            public bool Decompress;
            public ToPlanarFunc C2P;
            public ToCompsFunc P2IC;
        }

        #endregion
    }
}
