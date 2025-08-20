using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Filter;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Function
{
    /// <summary>
    /// A function that interpolates using a sample table.
    /// </summary>
    /// <remarks>
    /// Useful resources:
    /// 
    /// http://en.wikipedia.org/wiki/Trilinear_interpolation
    /// 
    /// Pretty much describes the method of interpolation used
    /// here.
    /// 
    /// http://paulbourke.net/miscellaneous/interpolation/index.html
    /// 
    /// Describes a faster way for trilinar interpolation, which can
    /// be extended to higher dimensionality.
    /// 
    /// http://www.paulinternet.nl/?page=bicubic
    /// 
    /// Describes multidimensional bicubic interpolation (which needs
    /// to be implemented)
    /// 
    /// http://osl.iu.edu/~tveldhui/papers/MAScThesis/node33.html
    /// 
    /// Describes a faster (if not as accurate) method of interpolation
    /// that can be used on multidimensional samples, without manually
    /// extending the code for each additional dimension.
    /// </remarks>
    public sealed class PdfSampleFunction : PdfFunction, ICStream
    {
        #region Variables and properties

        IWStream _stream;

        /// <summary>
        /// Stream based objects must be indirect
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        /// <summary>
        /// An array of m positive integers, specifies the number of samples in
        /// each input dimension.
        /// </summary>
        /// <remarks>
        /// m = Number of input values
        /// Should be UIntArray
        /// </remarks>
        public int[] Size { get { return ((IntArray)_elems.GetPdfTypeEx("Size", PdfType.IntArray)).ToArray(); } }

        /// <summary>
        /// The number of bits that shall represent each sample. 
        /// </summary>
        /// <remarks>Valid values shall be 1, 2, 4, 8, 12, 16, 24, and 32</remarks>
        public int BitsPerSample { get { return _elems.GetUIntEx("BitsPerSample"); } }

        public InterpolationType Order { get { return (InterpolationType)_elems.GetUInt("Order", 1); } }

        /// <summary>
        /// An array of 2 × m numbers specifying the linear mapping of 
        /// input values into the domain of the function’s sample table.
        /// </summary>
        public xRange[] Encode
        {
            get
            {
                var ar = (RealArray)_elems.GetPdfType("Encode", PdfType.RealArray);
                if (ar == null)
                {
                    var size = Size;
                    var da = new xRange[size.Length];
                    for (int c = 0; c < da.Length; c++)
                        da[c] = new xRange(0, size[c] - 1);
                    return da;
                }
                return xRange.Create(ar);
            }
        }

        /// <summary>
        /// Maximum extent of input values. Values falling outside these
        /// ranges will be clipped.
        /// </summary>
        public xRange[] Decode
        {
            get
            {
                var ar = (RealArray)_elems.GetPdfType("Decode", PdfType.RealArray);
                if (ar == null) return Range;
                return xRange.Create(ar);
            }
            set
            {
                _elems.SetItem("Decode", new RealArray(xRange.ToArray(value)), false);
            }
        }

        /// <summary>
        /// Maximum extent of output values. Values falling outside these
        /// ranges will be clipped.
        /// </summary>
        public override xRange[] Range
        {
            get
            {
                return xRange.Create((RealArray)_elems.GetPdfTypeEx("Range", PdfType.RealArray));
            }
        }

        /// <summary>
        /// The number of output values
        /// </summary>
        public override int OutputValues
        {
            get { return Decode.Length; }
        }

        #endregion

        #region Init

        /// <summary>
        /// Creates a new sample function
        /// </summary>
        /// <param name="size">Number of samples in each input dimension</param>
        /// <param name="domain">Clips input values, if null this will be the same as range</param>
        /// <param name="encode">The sample coordinate space, if null will be the same as size</param>
        /// <param name="range">Clips output values</param>
        /// <param name="bits_per_sample">Number of bits per sample</param>
        /// <param name="samples">The samples</param>
        public PdfSampleFunction(int[] size, xRange[] domain, xRange[] encode, xRange[] range, int bits_per_sample, byte[] samples)
            : base(new TemporaryDictionary())
        {
            _elems.SetInt("FunctionType", 0);
            if (domain != null)
                _elems.DirectSet("Domain", new RealArray(xRange.ToArray(domain)));
            if (encode != null)
                _elems.DirectSet("Encode", new RealArray(xRange.ToArray(encode)));
            _elems.DirectSet("Range", new RealArray(xRange.ToArray(range)));
            _elems.SetInt("BitsPerSample", bits_per_sample);
            _elems.DirectSet("Size", new IntArray(size));
            _stream = new WritableStream(samples, _elems);

            int nbytes = OutputValues;
            for (int c = 0; c < size.Length; c++)
                nbytes *= size[c];
            if ((nbytes * bits_per_sample + 7) / 8 != samples.Length)
                throw new PdfNotSupportedException("Samples must be of the correct size");
            if (bits_per_sample != 1 && bits_per_sample != 2 &&
                bits_per_sample != 4 && bits_per_sample != 8 &&
                bits_per_sample != 12 && bits_per_sample != 16 &&
                bits_per_sample != 24 && bits_per_sample != 32)
                throw new PdfNotSupportedException("Bits per sample must be 1,2,4,8,12,16,24 or 32");
        }

        internal PdfSampleFunction(IWStream stream)
            : base(stream.Elements)
        { _stream = stream; }

        internal PdfSampleFunction(IWStream stream, PdfDictionary dict)
            : base(dict)
        { _stream = stream; }

        public override FCalcualte Init()
        {
            //In a pinch linear interpolation can be used instead though.
            // http://www.paulinternet.nl/?page=bicubic
            if (Order == InterpolationType.Cubic) //todo
                throw new NotImplementedException("Cubic interpolation");


            return new Calculator(Domain, Range, Encode, Decode, Size, BitsPerSample, _stream.DecodedStream);
        }

        #endregion

        #region ICStream

        void ICStream.Compress(List<FilterArray> filters, CompressionMode mode)
        {
            _stream.Compress(filters, mode);
        }

        PdfDictionary ICStream.Elements { get { return _elems; } }

        void ICStream.LoadResources()
        {
            _stream.LoadResources();
        }

        #endregion

        internal override void Write(PdfWriter write)
        {
            throw new PdfInternalException("Sample functions can't be saved as a direct object");
        }

        internal override void Write(PdfWriter write, SM store_mode)
        {
            if (store_mode == SM.Direct)
                throw new PdfInternalException("Sample functions can't be saved as a direct object");
            _stream.Write(write);
        }

        protected override bool Equivalent(PdfFunction obj)
        {
            var func = (PdfSampleFunction)obj;
            return
                Util.ArrayHelper.ArraysEqual<int>(Size, func.Size) &&
                BitsPerSample == func.BitsPerSample &&
                Order == func.Order &&
                xRange.Compare(Encode, func.Encode) &&
                xRange.Compare(Decode, func.Decode);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfSampleFunction(_stream.MakeCopy((WritableDictionary)elems), elems);
        }

        protected override void LoadResources()
        {
            _stream.LoadResources();
        }

        /// <summary>
        /// To make this object save correctly one have to
        /// make sure the stream has the same dictionary as
        /// the class.
        /// </summary>
        protected override void DictChanged()
        {
            _stream.SetDictionary(_elems);
        }

        /// <summary>
        /// For convinience, gathering all values for a input value
        /// in one struct.
        /// </summary>
        //[DebuggerDisplay("{Value}: {Floor} - {Next}")]
        [DebuggerDisplay("{Floor} - {Next}")]
        private struct InputtValue
        {
            /// <summary>
            /// Floor is the value without it's fraction
            /// </summary>
            public int Floor;

            /// <summary>
            /// Next is the value after the fraction
            /// </summary>
            public int Next;

            /// <summary>
            /// The fraction is the part "between" the samples
            /// </summary>
            public readonly double Fraction;

            //public readonly double Value;

            public InputtValue(double value, int max)
            {
                Floor = (int)value;
                Next = Floor + 1;
                Fraction = value - Floor;
                //Value = value;

                if (Next > max)
                {
                    Next = max;
                    if (Floor > max) Floor = max;
                }
            }
        }

        private class Calculator : FCalcualte
        {
            //Cached values to speed up computation
            private xRange[] _encode, _decode;
            private int[] _size;
            private uint[][] _samples;
            private uint _max;

            /// <summary>
            /// Contains the various sizes multiplied with
            /// themselves.
            /// 
            /// I.e. Size[2, 3, 4] => [1, 2x1, 3x2x1]
            /// 
            /// This is done to simplify finding the address
            /// samples.
            /// </summary>
            private int[] _size_multiplied;

            //Some preallocated memory, will cause MT issues
            InputtValue[] _input;
            int[] _addresses;
            double[] _sample;

            public Calculator(xRange[] domain, xRange[] range, xRange[] encode, xRange[] decode, int[] size, int bits_per_sample, byte[] bytes)
                : base(domain, range)
            {
                _encode = encode;
                _decode = decode;
                _size = size;

                //Generates sample table
                _max = ((uint)Math.Pow(2, bits_per_sample)) - 1;

                //Size of the number of samples in each output dimension
                var dim_size = _size[0];

                //Precomputed multiplier
                _size_multiplied = new int[_size.Length];
                _size_multiplied[0] = 1;

                for (int c = 1; c < _size.Length; c++)
                {
                    dim_size *= _size[c];
                    _size_multiplied[c] = _size_multiplied[c - 1] * _size[c - 1];
                }

                //Puts the samples for each output in it's own table.
                _samples = new uint[_n][];
                for (int c = 0; c < _n; c++)
                    _samples[c] = new uint[dim_size];

                //Total number of samples
                int total_size = dim_size * _n;

                #region Preallocating memory

                //Sample functions are called many times over,
                //so preallocating objects needed in the calc
                //function.
                _input = new InputtValue[_m];

                //Inputt can be 1d, 2d, 3d, etc. All points that sorrunds
                //the point must be interpolated together.
                //I.e. a 3d point will have 8 points sorrunding it
                //     (four above and four bellow, imagine the point
                //       sitting in a cube with each corner a point)
                //
                //Num points to interpolate is thus 2 ^ m
                int num_points = (1 << _m);

                //Used to hold sample addresses
                _addresses = new int[num_points];
                //Used to hold samples needed for interpolation
                _sample = new double[num_points];

                #endregion

                //The table format
                // A table's size is determined by the number of inputs, the Size and
                // the number of outputs
                //
                // The outputs are interleaved, i.e:
                //   0: Output 0, Sample 0
                //   1: Output 1, Sample 0
                //   2: Output 0, Sample 1
                //
                // This loop untangles the interleaving as it reads out the sample data. 
                if (bits_per_sample == 8 || bits_per_sample == 16 || bits_per_sample >= 24)
                {
                    int nBytes = bits_per_sample / 8;
                    var rPos = 0;

                    //Reads out the values for each output
                    for (int samp_n = 0; samp_n < dim_size; samp_n++)
                    {
                        for (int out_n = 0; out_n < _n; out_n++)
                        {
                            //Reads out a single sample
                            uint num = 0;
                            for (int b = 0; b < nBytes; b++)
                                num = num << 8 | bytes[rPos++];
                            _samples[out_n][samp_n] = num;
                        }
                    }
                }
                else
                {
                    //Slower, but works with any bbs size
                    var bs = new Util.BitStream(bytes);

                    //Reads out the values for each output
                    for (int samp_n = 0; samp_n < dim_size; samp_n++)
                    {
                        for (int out_n = 0; out_n < _n; out_n++)
                        { _samples[out_n][samp_n] = (uint)bs.FetchBits(bits_per_sample); }
                    }
                }
            }

            public override void Calculate(double input, double[] output)
            {
                Calculate(new double[] { input }, output);
            }

            public override void Calculate(double[] input_d, double[] output)
            {
                //Calculates the floor/next and fraction values for each input value.
                //
                // For instance, if the input is 12.7, this will be turned into
                // 12, 13 and .7 (assuming domain and encode is 1 to 1)
                for (int c = 0; c < _input.Length; c++)
                {
                    //Clips to the Domain. It's possible this step can be skipped since
                    //clipping is done after the interpolation, but I haven't looked at 
                    //the math.
                    var d = _domains[c];
                    var val = d.Clip(input_d[c]);

                    //Interpolate into Encode. Note that I assume that the encode can't
                    //map input values into reading samples from other output tables 
                    //(since I've split the interleaved output tables up into self 
                    //contained tables)
                    var enc = _encode[c];
                    val = Img.Internal.LinearInterpolator.Interpolate(val, d.Min, d.Max, enc.Min, enc.Max);
                    // ^ Todo: The interpolation factor should be precalced

                    //Clips to zero (clip to max is handeled in the next step)
                    val = (val < 0) ? 0 : val;

                    //Rest is handeled in the InputtValue constructor
                    _input[c] = new InputtValue(val, _size[c] - 1);
                }

                if (_m == 1)
                {
                    //Special casing 1d inputt simply because I wrote this first

                    //For each output value
                    for (int c = 0; c < _n; c++)
                    {
                        var ip = _input[0];

                        //Assuming a 1d data array.
                        double low_sample = _samples[c][ip.Floor];
                        double hig_sample = _samples[c][ip.Next];

                        //Interpolates.
                        //The fraction is the position of the input value between the
                        //low and high point, so multiply it by the distance to get the
                        //linear value. 
                        var val = low_sample + ip.Fraction * (hig_sample - low_sample);

                        //Scales the value into the output range using the
                        //interpolation formula: (x - 0) * (y_max - y_min) / (x_max - 0) + y_min;
                        var d = _decode[c];
                        val = val * (d.Max - d.Min) / _max + d.Min;

                        //Clips output to the output range
                        output[c] = _range[c].Clip(val);
                    }
                }
                else
                {
                    //Todo: This code is only tested on 1d, 2d and 4d inputt

                    //Calculates the sample addresses needed for the interpolation
                    //first. This isn't for performance reasons, just for code
                    //clarity.
                    for (int c = 0; c < _addresses.Length; c++)
                    {
                        // Each input variable has a "Floor" and "Next" value.
                        // (Calculated eariler)
                        //
                        //  - Floor is the input value without its fraction,
                        //  - Next is the first value after the input.
                        //
                        // Floor and Next is already clipped so that they won't
                        // overflow the array ( 0 < floor/next < Max - 1 )
                        //
                        // A 2d sampleset will have size[0] * size[1]
                        // samples. 
                        //
                        //  i.e.  Sample(0,0) sample(0,1) sample(0,2)
                        //        Sample(1,0) sample(1,1) sample(1,2)
                        //          Where size[0] = 2 and size[1] = 3
                        //
                        //  In the linear array these samples will lay
                        //  like this:
                        //       Sample(0,0) 
                        //       Sample(1,0)
                        //       Sample(0,1)
                        //       Sample(1,1)
                        //       Sample(0,2)
                        //       Sample(1,2)
                        //
                        // So to find sample (0,1) one have to first add the
                        // size of input[0], whereas (0,2) needs that size
                        // added twice*.
                        //
                        //  * I.e. if input is (0,2), to find the right address
                        //    in memory you do 0 + 2 x size[0], giving address
                        //    four in memory (counted from 0)
                        //
                        // Same scheme is used for additional components
                        // i.e. For a input with three componets:
                        //      input[0].Value +
                        //      input[1].Value * size[0]
                        //      input[2].Value * size[0] * size[1]
                        // (Where size[x] is the number for samples for
                        // that input.)
                        //
                        // But the above addition gives the exact location for
                        // the input. A position that don't actually exists.
                        //
                        // So instad we take the numbers surrunding this "imaginary
                        // sample", so for:
                        //      address 0:  input[0].Floor input[1].Floor input[2].Floor
                        //      address 1:  input[0].Next  input[1].Floor input[2].Floor
                        //      address 2:  input[0].Floor input[1].Next  input[2].Floor
                        //      address 3:  input[0].Next  input[1].Next  input[2].Floor
                        //      address 4:  input[0].Floor input[1].Floor input[2].Next
                        //        ...
                        // Where "address 0" is one pixel near the imaginary pixel. For
                        // a 1D sampleset there will be 2 pixels near the imaginary one,
                        // where a 3D pixel will have 8 (and so on)
                        //
                        // IOW if we have a three component input we calcualte the addresses
                        // of 8 samples in the the sample table by looking at all combinations 
                        // of floor/next of the inputs.
                        //
                        // This is managed by bitshifting the address number
                        //      address 0:  0 -> 000 -> Floor Floor Floor
                        //      address 1:  1 -> 001 -> Next  Floor Floor
                        //      address 3:  3 -> 011 -> Next  Next  Floor
                        //      address 4:  4 -> 100 -> Floor Floor Next
                        //
                        // As the bitpattern meshes with our needs
                        int bit_pattern = c;
                        int address = 0;

                        //Adds together one address with the above formula. Note that
                        //_size has been premultiplied so that instead of calcing
                        //"_size[0] * _size[1] * _size[etc]", one just take the value 
                        //"_size_multiplied[final_address_nr]"
                        for (int i = 0; i < _m;)
                        {
                            address += (((bit_pattern & 1) == 0) ? _input[i].Floor : _input[i].Next) * _size_multiplied[i];
                            i++; bit_pattern >>= 1;
                        }

                        //The number of addresses increases exponentially. For 1D inputt one
                        //only need 2^1 = 2 addresses, for 4D inputt this grows to 2^4 = 16
                        _addresses[c] = address;
                    }

                    //Interpolates. 
                    //
                    // Each output is independed from eachother. Say you're doing CMYK to RGB 
                    // conversion. Then R,G,B will be the outputs and C,M,Y,K the inputs. 
                    // 
                    // One might think that to find the correct RGB value one make a lookup
                    // for the CMYK value, but that isn't the case. Instead one make a lookup
                    // for the R value for that CMYK value, G value for that CMYK value and
                    // finally the B value for that CMYK value.
                    //
                    // I.e. one got one sample table for each output.
                    //
                    // The addresses for each sample table is the same. In the original data
                    // the output sample tables were interleaved, but I've split them up for
                    // convinience. 
                    //
                    //Total number of interpolations are:  "2^m * n"
                    for (int out_n = 0; out_n < _n; out_n++)
                    {
                        //Gets the samples for this output
                        var org_samples = _samples[out_n];

                        //Fetches the samples needed for this interpolation
                        for (int c = 0; c < _sample.Length; c++)
                            _sample[c] = org_samples[_addresses[c]];

                        //Interpolates the samples until there's one
                        //sample left. (There can be only one!) 
                        for (int i_num = 0, num_samples = _sample.Length; i_num < _input.Length; i_num++)
                        {
                            //For a visualization of what's going on, see:
                            // http://en.wikipedia.org/wiki/File:3D_interpolation2.svg
                            //
                            //Basically we interpolate along the first axis, reducing
                            //the dimensionality of the samples by 1. Then we interpolate
                            //along the next axis, reducing the dimensionality further,
                            //until we're left with one point.
                            var fraction = _input[i_num].Fraction;

                            for (int i = 0, j = 0; i < num_samples;)
                            {
                                var samp1 = _sample[i++];
                                var samp2 = _sample[i++];
                                _sample[j++] = samp1 + fraction * (samp2 - samp1);
                            }

                            //Each pass halves the number of samples
                            num_samples /= 2;
                        }

                        //Scales the value into the output domain.
                        var d = _decode[out_n];
                        var result = Img.Internal.LinearInterpolator.Interpolate(_sample[0], 0, _max, d.Min, d.Max);
                        // ^ Scale factors can be precalculated

                        //Clips to the output range
                        output[out_n] = _range[out_n].Clip(result);
                    }
                }
            }
        }
    }

    public enum InterpolationType
    {
        Linear = 1,

        /// <summary>
        /// Not supported
        /// </summary>
        Cubic = 3
    }
}