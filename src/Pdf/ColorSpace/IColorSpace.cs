using System;
using System.Collections.Generic;
using System.Linq;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    public interface IColorSpace
    {
        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        PdfCSType CSType { get; }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        int NComponents { get; }

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        ColorConverter Converter { get; }

        /// <summary>
        /// Standar decode values, used for image
        /// decoding.
        /// </summary>
        double[] DefaultDecode { get; }

        /// <summary>
        /// Color to set when this colorspace is
        /// selected.
        /// </summary>
        PdfColor DefaultColor { get; }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        double[] DefaultColorAr { get; }

        /// <summary>
        /// If the colorspaces are equal
        /// </summary>
        bool Equals(IColorSpace cs);
    }
}
