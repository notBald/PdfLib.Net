using System;

namespace PdfLib.Img.Png
{
    public enum PNGCompression
    {
        Normal,
        Maximum,
        Zopli
    }

    /// <summary>
    /// Options for conversion from PDF to TIFF images.
    /// </summary>
    [Flags()]
    public enum PDFtoPNGOptions
    {
        /// <summary>
        /// No extra work will be done
        /// </summary>
        NONE,

        /// <summary>
        /// The decode array of the color space is included
        /// </summary>
        APPLY_DECODE = 0x01,

        /// <summary>
        /// If the image has a transparency mask, said mask will be
        /// embeded within the pdf data.
        /// </summary>
        EMBED_SMASK = 0x02,

        /// <summary>
        /// If the image use a ICC profile, it will be embeded inside
        /// the PNG container.
        /// </summary>
        EMBED_ICC = 0x04,

        /// <summary>
        /// Decode will only be applied when one have to convert
        /// the colorspace
        /// </summary>
        //SELECTIV_DECODE = 0x08,

        /// <summary>
        /// For when one want the output to always be RGB. This option
        /// also converts CalGray and CalRGB colorspaces to plain RGB,
        /// but images that are already plain gray will be left gray.
        /// 
        /// To convert those images set the "force_rgb" boolean flag
        /// </summary>
        //FORCE_RGB = 0x10,

        /// <summary>
        /// If saving the image as a JPX to disk, this option insures that
        /// the image appeares as it would in the PDF document.
        /// </summary>
        FOR_DISK = EMBED_SMASK | APPLY_DECODE | EMBED_ICC
    }

    [Flags()]
    public enum PngColorType
    {
        None = 0,       //Grayscale
        Palette = 1,    //Indexed grayscale
        Color = 2,      //Colored
                        //3 = Indexed with color
        Alpha = 4       //Grasycale with alpha
                        //6 = Color with alpha
    }

    /// <summary>
    /// How a row is filtered
    /// </summary>
    internal enum PngPREDICOR
    {
        EOD = -1,
        None, Sub, Up, Avg, Paeth
    }
}
