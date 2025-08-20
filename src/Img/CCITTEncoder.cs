/**
 * Encodes 1bit data into CITT G31D or G4 formats
 * Lisence: Public domain
 * 
 * This implementation assumes input data rows to be byte aligned. Should perhaps
 * allow a parameter to disable this, alternativly support 32bit aligned images
 * as well.
 */
//Buffers one rasterline from the source image. This is an advantage when debugging,
//as it's easier to keep track of what's being read.
//#define G4_BUFFER_RASTERLINE
//Seems like the draw color can be calculated, but keeping the code arround just in case.
//#define TRACK_DRAW_COLOR
using System;
using System.IO;
using System.Diagnostics;
using PdfLib.Util;

namespace PdfLib.Img
{
    public static class CCITTEncoder
    {
        public static byte[] EncodeG4(byte[] in_data, int width, int height)
        {
            return EncodeG4(in_data, width, height, false, false, true);
        }

        public static byte[] EncodeG4(byte[] in_data, int width, int height, bool black_is_1, bool byte_align_output, bool insert_end_marker)
        {
            if ((width + 7) / 8 * height > in_data.Length)
                throw new Exception("Not enough data for CCITT image");

            var ms = new MemoryStream(in_data.Length);
            EncodeG4(new MemoryStream(in_data), ms, width, height, black_is_1, byte_align_output, insert_end_marker);
            return ms.ToArray();
        }

        public static void EncodeG4(Stream inn, Stream output, int width, int height, bool black_is_1, bool byte_align_output, bool insert_end_marker)
        {
            var g4 = new G4(inn, (width + 7) / 8, width, black_is_1);
            var bw = new BitWriter(output);

            for (int y = 0; y < height; y++)
            {
#if G4_BUFFER_RASTERLINE

                //Note: Requires that input rows are byte aligned. G4 copies a whole row, byte for byte, to a buffer.
                //This is unlike EncodeG3 where the whole input image is treated as a series of bits. Though I don't
                //think there's any need for this. We'll see.
                g4.ReadLine();
#endif

                g4.Reset();

                //if (y == 682)
                //    y = y;

                while (g4.a0_val < width)
                {
#if G4_BUFFER_RASTERLINE
                    //Note, this assert is not perfect, as it does not take into account g4.has_a1. To function
                    //correctly g4.bs_inn.BitsRead == g4.a1_val when has_a1, and g4.bs_inn.BitsRead == g4.a0_val
                    //when !has_a1
                    Debug.Assert(g4.bs_inn.BitsRead == g4.a0_val || g4.bs_inn.BitsRead == g4.a1_val);
#endif
                    g4.FindA1();
                    g4.FindB1();
                    //b2 is always b1 + 1

                    if (g4.b2_val < g4.a1_val)
                    {
                        //Pass mode coding (Adds b2 - a0, but does not draw)
                        Code code = G4.PASS;

                        //Writes out the code
                        bw.Write(code.Pattern, code.Length);

                        //Puts a0 just under b2
                        // This isn't strickly possible to do on this implementation, but we
                        // work around it by using a a0_val variable and a bool flag. 
                        Debug.Assert(g4.b2_val > g4.a0_val);
                        g4.MoveA0(g4.b2_val);
                    }
                    else
                    {
                        var dist_a1_b1 = g4.a1_val - g4.b1_val;
                        if (Math.Abs(dist_a1_b1) <= 3)
                        {
                            //Vertical mode coding (dist from a0 to b1 + dist_a1_b1)
                            Code code = G4.VMode[3 + dist_a1_b1];

                            //Writes out the code
                            bw.Write(code.Pattern, code.Length);

                            //Puts a0 on a1
                            g4.A0 += 1;
                        }
                        else
                        {
                            g4.FindA2();

                            //Horizontal mode coding
                            Code code = G4.HORZ;

                            //Writes out the code
                            bw.Write(code.Pattern, code.Length);

                            //Writes out the distance
                            g4.WriteRun(bw);

                            //Puts a0 on a2
                            g4.A0 += 2;
                        }
                    }
                }

                //This naturally aligns the output to a byte boundary
                if (byte_align_output)
                    bw.Flush();

                g4.Swap();
            }

            if (insert_end_marker)
            {
                //Writes End of facsimile block (Two EOL)
                bw.Write(G4.EOFB.Pattern, G4.EOFB.Length);
            }

            bw.Dispose();
        }

