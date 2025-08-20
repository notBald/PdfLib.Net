using System;
using System.Collections;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Generic version of elements that casts
    /// object into the type given
    /// </summary>
    /// <typeparam name="T">PdfObject</typeparam>
    public abstract class TypeDict<T> : Elements, IEnumerable<KeyValuePair<string, T>>
        where T: PdfObject
    {
        #region Variables

        /// <summary>
        /// The type of the children in the collection
        /// </summary>
        readonly PdfType _child;

        /// <summary>
        /// Message to send when creating children
        /// </summary>
        readonly PdfItem _msg;

        /// <summary>
        /// How many items are in this dictionary
        /// </summary>
        public int Count { get { return _elems.Count; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor for already registered objects
        /// </summary>
        /// <param name="dict">The elements</param>
        /// <param name="child">The type to cast the elements into</param>
        protected TypeDict(PdfDictionary dict, PdfType child, PdfItem msg)
            : base(dict)
        { _child = child; _msg = msg; }

        /// <summary>
        /// Constructs a empty writable TypeDict.
        /// </summary>
        protected TypeDict(bool writable, ResTracker tracker, PdfType child, PdfItem msg)
            : base(writable, tracker)
        {
            _child = child;
            _msg = msg;
        }

        #endregion

        #region Indexing

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
        {
            return new TypeDictEnumerator<T>(_child, _elems, _msg, _child);
        }

        /// <summary>
        /// Retrive PdfObject from dictionary
        /// </summary>
        /// <param name="key">Key of the object</param>
        /// <returns>The PdfObject</returns>
        public T this[string key] 
        { 
            get { return (T)_elems.GetPdfType(key, _child, IntMsg.Message, _msg); }
            set { SetT(key, value); }
        }
        protected virtual void SetT(string key, T item)
        { throw new PdfNotWritableException(); }

        /// <summary>
        /// Enum wrapper
        /// </summary>
        /// <remarks>Do to generics limitation, PdfColorSpace has its own genereic less implementation of this class</remarks>
        private class TypeDictEnumerator<O> : IEnumerator<KeyValuePair<string, O>>
            where O:PdfObject
        {
            readonly IEnumerator<KeyValuePair<string, PdfItem>> _enum;
            readonly PdfType _t;
            readonly PdfDictionary _parent;
            readonly PdfItem _msg; readonly PdfType _child;
            readonly List<KeyValuePair<string, O>> _update;

            public TypeDictEnumerator(PdfType t, PdfDictionary parent, PdfItem msg, PdfType child)
            {
                _t = t;
                _parent = parent;
                _update = new List<KeyValuePair<string, O>>(_parent.Count);
                _msg = msg; _child = child;
                _enum = _parent.GetEnumerator();
            }

            public bool MoveNext() { return _enum.MoveNext(); }
            object IEnumerator.Current { get { return Current; } }
            public KeyValuePair<string, O> Current
            {
                get
                {
                    var kp = _enum.Current;
                    if (kp.Value.Type == _t)
                        return new KeyValuePair<string, O>(kp.Key, (O) kp.Value.Deref());

                    //Transformd the object into the right type
                    var child = kp.Value;
                    var new_kp = new KeyValuePair<string, O>(kp.Key, (O)child.ToType(_child, IntMsg.Message, _msg));

                    //The newly created object must be put in the dictionary, but it will have to wait until
                    //the enumeration is done.
                    if (!(child is PdfReference))
                        _update.Add(new_kp);
                    return new_kp;
                }
            }
            public void Reset() { _enum.Reset(); }
            public void Dispose()
            {
                _enum.Dispose();

                //Updates dictionary objects
                foreach (var kp in _update)
                    _parent.InternalReplace(kp.Key, kp.Value);
            }
        }

        #endregion

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected sealed override Elements MakeCopy(PdfDictionary elems)
        {
            return MakeCopy(elems, _child, _msg);
        }
        protected abstract TypeDict<T> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg);
    }
}
