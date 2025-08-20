using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Implements Hex decoding
    /// </summary>
    public sealed class PdfHexFilter : PdfFilter
    {
        #region Variables and properties

        /// <summary>
        /// End of data mark
        /// </summary>
        const byte EOD = 0x3E;

        /// <summary>
        /// This filter's PDF name
        /// </summary>
        public override string Name { get { return "ASCIIHexDecode"; } }

        #endregion

        #region Encode

        public static byte[] Encode(byte[] data)
        {
            var ret = new byte[data.Length * 2];
            int write_pos = 0;
            for (int c = 0; c < data.Length; c++)
            {
                byte b = data[c];

                //First four bits
                int b1 = (b & 0xF0) >> 4;
                b1 += (b1 < 0x0A) ? 48 : 55;
                ret[write_pos++] = (byte)b1;

                //Next four bits
                b1 = b & 0x0F;
                b1 += (b1 < 0x0A) ? 48 : 55;
                ret[write_pos++] = (byte)b1;
            }
            Debug.Assert(write_pos == ret.Length);
            return ret;
        }

        #endregion

        #region Decode

        /// <summary>
        /// Untested
        /// </summary>
        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
            int write_pos = 0;
            bool shift = false;
            int write = 0;
            byte[] decoded_data = new byte[data.Length / 2];
            for (int c = 0; c < data.Length; c++)
            {
                int b = data[c];
                if (b == EOD)
                {
                    if ((c & 1) == 1)
                    {
                        //If a > sign appear at a odd number, it counts as a 
                        //zero.
                        write_pos++;
                    }
                    break;
                }
                b -= 48;
                if (b > 9)
                {
                    b -= 7;
                    if (b > 15)
                    {
                        b -= 32;

                        //This is an error, as there is no whitespace
                        //this high
                        if (b > 15) 
                            throw new PdfFilterException(Internal.PdfType.Integer, ErrCode.OutOfRange);
                    }
                }
                else if (b < 0)
                {
                    //Checks for whitespace (space, \n, \r, \t, ff and \0)
                    if (b == -16 || b == -38 || b == -35 || b == -39 || b == -36 || b == -48)
                        continue;

                    throw new PdfFilterException(Internal.PdfType.Integer, ErrCode.OutOfRange);
                }
                shift = !shift;
                if (shift) write = b << 4;
                else decoded_data[write_pos++] = (byte)(write | b);
            }
            var ret = new byte[write_pos];
            Buffer.BlockCopy(decoded_data, 0, ret, 0, write_pos);
            return ret;
        }

#if LONGPDF
        internal override byte[] FindEnd(Stream source, long startpos, PdfFilterParms fparams)
#else
        internal override byte[] FindEnd(Stream source, int startpos, PdfFilterParms fparams)
