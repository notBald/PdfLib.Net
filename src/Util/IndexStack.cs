using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace PdfLib.Util
{
    /// <summary>
    /// Stack implementation that allows for indexing the elements in the stack
    /// </summary>
    /// <typeparam name="T">Type of element in the stack</typeparam>
    public class IndexStack<T> : IEnumerable<T>
    {
        #region Variables

        /// <summary>
        /// Array with stack items
        /// </summary>
        T[] _items;

        /// <summary>
        /// The top of the stack
        /// </summary>
        int _top;

        #endregion

        #region Properties

        /// <summary>
        /// Fetches an item in the stack. Index 0 is the top element
        /// </summary>
        /// <param name="i">Index into the stack</param>
        /// <returns>Item from the stack</returns>
        public T this[int i]
        {
            get
            {
                if (i < 0 || i > _top) throw new IndexOutOfRangeException();

                //Reversing the index so that "0" is the "_top" element
                i = _top - i;

                return _items[i];
            }
        }

        /// <summary>
        /// Number of items in the stack
        /// </summary>
        public int Count { get { return _top + 1; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructs a stack
        /// </summary>
        /// <param name="capacity">Initial space for elements</param>
        public IndexStack(int capacity)
        {
            _items = new T[capacity];
            _top = -1;
        }

        public IndexStack() : this(0) { }

        #endregion

        #region Methods

        /// <summary>
        /// Inserts an item at the given position
        /// </summary>
        public void Insert(int index, T item)
        {
            if (index < 0 || index > _top) throw new IndexOutOfRangeException();
            index = _top - index;
            _top++;
            if (_top >= _items.Length)
                ExpandStack();
            Array.Copy(_items, index, _items, index + 1, _top - index);
            _items[index] = item;
        }

        /// <summary>
        /// Removes the item at the given position
        /// </summary>
        public void Remove(int index)
        {
            if (index < 0 || index > _top) throw new IndexOutOfRangeException();
            index = _top - index;
            if (index == _top--) return;
            Array.Copy(_items, index + 1, _items, index, _top - index);
        }

        [DebuggerStepThrough]
        public void Push(T item)
        {
            _top++;
            if (_top >= _items.Length)
                ExpandStack();

            _items[_top] = item;
        }

        [DebuggerStepThrough]
        public T Peek()
        {
            return _items[_top];
        }

        [DebuggerStepThrough]
        public T Pop()
        {
            return _items[_top--];
        }

        [DebuggerStepThrough]
        public void Clear()
        {
            _top = -1;
        }

        private void ExpandStack()
        {
            T[] tmp = new T[_items.Length * 2 + 1];
            if (_items.Length > 0)
                Array.Copy(_items, 0, tmp, 0, _items.Length);
                //Buffer.BlockCopy(_items, 0, tmp, 0, _items.Length * sizeof(T));
            _items = tmp;
        }

        public override string ToString()
        {
            return string.Format("Stack with {0} items", _top + 1);
        }

        #endregion

        #region IEnumerator

        public IEnumerator GetEnumerator() { return new Enumrer(this); }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumrer(this);
        }

        class Enumrer : IEnumerator<T>
        {
            int _i = -1;
            readonly IndexStack<T> _l;
            public Enumrer(IndexStack<T> l) { _l = l; }
            object IEnumerator.Current { get { return _l[_i]; } }
            public T Current { get { return _l[_i]; } }
            public bool MoveNext() { return ++_i < _l.Count; }
            public void Reset() { _i = -1; }
            public void Dispose() { }
        }

        #endregion
    }
}
