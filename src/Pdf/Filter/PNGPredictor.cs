using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// For decompressing PNG images
    /// </summary>
    /// <remarks>
    /// For compression: (Indexed images and images with sub 8-bit pixel depth tend to be better off without filtering)
    /// 
    /// For best compression of truecolor and grayscale images, we recommend an adaptive filtering approach in 
    /// which a filter is chosen for each scanline. The following simple heuristic has performed well in early 
    /// tests: compute the output scanline using all five filters, and select the filter that gives the 
    /// smallest sum of absolute values of outputs. (Consider the output bytes as signed differences for this 
    /// test.) This method usually outperforms any single fixed filter choice. However, it is likely 
    /// that much better heuristics will be found as more experience is gained with PNG.
    /// 
    /// My understanding here is:
    ///  - Run a filter over a scanline. Add togeter all the output, then compare it with the Math.Abs(output)
    ///    of the other filters. The filter with the smalest output wins. 
    /// </remarks>
    public static class PNGPredictor
    {
        /// <summary>
        /// Unfilters filtered data
        /// </summary>
        /// <param name="colors">Number of color components per sample</param>
        /// <param name="bpc">Bits per color component</param>
        /// <param name="columns">Number of samples per row</param>
        /// <see cref="http://www.w3.org/TR/2003/REC-PNG-20031110/#9-figure91"/>
        /// <remarks>
        /// Filters are applied to bytes, not to pixels, regardless of the bit depth 
        /// or colour type of the image.
        /// 
        /// Todo: Tiff predicor
        /// 
        /// Persudo code
        /// 
        /// image[x][y] (where x,y marks a sample, not a byte)
        /// foreach(row)
        /// {
        ///     for (y = width -1; y bigger than 1; y--)
        ///       image[row][y] -= image[row][y - 1];
        /// }
        /// 
        /// </remarks>
        public static byte[] Recon(Stream data, int colors, int bpc, int columns)
        {
            //How many bytes on one row
            int stride = (colors * bpc * columns + 7) / 8;

            //How many positions one have to move to the left to
            //get the corresponding byte value.
            int sampl_offset = (colors * bpc + 7) / 8;

            //Specs say that boundary values are zero. Inits last row to zero then
            byte[] cur = new byte[stride];
            byte[] last;
            
            //To prevent lots of mem copies, rows are kept in a list until
            //they are put together.
            List<byte[]> rows = new List<byte[]>(512);

            //How many pixel relevant bytes have been read in total
            int total_read = 0;

            while (true)
            {
                //First byte of the row tells which predicor to use.
                PRED pred = (PRED)data.ReadByte();
                if (pred == PRED.EOD) break;

                //Create new current row
                last = cur;
                cur = new byte[stride];
                
                //Reads out one row
                int read = data.Read(cur, 0, stride);
                while (read < stride)
                {
                    var r = data.Read(cur, 0, stride - read);
                    if (r == 0) 
                        break;
                    read += r;
                }
                total_read += read;

                if (read == 0) 
                    break; //End of data

                // c b
                // a x
                switch (pred)
                {
                    case PRED.None:
                        //Recon(x) = Filt(x) (Nothing to do)
                        break;
                    case PRED.Sub:
                        //Recon(x) = Raw(x) + Raw(x-1)
                        //Starts at _sampl_offset as data before that is 0
                        for (int c = sampl_offset; c < cur.Length; c++)
                            cur[c] += cur[c - sampl_offset];
                        break;

                    case PRED.Up:
                        //Recon(x) = Raw(x) + Prior(x) 
                        for (int c = 0; c < cur.Length; c++)
                            cur[c] += last[c];
                        break;

                    case PRED.Avg:
                        //Recon(x) = Raw(x) + floor((Raw(x-1) + Prior(x)) / 2) 
                        //Special case cur[0], as x-1 is out of bounds.
                        for (int c=0; c < sampl_offset; c++)
                            cur[c] += (byte)(last[c] / 2);
                        for (int c = sampl_offset; c < cur.Length; c++)
                            cur[c] += (byte)((cur[c - sampl_offset] + last[c]) / 2);
                        break;

                    case PRED.Paeth:
                        //Recon(x) = Raw(x) + PaethPredictor(Raw(x-1), Prior(x), Prior(x-1)) 
                        //Special case cur[0], as x-1 is out of bounds.
                        for (int c = 0; c < sampl_offset; c++)
                            cur[c] += PaethPredictor(0, last[c], 0);
                        for (int c = sampl_offset; c < cur.Length; c++) 
                        {
                            int sub_pos = c - sampl_offset;
                            cur[c] += PaethPredictor(cur[sub_pos], last[c], last[sub_pos]);
                        }
                        break;

                    default:
                        throw new NotSupportedException();
                }

                rows.Add(cur);
                if (read != stride) 
                    break;
            }
           
            //Array for the entire image
            byte[] bytes = new byte[stride * rows.Count];

            //Throws if there's missing data. (Todo: 1001head.com_characters_18 throws here which results in the "1001" being faded, comment this out and it works)
            if (total_read != bytes.Length)
                throw new PdfFilterException(ErrCode.UnexpectedEOD);

            //Reassembles the rows.
            int pos = 0;
            for (int c = 0; c < rows.Count; c++)
            {
                Buffer.BlockCopy(rows[c], 0, bytes, pos, stride);
                pos += stride;
            }

            return bytes;
        }

        public static byte[] TiffPredicor(byte[] data, int colors, int bpc, int columns)
        {
            return TiffPredicor(new MemoryStream(data), colors, bpc, columns);
        }

        public static byte[] TiffPredicor(Stream data, int colors, int bpc, int columns)
        {
            //How many bytes on one row
            int stride = (colors * bpc * columns + 7) / 8;

            //Optimize for the comon case of 8 bits per sample
            if (bpc == 8)
            {
                //Specs say that boundary values are zero. Inits last row to zero then
                byte[] cur;// = new byte[stride];

                //To prevent lots of mem copies, rows are kept in a list until
                //they are put together.
                List<byte[]> rows = new List<byte[]>(512);

                //How many pixel relevant bytes have been read in total
                int total_read = 0;

                //How many positions one have to move to the left to
                //get the corresponding byte value.
                int sampl_offset = (colors * bpc + 7) / 8;

                while (true)
                {
                    //Create new current row
                    cur = new byte[stride];

                    //Reads out one row
                    int read = data.Read(cur, 0, stride);
                    while (read < stride)
                    {
                        var r = data.Read(cur, 0, stride - read);
                        if (r == 0) break;
                        read += r;
                    }
                    total_read += read;

                    if (read == 0) break; //End of data

                    //Recon(x) = Filt(x) + Recon(a)
                    //Starts at _sampl_offset as data before that is 0
                    for (int c = sampl_offset; c < cur.Length; c++)
                        cur[c] += cur[c - sampl_offset];

                    rows.Add(cur);
                    if (read != stride) break;
                }

                //Array for the entire image
                byte[] bytes = new byte[stride * rows.Count];

                //Throws if there's missing data. (Todo: pad)
                if (total_read != bytes.Length)
                {
                    //Assumes that there's junk data
                    if (total_read % stride != 0)
                    {
                        rows.RemoveAt(rows.Count - 1);
                        Array.Resize<byte>(ref bytes, rows.Count * stride);
                    }
                    else 
                        throw new PdfFilterException(ErrCode.UnexpectedEOD);
                }

                //Reassembles the rows.
                int pos = 0;
                for (int c = 0; c < rows.Count; c++)
                {
                    Buffer.BlockCopy(rows[c], 0, bytes, pos, stride);
                    pos += stride;
                }

                return bytes;
            }

            //Handles any bitsize from 1 to 32* 
            //Can be extended up to 56 bits per sample by using GetBits instead of GetBitsU
            // * Components over 8 bit might butt heads with Mr. Endian. I've not given that
            //   any thought. 

            //Bits per pixel
            int bpp = colors * bpc;
            uint[] pix1_components = new uint[colors];
            uint[] pix2_components = new uint[colors];

            //BitReader
            var br = new Util.BitStream64(data);
            var ms = new MemoryStream(columns*500);
            var bw = new Util.BitWriter(ms);

            while (true)
            {
                if (!br.HasBits(bpp))
                    break; //End of data

                //Reads out the first pixel of the row
                for (int sample_nr = 0; sample_nr < pix1_components.Length; sample_nr++)
                    pix1_components[sample_nr] = br.GetBitsU(bpc);

                int colum_nr = 1;

                for (; colum_nr < columns; colum_nr++)
                {
                    if (!br.HasBits(bpp))
                    {
                        Log.Err(ErrSource.Filter, ErrCode.UnexpectedEOD);
                        break;
                    }

                    //Performs the magic.
                    for (int sample_nr = 0; sample_nr < pix1_components.Length; sample_nr++)
                        pix2_components[sample_nr] = (br.GetBitsU(bpc) + pix1_components[sample_nr]);
                    
                    //Writes out pix1
                    for (int sample_nr = 0; sample_nr < pix1_components.Length; sample_nr++)
                        bw.Write(pix1_components[sample_nr], bpc);

                    //Swaps
                    var temp = pix1_components;
                    pix1_components = pix2_components;
                    pix2_components = temp;
                }

                //Writes out last pixel
                for (int sample_nr = 0; sample_nr < pix1_components.Length; sample_nr++)
                    bw.Write(pix1_components[sample_nr], bpc);

                //Throws if there's missing data. (Todo: pad up to end of row)
                if (colum_nr != columns)
                    throw new PdfFilterException(ErrCode.UnexpectedEOD);
            }

            bw.Dispose();
            return ms.ToArray();
        }

        /// <summary>
        /// As in (9.4 Filter type 4: Paeth)
        /// </summary>
        static byte PaethPredictor(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }

        private enum PRED
        {
            EOD = -1,
            None, Sub, Up, Avg, Paeth
        }
    }
}