        public static byte[] EncodeG31D(byte[] in_data, int width, int height)
        {
            return EncodeG31D(in_data, width, height, false, false);
        }

        public static byte[] EncodeG31D(byte[] in_data, int width, int height, bool black_is_1, bool byte_align_output)
        {
            if ((width + 7) / 8 * height > in_data.Length)
                throw new Exception("Not enough data for CCITT image");

            var ms = new MemoryStream(in_data.Length);
            EncodeG31D(new MemoryStream(in_data), ms, width, height, black_is_1, byte_align_output);
            return ms.ToArray();
        }

        /// <summary>
        /// Encodes a 1-bit image into the CCITT G31D format and writes the encoded data to the output stream.
        /// </summary>
        /// <param name="input">The input stream containing the 1-bit image data, assumed to be row aligned on byte bounderies.</param>
        /// <param name="output">The output stream where the encoded data will be written.</param>
        /// <param name="width">The width of the image in pixels.</param>
        /// <param name="height">The height of the image in pixels.</param>
        /// <param name="black_is_1">A boolean flag indicating if black pixels are represented by 1 (true) or 0 (false).</param>
        /// <param name="byte_align_output">A boolean flag indicating if the output should be byte-aligned.</param>
        public static void EncodeG31D(Stream input, Stream output, int width, int height, bool black_is_1, bool byte_align_output)
        {
            var bs = new BitStream(input);
            var bw = new BitWriter(output);

            //Encodes each row and writes the row to the stream
            for (int y = 0; y < height; y++)
            {
                //Pixels on the rows always start on white and then alternate black, white, etc.
                //What we do is count up the number of white bits, write out, then
                //count up the number of black bits, write it out, and continue doing
                //this until we reach the end of the row.
                for (int x = 0; x < width;)
                {
                    //Writes out a white run
                    int nr = black_is_1 ? G3.CountBlackPixels(bs, width - x) : G3.CountWhitePixels(bs, width - x);
                    G3.WriteRun(bw, G3.WhiteMakeUpCodes, G3.WhiteTerminatingCodes, nr);
                    x += nr;

                    if (x >= width)
                        break;

                    //Writes out a black run
                    nr = black_is_1 ? G3.CountWhitePixels(bs, width - x) : G3.CountBlackPixels(bs, width - x);
                    G3.WriteRun(bw, G3.BlackMakeUpCodes, G3.BlackTerminatingCodes, nr);
                    x += nr;
                }

                //This naturally aligns the output to a byte boundary
                if (byte_align_output)
                    bw.Flush();

                //Assumes that input rows are byte aligned.
                bs.ByteAlign();
            }

            bw.Dispose();
        }

        private class G4 : G3
        {
            /// <summary>
            /// The starting changing pixel on the coding line. At the start
            /// of a line a0 is set on an imaginary white pixel situated right
            /// before the first pixel on the line.
            /// </summary>
            /// <remarks>This value is an index into current_line_pos, so to
            /// find the real a0 you must: current_line_pos[a0]</remarks>
            private int a0;

            /// <summary>
            /// The changing pixel after a0. I.e. the first black pixel after a white
            /// a0 pixel, or the first white pixel after a black a0
            /// </summary>
            /// Always a0 + 1
            //public int a1;

            /// <summary>
            /// First changing element on the reference line
            /// </summary>
            /// <remarks>b0 is the search position in witch to find b1. b2 is always b1 + 1</remarks>
            public int b0, b1;

#if G4_BUFFER_RASTERLINE

            /// <summary>
            /// The line we're currently reading from
            /// </summary>
            byte[] current_line;

#endif

            /// <summary>
            /// This is the changing positions of the reference line.
            /// </summary>
            int[] reference_line_pos, current_line_pos;

            /// <summary>
            /// Width of the source and destination image
            /// </summary>
            int width;

            /// <summary>
            /// When moving a0 to a position before a1, this flag i set
            /// so that a1 isn't searched for again on the next loop
            /// </summary>
            bool has_a1;

#if TRACK_DRAW_COLOR
            /// <summary>
            /// Reverses how the image is encoded. Some images compresses more this way.
            /// </summary>
            bool black_is_1;

