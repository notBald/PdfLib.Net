using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Items array is to array what Elements is to a dictionary, a wrapper.
    /// </summary>
    public abstract class ItemArray : PdfObject, IEnumRef, IKRef
    {
        #region Variables and properties

        /// <summary>
        /// When importing objects from a file on the disk,
        /// this property governs how it will be treated.
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Auto; } }

        /// <summary>
        /// Items in this array
        /// </summary>
        protected PdfArray _items;

        #endregion

        #region Init

        protected ItemArray(PdfArray array) { _items = array; if (array == null) throw new ArgumentNullException(); }

        /// <summary>
        /// Constructor for writable itemarrays
        /// </summary>
        /// <param name="tracker">Tracker that will own this object</param>
        protected ItemArray(bool writable, ResTracker tracker)
            : this(PdfArray.Create(writable, tracker))
        { }

        /// <summary>
        /// Constructor for writable itemarrays
        /// </summary>
        /// <param name="tracker">Tracker that will own this object</param>
        /// <param name="initial_capacity">Set the capacity, all values must then be set</param>
        protected ItemArray(bool writable, ResTracker tracker, PdfItem[] initial)
            : this(PdfArray.Create(writable, tracker, initial))
        { }

        #endregion

        #region IEnumRef

        /// <summary>
        /// If there are children in this dictionary
        /// </summary>
        public bool HasChildren { get { return _items.Length != 0; } }

        /// <summary>
        /// Writable references
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        { get { return new IEnumRefEnImpl(_items.GetEnumerator()); } }

        #endregion

        #region IKRef

        /// <summary>
        /// Default save mode
        /// </summary>
        SM IKRef.DefSaveMode { get { return DefSaveMode; } }

        /// <summary>
        /// If this object has a dummy tracker.
        /// </summary>
        bool IKRef.IsDummy { get { return _items.IsWritable && _items.Tracker == null; } }

        /// <summary>
        /// Adobt this object. Will return false if the adobtion failed.
        /// </summary>
        /// <param name="tracker"></param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker)
        {
            if (_items is TemporaryArray)
                _items = (PdfArray)tracker.Adopt((ICRef)_items);
            return _items.Tracker == tracker;
        }

        /// <summary>
        /// Adobt this object. Will return false if the adobtion failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adobt this item</param>
        /// <param name="state">State information about the adoption</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker, object state)
        {
            if (_items is TemporaryArray)
                _items = (PdfArray)tracker.Adopt((ICRef)_items, state);
            return _items.Tracker == tracker;
        }

        /// <summary>
        /// Checks if a tracker owns this object
        /// </summary>
        /// <param name="tracker">Tracker</param>
        /// <returns>True if the tracker is the owner</returns>
        bool IKRef.IsOwner(ResTracker tracker)
        {
            return _items.Tracker == tracker;
        }

        #endregion

        #region ICRef

        object ICRef.GetChildren() { return ((ICRef)_items).GetChildren(); }

        ICRef ICRef.MakeCopy(object data, ResTracker t)
        { return (ICRef)MakeCopy((PdfArray)((ICRef)_items).MakeCopy(data, t), t); }

        protected abstract ItemArray MakeCopy(PdfArray array, ResTracker tracker);

        //Keep this method in sync with PdfDictionary.LoadResources and PdfArray
        void ICRef.LoadResources(HashSet<object> check)
        {
            if (check.Contains(this))
                return;
            check.Add(this);

            foreach (PdfItem item in _items)
            {
                var val = item;
                if (val is PdfReference)
                    val = ((PdfReference)val).Deref();

                if (val is ICRef)
                {
                    var icref = (ICRef)val;
                    if (icref.Follow)
                        icref.LoadResources(check);
                }
            }
        }

        bool ICRef.Follow { get { return true; } }

        #endregion

        #region IRef

        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference
        { get { if (_items is IRef) return ((IRef)_items).HasReference; return false; } }

        WritableReference IRef.Reference
        {
            [DebuggerStepThrough]
            get { if (_items is IRef) return ((IRef)_items).Reference; return null; }
            set { if (_items is IRef) ((IRef)_items).Reference = value; }
        }

        #endregion

        #region required overrides

        /// <summary>
        /// Tests this object for equality. Beware that it may return 
        /// true on unequal objects that don't specifically support this
        /// method as it only checks the item array.
        /// </summary>
        /// <param name="obj">Object to compare to</param>
        /// <returns>True if the item arrays are equivalent</returns>
        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is ItemArray)
                return _items.Equivalent(((ItemArray)obj)._items);
            return false;
        }

        /// <summary>
        /// Makes a clone of this item
        /// </summary>
        /// <returns></returns>
        public override PdfItem Clone()
        {
            return MakeCopy((PdfArray)_items.Clone(), _items.Tracker);
        }

        /// <summary>
        /// Writes itself to a stream
        /// </summary>
        /// <param name="write"></param>
        internal override void Write(PdfWriter write)
        {
            _items.Write(write);
        }

        public override string ToString()
        {
            return _items.ToString();
        }

        #endregion
    }
}
