using System;
using System.IO;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Implements PDF RunLength decoding
    /// </summary>
    /// <remarks>
    /// Similar to Apple Packbits except that code 128
    /// marks EOF.
    /// </remarks>
    public sealed class PdfRunLengthFilter : PdfFilter
    {
        #region Variables and properties

        /// <summary>
        /// End of data
        /// </summary>
        const byte EOD = 128;

        /// <summary>
        /// Holdover from packbits impl.
        /// </summary>
        const int MAX_PACKBITS_LENGTH = 128;

        /// <summary>
        /// This filter's PDF name
        /// </summary>
        public override string Name { get { return "RunLengthDecode"; } }

        #endregion

        #region Init

        #endregion

        /// <summary>
        /// Decodes runlength encoded data
        /// </summary>
        /// <param name="data">Encoded data</param>
        /// <param name="fparams">Not used</param>
        /// <returns>Decoded data</returns>
        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
            //Decoded image
            MemoryStream dimg = new MemoryStream();

            //Number of written bytes
            //int nWB = 0;

            //Itterates until all pixels have been filled out.
            for (int nDecB = 0; nDecB < data.Length; )
            {
                //Fetches a byte.
                byte b = data[nDecB++];

                //When b is smaler than 128, copy b+1 bytes to the uncompressed array.
                if (b < 128)
                {
                    b++;
                    if (nDecB + b >= data.Length)
                    {
                        dimg.Write(data, nDecB, data.Length - nDecB);
                        break;
                        //throw new IndexOutOfRangeException("Corrupt inputt data");
                    }

                    dimg.Write(data, nDecB, b);

                    nDecB += b;
                    //nWB += b;

                    continue;
                }

                //When b is bigger than 128, copy the next byte (-b+1) times
                if (b > 128)
                {
                    if (nDecB == data.Length)
                        throw new IndexOutOfRangeException("Corrupt inputt data");

                    int sb = ((sbyte)b) * -1 + 1;
                    byte nb = data[nDecB++];
                    for (int c = 0; c < sb; c++)
                        dimg.WriteByte(nb);

                    continue;
                }

                //When b is 128 we've reached the end
                return dimg.ToArray();
            }

            Log.Warn(WarnType.PackbitsNoEOD);
            return dimg.ToArray();
        }

        /// <summary>
        /// Finds the ending by reading to byte with value 128
        /// </summary>
#if LONGPDF
        internal override byte[] FindEnd(Stream source, long startpos, PdfFilterParms fparams)
#else
        internal override byte[] FindEnd(Stream source, int startpos, PdfFilterParms fparams)
#endif
        {
            source.Position = startpos;
            var rec = new Util.RecordStream(4096, source);
            int b;
            while ((b = rec.ReadByte()) != -1 && b != EOD)
                ;
            return rec.ToArray();
        }

        /// <summary>
        /// Encodes data into runlength format
        /// </summary>
        /// <param name="data">Data to encode</param>
        /// <returns>Runlength encoded data</returns>
        public static byte[] Encode(byte[] data, int row_length = 0)
        {
            //big enough for the worst case senarios.
            bool eod = true;
            if (row_length == 0)
                row_length = data.Length;
            else
                eod = false;
            int temp_row = row_length + row_length / 128 + 1;
            byte[] encoded =
                new byte[(int)(temp_row * Math.Ceiling((float)data.Length / row_length))];
            int ec = 0; //encoded count.
            int dc = 0; //data count.
            int stop = row_length;

            while (dc < data.Length)
            {
                if (dc == stop)
                    stop += row_length;

                //Counts the number of times the byte repeats.
                int b_count = count_same(data, dc, stop);

                //If it repeates, writes how many times it repeats
                //and the bytes value
                if (b_count > 1)
                {
                    sbyte sb = (sbyte)((b_count - 1) * -1);
                    encoded[ec++] = unchecked((byte)sb);
                    encoded[ec++] = data[dc];
                    dc += b_count;
                    continue;
                }

                //Since we know the value only repeats once we count 
                //the number of unique values instead.
                b_count = count_diff(data, dc, stop);

                //Calculates the max size of a literal run worth fusing.
                int max = Math.Min(MAX_PACKBITS_LENGTH, stop - dc);

                //Fuses literal runs. This means that two byte repeates
                //are encoded in a literal run instead of being encoded
                //as repeates. This saves one byte, as one do not have
                //to redeclare the next literal run.
                while (b_count < max)
                {
                    //Checks if there is a two byte repeat after the literal
                    //run.
                    byte next_count = count_same(data, dc + b_count, stop);
                    if (next_count != 2) break;

                    //It never hurts to fuse a two repeat to a literal run,
                    //assuming one respects "max".
                    b_count += 2;

                    //Checks if there's a literal run after the two byte
                    //repeat.
                    next_count = count_diff(data, dc + b_count, stop);
                    if (next_count == 1)
                    {
                        //Fuses multiple two byte repeat runs 
                        //(todo: Not sure if this works as intended, that will say compresses better,
                        //       in any case it won't corrupt the file or anything)
                        int snext_count = count_same(data, dc + b_count, stop);
                        if (snext_count == 2)
                            continue;
                    }

                    b_count += next_count;
                    if (b_count >= MAX_PACKBITS_LENGTH)
                    {
                        b_count = MAX_PACKBITS_LENGTH;
                        break;
                    }
                }

                //Encodes the length of the literal run.
                encoded[ec++] = (byte)(b_count - 1);
                //if (dc + b_count > data.Length || ec + b_count > encoded.Length)
                //    Main.Debug();
                Buffer.BlockCopy(data, dc, encoded, ec, b_count);
                ec += b_count;
                dc += b_count;
            }

            if (eod)
            {
                byte[] ret = new byte[ec + 1];
                Buffer.BlockCopy(encoded, 0, ret, 0, ec);
                ret[ret.Length - 1] = EOD; //<-- End marker
                return ret;
            }
            else
            {
                byte[] ret = new byte[ec];
                Buffer.BlockCopy(encoded, 0, ret, 0, ec);
                return ret;
            }
        }

        private static byte count_diff(byte[] data, int start, int stop)
        {
            int max = Math.Min(MAX_PACKBITS_LENGTH, stop - start);
            if (max == 0) return 0;
            max += start;
            byte b_count = 1;
            byte b = data[start];
            for (int c = start + 1; c < max; c++)
                if (data[c] != b)
                {
                    b_count++;
                    b = data[c];
                }
                else
                {
                    b_count--;
                    break;
                }
            return b_count;
        }

        private static byte count_same(byte[] data, int start, int stop)
        {
            int max = Math.Min(MAX_PACKBITS_LENGTH, stop - start);
            if (max == 0) return 0;
            max += start;
            byte b_count = 1;
            byte b = data[start];
            for (int c = start + 1; c < max; c++)
                if (data[c] == b)
                    b_count++;
                else
                    break;
            return b_count;
        }

        #region Other overrides

        public override bool Equals(PdfFilter filter) { return filter is PdfRunLengthFilter; }

        public override string ToString()
        {
            return "/RunLengthDecode";
        }

        #endregion
    }
}
