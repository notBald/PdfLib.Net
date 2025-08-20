using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Img.Tiff.Internal
{
    public enum DataType : ushort
    {
        BOOL = 0,
        BYTE = 1,
        ASCII,

        /// <summary>
        /// Unsigned 16-bit integer
        /// </summary>
        SHORT,

        /// <summary>
        /// Unsigned 32-bit integer
        /// </summary>
        LONG,

        ///<summary>
        ///RATIONAL is an offset to a 64-bit number where the first 
        ///32-bit are to be devided on the last 32-bit
        ///</summary>
        RATIONAL,
        SBYTE,
        UNDEFINED,
        SSHORT,

        /// <summary>
        /// Signed 32 bit integer
        /// </summary>
        SLONG,
        SRATIONAL,
        FLOAT,
        DOUBLE,
        IFD,

        /// <summary>
        /// BigTiff
        /// </summary>
        LONG8 = 16,
        SLONG8,
        IFD8
    }

    public enum SampleType : ushort
    {
        Unknown,
        PreMultimpiedAlpha,
        Alpha
    }

    [Flags()]
    public enum ImageType : uint
    {
        None,
        Reduced = 1,
        Page = 2,
        Mask = 4
    }

    public enum Orientation : ushort
    {
        UpLeft = 1,
        UpRight,
        DownRight,
        DownLeft,
        LeftUp,
        RightUp,
        RightDown,
        LeftDown
    }

    public enum Photometric : ushort
    {
        WhiteIsZero,
        BlackIsZero,
        RGB,
        Palette,
        TransparencyMask,
        Separated,
        YCbCr,
        CIELAB = 8
    }

    public enum PixelMode : ushort
    {
        Chunky = 1,
        Planar
    }

    public enum ResUnit : ushort
    {
        None = 1,
        Inch,
        Centimeter
    }

    public enum TiffInk : ushort
    {
        CMYK = 1,
        Other = 2
    }

    public enum SUBSAMPLING : ushort
    {
        None = 1,
        Half,
        Quarter = 4
    }

    [DebuggerDisplay("{Horizontal} - {Vertical}")]
    public class SubSampling
    {
        public readonly SUBSAMPLING Horizontal, Vertical;
        public SubSampling(ushort h, ushort v)
        { Horizontal = (SUBSAMPLING)h; Vertical = (SUBSAMPLING)v; }
    }

    public enum SubSamplingPosition : ushort
    {
        CENTERED = 1,
        COSITED
    }

    public enum SampleFormat : ushort
    {
        UINT = 1,
        INT,
        FLOAT,
        VOID
    }
}
