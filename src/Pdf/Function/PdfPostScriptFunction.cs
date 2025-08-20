using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Filter;
using PdfLib.Write.Internal;
using PdfLib.PostScript;
using PdfLib.PostScript.Primitives;

namespace PdfLib.Pdf.Function
{
    /// <summary>
    /// Executes a postscript. Use PdfPSFCreator to make these functions
    /// </summary>
    /// <remarks>Also called "FunctionType4"</remarks>
    public sealed class PdfPostScriptFunction : PdfFunction, ICStream
    {
        #region Variables and properties

        readonly IWStream _stream;

        /// <summary>
        /// Stream based objects must be indirect
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        public override xRange[] Range
        {
            get { return xRange.Create((RealArray)_elems.GetPdfTypeEx("Range", PdfType.RealArray)); }
        }

        public override int OutputValues { get { return Range.Length; } }

        /// <summary>
        /// The post script as human readable text
        /// </summary>
        internal string PostScript { get { return new PdfContent(_stream, _elems).ContentText; } }

        #endregion

        #region Init

        internal PdfPostScriptFunction(IWStream stream)
            : base(stream.Elements)
        { _stream = stream; }

        private PdfPostScriptFunction(IWStream stream, PdfDictionary dict)
            : base(dict)
        { _stream = stream; }

        public override FCalcualte Init()
        {
            return new Calculator(Domain, Range, _stream.DecodedStream);
        }

        #endregion

        #region ICStream

        void ICStream.Compress(List<FilterArray> filters, CompressionMode mode)
        {
            ((ICStream)_stream).Compress(filters, mode);
        }

        PdfDictionary ICStream.Elements { get { return _elems; } }
        void ICStream.LoadResources()
        {
            _stream.LoadResources();
        }

        #endregion

        #region Boilerplate

        protected override bool Equivalent(PdfFunction obj)
        {
            var func = (PdfPostScriptFunction)obj;
            return PostScript == func.PostScript;
        }

        internal override void Write(PdfWriter write)
        {
            throw new PdfInternalException("PostScript functions can't be saved as a direct object");
        }

        internal override void Write(PdfWriter write, SM store_mode)
        {
            if (store_mode == SM.Direct)
                throw new PdfInternalException("Sample functions can't be saved as a direct object");
            _stream.Write(write);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfPostScriptFunction(_stream, _elems);
        }

        protected override void LoadResources()
        {
            _stream.LoadResources();
        }

        protected override void DictChanged()
        {
            _stream.SetDictionary(_elems);
        }

        #endregion

        private class Calculator : FCalcualte
        {
            PSInterpreter _ps;
            PSProcedure _proc;

            public Calculator(xRange[] domain, xRange[] range, byte[] data)
                : base(domain, range)
            {
                _ps = new PSInterpreter();
                _ps.Run(data);
                _proc = _ps.PopProc();
                _ps.MakeFast(_proc);
            }

            public override void Calculate(double input, double[] output)
            {
                Calculate(new double[] { input }, output);
            }

            public override void Calculate(double[] input, double[] output)
            {
                //Prepears the interprentor
                _ps.Clear();

                //Clips input to domain and pushes onto the _ps stack
                for (int c = 0; c < input.Length; c++)
                {
                    var d = _domains[c];
                    _ps.PushNum(d.Clip(input[c]));
                }

                //Executes.
                _ps.Run(_proc);

                //Gets data back out.
                for (int c = output.Length - 1; c >= 0; c--)
                    output[c] = _ps.PopNum();
            }
        }
    }

    /// <summary>
    /// For creating post script functions
    /// </summary>
    public class PdfPSFCreator
    {
        MemoryStream _script = new MemoryStream(64);
        PdfLib.Write.Internal.PdfWriter _writer;

        /// <summary>
        /// The post script as human readable text
        /// </summary>
        internal string PostScript { get { return ASCIIEncoding.ASCII.GetString(_script.ToArray()); } }

        public PdfPSFCreator()
        {
            _writer = new PdfWriter(_script, SaveMode.Compressed, PM.None, CompressionMode.None, PdfVersion.V00);
            _writer.Write((byte) '{');
        }

        public void Exp(double exp)
        {
            _writer.WriteDouble(exp);
            _writer.WriteKeyword("exp");
        }

        public void Exp(double num, double exp)
        {
            _writer.WriteDouble(num);
            _writer.WriteDouble(exp);
            _writer.WriteKeyword("exp");
        }

        /// <summary>
        /// Creates a new PostScript function
        /// </summary>
        /// <param name="domain">Input values are clipped to domain.</param>
        /// <param name="range">Output values are clipped to range</param>
        /// <returns>The PostScript function</returns>
        public PdfPostScriptFunction CreateFunction(xRange[] domain, xRange[] range)
        {
            var dict = new TemporaryDictionary();
            dict.SetInt("FunctionType", 4);
            if (domain == null || domain.Length == 0)
                throw new ArgumentException("domain");
            dict.SetItem("Domain", new RealArray(xRange.ToArray(domain)), false);
            if (range == null || range.Length == 0)
                throw new ArgumentException("range");
            dict.SetItem("Range", new RealArray(xRange.ToArray(range)), false);
            var new_s = new MemoryStream(_script.ToArray());
            var new_w = new PdfWriter(new_s, SaveMode.Compressed, PM.None, CompressionMode.None, PdfVersion.V00);
            _writer.Write((byte) '}');
            _writer = new_w;
            var ms = _script;
            _script = new_s;
            return new PdfPostScriptFunction(new WrMemStream(dict, ms));
        }
    }
}
