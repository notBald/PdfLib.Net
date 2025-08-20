using System.IO;
using PdfLib.Util;

namespace PdfLib.Img.Tiff.Internal
{
    /// <summary>
    /// Represents the header of a TIFF file, containing metadata about the file format.
    /// </summary>
    class TiffHeader
    {
        /// <summary>
        /// Indicates whether the TIFF file is in BigTIFF format.
        /// </summary>
        public readonly bool IsBigTiff;

        /// <summary>
        /// Indicates whether the TIFF file uses big-endian byte order.
        /// </summary>
        public readonly bool IsBigEndian;

        /// <summary>
        /// The position of the first Image File Directory (IFD) in the TIFF file.
        /// </summary>
        public readonly long FirstIDFPos;

        /// <summary>
        /// Initializes a new instance of the <see cref="TiffHeader"/> class.
        /// </summary>
        /// <param name="big_tiff">Indicates whether the TIFF file is in BigTIFF format.</param>
        /// <param name="big_endian">Indicates whether the TIFF file uses big-endian byte order.</param>
        /// <param name="first_idf">The position of the first Image File Directory (IFD) in the TIFF file.</param>
        private TiffHeader(bool big_tiff, bool big_endian, long first_idf)
        {
            IsBigTiff = big_tiff;
            IsBigEndian = big_endian;
            FirstIDFPos = first_idf;
        }

        /// <summary>
        /// Creates a <see cref="TiffHeader"/> instance by reading the header information from the specified stream.
        /// </summary>
        /// <param name="file">The stream containing the TIFF file.</param>
        /// <returns>A <see cref="TiffHeader"/> object if the header is valid; otherwise, null.</returns>
        internal static TiffHeader Create(Stream file)
        {

            var head = new byte[8];
            bool big_endian;

            //Reads in what type of tiff file this is.
            int read = file.Read(head, 0, 8);
            if (read != 8) return null;

            if (head[0] == 0x49)
            {
                if (head[1] != 0x49)
                    return null;
                big_endian = false;
            }
            else
            {
                if (head[0] != 0x4D || head[1] != 0x4D)
                    return null;
                big_endian = true;
            }

            int version = StreamReaderEx.ReadShort(big_endian, 2, head);

            if (version == 42)
            {
                if (version != 42)
                    return null;

                long offset = StreamReaderEx.ReadUInt(big_endian, 4, head);
                if (offset < 4)
                    return null;

                return new TiffHeader(false, big_endian, offset);
            }
            else if (version == 43)
            {
                if (StreamReaderEx.ReadShort(big_endian, 4, head) != 8 ||
                    StreamReaderEx.ReadShort(big_endian, 6, head) != 0 ||
                    file.Read(head, 0, 8) != 8)
                    return null;

                long offset = StreamReaderEx.ReadLong(big_endian, 0, head);
                if (offset < 16)
                    return null;

                return new TiffHeader(true, big_endian, offset);
            }

            return null;
        }
    }
}