            /// <summary>
            /// Whenever this is a white or black run
            /// </summary>
            bool in_white_run;
            bool fetch_white { get { return in_white_run != black_is_1; } }
#else
            /// <summary>
            /// Reverses how the image is encoded. Some images compresses more this way.
            /// </summary>
            int black_is_1;

            bool in_white_run { get { return (a0 & 1) == 0; } }
            bool fetch_white { get { return (a0 & 1) == black_is_1; } }
#endif

            public BitStream bs_inn;
#if G4_BUFFER_RASTERLINE
            Stream inn;
#endif

            public int A0
            {
                get { return a0; }
                set
                {
                    a0 = value;
                    if (a0 < current_line_pos.Length)
                        a0_val = current_line_pos[a0];
                    else
                        a0_val = width;
#if TRACK_DRAW_COLOR
                    in_white_run = (a0 & 1) == 0;
#endif
                }
            }

            public int a0_val;

            public int a1_val
            {
                get
                {
                    var a1 = a0 + 1;
                    if (a1 < current_line_pos.Length)
                        return current_line_pos[a1];
                    return width;
                }
            }

            public int a2_val
            {
                get
                {
                    var a2 = a0 + 2;
                    if (a2 < current_line_pos.Length)
                        return current_line_pos[a2];
                    return width;
                }
            }

            public int b1_val
            {
                get
                {
                    if (b1 < reference_line_pos.Length)
                        return reference_line_pos[b1];
                    return width;
                }
            }

            public int b2_val
            {
                get
                {
                    var b2 = b1 + 1;
                    if (b2 < reference_line_pos.Length)
                        return reference_line_pos[b2];
                    return width;
                }
            }

            public G4(Stream inn, int in_stride, int width, bool black_is_1)
            {
                this.width = width;
#if TRACK_DRAW_COLOR
                this.black_is_1 = black_is_1;
#else
                this.black_is_1 = black_is_1 ? 1 : 0;
#endif

#if G4_BUFFER_RASTERLINE
                current_line = new byte[width];
                bs_inn = new BitStream(current_line);
                this.inn = inn;
#else
                bs_inn = new BitStream(inn);
#endif

                //a positions
                current_line_pos = new int[width];

                //b positions
                reference_line_pos = new int[current_line_pos.Length];

                //First line is all white, so the first black pixel is set
                //at a position after the first line.
                reference_line_pos[1] = width;
                if (2 < reference_line_pos.Length)
                    reference_line_pos[2] = width;

                //b2 must also be width. Technically there's b2 white and b2 black,
                //but b2 black is in this case never read.
                if (3 < reference_line_pos.Length)
                    reference_line_pos[3] = width;
            }

            public void WriteRun(BitWriter bw)
            {
                int a1 = a1_val;
                int dist_to_a1 = a1 - a0_val;
                int dist_to_a2 = a2_val - a1;
                if (in_white_run)
                {
                    WriteRun(bw, WhiteMakeUpCodes, WhiteTerminatingCodes, dist_to_a1);
                    WriteRun(bw, BlackMakeUpCodes, BlackTerminatingCodes, dist_to_a2);
                }
                else
                {
                    WriteRun(bw, BlackMakeUpCodes, BlackTerminatingCodes, dist_to_a1);
                    WriteRun(bw, WhiteMakeUpCodes, WhiteTerminatingCodes, dist_to_a2);
                }
            }

            public void MoveA0(int max_value)
            {
                while (a1_val < max_value)
                {
                    a0++;
                    FindA1();
                }
                a0_val = max_value;
#if TRACK_DRAW_COLOR
                in_white_run = (a0 & 1) == 0;
#endif

                has_a1 = a1_val - max_value > 0;
            }

            public void FindA1()
            {
                if (has_a1)
                {
                    //Nothing to do
                    has_a1 = false;
                    return;
                }

                var a1 = a0 + 1;
                if (a1 >= current_line_pos.Length)
                    return;

                int distance = fetch_white ?
                    CountWhitePixels(bs_inn, width - a0_val) :
                    CountBlackPixels(bs_inn, width - a0_val);

                //We find the actual posittion by adding the distance
                //to a0
                current_line_pos[a1] = a0_val + distance;
            }

