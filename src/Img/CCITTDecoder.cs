//#define STREAM //<-- Adds support for stream sources
using System;
using System.Diagnostics;

#if STREAM
using System.IO;
#endif

#region Notes about CCITT
/* http://www.asmail.be/msg0054894761.html (Joris Van Damme)
** What is proper k value in two-dimensional T.4 encoding? **

The T.4 spec says writers should pick a k value depending on resolution
of the image. The k value determines the maximum number of lines that
are encoded with the two-dimensional compression scheme after a line
with one-dimensional encoding (the "key" line, to use video compression
speak).

Note that a writer is free to insert less then k-1 two-dimensionally
encoded lines at any point. Also note that the differentiation between
one-dimensional and two-dimensional compression is encoded at the start
of each line. Thus, a reader does not need to know what k was used, it
simply reads what compression scheme is used from the first few bits of
each line.

 * 
 * ** What is CCITT RLE and RLEW compression? **

The ITU T.4 and T.6 specifications help define TIFF compression 3 (T.4,
aka Group 3 FAX), and TIFF compression 4 (T.6, aka Group 4 FAX). They do
not directly define TIFF compression 2 (CCITT RLE), nor TIFF compression
32771 (CCITT RLEW). Googling around to double-check my understandings of
these last two, there was surprisingly little I could find. So, here's
to the best of my knowledge what these compression schemes are:

Both ressemble T.4 compression. Neither uses the T4Options tag. The only
difference with classic one-dimensional T.4 compression is the total
absence of EOL codes. Instead of EOL are potentially some fillCS bits with
value 0 to ensure the start of each new line sits on a byte boundary in
RLE compression, or a word boundary in RLEW compression. As the start of
each data block in TIFF sits on a word boundary anyway, there is no
ambiguouity in this description. Nevertheless, as each compressed image
data block is totally self-contained in TIFF, I would propose the offset
of each compressed line relative to the offset of the compressed data as
a whole is what counts, so bad writers that incorrectly dump RLEW
compressed data on odd byte offsets, would have to write each compressed
data line on an odd byte offset too.

I've double-checked my understanding of these compression modes by
writing data with my own proprietary encoder and reading it back with
LibTiff. This works for RLE compression, but does not seem to work for
RLEW compression.
 */
#endregion

namespace PdfLib.Img
{
    /// <summary>
    /// Multithread safe decoder for CCITT data
    /// Lisence: Public domain
    /// </summary>
    /// <remarks>
    /// See: http://www.fileformat.info/mirror/egff/ch09_05.htm
    /// Todo:
    ///   - Uncompressed G3/G4 images - need test images.
    ///   - Perhaps make sure to not draw over the width of the image?
    ///   - More fault tolerance (fill out lines that wasn't completed before returning data perhaps)
    /// </remarks>
    public static class CCITTDecoder
    {
        #region Decode functions

        #region Tiff "G2" decoding

        //Only used in Tiff files. 
        ///// <summary>
        ///// Decodes a byte aligned RLE image.
        ///// </summary>
        ///// <remarks>RLEW should be just RLE with 16-bit word alignement. 
        ///// I.e. alignWord instead of alignByte on line ends</remarks>
        public static bool DecodeG3RLE(int imageWidth, int imageHeight, byte[] encoded_image,
            bool bis1, byte[] target, int offset)
        {
            G3 g3 = new G3(imageWidth, imageHeight, encoded_image, false, bis1, target, offset);

            while (true)
            {
                int code = g3.GetCode();

                //Make-up codes are larger than 63
                if (code > 63)
                {
                    g3.current_run += code;
                    continue;
                }
                else if (code < 0)
                {
                    //EOL and ZERO is not allowed in G3huff images
                    return false;
                }

                //Terminates the run.
                g3.fill(code);

                //Ends lines when they reach imageWidth
                if (g3.run_total >= imageWidth)
                {
                    g3.n_decoded_rows++;
                    g3.run_total = 0;
                    g3.in_white_run = true;
                    g3.ByteAlign();

                    if (g3.n_decoded_rows == imageHeight)
                        return true;
                }
            }
        }

        #endregion

        /// <summary>
        /// Decodes the image data and returns the result as a eight pixels
        /// per byte image.
        /// </summary>
        /// <param name="imageWidth">Width of the output image</param>
        /// <param name="imageHeight">Height of the output image</param>
        /// <param name="encoded_image">Compressed data</param>
        /// <param name="lsb2msb">Endianess, true is little endian</param>
        /// <param name="enc_align">Rows are byte aligned</param>
        /// <param name="EndOfLine">There's EOL markers in the data</param>
        /// <param name="EndOfBlock">There's EOD markers in the data</param>
        /// <param name="options">G3 or G32D</param>
        /// <param name="ENDED_ON_EOD">Decoding ended on End of Data</param>
        /// <returns>Decoded data. Null on data that can't be decoded</returns>
        public static byte[] DecodeG3(int imageWidth, int imageHeight,
            byte[] encoded_image, bool lsb2msb, bool enc_align, bool black_is_1,
            bool EndOfLine, bool EndOfBlock, int options, out bool ENDED_ON_EOD)
        {
            // G32D images are pretty much G4 images with
            // 13-bit EOLs strewned about.
            if ((options & 0x01) != 0)
            {
                var g4 = new G4(imageWidth, imageHeight, encoded_image, false, black_is_1);
                return DecodeG32D(g4, lsb2msb, out ENDED_ON_EOD) ? g4.DecodedData : null;
            }

            var g3 = new G3(imageWidth, imageHeight, encoded_image, false, black_is_1);
            if (DecodeG3(g3, lsb2msb, enc_align, EndOfLine, EndOfBlock, out ENDED_ON_EOD))
                return g3.DecodedData;
            return null;
        }

        /// <summary>
        /// Decodes the image data and returns the result as a eight pixels
        /// per byte image.
        /// </summary>
        /// <param name="imageWidth">Width of the output image</param>
        /// <param name="imageHeight">Height of the output image</param>
        /// <param name="encoded_image">Compressed data</param>
        /// <param name="lsb2msb">Endianess, true is little endian</param>
        /// <param name="enc_align">Rows are byte aligned</param>
        /// <param name="EndOfLine">There's EOL markers in the data</param>
        /// <param name="EndOfBlock">There's EOD markers in the data</param>
        /// <param name="options">G3 or G32D</param>
        /// <param name="ENDED_ON_EOD">Decoding ended on End of Data</param>
        /// <param name="black_is_1">Black pixels will be written as white</param>
        /// <param name="output_buffer">Store for decoded data</param>
        /// <param name="output_offset">Where to start writing in the output buffer</param>
        /// <returns>True if the decoding succeded</returns>
        public static bool DecodeG3(int imageWidth, int imageHeight,
            byte[] encoded_image, bool lsb2msb, bool enc_align, bool black_is_1,
            bool EndOfLine, bool EndOfBlock, int options, out bool ENDED_ON_EOD,
            byte[] output_buffer, int output_offset)
        {
            // G32D images are pretty much G4 images with
            // 13-bit EOLs strewned about.
            if ((options & 0x01) != 0)
                return DecodeG32D(new G4(imageWidth, imageHeight, encoded_image, false, black_is_1, output_buffer, output_offset), lsb2msb, out ENDED_ON_EOD);

            return DecodeG3(new G3(imageWidth, imageHeight, encoded_image, false, black_is_1, output_buffer, output_offset),
                lsb2msb, enc_align, EndOfLine, EndOfBlock, out ENDED_ON_EOD);
        }

#if STREAM

        /// <summary>
        /// Decodes the image data and returns the result as a eight pixels
        /// per byte image.
        /// </summary>
        /// <returns>Decoded data. Null on data that can't be decoded</returns>
        public static byte[] DecodeG3(int imageWidth, int imageHeight,
            Stream encoded_image, bool lsb2msb, bool enc_align,
            bool EndOfLine, bool EndOfBlock, int options, out bool ENDED_ON_EOD)
        {
            // G32D images are pretty much G4 images with
            // 13-bit EOLs strewned about.
            if ((options & 0x01) != 0)
                return DecodeG32D(new G4(imageWidth, imageHeight, encoded_image, false, false), lsb2msb, out ENDED_ON_EOD);

            return DecodeG3(new G3(imageWidth, imageHeight, encoded_image, false, false),
                lsb2msb, enc_align, EndOfLine, EndOfBlock, options, out ENDED_ON_EOD);
        }

#endif

        /// <summary>
        /// Decodes the image data and returns the result as a eight pixels
        /// per byte image.
        /// </summary>
        /// <returns>Decoded data. Null on data that can't be decoded</returns>
        private static bool DecodeG3(G3 g3, bool lsb2msb, bool enc_align,
            bool EndOfLine, bool EndOfBlock, out bool ENDED_ON_EOD)
        {
            ENDED_ON_EOD = false;
            int imageWidth = g3.imageWidth, imageHeight = g3.imageHeight;

            if (lsb2msb) g3.reverse_data();

            while (true) //Bug: Will decode files lacking the first eol.
            {
                int code = g3.GetCode();

                //End of data. We leave it to the caller to worry about how much
                //data is decoded.
                if (code == EOD)
                {
                    if (g3.run_total != 0)
                    {
                        g3.current_run = 0;
                        g3.fill(imageWidth - g3.run_total);
                        g3.n_decoded_rows++;

                        //We only flag this when a row hasn't been fully filled out,
                        //that way one can determine if enough data has been decoded
                        //by looking at this flag and the number of decoded rows
                        // (i.e. flag = false and rows = height means everything is okay)
                        ENDED_ON_EOD = true;
                    }

                    return true;
                }

                if (code == ZERO)
                {
                    //Zeros are only allowed before eol.
                    while (true)
                    {
                        //No need for "hadBits" as we know we
                        //have at least 1
                        g3.ClearBits(1);
                        code = g3.GetCode();
                        if (code == ZERO) continue;

                        //Everything is OK, Pass the bucked 
                        //over to the EoL rutine.
                        if (code == EOL) break;

                        //An error has occured. Let the
                        //error rutine look at it
                        code = ERROR;
                        break;
                    }
                }

                //EOL is for the most part ignored, but are usefull when correcting errors
                if (code == EOL)
                {
                    if ((g3.current_run != 0 || g3.run_total != 0) && imageWidth - g3.run_total > 0)
                    {
                        //Effectivly EoL should only come at the "beginning" of rows, but if they
                        //come in the middle we assume a error. IOW, we fill out the remainds of the
                        //row and continue.
                        g3.current_run = 0;
                        g3.fill(imageWidth - g3.run_total);
                        g3.in_white_run = true;
                        g3.run_total = 0;
                        g3.n_decoded_rows++;
                    }

                    if (EndOfBlock && g3.PeekEoL12())
                    {
                        //If we're expecting a EoB, look for it.
                        g3.ClearBits(12);
                        for (int num_eols = 3; g3.PeekEoL12(); num_eols++)
                        {
                            g3.ClearBits(12);
                            if (num_eols == 6)
                            {
                                //We're at the end of the file, leave it to the 
                                //caller to figure out if we've decoded enough.
                                return true;
                            }
                            g3.ClearBits(12);
                        }
                    }

                    continue;
                }

                if (code == ERROR)
                {
                    if (EndOfLine)
                    {
                        //Only pad up to line end when there's EoL markers, as
                        //it's only then we try to find the next line.
                        if ((g3.current_run != 0 || g3.run_total != 0) && imageWidth - g3.run_total > 0)
                        {
                            g3.current_run = 0;
                            g3.fill(imageWidth - g3.run_total);
                            g3.in_white_run = true;
                            g3.run_total = 0;
                            g3.n_decoded_rows++;
                        }

                        //Search for the next line and decode from there
                        if (enc_align) g3.ByteAlign();
                        while (g3.HasBits(12))
                        {
                            if (g3.PeekEoL12())
                            {
                                g3.ClearBits(12);
                                continue;
                            }
                            if (enc_align) g3.ClearBits(8);
                            else g3.ClearBits(1);
                        }
                    }
                    else
                    {
                        //We must advance a little to prevent eternal looping.
                        g3.ClearBits(1);
                    }

                    //Hopes for the best by simply decodeing onwards.
                    //Not like we can know where the next line starts
                    //anyhow.
                    continue;
                }

                //Make-up codes are larger than 63
                if (code > 63)
                {
                    g3.current_run += code;
                    continue;
                }

                //Terminates the run.
                g3.fill(code);

                if (g3.run_total >= imageWidth)
                {
                    //Byte align has to come right after a full run.
                    if (enc_align) g3.ByteAlign();
                    g3.in_white_run = true;
                    g3.current_run = 0;
                    g3.run_total = 0;
                    g3.n_decoded_rows++;

                    //Some images spesifies their height
                    if (g3.n_decoded_rows == imageHeight)
                        return true;
                }
            }

        }

