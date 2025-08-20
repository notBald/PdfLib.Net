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
    /// An array that contains only names
    /// </summary>
    internal sealed class NameArray : ItemArray, IEnumerable<string>
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.NameArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.NameArray; } }

        /// <summary>
        /// Length of the array
        /// </summary>
        public int Length { get { return _items.Length; } }

        #endregion

        #region Init

        public NameArray()
            : base(new TemporaryArray())
        { }

        internal NameArray(PdfArray ar)
            : base(ar)
        { }

        public NameArray(params string[] names)
            : this(new TemporaryArray(names.Length))
        {
            for (int c = 0; c < names.Length; c++)
            {
                var name = names[c];
                _items[c] = name == null ? (PdfItem) PdfNull.Value : new PdfName(name);
            }
        }

        #endregion

        #region indexing

        public string this[int i]
        {
            //This should probably be changed to return null pointers instead of PdfNull.
            //Todo: go over all code that uses this get value (right click find refs)
            get 
            {
                var str = _items[i];
                if (!(str is PdfName) && !(str is PdfNull))
                    throw new PdfCastException(ErrSource.Array, PdfType.Name, ErrCode.WrongType);
                return _items[i].GetString(); 
            }
            set 
            {
                if (value == null)
                    _items[i] = null;
                else
                    _items[i] = new PdfName(value);
            }
        }

        #endregion

        #region Add/Remove

        public void Add(string name)
        {
            if (name == null)
                _items.AddItem(PdfNull.Value);
            else
                _items.Add(name);
        }

        public void Remove(int index)
        {
            _items.Remove(index);
        }

        #endregion

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator() { return _items.GetEnumerator(); }

        public IEnumerator<string> GetEnumerator()
        {
            foreach (PdfItem item in _items)
            {
                if (!(item is PdfName))
                {
                    if (item == PdfNull.Value)
                        yield return null;
                    else
                        throw new PdfCastException(ErrSource.Array, PdfType.Name, ErrCode.WrongType);
                }
                else
                    yield return item.GetString();
            }
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new NameArray(_items);
        }

        #endregion

        #region Utility

        public string[] ToArray()
        {
            var ret = new string[_items.Length];
            for (int c = 0; c < ret.Length; c++)
                ret[c] = this[c];
            return ret;
        }

        #endregion
    }
}