            public void FindA2()
            {
                var a2 = a0 + 2;
                if (a2 >= current_line_pos.Length)
                    return;

                //The maximum legal distance
                int distance = width - a1_val;

                //a1 will be set one pixel beyond the line, so testing for
                //negative.
                if (distance <= 0)
                {
                    //Setting a1 == a2, so that a null terminating code is written.
                    current_line_pos[a2] = a1_val;
                    return;
                }

                distance = !fetch_white ?
                    CountWhitePixels(bs_inn, distance) :
                    CountBlackPixels(bs_inn, distance);

                //We find the actual posittion by adding the distance
                //to a0
                current_line_pos[a2] = a1_val + distance;
            }

            /// <summary>
            /// B1 is the first pixel to the right, and of color opposite color, to a0
            /// </summary>
            public void FindB1()
            {
                int mod;
                if (in_white_run)
                {
                    //Searches for black b1
                    mod = (b0 & 1) == 1 ? 0 : 1;
                }
                else
                {
                    //Searches for white b1
                    mod = (b0 & 1) == 0 ? 0 : 1;
                }

                b1 = b0 + mod;

                //a0_val is technically -1 in this instance, but this impl.
                //resets a0_val to 0. The algo below would work correctly
                //if a0 was set to an initial -1, but that will break other 
                //code.
                //
                //My mistake was not realizing that b1 can be zero, and this
                //is the quick fix to allow for that to happen. I think a0 == 0
                //is superfluous, but keeping it for now.
                if (a0 == 0 && a0_val == 0) return;

                if (b1 < reference_line_pos.Length)
                {
                    while (reference_line_pos[b1] <= a0_val)
                    {
                        b1 += 2;
                        if (b1 >= reference_line_pos.Length)
                            break;
                    }
                }

                //Advancing b0, but not beyond the "other" b1 color. I.e. if this
                //is b1 black, we set it at the first b1 white position to search
                //from. BTW. This is just a micro optimization, the key is that
                //b0 must always be low enough. 
                b0 = Math.Max(b0, b1 - 1);
            }

#if G4_BUFFER_RASTERLINE

            /// <summary>
            /// Reads one whole raster line from the input.
            /// </summary>
            public void ReadLine()
            {
                inn.Read(current_line, 0, stride);
            }

#endif

            public void Swap()
            {
                var temp = current_line_pos;
                current_line_pos = reference_line_pos;
                reference_line_pos = temp;

                //Zeroing the used part (the +1 is because c < l, not c <= l)
                //int l = Math.Min(current_line_pos.Length, b1+1);
                //for (int c = 0; c < l; c++)
                //    current_line_pos[c] = 0;

                //Makes sure b2 will give end line. This works becase b1 will never
                //move beyond "width", and b2 is b1 + 1. Making sure the array
                //ends with two width thus works.
                int a1 = a0 + 1, w = width;
                if (a1 < reference_line_pos.Length)
                    reference_line_pos[a1] = w;
                //However, there's also b1 for black, so we add width for that one too
                if (++a1 < reference_line_pos.Length)
                    reference_line_pos[a1] = w;

#if !G4_BUFFER_RASTERLINE
                bs_inn.ByteAlign();
#endif

                Debug.Assert(reference_line_pos[a0] == w);
            }

            public void Reset()
            {
#if G4_BUFFER_RASTERLINE
                bs_inn.Reset();
#endif
                a0 = 0;
                a0_val = 0;
                b0 = 0;
#if TRACK_DRAW_COLOR
                in_white_run = true;
#endif
                has_a1 = false;
            }

            public static readonly Code PASS = new Code(0x1, 4);
            public static readonly Code HORZ = new Code(0x1, 3);
            public static readonly Code EOFB = new Code(0x1001, 24);

            public static readonly Code[] VMode =
            {
                new Code(0x2, 7),
                new Code(0x2, 6),
                new Code(0x2, 3),
                new Code(0x1, 1),
                new Code(0x3, 3),
                new Code(0x3, 6),
                new Code(0x3, 7)
            };
        }

        private class G3
        {
            #region Tables

