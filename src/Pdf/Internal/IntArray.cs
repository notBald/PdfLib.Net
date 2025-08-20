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
    /// An array of integers. "null" is not allowed. 
    /// Though indirect values are allowed.
    /// </summary>
    /// <remarks>Can not be changed / written to</remarks>
    internal sealed class IntArray : PdfArray, IEnumerable<int>
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.IntArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.IntArray; } }

        public override bool IsWritable { get { return false; } }

        #endregion

        #region Init

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        internal IntArray(int[] items)
            : base(new PdfItem[items.Length])
        {
            for (int c = 0; c < items.Length; c++)
                _items[c] = new PdfInt(items[c]);
        }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        internal IntArray(PdfItem[] items)
            : base(items) { }

        /// <summary>
        /// Create array with one item.
        /// </summary>
        /*internal IntArray(PdfItem item)
            : base(item) { }*/

        #endregion

        #region Array index operator

        public new int this[int i]
        {
            get { return _items[i].GetInteger(); }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the array.
        /// </summary>
        public new IEnumerator<int> GetEnumerator()
        {
            return new IntArrayEnumerator(this);
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        private class IntArrayEnumerator : IEnumerator<int>
        {
            int _count = -1;
            readonly IntArray _parent;
            public IntArrayEnumerator(IntArray parent)
            { _parent = parent; }
            public bool MoveNext() { return ++_count != _parent.Length; }
            object IEnumerator.Current { get { return Current; } }
            public int Current { get { return _parent[_count]; } }
            public void Reset() { _count = -1; }
            public void Dispose() { }
        }

        #endregion

        #region Mix methods

        /// <summary>
        /// Converts to a plain int array
        /// </summary>
        public int[] ToArray()
        {
            var ret = new int[_items.Length];
            for (int c = 0; c < ret.Length; c++)
                ret[c] = _items[c].GetInteger();
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
            return new IntArray((PdfItem[]) _items.Clone());
        }

        /// <summary>
        /// Used when moving the array to a different document
        /// </summary>
        protected override PdfArray MakeCopy(PdfItem[] data, ResTracker tracker)
        {
            return new IntArray(data);
        }

        #endregion
    }
}
