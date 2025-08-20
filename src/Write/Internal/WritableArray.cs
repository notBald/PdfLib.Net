using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Write.Internal
{
    public class WritableArray : PdfArray, IRef
    {
        #region Variables and properties

        readonly ResTracker _tracker;

        internal override ResTracker Tracker
        { get { return _tracker; } }

        public override bool IsWritable { get { return true; } }

        #endregion

        #region Init

        internal WritableArray(List<PdfItem> items, ResTracker tracker)
            : this(items.ToArray(), tracker) { }

        internal WritableArray(PdfItem item, ResTracker tracker)
            : this(new PdfItem[] { item }, tracker) { }

        internal WritableArray(PdfItem[] items, ResTracker tracker)
            : base(items) 
        {
            if (tracker == null) throw new ArgumentNullException();
            _tracker = tracker; 
        }

        internal WritableArray(ResTracker tracker)
        {
            if (tracker == null) throw new ArgumentNullException();
            _tracker = tracker; 
        }

        #endregion

        #region Set methods

        /// <summary>
        /// Adds a new item and places it at the end of the array
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        internal override void AddNewItem(PdfItem item, bool reference)
        {
            int index = _items.Length;
            Array.Resize<PdfItem>(ref _items, index + 1);
            SetNewItem(index, item, reference);
        }

        internal override void SetNewItem(int index, PdfItem item, bool reference)
        {
            if (item is PdfReference)
                throw new PdfInternalException("Don't set a reference with SetNewItem.");
            Debug.Assert(_items[index] == null);

            if (reference)
            {
                WritableReference r = null;
                if (item is IRef)
                {
                    r = ((IRef)item).Reference;
                    if (r == null)
                    {
                        r = _tracker.CreateWRef(item);

                        //Done automatically now
                        //((IRef)item).Reference = r;
                    }
                }
                else
                    r = _tracker.CreateWRef(item);
                SetIndirectItem(index, r);
            }
            else
                //Sets the item straight into the dictionary. No refcounting
                SetOrReplaceItem(index, item);
        }

        /// <summary>
        /// Injects a newly created item at the index of the array. Use
        /// the normal PrependItem if you don't know if you can use this.
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="index">Set to 0 to ad ot the head</param>
        /// <param name="reference">If it is to be set as a reference</param>
        internal override void InjectItem(PdfItem item, int index, bool reference)
        {
            var source = _items;
            _items = new PdfItem[_items.Length + 1];
            Array.Copy(source, 0, _items, 0, index);
            Array.Copy(source, index, _items, index + 1, source.Length - index);
            SetItem(index, item, reference);
        }

        /// <summary>
        /// Adds an item to the end of the array
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="reference">
        /// Whenever the item needs to be a reference or not
        /// </param>
        public override void AddItem(PdfItem item, bool reference)
        {
            int index = _items.Length;
            Array.Resize<PdfItem>(ref _items, index + 1);
            SetItem(index, item, reference);
        }

        internal override void SetItem(int index, PdfItem item, bool reference)
        {
            if (item is PdfReference)
                throw new PdfInternalException("Don't set a reference with SetItem.");

            if (reference)
            {
                WritableReference r = null;
                if (item is IRef)
                {
                    r = ((IRef)item).Reference;
                    if (r == null)
                    {
                        r = _tracker.CreateWRef(item);

                        //Gets done automatically now
                        //((IRef)item).Reference = r;
                    }
                }
                else
                    r = _tracker.CreateWRef(item);
                SetIndirectItem(index, r);
            }
            else
                //Sets the item straight into the array. No refcounting
                SetOrReplaceItem(index, item);
        }

        /// <summary>
        /// For setting items that are indirect
        /// </summary>
        private void SetIndirectItem(int index, PdfReference item)
        {
            //The reason we don't use WritableReference in the parameter is
            //for convinience.
            if (!(item is WritableReference))
                throw new NotSupportedException("Reference must be created for a writable document");

            //Does the needed reference counting, potentially adopting or
            //copying the reference as needed.
            _tracker.RefCountRef(ref item);

            //Puts it in the catalog, taking care to deref any item overwritten.
            SetOrReplaceItem(index, item);
        }

        /// <summary>
        /// Sets a new item into the array
        /// </summary>
        /// <param name="index">Array index of where to place the item</param>
        /// <param name="item">Item to set</param>
        private void SetOrReplaceItem(int index, PdfItem item)
        {
            PdfItem val = _items[index];
            if (val != null)
                //Decrement the old reference
                _tracker.DecRefCount(val);
            _items[index] = item;
        }

        /// <summary>
        /// Removes all items in the array
        /// </summary>
        public override void Clear()
        {
            for (int c = _items.Length - 1; c >= 0; c--)
                this[c] = null;
            _items = new PdfItem[0];
        }

        #endregion

        #region IRef
        //Other classes, such as ItemsArray, use the WritableArray 
        //(or any IRef arrays) to implement the IRef interface.
        //
        //WritableArray itself does not need IRef, as the class is
        //internal, and IRef is for the convinience of PdfLib clients. 

        private WritableReference _ref = null;

        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference { get { return _ref != null; } }

        /// <summary>
        /// This object's reference.
        /// </summary>
        WritableReference IRef.Reference
        {
            [DebuggerStepThrough]
            get { return _ref; }
            set { _ref = value; }
        }

        #endregion

        #region Overrides

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
            return new WritableArray((PdfItem[])_items.Clone(), _tracker);
        }

        #endregion
    }
}