        /// <summary>
        /// Decodes a G32D encoded image and returns the result
        /// </summary>
        /// <remarks>
        /// G4 and G32D is similar enough that they can probably be handled by
        /// one decoder, but it easier to test when the decoders are sepperate
        /// </remarks>
        /// <param name="lsb2msb">Fill order, default is false</param>
        /// <returns>An array with one byte per pixel</returns>
        private static bool DecodeG32D(G4 g4, bool lsb2msb, out bool ENDED_ON_EOD)
        {
            if (lsb2msb) g4.reverse_data();
            ENDED_ON_EOD = false;
            int imageWidth = g4.imageWidth, imageHeight = g4.imageHeight;

            while (true)
            {
                //G32D expects the file to start with EOL
                int code = g4.GetCode();
                if (code == ZERO)
                {
                    //Note. GetCode does not advance on ZERO or ERROR
                    g4.ClearBits(1);
                    continue;
                }
                if (code != EOL)
                {
                    //todo: Search for EOL and continue from there.
                    return false;
                }

            mode_check:
                if (!g4.HasBits(1))
                {
                    //Unexpected end of file. Arguably one can return what have been decoded.
                    // (That data is stored in DecodedData, and can be fetched from there)
                    ENDED_ON_EOD = true;
                    return false;
                }

                bool mode2d = g4.GetBits(1) == 1;
                g4.ClearBits(1);
                if (mode2d)
                {
                    //EOL + 1 means we got 1D encoding.
                    bool done;
                    if (!DecodeG31D(g4, out done))
                    {
                        //todo: Search for next EOL and continue.
                        return false;
                    }

                    if (done)
                        return true;

                    g4.SetLineASRef();
                    goto mode_check;
                }

            next_mode:
                MODES mode_code = (MODES)g4.GetCode(MODE_TABLE_BITS, MODE_JUMP_TABLE);

                /*debug3 += "" + ++debug + "\t";
                debug2 += "" + g4.a0 + "\t";
                debugs += "" + g4.b1 + "\t";
                if (g4.n_decoded_rows == line + 1)
                {
                  //if (392 < line && line < 395)
                  //Console.Write("Row "+(line+1)+":\n" +debug3 + "\n" + debug2 + "\n" + debugs + "\n");
                  debug3 = "";
                  debug2 = "";
                  debugs = "";
                  line++;
                  //return null;
                }//*/
                //if (debug == 88359)
                //  Console.Write("");
                switch (mode_code)
                {
                    default:
                        return false;

                    /// <summary>
                    /// Pass mode passes the distance from a0 to
                    /// b2 (the first changing element after b1)
                    /// and sets a0 to b2. 
                    /// 
                    /// This is the only time a0 != run_total
                    /// </summary>
                    case MODES.PASS:
                        //Moves b1 to the first changing element of 
                        //the opposite color and to the right of a0. 
                        g4.FindB1();

                        //Finds b2. Since the ref_line consists of 
                        //color alternating enteries b2 is always
                        //to the right of b1. (b2 is the same
                        //color as a0)
                        Debug.Assert(g4.ref_line.Length > g4.ref_line_pos);
                        g4.b1 = g4.ref_line[g4.ref_line_pos++];

                        //Distance from a0 to b2
                        g4.current_run += g4.b1 - g4.a0;

                        //Moves a0 to b2
                        g4.a0 = g4.b1;

                        //Sets b1 to the first changing element after
                        //a0 (and of the opposite color)
                        Debug.Assert(g4.ref_line.Length > g4.ref_line_pos);
                        g4.b1 = g4.ref_line[g4.ref_line_pos++];
                        goto next_mode;

                    case MODES.HORIZONTAL:
                        //Decodes first one color and then the next
                        if (!DecodeH(g4) || !DecodeH(g4))
                            return false;
                        break;

                    case MODES.V0:
                        DecodeV(g4, 0);
                        break;

                    case MODES.VR1:
                        DecodeV(g4, 1);
                        break;

                    case MODES.VR2:
                        DecodeV(g4, 2);
                        break;

                    case MODES.VR3:
                        DecodeV(g4, 3);
                        break;

                    case MODES.VL1:
                        DecodeVL(g4, -1);
                        break;

                    case MODES.VL2:
                        DecodeVL(g4, -2);
                        break;

                    case MODES.VL3:
                        DecodeVL(g4, -3);
                        break;

                    case MODES.EXT2D:
                        return false;

                    case MODES.ERROR:
                        if (!g4.HasBits(MODE_TABLE_BITS) && g4.n_decoded_rows + 1 == imageHeight)
                        {
                            //Slight hack. I have two G32D images. One of these ends up here, seemingly
                            //lacking one row. Todo: Figure out why.
                            return true;
                        }
                        return false;
                }

                //When a0 exceeds the imageWidth, move to the next row
                if (g4.a0 >= imageWidth)
                {
                    g4.n_decoded_rows++;

                    //Checks if we're done decoding.
                    if (g4.n_decoded_rows == imageHeight)
                        return true;

                    //The imaginary pixel is always white.
                    g4.in_white_run = true;

                    g4.SetLineASRef();
                    continue;
                }

                goto next_mode;
            }
        }

