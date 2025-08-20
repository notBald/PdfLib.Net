using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PdfLib.Util
{
    /// <summary>
    /// Decodes LZW data
    /// 
    /// Note, that there are variants to LZW. Pdf supports two variants, with or without early change,
    /// with both variants having an initial codesize of 9 bits. 
    /// </summary>
    /// <remarks>
    /// This implementation takes advantage of how codewords always decode to the same first byte and 
    /// are related to the previous code. The first byte of a codeword is unique to that codeword, 
    /// while all other bytes are equal to the previous codeword. This means if we know the previous 
    /// codeword for the current codeword, we know all the bytes for the codeword.
    ///      
    /// Instead of a dictionary over whole codewords, we bookkeep:
    ///   1. the previous code of a code word
    ///   2. the byte output by that code
    ///      (This is a little odd. The byte is not the byte for the previous code, it's for the
    ///       current code. To get the byte for the previous code, we have to look at the entry
    ///       for the "previous code for the previous code".
    ///  
    /// Bookkeeping layout:
    ///  Assume we're decoding the data: 0, 1, 1, 3, 6, 2 
    ///  
    ///  The bookkeeping array will have some values prefilled, in this instance we have the values 0, 1, and 2 prefilled.
    ///   idx:      Index in the book-keeping array, this index correspond with the code. I.e. idx 5 is equal Code 5
    ///   PrevCode: Previous code. I.e. at idx 0, there is no previous code. At idx 3, in this example the previous code
    ///             will be "0"
    ///   Output:   What byte to output for the code. Note that for the prefiled codes, this values are the same as the
    ///             code itself. 
    /// 
    ///  idx PrevCode  Output 
    ///   0: N/A        "0"
    ///   1: N/A        "1"
    ///   2: N/A        "2"
    ///   3: 0           1 
    ///   4: 1           1 
    ///   5: 1           0 
    ///   6: 3           0
    ///   7: 6           2
    ///   
    ///  The table above is the finished table that we get after decoding the sequence: 0, 1, 1, 3, 6, 2. Now, we
    ///  will go over how this sequence is decoded.
    ///     0: Looks up idx 0 and outputs: 0 (First byte is always written out straight away.)
    ///        Note, that no new entry is made in the dictionary, but after this each code will create a new entry in
    ///              the dictionary.
    ///     1: Looks up idx 1 and outputs: 1 (Codes < 3 is always written out straight away. See algo 0.)
    ///         - At the same time, a new entry is placed in the dictionary. This at position idx 3, and that entry will
    ///           be "0", "1". The "0" is the previous code, i.o.w. the code used before this code, the "1" is what byte we 
    ///           output for this code.
    ///     1: Looks up idx 1 and outputs: 1
    ///         - At the same time, a new entry is placed in the dictionary. This at position idx 4, and that entry will
    ///           be "1", "1". The first "1" is the previous code, the second "1" is what byte we output for this code.
    ///     3. Looks up idx 3 and outputs: 0 1 (Code > 2 but less than idx. See algo 1)
    ///         - At the same time, a new entry is placed in the dictionary. This at position idx 5, and that entry will
    ///           be "1", "0". The "1" is the previous code, the "0" is the first byte we output for this code.
    ///     6. Outputs: 0 1 0 (Code > 2 and >= idx. See algo 2)
    ///         - At the same time, a new entry is placed in the dictionary. This at position idx 6, and that entry will
    ///           be "3", "0". The "3" is the previous code, the "0" is the first byte we output for this code.
    ///     2. Outputs: 2 (Codes < 3 is always written out straight away, by algo 0, and we're done)
    /// 
    ///  Algo 0: (Codes in the default dictionary.)
    ///    Output buffer is overwritten and written out. (Algo 0 is not it's own algorithm, it's what happens with algo 1.)
    /// 
    ///   Algo 1: (Codes in the "dictionary")
    ///    We don't have a dictionary, instead we have an array where each entry has two values:
    ///     current code => { "previous code", "last byte to output for curent code" }
    ///     
    ///    This array is big enough to contain all 4096 codes, with the first 255 codes prefilled.
    ///      
    ///    In the example we encountred code "3". We make a lookup in the array, where we find the values { 0 (prev code), 1 (last byte) }
    /// 
    ///    We put the byte in the output buffer, then grab the next one. Since we're getting the bytes in the revese order than what we want, this
    ///    implementation writes the bytes into an output buffer instead of straight out to the stream. This buffer is also be useful for algo 2, 
    ///    which makes use of data already written.
    ///      
    ///    We then look up the value for the previous code: { N/A, 0 }. This is the last code to lookup since it's < 3, so we put the byte in the output buffer, then
    ///    write that buffer out. 
    ///      
    ///    Note that the buffer is always overwritten, as this algo dosn't use the data in the buffer. The data is not cleared however, so that it
    ///    can be used by algo 2. 
    ///      
    ///   Algo 2: (Codes not in the "dictionary")
    ///    The last time we wrote something out, it was "0 1". That is the previous code, which is not yet put in the "dictionary". Since there is no previous code to lookup,
    ///    but we happen to have the output string in the output buffer, we add the first character of that string to the last pos. "0 1 0". Then we write that out. 
    ///     
    ///    Algo 2 does not overwrite the output buffer.
    ///    
    ///    Note that algo 2 writes data at the bottom of the buffer. This is because it's writing data in the order it will be written out. When the buffer is written out,
    ///    the top is written first, followed by the bottom. 
    ///     
    /// 
    /// To optimize this further: 
    ///  - The output buffer can be written out altogether. That will require a little more smarts in algo 1/2, but I think it will be
    ///    slower as then you will always be writing 1 byte at a time to the output stream.
    ///  - If you simply decompress everything into a buffer, you can replace the output buffer with pointers into the "result buffer" and
    ///    that way eliminate the need for algo 1 to copy from the output buffer to the result buffer. (Algo 2 will be copying from the
    ///    result buffer to the result buffer)
    ///     - Or maybe instead of buffering everything, buffer all output between each clear dict command with a resizable buffer 
    ///       and output to the stream every clear dict.
    ///       
    /// To benchmark this implementation, look for the LZWtests project
    /// </remarks>
    public class LZW
    {
        /// <summary>
        /// The maximum codesize for LZW is 12 bits. A.k.a. 4096 different codes
        /// </summary>
        const int MAX_DICT_SIZE = 4096;

        /// <summary>
        /// Code that tells the decoder the stream is finished
        /// </summary>
        const int EOD = 257;

        /// <summary>
        /// Code that tells the decoder to reset the dictionary to its initial state.
        /// </summary>
        const int CLEAR_TABLE = 256;

        /// <summary>
        /// This is the initial value of the "add code to dictionary" code.
        /// </summary>
        const int ADD_NEW = 258;

        /// <summary>
        /// Represents a node in a linked list of previous codes, with the associated first byte of the current code.
        /// </summary>
        /// <remarks>
        /// Using separate byte and ushort[] (or int[]) arrays instead of this struct might be faster due to better cache locality.
        /// </remarks>
        [DebuggerDisplay("FirstByte: {FirstByte}")]
        private struct CodePoint
        {
            public int PreviousCode;
            public byte FirstByte;

#if DEBUG
            internal string ToString(CodePoint[] siblings)
            {
                return  (PreviousCode != 0 ? siblings[PreviousCode].ToString(siblings) : "") + ((char)FirstByte);
            }
#endif
        }

        /// <summary>
        /// A list of codepoints. Each codepoint contains the first byte the be written along with
        /// a pointer to the former codepoint that this codepoint was constructed from. This way,
        /// we can recreate the data by following the pointers until PreviousCode == 0
        /// </summary>
        /// <remarks>
        /// Since there's only 4096 entries, we create an array big enough to contain all
        /// entries.
        /// </remarks>
        readonly CodePoint[] _code_points = new CodePoint[MAX_DICT_SIZE];

#if DEBUG
        internal string[] CodePoints
        {
            get
            {
                var ret = new string[_code_points.Length];

                for (int c = 0; c < ret.Length; c++)
                {
                    if (_code_points[c].FirstByte != 0 && _code_points[c].PreviousCode != 0)
                        ret[c] = _code_points[c].ToString(_code_points);
                }

                return ret;
            }
        }
#endif

        /// <summary>
        /// When constructing what data to write, it's put on the start and end of this buffer.
        /// 
        /// Algo 1: Uses this buffer to reverse what it's writing
        /// Algo 2: Uses what algo 1 and previous algo 2 has written into the buffer as 
        ///         the start of it's own string.
        /// </summary>
        /// <remarks>
        /// Note the use of MAX_DICT_SIZE. It also gets used in the code, so to change the size the code
        /// must be updated.
        /// </remarks>
        readonly byte[] _output_buffer = new byte[MAX_DICT_SIZE]; // can be smaller.  MAX_DICT_SIZE - 258 I think

        /// <summary>
        /// Convenient pointer to the end of the output buffer
        /// </summary>
        const int LAST_BUFFER_POS = MAX_DICT_SIZE - 1;

        /// <summary>
        /// Initializes a reusable but non-reentrant LZW decoder
        /// </summary>
        public LZW()
        {
            //The first 256 entries are fixed. These will also never be overwritten so this needs
            //only be done once
            for (int c=0; c < CLEAR_TABLE; c++)
                _code_points[c] = new CodePoint() { FirstByte = (byte)c };
        }

        /// <summary>
        /// Decodes a compressed LZW byte array.
        /// </summary>
        /// <param name="input">Compressed LZW array</param>
        /// <param name="early_change">Whether to increase codesize early</param>
        /// <returns>New array with decoded bytes</returns>
        public byte[] Decode(byte[] input, bool early_change)
        {
            var br = new BitStream(input);
            byte[] output = new byte[4096];
            int output_pos = 0;
            int ec = early_change ? 1 : 0;

            while (br.HasBits(9))
            {
                output_pos = Decode(br, ec, ref output, output_pos);
                if (output_pos < 0)
                {
                    //int.MinValue is used in place of "-0"
                    if (output_pos == int.MinValue)
                        output_pos = 0;
                    else
                        //Turns the number positive, it's used to resize the
                        //output array.
                        output_pos *= -1;

                    //Negative value means there's no more data to fetch
                    break;
                }
            }

            Array.Resize(ref output, output_pos);
            return output;
        }

        /// <summary>
        /// Stream based decode
        /// </summary>
        /// <param name="input">Encoded LZW stream</param>
        /// <param name="early_change">Whether to increase codesize early</param>
        /// <param name="output">Decoded stream</param>
        public void Decode(Stream input, bool early_change, Stream output)
        {
            var br = new BitStream(input);
            byte[] output_bytes = new byte[4096];
            int ec = early_change ? 1 : 0;

            while (br.HasBits(9))
            {
                int output_pos = Decode(br, ec, ref output_bytes, 0);
                if (output_pos > 0)
                    output.Write(output_bytes, 0, output_pos);
                else if (output_pos < 0)
                {
                    //int.MinValue is used in place of "-0"
                    if (output_pos != int.MinValue)
                        output.Write(output_bytes, 0, output_pos * -1);

                    //Negative value means there's no more data to fetch, so we
                    //break out of the loop.
                    break;
                }
            }
        }

        /// <summary>
        /// Decode a LZW compressed chunk of data
        /// </summary>
        /// <param name="br">Bit reader</param>
        /// <param name="ec">
        /// Early change is an off by one error that became part of the specs. It causes the code's bitlength to
        /// change earlier, which adds 1 extra bit per code change to the final size and makes the codetable 1 
        /// entry smaller, though some encoders use the full codetable so we have to support that too.
        /// </param>
        /// <param name="output">Buffer bytes will be written to</param>
        /// <param name="output_pos">Current position in the output buffer</param>
        /// <returns>
        /// Positive number = number of bytes in the buffer
        /// Negative number = number of bytes in the buffer, but there's no more data to fetch.
        /// int.MinValue: Indicates no data in the output buffer (used instead of "-0").
        ///  Why not simply use 0? Well, CLEAR_TABLE commands may have zero bytes to output.
        ///  It's possible to make this code more clever, and thus remove the need for int.MinValue.
        ///  I.e. check if output_pos is zero, in that case simply read the next code. Something like that.
        /// </returns>
        private int Decode(BitStream br, int ec, ref byte[] output, int output_pos)
        {   
            //Previous code is simply the code that was fetched before the current code.
            int previous_code, code;

            //For TIFF and PDF the first code length is always 9.
            int code_length = 9;

            //The first code gets special treatment, as it does not yet have a "previous code".
            //To simplify the algorithm we handle this before entering the loop
            code = br.GetBits(code_length);

            if (code >= CLEAR_TABLE)
            {
                //The first code is often a clear table command. We simply ignore them.
                //(Clear table is done regardless of it being commanded) 
                if (code == CLEAR_TABLE)
                    //if output_pos == 0, you could potentially do another br.GetBits(code_length)
                    //until we get a usable code. Then we wouldn't need the int.MinValue special case.
                    // TODO: Do this. But first, check if my thinking is right. That zero is never
                    // returned during normal decoding.
                    return output_pos;
                else
                    //EOD or Error. Since we're error tolerant, we treat this the same way.
                    //Returning int.MinValue since "-0" is not a thing. Alternatively modify
                    //the clear table code as mentioned in the comment above and return "0" here.
                    return int.MinValue;
            }

            //This is the next code that will be created sometimes in the future. We already know 
            //the code as it is always +1 over the last one created
            int next_new_code = ADD_NEW;

            //Before writing to the putput, all writes are put in this buffer. I suspect this buffer
            //can be optimized away by always having enough space at the end of output, but I'm unsure
            //of the benefit as we'll then often have to move data around. 
            int buffer_pos = 0;

            //Pointer to data at the top of the buffer. Notice that we use MAX_DICT_SIZE. The buffer can
            //probably be smaller than this, but I don't think a few hundred bytes matter.
            int buffer_top_pos = MAX_DICT_SIZE;

            //Cache the first byte. This avoids having to figure out where in the buffer it is.
            byte first_byte = (byte)code;

            //There's no need to check the length. We know the buffer is always big enough here.
            //(See comment for upping the buffer size)
            _output_buffer[buffer_pos++] = first_byte;
            output[output_pos++] = first_byte;

            while (br.HasBits(code_length))
            {
                //The previous code is the code that will be added to the "dictionary/cache".
                //Note that in effect this means that codes lag. This means you can't use a
                //code until two codes after. I.e. <create code> <can't use> <can use>
                previous_code = code;

                //We read out the 9-12 bit code word.
                code = br.GetBits(code_length);

                //We stop encoding at either an end of data marker or the outright end of data.
                if (code == EOD)
                    //We know we have at least 1 byte of data, so this will never be "-0"
                    return -output_pos;

                //When we clear the table, we basically restart the algorithm, giving the calle a
                //chance to write out data and such.
                if (code == CLEAR_TABLE)
                    return output_pos;

                //Picks between algo 1 and 2
                if (code < next_new_code)
                {
                    //This is Algo 1. It does not make use of data in the buffer, so we remove it.
                    buffer_pos = 0;

                    //When code is bigger than EOD, the code must be written backwards.  We write these 
                    //codes downwards from the end of the buffer. Codes smaller than EOD is writing to 
                    //the start of the buffer.
                    //
                    //One could remove this check and make the do { } while loop into a plain while loop 
                    //(and that way also write codes < EOD at the end of the buffer) I've not done any 
                    //benchmarking, so this might be one of those wasteful micro-optimizations *.
                    // * Benchmark indicates: A smigend slower on a 7700K, significantly faster on a laptop.
                    if (code > EOD)
                    {
                        int tmp = code;
                        buffer_top_pos = LAST_BUFFER_POS;
                        do
                        {
                            _output_buffer[buffer_top_pos--] = _code_points[tmp].FirstByte;
                            tmp = _code_points[tmp].PreviousCode;
                        } while (tmp > EOD) ;

                        // Could have done something like while (tmp != -1) instead, but for that the
                        // PreviousCode must be initialized to -1. Instead we simply do the end byte
                        // here. (This because the code 0 is valid and thus tmp != 0 would fail for
                        // that). 
                        first_byte = _code_points[tmp].FirstByte;
                        _output_buffer[buffer_top_pos] = first_byte;
                    }
                    else
                    {
                        //Since we write data to the top of the buffer, we also have to reset this data
                        buffer_top_pos = MAX_DICT_SIZE;

                        //Writes data to the bottom of the buffer
                        first_byte = (byte) code;
                        _output_buffer[buffer_pos++] = first_byte;
                    }
                }
                else
                {
                    //If there's data in the buffer, we know that first_byte is already set. (The first byte
                    //is somewhere in the top buffer as that data is to be written out first.)
                    if (buffer_top_pos == MAX_DICT_SIZE)
                        first_byte = _output_buffer[0];

                    //Code is not in the cache
                    _output_buffer[buffer_pos++] = first_byte;
                }

                //Adds the entry to the code cache
                _code_points[next_new_code++] = new CodePoint() { FirstByte = first_byte, PreviousCode = previous_code };

                //When we get as many entries in the table as there's range in the code words, we're to
                //increase the size of the code words. But many encodes got this detail wrong, an off by
                //one error. This is what the "early change" is about.
                //
                //Because of the "off by one" error, the _code_table has room for one more
                //entry. See LZW test 1.pdf for a file that makes use of this quirk. Thus
                //we cap code lengths to 12. If a file sends two more 12 bit codes, we'll 
                //get an index out of bounds on _code_table[_add_new++]
                if (next_new_code + ec == (1 << code_length) && code_length < 12)
                     code_length++;

                //Resizes the buffer to be big enough for the write. Notice the +1, that's
                //so there's always room for the first write.
                int to_write = output_pos + buffer_pos + MAX_DICT_SIZE - buffer_top_pos + 1;
                if (to_write >= output.Length)
                    Array.Resize(ref output, output.Length * 2 + to_write);

                //To simplify things, we write data taken from the cache at the top of the output buffer.
                //This becase the data is effectivly written backwards and we don't know the length. 
                for (int c = buffer_top_pos; c < _output_buffer.Length; c++)
                    output[output_pos++] = _output_buffer[c];

                //Writes out the data at the bottom of the buffer. Buffer.BlockCopy slows this code down,
                //so don't bother with it.
                for (int c = 0; c < buffer_pos; c++)
                    output[output_pos++] = _output_buffer[c];
            }

            return output_pos;
        }

        /// <summary>
        /// Quick and dirty LZW compressor
        /// </summary>
        /// <param name="input">Data to encode</param>
        /// <param name="early_change">Early change bug. Required for Tiff files, optional for PDF.</param>
        /// <returns>Encoded byte array.</returns>
        public static byte[] Encode(byte[] input, bool early_change)
        {
            var bw = new BitWriter((input.Length + 16) / 2);

            //Tiff, Gif and PDF starts codes at 9 bits, ends at 12 bits.
            int n_bits = 9;

            //We use this to track the number of entries, but it can be calculated with 1 << n_bits.
            //What is faster, I don't know.
            int max_entries = 512;

            //We implement early changes by removing one from the max entries. That way the change will
            //happen one step before it should.
            if (early_change)
                max_entries--;

            //Using a generic string dictionary. Hardly ideal.
            Dictionary<string, int> dictionary = new Dictionary<string, int>(4096 - 256);
            var w = new StringBuilder(256);

            //Instead of looking up the previous code in the dictionary, we keep it.
            int prev_code = 0, code;

            //The first code written is to be 256. Most decoders don't care, but some do.
            bw.Write(CLEAR_TABLE, n_bits);

            //We take one byte at a time. The Tiff specs note that it's possible to work with
            //different bit sizes, but I suspect no software supports it. My decoder certainly
            //do not.
            foreach (byte b in input)
            {
                //LZW works in a very simple manner. The decoder and encoder builds a dictionary
                //from the bytes they read. What this encoder will do is add characters to a string
                //until it finds a string that isn't in the dictionary. It will then write out
                //a code for the string that is in the dictionary, and add the unknown string 
                //of characters to the dictionary. Then repeat.
                char ch = (char)b;
                w.Append(ch);

                //Special case length = 1. These codes are always in the dictionary, and always
                //map to the same string code. I.e value 5 will always map to code 5.
                //
                //By not keeping them in the dict, we can empty the dict with the dict.clear() method.
                if (w.Length == 1)
                {
                    //Since mapping is 1 to 1, we simply use the byte value as the code value.
                    prev_code = b;
                }
                //When a code is in the dictionary, we don't write anything. 
                else if (dictionary.TryGetValue(w.ToString(), out code))
                {
                    //This is the code that the decoder needs to find
                    //the string in its dictionary.
                    prev_code = code;
                }
                else
                {
                    //Writes out the previous dictionary match.
                    // Say that the current string is AWESOME and
                    // the previous was "AWESOM" (which is in the
                    // dictionary, otherwise we wouldn't be here).
                    //
                    // Then we now write out the code for the
                    // "AWESOM"string.
                    //
                    // When the decoder reads this code, it
                    // will find the string "AWESOM" in its dict.
                    //
                    // Finally, we ensure that the next string
                    // will always start with E, which will become
                    // "AWESOME" when the decoder writes the next
                    // string.
                    bw.Write(prev_code, n_bits);

                    //We add the unknown string to the dictionary.
                    // By the example above, we'd now have both
                    // "AWESOME" and "AWESOM" in the dict.
                    int new_code = ADD_NEW + dictionary.Count;
                    dictionary.Add(w.ToString(), new_code);

                    //The character that wasn't written still got to be
                    //written, so we will write it out as the next dict
                    //code.
                    // I.e the next code written will map to a string
                    // in the dictionary that starts with "E", in case of
                    // the "AWESOME" example.
                    w.Clear();
                    w.Append(ch);
                    prev_code = b;

                    //We check if we've used up all the space in the dictionary.
                    if (new_code >= max_entries)
                    {
                        //If so, we increase the size.
                        n_bits++;
                        max_entries = 1 << n_bits;
                        if (early_change)
                            max_entries--;

                        //Once a dictionary gets 4096 entries, it's full. That means
                        //we tell the decoder to clear the dictionary, and then we
                        //start building a new dictionary from scratch
                        if (n_bits > 12)
                        {
                            max_entries = 512;
                            if (early_change)
                                max_entries--;
                            n_bits = 9;

                            //This command tells the decoder to clear it's dictionary and
                            //reset number of bits back to nine.
                            bw.Write(CLEAR_TABLE, 12);

                            //We also have to clear our own dictionary.
                            dictionary.Clear();
                        }
                    }
                }
            }

            //Will only be zero when encoding a byte array of zero length.
            if (w.Length > 0)
            {
                bw.Write(prev_code, n_bits);
            }

            //This end of data marker is required by the specs, but I've not found a decoder
            //that cares about it. It probably matters for inline PDF images, though.
            bw.Write(EOD, n_bits);

            return bw.ToArray();
        }
    }
}