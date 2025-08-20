using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using System.Runtime.InteropServices;

namespace PdfLib.Pdf.Function
{
    /// <summary>
    /// I've not glanced at the speces, but from the name
    /// I assume it's simply a pass through function.
    /// 
    /// Note, not to be used outside the graphics state.
    /// PdfLib won't stop you from using it with patterns,
    /// but it won't render in Adobe.
    /// </summary>
    public sealed class PdfIdentityFunction : PdfFunction
    {
        #region Variables and properties

        public override xRange[] Range
        {
            get { return null; }
        }

        public override int OutputValues
        {
            get { return _n; }
        }

        private readonly int _n;

        #endregion

        #region Init

        public PdfIdentityFunction(int n_inputs, int n_outputs) :
            base(new TemporaryDictionary())
        {
            _elems.SetInt("FunctionType", int.MaxValue);
            var da = new double[n_inputs * 2];
            for (int c = 0; c < da.Length;)
            {
                da[c++] = 0;
                da[c++] = 1;
            }
            _elems.SetNewItem("Domain", new RealArray(da), false);
            _n = n_outputs;
        }

        public override FCalcualte Init()
        {
            return new Calculator(Domain, Range);
        }

        #endregion      

        protected override bool Equivalent(PdfFunction obj)
        {
            return true;
        }

        internal override void Write(PdfWriter write)
        {
            write.WriteName("Identity");
        }

        protected override Elements MakeCopy(Primitives.PdfDictionary elems)
        {
            return this;
        }

        public override string ToString()
        {
            return "Identity function";
        }

        private class Calculator : FCalcualte
        {
            public Calculator(xRange[] domain, xRange[] range)
                : base(domain, range)
            { }

            public override void Calculate(double input, double[] output)
            {
                for (int c = 0; c < output.Length; c++)
                    output[c] = input;
            }

            public override void Calculate(double[] input, double[] output)
            {
                for (int c = 0; c < input.Length; c++)
                    output[c] = input[c];
            }
        }
    }
}