        /// <summary>
        /// Does exactly the same as DecodeG3, except tweaked for use by the DecodeG32D funtion.
        /// </summary>
        private static bool DecodeG31D(G3 g3, out bool done)
        {
            done = false;
            while (true)
            {
                int code = g3.GetCode();
                //if (++debug == 182)
                //  Console.Write("");

                /*if (g3.n_decoded_rows == 350 && g3.run_total == 884)
                  Console.Write("Debug");//*/

                if (code == ZERO)
                {
                    //Zeros are only allowed before eol.
                    while (true)
                    {
                        //g3.HasBits(1);
                        g3.ClearBits(1);
                        code = g3.GetCode();
                        if (code == ZERO) continue;
                        if (code == EOL) break;

                        //An error has occured. Todo: fault tolerance
                        return false;
                    }
                }

                if (code == EOL)
                {
                    g3.in_white_run = true;
                    g3.n_eol++;

                    if (g3.run_total >= g3.imageWidth)
                    {
                        g3.n_decoded_rows++;
                        if (g3.n_decoded_rows >= g3.imageHeight)
                        {
                            done = true;
                            return true;
                        }

                        //Theoretically not needed, in fact a non zero value should be treated as 
                        //an error. (current_run contains the amount of pixels waiting to be drawn)
                        g3.current_run = 0;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                    /*g3.current_run = 0;
                    g3.run_total = 0;

                    //6 consecutive eols means the job's done, no File I've tested with bothers
                    //with these though.
                    if (g3.n_eol == 6)
                    {
                        code = ERROR;
                        goto if_error;
                    }
               
                    continue;*/
                }

                //if_error:
                if (code == ERROR)
                {

                    done = true;
                    //The specs say the image should end with 6xEOL but that is not the case
                    //with the images I got. Instead this tests if the image is fully decoded
                    //and returns the data if that's the case. 
                    if (g3.run_total == g3.imageWidth && g3.n_decoded_rows == g3.imageHeight - 1
                        && g3.bit_buffer == 0 && g3.n_bytes_read == g3.source_length)
                        return true;

                    //Todo: Put recovery code here. (Seach for the next EOL and continue from there)
                    return false;
                }

                //Resets the line ending count. Todo: check if the n_eol is an odd value.
                //(1 =  first white run, 0 = other runs, anything else - error)
                g3.n_eol = 0;

                //Make-up codes are larger than 63
                if (code > 63)
                {
                    g3.current_run += code;
                    continue;
                }

                //Terminates the run.
                g3.fill(code);
            }
        }

        #region Tiff G4 decode

        /* For Tiff
        public static byte[] DecodeG4(int imageWidth, int imageHeight, List<byte[]> strips, int rowsperstrip, bool lsb2msb)
        {
            if (strips.Count == 1)
                return DecodeG4(imageWidth, imageHeight, strips[0], lsb2msb);
            int height_remaning = imageHeight;
            byte[] full = new byte[(imageWidth + 7) / 8 * imageHeight];
            int dest_off = 0;
            foreach (byte[] ba in strips)
            {
                int rows = rowsperstrip;
                if (height_remaning < rowsperstrip)
                {
                    rows = height_remaning;
                    if (rows <= 0) return null;
                }
                height_remaning -= rowsperstrip;
                var dec = DecodeG4(imageWidth, rows, ba, lsb2msb);
                if (dec == null) return null;
                Buffer.BlockCopy(dec, 0, full, dest_off, dec.Length);
                dest_off += dec.Length;
            }

            return full;
        }
        */

        #endregion

        /// <summary>
        /// Decodes a G4 encoded image and returns the result
        /// </summary>
        /// <param name="imageWidth">Width of the decoded image</param>
        /// <param name="imageHeight">Height of the decoded image</param>
        /// <param name="encoded_image">The raw encoded image data</param>
        /// <param name="eob">End of Block</param>
        /// <param name="blackis1">Black is 1 instead of 0</param>
        /// <param name="eol">End of line</param>
        /// <param name="enc_align">Data is aligned</param>
        /// <param name="ENDED_ON_EOD">How the image was closed</param>
        /// <returns>1BPP image data or null if decoding failed</returns>
        public static byte[] DecodeG4(int imageWidth, int imageHeight, byte[] encoded_image,
            bool eob, bool blackis1, bool eol, bool enc_align, out bool ENDED_ON_EOD)
        {
            var g4 = new G4(imageWidth, imageHeight, encoded_image, eob, blackis1);
            if (DecodeG4(g4, eol, enc_align, out ENDED_ON_EOD))
                return g4.DecodedData;
            return null;
        }

        /// <summary>
        /// Decodes a G4 encoded image and returns the result
        /// </summary>
        /// <param name="imageWidth">Width of the decoded image</param>
        /// <param name="imageHeight">Height of the decoded image</param>
        /// <param name="encoded_image">The raw encoded image data</param>
        /// <param name="eob">End of Block</param>
        /// <param name="blackis1">Black is 1 instead of 0</param>
        /// <param name="eol">End of line</param>
        /// <param name="enc_align">Data is aligned</param>
        /// <param name="ENDED_ON_EOD">How the image was closed</param>
        /// <param name="target">Array where the result will be written</param>
        /// <param name="target_offset">Offset into the target array</param>
        /// <returns>1BPP image data</returns>
        public static bool DecodeG4(int imageWidth, int imageHeight, byte[] encoded_image,
            bool lsb2msb,
            bool eob, bool blackis1, bool eol, bool enc_align, out bool ENDED_ON_EOD,
            byte[] target, int target_offset)
        {
            var g4 = new G4(imageWidth, imageHeight, encoded_image, eob, blackis1, target, target_offset);
            if (lsb2msb)
                g4.reverse_data();
            return DecodeG4(g4,
                eol, enc_align, out ENDED_ON_EOD);
        }

#if STREAM

        /// <summary>
        /// Decodes a G4 encoded image and returns the result
        /// </summary>
        /// <returns>1BPP image data</returns>
        public static byte[] DecodeG4(int imageWidth, int imageHeight, Stream encoded_image,
            bool eob, bool blackis1, bool eol, bool enc_align, out bool ENDED_ON_EOD)
        {
            return DecodeG4(new G4(imageWidth, imageHeight, encoded_image, eob, blackis1),
                eol, enc_align, out ENDED_ON_EOD);
        }

#endif

        /// <summary>
        /// Decodes a G4 encoded image and returns the result
        /// </summary>
        /// <returns>1BPP image data</returns>
        private static bool DecodeG4(G4 g4, bool eol, bool enc_align, out bool ENDED_ON_EOD)
        {
            ENDED_ON_EOD = false;
            int imageWidth = g4.imageWidth, imageHeight = g4.imageHeight;

            //int debug = 0; String debugs = "", debug2 = "", debug3 = ""; int line = 93, code_nr = 0;
            while (true)
            {
                //if (g4.n_decoded_rows == 689)
                //    Console.Write("");

                MODES mode_code = (MODES)g4.GetCode(MODE_TABLE_BITS, MODE_JUMP_TABLE);

                /*debug3 += "" + ++debug + "\t";
                debug2 += "" + g4.a0 + "\t";
                debugs += "" + g4.b1 + "\t";
                if (g4.n_decoded_rows == line)
                {
                  //if (392 < line && line < 395)
                  //Console.Write("Row "+(line+1)+":\n" +debug3 + "\n" + debug2 + "\n" + debugs + "\n");
                  debug3 = "";
                  debug2 = "";
                  debugs = "";
                  code_nr++;
                  if (code_nr == 13)
                  {
                      //g4.n_decoded_rows++;
                      //return g4.DecodedData;
                      line++;
                  }
                  //line++;
                  //return g4.DecodedData;
                  //return null;
                }//*/
                //if (debug == 121)
                //  Console.Write("");
                //if (g4.n_bytes_read >= 7099)
                //    Console.Write("");

                switch (mode_code)
                {
                    default:
                        goto case MODES.ERROR;

                    /// <summary>
                    /// Pass mode passes the distance from a0 to
                    /// b2 (the first changing element after b1)
                    /// and sets a0 to b2. 
                    /// 
                    /// This is the only time a0 != run_total
                    /// </summary>
                    case MODES.PASS:
                        //Moves b1 to the first changing element of 
                        //the opposite color and to the right of a0. 
                        g4.FindB1();

                        //Finds b2. Since the ref_line consists of 
                        //color alternating enteries b2 is always
                        //to the right of b1. (b2 is the same
                        //color as a0)
                        Debug.Assert(g4.ref_line.Length > g4.ref_line_pos);
                        g4.b1 = g4.ref_line[g4.ref_line_pos++];

                        //Distance from a0 to b2
                        g4.current_run += g4.b1 - g4.a0;

                        //Moves a0 to b2
                        g4.a0 = g4.b1;

                        //Sets b1 to the first changing element after
                        //a0 (and of the opposite color)
                        Debug.Assert(g4.ref_line.Length > g4.ref_line_pos);
                        g4.b1 = g4.ref_line[g4.ref_line_pos++];
                        continue;

                    case MODES.HORIZONTAL:
                        //Decodes first one color and then the next
                        if (!DecodeH(g4) || !DecodeH(g4))
                            goto case MODES.ERROR;
                        break;

                    case MODES.V0:
                        DecodeV(g4, 0);
                        break;

                    case MODES.VR1:
                        DecodeV(g4, 1);
                        break;

                    case MODES.VR2:
                        DecodeV(g4, 2);
                        break;

                    case MODES.VR3:
                        DecodeV(g4, 3);
                        break;

                    case MODES.VL1:
                        DecodeVL(g4, -1);
                        break;

                    case MODES.VL2:
                        DecodeVL(g4, -2);
                        break;

                    case MODES.VL3:
                        DecodeVL(g4, -3);
                        break;

                    case MODES.EXT1D:
                        //Tests for EOL bit.
                        if (g4.HasBits(3))
                        {
                            if (g4.GetBits(3) == 1)
                            {
                                //This is a EOL
                                if ((g4.run_total != 0 || g4.current_run != 0) && imageWidth - g4.run_total > 0)
                                {
                                    g4.current_run = 0;
                                    g4.fill(imageWidth - g4.run_total);
                                    g4.in_white_run = true;
                                    g4.SetLineASRef();
                                    g4.ClearRefLine();
                                    g4.n_decoded_rows++;
                                }
                                g4.ClearBits(3);

                                //If we're expecting a EoB, look for it.
                                if (g4.EndOfBlock && g4.PeekEoL12())
                                {
                                    g4.ClearBits(12);

                                    //We're at the end of the file, leave it to the 
                                    //caller to figure out if we've decoded enough.
                                    goto case MODES.EOD;
                                }

                                continue;
                            }
                            if (g4.GetBits(3) == 7)
                            {
                                g4.ClearBits(3);
                                throw new NotImplementedException("Uncompressed mode");
                                //goto case MODES.EOD;
                            }
                            //var test = g4.GetBits(3);
                            g4.ClearBits(3);
                        }
                        goto case MODES.ERROR;

                    case MODES.ERROR:
                        if (eol)
                        {
                            //Commit the current line.
                            if ((g4.run_total != 0 || g4.current_run != 0) && imageWidth - g4.run_total > 0)
                            {
                                g4.current_run = 0;
                                g4.fill(imageWidth - g4.run_total);
                                g4.in_white_run = true;
                                g4.SetLineASRef();
                                g4.ClearRefLine();
                                g4.n_decoded_rows++;
                            }

                            //Search for the next line and decode from there
                            if (enc_align) g4.ByteAlign();
                            do
                            {
                                mode_code = (MODES)g4.PeekCode(MODE_TABLE_BITS, MODE_JUMP_TABLE);
                                if (mode_code == MODES.EXT1D)
                                {
                                    g4.GetCode(MODE_TABLE_BITS, MODE_JUMP_TABLE);
                                    goto case MODES.EXT1D;
                                }
                                if (enc_align) g4.ClearBits(8);
                                else g4.ClearBits(1);
                            } while (mode_code != MODES.EOD);
                        }

                        //Doing error correction when there's no EoL is tricky
                        //at best, so crossing fingers and hoping for the best
                        continue;

                    case MODES.EOD:
                        //I see G4 files with junk at the end, so we'll have to trust that
                        //we've decoded everything. Leave it to the image class to handle
                        //the junk or fill in lacking data.
                        //if (g4.a0 != 0)
                        //{
                        //    g4.fill(imageWidth - g4.a0);
                        //    g4.n_decoded_rows++;
                        //    ENDED_ON_EOD = false;
                        //}
                        //else
                        //    ENDED_ON_EOD = true;
                        ENDED_ON_EOD = g4.a0 != 0;
                        return true;
                }

                //When a0 exceeds the imageWidth, move to the next row
                if (g4.a0 >= imageWidth)
                {
                    g4.n_decoded_rows++;

                    //Checks if we're done decoding.
                    if (g4.n_decoded_rows == imageHeight)
                        return true;

                    //a0 always starts at 0 on a new row. This is actually an "imaginary"
                    //position right in front of the actual line.
                    g4.a0 = 0;
                    g4.run_total = 0;

                    //The imaginary pixel is always white.
                    g4.in_white_run = true;

                    //Swaps the reference arrays (line <=> ref_line). 
                    int[] tmp = g4.line;

                    //todo: Does not need to be cleared all the way to imageWidth
                    Array.Clear(tmp, g4.line_pos, tmp.Length - g4.line_pos);

                    //Zeroes the old ref line so that it can be reused.
                    // todo: is a new array faster? Perhaps init this to imageWidth and change DetectB1?
                    //       or remove this since only one array actually needs to be cleared. 
                    Array.Clear(g4.ref_line, 0, g4.ref_line.Length);

                    g4.line_pos = 0;
                    g4.line = g4.ref_line;
                    g4.ref_line = tmp;
                    g4.ref_line_pos = 1;

                    //Sets b1 to the first changing element after a0
                    g4.b1 = g4.ref_line[0];

                    if (enc_align) g4.ByteAlign();
                    continue;
                }
            }
        }

        /// <summary>
        /// Decodes right vertical modes and V0
        /// </summary>
        /// <remarks>
        /// In this mode a1 is encoded relative to position b1.
        /// where a1 is the fist pixel of opposite color after a0.
        /// 
        /// a1 is also defined to be n pixels to the right of b1
        /// where n is the distance given by the parameter "l".
        /// </remarks>
        private static void DecodeV(G4 g4, int l)
        {
            //Sets b1 to the right of a0 and on the first
            //element that changes from a0's color to the
            //opposite color.
            g4.FindB1();

            //Draws the line. l is 0, 1, 2 or 3
            g4.fill(g4.b1 - g4.a0 + l);

            //a0 is now at the right to b1 and a0 has also changed
            //color. By moving b1 one to the right its color is
            //changed, but that does not mean it is right to a0 so
            //FindB1 has to be run later.
            Debug.Assert(g4.ref_line.Length > g4.ref_line_pos);
            g4.b1 = g4.ref_line[g4.ref_line_pos++];
            //Increments ref_line_pos so that it points at the next
            //b1 (which is of opposite color of the current)
        }

        /// <summary>
        /// Decodes left vertical modes.
        /// </summary>
        /// <remarks>
        /// In this mode a1 is encoded relative to position b1.
        /// (where a1 is the fist pixel of opposite color after a0.)
        /// 
        /// Example:               .b1
        /// (X) = black    xxx'''''xx
        ///  '  = white    xxx''''xxx
        ///                   ^a0 ^a1 = b1 - 1
        ///     
        /// a1 is defined to be n pixels to the left of b1
        /// where n is the distance given by the parameter "l".
        /// </remarks>
        private static void DecodeVL(G4 g4, int l)
        {
            g4.FindB1();
            g4.fill(g4.b1 - g4.a0 + l);

            //Test image "g4jim.tif" (jim___ah.tif converted to g4 format from the
            //libtiff test images) causes negative ref indexes. Probably a bug in
            //my code but this Scann lets me decode the image. 
            if (g4.ref_line_pos > 0)
            {
                //Sets b1 to an element to the left of a0 but of the same
                //color while ref_line_pos points to the next b1 of the
                //opposite color.
                g4.ref_line_pos--;
                if (g4.ref_line_pos == 0)
                    g4.b1 = 0;
                else
                    g4.b1 = g4.ref_line[g4.ref_line_pos - 1];
            }
        }

        /// <summary>
        /// Decodes a 1D code word. This means one lookup from the
        /// white table and one from the black with potentially more
        /// lookups if there are makeup codes.
        /// </summary>
        /// <remarks>
        /// Horizontal ignores a0 and b1 values but must respect "pass" 
        /// mode. Pass mode is respected by not zeroing current_run 
        /// before the first "fillCS" call.
        /// </remarks>
        /// <returns>True if successful</returns>
        private static bool DecodeH(G4 g4)
        {
            //Loop for make-up codes.
            while (true)
            {
                //Gets the runlength.
                int code = g4.GetCode();

                //G4 does not allow for code words, so fail if we get one.
                if (code < 0)
                    return false;

                //Make-up codes are larger than 63 and needs to be terminated
                //by a sub 63 runlength codes.
                if (code > 63)
                {
                    g4.current_run += code;
                    continue;
                }

                //Paints x number of pixels equal to the runlength + makeup codes
                g4.fill(code);
                return true;
            }
        }

        #endregion

        #region Parameter classes

        /// <summary>
        /// I use this class to hold various variables instead of passing them all
        /// on each method call.
        /// </summary>
        class G3
        {
            #region Variables

            /// <summary>
            /// Used to speed up white drawing
            /// </summary>
            public byte[] _white_run = null;
            public bool UseFastWhiteFill
            {
                get { return _white_run != null; }
                set
                {
                    if (value)
                    {
                        _white_run = new byte[512];
                        for (int c = 0; c < _white_run.Length; c++)
                            _white_run[c] = 255;
                    }
                    else
                    {
                        _white_run = null;
                    }
                }
            }

            /// <summary>
            /// A buffer containing up to 32 undecoded bits
            /// </summary>
            internal uint bit_buffer = 0;

            /// <summary>
            /// Number of bits in the buffer
            /// </summary>
            internal int n_buf_bits = 0;

            /// <summary>
            /// Number of bytes read from the encoded_data array
            /// </summary>
            internal int n_bytes_read = 0;

            /// <summary>
            /// Number of end of lines encountered
            /// </summary>
            internal int n_eol = 0;

            /// <summary>
            /// Number of pixels in the current run
            /// </summary>
            internal int current_run = 0, run_total = 0, actual_height, stride;

            public readonly int imageWidth, imageHeight;

            /// <summary>
            /// Whenever to draw with black or white pixels
            /// </summary>
            public bool in_white_run = true;

            /// <summary>
            /// All the encoded data or null. Used when
            /// byteswapping.
            /// </summary>
            private byte[] encoded_data;

            /// <summary>
            /// From where encoded data is fetched
            /// </summary>
#if STREAM
            private Stream source_data;
#endif
            internal readonly int source_length;

            /// <summary>
            /// The finished decoded data
            /// </summary>
            private byte[] decoded_data;
            private int decode_offset;

            /// <summary>
            /// Returns the decoded data
            /// </summary>
            public byte[] DecodedData
            {
                get
                {
                    int total_size = n_decoded_rows * stride;
                    if (decode_offset == 0 && total_size == decoded_data.Length)
                        return decoded_data;
                    var ret = new byte[total_size];
                    Buffer.BlockCopy(decoded_data, decode_offset, ret, 0, total_size);
                    return ret;
                }
            }

#if DEBUG
            internal string HexEncodedData
            { get { return Pdf.Filter.PdfHexFilter.HexDump(encoded_data); } }
#endif

            //todo: Make into a property
            public int n_decoded_rows = 0;

            /// <summary>
            /// The decode_buffer is byte aligned, this variable stores
            /// the amount of padding needed on each line to align the rows.
            /// </summary>
            /// <remarks>G3/4 don't encode beyond row boundaries, so the padding is
            /// only used for calculating the position.</remarks>
            private readonly int padding;


            public readonly bool EndOfBlock, BlackIs1;

            #endregion

            #region Constructor

            public G3(int width, int height, byte[] encoded_data, bool end_of_block, bool bis1)
                : this(width, height, encoded_data, end_of_block, bis1, null, 0)
            { }

            public G3(int width, int height, byte[] encoded_data, bool end_of_block, bool bis1, byte[] target, int target_offset)
            {
                imageWidth = width;
                imageHeight = height;
                stride = (width + 7) / 8;
                padding = stride * 8 - imageWidth;
                actual_height = height == 0 ? 100 : height;
                decoded_data = target == null ? new byte[target_offset + stride * actual_height] : target;
                decode_offset = target_offset;
                this.encoded_data = encoded_data;
#if STREAM
                this.source_data = new MemoryStream(encoded_data);
#endif
                source_length = encoded_data.Length;

                EndOfBlock = end_of_block;
                UseFastWhiteFill = true;
                BlackIs1 = bis1;
            }

#if STREAM
            public G3(int width, int height, Stream encoded_data, bool end_of_block, bool bis1)
            {
                imageWidth = width;
                imageHeight = height;
                stride = (width + 7) / 8;
                padding = stride * 8 - imageWidth;
                actual_height = (height == 0) ? 100 : height;
                decoded_data = new byte[stride * actual_height];
                this.encoded_data = null;
                this.source_data = encoded_data;
                source_length = (int)encoded_data.Length;

                EndOfBlock = end_of_block;
                UseFastWhiteFill = true;
                BlackIs1 = bis1;
            }
#endif

            #endregion

            #region Bit function

            /// <summary>
            /// Aligns the bit buffer to the next word.
            /// </summary>
            public void WordAlign()
            {
                ByteAlign();
                if ((n_bytes_read - n_buf_bits / 8) % 2 == 0)
                {
                    HasBits(8);
                    ClearBits(8);
                }
            }

            /// <summary>
            /// Aligns the bit buffer to the next byte.
            /// </summary>
            public void ByteAlign()
            {
                //Number of whole bytes in the buffer
                int bytes = n_buf_bits / 8;

                //Number of bits in a partial byte
                int shift = n_buf_bits - bytes * 8;

                //Removes the partial byte.
                ClearBits(shift);
            }

            /// <summary>
            /// Tests if there is enough bits and fills the bit buffer if needed.
            /// </summary>
            /// <param name="n">How many bits are needed. Can't be over 25</param>
            public bool HasBits(int n)
            {
                if (n <= n_buf_bits)
                    return true;

                //How many bytes of avalible space is in the buffer
                int to_offset = 32 - n_buf_bits;
                int to_fill = (32 - n_buf_bits) / 8;

                //Crops the value to not overflow the encoded byte array.
                to_fill = Math.Min(to_fill + n_bytes_read, source_length);
                to_offset -= (to_fill - n_bytes_read) * 8;

                //Fills the buffer. Note, there's no need to clear the buffer since 
                //uncleared bits should always be zero anyway but if there is a need
                //insert "bit_buffer &= 0xFFFFFFFFU >> to_offset;" right after 
                //"int to_offset = (32 - n_buf_bits);"
                uint tmp_buffer = 0;
                while (n_bytes_read < to_fill)
                {
#if STREAM
                    n_bytes_read++;
                    byte r = (byte) source_data.ReadByte();
                    //^ One does not need to check for -1 here.
#else
                    byte r = encoded_data[n_bytes_read++];
#endif
                    tmp_buffer = tmp_buffer << 8 | r;
                    n_buf_bits += 8;
                }
                bit_buffer |= tmp_buffer << to_offset;

                return n <= n_buf_bits;
            }

            /// <summary>
            /// Reverses the bytes in the encoded data. Use a lookup table if
            /// this function needs to be faster.
            /// </summary>
            /// <remarks>
            /// This function is used to support little endian tif files.
            /// </remarks>
            public void reverse_data()
            {
                if (encoded_data == null) throw new NotImplementedException("Byteswapped stream");

                byte[] data = encoded_data;
                encoded_data = new byte[encoded_data.Length];
                for (int c = 0; c < encoded_data.Length; c++)
                {
                    byte val = data[c];
                    byte ret = 0;
                    for (byte i = 0; i < 8; i++)
                        ret = (byte)(ret << 1 | (val & 1 << i) >> i);
                    encoded_data[c] = ret;
                }
#if STREAM
                source_data.Close();
                source_data = new MemoryStream(encoded_data);
#endif
            }

            /// <summary>
            /// Gets n bits from the bit buffer array.
            /// </summary>
            /// <param name="n">Number of bits to fetch</param>
            /// <returns>The bits in the lower end of the int.</returns>
            public int GetBits(int n)
            {
                return (int)(bit_buffer >> 32 - n);
            }

            /// <summary>
            /// Removes n bits from the buffer. Don't exceed n_buf_bits
            /// </summary>
            /// <param name="n">Number of bits to remove.</param>
            public void ClearBits(int n)
            {
                bit_buffer = bit_buffer << n;
                n_buf_bits -= n;
            }

            /// <summary>
            /// Fetches the next code word. 
            /// </summary>
            public int GetCode()
            {
                return in_white_run ? GetCode(WHITE_TABLE_BITS, WHITE_JUMP_TABLE) :
                                        GetCode(BLACK_TABLE_BITS, BLACK_JUMP_TABLE);
            }

            /// <summary>
            /// Peeks the next code word. 
            /// </summary>
            public int PeekCode()
            {
                return in_white_run ? PeekCode(WHITE_TABLE_BITS, WHITE_JUMP_TABLE) :
                                        PeekCode(BLACK_TABLE_BITS, BLACK_JUMP_TABLE);
            }

            /// <summary>
            /// Gets a code from a jump table.
            /// </summary>
            /// <remarks>
            /// The jump table must be formated so that codes with
            /// bit length smaler than jtbl can be taken straight
            /// from the table while codes larger than jtbl contains
            /// a code value that is really a deeper jump into the
            /// table.
            /// 
            /// Just consider as if there's secondary tables appended
            /// after the primary table. The fist lookup say witch
            /// secondary table to use.
            /// </remarks>
            /// <param name="jtbl">Jump table bit length</param>
            /// <param name="table">The jump table, second colum must be "nbits"</param>
            /// <returns>Code words value, -1337 if error</returns>
            public int GetCode(int jtbl, int[,] table)
            {
                int jump;
                if (!HasBits(jtbl))
                {
                    if (n_buf_bits != 0)
                    {
                        //G32D Stripped.tif, converted to PDF, ends up here.
                        //The TIF won't end up here, as it use a buffer as large
                        //as the biggest strip. While the PDF has no extra space.
                        //
                        //This is not an error, however, as the number of bits left
                        //in the buffer may still be suffucient for code word shorter
                        //than the jump table bits (which is an implementation detail).
                        jump = GetBits(n_buf_bits) << jtbl - n_buf_bits;
                    }
                    else
                        //When calling HasBits, the bit buffer is filled. If there's
                        //no bits un said buffer, we know we're at EOD
                        return EOD;
                }
                else
                    jump = GetBits(jtbl);
                int nBits = table[jump, 1];
                int value = table[jump, 0];

                //Makes a second jump in the table. I.e. the first lookup
                //said "look for more bits since this codeword is too big
                //for this table".
                if (nBits > jtbl)
                {
                    if (!HasBits(nBits))
                    {
                        if (n_buf_bits <= 0)
                        {
                            //When calling HasBits, the bit buffer is filled. If there's
                            //no bits in said buffer, we know we're at EOD
                            return EOD;
                        }

                        //Hr mode codes can actually need to read more bits then there is data.
                        //In wich case, we "add zero bits". This is an implementation detail,
                        //there a code word may require less bits than the jump table bits
                        Debug.Assert((bit_buffer & (1 << n_buf_bits) - 1 << 32 - n_buf_bits) == bit_buffer);
                    }

                    jump = (GetBits(nBits) & (1 << nBits - jtbl) - 1) + value;

                    //Value holds the position of the subtable. The upper
                    //bits of the GetBits call is masked out, and then the
                    //lower bits are added to the value.

                    nBits = table[jump, 1];
                    value = table[jump, 0];
                }

                ClearBits(nBits);
                return value;
            }

            /// <summary>
            /// Peeks a code without advancing the stream
            /// </summary>
            /// <remarks>
            /// Primarily for use when correcting errors.
            /// </remarks>
            public int PeekCode(int jtbl, int[,] table)
            {
                if (!HasBits(jtbl)) return EOD;
                int jump = GetBits(jtbl);
                int nBits = table[jump, 1];
                int value = table[jump, 0];
                if (nBits > jtbl)
                {
                    if (!HasBits(nBits)) return EOD;
                    jump = (GetBits(nBits) & (1 << nBits - jtbl) - 1) + value;
                    nBits = table[jump, 1];
                    value = table[jump, 0];
                }
                return value;
            }

            /// <summary>
            /// Look for a 12bit EoL code
            /// </summary>
            /// <returns>True if EoL was found</returns>
            public bool PeekEoL12()
            {
                if (!HasBits(12)) return false;
                return GetBits(12) == 1;
            }

            #endregion

            #region Fill function

            /// <summary>
            /// Fills up an array.
            /// </summary>
            /// <remarks>
            /// Called often so worth optimizing.
            /// </remarks>
            public virtual void fill(int length)
            {
                //I calc the pos instead of keeping track of it. Probably costs
                //a few CPU cycles.
                int pos = run_total + n_decoded_rows * (imageWidth + padding);
                length += current_run;

                //Enlarge the array as needed.
                if (n_decoded_rows >= actual_height)
                {
                    //When decode_offset != 0, it means the caller supplied the ouput buffer.
                    //Enlarging it is therefore not a option.
                    if (decode_offset != 0)
                        throw new NotSupportedException("Outputbuffer not large enough.");

                    actual_height *= 2;
                    var temp = new byte[actual_height * stride];
                    Buffer.BlockCopy(decoded_data, 0, temp, 0, decoded_data.Length);
                    decoded_data = temp;
                }

                //Records how much was drawn on the current row. Done first since
                //length will be modified by the fillCS algo.
                run_total += length;

                //Prevents drawing over the width of the row
                if (run_total > imageWidth)
                {
                    run_total -= length;
                    length = imageWidth - run_total;
                    run_total = imageWidth;
                }

                #region 1BPP fillCS rutine

                //Alternatly the data can be flipped after it's been decoded.
                bool draw = BlackIs1 ? !in_white_run : in_white_run;

                if (draw) //No need to draw black
                {
                    int array_pos = pos / 8;
                    byte b;

                    //Draws to the next byte boundary.
                    int bits_before = pos % 8;
                    if (bits_before > 0)
                    {
                        int bits_to_draw = Math.Min(8 - bits_before, length);
                        b = decoded_data[decode_offset + array_pos];
                        for (int c = 0; c < bits_to_draw; c++)
                            b |= (byte)(0x80 >> bits_before++);
                        decoded_data[decode_offset + array_pos++] = b;
                        length -= bits_to_draw;
                    }

                    //Draws full bytes
                    if (length > 7)
                    {
                        int bytes_to_draw = length / 8;
                        int i = bytes_to_draw / _white_run.Length;
                        length -= bytes_to_draw * 8;
                        for (int c = 0; c < i; c++)
                        {
                            Buffer.BlockCopy(_white_run, 0, decoded_data, decode_offset + array_pos, _white_run.Length);
                            array_pos += _white_run.Length;
                        }
                        bytes_to_draw -= i * _white_run.Length;
                        Buffer.BlockCopy(_white_run, 0, decoded_data, decode_offset + array_pos, bytes_to_draw);
                        array_pos += bytes_to_draw;
                    }

                    //Draws remainding bits.
                    if (length > 0)
                    {
                        b = 0x80;
                        for (int c = 1; c < length; c++)
                            b |= (byte)(0x80 >> c);
                        decoded_data[decode_offset + array_pos] = b;
                    }
                }

                #endregion

                //Resets the ammount that is to be drawn in one call.
                current_run = 0;

                //Swaps to the black or white run.
                in_white_run = !in_white_run;
            }

            #endregion
        }

        class G4 : G3
        {
            #region Variables

            /// <summary>
            /// The starting changing pixel on the coding line. At the start
            /// of a line a0 is set on an imaginary white pixel situated right
            /// before the first pixel on the line.
            /// </summary>
            public int a0 = 0;

            /// <summary>
            /// First changing element on the reference line on the right of a1
            /// </summary>
            public int b1;

            /// <summary>
            /// The referenc line used for vertical encoding. This line contains
            /// the "a0" positions of the last decoded line. 
            /// </summary>
            /// <remarks>
            /// Set to one pixel bigger than the image's width so that it can
            /// fit an imaginary first pixel.
            /// </remarks>
            public int[] ref_line, line;

            /// <summary>
            /// Position in the corresponding line.
            /// </summary>
            public int line_pos = 0, ref_line_pos = 0;

            #endregion

            #region Constructor

            public G4(int width, int height, byte[] encoded_data, bool eob, bool black_is_1, byte[] target, int target_offset)
                : base(width, height, encoded_data, eob, black_is_1, target, target_offset)
            { Init(); }

            public G4(int width, int height, byte[] encoded_data, bool eob, bool black_is_1)
                : base(width, height, encoded_data, eob, black_is_1)
            { Init(); }

#if STREAM

            public G4(int width, int height, Stream encoded_data, bool eob, bool black_is_1)
                : base(width, height, encoded_data, eob, black_is_1)
            { Init(); }

#endif

            private void Init()
            {
                //Made one bigger to fit an imaginary first pixel
                ref_line = new int[imageWidth + 1];
                line = new int[imageWidth + 1];
                //Issue 76 - T6 CCITT.pdf is an example where "one bigger" isn't enough.
                //atec-2008-01.pdf (page 4) is an example where "two bigger" isn't enough.
                //  todo: look into this.

                //b1 is always "imageWith" on the ref line.
                b1 = imageWidth;
                ref_line[0] = b1;
            }

            #endregion

            /// <summary>
            /// Finds the value of B1.
            /// </summary>
            /// <remarks>
            /// B1 is defined in the specs as "the first changing element 
            /// to the right of a0 of the opposite color."
            /// 
            /// For instance if a0 is white, b1 will be the first black
            /// pixel following a white pixel to the right of a0.
            /// 
            ///                      b1
            /// Ref line:  BBBBBBBBWWBBB 
            /// Code line: BBWWWWWWWWWWW
            ///              a0
            ///           
            /// To simplify finding this "changing element" I keep a record of 
            /// all color transitions, recorded as they are "drawn" by the 
            /// g4.fillCS function.
            /// </remarks>
            public void FindB1()
            {
                //a0 is on a imaginary pixel before the line, that means b1 can be
                //zero (since zero is after '-1'). 
                if (line_pos == 0)
                    return;

                //b1 needs to be to the right of a0 so we loop through the array
                //until a b1 to the right of a0 is found.
                int max = imageWidth <= a0 ? imageWidth : a0 + 1;
                while (b1 < max)
                {
                    //Advance by two positions so that ref_line_pos always points at
                    //the "next" b1. The next b1 must be of the opposite color since
                    //colors alternate from codemode to codemode. 
                    ref_line_pos += 2;

                    //Retrives the stored away pixel change position.
                    b1 = ref_line[ref_line_pos - 1];

                    //b is never zero (Remove this by initing ref_line to imageWidth 
                    //instead of 0)
                    if (b1 == 0)
                        b1 = imageWidth;
                }
            }

            /// <summary>
            /// Fills pixels into the decoded array.
            /// </summary>
            public override void fill(int length)
            {
                //Does the actual filling
                base.fill(length);

                //Records the stop value in the reference array.
                line[line_pos] = run_total;

                //Bad design perhaps. It should be possible to code
                //things so that there's no need to expand these arrays
                line_pos++;
                if (line.Length == line_pos || line.Length == ref_line_pos)
                {
                    //According to my reading of the specs, the worst case 
                    //scenario is one color transition per pixel.
                    //
                    //But my implementation also records 0 length draws,
                    //and that way there can be more than one color transition
                    //per pixel. 
                    Array.Resize(ref line, line.Length + 2);
                    Array.Resize(ref ref_line, line.Length + 2);

                    //Using +2 to avoid "IndexOutOfBounds" later
                }

                //Sets a0 to the amount filled. For the most
                //part a0 equals "run_total", except after PASS 
                //mode since it moves a0 without calling "fillCS".
                a0 = run_total;
            }

            /// <summary>
            /// Swaps the line and ref_lines.
            /// </summary>
            public void SetLineASRef()
            {
                //a0 always starts at 0 on a new row. This is actually an "imaginary"
                //position right in front of the actual line.
                a0 = 0;
                run_total = 0;

                //Swaps the reference arrays (line <=> ref_line). 
                int[] tmp = line;

                //todo: Does not need to be cleared all the way to imageWidth
                Array.Clear(tmp, line_pos, imageWidth - line_pos);

                //Zeroes the old ref line so that it can be reused.
                // todo: is a new array faster? Perhaps init this to imageWidth and change FindB1?
                //       or remove this since only one array actually needs to be cleared. 
                Array.Clear(ref_line, 0, imageWidth);

                line_pos = 0;
                line = ref_line;
                ref_line = tmp;
                ref_line_pos = 1;

                //Sets b1 to the first changing element after a0
                b1 = ref_line[0];
            }

            public void ClearRefLine() { Array.Clear(ref_line, 0, imageWidth); }
        }

        #endregion

        #region Jump tables

        /// <summary>
        /// 8-bits jump table for "white codes", with 4-bit or 1-bit sub tables.
        /// </summary>
        /// <remarks>
        /// I selected 8 bit after observing that the majorety of codes fits into 8-bits 
        /// and thus any table smaller than 8 bits would just lead to several sub tables
        /// taking the place of the smaller main table.
        /// 
        /// Other observations about the codewords:
        ///  - There's no "length" encoding so you can't tell the code's length
        ///    from reading the first bits (but from 4 bits it's possible to know that the 
        ///    code is at most 12, 9, 6 or 5 bits.)
        ///  - A 7-bit lookuptable results in 41 1-bit, 8 2-bits and 2 5-bit subtables
        ///    for a total table size of 306 values (against 304 for 8-bit)
        ///  - A 6-bit lookuptable results in 10 1-bit, 41 2-bits, 8 3-bits and 1 6-bit 
        ///    subtables for a total table size of 324 values
        /// </remarks>
        static readonly int[,] WHITE_JUMP_TABLE = {
          {256,12 },  //  0 End of block
          {288,12 },  //  1 - 1792, 1856, 1920, 1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560
          { 29, 8 },  //  2
          { 30, 8 },  //  3
          { 45, 8 },  //  4
          { 46, 8 },  //  5
          { 22, 7 },  //  6 - 0000011-0
          { 22, 7 },  //  7 - 0000011-1
          { 23, 7 },  //  8 - 0000100-0
          { 23, 7 },  //  9 - 0000100-1
          { 47, 8 },  // 10
          { 48, 8 },  // 11
          { 13, 6 },  // 12 - 000011-00
          { 13, 6 },  // 13 - 000011-01
          { 13, 6 },  // 14 - 000011-10
          { 13, 6 },  // 15 - 000011-11
          { 20, 7 },  // 16 - 0001000-0
          { 20, 7 },  // 17 - 0001000-1
          { 33, 8 },  // 18
          { 34, 8 },  // 19
          { 35, 8 },  // 20
          { 36, 8 },  // 21
          { 37, 8 },  // 22
          { 38, 8 },  // 23
          { 19, 7 },  // 24 - 0001100-0
          { 19, 7 },  // 25 - 0001100-1
          { 31, 8 },  // 26
          { 32, 8 },  // 27
          { 1 , 6 },  // 28 - 000111-00
          { 1 , 6 },  // 29 - 000111-01
          { 1 , 6 },  // 30 - 000111-10
          { 1 , 6 },  // 31 - 000111-11
          { 12, 6 },  // 32 - 001000-00
          { 12, 6 },  // 33 - 001000-01
          { 12, 6 },  // 34 - 001000-10
          { 12, 6 },  // 35 - 001000-11
          { 53, 8 },  // 36
          { 54, 8 },  // 37
          { 26, 7 },  // 38 - 0010011-0
          { 26, 7 },  // 39 - 0010011-1
          { 39, 8 },  // 40
          { 40, 8 },  // 41
          { 41, 8 },  // 42
          { 42, 8 },  // 43
          { 43, 8 },  // 44
          { 44, 8 },  // 45
          { 21, 7 },  // 46 - 0010111-0
          { 21, 7 },  // 47 - 0010111-1
          { 28, 7 },  // 48 - 0011000-0
          { 28, 7 },  // 49 - 0011000-1
          { 61, 8 },  // 50
          { 62, 8 },  // 51
          { 63, 8 },  // 52
          {  0, 8 },  // 53
          {320, 8 },  // 54
          {384, 8 },  // 55
          { 10, 5 },  // 56 - 00111-000
          { 10, 5 },  // 57 - 00111-001
          { 10, 5 },  // 58 - 00111-010
          { 10, 5 },  // 59 - 00111-011
          { 10, 5 },  // 60 - 00111-100
          { 10, 5 },  // 61 - 00111-101
          { 10, 5 },  // 62 - 00111-110
          { 10, 5 },  // 63 - 00111-111
          { 11, 5 },  // 64 - 01000-000
          { 11, 5 },  // 65 - 01000-001
          { 11, 5 },  // 66 - 01000-010
          { 11, 5 },  // 67 - 01000-011
          { 11, 5 },  // 68 - 01000-100
          { 11, 5 },  // 69 - 01000-101
          { 11, 5 },  // 70 - 01000-110
          { 11, 5 },  // 71 - 01000-111
          { 27, 7 },  // 72 - 0100100-0
          { 27, 7 },  // 73 - 0100100-1
          { 59, 8 },  // 74
          { 60, 8 },  // 75
          {274, 9 },  // 76 - 1472, 1536
          {276, 9 },  // 77 - 1600, 1728
          { 18, 7 },  // 78 - 0100111-0
          { 18, 7 },  // 79 - 0100111-1
          { 24, 7 },  // 80 - 0101000-0
          { 24, 7 },  // 81 - 0101000-1
          { 49, 8 },  // 82
          { 50, 8 },  // 83
          { 51, 8 },  // 84
          { 52, 8 },  // 85
          { 25, 7 },  // 86 - 0101011-0
          { 25, 7 },  // 87 - 0101011-1
          { 55, 8 },  // 88
          { 56, 8 },  // 89
          { 57, 8 },  // 90
          { 58, 8 },  // 91
          {192, 6 },  // 92 - 010111-00
          {192, 6 },  // 93 - 010111-01
          {192, 6 },  // 94 - 010111-10
          {192, 6 },  // 95 - 010111-11
          {1664,6 },  // 96 - 011000-00
          {1664,6 },  // 97 - 011000-01
          {1664,6 },  // 98 - 011000-10
          {1664,6 },  // 99 - 011000-11
          {448, 8 },  //100
          {512, 8 },  //101
          {272, 9 },  //102 - 704, 768
          {640, 8 },  //103
          {576, 8 },  //104
          {278, 9 },  //105 - 832, 896
          {280, 9 },  //106 - 960, 1024
          {282, 9 },  //107 - 1088, 1152
          {284, 9 },  //108 - 1216, 1280
          {286, 9 },  //109 - 1344, 1408
          {256, 7 },  //110 - 0110111-0
          {256, 7 },  //111 - 0110111-1
          { 2 , 4 },  //112 - 0111-0000
          { 2 , 4 },  //113 - 0111-0001
          { 2 , 4 },  //114 - 0111-0010
          { 2 , 4 },  //115 - 0111-0011
          { 2 , 4 },  //116 - 0111-0100
          { 2 , 4 },  //117 - 0111-0101
          { 2 , 4 },  //118 - 0111-0110
          { 2 , 4 },  //119 - 0111-0111
          { 2 , 4 },  //120 - 0111-1000
          { 2 , 4 },  //121 - 0111-1001
          { 2 , 4 },  //122 - 0111-1010
          { 2 , 4 },  //123 - 0111-1011
          { 2 , 4 },  //124 - 0111-1100
          { 2 , 4 },  //125 - 0111-1101
          { 2 , 4 },  //126 - 0111-1110
          { 2 , 4 },  //127 - 0111-1111
          { 3 , 4 },  //128 - 1000-0000
          { 3 , 4 },  //129 - 1000-0001
          { 3 , 4 },  //130 - 1000-0010
          { 3 , 4 },  //131 - 1000-0011
          { 3 , 4 },  //132 - 1000-0100
          { 3 , 4 },  //133 - 1000-0101
          { 3 , 4 },  //134 - 1000-0110
          { 3 , 4 },  //135 - 1000-0111
          { 3 , 4 },  //136 - 1000-1000
          { 3 , 4 },  //137 - 1000-1001
          { 3 , 4 },  //138 - 1000-1010
          { 3 , 4 },  //139 - 1000-1011
          { 3 , 4 },  //140 - 1000-1100
          { 3 , 4 },  //141 - 1000-1101
          { 3 , 4 },  //142 - 1000-1110
          { 3 , 4 },  //143 - 1000-1111
          {128, 5 },  //144 - 10010-000
          {128, 5 },  //145 - 10010-001
          {128, 5 },  //146 - 10010-010
          {128, 5 },  //147 - 10010-011
          {128, 5 },  //148 - 10010-100
          {128, 5 },  //149 - 10010-101
          {128, 5 },  //150 - 10010-110
          {128, 5 },  //151 - 10010-111
          { 8 , 5 },  //152 - 10011-000
          { 8 , 5 },  //153 - 10011-001
          { 8 , 5 },  //154 - 10011-010
          { 8 , 5 },  //155 - 10011-011
          { 8 , 5 },  //156 - 10011-100
          { 8 , 5 },  //157 - 10011-101
          { 8 , 5 },  //158 - 10011-110
          { 8 , 5 },  //159 - 10011-111
          { 9 , 5 },  //160 - 10100-000
          { 9 , 5 },  //161 - 10100-001
          { 9 , 5 },  //162 - 10100-010
          { 9 , 5 },  //163 - 10100-011
          { 9 , 5 },  //164 - 10100-100
          { 9 , 5 },  //165 - 10100-101
          { 9 , 5 },  //166 - 10100-110
          { 9 , 5 },  //167 - 10100-111
          { 16, 6 },  //168 - 101010-00
          { 16, 6 },  //169 - 101010-01
          { 16, 6 },  //170 - 101010-10
          { 16, 6 },  //171 - 101010-11
          { 17, 6 },  //172 - 101011-00
          { 17, 6 },  //173 - 101011-01
          { 17, 6 },  //174 - 101011-10
          { 17, 6 },  //175 - 101011-11
          { 4 , 4 },  //176 - 1011-0000
          { 4 , 4 },  //177 - 1011-0001
          { 4 , 4 },  //178 - 1011-0010
          { 4 , 4 },  //179 - 1011-0011
          { 4 , 4 },  //180 - 1011-0100
          { 4 , 4 },  //181 - 1011-0101
          { 4 , 4 },  //182 - 1011-0110
          { 4 , 4 },  //183 - 1011-0111
          { 4 , 4 },  //184 - 1011-1000
          { 4 , 4 },  //185 - 1011-1001
          { 4 , 4 },  //186 - 1011-1010
          { 4 , 4 },  //187 - 1011-1011
          { 4 , 4 },  //188 - 1011-1100
          { 4 , 4 },  //189 - 1011-1101
          { 4 , 4 },  //190 - 1011-1110
          { 4 , 4 },  //191 - 1011-1111
          { 5 , 4 },  //192 - 1100-0000
          { 5 , 4 },  //193 - 1100-0001
          { 5 , 4 },  //194 - 1100-0010
          { 5 , 4 },  //195 - 1100-0011
          { 5 , 4 },  //196 - 1100-0100
          { 5 , 4 },  //197 - 1100-0101
          { 5 , 4 },  //198 - 1100-0110
          { 5 , 4 },  //199 - 1100-0111
          { 5 , 4 },  //200 - 1100-1000
          { 5 , 4 },  //201 - 1100-1001
          { 5 , 4 },  //202 - 1100-1010
          { 5 , 4 },  //203 - 1100-1011
          { 5 , 4 },  //204 - 1100-1100
          { 5 , 4 },  //205 - 1100-1101
          { 5 , 4 },  //206 - 1100-1110
          { 5 , 4 },  //207 - 1100-1111
          { 14, 6 },  //208 - 110100-00
          { 14, 6 },  //209 - 110100-01
          { 14, 6 },  //210 - 110100-10
          { 14, 6 },  //211 - 110100-11
          { 15, 6 },  //212 - 110101-00
          { 15, 6 },  //213 - 110101-01
          { 15, 6 },  //214 - 110101-10
          { 15, 6 },  //215 - 110101-11
          { 64, 5 },  //216 - 11011-000
          { 64, 5 },  //217 - 11011-001
          { 64, 5 },  //218 - 11011-010
          { 64, 5 },  //219 - 11011-011
          { 64, 5 },  //220 - 11011-100
          { 64, 5 },  //221 - 11011-101
          { 64, 5 },  //222 - 11011-110
          { 64, 5 },  //223 - 11011-111
          { 6 , 4 },  //224 - 1110-0000
          { 6 , 4 },  //225 - 1110-0001
          { 6 , 4 },  //226 - 1110-0010
          { 6 , 4 },  //227 - 1110-0011
          { 6 , 4 },  //228 - 1110-0100
          { 6 , 4 },  //229 - 1110-0101
          { 6 , 4 },  //230 - 1110-0110
          { 6 , 4 },  //231 - 1110-0111
          { 6 , 4 },  //232 - 1110-1000
          { 6 , 4 },  //233 - 1110-1001
          { 6 , 4 },  //234 - 1110-1010
          { 6 , 4 },  //235 - 1110-1011
          { 6 , 4 },  //236 - 1110-1100
          { 6 , 4 },  //237 - 1110-1101
          { 6 , 4 },  //238 - 1110-1110
          { 6 , 4 },  //239 - 1110-1111
          { 7 , 4 },  //240 - 1111-0000
          { 7 , 4 },  //241 - 1111-0001
          { 7 , 4 },  //242 - 1111-0010
          { 7 , 4 },  //243 - 1111-0011
          { 7 , 4 },  //244 - 1111-0100
          { 7 , 4 },  //245 - 1111-0101
          { 7 , 4 },  //246 - 1111-0110
          { 7 , 4 },  //247 - 1111-0111
          { 7 , 4 },  //248 - 1111-1000
          { 7 , 4 },  //249 - 1111-1001
          { 7 , 4 },  //250 - 1111-1010
          { 7 , 4 },  //251 - 1111-1011
          { 7 , 4 },  //252 - 1111-1100
          { 7 , 4 },  //253 - 1111-1101
          { 7 , 4 },  //254 - 1111-1110
          { 7 , 4 },  //255 - 1111-1111
          //This is the table for codes: End of line (4-bits, 16 values)
          {ZERO, 0}, //256 - 12 bits of zeros gives this
          {EOL, 12}, //257 - End of line code word
          {ERROR,0}, //258
          {ERROR,0}, //259
          {ERROR,0}, //260
          {ERROR,0}, //261
          {ERROR,0}, //262
          {ERROR,0}, //263
          {ERROR,0}, //264
          {ERROR,0}, //265
          {ERROR,0}, //266
          {ERROR,0}, //267
          {ERROR,0}, //268
          {ERROR,0}, //269
          {ERROR,0}, //270
          {ERROR,0}, //271
          //Table for 704 and 768 (1-bit)
          {704, 9 }, //272
          {768, 9 }, //273
          //Table for 1472 and 1536 (1-bit)
          {1472,9 }, //274
          {1536,9 }, //275
          //Table for 1600 and 1728 (1-bit)
          {1600,9 }, //276
          {1728,9 }, //277
          //Table for 832 and 896 (1-bit)
          {832, 9 }, //278
          {896, 9 }, //279
          //Table for 960 and 1024 (1-bit)
          {960, 9 }, //280
          {1024,9 }, //281
          //Table for 1088 and 1152 (1-bit)
          {1088,9 }, //282
          {1152,9 }, //283
          //Table for 1216 and 1280 (1-bit)
          {1216,9 }, //284
          {1280,9 }, //285
          //Table for 1344 and 1408 (1-bit)
          {1344,9 }, //286
          {1408,9 }, //287
          //Table for 1792, 1856, 1920, 1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560
          //(4-bit table)
          {1792,11}, //288 - 00000001000-0
          {1792,11}, //289 - 00000001000-1
          {1984,12}, //290
          {2048,12}, //291
          {2112,12}, //292
          {2176,12}, //293
          {2240,12}, //294
          {2304,12}, //295
          {1856,11}, //296 - 00000001100-0
          {1856,11}, //297 - 00000001100-1
          {1920,11}, //298 - 00000001101-0
          {1920,11}, //299 - 00000001101-1
          {2368,12}, //300
          {2432,12}, //301
          {2496,12}, //302
          {2560,12}  //303
        };

        const int WHITE_TABLE_BITS = 8;
        const int BLACK_TABLE_BITS = 7;
        //protected const int UNCOMPRESSED_TABLE_BITS = 6;
        const int EOD = -888;
        const int ERROR = -1337;
        const int ZERO = -1;
        const int EOL = -2;

        /// <summary>
        /// 7-bit jump table for black codes with 6-bit sub tables.
        /// </summary>
        /// <remarks>
        /// Unlike the white table, most black codes does not fit into 8-bits 
        /// (most needing 12-bits). Using the same scheme as the white table
        /// will therefore lead into several 5 and 6-bit subtables.
        /// 
        /// Since most codes still "fit 8-bit" after stripping the fist four  
        /// bits, you can use an 4-bit, 8-bit, 1-bit jump table scheme. That  
        /// yields one 16 value table, one 256 value table and 20 2 value  
        /// tables - at the cost of an extra table jump.
        /// 
        /// However I prefer two table jumps and a 7 bit jumptable needs only one
        /// 6-bit and four 5-bit subtables. Which gives 320 vs 312 enteries on the 
        /// above scheme.
        /// 
        /// Observations:
        ///  - All codes over 9 bits start with 4 zeros
        ///  - Codes can span from 2 to 13 bits
        /// </remarks>
        static readonly int[,] BLACK_JUMP_TABLE = {
          {128,12 }, //  0 End of block and 1792-2560
          {160,13 }, //  1 (001) - 18, 24-25, 52-56, 59-60, 64, 320, 384,
                     //            448, 512, 576, 896, 640, 704, 768, 832, 
                     //            960, 1024, 1088, 1152, 1216, 1280, 1344, 
                     //            1408, 1472, 1536, 1600, 1664, 1728
          {224,12 }, //  2 (010) - 13, 16, 23, 44-47, 50-51, 57-58, 61, 256
          {256,12 }, //  3 (011) - 14, 17, 22, 30-33, 40-41, 48-49, 62-63
          { 10, 7 }, //  4
          { 11, 7 }, //  5
          {288,12 }, //  6 (110) - 0, 15, 19-21, 26-29, 34-39, 42-43, 128, 192
          { 12, 7 }, //  7
          { 9 , 6 }, //  8 - 000100-0
          { 9 , 6 }, //  9 - 000100-1
          { 8 , 6 }, // 10 - 000101-0
          { 8 , 6 }, // 11 - 000101-1
          { 7 , 5 }, // 12 - 00011-00
          { 7 , 5 }, // 13 - 00011-01
          { 7 , 5 }, // 14 - 00011-10
          { 7 , 5 }, // 15 - 00011-11
          { 6 , 4 }, // 16 - 0010-000
          { 6 , 4 }, // 17 - 0010-001
          { 6 , 4 }, // 18 - 0010-010
          { 6 , 4 }, // 19 - 0010-011
          { 6 , 4 }, // 20 - 0010-100
          { 6 , 4 }, // 21 - 0010-101
          { 6 , 4 }, // 22 - 0010-110
          { 6 , 4 }, // 23 - 0010-111
          { 5 , 4 }, // 24 - 0011-000
          { 5 , 4 }, // 25 - 0011-001
          { 5 , 4 }, // 26 - 0011-010
          { 5 , 4 }, // 27 - 0011-011
          { 5 , 4 }, // 28 - 0011-100
          { 5 , 4 }, // 29 - 0011-101
          { 5 , 4 }, // 30 - 0011-110
          { 5 , 4 }, // 31 - 0011-111
          { 1 , 3 }, // 32 - 010-0000
          { 1 , 3 }, // 33 - 010-0001
          { 1 , 3 }, // 34 - 010-0010
          { 1 , 3 }, // 35 - 010-0011
          { 1 , 3 }, // 36 - 010-0100
          { 1 , 3 }, // 37 - 010-0101
          { 1 , 3 }, // 38 - 010-0110
          { 1 , 3 }, // 39 - 010-0111
          { 1 , 3 }, // 40 - 010-1000
          { 1 , 3 }, // 41 - 010-1001
          { 1 , 3 }, // 42 - 010-1010
          { 1 , 3 }, // 43 - 010-1011
          { 1 , 3 }, // 44 - 010-1100
          { 1 , 3 }, // 45 - 010-1101
          { 1 , 3 }, // 46 - 010-1110
          { 1 , 3 }, // 47 - 010-1111
          { 4 , 3 }, // 48 - 011-0000
          { 4 , 3 }, // 49 - 011-0001
          { 4 , 3 }, // 50 - 011-0010
          { 4 , 3 }, // 51 - 011-0011
          { 4 , 3 }, // 52 - 011-0100
          { 4 , 3 }, // 53 - 011-0101
          { 4 , 3 }, // 54 - 011-0110
          { 4 , 3 }, // 55 - 011-0111
          { 4 , 3 }, // 56 - 011-1000
          { 4 , 3 }, // 57 - 011-1001
          { 4 , 3 }, // 58 - 011-1010
          { 4 , 3 }, // 59 - 011-1011
          { 4 , 3 }, // 60 - 011-1100
          { 4 , 3 }, // 61 - 011-1101
          { 4 , 3 }, // 62 - 011-1110
          { 4 , 3 }, // 63 - 011-1111
          { 3 , 2 }, // 64 - 10-00000
          { 3 , 2 }, // 65 - 10-00001
          { 3 , 2 }, // 66 - 10-00010
          { 3 , 2 }, // 67 - 10-00011
          { 3 , 2 }, // 68 - 10-00100
          { 3 , 2 }, // 69 - 10-00101
          { 3 , 2 }, // 70 - 10-00110
          { 3 , 2 }, // 71 - 10-00111
          { 3 , 2 }, // 72 - 10-01000
          { 3 , 2 }, // 73 - 10-01001
          { 3 , 2 }, // 74 - 10-01010
          { 3 , 2 }, // 75 - 10-01011
          { 3 , 2 }, // 76 - 10-01100
          { 3 , 2 }, // 77 - 10-01101
          { 3 , 2 }, // 78 - 10-01110
          { 3 , 2 }, // 79 - 10-01111
          { 3 , 2 }, // 80 - 10-10000
          { 3 , 2 }, // 81 - 10-10001
          { 3 , 2 }, // 82 - 10-10010
          { 3 , 2 }, // 83 - 10-10011
          { 3 , 2 }, // 84 - 10-10100
          { 3 , 2 }, // 85 - 10-10101
          { 3 , 2 }, // 86 - 10-10110
          { 3 , 2 }, // 87 - 10-10111
          { 3 , 2 }, // 88 - 10-11000
          { 3 , 2 }, // 89 - 10-11001
          { 3 , 2 }, // 90 - 10-11010
          { 3 , 2 }, // 91 - 10-11011
          { 3 , 2 }, // 92 - 10-11100
          { 3 , 2 }, // 93 - 10-11101
          { 3 , 2 }, // 94 - 10-11110
          { 3 , 2 }, // 95 - 10-11111
          { 2 , 2 }, // 96 - 11-00000
          { 2 , 2 }, // 97 - 11-00001
          { 2 , 2 }, // 98 - 11-00010
          { 2 , 2 }, // 99 - 11-00011
          { 2 , 2 }, //100 - 11-00100
          { 2 , 2 }, //101 - 11-00101
          { 2 , 2 }, //102 - 11-00110
          { 2 , 2 }, //103 - 11-00111
          { 2 , 2 }, //104 - 11-01000
          { 2 , 2 }, //105 - 11-01001
          { 2 , 2 }, //106 - 11-01010
          { 2 , 2 }, //107 - 11-01011
          { 2 , 2 }, //108 - 11-01100
          { 2 , 2 }, //109 - 11-01101
          { 2 , 2 }, //110 - 11-01110
          { 2 , 2 }, //111 - 11-01111
          { 2 , 2 }, //112 - 11-10000
          { 2 , 2 }, //113 - 11-10001
          { 2 , 2 }, //114 - 11-10010
          { 2 , 2 }, //115 - 11-10011
          { 2 , 2 }, //116 - 11-10100
          { 2 , 2 }, //117 - 11-10101
          { 2 , 2 }, //118 - 11-10110
          { 2 , 2 }, //119 - 11-10111
          { 2 , 2 }, //120 - 11-11000
          { 2 , 2 }, //121 - 11-11001
          { 2 , 2 }, //122 - 11-11010
          { 2 , 2 }, //123 - 11-11011
          { 2 , 2 }, //124 - 11-11100
          { 2 , 2 }, //125 - 11-11101
          { 2 , 2 }, //126 - 11-11110
          { 2 , 2 }, //127 - 11-11111
          //This is the table for codes: End of line (5-bits, 32 values)
          {ZERO, 0}, //128 - 12 bits of zeros gives this
          {EOL, 12}, //129 - End of line code word
          {ERROR,0}, //130
          {ERROR,0}, //131
          {ERROR,0}, //132
          {ERROR,0}, //133
          {ERROR,0}, //134
          {ERROR,0}, //135
          {ERROR,0}, //136
          {ERROR,0}, //137
          {ERROR,0}, //138
          {ERROR,0}, //139
          {ERROR,0}, //140
          {ERROR,0}, //141
          {ERROR,0}, //142
          {ERROR,0}, //143
          {1792,11}, //144
          {1792,11}, //145
          {1984,12}, //146
          {2048,12}, //147
          {2112,12}, //148
          {2176,12}, //149
          {2240,12}, //150
          {2304,12}, //151
          {1856,11}, //152
          {1856,11}, //153
          {1920,11}, //154
          {1920,11}, //155
          {2368,12}, //156
          {2432,12}, //157
          {2496,12}, //158
          {2560,12}, //159
          // Table for 18, 24-25, 52-56, 59-60, 64, 320, 384,
          //           448, 512, 576, 896, 640, 704, 768, 832, 
          //           960, 1024, 1088, 1152, 1216, 1280, 1344, 
          //           1408, 1472, 1536, 1600, 1664, 1728 (6-bit)
          { 18, 10}, //160 - 0000001000-000
          { 18, 10}, //161 - 0000001000-001
          { 18, 10}, //162 - 0000001000-010
          { 18, 10}, //163 - 0000001000-011
          { 18, 10}, //164 - 0000001000-100
          { 18, 10}, //165 - 0000001000-101
          { 18, 10}, //166 - 0000001000-110
          { 18, 10}, //167 - 0000001000-111
          { 52, 12}, //168 - 000000100100-0
          { 52, 12}, //169 - 000000100100-1
          {640, 13}, //170
          {704, 13}, //171
          {768, 13}, //172
          {832, 13}, //173
          { 55, 12}, //174 - 000000100111-0
          { 55, 12}, //175 - 000000100111-1
          { 56, 12}, //176 - 000000101000-0
          { 56, 12}, //177 - 000000101000-1
          {1280,13}, //178
          {1344,13}, //179
          {1408,13}, //180
          {1472,13}, //181
          { 59, 12}, //182 - 000000101011-0
          { 59, 12}, //183 - 000000101011-1
          { 60, 12}, //184 - 000000101100-0
          { 60, 12}, //185 - 000000101100-1
          {1536,13}, //186
          {1600,13}, //187
          { 24, 11}, //188 - 00000010111-00
          { 24, 11}, //189 - 00000010111-01
          { 24, 11}, //190 - 00000010111-10
          { 24, 11}, //191 - 00000010111-11
          { 25, 11}, //192 - 00000011000-00
          { 25, 11}, //193 - 00000011000-01
          { 25, 11}, //194 - 00000011000-10
          { 25, 11}, //195 - 00000011000-11
          {1664,13}, //196
          {1728,13}, //197
          {320, 12}, //198 - 000000110011-0
          {320, 12}, //199 - 000000110011-1
          {384, 12}, //200 - 000000110100-0
          {384, 12}, //201 - 000000110100-1
          {448, 12}, //202 - 000000110101-0
          {448, 12}, //203 - 000000110101-0
          {512, 13}, //204
          {576, 13}, //205
          { 53, 12}, //206 - 000000110111-0
          { 53, 12}, //207 - 000000110111-1
          { 54, 12}, //208 - 000000111000-0
          { 54, 12}, //209 - 000000111000-1
          {896, 13}, //210
          {960, 13}, //211
          {1024,13}, //212
          {1088,13}, //213
          {1152,13}, //214
          {1216,13}, //215
          { 64, 10}, //216 - 0000001111-000
          { 64, 10}, //217 - 0000001111-001
          { 64, 10}, //218 - 0000001111-010
          { 64, 10}, //219 - 0000001111-011
          { 64, 10}, //220 - 0000001111-100
          { 64, 10}, //221 - 0000001111-101
          { 64, 10}, //222 - 0000001111-110
          { 64, 10}, //223 - 0000001111-111
          //Table for 13, 16, 23, 44-47, 50-51, 57-58, 61, 256
          { 13, 8 }, //224 - 00000100-0000
          { 13, 8 }, //225 - 00000100-0001
          { 13, 8 }, //226 - 00000100-0010
          { 13, 8 }, //227 - 00000100-0011
          { 13, 8 }, //228 - 00000100-0100
          { 13, 8 }, //229 - 00000100-0101
          { 13, 8 }, //230 - 00000100-0110
          { 13, 8 }, //231 - 00000100-0111
          { 13, 8 }, //232 - 00000100-1000
          { 13, 8 }, //233 - 00000100-1001
          { 13, 8 }, //234 - 00000100-1010
          { 13, 8 }, //235 - 00000100-1011
          { 13, 8 }, //236 - 00000100-1100
          { 13, 8 }, //237 - 00000100-1101
          { 13, 8 }, //238 - 00000100-1110
          { 13, 8 }, //239 - 00000100-1111
          { 23, 11}, //240 - 00000101000-0
          { 23, 11}, //241 - 00000101000-1
          { 50, 12}, //242
          { 51, 12}, //243
          { 44, 12}, //244
          { 45, 12}, //245
          { 46, 12}, //246
          { 47, 12}, //247
          { 57, 12}, //248
          { 58, 12}, //249
          { 61, 12}, //250
          { 256,12}, //251
          { 16, 10}, //252 - 0000010111-00
          { 16, 10}, //253 - 0000010111-01
          { 16, 10}, //254 - 0000010111-10
          { 16, 10}, //255 - 0000010111-11
          //Table for 14, 17, 22, 30-33, 40-41, 48-49, 62-63
          { 17, 10}, //256 - 0000011000-00
          { 17 ,10}, //257 - 0000011000-01
          { 17 ,10}, //258 - 0000011000-10
          { 17 ,10}, //259 - 0000011000-11
          { 48, 12}, //260
          { 49, 12}, //261
          { 62, 12}, //262
          { 63, 12}, //263
          { 30, 12}, //264
          { 31, 12}, //265
          { 32, 12}, //266
          { 33, 12}, //267
          { 40, 12}, //268
          { 41, 12}, //269
          { 22, 11}, //270 - 00000110111-0
          { 22, 11}, //271 - 00000110111-1
          { 14, 8 }, //272 - 00000111-0000
          { 14, 8 }, //273 - 00000111-0001
          { 14, 8 }, //274 - 00000111-0010
          { 14, 8 }, //275 - 00000111-0011
          { 14, 8 }, //276 - 00000111-0100
          { 14, 8 }, //277 - 00000111-0101
          { 14, 8 }, //278 - 00000111-0110
          { 14, 8 }, //279 - 00000111-0111
          { 14, 8 }, //280 - 00000111-1000
          { 14, 8 }, //281 - 00000111-1001
          { 14, 8 }, //282 - 00000111-1010
          { 14, 8 }, //283 - 00000111-1011
          { 14, 8 }, //284 - 00000111-1100
          { 14, 8 }, //285 - 00000111-1101
          { 14, 8 }, //286 - 00000111-1110
          { 14, 8 }, //287 - 00000111-1111
          //Table for 0, 15, 19-21, 26-29, 34-39, 42-43, 128, 192
          { 15, 9 }, //288 - 000011000-000
          { 15, 9 }, //289 - 000011000-001
          { 15, 9 }, //290 - 000011000-010
          { 15, 9 }, //291 - 000011000-011
          { 15, 9 }, //292 - 000011000-100
          { 15, 9 }, //293 - 000011000-101
          { 15, 9 }, //294 - 000011000-110
          { 15, 9 }, //295 - 000011000-111
          {128, 12}, //296
          {192, 12}, //297
          { 26, 12}, //298
          { 27, 12}, //299
          { 28, 12}, //300
          { 29, 12}, //301
          { 19, 11}, //302 - 00001100111-0
          { 19, 11}, //303 - 00001100111-1
          { 20, 11}, //304 - 00001101000-0
          { 20, 11}, //305 - 00001101000-1
          { 34, 12}, //306
          { 35, 12}, //307
          { 36, 12}, //308
          { 37, 12}, //309
          { 38, 12}, //310
          { 39, 12}, //311
          { 21, 11}, //312 - 00001101100-0
          { 21, 11}, //313 - 00001101100-1
          { 42, 12}, //314
          { 43, 12}, //315
          { 0 , 10}, //316 - 0000110111-00
          { 0 , 10}, //317 - 0000110111-01
          { 0 , 10}, //318 - 0000110111-10
          { 0 , 10}  //319 - 0000110111-11
        };

        /// <summary>
        /// Jump table over mode codes. Extension mode codes are treated like 
        /// an 7-bit mode code, ignoring the last 3 bits.
        /// 
        /// I picked a 4-bit jump table since it would give a single 3-bit sub 
        /// table. Didn't spend any time evaluating other bit sizes.
        /// </summary>
        static readonly int[,] MODE_JUMP_TABLE = {
          { 16 , 7 }, //  0 - VR2, VR3, VL2, VL3, EXT, EXT1D
          { (int) MODES.PASS , 4 }, //  1
          { (int) MODES.HORIZONTAL , 3 }, //  2 - 001-0
          { (int) MODES.HORIZONTAL , 3 }, //  3 - 001-1
          { (int) MODES.VL1 , 3 }, //  4 - 010-0
          { (int) MODES.VL1 , 3 }, //  5 - 010-1
          { (int) MODES.VR1 , 3 }, //  6 - 011-0
          { (int) MODES.VR1 , 3 }, //  7 - 010-1
          { (int) MODES.V0 , 1 }, //  8 - 1-000
          { (int) MODES.V0 , 1 }, //  9 - 1-001
          { (int) MODES.V0 , 1 }, // 10 - 1-010
          { (int) MODES.V0 , 1 }, // 11 - 1-011
          { (int) MODES.V0 , 1 }, // 12 - 1-100
          { (int) MODES.V0 , 1 }, // 13 - 1-101
          { (int) MODES.V0 , 1 }, // 14 - 1-110
          { (int) MODES.V0 , 1 }, // 15 - 1-111
          //Table for VR2, VR3, VL2, VL3, EXT
          { (int) MODES.EXT1D , 9 }, //8 - EXT1D (12 bits)
          { (int) MODES.EXT2D , 7 }, //9 - EXT2D (10 bits)
          { (int) MODES.VL3 , 7 }, //10
          { (int) MODES.VR3 , 7 }, //11
          { (int) MODES.VL2 , 6 }, //12 - 10-0
          { (int) MODES.VL2 , 6 }, //13 - 10-1
          { (int) MODES.VR2 , 6 }, //14 - 11-0
          { (int) MODES.VR2 , 6 }, //15 - 11-1
        };
        const int MODE_TABLE_BITS = 4;

        enum MODES
        {
            ERROR = -1337,
            EOD = -888,
            PASS = 0,
            HORIZONTAL,
            V0,
            VR1,
            VR2,
            VR3,
            VL1,
            VL2,
            VL3,
            EXT1D,

            /// <summary>
            /// Extension codes for 2D encoding.
            /// (Ie. not G3 and G4, but G32D)
            /// </summary>
            EXT2D
        }

        #endregion
    }
}