            /// <summary>
            /// Terminaing codes for white pixels. These codes terminates a run.
            /// </summary>
            /// <remarks>Table 2/T6 in the specs</remarks>
            internal static readonly Code[] WhiteTerminatingCodes =
            {
                new Code(0x35, 8),
                new Code(0x07, 6),
                new Code(0x07, 4),
                new Code(0x08, 4),
                new Code(0x0b, 4),
                new Code(0x0c, 4),
                new Code(0x0e, 4),
                new Code(0x0f, 4),
                new Code(0x13, 5),
                new Code(0x14, 5),
                new Code(0x07, 5),
                new Code(0x08, 5),
                new Code(0x08, 6),
                new Code(0x03, 6),
                new Code(0x34, 6),
                new Code(0x35, 6),
                new Code(0x2a, 6),
                new Code(0x2b, 6),
                new Code(0x27, 7),
                new Code(0x0c, 7),
                new Code(0x08, 7),
                new Code(0x17, 7),
                new Code(0x03, 7),
                new Code(0x04, 7),
                new Code(0x28, 7),
                new Code(0x2b, 7),
                new Code(0x13, 7),
                new Code(0x24, 7),
                new Code(0x18, 7),
                new Code(0x02, 8),
                new Code(0x03, 8),
                new Code(0x1a, 8),
                new Code(0x1b, 8),
                new Code(0x12, 8),
                new Code(0x13, 8),
                new Code(0x14, 8),
                new Code(0x15, 8),
                new Code(0x16, 8),
                new Code(0x17, 8),
                new Code(0x28, 8),
                new Code(0x29, 8),
                new Code(0x2a, 8),
                new Code(0x2b, 8),
                new Code(0x2c, 8),
                new Code(0x2d, 8),
                new Code(0x04, 8),
                new Code(0x05, 8),
                new Code(0x0a, 8),
                new Code(0x0b, 8),
                new Code(0x52, 8),
                new Code(0x53, 8),
                new Code(0x54, 8),
                new Code(0x55, 8),
                new Code(0x24, 8),
                new Code(0x25, 8),
                new Code(0x58, 8),
                new Code(0x59, 8),
                new Code(0x5a, 8),
                new Code(0x5b, 8),
                new Code(0x4a, 8),
                new Code(0x4b, 8),
                new Code(0x32, 8),
                new Code(0x33, 8),
                new Code(0x34, 8),
            };

            /// <summary>
            /// Terminaing codes for black pixels. These codes terminates a run.
            /// </summary>
            /// <remarks>Table 2/T6 in the specs</remarks>
            internal static readonly Code[] BlackTerminatingCodes =
            {
                new Code(0x37, 10),
                new Code(0x02,  3),
                new Code(0x03,  2),
                new Code(0x02,  2),
                new Code(0x03,  3),
                new Code(0x03,  4),
                new Code(0x02,  4),
                new Code(0x03,  5),
                new Code(0x05,  6),
                new Code(0x04,  6),
                new Code(0x04,  7),
                new Code(0x05,  7),
                new Code(0x07,  7),
                new Code(0x04,  8),
                new Code(0x07,  8),
                new Code(0x18,  9),
                new Code(0x17, 10),
                new Code(0x18, 10),
                new Code(0x08, 10),
                new Code(0x67, 11),
                new Code(0x68, 11),
                new Code(0x6c, 11),
                new Code(0x37, 11),
                new Code(0x28, 11),
                new Code(0x17, 11),
                new Code(0x18, 11),
                new Code(0xca, 12),
                new Code(0xcb, 12),
                new Code(0xcc, 12),
                new Code(0xcd, 12),
                new Code(0x68, 12),
                new Code(0x69, 12),
                new Code(0x6a, 12),
                new Code(0x6b, 12),
                new Code(0xd2, 12),
                new Code(0xd3, 12),
                new Code(0xd4, 12),
                new Code(0xd5, 12),
                new Code(0xd6, 12),
                new Code(0xd7, 12),
                new Code(0x6c, 12),
                new Code(0x6d, 12),
                new Code(0xda, 12),
                new Code(0xdb, 12),
                new Code(0x54, 12),
                new Code(0x55, 12),
                new Code(0x56, 12),
                new Code(0x57, 12),
                new Code(0x64, 12),
                new Code(0x65, 12),
                new Code(0x52, 12),
                new Code(0x53, 12),
                new Code(0x24, 12),
                new Code(0x37, 12),
                new Code(0x38, 12),
                new Code(0x27, 12),
                new Code(0x28, 12),
                new Code(0x58, 12),
                new Code(0x59, 12),
                new Code(0x2b, 12),
                new Code(0x2c, 12),
                new Code(0x5a, 12),
                new Code(0x66, 12),
                new Code(0x67, 12),
            };

