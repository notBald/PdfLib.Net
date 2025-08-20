using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Function;
using PdfLib.Write.Internal;
using PdfLib.Img.Internal;
using PdfLib.Util;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    /// <summary>
    /// Used for reading out coordnites in stream based shaders
    /// </summary>
    internal abstract class CoordReader
    {
        /// <summary>
        /// Bit reader
        /// </summary>
        protected readonly BitStream64 br;

        /// <summary>
        /// Bits per flag
        /// </summary>
        protected readonly int bpf;

        /// <summary>
        /// Number of color components
        /// </summary>
        protected readonly int ncomps;

        private readonly int bpc;
        private readonly int bpcomp;
        private readonly double x_range, y_range;
        private readonly double x_min, y_min;
        private readonly double[,] decode_lookup;

        protected CoordReader(PdfStreamShading p, byte[] data)
        {
            br = new BitStream64(data);

            bpf = p.BitsPerFlag;
            bpc = p.BitsPerCoordinate;
            bpcomp = p.BitsPerComponent;
            var decode = p.Decode;

            //Decode array works similar to image's decode array, however instead
            //of making a decode lookup table we interpolate into decode directly.
            //
            //This since coords can have up to 32BBP, and a decode lookup table
            //would then get pretty large. 
            long coord_max = (1L << bpc) - 1;

            x_min = decode[0].Min;
            //Interpolation formula gives:
            //(x - 0) * (x_max - x_min) / (coord_max - 0) + x_min
            x_range = (decode[0].Max - x_min) / coord_max;
            y_min = decode[1].Min;
            y_range = (decode[1].Max - y_min) / coord_max;

            //Makes a DecodeLookup table for the colors
            ncomps = decode.Length - 2;
            decode_lookup = PdfImage.CreateDLT(decode, 2, bpcomp, ncomps);
        }

        protected xPoint ReadCoord()
        {
            var x = br.FetchBitsU(bpc) * x_range + x_min;
            return new xPoint(x, br.FetchBitsU(bpc) * y_range + y_min);
        }

        protected void ReadColor(double[] col)
        {
            for (int c = 0; c < col.Length; c++)
                col[c] = decode_lookup[c, br.FetchBitsU(bpcomp)];
        }
    }

    /// <summary>
    /// Used for reading out coordnites in stream based shaders
    /// </summary>
    internal abstract class CoordReaderF
    {
        /// <summary>
        /// Bit reader
        /// </summary>
        protected readonly BitStream64 br;

        /// <summary>
        /// Bits per flag
        /// </summary>
        protected readonly int bpf;

        /// <summary>
        /// Number of color components
        /// </summary>
        protected readonly int ncomps;

        private readonly int bpc;
        private readonly int bpcomp;
        private readonly float x_range, y_range;
        private readonly float x_min, y_min;
        private readonly double[,] decode_lookup;

        protected CoordReaderF(PdfStreamShading p, byte[] data)
        {
            br = new BitStream64(data);

            bpf = p.BitsPerFlag;
            bpc = p.BitsPerCoordinate;
            bpcomp = p.BitsPerComponent;
            var decode = p.Decode;

            //Decode array works similar to image's decode array, however instead
            //of making a decode lookup table we interpolate into decode directly.
            //
            //This since coords can have up to 32BBP, and a decode lookup table
            //would then get pretty large. 
            long coord_max = (1L << bpc) - 1;

            x_min = (float) decode[0].Min;
            //Interpolation formula gives:
            //(x - 0) * (x_max - x_min) / (coord_max - 0) + x_min
            x_range = (float) ((decode[0].Max - x_min) / coord_max);
            y_min = (float) decode[1].Min;
            y_range = (float) ((decode[1].Max - y_min) / coord_max);

            //Makes a DecodeLookup table for the colors
            ncomps = decode.Length - 2;
            decode_lookup = PdfImage.CreateDLT(decode, 2, bpcomp, ncomps);
        }

        protected xPointF ReadCoord()
        {
            var x = br.FetchBitsU(bpc) * x_range + x_min;
            return new xPointF(x, br.FetchBitsU(bpc) * y_range + y_min);
        }

        protected void ReadColor(double[] col)
        {
            for (int c = 0; c < col.Length; c++)
                col[c] = decode_lookup[c, br.FetchBitsU(bpcomp)];
        }
    }

    internal abstract class CoordWriter
    {
        private readonly LinearInterpolator _interpolator_x, _interpolator_y;
        private readonly LinearInterpolator[] _color;
        readonly int _bp_coord, _bp_color;
        protected readonly BitWriter _bw;
        protected readonly byte[] _data;

        public CoordWriter(int bits_per_coord, int bits_per_color, xRange[] decode, xRect bounds,
            int max_bits_to_write)
        {
            _bp_coord = bits_per_coord;
            _bp_color = bits_per_color;

            int max_value = (int)(((long)1 << bits_per_coord) - 1);

            _interpolator_x = new LinearInterpolator(bounds.Left, bounds.Right, 0, max_value);
            _interpolator_y = new LinearInterpolator(bounds.Bottom, bounds.Top, 0, max_value);

            //Maximum bits for one tensor
            _data = new byte[(max_bits_to_write + 2 + 7) / 8];
            _bw = new BitWriter(_data);

            max_value = (int)(((long)1 << bits_per_color) - 1);
            _color = new LinearInterpolator[decode.Length - 2];
            for (int c = 2, k = 0; c < decode.Length; c++)
                _color[k++] = new LinearInterpolator(decode[c].Min, decode[c].Max, 0, max_value);
        }

        protected void WriteCoord(xPoint coord)
        {
            _bw.Write((ulong)_interpolator_x.Interpolate(coord.X), _bp_coord);
            _bw.Write((ulong)_interpolator_y.Interpolate(coord.Y), _bp_coord);
        }

        protected void WriteColor(double[] col)
        {
            for (int c = 0; c < col.Length; c++)
                _bw.Write((int)_color[c].Interpolate(col[c]), _bp_color);
        }
    }
}

