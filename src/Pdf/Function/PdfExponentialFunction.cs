using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Function
{
    /// <summary>
    /// Exponential Interpolation function
    /// </summary>
    public sealed class PdfExponentialFunction : PdfFunction
    {
        #region Variables and properties

        public double[] C0
        {
            get
            {
                var ar = (RealArray)_elems.GetPdfType("C0", PdfType.RealArray);
                if (ar != null) return ar.ToArray();
                return new double[] { 0 };
            }
        }

        public double[] C1
        {
            get
            {
                var ar = (RealArray)_elems.GetPdfType("C1", PdfType.RealArray);
                if (ar != null) return ar.ToArray();
                return new double[] { 1 };
            }
        }

        public override xRange[] Range
        {
            get
            {
                var ar = (RealArray)_elems.GetPdfType("Range", PdfType.RealArray);
                if (ar == null) return null;
                return xRange.Create(ar);
            }
        }

        public double N { get { return _elems.GetRealEx("N"); } }

        public override int OutputValues { get { return C0.Length; } }

        #endregion

        #region Init

        /// <summary>
        /// Creates a new exponetial function
        /// </summary>
        /// <param name="n">The interpolation exponent</param>
        /// <param name="C0">Optional lower bounds</param>
        /// <param name="C1">Optional higher bounds</param>
        /// <param name="domain">The input domain. Will be set to [0, 1] if null</param>
        /// <param name="range">Optional output range</param>
        public PdfExponentialFunction(double n, double[] C0, double[] C1, xRange? domain, xRange[] range)
            : base(2, 1, (domain != null) ? new xRange[] { domain.Value } : null, range)
        {
            _elems.SetReal("N", n);
            if (C0 != null)
                _elems.SetNewItem("C0", new RealArray(C0), false);
            if (C1 != null)
                _elems.SetNewItem("C1", new RealArray(C1), false);
        }

        internal PdfExponentialFunction(PdfDictionary dict)
            : base(dict)
        { }

        public override FCalcualte Init()
        {
            return new Calculator(Domain, Range, C0, C1, N);
        }

        #endregion

        

        #region Required override

        protected override bool Equivalent(PdfFunction obj)
        {
            var func = (PdfExponentialFunction) obj;
            return
                Util.ArrayHelper.ArraysEqual<double>(C0, func.C0) &&
                Util.ArrayHelper.ArraysEqual<double>(C1, func.C1) &&
                N == func.N;
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfExponentialFunction(elems);
        }

        #endregion

        private class Calculator : FCalcualte
        {
            private xRange _domain;
            private double[] _c0, _c1;

            /// <summary>
            /// The interpolation exponent
            /// </summary>
            private double _exp;

            public Calculator(xRange[] domain, xRange[] range, double[] c0, double[] c1, double n)
                : base(domain, range)
            {
                if (_m != 1)
                    throw new PdfInternalException("Type 2 functions can only have one input value");
                _domain = _domains[0];
                _c0 = c0;
                _c1 = c1;
                _n = c0.Length;
                if (_n != _c1.Length)
                    throw new PdfInternalException("Currupt array");
                _exp = n;
                for (int c = 0; c < _c0.Length; c++)
                    _c1[c] -= _c0[c];
            }

            public override void Calculate(double[] input, double[] output)
            {
                Calculate(input[0], output);
            }

            /// <summary>
            /// 7.10.3 Calculates using formula
            /// 
            /// Yj = C0j + x^N * (C1j - C0j)
            /// (Where j is the component)
            /// </summary>
            public override void Calculate(double input, double[] output)
            {
                //Clips to the domain
                if (input < _domain.Min)
                    input = _domain.Min;
                else if (input > _domain.Max)
                    input = _domain.Max;

                for (int j = 0; j < _n; j++)
                {
                    //_c1 has _c0 substracted already
                    var comp = _c0[j] + Math.Pow(input, _exp) * _c1[j];
                    if (_range != null)
                    {
                        //Clips to the range.
                        if (comp < _range[j].Min)
                            comp = _range[j].Min;
                        else if (comp > _range[j].Max)
                            comp = _range[j].Max;
                    }
                    output[j] = comp;
                }
            }
        }
    }
}