            /// <summary>
            /// Make up codes does not terminate a run. IOW, there will always be a termination code after a make up code
            /// </summary>
            internal static readonly Code[] WhiteMakeUpCodes =
            {
                new Code(0x00,  0), // Dummy entery
                new Code(0x1b,  5), //  64
                new Code(0x12,  5), // 128 (64 + 64) 
                new Code(0x17,  6), // 192 (128 + 64)
                new Code(0x37,  7), // 256 (192 + 64)
                new Code(0x36,  8), // 320 (256 + 64)
                new Code(0x37,  8), // 384 ... + 64
                new Code(0x64,  8), // 448
                new Code(0x65,  8), // 512
                new Code(0x68,  8), // 576
                new Code(0x67,  8), // 640
                new Code(0xcc,  9), // 704
                new Code(0xcd,  9), // 768
                new Code(0xd2,  9), // 832
                new Code(0xd3,  9), // 896
                new Code(0xd4,  9), // 960
                new Code(0xd5,  9), // 1024
                new Code(0xd6,  9), // 1088
                new Code(0xd7,  9), // 1152
                new Code(0xd8,  9), // 1216
                new Code(0xd9,  9), // 1280
                new Code(0xda,  9), // 1344
                new Code(0xdb,  9), // 1408
                new Code(0x98,  9), // 1472
                new Code(0x99,  9), // 1536
                new Code(0x9a,  9), // 1600
                new Code(0x18,  6), // 1664
                new Code(0x9b,  9), // 1728

                // These codes are shared with black
                new Code(0x08, 11), // 1792
                new Code(0x0c, 11), // 1856
                new Code(0x0d, 11), // 1920
                new Code(0x12, 12), // 1984
                new Code(0x13, 12), // 2048
                new Code(0x14, 12), // 2112
                new Code(0x15, 12), // 2176
                new Code(0x16, 12), // 2240
                new Code(0x17, 12), // 2304
                new Code(0x1c, 12), // 2368
                new Code(0x1d, 12), // 2432
                new Code(0x1e, 12), // 2496
                new Code(0x1f, 12), // 2560
            };

            /// <summary>
            /// End of Line code
            /// </summary>
            internal static readonly Code EOL = new Code(0x01, 12);

            internal static readonly Code[] BlackMakeUpCodes =
            {
                new Code(0x00,  0), // Dummy entery
                new Code(0x0f, 10),
                new Code(0xc8, 12),
                new Code(0xc9, 12),
                new Code(0x5b, 12),
                new Code(0x33, 12),
                new Code(0x34, 12),
                new Code(0x35, 12),
                new Code(0x6c, 13),
                new Code(0x6d, 13),
                new Code(0x4a, 13),
                new Code(0x4b, 13),
                new Code(0x4c, 13),
                new Code(0x4d, 13),
                new Code(0x72, 13),
                new Code(0x73, 13),
                new Code(0x74, 13),
                new Code(0x75, 13),
                new Code(0x76, 13),
                new Code(0x77, 13),
                new Code(0x52, 13),
                new Code(0x53, 13),
                new Code(0x54, 13),
                new Code(0x55, 13),
                new Code(0x5a, 13),
                new Code(0x5b, 13),
                new Code(0x64, 13),
                new Code(0x65, 13),

                // These codes are shared with white
                new Code(0x08, 11), // 1792
                new Code(0x0c, 11), // 1856
                new Code(0x0d, 11), // 1920
                new Code(0x12, 12), // 1984
                new Code(0x13, 12), // 2048
                new Code(0x14, 12), // 2112
                new Code(0x15, 12), // 2176
                new Code(0x16, 12), // 2240
                new Code(0x17, 12), // 2304
                new Code(0x1c, 12), // 2368
                new Code(0x1d, 12), // 2432
                new Code(0x1e, 12), // 2496
                new Code(0x1f, 12), // 2560
            };

