using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Read;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Implements ASCII85Decode Filter
    /// </summary>
    /// <remarks>Overwrites the input array when decoding</remarks>
    public sealed class Pdf85Filter : PdfFilter
    {
        #region Variables and properties

        /// <summary>
        /// End of data mark
        /// </summary>
        const byte EOD = 0x7E;
        const byte EOD2 = 0x3E;

        /// <summary>
        /// This filter's PDF name
        /// </summary>
        public override string Name { get { return "ASCII85Decode"; } }

        #endregion

        #region Init

        #endregion

        #region Decode

        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
            //string debug = PdfHexFilter.HexDump(data);
            int write_pos = 0;
            int[] ba = new int[5];
            int bi;

            //This array need only be 4/5 the size of the original array,
            //as a85 produces 5 bytes per 4 bytes of data.
            byte[] dout = new byte[data.Length];

            for (int c = 0; c < data.Length; c++)
            {
                #region Fetch bytes
                for (int i = 0; true; c++)
                {
                    if (c == data.Length) 
                        throw new PdfFilterException(ErrCode.UnexpectedEOD);
                    int b = data[c];

                    if (b < 33)
                    {
                        //Checks for whitespace (space, \n, \r, \t, ff and \0)
                        if (b == 32 || b == 10 || b == 13 || b == 9 || b == 12 || b == 0)
                            continue;
                        throw new PdfFilterException(PdfType.Integer, ErrCode.OutOfRange);
                    }
                    
                    if (b > 117)
                    {
                        if (b == 122)
                        {
                            if (i != 0) throw new PdfFilterException(PdfType.Integer, ErrCode.OutOfRange);
                            while (i < 5) ba[i++] = 0;
                            //Need to check if the output array is still big enough.
                            if (dout == data && write_pos + 7 >= c || dout != data && write_pos + 7 >= dout.Length)
                            { //And enlarge it if it isn't. 
                              //(Using +7 (4+3) to compensate for the 3 potential "overflow" bytes when writing to
                              //dout at the end of the stream, as well as the 4 bytes the "z" (122) will take.
                                var tmp = new byte[dout.Length * 2];
                                Buffer.BlockCopy(dout, 0, tmp, 0, write_pos);
                                dout = tmp;
                            }
                            break;
                        }
                        if (b == EOD)
                        {
                            c++;
                            if (c < data.Length && data[c] == EOD2)
                            {
                                if (i == 0) goto end;

                                if (i < 5)
                                {
                                    //Apparently, EOD is part of the data. 
                                    ba[i] = 126;
                                    for (int k = i + 1; k < 5; k++) ba[k] = 0;
                                }
                                bi = ba[0] * 85 * 85 * 85 * 85 + ba[1] * 85 * 85 * 85 + ba[2] * 85 * 85
                                     + ba[3] * 85 + ba[4];
                                dout[write_pos++] = (byte)((bi >> 24) & 0xFF);
                                dout[write_pos++] = (byte)((bi >> 16) & 0xFF);
                                dout[write_pos++] = (byte)((bi >> 8) & 0xFF);
                                dout[write_pos] = (byte)(bi & 0xFF);
                                write_pos -= 4 - i;
                                goto end;
                            }
                        }
                        throw new PdfFilterException(PdfType.Integer, ErrCode.OutOfRange);
                    }

                    b -= 33;
                    ba[i++] = b;
                    if (i == 5) break;
                }
                #endregion

                bi = ba[0]*85*85*85*85 + ba[1]*85*85*85 + ba[2]*85*85
                     + ba[3]*85 + ba[4];
                dout[write_pos++] = (byte)((bi >> 24) & 0xFF);
                dout[write_pos++] = (byte)((bi >> 16) & 0xFF);
                dout[write_pos++] = (byte)((bi >> 8) & 0xFF);
                dout[write_pos++] = (byte)(bi & 0xFF);
            }

        end:
            var ret = new byte[write_pos];
            Buffer.BlockCopy(dout, 0, ret, 0, ret.Length);
            return ret;
        }

        /// <summary>
        /// Finds the end by searching for ~>
        /// </summary>
#if LONGPDF
        internal override byte[] FindEnd(Stream source, long startpos, PdfFilterParms fparams)
#else
        internal override byte[] FindEnd(Stream source, int startpos, PdfFilterParms fparams)
#endif
        {
            System.Diagnostics.Debug.Assert(false, "Untested code");
            source.Position = startpos;
            int b, buf_pos = 0;
            var buf = new byte[4096];
            while ((b = source.ReadByte()) != -1)
            {
                buf[buf_pos++] = (byte) b;
                if (buf_pos == buf.Length)
                    Array.Resize<byte>(ref buf, buf.Length * 2);

                if (b == EOD)
                {
                    b = source.ReadByte();
                    buf[buf_pos++] = (byte)b;
                    if (b == EOD2)
                        break;
                    if (buf_pos == buf.Length)
                        Array.Resize<byte>(ref buf, buf.Length * 2);
                }
            }
            Array.Resize<byte>(ref buf, buf_pos);
            return buf;
        }

        #endregion

        #region Overrides

        public override bool Equals(PdfFilter filter)
        {
            return filter is Pdf85Filter;
        }

        public override string ToString()
        {
            return "/ASCII85Decode";
        }

        #endregion
    }
}
