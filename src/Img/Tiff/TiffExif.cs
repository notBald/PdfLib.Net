using PdfLib.Img.Tiff.Internal;
using System;

namespace PdfLib.Img.Tiff
{
    public class TiffExif : TiffIFD
    {
        #region Init

        internal TiffExif(TiffStreamReader file, bool is_big_tiff)
            : base(file, is_big_tiff)
        { }

        internal static TiffExif Create(long idf, TiffStreamReader file, bool is_big_tiff)
        {
            try
            {
                if (idf < 8) return null;

                long count = is_big_tiff ? file.ReadLong(idf) : file.ReadUShort(idf);

                //Reads out all tags in one go
                int size;
                long position;
                if (is_big_tiff)
                {
                    size = (int)count * 20;
                    position = idf + 8;
                }
                else
                {
                    size = (int)count * 12;
                    position = idf + 2;
                }

                byte[] tags = new byte[size];
                file.ReadEx(position, tags, 0, size);

                var exif = new TiffExif(file, is_big_tiff);
                exif.Tags = TiffStream.CreateTags((int)count, tags, 0, exif);
                return exif;
            }
            catch (Exception) { return null; }
        }

        #endregion

        internal override void Repair()
        {
            
        }
    }
}
