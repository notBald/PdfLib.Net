//Code from: https://web.archive.org/web/20170610033420/http://dev.khsu.ru/el/crc32/
// MIT lisenced
// Used for validating PNG file chunks, about 3x faster than alt impl.
using System;

namespace PdfLib.Encryption
{
    public class CRC32
    {
        private const uint kCrcPoly = 0xEDB88320;
        private const uint kInitial = 0xFFFFFFFF;
        private readonly uint[] Table;
        private const uint CRC_NUM_TABLES = 8;

        private uint value;

        public CRC32()
        {
            unchecked
            {
                Table = new uint[256 * CRC_NUM_TABLES];
                uint i;
                for (i = 0; i < 256; i++)
                {
                    uint r = i;
                    for (int j = 0; j < 8; j++)
                        r = (r >> 1) ^ (kCrcPoly & ~((r & 1) - 1));
                    Table[i] = r;
                }
                for (; i < 256 * CRC_NUM_TABLES; i++)
                {
                    uint r = Table[i - 256];
                    Table[i] = Table[r & 0xFF] ^ (r >> 8);
                }
            }

            Reset();
        }

        /// <summary>
        /// Reset CRC
        /// </summary>
        public void Reset()
        {
            value = kInitial;
        }

        public int Value
        {
            get { return (int)~value; }
        }

        public uint CRC { get { return ~value; } }

        public void Update(byte b)
        {
            value = (value >> 8) ^ Table[(byte)value ^ b];
        }

        public void Update(byte[] data, int offset, int count)
        {
            new ArraySegment<byte>(data, offset, count);     // check arguments
            if (count == 0) return;

            var table = Table;

            uint crc = value;

            for (; (offset & 7) != 0 && count != 0; count--)
                crc = (crc >> 8) ^ table[(byte)crc ^ data[offset++]];

            if (count >= 8)
            {
                /*
                 * Idea from 7-zip project sources (http://7-zip.org/sdk.html)
                 */

                int to = (count - 8) & ~7;
                count -= to;
                to += offset;

                while (offset != to)
                {
                    crc ^= (uint)(data[offset] + (data[offset + 1] << 8) + (data[offset + 2] << 16) + (data[offset + 3] << 24));
                    uint high = (uint)(data[offset + 4] + (data[offset + 5] << 8) + (data[offset + 6] << 16) + (data[offset + 7] << 24));
                    offset += 8;

                    crc = table[(byte)crc + 0x700]
                        ^ table[(byte)(crc >>= 8) + 0x600]
                        ^ table[(byte)(crc >>= 8) + 0x500]
                        ^ table[/*(byte)*/(crc >> 8) + 0x400]
                        ^ table[(byte)(high) + 0x300]
                        ^ table[(byte)(high >>= 8) + 0x200]
                        ^ table[(byte)(high >>= 8) + 0x100]
                        ^ table[/*(byte)*/(high >> 8) + 0x000];
                }
            }

            while (count-- != 0)
                crc = (crc >> 8) ^ table[(byte)crc ^ data[offset++]];

            value = crc;
        }

        public static int Compute(byte[] data, int offset, int size)
        {
            var crc = new CRC32();
            crc.Update(data, offset, size);
            return crc.Value;
        }

        public static int Compute(byte[] data)
        {
            return Compute(data, 0, data.Length);
        }

        public static int Compute(ArraySegment<byte> block)
        {
            return Compute(block.Array, block.Offset, block.Count);
        }
    }
}
/*
 
 Copyright (c) 2012-2015 Eugene Larchenko (spct@mail.ru)

 Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

 The above copyright notice and this permission notice shall be included in
 all copies or substantial portions of the Software.

 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 THE SOFTWARE.
 
*/
