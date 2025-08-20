using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Function
{
    //Should probably add a "cleanup" method. Functions can cache quite a bit of data,
    //and currently the data gets regenerated before use anyway (since one can't know
    //if it has changed).
    public abstract class PdfFunction : Elements
    {
        #region Variables and properties

        /// <summary>
        /// An array of 2xm numbers, where m is the number of input values.
        /// Input values outside the domain are clipped
        /// </summary>
        public xRange[] Domain 
        { 
            get 
            { 
                //Not testing (ar.Length & 1) == 0
                var ar = (RealArray)_elems.GetPdfTypeEx("Domain", PdfType.RealArray);
                var m = ar.Length / 2;
                var ret = new xRange[m];
                for (int c = 0, pos = 0; c < m; c++)
                    ret[c] = new xRange(ar[pos++], ar[pos++]);
                return ret;
            } 
        }

        /// <summary>
        /// Number of inputt values for this function
        /// </summary>
        public int InputValues { get { return Domain.Length; } }

        /// <summary>
        /// Number of outputvalues for this function.
        /// </summary>
        public abstract int OutputValues { get; }

        /// <summary>
        /// Required for type 0 and 4, not required for type 1, 2 and 3
        /// Output values beyond the range are clipped
        /// </summary>
        public abstract xRange[] Range { get; }

        internal sealed override PdfType Type { get { return PdfType.Function; } }

        public int FunctionType { get { return _elems.GetUIntEx("FunctionType"); } }

        #endregion

        #region Init

        /// <summary>
        /// For when one wish to create writable objects one can use this constructor
        /// </summary>
        /// <param name="function_type">The type of function this is</param>
        /// <param name="n_inputs">Number of inputs the function will have</param>
        /// <param name="domain">Clips input values, if null will
        /// be set to 0 to 1 for each inputt</param>
        /// <param name="range">
        /// Optional range. Clips output values. (Not optional for Type 0 and 4)
        /// </param>
        protected PdfFunction(int function_type, int n_inputs, xRange[] domain, xRange[] range)
            :base(new TemporaryDictionary())
        {
            _elems.SetInt("FunctionType", function_type);
            if (domain == null)
            {
                domain = new xRange[n_inputs];
                for (int c = 0; c < domain.Length; c++)
                    domain[c] = new xRange(0, 1);
            }
            _elems.DirectSet("Domain", new RealArray(xRange.ToArray(domain)));
            if (range != null)
                _elems.DirectSet("Range", new RealArray(xRange.ToArray(range)));
        }

        /// <summary>
        /// Standar constructor
        /// </summary>
        protected PdfFunction(PdfDictionary dict) : base(dict) { }

        /// <summary>
        /// Ready the function for usage
        /// </summary>
        public abstract FCalcualte Init();

        internal static PdfFunction Create(PdfDictionary dict)
        {
            switch (dict.GetUIntEx("FunctionType"))
            {
                case 2:
                    return new PdfExponentialFunction(dict);

                case 3:
                    return new PdfStitchingFunction(dict);
            }
            throw new NotImplementedException();
        }

        internal static PdfFunction Create(IWStream dict)
        {
            switch (dict.Elements.GetUIntEx("FunctionType"))
            {
                case 0:
                    return new PdfSampleFunction(dict);

                case 4:
                    return new PdfPostScriptFunction(dict);
            }
            throw new NotImplementedException();
        }

        #endregion

        #region Overrides

        internal sealed override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj != null && obj.GetType() == GetType())
            {
                var func = (PdfFunction)obj;
                if (xRange.Compare(Domain, func.Domain) &&
                    OutputValues == func.OutputValues &&
                    xRange.Compare(Range, func.Range))
                {
                    return Equivalent(func);
                }
            }
            return false;
        }

        protected abstract bool Equivalent(PdfFunction obj);

        #endregion
    }

    /// <summary>
    /// An array of PdfFunction
    /// </summary>
    /// <remarks>
    /// Note that this class is assumed to be immutable
    /// 
    /// To change that assumtion, implement the IKRef interface
    /// </remarks>
    public class PdfFunctionArray : TypeArray<PdfFunction>
    {
        #region Variables and properties

        /// <summary>
        /// Returns PdfType.FunctionArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.FunctionArray; } }

        /// <summary>
        /// If this is the identity function
        /// </summary>
        readonly bool _ident;

        /// <summary>
        /// When true the array will be left out during the save
        /// operation if there's only one item in the array
        /// </summary>
        readonly bool _strip_array;

        /// <summary>
        /// If this functions array can be stipped
        /// </summary>
        internal bool StripArray { get { return _strip_array; } }

        /// <summary>
        /// If this is the identity function
        /// </summary>
        internal bool IsIdent { get { return _ident; } }

        #endregion

        #region Init

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Filter items</param>
        /// <param name="identity">True allows for the identity function</param>
        internal PdfFunctionArray(PdfArray items, bool identity, bool strip_array)
            : base(items, PdfType.Function)
        { 
            _ident = identity;
            _strip_array = strip_array;
        }
        internal PdfFunctionArray(PdfItem[] items, bool identity, bool strip_array)
            : this(new TemporaryArray(items), identity, strip_array) { }
        internal PdfFunctionArray(PdfArray items, bool identity)
            : this(items, identity, true) { }
        internal PdfFunctionArray(PdfItem[] items, bool identity)
            : this(items, identity, true) { }


        /// <summary>
        /// Create array with one item.
        /// </summary>
        /// <param name="items">Filter items</param>
        /// <param name="identity">True allows for the identity function</param>
        internal PdfFunctionArray(PdfItem item, bool identity)
            : base(item, PdfType.Function)
        { _ident = identity; _strip_array = true; }

        /// <summary>
        /// Empty array
        /// </summary>
        public PdfFunctionArray() : base(new TemporaryArray(), PdfType.Function) { }

        #endregion

        public void Add(PdfFunction func)
        {
            _items.AddItem(func, func.DefSaveMode == SM.Indirect);
        }

        public new PdfFunction[] ToArray()
        {
            var fa = new PdfFunction[_items.Length];
            for (int c = 0; c < fa.Length; c++)
                fa[c] = this[c];
            return fa;
        }

        #region Overrides

        public override void Set(int index, PdfFunction val)
        {
            _items.SetItem(index, val, true);
        }

        protected override PdfItem GetParam(int index)
        {
            return (_ident) ? this : null;
        }

        /// <summary>
        /// Content is optional, so it can be "null", 
        /// the plain contents item (must be a reference)
        /// or an array of contents streams.
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            //System.Diagnostics.Debug.Assert(false, "Untested code");
            if (_items.Length == 0)
                write.WriteNull();
            else if (_items.Length == 1 && _strip_array)
                _items[0].Write(write);
            else
                _items.Write(write);
        }

        /// <summary>
        /// When being saved as an indirect object, don't
        /// strip the array.
        /// </summary>
        internal override void Write(PdfWriter write, SM store_mode)
        {
            Debug.Assert(store_mode == SM.Indirect);
            _items.Write(write);
        }

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new PdfFunctionArray(array, _ident, _strip_array);
        }

        #endregion
    }
}
