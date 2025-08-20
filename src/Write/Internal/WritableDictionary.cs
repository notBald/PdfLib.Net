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
    /// Objects can be "set" six ways:
    ///  1. It's a newly created object, inserted directly
    ///  2. It's a newly created object, inserted indirectly
    ///  3. It's a object imported from a file, inserted directly
    ///  4. It's a object imported from a file, inserted indirectly
    ///  5. It's an existing object taken from another part of the document. Inserted directly.
    ///  6. It's an existing object taken from another part of the document. Inserted indirectly.
    /// 
    /// Futher there is:
    ///  IRef objects, Items, References.
    ///  1. Usualy IRef objects that are not elements are of the "BaseArray" kind. There's
    ///     no real support for plain IRef's right now, but they can be set as direct objects.
    ///  2. Items can be anything, so one must test if they are references or IRef
    ///  3. References must be owned by writable dictionaries, non-writable don't care.
    /// 
    /// Refcounting is to work like this: (when put in the dictionary)
    ///  1. Do nothing, assume all childreferences to be 1. Never set a "New" direct object twice.
    ///  2. Set refcount to 1, don't bother with child references. New iobjects can be set twice,
    ///     since the "newly created" flag is cleared.
    ///  3. Parser incs the refount, do nothing.
    ///  4. Parser incs the refount, do nothing.
    ///  5. Refcount all child references, if refcount is zero refcount the child's child references
    ///  6. Inc refcount. If refcount was zero, refcount child references, and possibly
    ///     their children and so on.
    ///     
    /// How to proceed when adding to the dictionary:
    ///  1. Creating a new direct object (IRef or not IRef)
    ///     SetNewItem(key, item, false)
    ///  1b. Setting the new direct object again.
    ///     SetDirectItem(key, item) or SetItem(item)
    ///     -- Note that SetItem may copy the direct object
    ///  2. Creating a new indirect object (not IRef)
    ///     SetNewItem(key, item, true)
    ///  2b Creating a new indirect object (Elements)
    ///     SetNewItem(key, Element) or SetItem(key, Element, true)
    ///     -- Note that SetItem may copy the object
    ///     -- Note that SetNewItem use the default store mode of 
    ///        the element, which means the element silently may
    ///        be set direct. If the element must be indirect use 2c
    ///        with store mode "indirect".
    ///  2c Creating a new indirect object (IRef)
    ///     _tracker.Register(...), SetIndirectItem(key, item.Reference)
    ///  3-4 Handled by parser
    ///  5. Not currently supported (Must take ownership of references through some means)
    ///  6. Not currently supported
    ///  
    ///  Note that the Set* primitive functions are safe to use. They are
    ///  always direct and there's no child references to worry about.
    /// </remarks>
    internal sealed class WritableDictionary : PdfDictionary, IRef
    {
        #region Variables and properties

        private readonly ResTracker _tracker;

        internal override ResTracker Tracker { get { return _tracker; } }

        public override bool IsWritable { get { return true; } }

        #region IRef
        //Other classes, such as Elements, use the WritableDictionary 
        //(or any IRef dictionaries) to implement the IRef interface.
        //
        //WritableDictionary itself does not need IRef, as the class is
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

        #endregion

        #region Init

        /// <summary>
        /// All writable dictionaries must have a resource tracker
        /// </summary>
        internal WritableDictionary(ResTracker tracker) : base() 
        {
            if (tracker == null) throw new ArgumentNullException();
            _tracker = tracker;
        }

        /// <summary>
        /// All writable dictionaries must have a resource tracker
        /// </summary>
        /// <param name="cat">Catalog with values for the dictionary</param>
        /// <param name="tracker">Used for resource tracking</param>
        /// <param name="ref_count">
        /// If references in the catalog is to be refcounted. Catalogs
        /// created from the parser is already ref counted, whereas
        /// catalogs created the manual way is not.
        /// (Not sure if there's a need for this feature)
        /// </param>
        internal WritableDictionary(Catalog cat, ResTracker tracker)
            : base(cat)
        {
            if (tracker == null) throw new ArgumentNullException();
            _tracker = tracker;
        }

        #endregion

        #region Set methods

        /// <summary>
        /// Sets a name in the dictionary
        /// </summary>
        /// <param name="key">A dictionary key. Is technically a name too</param>
        /// <param name="name">A plain name (no slash)</param>
        public override void SetName(string key, string name)
        {
            AddOrReplaceItem(key, new PdfName(name));
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
                //Add will throw if the key exists
                _catalog.Add("Type", new PdfName(name));
            }
        }

        /// <summary>
        /// For setting items that are indirect
        /// </summary>
        private void SetIndirectItem(string key, PdfReference item)
        {
            //The reason we don't use WritableReference in the parameter is
            //for convinience.
            if (!(item is WritableReference))
                throw new NotSupportedException("Reference must be created for a writable document");

            //Does the needed reference counting, potentially adopting or
            //copying the reference as needed.
            _tracker.RefCountRef(ref item);

            //Puts it in the catalog, taking care to deref any item overwritten.
            AddOrReplaceItem(key, item);
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

            //The item may have inner references in need of refcounting
            _tracker.RefCountItem(ref item);

            //Puts it in the catalog, taking care to deref any item overwritten.
            AddOrReplaceItem(key, item);
        }

        /// <summary>
        /// This function is only to be used during object creation
        /// </summary>
        /// <remarks>
        /// This function does not refcount directly set items. If
        /// the item is to be referenced then one could use other
        /// methods just fine, but it's nice to use the same function
        /// consitantly, and it allso makes it easy to change between
        /// referenced and unreferenced.
        /// </remarks>
        internal override void SetNewItem(string key, PdfItem item, bool reference)
        {
            if (item is PdfReference)
                throw new PdfInternalException("Don't set a reference with SetNewItem.");

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
                SetIndirectItem(key, r);
            }
            else
            {
                Debug.Assert(item.DefSaveMode != SM.Indirect);

                //Sets the item straight into the dictionary. No refcounting
                AddOrReplaceItem(key, item);
            }
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
                //IRef items may have a reference with them
                if (item is IRef)
                {
                    bool must_ref_count = false;

                    //ICRef items contains references, and they must potentially
                    //be replaced.
                    if (item is ICRef)
                    {
                        if (item is IKRef ikref)
                        {
                            //IKRef objects can be adopted. We prefer doing that.
                            must_ref_count = ikref.IsOwner(_tracker);
                            if (!must_ref_count && !ikref.Adopt(_tracker))
                                item = _tracker.MakeCopy(ikref, true);
                        }
                        else
                        {
                            //Note that items that implement IEnumRef are required
                            //to also implement ICRef, unless they are a PdfReference
                            if (item is IEnumRef)
                                must_ref_count = _tracker.OwnsEnumRef((IEnumRef)item);
                            //^ This call could be taken out if the MakeCopy function
                            //  was remade to notify when a copy is made. This because
                            //  MakeCopy also checks for ownership, and if owns it it
                            //  exits (since clone == false).

                            //We can't be sure if we own these objects, but we
                            //can be pretty sure that they are immutable. So
                            //we do a uncloned "copy". Change "false" to "true"
                            //if this assumption is wrong.
                            item = _tracker.MakeCopy((ICRef)item, false);
                        }
                    }

                    var iref = (IRef)item;
                    if (!iref.HasReference)
                    {
                        //Must be done before "RefCountItem" so that
                        //the correct ownership will be detected.
                        iref.Reference = Tracker.CreateWRef(item);

                        //To scenarios must be supported here.
                        // (1) An adopted object gets set indirect
                        // (2) An already owned object gets set indirect
                        //
                        // In the latter case we must refcount.
                        if (must_ref_count)
                        {
                            //Note, we know the item is owned by this tracker, so there's no
                            //need to update "var iref" (i.e. "item" will not be changed inside RefCountItem)
                            _tracker.RefCountItem(ref item);
                        }
                    }

                    SetIndirectItem(key, iref.Reference);
                }
                else if (item is PdfReference)
                    throw new PdfInternalException("Reference to reference");
                else if (item == null)
                    SetDirectItem(key, null);
                else
                {
                    //For when a direct object is later set indirect. 
                    if (item is IEnumRef && _tracker.OwnsEnumRef((IEnumRef)item))
                        _tracker.RefCountItem(ref item);

                    SetIndirectItem(key, Tracker.CreateWRef(item));
                }
            }
            else
            {
                Debug.Assert(item == null || item.DefSaveMode != SM.Indirect);
                if (item != null && !_tracker.OwnsItem(item))
                {
                    //The item may be adoptable, which we should always prefer.
                    if (item is IKRef ikref && ikref.IsOwner(null))
                    {
                        Debug.Assert(false, "This is not ideal, calling function should have set ref to true");
                        // ^ I can't do this, as it is possible the item has to be direct.
                        ikref.Adopt(_tracker);
                    }
                    else
                        item = _tracker.MakeCopy((ICRef)item, false);
                }
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
            if(!elem.HasReference)
                _tracker.Register(iref, elem.DefSaveMode);

            SetIndirectItem(key, iref.Reference);
        }//*/

        /// <summary>
        /// For setting items that are to be indirect
        /// </summary>
        /*public override void SetItemIndirect(string key, PdfItem item)
        {
            if (item is IRef)
            {
                var iref = (IRef)item;
                if (!iref.HasReference)
                {
                    _tracker.Register(iref, iref.DefSaveMode);

                    //The way things are now, all references
                    //are dealt with by the tracker. This means
                    //we leave it to the tracker to figure out
                    //what to do with any eventual child reference
                    //this formely direct object has.
                    iref.Reference.RefCount = 0; // <- removes the "newly created flag")
                }

                SetIndirectItem(key, iref.Reference);
            }
            else
            {
                var r = _tracker.CreateWRef(item);
                SetIndirectItem(key, r);
            }
        }*/

        /// <summary>
        /// Sets an elements as indirect
        /// </summary>
        /// <param name="key">Key to set the element at</param>
        /// <param name="item">Item to add</param>
        /// <param name="reference">If the item should be added as indirect</param>
        /*public override void SetItem(string key, Elements item, bool reference)
        {
            if (!reference)
            {//adds the item as direct

                //Objects with DefSaveMode indirect can't be added as direct,
                //but this should never happen
                Debug.Assert(item.DefSaveMode != SM.Indirect);

                if (!_tracker.Owns(item))
                    item = (Elements)_tracker.MakeCopy(item);

                SetDirectItem(key, item);
            }
            else
            {//adds the item as indirect
                IRef iref;
                if (!_tracker.Owns(item))
                {
                    iref = (IRef)_tracker.MakeCopy(item);
                    _tracker.Register(iref, item.DefSaveMode);
                }
                else
                    iref = (IRef)item;

                SetIndirectItem(key, iref.Reference);
            }
        }*/
        
        /// <summary>
        /// Sets a direct boolean
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="boolean">Value</param>
        public override void SetBool(string key, bool boolean)
        {
            AddOrReplaceItem(key, (boolean) ? PdfBool.True : PdfBool.False);
        }

        /// <summary>
        /// Sets a direct int
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="num">Value</param>
        public override void SetInt(string key, int num)
        {
            AddOrReplaceItem(key, new PdfInt(num));
        }

        /// <summary>
        /// Sets a direct float
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="num">Value</param>
        public override void SetReal(string key, double num)
        {
            AddOrReplaceItem(key, new PdfReal(num));
        }

        /// <summary>
        /// This method adds an item without refcounting it.
        /// It does properly remove items being overwritten.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="item"></param>
        private void AddOrReplaceItem(string key, PdfItem item)
        {
            lock (_catalog)
            {
                if (item == null)
                {
                    //"null" removes the key. This is
                    //intended, but for testing it's nice to
                    //catch unintended happenstances of this.
                    //Debug.Assert(false, "Item set to null");
                    Remove(key);
                }
                else
                {
                    PdfItem val;
                    if (_catalog.TryGetValue(key, out val))
                    {
                        //Decrement the old reference
                        _tracker.DecRefCount(val);
                        _catalog[key] = item;
                    }
                    else
                    {
                        _catalog.Add(key, item);
                    }
                }
            }
        }

        #endregion

        #region Required overrides

        /// <summary>
        /// Creates a reverse lookup dictionary
        /// </summary>
        public override Dictionary<PdfItem, string> GetReverseDict()
        {
            lock (_catalog)
            {
                var r = new Dictionary<PdfItem, string>(_catalog.Count);
                foreach (var kp in _catalog)
                    r.Add(kp.Value.Deref(), kp.Key);
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
        /// <returns>A cloned WritableDictionary</returns>
        public override PdfItem Clone()
        {
            return new WritableDictionary(_catalog.Clone(), _tracker);
        }

        /// <summary>
        /// Removes an item from the dictionary
        /// </summary>
        public override void Remove(string key)
        {
            lock (_catalog)
            {
                PdfItem val;
                if (_catalog.TryGetValue(key, out val))
                    _tracker.DecRefCount(val);

                _catalog.Remove(key);
            }
        }

        #endregion

        #region Notification

        /// <summary>
        /// Reigsters an object for notification
        /// </summary>
        /// <param name="elm">Element to register</param>
        internal override void Notify(INotify elm, bool register)
        {
            _tracker.Notify(elm, register);
        }

        #endregion
    }
}
