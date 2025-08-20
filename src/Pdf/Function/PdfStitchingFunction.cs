using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Function
{
    /// <summary>
    /// PdfFunction Type 3 (7.10.4)
    /// </summary>
    /// <remarks>
    /// Stitches together multiple "single input" functions.
    /// I.e. From 0-7 use Funtion 1, from 8-15 use function 2, etc.
    /// </remarks>
    internal sealed class PdfStitchingFunction : PdfFunction
    {
        #region Variables and properties

        public PdfFunctionArray Functions 
        { get { return (PdfFunctionArray) _elems.GetPdfTypeEx("Functions", PdfType.FunctionArray, IntMsg.DoNotChange, null); } }

        public override xRange[] Range
        {
            get 
            { 
                var ar = (RealArray) _elems.GetPdfType("Range", PdfType.RealArray);
                if (ar == null) return null;
                var ret = new xRange[ar.Length / 2];
                var m = Domain.Length;
                for (int c = 0, pos = 0; c < m; c++)
                    ret[c] = new xRange(ar[pos++], ar[pos++]);
                return ret;
            }
        }

        /// <summary>
        /// Interval where functions shall apply
        /// </summary>
        /// <remarks>
        /// Domain0 Bounds0 Bounds1 ... BoundsN Domain1
        /// </remarks>
        public double[] Bounds 
        { 
            get 
            { 
                var ar = ((RealArray) _elems.GetPdfTypeEx("Bounds", PdfType.RealArray)).ToArray();
                int k = Functions.Length;
                if (ar.Length != k - 1) throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return ar;
            } 
        }

        /// <summary>
        /// Maps subset and domain to functions
        /// </summary>
        public double[] Encode
        { 
            get 
            {
                var ar = ((RealArray)_elems.GetPdfTypeEx("Encode", PdfType.RealArray)).ToArray();
                int k = Functions.Length;
                if (ar.Length != k * 2) throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return ar;
            } 
        }

        /// <summary>
        /// Number of output values
        /// </summary>
        public override int OutputValues
        {
            get 
            {
                //Presumably all functions have the
                //same number of output values.
                return Functions[0].OutputValues;
            }
        }

        #endregion

        #region Init

        public PdfStitchingFunction(PdfFunctionArray funcs, double[] bounds, xRange[] encode)
            : base(3, 1, null, null)
        {
            if (funcs == null || bounds == null || encode == null)
                throw new ArgumentNullException();
            if (funcs.Length != bounds.Length + 1)
                throw new PdfNotSupportedException("bounds must be one less than functions");
            if (funcs.Length != encode.Length)
                throw new PdfNotSupportedException("Encode must be one same length as functions");
            _elems.SetInt("FunctionType", 3);
            
            //Methods for setting these aren't public, so this will never happen
            Debug.Assert(!funcs.IsIdent && !funcs.StripArray);
            
            _elems.SetItem("Functions", funcs, true);
            _elems.SetItem("Bounds", new RealArray(bounds), false);
            _elems.SetItem("Encode", new RealArray(xRange.ToArray(encode)), false);
        }

        internal PdfStitchingFunction(PdfDictionary dict)
            : base(dict)
        { }

        public override FCalcualte Init()
        {
            return new Calculator(Domain, Range, Functions, Bounds, Encode);
        }

        #endregion

        #region Required override

        protected override bool Equivalent(PdfFunction obj)
        {
            var func = (PdfStitchingFunction)obj;
            return Functions.Equivalent(func.Functions);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfStitchingFunction(elems);
        }

        #endregion

        private class Calculator : FCalcualte
        {
            private FCalcualte[] _functions;
            private double[] _bounds;
            private xRange _domain;
            private fEncode[] _fencode;

            public Calculator(xRange[] domain, xRange[] range, PdfFunctionArray func, double[] bounds, double[] encode)
                : base(domain, range)
            {
                if (_m != 1)
                    throw new PdfInternalException("Type 3 functions can only have one input value");


                _functions = new FCalcualte[func.Length];
                for (int c = 0; c < _functions.Length; c++)
                {
                    var f = func[c];
                    _functions[c] = f.Init();
                }
                _bounds = bounds;
                _domain = _domains[0];

                //Padd the bounds array with the "Domain0/1" values, as that is the true start/end.
                var temp = new double[_bounds.Length + 2];
                Array.Copy(_bounds, 0, temp, 1, _bounds.Length);
                temp[0] = _domain.Min;
                temp[temp.Length - 1] = _domain.Max;
                _bounds = temp;

                //Calculates as much of the interpolation formula as I can get away with
                // Encode[2n+1] - Encode[2n] / _bounds[n-1] - _bounds[n]
                _fencode = new fEncode[_bounds.Length - 1];
                for (int c = 0; c < _fencode.Length; c++)
                {
                    var c2 = 2 * c;
                    var enc_min = encode[c2];
                    var factor = (encode[c2 + 1] - enc_min) / (_bounds[c + 1] - _bounds[c]);
                    if (double.IsInfinity(factor))
                        factor = double.MaxValue;
                    _fencode[c] = new fEncode(enc_min, factor);
                }
            }

            public override void Calculate(double[] input, double[] output)
            {
                Calculate(input[0], output);
            }

            public override void Calculate(double input, double[] output)
            {
                //Clips to the domain
                if (input < _domain.Min)
                    input = _domain.Min;
                else if (input > _domain.Max)
                    input = _domain.Max;

                //Finds the relevant bounds index (and function index)
                //[ Domain0 Bounds0, Bounds1, ..., Domain1]
                // Note that the "domains" need to be compensated for.
                int c = 0; int end = _bounds.Length - 2;
                while (c < end && input >= _bounds[c + 1])
                {
                    c++;
                }
                //C should be pointing at the bounds _after_ the bounds we want
                //Must therefore substract 2 (1 for Domain0, 1 to get the wanted index)
                //c -= 2;

                //x' = Interpolate (x, Bounds[c - 1], Bounds[c], Encode[2xc], Encode[2xc+1])
                input = _fencode[c].Min + (input - _bounds[c]) * _fencode[c].Factor;

                _functions[c].Calculate(input, output);
            }
        }

        struct fEncode
        {
            /// <summary>
            /// Minimum value of the "encoding" bounds
            /// </summary>
            public readonly double Min;

            /// <summary>
            /// Precomputed scale factor
            /// </summary>
            public readonly double Factor;

            public fEncode(double min, double factor) { Min = min; Factor = factor; }
        }
    }
}
