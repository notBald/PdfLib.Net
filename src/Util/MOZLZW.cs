/* Copyright 2012 Mozilla Foundation
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 * https://github.com/mozilla/pdf.js/blob/4b4ac8a13d9f0f4ed5a20535c5c5c67f37d0a01f/src/core/stream.js#L1193
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Util
{
    internal class MOZLZW
    {
        const int maxLzwDictionarySize = 4096;

        private readonly byte[] dictionaryValues = new byte[maxLzwDictionarySize];
        private readonly ushort[] dictionaryLengths = new ushort[maxLzwDictionarySize];
        private readonly ushort[] dictionaryPrevCodes = new ushort[maxLzwDictionarySize];
        private readonly byte[] currentSequence = new byte[maxLzwDictionarySize];

        readonly int earlyChange;
        int codeLength = 9;
        int nextCode = 258;
        int prevCode = 0;
        int currentSequenceLength = 0;
        const int blockSize = 512;
        int estimatedDecodedSize = blockSize * 2;
        const int decodedSizeDelta = blockSize;
        byte[] buffer = new byte[0];
        int buffer_pos = 0;

        BitStream _bs;

        public MOZLZW(byte[] source, bool early_change)
        {
            _bs = new BitStream(source);

            for (ushort i = 0; i < 256; ++i)
            {
                dictionaryValues[i] = (byte) i;
                dictionaryLengths[i] = 1;
            }

            earlyChange = early_change ? 1 : 0;
        }

        public byte[] Decode()
        {
            while (ReadBlock())
                ;

            Array.Resize(ref buffer, buffer_pos);
            return buffer;
        }

        private bool ReadBlock()
        {
            int i, j, q;
            int decodedLength = 0;

            Array.Resize(ref buffer, buffer.Length + estimatedDecodedSize);

            for (i = 0; i < blockSize; i++)
            {
                if (!_bs.HasBits(codeLength))
                    return false;
                int code = _bs.GetBits(codeLength);
                bool hasPrev = currentSequenceLength > 0;
                if (code < 256)
                {
                    currentSequence[0] = (byte)code;
                    currentSequenceLength = 1;
                }
                else if (code >= 258)
                {
                    if (code < nextCode)
                    {
                        currentSequenceLength = dictionaryLengths[code];
                        for (j = currentSequenceLength - 1, q = code; j >= 0; j--)
                        {
                            currentSequence[j] = (byte)dictionaryValues[q];
                            q = dictionaryPrevCodes[q];
                        }
                    }
                    else
                    {
                        currentSequence[currentSequenceLength++] = currentSequence[0];
                    }
                }
                else if (code == 256)
                {
                    codeLength = 9;
                    nextCode = 258;
                    currentSequenceLength = 0;
                    continue;
                }
                else
                {
                    return false;
                }

                if (hasPrev)
                {
                    dictionaryPrevCodes[nextCode] = (ushort)prevCode;
                    dictionaryLengths[nextCode] = (ushort)(dictionaryLengths[prevCode] + 1);
                    dictionaryValues[nextCode] = currentSequence[0];
                    nextCode++;
                    codeLength =
                      (((nextCode + earlyChange) & (nextCode + earlyChange - 1)) != 0)
                        ? codeLength
                        : (int)Math.Min(Math.Log(nextCode + earlyChange) / 0.6931471805599453 + 1, 12);

                    if (nextCode == 481)
                    {

                    }
                }
                prevCode = code;

                decodedLength += currentSequenceLength;
                if (estimatedDecodedSize < decodedLength)
                {
                    do
                    {
                        estimatedDecodedSize += decodedSizeDelta;
                    } while (estimatedDecodedSize < decodedLength);

                    Array.Resize<byte>(ref buffer, buffer.Length + estimatedDecodedSize);
                }
                for (j = 0; j < currentSequenceLength; j++)
                {
                    buffer[buffer_pos++] = currentSequence[j];
                }
            }

            return true;
        }
    }
}
