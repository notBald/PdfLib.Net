//#define SAFE_TOSTRING //for when debuging the implementation
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Internal.Minor;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Function;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Annotation;
using PdfLib.Pdf.Form;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// A Pdf Array
    /// </summary>
    public abstract class PdfArray : PdfObject, IEnumRef, ICRef, IEnumerable
    {
        #region Variables and properties

        /// <summary>
        /// Returns PdfType.Array
        /// </summary>
        internal override PdfType Type { get { return PdfType.Array; } }

        /// <summary>
        /// If this is a writable array
        /// </summary>
        public abstract bool IsWritable { get; }

        /// <summary>
        /// Length of the array
        /// </summary>
        public int Length 
        { 
            get { return _items.Length; }
            internal set
            {
                if (!IsWritable) throw new PdfNotWritableException();
                lock(_items)
                    Array.Resize<PdfItem>(ref _items, value);
            }
        }

        /// <summary>
        /// The items in the array
        /// </summary>
        protected PdfItem[] _items;

        /// <summary>
        /// Gets a res tracker, null if not avalible.
        /// </summary>
        internal virtual ResTracker Tracker { get { return null; } }

        #endregion

        #region Init

        /// <summary>
        /// Create array from list
        /// </summary>
        protected PdfArray(List<PdfItem> items)
        { _items = items.ToArray(); }

        /// <summary>
        /// Create empty array
        /// </summary>
        protected PdfArray() { _items = new PdfItem[0]; }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        protected PdfArray(PdfItem[] items)
        { _items = items; }

        /// <summary>
        /// Create array from a single item.
        /// </summary>
        protected PdfArray(PdfItem item)
            : this(new PdfItem[] { item }) { }


        /// <summary>
        /// Helper method for creating a writable array when applicable
        /// </summary>
        internal static PdfArray Create(bool writable, ResTracker tracker)
        {
            return tracker == null ? (writable ? new TemporaryArray() : (PdfArray) new SealedArray()) : 
                new WritableArray(tracker);
        }

        /// <summary>
        /// Helper method for creating a writable array when applicable
        /// </summary>
        internal static PdfArray Create(bool writable, ResTracker tracker, PdfItem[] items)
        {
            return tracker == null ? (writable ? new TemporaryArray(items) : (PdfArray)new SealedArray(items))
                : new WritableArray(items, tracker);
        }

        /// <summary>
        /// Helper method for creating a writable array when applicable
        /// </summary>
        internal static PdfArray Create(bool writable, ResTracker tracker, PdfItem item)
        {
            return tracker == null ? (writable ? new TemporaryArray(item) : (PdfArray)new SealedArray(item)) 
                : new WritableArray(item, tracker);
        }

        #endregion

        #region Abstract set methods

        /// <summary>
        /// Inserts a number at the position. All items
        /// after this position are moved up one index.
        /// </summary>
        /// <param name="index">Index to insert at</param>
        /// <param name="name">Name to insert</param>
        public void Insert(int index, int val)
        { InjectItem(new PdfInt(val), index, false); }

        /// <summary>
        /// Inserts a name at the position. All items
        /// after this position are moved up one index.
        /// </summary>
        /// <param name="index">Index to insert at</param>
        /// <param name="name">Name to insert</param>
        public void Insert(int index, string name)
        { InjectItem(new PdfName(name), index, false); }

        /// <summary>
        /// Injects a newly created item at the index of the array. All items
        /// after this position are moved up one index.
        /// </summary>
        /// <param name="item">Item to prepend</param>
        /// <param name="index">Set to 0 to add to the head</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        internal virtual void InjectNewItem(PdfItem item, int index, bool reference)
        { InjectItem(item, index, reference); }

        /// <summary>
        /// Injects a newly created item at the index of the array. All items
        /// after this position are moved up one index.
        /// </summary>
        /// <param name="item">Item to prepend</param>
        /// <param name="index">Set to 0 to add to the head</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        internal virtual void InjectItem(PdfItem item, int index, bool reference)
        { throw new PdfNotWritableException(); }

        /// <summary>
        /// Adds a newly created item and places it at the end of the array. Use
        /// the normal AddItem if you don't know if you can use this.
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        internal virtual void AddNewItem(PdfItem item, bool reference)
        { AddItem(item, reference); }

        /// <summary>
        /// Sets a newly created item into the array
        /// </summary>
        /// <param name="index">Array index of where to place the item</param>
        /// <param name="item">Item to set</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        internal virtual void SetNewItem(int index, PdfItem item, bool reference)
        { SetItem(index, item, reference); }

        /// <summary>
        /// Adds a number
        /// </summary>
        /// <param name="val">Value of number</param>
        public void Add(int val) { AddItem(new PdfInt(val)); }

        /// <summary>
        /// Adds a number
        /// </summary>
        /// <param name="val">Value of number</param>
        public void Add(double val) { AddItem(new PdfReal(val)); }

        /// <summary>
        /// Adds a name
        /// </summary>
        /// <param name="name">The name</param>
        public void Add(string name) { AddItem(new PdfName(name)); }

        /// <summary>
        /// Adds a item and places it at the end of the array.
        /// </summary>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        public virtual void AddItem(PdfItem itm, bool reference)
        { throw new PdfNotWritableException(); }

        /// <summary>
        /// Adds an item, automatically sets it with reference if there is one.
        /// </summary>
        internal void AddItem(IRef itm)
        {
            AddItem((PdfItem) itm, itm.HasReference);
        }

        /// <summary>
        /// Adds an item
        /// </summary>
        public void AddItem(PdfItem itm)
        {
            if (itm is IRef)
                AddItem((IRef)itm);
            else
                AddItem(itm, false);
        }

        /// <summary>
        /// Sets a item into the array
        /// </summary>
        /// <param name="index">Array index of where to place the item</param>
        /// <param name="item">Item to set</param>
        /// <param name="reference">If this item is to be set with a rererence</param>
        internal virtual void SetItem(int index, PdfItem item, bool reference) 
        { throw new PdfNotWritableException(); }

        #endregion

        #region Remove

        /// <summary>
        /// Removes an item from the array, changes the array's size.
        /// </summary>
        /// <param name="itm"></param>
        public void Remove(PdfItem itm)
        {
            lock (_items)
            {
                var i = Util.ArrayHelper.IndexOf(_items, 0, itm.Deref(), _items.Length);
                if (i == -1 && itm is IRef)
                {
                    var iref = (IRef)itm;
                    if (iref.HasReference)
                        i = Util.ArrayHelper.IndexOf(_items, 0, iref.Reference, _items.Length);
                }
                if (i != -1)
                    Remove(i);
            }
        }

        /// <summary>
        /// Removes the item at the given index
        /// </summary>
        /// <param name="idx">Index of item to remove</param>
        public void Remove(int idx)
        {
            lock (_items)
            {
                SetItem(idx, null, false);
                var temp = new PdfItem[_items.Length - 1];
                Array.Copy(_items, temp, idx);
                if (idx + 1 < temp.Length)
                    Array.Copy(_items, idx + 1, temp, idx, temp.Length - idx);
                _items = temp;
            }
        }

        #endregion

        #region Array index operator

        /// <summary>
        /// Fetch or set the items in this array, note that "null" values are
        /// returned as PdfNull, and not null pointers. However one can safely
        /// set null pointers, they are silently converted to PdfNull.
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public PdfItem this[int i]
        {
            get { return _items[i]; }
            set { SetItem(i, value, false); }
        }

        public void Set(int i, string name)
        {
            this[i] = new PdfName(name);
        }

        public void Set(int i, int val)
        {
            this[i] = new PdfInt(val);
        }

        public void Set(int i, double val)
        {
            this[i] = new PdfReal(val);
        }

        /// <summary>
        /// Gets a objected of the desired type.
        /// </summary>
        internal PdfItem GetPdfTypeEx(int index, PdfType type)
        {
            return GetPdfTypeEx(index, type, IntMsg.NoMessage, null);
        }

        /// <summary>
        /// Gets a objected of the desired type.
        /// </summary>
        internal PdfItem GetPdfTypeEx(int index, PdfType type, IntMsg message, object obj)
        {
            lock (_items)
            {
                var org = _items[index];
                //No need to check for PdfNull as ToType handles that.
                var ret = (PdfItem)org.ToType(type, message, obj);
                if (!(org is PdfReference))
                    _items[index] = (PdfItem)ret;
                return (PdfItem)ret;
            }
        }

        /// <summary>
        /// Gets a objected of the desired type.
        /// </summary>
        internal PdfItem GetPdfType(int index, PdfType type, IntMsg message, object obj)
        {
            lock (_items)
            {
                var org = _items[index];
                if (org == PdfNull.Value) return null;
                var ret = (PdfItem)org.ToType(type, message, obj);
                if (!(org is PdfReference))
                    _items[index] = (PdfItem)ret;
                return (PdfItem)ret;
            }
        }

        internal string GetNameEx(int index)
        {
            var org = _items[index];
            if (org == PdfNull.Value)
                throw new PdfReadException(ErrSource.Array, PdfType.Name, ErrCode.Missing);
            var itm = org.Deref();
            if (itm is PdfName) return itm.GetString();
            throw new PdfReadException(ErrSource.Array, PdfType.Name, ErrCode.WrongType);
        }

        internal PdfArray GetArrayEx(int index)
        {
            var org = _items[index];
            if (org == null)
                throw new PdfReadException(PdfType.Array, ErrCode.Missing);
            var itm = org.Deref();
            if (itm is PdfArray) return (PdfArray)itm;
            throw new PdfReadException(ErrSource.Array, PdfType.Array, ErrCode.WrongType);
        }

        public bool GetBoolEx(int index)
        {
            var b = _items[index].Deref();
            if (b is PdfBool) return ((PdfBool)b).Value;
            else throw new PdfReadException(ErrSource.Array, PdfType.Bool, ErrCode.WrongType);
        }

        public PdfString GetStringObjEx(int index)
        {
            var s = _items[index].Deref();
            if (s is PdfString) return ((PdfString)s);
            else throw new PdfReadException(ErrSource.Array, PdfType.String, ErrCode.WrongType);
        }

        public PdfName GetName(int index)
        {
            return _items[index].Deref() as PdfName;
        }

        #endregion

        #region IEnumRef

        /// <summary>
        /// If there are children in this dictionary
        /// </summary>
        public bool HasChildren
        { get { return _items.Length != 0; } }

        /// <summary>
        /// Writable references
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        { get { return new IEnumRefEnImpl(_items.GetEnumerator()); } }

        #endregion

        #region ICRef

        object ICRef.GetChildren() { return _items; }

        ICRef ICRef.MakeCopy(object data, ResTracker t) { return (ICRef)MakeCopy((PdfItem[])data, t); }

        protected abstract PdfArray MakeCopy(PdfItem[] data, ResTracker tracker);

        //Keep this method in sync with PdfDictionary.LoadResources and ItemArray
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

        #region Other methods

        /// <summary>
        /// Removes all items in the array
        /// </summary>
        public virtual void Clear()
        {
            throw new PdfNotWritableException();
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj))
                return true;

            if (obj == null)
                return false;

            if (obj is PdfArray)
            {
                var a2 = ((PdfArray)obj)._items;
                var items = _items;
                if (items.Length != a2.Length)
                    return false;

                for (int i = 0; i < items.Length; i++)
                {
                    var i1 = items[i];
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
        /// Returns what PdfVersion information
        /// this object has avalible.
        /// </summary>
        internal sealed override PdfVersion GetPdfVersion(bool fetch, HashSet<object> set)
        {
            //First get the version of this class
            PdfVersion ver = base.PdfVersion;
            set.Add(this);

            //Then peek on the children.
            var items = _items;
            Monitor.Enter(items);
            try
            {
                for (int c = 0; c < items.Length; c++)
                {
                    var kid = items[c];
                    if (set.Contains(kid)) continue;
                    Monitor.Exit(items);
                    try 
                    { 
                        var v = (fetch) ? kid.GetPdfVersion(true, set) : kid.PdfVersion;
                        if (v > ver) ver = v;
                    }
                    finally { Monitor.Enter(items); }
                }
            } finally { Monitor.Exit(items); }

            return ver;
        }

        /// <summary>
        /// Makes a clone of the array
        /// </summary>
        /*public override PdfItem Clone()
        {
            return MakeCopy((PdfItem[]) _items.Clone(), Tracker);
        }*/

        /// <summary>
        /// Makes a copy of the array
        /// </summary>
        /// <remarks>Assumes items to be immutable</remarks>
        /*protected override object Copy()
        {
            //Debug.Assert(false, "This method should probably not be used");
            var ret = new PdfItem[_items.Length];
            for (int c = 0; c < ret.Length; c++)
            {
                var itm = _items[c];
                ret[c] = (itm is PdfObject) ? itm.Clone() : itm;
            }
            return new SealedArray(ret);
        }*/

        /// <summary>
        /// Flattens an array to only contain PdfInt or PdfReal.
        /// </summary>
        /// <returns>
        /// New array if flattened, same array if there
        /// was no need to flatten.
        /// </returns>
        protected PdfItem[] FlattenNumeric()
        {
            var items = _items;
            for (int c = 0; c < items.Length; c++)
            {
                PdfItem itm = items[c];
                if (itm is PdfInt || itm is PdfReal) continue;

                var ar = new PdfItem[items.Length];
                Array.Copy(items, ar, c);
                for (; c < items.Length; c++)
                {
                    itm = items[c].Deref();
                    if (itm is PdfInt || itm is PdfReal) ar[c] = itm;
                    else ar[c] = new PdfReal(itm.GetReal());
                }
                return ar;
            }
            return items;
        }

        /// <summary>
        /// String representation of the array
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(string.Format("{0} [", GetType().Name));
            var items = _items;
            if (items.Length > 0)
            {
                sb.Append(items[0].ToString());
                for (int c = 1; c < items.Length; c++)
                {
                    sb.Append(' ');
#if SAFE_TOSTRING
                    sb.Append("item "+(c+1));
#else
                    sb.Append(items[c].ToString());
#endif
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// Create high level arrays
        /// </summary>
        internal static PdfObject ToType(PdfType type, IntMsg msg, object obj, PdfArray items)
        {
            switch(type)
            {
                case PdfType.ColorSpace:
                    return ColorSpace.PdfColorSpace.Create(items, msg, obj);

                case PdfType.FilterArray:
                    return new Filter.FilterArray(items);

                case PdfType.IntArray:
                    return new IntArray(items._items);

                case PdfType.RealArray:
                    return new RealArray(items._items);

                case PdfType.PageTree:
                    return PageTree.Create(items, (PdfPages) obj);

                case PdfType.ContentArray:
                    return new PdfContentArray(items);

                case PdfType.FilterParmsArray:
                    return new Filter.FilterParmsArray(items, (Filter.FilterArray) obj);

                case PdfType.FunctionArray:
                    return new PdfFunctionArray(items, msg == IntMsg.Special, msg != IntMsg.DoNotChange);

                case PdfType.AnnotationElms:
                    return new Annotation.AnnotationElms(items, (PdfPage) obj);

                case PdfType.TreeNodeArray:
                    return new NodeArray(items);

                case PdfType.Limit:
                    return new PdfLimit(items);

                case PdfType.DictArray:
                    return new DictArray(items);

                case PdfType.Destination:
                    return new PdfDestination(items);

                case PdfType.ActionArray:
                    var ar = (object[]) obj;
                    return new PdfActionArray(items, (PdfCatalog) ar[0], msg == IntMsg.ResTracker, (ResTracker) ar[1]);

                case PdfType.DocumentID:
                    return new PdfDocumentID(items);

                case PdfType.AnnotBorder:
                    return new PdfAnnotBorder(items);

                case PdfType.NameArray:
                    return new NameArray(items);

                case PdfType.FieldDictionaryAr:
                    return new PdfFormFieldAr(items);

                case PdfType.CalloutLine:
                    return new PdfFreeTextAnnot.CalloutLine(items);

                default:
                    return Read.Parser.Parser.CreatePdfType(items._items, type);
            }
        }

        /// <summary>
        /// Writes itself to a stream
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            write.WriteArray(_items);
        }

        #endregion

        #region IEnumerable
    
        /// <summary>
        /// Returns an enumerator that iterates through the array.
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        #endregion

        #region Static helper methods

        /// <summary>
        /// Convert an array of names into a array of strings
        /// </summary>
        internal static string[] ToNameArray(PdfArray ar)
        {
            var r = new string[ar.Length];
            for (int c=0; c < r.Length; c++)
            {
                var itm = ar[c].Deref();
                if (!(itm is PdfName))
                    throw new PdfReadException(ErrSource.Array, PdfType.Name, ErrCode.WrongType);
                r[c] = itm.GetString();
            }
            return r;
        }

        #endregion
    }
}
