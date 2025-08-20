using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// An array of floats. "null" is not allowed. 
    /// Though indirect values are allowed.
    /// </summary>
    /// <remarks>Can not be changed / written to</remarks>
    internal sealed class RealArray : PdfArray, IRealArray
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.RealArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.RealArray; } }

        /// <summary>
        /// Real arrays can be writable
        /// </summary>
        ResTracker _tracker;

        internal override ResTracker Tracker { get { return _tracker; } }

        public override bool IsWritable { get { return _tracker != null; } }

        #endregion

        #region Init

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        public RealArray(params double[] reals)
         : base(new PdfItem[reals.Length])
        {
            for (int c = 0; c < reals.Length; c++)
                _items[c] = new PdfReal(reals[c]);
        }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        internal RealArray(PdfItem[] items)
            : base(items) { }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        internal RealArray(PdfItem[] items, ResTracker tracker)
            : base(items) { _tracker = tracker; }

        /// <summary>
        /// Create array with one item.
        /// </summary>
        /*internal RealArray(PdfItem item)
            : base(item) { }*/

        #endregion

        #region Array index operator

        /// <summary>
        /// Gets or sets a floating point value
        /// </summary>
        /// <param name="i">Array index</param>
        /// <returns>a floating point value</returns>
        public new double this[int i]
        {
            get { return _items[i].GetReal(); }
            set 
            {
                if (_tracker == null)
                    throw new PdfNotWritableException();
                _tracker.DecRefCount(_items[i]);
                _items[i] = new PdfReal(value); 
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the array.
        /// </summary>
        public new IEnumerator<double> GetEnumerator()
        {
            return new RealArrayEnumerator(this);
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        internal class RealArrayEnumerator : IEnumerator<double>
        {
            int _count = -1;
            readonly IRealArray _parent;
            public RealArrayEnumerator(IRealArray parent)
            { _parent = parent; }
            public bool MoveNext() { return ++_count != _parent.Length; }
            object IEnumerator.Current { get { return Current; } }
            public double Current { get { return _parent[_count]; } }
            public void Reset() { _count = -1; }
            public void Dispose() { }
        }

        #endregion

        #region Mix methods

        /// <summary>
        /// Converts to a plain double array
        /// </summary>
        public double[] ToArray()
        {
            var ret = new double[_items.Length];
            for (int c = 0; c < ret.Length; c++)
                ret[c] = _items[c].GetReal();
            return ret;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Makes a copy of the array
        /// </summary>
        /// <remarks>Assumes items to be immutable</remarks>
        public override PdfItem Clone()
        {
            return new RealArray((PdfItem[])_items.Clone());
        }

        /// <summary>
        /// Used when moving the array to a different document
        /// </summary>
        protected override PdfArray MakeCopy(PdfItem[] data, ResTracker t)
        {
            return new RealArray(data);
        }

        #endregion
    }

    internal interface IRealArray : IEnumerable<double>
    {
        double this[int i] { get; set; }
        int Length { get; }

        /// <summary>
        /// Converts to a plain double array
        /// </summary>
        double[] ToArray();
    }
}