#endif
        {
            var hold = source.Position;
            source.Position = startpos;
            var rec = new Util.RecordStream(4096, source);
            int _last = 0, _cur;
            while (true)
            {
                _cur = rec.ReadByte();
                if (_cur == -1) break;
                const int END = (int)PdfLib.Read.Chars.Greater;
                if (_last == END && _cur == END)
                    return rec.ToArray();
                _last = _cur;
            }
            source.Position = hold;
            return null;
        }

        #endregion

        #region White space encode

        /// <summary>
        /// Encodes data into a single line, with no
        /// columns nor newlines
        /// </summary>
        /// <param name="data">Data to hex encode</param>
        public static byte[] LineEncode(byte[] data)
        {
            if (data.Length == 0) return new byte[0];

            data = Encode(data);

            //Calculates the total length, including white space
            int ret_size = data.Length + (data.Length - 1) / 2;

            var ret = new byte[ret_size];
            for (int c=0, write_pos = 0; ; )
            {
                ret[write_pos++] = data[c++];
                ret[write_pos++] = data[c++];
                if (c == data.Length) break;
                ret[write_pos++] = 32;
            }

            return ret;
        }

        /// <summary>
        /// Outputs xx xx xx xx  xx xx xx xx\n
        /// </summary>
        public static byte[] WhiteEncode(byte[] data)
        {
            data = Encode(data);
            
            //Takes the data length, + white space needed between double bytes + white space
            //needed between every 8 bytes. The -1, +6 offsets is to add/remove whitespace
            //at the right spots. I.e. don't want whitespace after 2 bytes, but want one extra
            //whitespace after 8 (for the extra ' ' in the middle).
            int ret_size = data.Length + (data.Length - 1) / 2 + (data.Length + 6) / 16;

            var ret = new byte[ret_size];
            bool again = true; int write_pos = 0;
            for (int c = 0; c < data.Length; )
            {
                Debug.Assert(write_pos % 25 == 0);
                Debug.Assert(c % 16 == 0);
            again:
                for (int i = 0; i < 4; i++)
                {
                    ret[write_pos++] = data[c];
                    if (++c == data.Length) goto done;
                    ret[write_pos++] = data[c++];
                    if (write_pos == ret_size) goto done;
                    ret[write_pos++] = 32;
                }

                if (write_pos == ret_size) goto done;
                if (again)
                {
                    ret[write_pos++] = 32;
                    again = false;
                    goto again;
                }

                again = true;
                if (write_pos == ret_size) goto done;
                ret[write_pos - 1] = 10;
            }
        done:
            Debug.Assert(write_pos == ret.Length);
            return ret;
        }

        /// <summary>
        /// Outputs fields formated to the integer array
        /// </summary>
        /// <param name="format">
        /// An array such as [1 4 2] gives:
        /// xx - xx xx xx xx - xx xx
        /// </param>
        /// <param name="data">Bytes to convert</param>
        /// <param name="space_sep">If only space should be used as sep</param>
        public static byte[] FormatEncode(byte[] data, int[] format, bool space_sep)
        {
            if (format.Length == 0) return new byte[0];
            byte sep = (byte) ((space_sep) ? 32 : 45);

            data = Encode(data);

            #region Messy code that calcs the correct return array size
            //ret_size must be correct, or the conversion code algo must be changed. Naturally I wrote
            //all this so that I could get away with not modifying the conversion algo LOL.

            //Calculates the number of non-whitespace bytes on each line. 
            int foc = format[0] * 2;
            int org_field_size = foc;
            for (int c = 1; c < format.Length; c++)
                org_field_size += format[c] * 2;

            //Prevents divide by zero
            if (org_field_size == 0)
                return new byte[0];

            //Calculates the number of lines, then how many bytes overflow the last line
            int num_lines = data.Length / org_field_size;
            int ret_size = data.Length + (data.Length - 1) / 2 + num_lines * (format.Length - 1) * 2; //<-- Includes newlines, but not the last \n
            int fraction = data.Length - num_lines * org_field_size;

            //The bytes overflowing the lines are formated with space, need to calc how long the overflow line
            //is with spaces inserted
            bool found = false;
            if (foc == 0)
            {
                ret_size += num_lines;
                throw new NotImplementedException("needs a +1 depending on the fraction");
                //The fraction is the "unfinished last line, i.e +1 if the line is there.
            }
            if (format[format.Length-1] == 0 && format.Length != 1)
            {
                ret_size += num_lines;
                throw new NotImplementedException("needs a +1 depending on the fraction");
                //I.e. +1 if there's line isn't there.
            }
            int new_field_size = foc + 1; //+1 for \n
            int offest = 0;
            for (int c = 1, org = foc; c < format.Length; c++)
            {
                foc = format[c] * 2;
                if (!found)
                {
                    if (fraction <= org)
                    {
                        found = true;
                        if (fraction != 0)
                            offest = (c - 1) * 2; // for "- "
                    }
                    org += foc;
                }
                new_field_size += foc + (foc - 1) / 2 + 3; //+3 for " - "
            }
            if (!found && fraction != 0 && fraction < org_field_size)
                offest = (format.Length-1) * 2;
            ret_size += offest;

            #endregion

            #region Conversion code

            var ret = new byte[ret_size];
            int write_pos = 0;
            int field_pos = 0;
            for (int c = 0; c < data.Length; )
            {
                //Debug.Assert(write_pos % new_field_size == 0);
                Debug.Assert(c % org_field_size == 0);
            again:
                int field = format[field_pos++];
                for (int i = 0; i < field; i++)
                {
                    ret[write_pos++] = data[c];
                    if (++c == data.Length) goto done;
                    ret[write_pos++] = data[c++];
                    if (write_pos == ret_size) goto done;
                    ret[write_pos++] = 32;
                }

                if (write_pos == ret_size) goto done;
                if (field_pos < format.Length)
                {
                    ret[write_pos++] = sep;
                    ret[write_pos++] = 32;
                    goto again;
                }

                ret[write_pos - 1] = 10;
                field_pos = 0;
            }
        done:
            Debug.Assert(write_pos == ret.Length);

            #endregion

            return ret;
        }

        /// <summary>
        /// This function should only be used when debugging, if
        /// you want to use it anyway do a Lexer.GetString(FormatEncode(...))
        /// instead.
        /// </summary>
        public static string HexDump(byte[] data, int[] columns)
        {
            if (data == null) return "null";
            return Read.Lexer.GetString(FormatEncode(data, columns, true));
        }
        public static string HexDump(byte[] data)
        { return HexDump(data, new int[] { 5, 5, 5, 5, 5 }); }

        #endregion

        #region Overrides

        public override bool Equals(PdfFilter filter) { return filter is PdfHexFilter; }

        /// <summary>
        /// Just to feed the debugger
        /// </summary>
        public override string ToString()
        {
            return "/ASCIIHexDecode";
        }

        #endregion
    }
}