            #endregion

            /// <summary>
            /// Writes to a run using the supplied tables.
            /// 
            /// The make_up table contains codes that corresponds to length with a multiple of 64.
            /// So we write out as many make up codes as we can, up to the length of the number.
            /// 
            /// Then we finish with a termination code. These codes cover the rage from 0 to 63.
            /// So if we are to write the number 3000, we first write out the highest make up code,
            /// for 2560, then the makeup for 384, and we terminate with 56.
            /// 
            /// I.O.W, 3000 white becomes => 0000000111110011011101011001
            /// Alternativly:                     01100001101100101011001
            /// 
            /// When a CCITT decoder encounter those bitpatterns, it will decode them to 3000.
            /// 
            /// (The smaller bitpattern use the 1664 runlength, which is unusually short for white. It's
            ///  possible to get better compression by prefering that pattern over 2560, but I doubt 
            ///  it's worth the extra effort.)
            /// </summary>
            internal static void WriteRun(BitWriter bw, Code[] make_up, Code[] terminate, int length)
            {
                int index = length / 64;
                int remainder = length % 64;
                Code code;

                while (index > 0)
                {
                    int max = Math.Min(index, 40);
                    code = make_up[max]; //Length written is max * 64
                    bw.Write(code.Pattern, code.Length);
                    index -= max;
                }

                code = terminate[remainder];
                bw.Write(code.Pattern, code.Length);
            }

            /// <summary>
            /// White pixels are 1
            /// </summary>
            /// <param name="r">Source stream</param>
            /// <param name="max">Max number of bits to read from the source stream</param>
            internal static int CountWhitePixels(BitStream r, int max)
            {
                //Reads 24 bits at a time instead of 1 by 1. This is a quick and dirty
                //attempt at optimizing the code, but whenever it's actually faster is
                //another matter. Better still would be to use table lookups. I believe
                //that's the quickest way of counting bits. 
                //
                //Note: Prevents reading beyond max by setting count too high.
                int count = 24;

                //Counts whole bytes first
                while (count < max && r.HasBits(24) && r.PeekBits(24) == 0xFFFFFF)
                {
                    count += 24;
                    r.ClearBits(24);
                }

                //Reducing count to 8 over
                count -= 16;

                //Count bytes
                while (count < max && r.HasBits(8) && r.PeekBits(8) == 0xFF)
                {
                    count += 8;
                    r.ClearBits(8);
                }

                //Now reading one bit at a time, so using the true count value
                count -= 8;

                //Counts single bits
                while (count < max && r.HasBits(1) && r.PeekBits(1) == 1)
                {
                    count++;
                    r.ClearBits(1);
                }

                return count;
            }

            /// <summary>
            /// Black pixels are 0
            /// </summary>
            /// <param name="r">Source stream</param>
            /// <param name="max">Max number of bits to read from the source stream</param>
            internal static int CountBlackPixels(BitStream r, int max)
            {
                //Prevents reading beyond max by setting count too high
                int count = 24;

                //Counts whole bytes first
                while (count < max && r.HasBits(24) && r.PeekBits(24) == 0)
                {
                    count += 24;
                    r.ClearBits(24);
                }

                //Reducing count to 8 over
                count -= 16;

                //Count bytes
                while (count < max && r.HasBits(8) && r.PeekBits(8) == 0)
                {
                    count += 8;
                    r.ClearBits(8);
                }

                //Now reading one bit at a time, so using the true count value
                count -= 8;

                //Counts single bits
                while (count < max && r.HasBits(1) && r.PeekBits(1) == 0)
                {
                    count++;
                    r.ClearBits(1);
                }

                //Count is always 1 over.
                return count;
            }
        }

        /// <summary>
        /// Contains a bitpattern and the length of the bit pattern. 
        /// </summary>
        struct Code
        {
            public readonly int Pattern;
            public readonly int Length;
            public Code(int pat, int l) { Pattern = pat; Length = l; }
            public override string ToString()
            {
                return Convert.ToString(Pattern, 2).PadLeft(Length, '0');
            }
        }
    }
}
