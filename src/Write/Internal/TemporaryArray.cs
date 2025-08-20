using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;


namespace PdfLib.Write.Internal
{
    /// <summary>
    /// An array one can write to without a owner.
    /// </summary>
    /// <remarks>
    /// Used to support adoption
    /// </remarks>
    public sealed class TemporaryArray : PdfArray
    {
        #region Variables and properties

        /// <summary>
        /// A number of functions just checks if there is a tracker
        /// </summary>
        ResTracker _dummy_tracker = null;

        /// <summary>
        /// Instead of glancing at the dummy tracker above those functions
        /// may use this property instead.
        /// </summary>
        public override bool IsWritable { get { return true; } }

        internal override ResTracker Tracker
        {
            get
            {
                //if (_dummy_tracker == null)
                //    _dummy_tracker = new ResTracker();
                return _dummy_tracker;
            }
        }

        #endregion

        #region Init

        public TemporaryArray(List<PdfItem> items)
            : base(items) { }

        public TemporaryArray(params PdfItem[] items)
            : base(items) { }

        /// <summary>
        /// Creates an array of the set size
        /// </summary>
        /// <param name="size">Remeber that every entery of the item array must be set to PdfNull or another value</param>
        internal TemporaryArray(int size)
            : base(new PdfItem[size]) { }

        public TemporaryArray()
            : base() { }

        #endregion

        #region Set methods

        /// <summary>
        /// Injects a newly created item at the index of the array. All items
        /// after this position are moved up one index.
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="index">Set to 0 to add to the head</param>
        /// <param name="reference">If it is to be set as a reference</param>
        internal override void InjectItem(PdfItem item, int index, bool reference)
        {
            lock (_items)
            {
                var source = _items;
                _items = new PdfItem[_items.Length + 1];
                Array.Copy(source, 0, _items, 0, index);
                Array.Copy(source, index, _items, index + 1, source.Length - index);
                SetItem(index, item, reference);
            }
        }

        /// <summary>
        /// Adds a new item and places it at the end of the array
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        internal override void AddNewItem(PdfItem item, bool reference)
        {
            lock (_items)
            {
                int index = _items.Length;
                Array.Resize<PdfItem>(ref _items, index + 1);
                SetNewItem(index, item, reference);
            }
        }

        internal override void SetNewItem(int index, PdfItem item, bool reference)
        {
            if (item is PdfReference)
                throw new PdfInternalException("Don't set a reference with SetNewItem.");

            lock (_items)
            {
                Debug.Assert(_items[index] == null);

                if (reference)
                    //True, since we don't know if this is a unique reference or not.
                    _items[index] = new TempReference(item, true);
                else
                    //Sets the item straight into the dictionary. No refcounting
                    _items[index] = item;
            }
        }

        /// <summary>
        /// Adds a new item and places it at the end of the array
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        public override void AddItem(PdfItem item, bool reference)
        {
            lock (_items)
            {
                int index = _items.Length;
                Array.Resize<PdfItem>(ref _items, index + 1);
                SetItem(index, item, reference);
            }
        }

        internal override void SetItem(int index, PdfItem item, bool reference)
        {
            if (item is PdfReference)
                throw new PdfInternalException("Don't set a reference with SetItem.");

            lock (_items)
            {
                if (reference)
                    //True, since we don't know if this is a unique reference or not.
                    _items[index] = new TempReference(item, true);
                else
                    //Sets the item straight into the dictionary. No refcounting
                    _items[index] = item;
            }
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Removes all items in the array
        /// </summary>
        public override void Clear()
        {
            //for (int c = _items.Length - 1; c >= 0; c--)
            //    this[c] = null;
            _items = new PdfItem[0];
        }

        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            return PdfArray.ToType(type, msg, obj, this);
        }

        /// <summary>
        /// Used when moving the array to another class.
        /// </summary>
        protected override PdfArray MakeCopy(PdfItem[] data, ResTracker tracker)
        {
            return new WritableArray(data, tracker);
        }

        /// <summary>
        /// Makes a clone of this array
        /// </summary>
        public override PdfItem Clone()
        {
            return new TemporaryArray((PdfItem[])_items.Clone());
        }

        #endregion
    }
}
