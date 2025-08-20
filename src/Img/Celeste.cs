using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Util;

namespace PdfLib.Img
{
#if CELESTE
    class Celeste
    {
        Header _head;
        byte[] _data;

        public bool HasAlpha => _head.BitsPerPixel == 32;
        public int BitsPerPixel => _head.BitsPerPixel;
        public int BitsPerComponent => 8;
        public int Width => _head.Width;
        public int Height => _head.Height;

        /// <summary>
        /// Pixels in Blue, Green Red, Alpha order
        /// </summary>
        public byte[] Pixels
        {
            get
            {
                //Raw order is BGR(A)
                byte[] raw = new byte[Width * Height * BitsPerPixel / 8];
                int r_pos = 0;
                byte runLength, r, g, b;
                if (BitsPerPixel == 32)
                {
                    byte a;
                    int c = 0;
                    while (c < _data.Length - 1)
                    {
                        runLength = _data[c++];
                        a = _data[c++];
                        if (a == 0)
                        {
                            b = g = r = 0;
                        }
                        else
                        {
                            if (c >= _data.Length - 2)
                                throw new Exception("Unexpected end of Celeste image data");
                            b = _data[c++];
                            g = _data[c++];
                            r = _data[c++];
                        }

                        for(int i=0; i < runLength; i++)
                        {
                            raw[r_pos++] = b;
                            raw[r_pos++] = g;
                            raw[r_pos++] = r;
                            raw[r_pos++] = a;
                        }
                    }
                }
                else
                {
                    for (int c = 0; c < _data.Length - 3; )
                    {
                        runLength = _data[c++];
                        b = _data[c++];
                        g = _data[c++];
                        r = _data[c++];

                        for (int i = 0; i < runLength; i++)
                        {
                            raw[r_pos++] = b;
                            raw[r_pos++] = g;
                            raw[r_pos++] = r;
                        }
                    }
                }

                if (r_pos != raw.Length)
                    throw new Exception("Unexpected end of Celeste image data");

                return raw;
            }
        }

        private Celeste(Header header, byte[] pixels)
        {
            _head = header;
            _data = pixels;
        }

        public static Celeste Open(Stream stream)
        {
            //Reads out the header
            var sr = new StreamReaderEx(stream, false);
            byte[] buffer = new byte[9];
            sr.ReadEx(buffer, 0, buffer.Length);

            var header = new Header(buffer);

            //There is only one image, so we read all the rest of the data
            buffer = new byte[sr.Length - 9];
            sr.ReadEx(buffer, 0, buffer.Length);

            return new Celeste(header, buffer);
        }

        class Header
        {
            readonly byte[] _data;

            public int Width => StreamReaderEx.ReadIntLE(0, _data);
            public int Height => StreamReaderEx.ReadIntLE(4, _data);

            public int BitsPerPixel => _data[8] == 0 ? 24 : 32;

            public Header(byte[] data)
            {
                _data = data;
            }
        }
    }
#endif
}
