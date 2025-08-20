using PdfLib.Img.Tiff.Internal;
using System.IO;

namespace PdfLib.Img.Tiff
{
    /// <summary>
    /// Provides functionality to read TIFF files.
    /// </summary>
    public class TiffReader
    {
        /// <summary>
        /// Opens a TIFF file from the specified file path.
        /// </summary>
        /// <param name="file_path">The path to the TIFF file.</param>
        /// <returns>A TiffStream object representing the opened TIFF file.</returns>
        public static TiffStream Open(string file_path)
        {
            return Open(File.OpenRead(file_path), true, true);
        }

        /// <summary>
        /// Opens a TIFF file from the specified stream.
        /// </summary>
        /// <param name="file">The stream containing the TIFF file.</param>
        /// <param name="on_demand_load">Indicates whether to load the TIFF file on demand.</param>
        /// <param name="dispose_stream">Indicates whether to dispose the stream after reading.</param>
        /// <returns>A TiffStream object representing the opened TIFF file.</returns>
        /// <exception cref="PdfInternalException">Thrown when the stream cannot be read or seeked.</exception>
        /// <exception cref="TiffReadException">Thrown when the TIFF header is invalid.</exception>
        public static TiffStream Open(Stream file, bool on_demand_load, bool dispose_stream)
        {
            if (!file.CanRead || !file.CanSeek)
                throw new PdfInternalException(SR.SeekStream);

            var head = TiffHeader.Create(file);
            if (head == null)
                throw new TiffReadException("Not a valid tiff header");

            var inn = new TiffStreamReader(file, head.IsBigEndian);

            return new TiffStream(head.IsBigTiff, head.FirstIDFPos, inn, dispose_stream, on_demand_load);
        }
    }
}
