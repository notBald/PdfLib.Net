using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Write.Internal
{

    /// <remarks>
    /// Dictionary used for objects that lacks a parent (tracker).
    /// 
    /// The temporary dictionary makes no attempt at resource tracking.
    /// This means a bit of extra work for the resource tracker when
    /// it takes ownership over a temporary dictionary.
    /// </remarks>
    public sealed class TemporaryDictionary : PdfDictionary, IRef
    {
        #region Variables and properties

        ResTracker _dummy_tracker = null;

        internal override ResTracker Tracker 
        { 
            get 
            {
                //if (_dummy_tracker == null) _dummy_tracker = new ResTracker();
                return _dummy_tracker; 
            } 
        }

        public override bool IsWritable { get { return true; } }

        #region IRef

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

        #endregion

        #region Init

        public TemporaryDictionary() : base() 
        {

        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="cat">Catalog with values for the dictionary</param>
        internal TemporaryDictionary(Catalog cat)
            : base(cat)
        {

        }

        #endregion

        #region Set methods

        /// <summary>
        /// Sets an item straight into the catalog of the dictionary.
        /// This can potentially set the object into an inconsistent state
        /// </summary>
        /// <remarks>
        /// Will throw if the item exists.
        /// </remarks>
        internal override void DirectSet(string key, PdfItem item)
        {
            lock (_catalog)
            {
                if (item != null)
                    _catalog.Add(key, item);
                else
                    _catalog.Remove(key);
            }
        }

        /// <summary>
        /// Sets a name in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key. Is technically a name too</param>
        /// <param name="name">A plain name (no slash)</param>
        public override void SetName(string key, string name)
        {
            AddItem(key, new PdfName(name));
        }

        /// <summary>
        /// Sets the type key of the the dictionary
        /// </summary>
        /// <param name="name">A plain name (no slash)</param>
        /// <remarks>Will throw if one try to set the
        /// type twice. That's by design.</remarks>
        public override void SetType(string name)
        {
            lock (_catalog)
            {
                _catalog.Add("Type", new PdfName(name));
            }
        }

        /// <summary>
        /// For setting items that are indirect
        /// </summary>
        private void SetIndirectItem(string key, PdfReference item)
        {
            AddItem(key, item);
        }

        /// <summary>
        /// Sets a key to an item
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="item">Item</param>
        private void SetDirectItem(string key, PdfItem item)
        {
            if (item is PdfReference)
                throw new PdfNotSupportedException("References can't be direct items");

#if DEBUG
            //Not forbidden, but an odd thing to do.
            if (item is IRef)
                Debug.Assert(!(item as IRef).HasReference, "Indirect object added as direct");
#endif

            AddItem(key, item);
        }

        /// <summary>
        /// Sets a new item, for use when constructing objects
        /// </summary>
        /// <remarks>
        /// It's a good idea to use "SetTempItem" over this if feasible.
        /// 
        /// Hmm. Or perhaps set that flag true every time a temp ref is
        /// read.
        /// </remarks>
        internal override void SetNewItem(string key, PdfItem item, bool reference)
        {
            if (item is PdfReference)
                throw new PdfInternalException("Don't set a reference with SetNewItem.");

            if (reference)
                SetIndirectItem(key, new TempReference(item));
            else
                AddItem(key, item);
        }

        /// <summary>
        /// Sets an elements as indirect
        /// </summary>
        /// <param name="key">Key to set the element at</param>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If the item should be set as a reference</param>
        /// <remarks>This function allows for setting values to null</remarks>
        public override void SetItem(string key, PdfItem item, bool reference)
        {
            if (reference)
            {
                if (item is PdfReference)
                    throw new PdfInternalException("Reference to reference");
                else if (item == null)
                    SetDirectItem(key, null);
                else
                    SetIndirectItem(key, new TempReference(item, true));
            }
            else
            {
                Debug.Assert(item == null || item.DefSaveMode != SM.Indirect);
                SetDirectItem(key, item);
            }
        }

        /// <summary>
        /// For setting newly created elements as indirect. 
        /// </summary>
        /*public override void SetNewItem(string key, Elements elem)
        {
            IRef iref = (IRef)elem;

            //Some elements create their own reference. Todo: Perhaps a dumb move?
            if (!elem.HasReference)
                Tracker.Register(iref, elem.DefSaveMode);

            SetIndirectItem(key, iref.Reference);
        }//*/

        /// <summary>
        /// Sets a direct boolean
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="boolean">Value</param>
        public override void SetBool(string key, bool boolean)
        {
            AddItem(key, (boolean) ? PdfBool.True : PdfBool.False);
        }

        /// <summary>
        /// Sets a direct int
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="num">Value</param>
        public override void SetInt(string key, int num)
        {
            AddItem(key, new PdfInt(num));
        }

        /// <summary>
        /// Sets a direct float
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="num">Value</param>
        public override void SetReal(string key, double num)
        {
            AddItem(key, new PdfReal(num));
        }

        private void AddItem(string key, PdfItem item)
        {
            lock (_catalog)
            {
                if (item == null)
                {
                    //"null" removes the key. This is
                    //intended, but for testing it's nice to
                    //catch unintended happenstances of this.
                    //Debug.Assert(false, "Item set to null");
                    _catalog.Remove(key);
                }
                else
                {
                    if (_catalog.ContainsKey(key))
                        _catalog[key] = item;
                    else
                        _catalog.Add(key, item);
                }
            }
        }

        #endregion

        /// <summary>
        /// Creates a reverse lookup dictionary
        /// </summary>
        public override Dictionary<PdfItem, string> GetReverseDict()
        {
            lock (_catalog)
            {
                var r = new Dictionary<PdfItem, string>(_catalog.Count);
                foreach (var kp in _catalog)
                {
                    var val = kp.Value;
                    if (!(val is WritableReference))
                        val = val.Deref();
                    r.Add(kp.Value.Deref(), kp.Key);
                }
                return r;
            }
        }

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override PdfDictionary MakeCopy(Catalog data, ResTracker tracker)
        {
            return new WritableDictionary(data, tracker);
        }

        /// <summary>
        /// Makes a clone of this dictionary
        /// </summary>
        public override PdfItem Clone()
        {
            lock (_catalog)
            {
                return new TemporaryDictionary(_catalog.Clone());
            }
        }

        /// <summary>
        /// Removes an item from the dictionary
        /// </summary>
        public override void Remove(string key)
        {
            lock (_catalog)
            {
                _catalog.Remove(key);
            }
        }
    }
}
