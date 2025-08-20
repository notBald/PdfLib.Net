using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Generic version of PdfArray that casts
    /// object into the type given type
    /// </summary>
    /// <remarks>
    /// Waiting to cast items intil they're needed. The reason
    /// for this is since references must be read from the file
    /// before they can be cast
    /// </remarks>
    public abstract class TypeArray<T> : ItemArray, IEnumerable<T>
        where T:PdfItem
    {
        #region Variables and properties

        /// <summary>
        /// The type the children will be cast to
        /// </summary>
        private readonly PdfType _child_type;

        /// <summary>
        /// Length of the array
        /// </summary>
        public int Length { get { return _items.Length; } }

        #endregion

        #region Init

        /// <summary>
        /// Create empty writable array
        /// </summary>
        protected TypeArray(bool writable, ResTracker tracker, PdfType child_type)
            : base(PdfArray.Create(writable, tracker))
        { _child_type = child_type; }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Will be cast to T</param>
        protected TypeArray(PdfArray items, PdfType child_type)
            : base(items)
        { _child_type = child_type;}

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Will be cast to T</param>
        protected TypeArray(PdfItem[] items, PdfType child_type, bool writable, ResTracker tracker)
            : base(PdfArray.Create(writable, tracker, items))
        { _child_type = child_type; }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Will be cast to T</param>
        protected TypeArray(PdfItem[] items, PdfType child_type)
            : base(new SealedArray(items))
        { _child_type = child_type; }

        /// <summary>
        /// Create array with one item.
        /// </summary>
        /// <param name="items">Will be cast to T</param>
        protected TypeArray(PdfItem item, PdfType child_type)
            : base(new SealedArray(item))
        {
            _child_type = child_type;
        }
        protected TypeArray(PdfItem item, PdfType child_type, bool writable, ResTracker tracker)
            : base(PdfArray.Create(writable, tracker, item))
        {
            _child_type = child_type;
        }
        protected TypeArray() : base(new SealedArray()) {}

        #endregion

        #region Array index operator

        /// <summary>
        /// Fetches the unconverted items. 
        /// </summary>
        internal PdfItem GetItem(int index) { return _items[index]; }

        public T this[int i]
        {

            get 
            { 
                var ret = _items.GetPdfType(i, _child_type, IntMsg.Message, GetParam(i));                
                return (T) ret;
            }
            set { Set(i, value); }
        }

        /// <summary>
        /// To make an array writable override this method and write _items[index] = val
        /// </summary>
        /// <param name="index"></param>
        /// <param name="val"></param>
        public virtual void Set(int index, T val)
        { throw new PdfNotWritableException(); }

        /// <summary>
        /// Returns an enumerator that iterates through the array.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            return new TypeArrayEnumerator<T>(this);
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        private class TypeArrayEnumerator<O> : IEnumerator<O>
            where O:PdfItem
        {
            int _count = -1;
            readonly TypeArray<O> _parent;
            public TypeArrayEnumerator(TypeArray<O> parent)
            { _parent = parent; }
            public bool MoveNext() { return ++_count < _parent.Length; }
            object IEnumerator.Current { get { return Current; } }
            public O Current { get { return _parent[_count]; } }
            public void Reset() { _count = -1; }
            public void Dispose() { }
        }

        #endregion

        #region Other methods

        public T[] ToArray()
        {
            T[] t = new T[_items.Length];
            for (int c = 0; c < t.Length; c++)
                t[c] = this[c];
            return t;
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null) return false;
            if (obj.GetType() == GetType())
            {
                var a2 = (TypeArray<T>)obj;
                if (Length != a2.Length)
                    return false;

                for (int i = 0; i < _items.Length; i++)
                {
                    var i1 = this[i];
                    if (i1 == null)
                    {
                        if (a2[i] == null) continue;
                        return false;
                    }
                    if (!i1.Equivalent(a2[i])) return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Subclasses can overide this to supply parameters
        /// </summary>
        protected virtual PdfItem GetParam(int index)
        {
            return null;
        }

        #endregion
    }
}
