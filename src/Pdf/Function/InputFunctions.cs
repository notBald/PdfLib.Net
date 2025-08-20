using PdfLib.Pdf.Primitives;
using System;

namespace PdfLib.Pdf.Function
{
    public class SingleInputFunctions
    {
        private FCalcualte[] _functions;

        /// <summary>
        /// The number of output components
        /// </summary>
        int _n;

        internal SingleInputFunctions(PdfFunctionArray func)
        {
            _functions = new FCalcualte[func.Length];
            for (int c = 0; c < _functions.Length; c++)
                _functions[c] = func[c].Init();

            _n = (_functions.Length == 1) ? func[0].OutputValues : _functions.Length;
        }

        public double[] GetColor(double t)
        {
            var col = new double[_n];
            if (_functions.Length == 1)
                _functions[0].Calculate(t, col);
            else
            {
                var component = new double[1];
                for (int c = 0; c < _n; c++)
                {
                    _functions[c].Calculate(t, component);
                    col[c] = component[0];
                }
            }
            return col;
        }
    }

    public class TwoInputFunctions
    {
        private FCalcualte[] _functions;

        /// <summary>
        /// The number of output components
        /// </summary>
        int _n;

        internal TwoInputFunctions(PdfFunctionArray func)
        {
            _functions = new FCalcualte[func.Length];
            for (int c = 0; c < _functions.Length; c++)
                _functions[c] = func[c].Init();

            _n = (_functions.Length == 1) ? func[0].OutputValues : _functions.Length;
        }

        public double[] GetColor(double x, double y)
        {
            var col = new Double[_n];
            if (_functions.Length == 1)
                _functions[0].Calculate(new double[] { x, y }, col);
            else
            {
                var component = new double[1];
                for (int c = 0; c < _n; c++)
                {
                    _functions[c].Calculate(new double[] { x, y }, component);
                    col[c] = component[0];
                }
            }
            return col;
        }
    }

    public abstract class FCalcualte
    {
        /// <summary>
        /// Number of input values
        /// </summary>
        protected int _m;

        /// <summary>
        /// Number of output values
        /// </summary>
        protected int _n;
        protected xRange[] _domains, _range;

        internal FCalcualte(xRange[] domain, xRange[] range)
        {
            _domains = domain;
            _m = _domains.Length;
            _range = range;
            if (_range != null) _n = _range.Length;
        }

        /// <summary>
        /// Preformes the function
        /// </summary>
        /// <param name="inputt">Input values</param>
        /// <param name="output">Output values are stored here</param>
        /// <remarks>Color conversion needs to be fast, so I've given a
        /// half hearted effort in reducing the number of double arrays
        /// that needs creating.</remarks>
        public abstract void Calculate(double[] input, double[] output);
        public abstract void Calculate(double input, double[] output);
    }
}
