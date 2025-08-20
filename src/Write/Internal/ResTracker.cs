#region Imports and defines
//Automatically turn on the object cache when there seems to be a need.
#define AUTO_CACHE
//When retargeting, remove problematic references. 
// (Does not work, see comment in RetargetLegal)
//#define PRUNE_INVALID
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Text;
using PdfLib.Write;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Read;
using PdfLib.Util;
#endregion

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Tracks resources used in a writable document
    /// </summary>
    /// <remarks>
    /// Todo: Have the tracker add new resourcees to a list, 
    /// so that it works better with StreamDocs
    /// </remarks>
    public class ResTracker
    {
        #region Variables and properties

        /// <summary>
        /// The highest allowed generation.
        /// </summary>
        /// <remarks>
        /// This is only relevant when saving XRefStreams
        /// 
        /// Implementation limits prevents you from setting this
        /// above 65535. Note that the normal XRef table is
        /// limited to 65535 generation numbers too, but that's by
        /// the specs on not my implementation. 
        /// 
        /// Also note that this limits how many objects to place
        /// in an object stream. 255 is probably a decent limit
        /// here, and since PdfLib ignores generation numbers this
        /// has little impact on that.
        /// 
        /// Finally, generation is now capped to 0, so this constant
        ///          no longer got any impact outside how many objects
        ///          a XRefStream can contain. 
        /// </remarks>
        const int MAX_GEN_NR = 255;

        /// <summary>
        /// Set when there's a potential issue with the
        /// refcount. Then one must run "FixResTable"
        /// before saving the document. 
        /// </summary>
        bool _must_fix_restable;

        /// <summary>
        /// The restracker needs a owner, but only
        /// when saving
        /// </summary>
        IDoc _owner;

        /// <summary>
        /// Dictionary over the documents resources. 
        /// </summary>
        /// <remarks>
        /// Note, the dictionary does not contain deleted objects.
        ///       
        ///       Currently there's no harm in removing deleted
        ///       references, this since a WritableReference
        ///       contain all the data it needs. 
        /// </remarks>
        readonly Dictionary<PdfObjID, WritableReference> _resources;

        /// <summary>
        /// A cache over foregin object references. 
        /// </summary>
        /// <remarks>
        /// It can be tempting to cache references instead or in addition to
        /// the objects themselves. While a good idea, on the surface, 
        /// such a cache must keep in account:
        ///  - There are no rules against multitple references pointing at the
        ///    same object.
        ///  - There is no rule against changing what object the reference points at.
        ///  - If there's a need to change the type of reference, the reference
        ///    will be thrown away in favor of a new one.
        /// Which basically means objects has to be cached regardless.
        /// 
        /// There's no point in caching IRef. All classes that implements IRef also
        /// implements ICRef*, while the reverse isn't true.
        ///  * Except the WritableObjStream, which under no circumstance is to be cached.
        /// </remarks>
        Dictionary<ICRef, CacheObj>[] _cache;

        /// <summary>
        /// The next id to try.
        /// </summary>
        /// <remarks>An objects id need only be unique, but an attempt
        /// should be made to keep them continious.
        /// 
        /// I.e. when objects are deleted then this id should be set 
        /// down to the delete's objects id.
        /// 
        /// Though there's currently no good way of telling if a 
        /// reference has truly been deleted. 
        ///   Perhaps use a weakcache?
        ///   
        /// In any case, ids will be renumbered before saving (as
        /// needed) so it's not of any real importance beyond 
        /// keeping references in the file and debugger the same.
        /// </remarks>
        int _next_id = 1;

        /// <summary>
        /// Used to give created objects a spesific id.
        /// Note that ID's beyond one million is not supported.
        /// </summary>
        /// <remarks>Currently only used to give the root
        /// catalog a high id, since I want that object to
        /// come last when writing.
        /// </remarks>
        internal int NextId { get { return _next_id; } set { _next_id = value; } }

        /// <summary>
        /// If the "FixResTable" function must be called.
        /// </summary>
        public bool IsDirty { get { return _must_fix_restable; } }

        /// <summary>
        /// If the document is to cache foreign objects so that
        /// it can recognize them if they're added to the document
        /// multiple times, and avoid making a copy then.
        /// </summary>
        public bool CacheObjects
        {
            get { return _cache != null; }
            set 
            {
                if (value)
                {
                    if (_cache == null)
                        _cache = new Dictionary<ICRef, CacheObj>[(int)PdfType.EOF];
                }
                else
                {
                    _cache = null;
                }
            }
        } 

        /// <summary>
        /// Owner document. Used for chekcing savemode and by ReWritableReferences
        /// looking for unparsed data.
        /// </summary>
        internal IDoc Doc
        {
            set
            {
                if (_owner != null)
                    throw new PdfInternalException("Tracker has owner");
                _owner = value;
            }
            get { return _owner; }
        }

        /// <summary>
        /// Whenever this is a dummy tracker or not.
        /// </summary>
        /// <remarks>
        /// Dummy tracker is used when there's a need to use code that requires a tracker, by an
        /// object that lacks a tracker
        /// </remarks>
        //internal bool IsDummy { get { return Doc == null; } }

        /// <summary>
        /// Provides a list of all stream resources in the document
        /// </summary>
        internal ICStream[] Streams
        {
            get
            {
                var list = new List<ICStream>(_resources.Count);
                foreach (var kp in _resources)
                {
                    var dref = kp.Value.Deref();
                    if (dref is ICStream)
                    {
                        ((IKRef)dref).Reference = kp.Value;
                        list.Add((ICStream)dref);
                    }
                }
                return list.ToArray();
            }
        }

        #endregion

        #region Init

        public ResTracker(IDoc owner)
            : this()
        { Doc = owner; }

        public ResTracker() 
        { 
            _resources = new Dictionary<PdfObjID, WritableReference>();
        }

        #endregion

        #region Make Copy of ICRef

        /// <summary>
        /// Checks if an object is in the cache
        /// </summary>
        /// <param name="obj">Object to look for, updated with cached object if any</param>
        /// <returns>If there was a cached object</returns>
        internal bool HasObject(ref ICRef obj)
        {
            if (_cache != null)
            {
                var cache = _cache[(int) ((PdfItem)obj).Type];

                if (cache != null)
                {
                    CacheObj itm;
                    if (cache.TryGetValue(obj, out itm))
                    {
                        obj = (ICRef) itm.Item;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if an object is in the cache
        /// </summary>
        /// <param name="obj">Object to look for, updated with cached object if any</param>
        /// <returns>If there was a cached object</returns>
        private bool HasObject(ICRef obj, out CacheObj cobj)
        {
            if (_cache != null)
            {
                var cache = _cache[(int)((PdfItem)obj).Type];

                if (cache != null)
                {
                    CacheObj itm;
                    if (cache.TryGetValue(obj, out itm))
                    {
                        cobj = itm;
                        return true;
                    }
                }
            }
            cobj = new CacheObj();
            return false;
        }

        /// <summary>
        /// Removes a object from the cache
        /// </summary>
        /// <param name="key">Key to objct to remove</param>
        private void RemoveObject(ICRef key)
        {
            if (_cache != null)
            {
                var cache = _cache[(int)((PdfItem)key).Type];

                if (cache != null)
                    cache.Remove(key);
            }
        }

        /// <summary>
        /// Adds an object to the cache
        /// </summary>
        /// <param name="obj">Key</param>
        /// <param name="my_obj">Object to cache</param>
        /// <returns>Object to cache</returns>
        private PdfItem AddObject(ICRef obj, PdfItem my_obj)
        {
            if (_cache != null)
            {
                //Fetches the cache for this object
                if (my_obj == null) return null;
                var i = (int)my_obj.Type;
                var cache = _cache[i];
                if (cache == null)
                {
                    cache = new Dictionary<ICRef, CacheObj>(10);
                    _cache[i] = cache;
                }
                cache.Add(obj, new CacheObj(my_obj));
            }
            return my_obj;
        }

        /// <summary>
        /// This function is for updating the reference in the foreign object cache
        /// </summary>
        /// <param name="key">Key to the object</param>
        /// <param name="reference">Reference one wish to set</param>
        /// <remarks>
        /// The basic problem is this (During a "MakeCopy"):
        ///  - When a object is encountered with a reference pointing at it, this
        ///    reference needs to be replaced.
        ///  - This is done by creating a reference, but..
        ///  - The object itself also needs to be copied, so we find ourself with
        ///    a situation where we have a reference to an object that does not
        ///    yet exist.
        ///  - MakeCopy does some book keeping so that it can update the reference to
        ///    the non-IRef object later (when it is done copying the object).
        ///  - For IRef objects the cached reference will then be updated automatically
        ///  - For non-IRef objects we add it to the foreign cache as normal, but at
        ///    that point in the "MakeCopy rutine" we don't have the "TempRef" yet
        ///  - Then later when we have the TempRef we don't have a ref to the original
        ///    object
        ///  - To solve this a temp_ref -> org_ref entery was added to the ref_dictionary
        ///    and then when we have the temp_ref, we make a lookup in the ref_dict,
        ///    get the org_ref, deref that and get the original item.
        ///  - Then we can update the CacheObj's reference with this function.
        ///  
        ///  - An alternate solution would be to rewrite the MakeCopy rutine so that
        ///    it holds on to the original object's reference for longer. That would
        ///    be more elegant, but I don't want to modify and debug the rutine just
        ///    yet... so I've made thid non-intrusive change instead. 
        /// </remarks>
        private void SetCachedReference(PdfItem key, PdfReference reference)
        {
            if (_cache != null)
            {
                //This will only execute for objects that does not implement IRef, but does
                //implement ICRef. Both sealed array and dictionary fit this criteria,
                //but they are copied into writable arrays and dictionaries, and those
                //implement IRef (This means an entery is added needlesly for those
                //objects as it will never be used).
                //
                //One object that does fit this criteria is RealArray.
                //Todo: Make a test with RealArray
                //Debug.Assert(false, "Untested code");
                _cache[(int)key.Type][(ICRef)key] = new CacheObj(reference.Deref(), (WritableReference)reference);
            }
        }

        /// <summary>
        /// Adopt an object. This function is intended for implementors
        /// of IKRef.Adopt, so don't call it directly instead use the 
        /// IKRef method.
        /// </summary>
        /// <param name="obj">Object to adopt</param>
        /// <returns>Adopted object</returns>
        /// <remarks>
        /// create_md_func_0 gives this a workout, though
        /// one scenario is not tested this way. For that
        /// use QuickScan to create a bookmark. This bookmark
        /// will cause Adopt(PdfItem child) to find an object
        /// already in the _resources and use its reference
        /// instead (when the document is saved as PDF).
        /// 
        /// This function is very similar to MakeCopy in purpose,
        /// but very dissimilar in implementation. Both tackle the issue
        /// that there may be valid and invalid circular references in
        /// the data structure. the former because it have to and the
        /// latter to avoid eternal loops or stack overflow.
        /// 
        /// This implementation is poorly suited to detecting invalid
        /// circular references (Makecopy can simply walk the stack
        /// to discover this), however at worst there will be a stack 
        /// overflow so it's not a "real" proplem.
        /// 
        /// The key to this implementation is that all ICRef type
        /// objects are put on a list for later checking, and all
        /// that checking is done here and never further down the
        /// stack. 
        /// </remarks>
        internal PdfItem Adopt(ICRef obj)
        {
            //Using a different technique than Makecopy to avoid
            //circular references
            var adopt_later = new List<AdoptionItem>();

            //This function will fill out the adopt later list
            //with anything that also need adoption. 
            var ret = Adopt(obj, adopt_later);

            //Then we go through the list and adopt those items.
            //This way we avoid cirulation, in cases were an item
            //has a reference to it's parent. 
            if (adopt_later.Count > 0)
            {
                var cache = new Dictionary<PdfItem, PdfItem>(adopt_later.Count);

                for (int c = 0; c < adopt_later.Count; c++)
                {
                    //Since this is a list, it does not matter if the
                    //adopt rutine adds additional enteries to this list
                    //as we are iterating it

                    //First we fetch an item
                    var aitem = adopt_later[c];
                    PdfItem item;

                    if (aitem is AdoptReference)
                    {
                        //We check if it's already adopted
                        if (!cache.TryGetValue(aitem.Item, out item))
                        {
                            //We can at least assume this child is an icref
                            var icref = (ICRef)aitem.Item.Deref();

                            //Adopts
                            if (icref is IKRef)
                            {
                                if (((IKRef)icref).Adopt(this, adopt_later))
                                    item = (PdfItem) icref;
                                else
                                    item = MakeCopy(icref);
                            }
                            else
                                item = Adopt(icref, adopt_later);

                            //Switces the pointer
                            aitem.Set(item);

                            //Registers this reference as adopted
                            cache.Add(aitem.Item, aitem.Item);
                        }
                    }
                    else
                    {
                        //We check if it's already adopted
                        if (!cache.TryGetValue(aitem.Item, out item))
                        {
                            ICRef icref;

                            //We can at least assume this child is an icref
                            icref = (ICRef)aitem.Item;

                            //Adopts
                            if (icref is IKRef)
                            {
                                if (((IKRef)icref).Adopt(this, adopt_later))
                                    item = (PdfItem)icref;
                                else
                                    item = MakeCopy(icref);
                            }
                            else
                                item = Adopt(icref, adopt_later);

                            cache.Add(aitem.Item, item);
                        }

                        //This method knows where to update the new item
                        aitem.Set(item);
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// Adopts an item
        /// </summary>
        /// <param name="obj">The item to adopt</param>
        /// <param name="state">A stateobject needed for adoption</param>
        /// <returns>The adopted item</returns>
        /// <remarks>
        /// This function is here since AdoptionItem is a private class,
        /// and this is a internal function
        /// </remarks>
        internal PdfItem Adopt(ICRef obj, object state)
        {
            return Adopt(obj, (List<AdoptionItem>)state);
        }

        /// <summary>
        /// Adops a object's children and makes a copy of the obj. 
        /// </summary>
        private PdfItem Adopt(ICRef obj, List<AdoptionItem> adopt_later)
        {
            //ICref objects, that aren't also IKRef objects, can't
            //be propperly adopted, but its children may be adoptable.
            //To avoid adopting children multiple times we do a looksee
            //in the cache.
            if (HasObject(ref obj))
                return (PdfItem)obj;

            var children = obj.GetChildren();
            if (children is Catalog)
                children = Adopt((Catalog)children, adopt_later);
            else
                children = Adopt((PdfItem[])children, adopt_later);

            //We must add the item to the cache so that it can later
            //be regonized.
            return (PdfItem) AddObject(obj, (PdfItem)obj.MakeCopy(children, this));
        }

        /// <summary>
        /// Adopts all children in a catalog
        /// </summary>
        private Catalog Adopt(Catalog children, List<AdoptionItem> adopt_later)
        {
            var cat = new Catalog(children.Count);
            foreach (KeyValuePair<string, PdfItem> child in children)
            {
                var val = Adopt(child.Value);
                if (val == null)
                    adopt_later.Add(new DictItem(child.Value, child.Key, cat));
                else if (val is AdoptReference)
                {
                    var ai = (AdoptReference)val;
                    cat.Add(child.Key, ai.Item);
                    adopt_later.Add(ai);
                }
                else
                    cat.Add(child.Key, val);
            }
            return cat;
        }

        /// <summary>
        /// Adopts all children in an array
        /// </summary>
        private PdfItem[] Adopt(PdfItem[] children, List<AdoptionItem> adopt_later)
        {
            //While it might look like it, this is not a recursive function nor does it check child arrays,
            //so we can lock for the entire opperation.
            lock (children)
            {
                for (int c = 0; c < children.Length; c++)
                {
                    //Prevents potential deadlock.
                    var child = children[c];

                    var val = Adopt(child);

                    if (val == null)
                        adopt_later.Add(new ArrayItem(children[c], c, children));
                    else if (val is AdoptReference)
                    {
                        var ai = (AdoptReference)val;
                        children[c] = ai.Item;
                        adopt_later.Add(ai);
                    }
                    else
                        children[c] = val;
                }
                return children;
            }
        }

        /// <summary>
        /// Stores away data for the adopt later rutine. The
        /// adoption itself isn't done here.
        /// </summary>
        private PdfItem Adopt(PdfItem child)
        {
            //To avoid circular references we avoid following them until it's safe
            if (child is ICRef)
            {
                ////This is a directly set object, and arguably direct ICRef items 
                ////should always be copied. (I.e. don't bother with caching)
                ////
                ////It's also highly unlikely for a direct ICRef obect to be in the
                ////cache unless one deliberatly make it happen using the low level
                ////API. 
                //ICRef pointless = (ICRef) child;
                //if (HasObject(ref pointless))
                //    return (PdfItem) pointless;

                //By returning null we tell the caller to check this object later
                return null;
            }

            if (child is PdfReference)
            {
                if (child is TempReference)
                {
                    var tr = (TempReference) child;
                    if (tr.RefCount)
                    {
                        // Hmm. This search can be slow on PDF files with very large
                        // numer of resources. This can actually dominate execution
                        // time.
                        //
                        // So we optimize for the common case. That is to say, we
                        // examine IRef to see if we can skip the search.
                        
                        //Searches for the reference and if found use that instead.
                        var item = tr.Deref();

                        if (item is IRef iref)
                        {
                            //This is the most important optimization here.
                            //
                            //This assumtion works because:
                            // - The Reference field on IRef is internal, so it's not manipulated
                            //   outside the libary.
                            // - There's only WritableReferences or subclasses of that in the
                            //   _references. So no chance of a SealedReference or anything of
                            //   that sort.
                            // - WritableReference adds itself to the IRef.Reference if the spot
                            //   is open.
                            // - This means code has to deliberatly set IRef.Reference to null,
                            //   which can happen but, TempWRef always make sure to remove itself
                            //   from IRef.Reference and The MakeCopy that nulls Reference is
                            //   spesificaly crafted for Adoption.
                            if (!iref.HasReference)
                                goto skip_search;

                            //This happens if you add resources from a document to a non-owned item,
                            //then add that to the document. Say, you open a docment, create a new
                            //PdfForm, add an image from the document to the form, then add the form
                            //to the document.
                            if (this.Owns(iref) && _resources.ContainsKey(iref.Reference.Id))
                            {
                                //Double checks just in case this is an old leftover reference
                                var wref = _resources[iref.Reference.Id];
                                if (ReferenceEquals(wref.Deref(), iref))
                                {
                                    //Using IncRefCount as it's not inconceivable that we've found
                                    //an unpruned reference with a RefCount of zero
                                    IncRefCount(wref);
                                    return wref;
                                }
                            }
                        }


                        foreach (var kv in _resources)
                            if (kv.Value.HasValue && ReferenceEquals(kv.Value.Deref(), item))
                            {
                                //Using IncRefCount as it's not inconceivable that we've found
                                //an unpruned reference with a RefCount of zero
                                IncRefCount(kv.Value);
                                return kv.Value;
                            }
                    }
                }
                else if (child is WritableReference)
                {
                    var awr = (WritableReference)child;
                    if (Owns(awr))
                        return child;
#if AUTO_CACHE
                    if (_cache == null && awr.Tracker != null)
                    {
                        //This writable ref is owned by another document. Setting the cache
                        //to avoid data duplication.
                        CacheObjects = true;
                    }
#endif
                }
#if AUTO_CACHE
                else if(_cache == null)
                {
                    //This is a foreign or new kind of reference. We typically want them
                    //cached, so turning the cache on.
                    CacheObjects = true;
                }
#endif
            skip_search:
                var target_child = child.Deref();

                //Checks if the target has been adopted already. If so, return the target's
                //reference.
                if (target_child is ICRef)
                {
                    CacheObj tc;
                    if (HasObject((ICRef)target_child, out tc))
                    {
                        var our_ref = tc.Reference;
                        Debug.Assert(our_ref != null, "Could be a bug"); //See comment below.
                        if (our_ref != null)
                            return our_ref;

                        //This can happen if an temporary object is set as a direct object, which is probably a bug*. Now,
                        //the cache is optional, so we delte the problematic cache entery and continue as if it wasn't cached.
                        // * There are some objects that must be set direct. 
                        var cache = _cache[(int)(target_child).Type];
                        cache.Remove((ICRef)target_child);
                    }
                }


                //We make this into a writable reference
                var wr = CreateTWRef(target_child);
                wr.RefCount = 1;

                //Any target that contains references need closer excamination
                if (target_child is ICRef)
                    return new AdoptReference(wr);
                else
                    return wr;
            }

            //No work needed to be done.
            Debug.Assert(!(child is IRef));
            return child;
        }

        /// <summary>
        /// This make copy rutine allows for excluding fields that
        /// one do not wish to have copied.
        /// </summary>
        /// <param name="obj">Object to copy</param>
        /// <param name="exclude">Fields to exclude</param>
        /// <returns>
        /// New object without the excluded fields.
        /// (Unless the item was cached)
        /// </returns>
        internal PdfItem MakeCopy(ICRef obj, object[] exclude)
        {
            if (obj is IKRef)
            {
                if (((IKRef)obj).Adopt(this))
                    return (PdfItem) obj;
            }

            if (HasObject(ref obj))
                return (PdfItem)obj;

            #region Local variables

            //Having looked at quite a few PDF files I've concluded that
            //using a stack for circular checks isn't that dumb an idea.
            //Deep recursion will be a rareity.
            var check_stack = new Stack<ICRef>(10);

            //The data stack contain the information needed to go back 
            //to the previous point of execution.
            var data_stack = new Stack<ResumeFrom>(10);

            #endregion

            var children = obj.GetChildren();
            if (children is PdfItem[])
            {
                PdfItem[] ar;
                lock (children) { ar = (PdfItem[])((PdfItem[])children).Clone(); }
                for (int c = 0; c < exclude.Length; c++)
                    ar[(int)exclude[c]] = PdfNull.Value;

                return (PdfItem)MakeCopy(obj, 0, ar, check_stack, data_stack);
            }
            else
            {
                Catalog dict;
                lock (children) { dict = ((Catalog)children).Clone(); }
                for (int c = 0; c < exclude.Length; c++)
                    dict.Remove((string)exclude[c]);

                return (PdfItem)MakeCopy(obj, 0, dict, check_stack, data_stack);
            }
        }

        /// <summary>Make a copy of a object that can be tracked by this tracker</summary>
        /// <param name="obj">Item to make a copy of</param>
        /// <param name="clone">In some cases one can forgo a copy if the item is immutable, use true if unsure</param>
        /// <remarks>
        /// In PDF files direct objects can not result in circular
        /// recursion. Unfortunatly this isn't the case with 
        /// PdfLib. I'm not entierly sure how, or if there's a need,
        /// to detect such circular referneces. Added some code to 
        /// this function towards that end though.
        /// 
        /// This function is reentrant. Though, since I ended up not
        /// using C# recursion there's no real need for that reentrancy.
        /// (There's still recursion, just done manualy as I have a need
        ///  to walk the stack to detect circular references.)
        ///  
        /// This algorithm looks for objects that can be problematic, but
        /// does no copying. If a problem is found, the other MakeCopy
        /// is called.
        /// 
        /// Future improvments
        ///  - Cache direct items. I.e. if a data structure has the same
        ///    direct item set in multiple locations, that item will be
        ///    cloned over and over again. However this is probably a
        ///    rare case and only incurs some more memory use.
        ///  
        ///  - Note, this comment isn't truly relevant as a cache feature has now been added: 
        ///  - Currently all foreign items are cloned. Consider scenarios such as this:
        ///     image1.colorspace = a_color_space (from another document)
        ///     image2.colorspace = a_color_space
        ///       (Makecopy runs each time image.colorspace is set)
        ///     image1.colorspace.somevalue = new_value
        ///   
        ///    in this case image2's colorspace will not have changed in tune with image1,
        ///    as the images have two sepperate colorspace objects.
        ///     
        ///    This is particularly an issue when adding multiple pages from other 
        ///    documents, who can be sharing quite a lot of resources among themselves.
        ///     (Which leads to bloated file sizes)
        ///  
        ///    A cache of foreign objects could be used to recognize a object that has
        ///    already been made a copy of.
        ///  
        ///    The "ref_dict" (in the other MakeCopy func) is in essence already such
        ///    a cache, only that it's limited to the object currently being copied.
        ///  
        ///    To extend that one have to recognize what document an object being
        ///    copied stems from. One can do that if one find a writable reference, and
        ///    perhaps by adding a new method to IKRef for "Owner". Then retrieve the
        ///    "ref_dict" one have cached for that document. (This since refs with the same
        ///    id spits out the same hash so there will be lots of collision.)
        ///     
        ///    The above solution will solve the issues of adding multiple pages, but not
        ///    so much for the first example unless that a_color_space has a reference.
        ///    So perhaps add a cache for IKRef objects with no reference set too.
        ///    
        ///    On multithreading:
        ///    Keep in mind that while this function is sort-of MT safe. The datastucture
        ///    it's reading from isn't (yet) MT safe. Mind, I don't intend to support MT 
        ///    safe writable documents so this isn't a critical feature.
        ///    
        ///    In summary
        ///      - Make a cache over ref_dict caches tied to foreign trackers
        ///      - In this function retrive the correct cache before calling the
        ///        impl. MakeCopy method by getting the tracker either from the IKRef 
        ///        interface or a Writable Reference (which are the two situations that
        ///        triggers an actual copy being made)
        ///      - There's no real need for a IKRef cache, as a IKRef object without a
        ///        reference (who can't be adopted) is likely a direct object so making
        ///        a copy just increase memory use, not file size on the disk.
        /// </remarks>
        internal PdfItem MakeCopy(ICRef obj, bool clone)
        {
            //First we check if there's a copy of this object already cached.
            if (HasObject(ref obj))
            {
                //One potential problem here are PdfPage objects. If a user adds the same
                //page to the document multiple times, it will seemeingly work but Adobe
                //will not be able to open the document. However the check must be done in
                //the "add page" functions of WritableDocument, as other objects may hold
                //page references.
                return (PdfItem)obj;

                //There's no need to do any refcount adjustmets here. That will be handeled
                //by the caller.
            }

            #region Local variables

            //Having looked at quite a few PDF files I've concluded that
            //using a stack for circular checks isn't that dumb an idea.
            //Deep recursion will be a rareity.
            var check_stack = new Stack<ICRef>(10);

            //The data stack contain the information needed to go back 
            //to the previous point of execution.
            var data_stack = new Stack<ResumeFrom>(10);

            //When entering copy mode the enumeration needs to be restarted.
            //The c stack simply holds the count of how many MoveNext calls
            //have been made (and works on the assumption that an unmodified
            //dictionary will have the same enumeration order)
            var c_stack = new Stack<int>(0);

            //Keeping a ref to the original object lets the function
            //return directly when a writable ref owned by this tracker
            //is encountered.
            var orginal_obj = obj;

            //Item array being checked and the position in that array.
            PdfItem[] ar; int c;

            //The dictionary being checked
            IEnumerator<KeyValuePair<string, PdfItem>> enumer = null;
            Catalog cat = null;

            #endregion

            #region Quickly check ownership

            if (obj is IRef)
            {
                //If this tracker does not own the object, copy it. If it does, return it
                var iref = (IRef)obj;
                if (iref.HasReference)
                {
                    if (iref.Reference.Tracker == this)
                        return (PdfItem)orginal_obj; //<-- This could be done before creating all those stacks (in region Local variables)
                    else
                        //Subtlety - param 3 will be cloned, so no need to do it here.
                        return (PdfItem)MakeCopy(obj, 0, obj.GetChildren() as PdfItem[], check_stack, data_stack);
                }

                //All IKref type objects must be copied
                if (obj is IKRef)
                {
                    if (((IKRef) obj).IsOwner(this))
                        return (PdfItem)orginal_obj; //<-- This could be done before creating all those stacks (in region Local variables)
                    else
                        //Subtlety - param 3 will be cloned, so no need to do it here.
                        return (PdfItem)MakeCopy(obj, 0, obj.GetChildren() as PdfItem[], check_stack, data_stack);
                }
            }

            #endregion

            #region Restart point

        restart:
            var children = obj.GetChildren();
            ar = children as PdfItem[];
            c = 0;
            if (clone && ar != null) ar = (PdfItem[])ar.Clone();

            #endregion

        resume:
            if (ar != null)
            {
                Monitor.Enter(ar);
                bool has_lock = true;

                try
                {
                    for (; c < ar.Length; c++)
                    {
                        #region Fetch item

                        PdfItem itm = ar[c];

                        #endregion

                        #region References must be taken ownership of

                        if (itm is PdfReference)
                        {
                            //If the array contain one correctly owned ref, one can assume all
                            //refs are okay.
                            if (itm is WritableReference && ((WritableReference)itm).Tracker == this)
                                return (PdfItem)orginal_obj;

                            //Must make copy: enter copy mode.
                            Monitor.Exit(ar);
                            has_lock = false;
                            return (PdfItem)MakeCopy(obj, c, ar, check_stack, data_stack);
                        }

                        #endregion

                        #region Mutable objects must always be copied

                        if (itm is IKRef)
                        {
                            //If the array contain one correctly owned ref, one can assume all
                            //refs are okay.
                            if (((IKRef)itm).IsOwner(this))
                                return (PdfItem)orginal_obj;

                            //Must make copy: enter copy mode.
                            Monitor.Exit(ar);
                            has_lock = false;
                            return (PdfItem)MakeCopy(obj, c, ar, check_stack, data_stack);
                        }

                        #endregion

                        #region Direct objects must be checked for references

                        if (itm is ICRef icref)
                        {
                            //Check if the icref is already in the stack.
                            if (check_stack.Contains(icref))
                            {
                                //We have a circular reference. This can only happen
                                //by user intent, and would in any case make the object
                                //imposible to print as it would be infinitly large.
                                throw new PdfInternalException("Circular reference in data");
                            }

                            //Pushes the current item onto the check stack.
                            check_stack.Push(obj);

                            //Pushes state information needed to resume from
                            //this point onto the stack.
                            data_stack.Push(new ResumeFrom(ar, c, null));

                            //Switches state information.
                            obj = icref;

                            //And checks the object.
                            Monitor.Exit(ar);
                            has_lock = false;
                            goto restart;
                        }

                        #region Sanity check

                        //One could also silently replace null with PdfNull.
                        if (itm == null)
                            throw new PdfInternalException("Null pointer is not allowed in data. Use PdfNull");

                        #endregion

                        #endregion
                    }
                }
                finally { if (has_lock) Monitor.Exit(ar); }
            }
            else
            {
                //When childen is set, it's a restart.
                if (children != null)
                {
                    cat = (Catalog)children;
                    enumer = cat.GetEnumerator();
                    c = 0;
                }

                //Implementation note
                //c is used to track the position in the enumer. When one enter copymode
                //one can then quickly get back to the current position (as the copy code
                //creates a new enumer and then counts up to c)
                //
                //On concurrency. Ideally we should lock the catalog while working on it,
                //but the only sideeffect is that we might get an object that is PdfDictionary
                //instead of the final object. Since we'll be making a copy anyway, it does
                //not matter. 
                for (; enumer.MoveNext(); c++)
                {
                    var kp = enumer.Current;
                    PdfItem itm = kp.Value;

                    #region References must be taken ownership of

                    if (itm is PdfReference)
                    {
                        //If the array contain one correctly owned ref, one can assume all
                        //refs are okay.
                        if (itm is WritableReference && ((WritableReference)itm).Tracker == this)
                            return (PdfItem)orginal_obj;

                        //Must make copy: enter copy mode.
                        return (PdfItem)MakeCopy(obj, c, null, check_stack, data_stack);
                    }

                    #endregion

                    #region Direct objects must be checked for references

                    if (itm is ICRef)
                    {
                        var icref = (ICRef)itm;

                        //Check if the icref is already in the stack.
                        if (check_stack.Contains(icref))
                        {
                            //We have a circular reference. This can only happen
                            //by user intent, and would in any case make the object
                            //imposible to print as it would be infinitly large.
                            throw new PdfInternalException("Circular reference in data");
                        }

                        //Pushes the current item onto the check stack.
                        check_stack.Push(obj);

                        //Pushes state information needed to resume from
                        //this point onto the stack.
                        data_stack.Push(new ResumeFrom(cat, enumer, null));
                        c_stack.Push(c);

                        //Switches state information.
                        obj = icref;

                        //And checks the object.
                        goto restart;
                    }

                    #region Sanity check

                    //One could also silently replace null with PdfNull.
                    if (itm == null)
                        throw new PdfInternalException("Null pointer is not allowed in data. Use PdfNull");

                    #endregion

                    #endregion
                }
            }

            //Moves back to parent object.
            //Finished checking the parent item and made a copy of the parent. 
            //Now unwind the stack, set the grand parent field and restart.
            if (data_stack.Count > 0)
            {
                //ResumeFrom is 1-1 with the check stack.
                ResumeFrom res_from = data_stack.Pop();

                #region Resuming

                //The parent object of this object.
                obj = check_stack.Pop();

                if (res_from.From is int)
                {
                    ar = (PdfItem[])res_from.Data;
                    c = (int)res_from.From + 1;
                    //Add +1 to c as it keeps track of where we are in the array, normally
                    //the for loop would have done a +1 but since we broke out of it we do 
                    //it here
                }
                else
                {
                    cat = (Catalog)res_from.Data;
                    //Setting children = null tells "restart", ar = null tells "not array"
                    c = c_stack.Pop() + 1; ar = null; children = null;
                    //Add +1 to c as it keeps track of how many items we've enumerated, normally
                    //the for loop would have done a +1 but since we broke out of it we do it here
                    enumer = (IEnumerator<KeyValuePair<string, PdfItem>>)res_from.From;
                }

                goto resume;
                

                #endregion
            }

            //No changes were needed, however a copy must be
            //made in case the user wants to make changes.
            //
            //Note that at this point we know that:
            // 1. The object has no references
            // 2. That it is immutable, unless the "clone"
            //    flag is set.
            //
            //In any case there's no real need to add these
            //to the object cache, as it will not result in
            //any saving of filesize when the PDF is saved.
            //
            //Even so we at least add the objects we make a
            //copy off to the cache. 
            if (!clone) return (PdfItem) obj;
            if (ar != null)
            {
                object aclone;
                lock (ar) aclone = ar.Clone();
                return AddObject(obj, (PdfItem)obj.MakeCopy(aclone, this));
            }
            else
            {
                object aclone;
                lock (cat) aclone = cat.Clone();
                return AddObject(obj, (PdfItem)obj.MakeCopy(aclone, this));
            }
        }

        /// <summary>
        /// Makes a copy of a object.
        /// </summary>
        /// <remarks>
        /// Unlike the MakeCopy above this MakeCopy does not detect
        /// circular direct references. The reason is because there
        /// may be references between the circular direct objects,
        /// which is allowed.
        /// 
        /// Since the code above will exit or enter copy mode if a
        /// ref is enountered it knows there's no PdfReferences 
        /// objects breaking up the chain when encountering a 
        /// circular reference, and can therefore throw.
        /// 
        /// This means in essence that this method will allow objects
        /// with direct circular reference to be copied. You will off course
        /// notice that when then saving the document as it will be infinitly
        /// large. Note that there's a couple of "Todo" comments for where 
        /// to add code to prevent these endless objects from being copied 
        /// (i.e. walk the stack for references).
        /// 
        /// Termiology:
        ///  -Restart: For checking a child object
        ///  -Resume: After having checked a child object, checking
        ///           is resumed to where it left off on the parent.
        ///  -Placeholder: Placeholders are set for circular references.
        ///   The nice thing about circular references is that (when found)
        ///   the offending object is already being checked futher up the
        ///   stack. Since it's being checked, no checking is needed. A 
        ///   placeholder is set instead and then the final result is 
        ///   fetched later.
        ///   
        /// Hinsight:
        ///  -Should have used an emumeration for itterating arrays, as
        ///   then I could have used the same code for Dictionaries
        ///   and arrays.
        /// </remarks>
        private ICRef MakeCopy(ICRef parent, int pos, object obj_data,
            Stack<ICRef> check_stack, Stack<ResumeFrom> data_stack)
        {
            #region Prep work

#if AUTO_CACHE
            //There's a good chance caching is desired, so to save the user
            //the bother we turn it on.
            if (_cache == null)
                CacheObjects = true;
#endif

            //This dictionary is used to prevent circular recursion over
            //refereces. This is done by putting every created ref in
            //the dictionary, and making a lookup before following a ref.
            // Dict<old_ref, new_ref>
            var ref_dict = new RefDictionary(16);
            
            //Holds the children of the object being copied. May be an 
            //array or a dictionary. 
            object children; 
            
            //Enumer is used when enumerating a dictionary
            IEnumerator<KeyValuePair<string, PdfItem>> enumer = null;

            //When a circular reference is encountered, a placeholder is set.
            //This allows the algorithim to continue executing, then resolve
            //the circular reference later.
            Queue<Placeholder> placeholders = new Queue<Placeholder>(0);
            
            //Clones data already on the stack. This was put on the stack by
            //the "quick copy method", now that we have to make a propper copy
            //this data must be copied too.
            foreach (var stack_itm in data_stack)
                stack_itm.Data = ((ICloneable)stack_itm.Data).Clone();

            //Fetches the inner data of the array or dictionary. 
            PdfItem[] ar; int c;
            if (obj_data is PdfItem[])
            {   //Object is an array
                lock (obj_data)
                {
                    ar = (PdfItem[])((PdfItem[])obj_data).Clone();
                }
                c = pos; children = null;
            }
            else
            {
                //Object is a dictionary
                ar = null;
                if (obj_data == null)
                {
                    lock (parent)
                    {
                        enumer = ((Catalog)parent.GetChildren()).GetEnumerator();
                        //Since we can't "go back" one value we must itterate up
                        //from the beginning.
                        for (int i = 0; i < pos; i++)
                            enumer.MoveNext();
                        children = ((Catalog)parent.GetChildren()).Clone();
                    }
                    c = -1;
                    goto resume;
                }
                else
                {
                    lock (obj_data)
                    {
                        //This is for when one wish to exclude certain values
                        //from the dictionary of the item.
                        enumer = ((Catalog)obj_data).GetEnumerator();
                        children = ((Catalog)obj_data).Clone();

                        //Should the enumarator also be cloned? I'm unsure, but it depends on how concurrent dictoionary handles
                        //removal insertions of keys. I belive it makes a clone for the enumarator, so that cloning here isn't
                        //needed. Todo: Investigate this assumption. 
                    }
                    c = -1;
                    goto resume;
                }
            }

            //Resumes from where the "quick copy method" left off. 
            goto not_in_cache;

            #endregion

            //When child object of type ICRef is found, the references
            //inside it needs to be checked. A resume point is put on
            //the stack, the child object is put in the "parent" variable
            //and a jump is made up here.
        restart:
            if (HasObject(ref parent))
            {
                //If this is to be a direct object, refcount must be
                //adjusted.
                if (data_stack.Peek().WRef == null)
                    IncChildCount((PdfItem)parent);

                //Just in case, the object must also be cloned.
                parent = QuickClone(parent);
                goto back;
            }
        not_in_cache: //<- When we know this object is not in the cache
            children = parent.GetChildren();
            ar = children as PdfItem[];
            c = 0;
            if (ar != null) { lock (children) ar = (PdfItem[])ar.Clone(); }

            //When resuming after a restart the array is already cloned.
            //Cloning it again is a bad idea, so a jump is made to this
            //point instead.
        resume:
            if (ar != null)
            {
                #region Copying an array

                //Itterates over each item in the array. Replacing references
                //with writable references and restarting for ICRef children.
                for (; c < ar.Length; c++)
                {
                    PdfItem itm = ar[c];
                    if (itm is PdfReference)
                    {
                        #region Step 1. Check if the ref is translated already

                        //Sanity check. Might happen if there's circular direct references,
                        //but never with legal data structures.
                        if (itm is WritableReference &&
                            ((WritableReference)itm).Tracker == this)
                            throw new PdfInternalException("Reference already owned by tracker.");

                        //First check if the reference has been "translated"
                        //from old to new already.
                        PdfReference new_ref; var old_ref = (PdfReference)itm;
                        if (ref_dict.TryGetValue(old_ref, out new_ref))
                        {
                            //Has the ref already, no need to recheck it.
                            ar[c] = new_ref;
                            IncRefCount(((WritableReference)new_ref));

                            if (new_ref is TempWRef && new_ref.Deref() == null)
                            {
                                //This is a temporary placeholder, and this reference will therefore need a placeholder.
                                SetRefPlaceholder(ar, c, parent, old_ref.Deref() as ICRef, check_stack, data_stack, placeholders);
                            }
                            continue;
                        }

                        #endregion

                        #region Step 2. Check what kind of object that's referenced

                        //Then peek at what the ref is pointing at.
                        var target = old_ref.Deref();
                        if (!(target is ICRef))
                        {
                            //Can safly create a new reference to non-ICRef objects.
                            //This because non-ICRef objects are immutable and safe
                            //to share between different documents.
                            var wref = CreateMyWRef(target);
                            ref_dict.Add(old_ref, wref);
                            ar[c] = wref;
                            wref.RefCount = 1;

                            //Note that "AddObject" does not accept non ICref objects,
                            //so objects like these are not cached/recognized. The
                            //test saving of "RefToFloat.pdf" shows how a MediaBox,
                            //due to this, goes from having a reference to a float
                            //to not having that reference when saved as 
                            //"Page by page double.pdf"
                            //
                            //I judge this loss as acceptable, as it makes "HasObject"
                            //a bit easier to use. References to simple objects are
                            //uncommon and even when used it's more for the convinience
                            //of the PDF creator than to save space.

                            continue;
                        }

                        #endregion

                        #region Step 3. Child is ICRef, must restart

                        //Have to check the references of the ICRef object this
                        //reference is pointing at.
                        var icref = (ICRef)target;

                        #region Check in foreign cache

                        //The target may be in the cache over foreign objects. In
                        //that case we use the already translated object instead.
                        CacheObj cobj;
                        if (HasObject(icref, out cobj))
                        {
                            //The object is in the cache, but this does not mean
                            //it has a reference in the cache. 
                            var cref = cobj.Reference;
                            if (cref != null)
                            {
                                //The object has a reference in the cache. 
                                Debug.Assert(Owns(cref), "There should never be references owned by other trackers in the cache");

                                //We simple replace the existing reference.
                                ar[c] = cref;
                                IncRefCount(cref);

                                //This isn't needed (as the object would simply be taken from the cache),
                                //but we update the ref translation dictionary
                                ref_dict.Add(old_ref, cref);

                                //Then we continue checking the dictionary.
                                continue;
                            }

                            //If there's no reference, we can safly create one.
                            cref = CreateMyWRef(target);

                            //This isn't needed, but we update the ref translation dictionary
                            ref_dict.Add(old_ref, cref);

                            //We also update the cached reference
                            cobj.Reference = cref;

                            //And since this object was direct before, we inc it's refcount.
                            IncChildCount(cobj.Item);

                            //Then we add it to the dictionary and continue checking the next value.
                            ar[c] = cref;
                            cref.RefCount = 1;
                            continue;
                        }

                        #endregion

                        //Creating a new Writable Reference, that will have its
                        //value updated later.
                        var temp_ref = new TempWRef(this, CreateID());
                        temp_ref.RefCount = 1;

                        //Register the ref right away.
                        _resources.Add(temp_ref.Id, temp_ref);

                        //Adding the temp ref so that other fields get the same
                        //reference object.
                        ref_dict.Add(old_ref, temp_ref);

                        //If target isn't a IRef type object we have to manualy
                        //update the reference in the foreign cache
                        if (!(target is IRef))
                        {
                            //To that end we add the temporary reference to the
                            //reference dictionary. That way we can find the key
                            //to the CacheObj in _cache later and update it.
                            ref_dict.Add(temp_ref, old_ref);
                        }

                        //Setting the temp ref. No harm if this happens multiple
                        //times.
                        ar[c] = temp_ref;

                        //It's possible that this ICRef is being checked futher up
                        //the stack already.
                        if (SetRefPlaceholder(ar, c, parent, icref, check_stack, data_stack, placeholders))
                        {
                            //Though This only happens if there's circular references.
                            continue;
                        }

                        //Storing data needed for restart.
                        check_stack.Push(parent);
                        data_stack.Push(new ResumeFrom(ar, c, temp_ref)); //(c+1 since the temp ref is "c")

                        //Restarting. Note that target may become a new object,
                        //so there's no purpose in holding on to it.
                        parent = icref;

                        #endregion

                        goto not_in_cache;
                    }

                    if (itm is ICRef)
                    {
                        #region Restarts to check ICRef child

                        var icref = (ICRef)itm;

                        if (icref == parent)
                            throw new PdfInternalException("Object has a direct reference to itself.");

                        //Check if the icref is already in the stack.
                        if (check_stack.Contains(icref))
                        {
                            //We have a circular reference. Set placeholder object.
                            placeholders.Enqueue(new Placeholder(ar, icref, c, data_stack, check_stack, false));

                            //Set the pos null, preventing more placeholders for
                            //this object.
                            ar[c] = PdfNull.Value;

                            //Todo, check if this is a broken or unbroken circular reference. Unbroken
                            //means that there's a "pdf ref" somwhere between this and the offending icref
                            //object. If it's an unbroken chain the document will be impossible to
                            //save, as it will be infinitly large.
                            ///  i.e. item1->reference->item2->reference->item1
                            ///       is allowed while
                            ///       item1->item2->item1 isn't allowed.
                            //But this will never happen for normal usage of the libary, so fixing this
                            //is not a priority.

                            continue;
                        }

                        //Pushes the current item onto the check stack.
                        check_stack.Push(parent);

                        //Pushes state information needed to resume from
                        //this point onto the stack.
                        data_stack.Push(new ResumeFrom(ar, c, null));

                        //Switches state information.
                        parent = icref;

                        #endregion

                        //And checks the object.
                        goto restart;
                    }

                    #region Sanity check

                    //One could instead silently replace null with PdfNull.
                    if (itm == null)
                        throw new PdfInternalException("Null pointer is not allowed in data. Use PdfNull");

                    #endregion
                }

                #endregion

                #region Create new or retarget

                //All items has been checked. Makes a clone of the item.
                var the_parent = (ICRef) AddObject(parent, (PdfItem) parent.MakeCopy(ar, this));
                if (the_parent == null)
                {
                    //If one couldn't copy the object, it must be retargeted to the equivalent object
                    //in the new document
                    children = null;
                    parent = Retarget((PdfItem)parent, check_stack, data_stack, ref_dict, ref ar, out c, ref children, out enumer);
                    goto resume;
                }
                else
                    parent = the_parent;

                #endregion
            }
            else
            {
                #region Copying a dictionary

                #region Step 0. Gets en enumerator and catalog
                Catalog cat = (Catalog)children;

                if (c != -1)
                {                    
                    //Fetches the enumer and clones the
                    //catalog when restarting
                    //
                    //C is not used by the dictionary for
                    //anything other than this.
                    enumer = cat.GetEnumerator();
                    cat = cat.Clone(); //Makes a clone to allow the dictionary to be modified
                }
#if DEBUG
                //if (check_stack.Count == 0)
                //{
                //    Debug.Assert(true);
                //}
#endif

                #endregion

                while (enumer.MoveNext())
                {
                    var kp = enumer.Current;

                    PdfItem itm = kp.Value;
                    if (itm is PdfReference)
                    {

                        #region Step 1. Check if the ref is translated already

                        //Sanity check. Might happen if there's circular direct references,
                        //but never with legal data structures.
                        if (itm is WritableReference &&
                            ((WritableReference)itm).Tracker == this)
                            throw new PdfInternalException("Reference already owned by tracker.");

                        //First check if the reference has been "translated"
                        //from old to new already.
                        PdfReference new_ref; var old_ref = (PdfReference)itm;
                        if (ref_dict.TryGetValue(old_ref, out new_ref))
                        {
                            //Has the ref already, no need to recheck it.
                            cat[kp.Key] = new_ref;
                            IncRefCount(((WritableReference)new_ref));

                            //It is possible that this reference points at an ilegal object.
                            //if (new_ref.Deref() == null)
                            //{
                                //References that point at nothing isn't complete yet, so they
                                //should be on the stack. (Unless they've been flushed from memory)
                                //Debug.Assert((old_ref.Deref() is ICRef && check_stack.Contains((ICRef)old_ref.Deref())));
                            //}

                            if (new_ref is TempWRef && new_ref.Deref() == null)
                            {
                                //This is a temporary placeholder, and this reference will therefore need a placeholder.
                                SetRefPlaceholder(cat, kp.Key, parent, old_ref.Deref() as ICRef, check_stack, data_stack, placeholders);
                            }

                            continue;
                        }

                        #endregion

                        #region Step 2. Check what kind of object that's referenced

                        //The peek at what the ref is pointing at.
                        var target = old_ref.Deref();
                        if (!(target is ICRef))
                        {
                            #region Step 2b. Halt on Pages

                            if (kp.Key == "Type" && target is PdfName && target.GetString() == "Pages")
                            {
                                itm = target;
                                goto HaltOnPage;
                            }

                            #endregion

                            //Can safly create a new refernce to non-ICRef objects.
                            var wref = CreateMyWRef(target);
                            ref_dict.Add(old_ref, wref);
                            cat[kp.Key] = wref;
                            wref.RefCount = 1;

                            //Note that "AddObject" does not accept non ICref objects,
                            //so objects like these are not cached/recognized. The
                            //test saving of "RefToFloat.pdf" shows how a MediaBox,
                            //due to this, goes from having a reference to a float
                            //to not having that reference when saved as 
                            //"Page by page double.pdf"
                            //
                            //I judge this loss as acceptable, as it makes "HasObject"
                            //a bit easier to use. References to simple objects are
                            //uncommon and even when used it's more for the convinience
                            //of the PDF creator than to save space.

                            continue;
                        }

                        #endregion

                        #region Step 3. Child is ICRef, must restart

                        //Have to check the references of the ICRef object this
                        //reference is pointing at.
                        var icref = (ICRef)target;

                        #region Check in foreign cache

                        //The target may be in the cache over foreign objects. In
                        //that case we use the already translated object instead.
                        CacheObj cobj;
                        if (HasObject(icref, out cobj))
                        {
                            //The object is in the cache, but this does not mean
                            //it has a reference in the cache. 
                            var cref = cobj.Reference;
                            if (cref != null)
                            {
                                //The object has a reference in the cache. 
                                Debug.Assert(Owns(cref), "There should never be references owned by other trackers in the cache");

                                //We simple replace the existing reference.
                                cat[kp.Key] = cref;
                                IncRefCount(cref);

                                //This isn't needed, but we update the ref translation dictionary
                                ref_dict.Add(old_ref, cref);

                                //Then we continue checking the dictionary.
                                continue;
                            }

                            //If there's no reference, we can safly create one.
                            cref = CreateMyWRef(target);

                            //This isn't needed, but we update the ref translation dictionary
                            ref_dict.Add(old_ref, cref);

                            //We also update the cached reference
                            cobj.Reference = cref;

                            //And since this object was direct before, we inc it's refcount.
                            IncChildCount(cobj.Item);

                            //Then we add it to the dictionary and continue checking the next value.
                            cat[kp.Key] = cref;
                            cref.RefCount = 1;

                            continue;
                        }

                        #endregion

                        //Creating a new Writable Reference, that will have its
                        //value updated later.
                        var temp_ref = new TempWRef(this, CreateID());
                        temp_ref.RefCount = 1;

                        //Register the ref right away.
                        _resources.Add(temp_ref.Id, temp_ref);

                        //Adding the temp ref so that other fields get the same.
                        ref_dict.Add(old_ref, temp_ref);

                        //If target isn't a IRef type object we have to manualy
                        //update the reference in the foreign cache
                        if (!(target is IRef))
                        {
                            //To that end we add the temporary reference to the
                            //reference dictionary. That way we can find the key
                            //to the CacheObj in _cache later and update it.
                            ref_dict.Add(temp_ref, old_ref);
                        }

                        //Setting the temp ref. No harm if this happens multiple
                        //times.
                        cat[kp.Key] = temp_ref;

                        //It's possible that this ICRef is being checked futher up
                        //the stack already.
                        if (SetRefPlaceholder(cat, kp.Key, parent, icref, check_stack, data_stack, placeholders))
                        {
                            //In which case there's no need to check this icref,
                            //as it's already being checked.

                            //But, when that icref is finished it's checking the
                            //reference must be updated.
                            continue;
                        }

                        //Storing data needed for restart. 
                        check_stack.Push(parent);
                        data_stack.Push(new ResumeFrom(cat, enumer, temp_ref));

                        //Restarting. Note that target may become a new object,
                        //so there's no purpose in holding on to it.
                        parent = icref;

                        #endregion

                        goto not_in_cache;
                    }

                    if (itm is ICRef)
                    {
                        #region Restarts to check ICRef child

                        var icref = (ICRef)itm;

                        if (icref == parent)
                            throw new PdfInternalException("Object has a direct reference to itself.");

                        //Check if the icref is already in the stack.
                        if (check_stack.Contains(icref))
                        {
                            //We have a circular reference. Set placeholder object.
                            placeholders.Enqueue(new Placeholder(cat, icref, kp.Key, data_stack, check_stack, false));

                            //Set the pos null, preventing more placeholders for
                            //this object.
                            cat[kp.Key] = PdfNull.Value;

                            //Todo, check if this is a broken or unbroken circular reference. Broken
                            //means that there's a "pdf ref" somwhere between this and the offending icref
                            //object. If it's an unbroken chain the document will be impossible to
                            //save, as it will be infinitly large.
                            ///  i.e. item1->reference->item2->reference->item1
                            ///       is allowed while
                            ///       item1->item2->item1 isn't allowed.
                            //But, unbroken circular reference are not legal and will never appear with
                            //common usage of the libary. Fixing this is not a priority. 

                            continue;
                        }

                        //Pushes the current item onto the check stack.
                        check_stack.Push(parent);

                        //Pushes state information needed to resume from
                        //this point onto the stack.
                        data_stack.Push(new ResumeFrom(cat, enumer, null));

                        ////Puts the object in the ref_dicts, to make it possible
                        ////to invalidate the item later if needed.
                        //if (_cache != null)
                        //    ref_dict.Add(new TempReference((PdfItem)icref), null);

                        //Switches state information.
                        parent = icref;

                        #endregion

                        goto restart;
                    }

                    #region Step 2b. Halt on Pages

                HaltOnPage:
                    if (kp.Key == "Type" && itm is PdfName && itm.GetString() == "Pages")
                    {
                        //Pages is one of those objects we don't want copied.
                        ar = null;
                        parent = Retarget((PdfItem)parent, check_stack, data_stack, ref_dict, ref ar, out c, ref children, out enumer);
                        goto resume;
                    }

                    #endregion

                    #region Sanity check

                    //One could also silently replace null with PdfNull.
                    if (itm == null)
                        throw new PdfInternalException("Null pointer is not allowed in data. Use PdfNull");

                    #endregion
                }

                #endregion

                #region Create new or retarget

                //All items has been checked. Makes a new item.
                var the_parent = (ICRef)AddObject(parent, (PdfItem)parent.MakeCopy(cat, this));
                if (the_parent == null)
                {
                    //If one couldn't copy the object, it must be retargeted to the equivalent object
                    //in the new document
                    ar = null;
                    parent = Retarget((PdfItem)parent, check_stack, data_stack, ref_dict, ref ar, out c, ref children, out enumer);
                    goto resume;
                }
                else
                    parent = the_parent;

                #endregion
            }

        //When a object is in the cache, there's no need or want to check the children
        //so we skip to unwinding the stack. 
        back:

            //Finished checking the parent item and made a copy of the parent. 
            //Now unwind the stack, set the grand parent field and restart.
            if (data_stack.Count > 0)
            {
                //if (data_stack.Count == 7)
                //    Console.WriteLine("ResTracker");

                //ResumeFrom is 1-1 with the check stack.
                ResumeFrom res_from = data_stack.Pop();

                #region Resuming

                if (res_from.WRef != null)
                {
                    //Only need to update the ref in this case.
                    res_from.WRef.SetValue((PdfItem)parent);

                    //Unless the object isn't of type IRef
                    if (!(parent is IRef))
                    {
                        //Then we have to update the reference in the
                        //foreign cache. This is done automatically by
                        //IRef objects.
                        SetCachedReference(ref_dict[res_from.WRef].Deref(), res_from.WRef);
                        ref_dict.Remove(res_from.WRef);
                    }

                    //Assumes that there's always a ICRef object
                    //for this TempWRef
                    Debug.Assert(check_stack.Count > 0);

                    //The parent object of this object.
                    parent = check_stack.Pop();

                    if (res_from.From is int)
                    {
                        ar = (PdfItem[])res_from.Data;
                        c = (int)res_from.From + 1;
                    }
                    else
                    {
                        children = (Catalog) res_from.Data;
                        //Setting c = -1 tells "don't clone", ar = null tells "not array"
                        c = -1; ar = null;
                        enumer = (IEnumerator<KeyValuePair<string, PdfItem>>)res_from.From;
                    }

                    goto resume;
                }
                else
                {//Note this code is replicated in the "retarget" function
                    if (res_from.From is int)
                    {
                        ar = (PdfItem[])res_from.Data;
                        c = (int)res_from.From;

                        //Have to update the data field in the parent with
                        //the copied ICRef object
                        ar[c++] = (PdfItem)parent;
                    }
                    else
                    {
                        children = (Catalog)res_from.Data;
                        enumer = (IEnumerator<KeyValuePair<string, PdfItem>>)res_from.From;

                        //Setting c = -1 tells "don't clone", ar = null tells "not array"
                        c = -1; ar = null;

                        //Updates the data in the parent. (Note that there may be an item 
                        //there but, while it looks the same, it's a different item)
                        lock (children) { ((Catalog)children)[enumer.Current.Key] = (PdfItem)parent; }
                    }

                    //Goes back to the grand parent
                    parent = check_stack.Pop();

                    goto resume;
                }

                #endregion
            }

            #region Setting data in placeholder positions

            SetDataInPlaceholders(placeholders, check_stack, parent, ref_dict);

            #endregion

            return parent;
        }

        private void SetDataInPlaceholders(Queue<Placeholder> placeholders, Stack<ICRef> check_stack, ICRef parent, RefDictionary ref_dict)
        {
            while (placeholders.Count > 0)
            {
                var ph = placeholders.Dequeue();

#if DEBUG
                //if (placeholders.Count == 0)
                //{
                //    Debug.Assert(true);
                //}
#endif

                //Placeholders are intended for those "oh shit" situations
                //and can therefore take whatever time they need to figure
                //out what to do.

                #region Step 1. Get data

                //Assume that we're finished with all child objects
                Debug.Assert(check_stack.Count == 0);

                //The object that sits in "parent" now is the not
                //quite finished object. What needs to be done is
                //walk it and retrive the value that we're after.
                var cs = ph.CheckStack;

                //The check stack contains a list of objects that
                //was being prossesed when the placeholder was set.
                //One of the objects is the original value of the
                //object we're after.

                #endregion

                #region Step 2. Find correct ICRef

                //We know that this object has been converted to
                //the value we want, and we now know where to look
                //for it. "It's somewhere in the stack", except if 
                //it's the top as that's the root (parent). 

                PdfItem wanted_item = (PdfItem)parent;
                PdfReference wanted_ref = null;
                ResumeFrom ds = ph.ResumeObject;
                if (ds != null)
                {
                    if (ds.From is int)
                        wanted_item = ((PdfItem[])ds.Data)[(int)ds.From];
                    else
                        wanted_item = ((Catalog)ds.Data)[ph.FromKey];

                    //While "PdfNull" is allowed, plain null pointers must never
                    //be in the data structure.
                    if (wanted_item == null)
                        throw new PdfInternalException("Bug. Wanted item not set.");
                    if (wanted_item is PdfReference)
                    {
                        wanted_ref = (PdfReference)wanted_item;
                        wanted_item = wanted_item.Deref();
                    }
                }

                #endregion

                #region Step 3. Place ICRef in placeholder pos

                //Finally we set the ICRef in place of the placeholder.
                if (ph.Field is int)
                {
                    var index = (int)ph.Field;
                    var par_ar = (PdfItem[])ph.ParentData;
                    if (par_ar[index] is TempWRef)
                    {
                        if (wanted_item is IRef && ((IRef)wanted_item).HasReference)
                        {
                            //We prefer to use an existing reference
                            if (((TempWRef)par_ar[index]).RefCount > 0)
                                //RefCount can actually be zero. This happens when an illigal object, with a TempWRef,
                                //got deleted. Calling DecRefCount on a ref with zero refcount cause a assert, but is
                                //otherwise harmless.
                                DecRefCount(par_ar[index]);

                            //This assert does not hold true, as other objects may have a reference to the wanted item
                            //Debug.Assert(((TempWRef)par_ar[index]).RefCount == ((IRef)wanted_item).Reference.RefCount);

                            //Puts the reference to the wanted item into this object
                            wanted_ref = ((IRef)wanted_item).Reference;
                            par_ar[index] = wanted_ref;
                            IncRefCount((WritableReference)wanted_ref);
                        }
                        else
                        {
                            ((TempWRef)par_ar[index]).SetValue(wanted_item);

                            //If the object isn't of type IRef
                            if (!(wanted_item is IRef))
                            {
                                //Then we have to update the reference in the
                                //foreign cache (This is done automatically by
                                //IRef objects).
                                SetCachedReference(ref_dict[(TempWRef)par_ar[index]].Deref(), (TempWRef)par_ar[index]);
                                ref_dict.Remove((TempWRef)par_ar[index]);
                            }
                        }
                    }
                    else if (ph.FieldWasRef)
                    {
                        if (wanted_item is IRef && ((IRef)wanted_item).HasReference)
                        {
                            wanted_ref = ((IRef)wanted_item).Reference;
                            par_ar[index] = wanted_ref;
                            IncRefCount((WritableReference)wanted_ref);
                        }
                        else if (wanted_ref != null)
                        {
                            par_ar[index] = wanted_ref;
                            if (wanted_ref is WritableReference)
                                IncRefCount((WritableReference)wanted_ref);
                        }
                        else
                        {
                            par_ar[index] = CreateMyWRef(wanted_item);
                            ((WritableReference)par_ar[index]).RefCount = 1;
                        }
                    }
                    else
                        par_ar[index] = wanted_item;
                }
                else
                {
                    var par_cat = (Catalog)ph.ParentData;
                    var par_key = par_cat[ph.Field.ToString()];
                    if (par_key is TempWRef)
                    {
                        if (wanted_item is IRef && ((IRef)wanted_item).HasReference)
                        {
                            //We prefer to use an existing reference, so when there
                            //is one we remove the temp ref
                            if (((TempWRef)par_key).RefCount > 0)
                                //RefCount can actually be zero. This happens when an illigal object, with a TempWRef,
                                //got deleted. 
                                DecRefCount(par_key);

                            //This assert does not hold true, as other objects may have a reference to the wanted item
                            //Debug.Assert(((TempWRef)par_key).RefCount == ((IRef)wanted_item).Reference.RefCount);
                            wanted_ref = ((IRef)wanted_item).Reference;
                            par_cat[ph.Field.ToString()] = wanted_ref;
                            IncRefCount((WritableReference)wanted_ref);
                        }
                        else
                        {
                            ((TempWRef)par_key).SetValue(wanted_item);

                            //If the object isn't of type IRef
                            if (!(wanted_item is IRef))
                            {
                                //Then we have to update the reference in the
                                //foreign cache (This is done automatically by
                                //IRef objects).
                                SetCachedReference(ref_dict[(TempWRef)par_key].Deref(), (TempWRef)par_key);
                                ref_dict.Remove((TempWRef)par_key);
                            }
                        }
                    }
                    else if (ph.FieldWasRef)
                    {
                        if (wanted_item is IRef && ((IRef)wanted_item).HasReference)
                            par_cat[ph.Field.ToString()] = ((IRef)wanted_item).Reference;
                        else if (wanted_ref != null)
                            par_cat[ph.Field.ToString()] = wanted_ref;
                        else
                        {
                            par_cat[ph.Field.ToString()] = CreateMyWRef(wanted_item);
                            ((WritableReference)par_cat[ph.Field.ToString()]).RefCount = 1;
                        }
                    }
                    else
                        par_cat[ph.Field.ToString()] = wanted_item;
                }

                #endregion
            }
        }

        private bool SetRefPlaceholder(object container, object key, ICRef parent, ICRef icref, Stack<ICRef> check_stack, Stack<ResumeFrom> data_stack, Queue<Placeholder> placeholders)
        {
            if (icref != null && (check_stack.Contains(icref) || icref == parent))
            {
                #region Add parrent if needed
                if (icref == parent)
                {
                    //This helps with sanity checking. Could make it so
                    //that it assumed the last item to be the right ref,
                    //but this way I know.

                    check_stack = CloneStack<ICRef>(check_stack);
                    check_stack.Push(parent);
                    data_stack = CloneStack<ResumeFrom>(data_stack);
                    data_stack.Push(new ResumeFrom(container, key, null));
                }
                #endregion

                placeholders.Enqueue(new Placeholder(container, icref, key, data_stack, check_stack, true));

                return true;
            }

            return false;
        }

        /// <summary>
        /// MakeCopy assumes that if an object is owned, there's no need
        /// to copy it. Adopt breaks this assumption by taking ownership
        /// before the internals is copied. 
        /// </summary>
        private PdfItem MakeCopy(ICRef obj)
        {
            if (obj is IRef)
            {
                var iref = (IRef)obj;
                if (Owns(iref))
                {
                    var hold = iref.Reference;
                    iref.Reference = null;
                    var copy = MakeCopy(obj, true);
                    iref.Reference = hold;
                    return copy;
                }
            }
            return MakeCopy(obj, true);
        }

        private ICRef QuickClone(ICRef obj)
        {
            var kids = obj.GetChildren();
            lock (kids)
            {
                if (kids is PdfItem[])
                {
                    var clone = ((PdfItem[])kids).Clone();
                    return obj.MakeCopy(clone, this);
                }

                {
                    var clone = ((Catalog)kids).Clone();
                    return obj.MakeCopy(clone, this);
                }
            }
        }

        /// <summary>
        /// Removes unfinished objects from the cache.
        /// 
        /// How this works:
        /// 1. Put all original ICref items in the ref_dict
        /// 2. When invalidating the cache look at each ICRef item
        ///    independantly. Don't look on ICRef children to ICRef
        ///    objects
        /// 3. If there's a ref with refcount 0, invalidate the ICRef
        ///
        /// This will work since:
        /// 1. Only the object currently being evaluated has incomplete
        ///    ICref objects in the cache. 
        /// 2. All ICRef objects will be in the ref_dict in a form that
        ///    makes it easy to find it again in the object cache
        ///
        /// Drawbacks:
        ///  the ref_dict will be more bloated than it already is and 
        ///  it's now used for three sepperate purposes.
        /// </summary>
        /// <param name="ref_dict"></param>
        private void RemoveFromCache(RefDictionary ref_dict, RefDictionary org_ref_dict)
        {
            foreach (var kp in ref_dict)
            {
                var obj = kp.Key.Deref();
                if (obj is ICRef)
                {
                    ICRef target = (ICRef) obj;
                    if (HasObject(ref target))
                    {
                        var children = target.GetChildren();
                        lock (children)
                        {
                            if (children is PdfItem[])
                            {
                                foreach (var child in (PdfItem[])children)
                                {
                                    if (Incomplete(child))
                                    {
                                        RemoveObject((ICRef)obj);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                foreach (var child_kp in (Catalog)children)
                                {
                                    if (Incomplete(child_kp.Value))
                                    {
                                        RemoveObject((ICRef)obj);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                if (kp.Key is TempReference)
                    org_ref_dict.Remove(kp.Key);
            }
        }

        private bool Incomplete(PdfItem itm)
        {
            if (itm is WritableReference)
            {
                var wr = (WritableReference)itm;
                if (wr.RefCount == 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Some objects can not be copied.
        ///  1. Page tree
        ///  2. Catalog
        ///  3. Trailer
        ///  4. PdfPage
        ///
        /// References to these objects must be retargeted to their equivalents in
        /// the new document.
        /// </summary>
        /// <param name="container">
        /// This is the original container object the illigal object would have represented
        /// </param>
        /// <param name="check_stack">
        /// A stack of original objects. Needed for resuming. 
        /// </param>
        /// <param name="data_stack">
        /// This stack contains partially converted data.
        /// </param>
        /// <param name="ref_dict">
        /// The makecopy method keeps a cache over references so that they can be reused.
        /// Deleted references must be removed from this cache.
        /// </param>
        /// <param name="ar">
        /// If the container is an arraytype, this is the fully converted array.
        /// </param>
        /// <param name="c">
        /// The position in the array. Since this method is only called
        /// on completed objects, we don't need to know its current value</param>
        /// <param name="children">
        /// If the container is a dict type, this is the fully converted dictionary.
        /// </param>
        /// <param name="enumer">
        /// Enumerator for enumerating the children.
        /// </param>
        /// <remarks>
        /// Beware when debugging: If you browse the document structure
        /// while debugging inside this function, you may inadvertedly 
        /// alter it and break this function.
        ///  - This because of the check_stack. It does a reference equivalence on
        ///    the object, so if the object is changed this assumption breaks.
        /// </remarks>
        private ICRef Retarget(PdfItem container, Stack<ICRef> check_stack, Stack<ResumeFrom> data_stack,
            RefDictionary ref_dict,
            ref PdfItem[] ar, out int c, ref object children, out IEnumerator<KeyValuePair<string, PdfItem>> enumer)
        {
            #region Find problematic root

            //For convinience we create a item for the illigal object.
            ICRef problem_item;
            if (ar != null)
                problem_item = new WritableArray(ar, this);
            else
                problem_item = new WritableDictionary((Catalog)children, this);
            //There's no need to worry about the "Type" as checking for
            //illigal objects is always done on the original object.

            //The checkstack contains the parent of the uncopyable object.
            var parent = check_stack.Pop();
            var data = data_stack.Pop();

            //The illigal object is inserted into it's supposed position.
            //in its parent. This way there's no need to special case it.
            data.Item = (PdfItem) problem_item;

            //The parent of an illigal object may also be uncopyable.
            //This is especially true in the case of a PdfPage, as
            //it will only come here because of its parrent reference
            while (CantCopy(parent))
            {
                if (check_stack.Count == 0)
                {
                    Debug.Assert(false, "Untested code path");
                    break;
                }

                //A parent object is always incomplete, this is
                //a problem so we reconstruct it in its incomplete
                //form.
                problem_item = (ICRef)MakeIncomplete(data);
                
                container = (PdfItem)parent;
                parent = check_stack.Pop();
                data = data_stack.Pop();

                //Place the illigal object where it's suppose to go.
                data.Item = (PdfItem)problem_item;

                if (check_stack.Count == 0)
                {

                    //Pages are allowed to be copied when they
                    //are the root object. In that case it
                    //will always be in the form of a PdfPage
                    //object.
                    if (parent is PdfPage)
                        break;
                }
            }

            #endregion

            var icref = (ICRef)container;
            data_stack.Push(data);
            check_stack.Push(parent);

            RetargetIlligal(icref, problem_item, new Dictionary<ICRef, RetargetState>(400), ref_dict);

            _owner.Retarget(new RetargetObject(container, data, ref_dict, this));

            #region Resumes

            //ResumeFrom is 1-1 with the check stack.
            ResumeFrom res_from = data_stack.Pop();

            if (res_from.From is int)
            {
                ar = (PdfItem[])res_from.Data;
                c = (int)res_from.From + 1;
                children = null;
                enumer = null;
            }
            else
            {
                children = (Catalog)res_from.Data;
                enumer = (IEnumerator<KeyValuePair<string, PdfItem>>)res_from.From;

                //Setting c = -1 tells "don't clone", ar = null tells "not array"
                c = -1; ar = null;
            }

            //Goes back to the grand parent
            return check_stack.Pop();

            #endregion
        }

        /// <summary>
        /// Makes an incomplete object from the current resume data.
        /// </summary>
        private PdfObject MakeIncomplete(ResumeFrom data)
        {

            if (data.From is int)
            {
                var index = (int)data.From;
                var ar = new PdfItem[index + 1];
                var org_array = (PdfItem[])data.Data;
                Array.Copy(org_array, ar, ar.Length);
                return new WritableArray(ar, this);
            }
            else
            {
                var org_dict = (Catalog)data.Data;
                var dict = new Catalog(org_dict.Count);
                var key = data.Key;
                var enumer = (IEnumerator<KeyValuePair<string, PdfItem>>)data.From;

                //enumer.Reset(); //<-- Not supported by ConcurentDictionary
                enumer.Dispose();
                enumer = org_dict.GetEnumerator();
                data.From = enumer;

                while (enumer.MoveNext())
                {
                    var kp = enumer.Current;
                    dict.Add(kp.Key, org_dict[kp.Key]);
                    if (kp.Key == (string) key) break;
                }

                return new WritableDictionary(dict, this);
            }
        }

        /// <summary>
        /// This object was illiegaly copied. That means refcounts and caches it affected when it was
        /// copied has to be modified. 
        /// 
        /// Go through all the child's kids, put them in a hashset with a "clean/dirty/undetermined/illigal"
        /// flag.
        ///
        /// If a reference to "child" is found (perhaps set the currently null placeholder ref to make this easier),
        /// then retarget that reference.
        ///
        /// Iligal child objects are flagged as illigal. References going to them are also retargeted
        ///
        /// Then when everything that needs retargeting is retargeted, delete all illigal objects from
        /// the various caches
        ///  - Optimalization: "Retargeted references" Think on the fersibility of that.
        ///
        /// Dealing with circular references:
        ///   1. The status flag shuld start off as "Unknown" (or not pressent in the hashset). 
        ///   2. Then when one start excamening the object the flag is set undetermined.
        ///      (Unless the object is illigal)
        ///   3. If a "undetermined or dirty" object is found, the object is flagged as dirty and
        ///      later deleted along with the illigal objects
        ///  This isn't ideal, but should be good enough. Though on second thought, since one figure
        ///  out illigal objects to begin with[*] one can simply drop the dirty bit, hmm.
        ///
        ///  [*]All illigal objects are in a form that can be recognized.
        ///   - Page has "Type" Page
        ///   - Pages has "Type" Pages
        ///   - PageTree is never referenced by non-Pages objects
        ///   - Trailer is always parsed        
        /// </summary>
        /// <param name="org_root">The original object, it's "root" as it's children will be examined</param>
        /// <param name="check_status">The new, possibly incomplete, object</param>
        private void RetargetIlligal(ICRef org_root, ICRef root, Dictionary<ICRef, RetargetState> check_status, RefDictionary ref_dict)
        {
            //Returns straight away if there's nothing to do
            if (root == null)
            {
                // Note: Since root is null we can assume that the object isn't
                //       cached, but for now we don't
                RemoveObject(org_root);
                return;
            }

            if (check_status.ContainsKey(org_root))
            {
                    return;         
            }

            var new_children = root.GetChildren();
#if PRUNE_INVALID
            var old_children = org_root.GetChildren();
            if (ReferenceEquals(new_children, old_children))
            {
                //Can't modify a straight copied dictionary. 
                RemoveObject(org_root);
                return;
            }
#endif

            if (PdfPage.IsPage((PdfItem)org_root))
            {
                //Pages can be both legal and illigal objects. Because of this,
                //checks if a page is part of a document before deleting it.

                //Checks if the page is in the document's page tree
                lock (new_children)
                {
                    var parent = ((Catalog)new_children)["Parent"].Deref() as Pdf.PdfPages;
                    if (parent != null && ((IKRef)parent).IsOwner(this))
                    {
                        //There's no need to remove a valid object from the cache.
                        //RemoveObject(org_root);
                        return;
                    }
                }
            }

            check_status.Add(org_root, new RetargetState(root, RetargetState.CheckStatus.Illigal));

            Monitor.Enter(org_root);
            bool has_lock = true;

            try
            {

                foreach (var kp in ItterateItem(org_root))
                {
                    //We get the new item.
                    PdfItem new_item;
                    if (TryGetICRefItem(kp.Key, new_children, out new_item))
                    {
                        //The item the new item was created from
                        PdfItem old_item = kp.Value;
                        if (ReferenceEquals(old_item, new_item))
                        {
#if PRUNE_INVALID
                        if (!(new_item is ICRef))
                            RemoveICRefItem(kp.Key, new_children);
#else
                            //May as well break here instead of "continue", as this is
                            //the end of the modified part of a "incomplete" object.
                            //
                            //Assuming enumerations itterate in the same order.
                            //
                            //This is done  futher down for "null" values. 
                            //
                            //It may be an idea to instead simply remove the
                            //items from the new_items, unless it's a writable
                            //reference or an object that can contain references
                            //(ICRef)
#endif
                            continue;
                        }

                        ICRef new_icref, old_icref;
                        if (new_item is ICRef)
                        {
                            new_icref = (ICRef)new_item;
                            old_icref = (ICRef)old_item;

                            //If the item is in the check_status something odd is going on,
                            //as we have direct objects in multiple places, but we can simply
                            //ignore the situation.
                            if (check_status.ContainsKey(old_icref))
                            {
                                continue;
                            }

                            Monitor.Exit(org_root);
                            has_lock = false;

                            //If this is another illigal object, it must be checked
                            //in a similar manner to this.
                            if (CantCopy(old_icref))
                                RetargetIlligal(old_icref, new_icref, check_status, ref_dict);
                            else
                                RetargetLegal(old_icref, new_icref, check_status, ref_dict);

                            Monitor.Enter(org_root);
                            has_lock = true;
                        }
                        else if (new_item is WritableReference)
                        {
                            var value = new_item.Deref();
                            if (value is ICRef)
                            {
                                new_icref = (ICRef)value;
                                old_icref = (ICRef)old_item.Deref();

                                //Objects need only be checked once, and it
                                //helps against circular references
                                if (!check_status.ContainsKey(old_icref))
                                {
                                    Monitor.Exit(org_root);
                                    has_lock = false;

                                    //If this is another illigal object, it must be checked
                                    //in a similar manner to this.
                                    if (CantCopy(old_icref))
                                    {
                                        RetargetIlligal(old_icref, new_icref, check_status, ref_dict);

                                        //We also have to remove the reference from the reference cache,
                                        //or we'll see this reference reused.
                                        //
                                        //Could do a if (wr.RefCount == 1) here, not doing it simply
                                        //means removing the key multiple times.
                                        RemoveValue(ref_dict, (WritableReference)new_item);
                                    }
                                    else
                                        RetargetLegal(old_icref, new_icref, check_status, ref_dict);

                                    Monitor.Enter(org_root);
                                    has_lock = true;
                                }
                            }
                            else if (value == null)
                            {
                                //A reference pointing to null simply means that it points
                                //at a item up the stack, being constructed. Legual or illigual
                                //this particular reference isn't needed anymore.
                                //
                                //Could do a if (wr.RefCount == 1) here, not doing it simply
                                //means removing the key multiple times.
                                RemoveValue(ref_dict, (WritableReference)new_item);
                            }

                            //Refcounting is still in effect. Note, this object is "invalid",
                            //meaning it can contain non-Writable references. DecChildCount(IenumRef)
                            //HashSet been modified to handle this scenario.
                            DecRefCount((WritableReference)new_item);

                            //The reference need to be removed from this object, to prevent it
                            //from being decremented twice.
                            SetICRefItem(kp.Key, new_children, PdfNull.Value);
                        }

                    }
                    else
                    {
                        //We break if the item does not exist.  This assumes that the
                        //enum itterates in the same order on the "org_root".
                        //
                        //If this isn't the case, simply remove this break.
                        break;
                    }
                }
            } finally { if (has_lock) Monitor.Exit(org_root); }

            //The foreign item must also be removed.
            RemoveObject(org_root);
        }

        /// <summary>
        /// Similar to RetargetIlligal, except in this case the root object is possible to copy,
        /// but may have children that is not possible to copy.
        /// </summary>
        /// <param name="org_root"></param>
        /// <param name="root"></param>
        /// <param name="check_status"></param>
        /// <param name="ref_dict"></param>
        private void RetargetLegal(ICRef org_root, ICRef root, Dictionary<ICRef, RetargetState> check_status, RefDictionary ref_dict)
        {
            //Returns straight away if there's nothing to do
            if (root == null)
            {
                // Note: Since root is null we can assume that the object isn't
                //       cached, but for now we don't
                RemoveObject(org_root);
                return;
            }

            if (check_status.ContainsKey(org_root))
            {
                return;
            }

            var new_children = root.GetChildren();
#if PRUNE_INVALID
            var old_children = org_root.GetChildren();
            if (ReferenceEquals(new_children, old_children))
            {
                //Can't modify a straight copied dictionary,
                //as that will modify the source dict.
                //
                //This can be solved by adding a method for
                //setting a empty dict in the root object.
                //
                //Or by telling the caller of this method
                //that it must remove the root object from
                //the document.
                RemoveObject(org_root);
                return;
            }
#endif

            var my_state = new RetargetState(root, RetargetState.CheckStatus.Undetermined);
            check_status.Add(org_root, my_state);

            Monitor.Enter(org_root);
            bool has_lock = true;

            try
            {
                foreach (var kp in ItterateItem(org_root))
                {
                    //We get the new item.
                    PdfItem new_item;
                    if (TryGetICRefItem(kp.Key, new_children, out new_item))
                    {
                        //The item the new item was created from
                        PdfItem old_item = kp.Value;

                        if (ReferenceEquals(old_item, new_item))
                        {
#if PRUNE_INVALID
                            if (!(new_item is ICRef))
                                RemoveICRefItem(kp.Key, new_children);
#else
                            //May as well break here instead of "continue", as this is
                            //the end of the modified part of a "incomplete" object.
                            //
                            //Assuming enumerations itterate in the same order.
                            //
                            //This is done  futher down for "null" values. 
                            //
                            //It may be an idea to instead simply remove the
                            //items from the new_items, unless it's a writable
                            //reference or an object that can contain references
                            //(ICRef)
#endif
                            continue;
                        }

                        old_item = old_item.Deref();
                        ICRef new_icref, old_icref;

                        if (old_item is ICRef)
                        {
                            if (new_item is WritableReference)
                            {
                                var derefed = new_item.Deref();
                                if (derefed == PdfNull.Value)
                                    continue;
                                new_icref = (ICRef)new_item.Deref();
                                old_icref = (ICRef)old_item.Deref();
                            }
                            else
                            {
                                if (new_item == PdfNull.Value)
                                    continue;
                                new_icref = (ICRef)new_item;
                                old_icref = (ICRef)old_item;
                            }

                            //If the item is in the check_status something odd is going on,
                            //as we have direct objects in multiple places, but we allow for
                            //it
                            RetargetState status;
                            if (check_status.TryGetValue(old_icref, out status))
                            {
                                if (status.Status == RetargetState.CheckStatus.Illigal)
                                {
                                    _owner.Retarget(new RetargetObject((PdfItem)old_icref, new_children, kp.Key, this));

                                    if (new_item is WritableReference)
                                    {
                                        var wr = (WritableReference)new_item;
                                        if (wr.RefCount == 0)
                                            RemoveValue(ref_dict, wr);
                                    }

                                    my_state.Status = RetargetState.CheckStatus.Modified;
                                }
                            }
                            else
                            {

                                //If this is another illigal object, it must be checked
                                //in a similar manner to this.
                                Monitor.Exit(org_root);
                                has_lock = false;

                                if (CantCopy(old_icref))
                                {
                                    RetargetIlligal(old_icref, new_icref, check_status, ref_dict);

                                    _owner.Retarget(new RetargetObject((PdfItem)old_icref, new_children, kp.Key, this));

                                    if (new_item is WritableReference)
                                    {
                                        var wr = (WritableReference)new_item;
                                        if (wr.RefCount == 0)
                                            RemoveValue(ref_dict, wr);
                                    }

                                    my_state.Status = RetargetState.CheckStatus.Modified;
                                }
                                else
                                {
                                    RetargetLegal(old_icref, new_icref, check_status, ref_dict);

                                    //No need to do anything with any reference.
                                }

                                Monitor.Enter(org_root);
                                has_lock = true;
                            }
                        }
                    }
                    else
                    {
                        //We break if the item does not exist.  This assumes that the
                        //enum itterates in the same order on the "org_root".
                        //
                        //If this isn't the case, simply remove this break.
                        break;
                    }
                }
            } finally { if (has_lock) Monitor.Exit(org_root); }

            //This object has no modifications.
            if (my_state.Status == RetargetState.CheckStatus.Undetermined)
                my_state.Status = RetargetState.CheckStatus.Clean;
        }

        /// <summary>
        /// Simplifies setting a child into a ICref
        /// </summary>
        private void SetICRefItem(object key, object icref_children, PdfItem value)
        {
            lock (icref_children)
            {
                if (icref_children is PdfItem[])
                    ((PdfItem[])icref_children)[(int)key] = value;
                else
                    ((Catalog)icref_children)[(string)key] = value;
            }
        }

        /// <summary>
        /// Simplifies removing a child out of a ICref
        /// </summary>
        private void RemoveICRefItem(object key, object icref_children)
        {
            SetICRefItem(key, icref_children, PdfNull.Value);
        }

        /// <summary>
        /// Simplifies getting a child out of a ICref
        /// </summary>
        private PdfItem GetICRefItem(object key, object icref_children)
        {
            if (icref_children is PdfItem[])
                return ((PdfItem[])icref_children)[(int)key];
            else
                return ((Catalog)icref_children)[(string)key];
        }

        /// <summary>
        /// Simplifies getting a child out of a ICref
        /// </summary>
        private bool TryGetICRefItem(object key, object icref_children, out PdfItem child)
        {
            lock (icref_children)
            {
                if (icref_children is PdfItem[])
                {
                    int index = (int)key;
                    var kids = (PdfItem[])icref_children;
                    if (index >= kids.Length)
                    {
                        child = null;
                        return false;
                    }
                    else
                    {
                        child = kids[index];
                        return true;
                    }
                }
                else
                {
                    return ((Catalog)icref_children).TryGetValue((string)key, out child);
                }
            }
        }

        #region Cantcopy helper methods

        /// <summary>
        /// Removes a recently created reference
        /// </summary>
        /// <remarks>
        /// This must be done when a uncopied object has been copied, i.e.
        /// we roll back.
        /// </remarks>
        private void PruneRefs(WritableReference wr, RefDictionary ref_dict)
        {
            if (Owns(wr) && wr.RefCount > 0)
            {
                wr.RefCount--;
                if (wr.RefCount == 0)
                {
                    _resources.Remove(wr.Id);

                    //We remove the reference from the ref_dict since we don't want
                    //this reference to be reused since it's been deregistered from
                    //the tracker.
                    RemoveValue(ref_dict, wr);
                }

                //Debug.Assert(wr.Deref() == null, "These references must always be null");
            }
        }

        /// <summary>
        /// Removes recently created references in an array
        /// </summary>
        /// <remarks>
        /// This must be done when a uncopied object has been copied, i.e.
        /// we roll back.
        /// </remarks>
        private void PruneRefs(PdfItem[] ar, RefDictionary ref_dict)
        {
            Monitor.Enter(ar);
            bool has_lock = true;

            try
            {
                for (int c = 0; c < ar.Length; c++)
                {
                    PdfItem child = ar[c];

                    if (child is WritableReference)
                    {
                        //We can assume the "ar[]" is unique and will not be called to be pruned twice.
                        // (If that were to happen, refcount would be wrong.
                        var wr = (WritableReference)child;
                        if (Owns(wr))
                        {
                            //Note "wr.RefCount > 0" serves as a protection against circular references
                            if (wr.RefCount > 0)
                            {
                                wr.RefCount--;

                                if (wr.RefCount == 0)
                                {
                                    _resources.Remove(wr.Id);

                                    //We remove the reference from the ref_dict since we don't want
                                    //this reference to be reused since it's been deregistered from
                                    //the tracker.
                                    RemoveValue(ref_dict, wr);

                                    var rw_child = wr.Deref();
                                    if (rw_child is ICRef)
                                    {
                                        Monitor.Exit(ar);
                                        has_lock = false;

                                        PruneRefs((ICRef)rw_child, ref_dict);

                                        Monitor.Enter(ar);
                                        has_lock = true;
                                    }
                                }
                            }
                        }
                    }
                    else if (child is ICRef)
                    {
                        //Children's references must also be removed
                        Monitor.Exit(ar);
                        has_lock = false;

                        PruneRefs((ICRef)child, ref_dict);

                        Monitor.Enter(ar);
                        has_lock = true;
                    }
                }
            } finally { if (has_lock) Monitor.Exit(ar); }
        }

        /// <summary>
        /// Removes recently created references in an catalog
        /// </summary>
        /// <remarks>
        /// This must be done when a uncopied object has been copied, i.e.
        /// we roll back.
        /// </remarks>
        private void PruneRefs(Catalog cat, RefDictionary ref_dict)
        {
            foreach (var kp in cat)
            {
                var child = kp.Value;
                if (child is WritableReference)
                {
                    //We can assume the "ar[]" is unique and will not be called to be pruned twice.
                    // (If that were to happen, refcount would be wrong.
                    var wr = (WritableReference)child;
                    if (Owns(wr))
                    {
                        //Note "wr.RefCount > 0" serves as a protection against circular references
                        if (wr.RefCount > 0)
                        {
                            wr.RefCount--;

                            if (wr.RefCount == 0)
                            {
                                _resources.Remove(wr.Id);

                                //We remove the reference from the ref_dict since we don't want
                                //this reference to be reused since it's been deregistered from
                                //the tracker.
                                RemoveValue(ref_dict, wr);

                                var rw_child = wr.Deref();
                                if (rw_child is ICRef)
                                    PruneRefs((ICRef)rw_child, ref_dict);
                            }
                        }
                    }
                }
                else if (child is ICRef)
                {
                    //Children's references must also be removed
                    PruneRefs((ICRef)child, ref_dict);
                }
            }
        }

        /// <summary>
        /// Removes recently created references in an ICRef
        /// </summary>
        /// <remarks>
        /// This must be done when a uncopied object has been copied, i.e.
        /// we roll back.
        /// </remarks>
        private void PruneRefs(ICRef icref, RefDictionary ref_dict)
        {
            var children = icref.GetChildren();
            if (children is PdfItem[])
                PruneRefs((PdfItem[])children, ref_dict);
            else
                PruneRefs((Catalog)children, ref_dict);
        }

        private void RemoveValue(RefDictionary ref_dict, PdfReference value)
        {
            foreach (var kp in ref_dict)
                if (ReferenceEquals(kp.Value, value))
                {
                    //Works fine since we don't enumerate any longer.
                    ref_dict.Remove(kp.Key);
                    break;
                }
        }

        private bool CantCopy(ICRef obj)
        {
            //We can assume that PageTree, PdfCatalog, and PdfTrailer will never get her in
            //raw form
            if (obj is Pdf.PdfPages || obj is PageTree || obj is PdfCatalog || obj is PdfTrailer || obj is PdfPage)
                return true;
            if (obj is PdfDictionary)
            {
                var type = ((PdfDictionary) obj).GetName("Type");
                return type == "Page" || type == "Pages";
            }
            return false;
        }

        #endregion

        /// <summary>
        /// Itterates a ICRef object
        /// </summary>
        private IEnumerable<KeyValuePair<object, PdfItem>> ItterateItem(ICRef icref)
        {
            var children = icref.GetChildren();
            if (children is PdfItem[])
            {
                var ar = (PdfItem[]) children;
                for(int c=0; c < ar.Length; c++)
                    yield return new KeyValuePair<object, PdfItem>(c , ar[c]);
            }
            else
                foreach (var kp in ((Catalog)children))
                    yield return new KeyValuePair<object, PdfItem>(kp.Key , kp.Value);
        }

        private Stack<T> CloneStack<T>(Stack<T> stack) 
        {
            var ar = stack.ToArray();
            Array.Reverse(ar);
            return new Stack<T>(ar); 
        }

        /// <summary>
        /// Stores all data needed to resume execution
        /// </summary>
        class ResumeFrom
        {
            /// <summary>
            /// The catalog or array being constructed
            /// </summary>
            public object Data;

            /// <summary>
            /// Position in the catalog or array
            /// </summary>
            public object From;

            /// <summary>
            /// Reference to update with a value
            /// </summary>
            public TempWRef WRef;

            public ResumeFrom(object data, object from, TempWRef wref)
            { Data = data; From = from; WRef = wref; }

            /// <summary>
            /// The new item or null if it isn't set.
            /// </summary>
            public PdfItem Item
            {
                get
                {
                    PdfItem value = ((From is int) ? ((PdfItem[])Data)[(int)From] : ((Catalog)Data)[(string) Key]);
                    if (value is PdfReference) return value.Deref();
                    return value; //Note, value can be nill
                }
                set
                {
                    if (WRef != null)
                    {
                        WRef.SetValue(value);
                    }
                    else
                    {
                        if (From is int)
                            ((PdfItem[])Data)[(int)From] = value;
                        else
                        {
                            var key = ((IEnumerator<KeyValuePair<string, PdfItem>>)From).Current.Key;

                            //Implementation detail: 
                            // The enumerator is not actually enumerating the
                            // catalog we're updating.
                            ((Catalog)Data)[key] = value;
                        }
                    }
                }
            }
            public object Key
            {
                get
                {
                    return (From is int) ? From :
                        ((IEnumerator<KeyValuePair<string, PdfItem>>)From).Current.Key;
                }
            }
        }

        /// <summary>
        /// Transparently handle retargeting
        /// </summary>
        public class RetargetObject
        {
            #region variables and properties

            readonly PdfType _type;

            readonly object _container;
            readonly object _key;
            readonly PdfItem _org_item;
            //readonly TempWRef _ref;
            readonly ResTracker _tracker;

            /// <summary>
            /// the type of object being retargeted
            /// </summary>
            public PdfType Type { get { return _type; } }

            /// <summary>
            /// Get/set the value currently targeted
            /// </summary>
            public PdfItem Value
            {
                get
                {
                    //if (_ref != null) return _ref.Deref();
                    if (_key is int)
                        return ((PdfItem[])_container)[(int)_key];
                    else
                        return ((Catalog)_container)[(string)_key];
                }
                set
                {
                    //if (_ref != null) _ref.SetValue(value);
                    //else
                    {
                        Debug.Assert(value is PdfNull || (value is IRef) && ((IRef)value).HasReference || value is WritableReference);
                        var val = Value;
                        if (val is WritableReference)
                            _tracker.DecRefCount((WritableReference)val);
                        val = value;
                        if (value is IRef)
                        {
                            var iref = ((IRef)value);
                            if (iref.HasReference)
                            {
                                val = iref.Reference;
                                _tracker.IncRefCount((WritableReference)val);
                            }
                        }
                        else if (val is WritableReference)
                            _tracker.IncRefCount((WritableReference)val);
                        if (_key is int)
                            ((PdfItem[])_container)[(int)_key] = val;
                        else
                            ((Catalog)_container)[(string)_key] = val;
                    }
                }
            }

            /// <summary>
            /// Lookup item
            /// </summary>
            public PdfItem Index 
            { 
                get { return _org_item; } 
            }

            #endregion

            #region Init

            /// <summary>
            /// Convinience constructor that takes a ResumeFrom object instead
            /// </summary>
            internal RetargetObject(PdfItem org_item, object data, Dictionary<PdfReference, PdfReference> ref_dict, ResTracker owner)
                : this(org_item, (ResumeFrom) data, (RefDictionary) ref_dict, owner)
            { }

            /// <summary>
            /// Convinience constructor that takes a ResumeFrom object instead
            /// </summary>
            private RetargetObject(PdfItem org_item, ResumeFrom data, RefDictionary ref_dict, ResTracker owner)
                : this(org_item, data.Data, data.Key, owner)
            {
                if (data.WRef != null)
                    owner.RemoveValue(ref_dict, data.WRef);
                //Todo:
                //
                //consider implementing retarget references.
                //These reference would take advantage of the fact that a reference is
                //decided by its "ID", and nothing else. So a retarget reference is one
                //that can update it's id, but not its value.
                //
                //Just inherit from WritableReference and overrider the "Write" to
                //fetch the "SaveString" from the xref table. 
            }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="org_object">Org object is needed for recognition</param>
            internal RetargetObject(PdfItem org_object, object parent_container, object key, /*TempWRef optional_ref,*/ ResTracker owner)
            {
                //Hopefully good enough, but not foolproof, determening of type
                var type = org_object.Type;
                if (type == PdfType.Dictionary)
                {
                    var dict = (PdfDictionary)org_object.Deref();
                    var t = dict.GetName("Type");
                    if (t == null)
                    {
                        //We make a guess
                        _type = dict.Contains("Contents") ? PdfType.Page : PdfType.Pages;
                    }
                    else
                    {
                        if (t == "Pages") _type = PdfType.Pages;
                        else if (t == "Page") _type = PdfType.Page;
                        else _type = dict.Contains("Contents") ? PdfType.Page : PdfType.Pages;
                    }
                }
                else if (type == PdfType.Array)
                    _type = PdfType.PageTree;

                if (parent_container is ICRef)
                    parent_container = ((ICRef)parent_container).GetChildren();

                _container = parent_container;
                _key = key;
                //_ref = optional_ref;
                _tracker = owner;
                _org_item = org_object;

                //By default the value is nulled
                Value = PdfNull.Value;
            }

            #endregion

            public override int GetHashCode()
            {
                return _org_item.GetHashCode();
            }
        }

        /// <summary>
        /// Used when checking objects in the retarget rutine
        /// </summary>
        class RetargetState
        {
            public readonly ICRef Item; //<-- Currently not used and can be null

            public CheckStatus Status;

            public RetargetState(ICRef item)
            { Item = item; }

            public RetargetState(ICRef item, CheckStatus status)
            { Item = item; Status = status; }

            /// <summary>
            /// Note, just being in the cache means the object has been checked
            /// and at least determined if it's illigal. 
            /// </summary>
            public enum CheckStatus
            {
                /// <summary>
                /// The object's status is yet to be determined
                /// </summary>
                Undetermined,

                /// <summary>
                /// This object is safe to drop for the retarget state cache
                /// </summary>
                Clean,

                /// <summary>
                /// The object was modified
                /// </summary>
                Modified,

                /// <summary>
                /// This object must be dropped
                /// </summary>
                Illigal
            }
        }

        /// <summary>
        /// Just so that one can clone the dictionary
        /// </summary>
        class RefDictionary : Dictionary<PdfReference, PdfReference>, ICloneable
        {
            public RefDictionary()
                : base() { }

            /// <summary>
            /// Copy constructor
            /// </summary>
            /// <remarks>Only clones PdfObjects</remarks>
            public RefDictionary(RefDictionary cat)
            {
                foreach (var kp in cat)
                    Add(kp.Key, kp.Value);
            }

#if DEBUG

            public new void Add(PdfReference r1, PdfReference r2)
            {
                //if (r1.Id.Nr == 139)
                //    Console.WriteLine("139");
                base.Add(r1, r2);
            }
#endif

            public RefDictionary(int capacity) : base(capacity) { }

            object ICloneable.Clone() { return new RefDictionary(this); }
            public RefDictionary Clone() { return new RefDictionary(this); }
        }

        class Placeholder
        {
            /// <summary>
            /// Note that this is the new parent's array.
            /// </summary>
            public object ParentData;

            /// <summary>
            /// The field we're to place data in
            /// </summary>
            public object Field;

            /// <summary>
            /// Whenever the original field was a reference
            /// </summary>
            public bool FieldWasRef;

            /// <summary>
            /// The problematic child
            /// </summary>
            public ICRef Org_child_ref;

            /// <summary>
            /// Current data stack
            /// </summary>
            public ResumeFrom[] DataStack;

            /// <summary>
            /// Current check stack
            /// </summary>
            public ICRef[] CheckStack;

            /// <summary>
            /// Key in dictionary we're fetching from
            /// </summary>
            public string FromKey;

            public ResumeFrom ResumeObject
            {
                get
                {
                    //Walks the checkstack and finds the position of the
                    //original object.
                    int opos = 0;
                    while (true)
                    {
                        if (opos == CheckStack.Length)
                            throw new PdfInternalException("Bug in ResTracker.MakeCopy");
                        if (ReferenceEquals(CheckStack[opos++], Org_child_ref)) break;
                    }

                    if (opos < DataStack.Length)
                        return DataStack[opos];
                    return null;
                }
            }

            public Placeholder(object p, ICRef o, object f, Stack<ResumeFrom> ds, Stack<ICRef> cs, bool was_ref)
            {
                ParentData = p;
                Org_child_ref = o;
                Field = f;
                DataStack = ds.ToArray();
                CheckStack = cs.ToArray();
                FieldWasRef = was_ref;

                //Problem: We need to know the key to the other dictionary, but
                //that key will be lost so we have to figure it out and save it.
                ResumeFrom rf = ResumeObject;
                if (ResumeObject != null)
                    if (!(rf.From is int))
                        FromKey = rf.Key.ToString();
            }
        }

        /// <summary>
        /// Items that are adopted sometimes are handeled later
        /// to avoid circular references. When a problematic
        /// item is encountered they are poushed on a list and
        /// handeled later.
        /// 
        /// However when one then get around to handeling an item
        /// pushed on this list one need to know where to put
        /// the finished item. This class contain all information
        /// needed for that.
        /// </summary>
        private abstract class AdoptionItem : PdfObject
        {
            /// <summary>
            /// The item that is to be potentially adopted or copied
            /// </summary>
            public readonly PdfItem Item;

            /// <summary>
            /// Constructs a temporary adoption item
            /// </summary>
            /// <param name="item">The item that will later be excamined</param>
            public AdoptionItem(PdfItem item) { Item = item; }

            /// <summary>
            /// Updates the container containing the item with a different item
            /// </summary>
            /// <param name="item">The new item</param>
            public abstract void Set(PdfItem item);

            #region Required overrides
            internal override PdfType Type { get { return PdfType.None; } }
            internal override void Write(PdfWriter write)
            { throw new NotSupportedException(); }
            #endregion
        }

        /// <summary>
        /// References are not followed during adoption until it's
        /// save to do so. The references itself is converted to
        /// one owned by this tracker, but the item it's pointing
        /// at is taken ownership of later.
        /// </summary>
        private class AdoptReference : AdoptionItem
        {
            public AdoptReference(TempWRef new_r)
                : base(new_r) { }
            public override void Set(PdfItem item)
            {
                ((TempWRef)Item).SetValue(item);
            }
        }

        /// <summary>
        /// When an item that needs a closer look is from a
        /// dictionary they are packed into this class along
        /// with the dictionary where the evt. copied item
        /// is to be placed back for updating.
        /// </summary>
        private class DictItem : AdoptionItem
        {
            private readonly string _key;
            private readonly Catalog _cat;
            public DictItem(PdfItem item, string key, Catalog cat)
                : base(item)
            { _key = key; _cat = cat; }
            public override void Set(PdfItem item)
            {
                _cat.Add(_key, item);
            }
        }

        /// <summary>
        /// When an item that needs a closer look is from an
        /// array they are packed into this class along
        /// with the array where the evt. copied item
        /// is to be placed back for updating.
        /// </summary>
        private class ArrayItem : AdoptionItem
        {
            private readonly int _pos;
            private readonly PdfItem[] _cat;
            public ArrayItem(PdfItem item, int index, PdfItem[] cat)
                : base(item)
            { _pos = index; _cat = cat; }
            public override void Set(PdfItem item)
            {
                _cat[_pos] = item;
            }
        }

        #endregion

        #region Save methods

        internal XRefTable[] CreateChunckedXRef(XRefState state, List<PdfObjID> id_list)
        {
            #region Step. 1 find all new resources



            #endregion

            #region Step. 2 Remove those without savestring

            //SortedList<int, WritableReference> references = new SortedList<int, WritableReference>(xref_renum.Count);
            //foreach (var wr in xref_renum)
            //{
            //    if (wr.SaveString == null)
            //        compressed_list.Remove(wr);
            //    else
            //    {
            //        int id = WritableReference.GetId(wr.SaveString);
            //        references.Add(id, wr);
            //    }
            //}

            #endregion

            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a partial XRef table
        /// </summary>
        /// <param name="state">State information used for creating the table</param>
        /// <returns>The partial table</returns>
        /// <remarks>
        /// Note that this function does not leave the first entery as "null". This
        /// since partial tables need not have a reserved entery.
        /// </remarks>
        internal XRefTable CreatePartialXRef(XRefState state)
        {
            #region Step. 1 find all new resources

            //We ignore all resources in the old_resources table
            var old_resources = state.Resources;

            //Objects that must be renumbered is put in this list. These
            //objects need not be in the xref_list since it is for refs
            //that may have to be renumbered
            var xref_renum = new List<WritableReference>();

            //Some of those objects in the xref list may be compressed,
            //those objects are also put here (is in both list IOW)
            var compressed_list = new List<WritableReference>();

            //Itterates though the resources, finding new ones.
            foreach (var kp in _resources)
            {
                if (!old_resources.Contains(kp.Key))
                {
                    old_resources.Add(kp.Key);

                    var xref = kp.Value;
                    switch (xref.SaveMode)
                    {
                        case SM.Auto:
                        case SM.Compressed:
                            if (state.SaveMode == SaveMode.Compressed)
                                compressed_list.Add(xref);
                            goto default;
                        case SM.Ignore:
                        case SM.Direct:
                            continue;
                        default:
                            //We renumber all references. It simplifies the implementation.
                            xref_renum.Add(xref);
                            continue;
                    }
                }
            }

            #endregion

            #region Step. 2 Create stream objects
            //Comp_obj_list contains the list of references that
            //will be compressed. Used to replace these references
            //with index references.
            var comp_obj_list = new List<WritableReference>();
            const int MAX_NR_OF_OBJ_IN_STREAM = 64;

            //Creates compressed streams. For now it creates one big uncompressed stream.
            if (compressed_list.Count > 0)
            {
                //Impl. detail: Compressed object with more than 65000 items
                //              is not supported. (With X X 2 format)
                //              This since indexes are kept in ushort vars
                //              However putting 65000 objects into a stream
                //              is probably not a good idea anyway.
                int i = 0, end = 0;
                while (i < compressed_list.Count)
                {
                    //Adds as many objects to a object stream as it can.
                    //This isn't a particulary smart way of doing it, also
                    //the specs recomend against putting too many objects
                    //in a stream.
                    //
                    //Using MAX_GEN_NR as a sensible limit to the number
                    //of objects. 
                    int max = Math.Min(MAX_NR_OF_OBJ_IN_STREAM, compressed_list.Count - i);

                    //Increases the size of the XRef table if needed. 
                    state.higest_gen_nr = Math.Max(state.higest_gen_nr, max);

                    end += max;
                    var wos = new WritableObjStream();
                    for (; i < end; i++)
                        wos.Add(compressed_list[i]);

                    //WritableObjStream does not really need to be a "PdfObject",
                    //but this way we slip it into the existing code.
                    var woref = new WritableReference(this, PdfObjID.Null, wos);
                    ((IRef)wos).Reference = woref;
                    xref_renum.Add(woref);
                    comp_obj_list.Add(woref);
                }
            }

            #endregion

            #region Flush step

            if (state.flush)
            {
                xref_renum.AddRange(state.extrarefs);
            }

            #endregion

            #region Step. 3 Renumbers

            //We renumber all resources. There's no actual need to preserve ids
            //and it would complicate the code quite a bit as I would have to
            //keep track of free ids somehow.
            var xref_pos_ar = xref_renum.ToArray();
            var last_id = state.last_id;

            //Sets the strings that will be used for saving.
            //Note that this function also caps the generation numbers.
            // (But the id alone is unique, so the capping does not break anything)
            for (int c = 0; c < xref_pos_ar.Length; c++)
            {
                var xref = xref_pos_ar[c];
                if (xref != null)
                    xref.SaveString = Lexer.GetBytes(string.Format("{0} {1} R", last_id + c, Math.Min(MAX_GEN_NR, xref.GenerationNr)));
            }

            state.last_id += xref_pos_ar.Length;

            #endregion

            #region Step. 4 Switches references with "index references into object streams"
            //This is only relevant for XRefStreams

            //Replace references with index references. An index reference is
            //a reference into a compressed stream.
            for (int c = 0; c < comp_obj_list.Count; c++)
            {
                var woref = comp_obj_list[c];
                int id = WritableReference.GetId(woref.SaveString);

                //What happens is bluntly that a child will
                //reference a "type 2" reference in the Xref
                //table. This Type 2 reference is not a offset
                //to a position in the file, but a reference
                //to a Type 1 reference (that is a objstream)
                //and a index into that reference.
                var wos = ((WritableObjStream)woref.Deref()).Objects;

                for (int i = 0; i < wos.Count; i++)
                {
                    var child = wos[i];
                    int child_id = WritableReference.GetId(child.SaveString);

                    //I don't want to create a new object for this, so
                    //making creative use of WritableReference and
                    //PdfObjID. Note that owner is null. That's how
                    //"IndexedReference" is detected later.
                    //Holds the id to the ObjStream and the index into it.
                    var index_ref = new WritableReference(null, new PdfObjID(id, (ushort)i), null);

                    //We place this new reference in place of the original.
                    //The PdfObjID is not used during saving so it matters not
                    //that this replaced reference has a different one than the
                    //original.

                    //But we do store away the original id in the refcount to aid
                    //the table sorting algorithm
                    index_ref.RefCount = child_id;
                    
                    //Note that while compressed objects are in the xref table,
                    //they are not written to the outer PDF file. When spotting
                    //and index ref the write algorithm will simply ignore it.
                    xref_pos_ar[child_id - last_id] = index_ref;
                }
            }

            #endregion

            #region Step. 5 Creates the object list

            XRefTable ret = new XRefTable();
            ret.CatalogPos = -1;
            ret.Table = xref_pos_ar;
            ret.Offsets = new long[xref_pos_ar.Length];
            state.size += xref_pos_ar.Length;

            return ret;

            #endregion
        }

        /// <summary>
        /// Creates the XRefStream table
        /// </summary>
        /// <param name="higest_gen_nr">Higest generation in the table</param>
        /// <param name="higest_id">Highest id in the table</param>
        /// <param name="optional_reference">Optional reference to add to the table, usualy the trailer.</param>
        /// <returns></returns>
        internal XRefTable CreateXRefStreamTable(out int higest_gen_nr, out int higest_id, WritableReference optional_reference, PM PaddMode)
        {
            #region Step 1. Create list of references
            //Turns the dictionary into lists over outer and
            //compressed objects

            //This list contains objects that needs an xref. 
            var xref_list = new List<WritableReference>(_resources.Count);

            //Some of those objects in the xref list may be compressed,
            //those objects are also put here (is in both list IOW)
            var compressed_list = new List<WritableReference>();

            //Objects that must be renumbered is put in this list. These
            //objects need not be in the xref_list since it is for refs
            //that may have to be renumbered
            var xref_renum = new List<WritableReference>();

            //The PdfCatalog gets special treatment. This so that one can
            //be able to update it before saving. It's not a terribly
            //important feature, but here it is.
            WritableReference cat = null;
            bool cat_compressed = false;
            int cat_index = -1;

            //Collected so to figure out the needed size of the stream table.
            //This is a bit of a wasted effort as gen numbers are capped to
            //255.
            higest_gen_nr = 0;

            foreach (var kp in _resources)
            {
                var xref = kp.Value;
                if (xref.RefCount > 0)
                {
                    //More special treatment for the catalog. Basically it picks the catalog
                    //out of the resources so that later code can deal with it as needed.
                    // (Basically later code will put the catalog as the last object.)
                    if (xref.Type == PdfType.Catalog)
                    {
                        if (cat != null) throw new PdfInternalException("Two catalogs?");
                        cat = xref;
                        if (xref.SaveMode == SM.Compressed && xref.GenerationNr == 0)
                        {
                            cat_compressed = true;
                            xref_renum.Add(cat);
                        }

                        continue;
                    }

                    switch (xref.SaveMode)
                    {
                        case SM.Auto:
                            //When refcount is 1, this is treated as a direct object
                            if (xref.RefCount == 1)
                            {
                                //Must be set null in case it has been set before.
                                //References without savestring will in any case
                                //automatically save themselves into the item they're
                                //in.
                                xref.SaveString = null;
                                continue;
                            }
                            goto case SM.Compressed;

                        case SM.Compressed:
                            compressed_list.Add(xref);

                            //Compressed items must have a gen of zero (as that number is
                            //used for the object id)
                            if (xref.GenerationNr != 0)
                                xref_renum.Add(xref);
                            else
                                goto default;
                            continue;
                        case SM.Direct:
                            //Debug.Assert(false, "Tried to save direct object as indirect");
                            xref.SaveString = null;
                            continue;
                        case SM.Ignore:
                            //Some objects are never to be saved to the stream.
                            continue;

                        default:
                            Debug.Assert(xref.SaveMode != SM.Unknown);
                            if (xref.GenerationNr > MAX_GEN_NR)
                            {
                                //Since PdfLib ignores gen numbers alltogetehr there's
                                //nothing that really needs to be done here. Gen numbers 
                                //that are too large are simply capped. Tada.
                                //
                                //This works fine since the id of an object consists of
                                //the gen numer and the id. I.e. since PdfLib assigns
                                //unique ids the gen number is meaningless  (and might as
                                //well be zeroed). 
                            }
                            higest_gen_nr = Math.Max(higest_gen_nr, xref.GenerationNr);
                            xref_list.Add(xref);
                            continue;
                    }
                }
            }

            //Adds additional references. (For now I'm only interested in adding the trailer)
            //I.O.W. here's the spot to add resources that for one reason or another couldn't
            //be placed in the resource table.
            if (optional_reference != null) xref_renum.Add(optional_reference);

            #endregion

            #region Step 2. Create stream objects
            //Comp_obj_list contains the list of references that
            //will be compressed. Used to replace these references
            //with index references.
            var comp_obj_list = new List<WritableReference>();

            //More special treatment for the catalog. Now adding
            //compressed catalogs to the compressed objects.
            if (cat_compressed)
            {
                compressed_list.Add(cat);
                cat_index = compressed_list.Count;
            }

            //Creates compressed streams. Now ideally objects that belong
            //togehter should be packed into the same stream. This isn't
            //done, this algorithem just adds objects until it hits the size
            //limit. Definitly room for improvment here.
            //
            //It's also possible to link togheter object streams into one
            //larger stream. However I do not see any purpose in doing that
            //unless one is appending data to an existing document.
            if (compressed_list.Count > 0)
            {
                //Impl. detail: Compressed object with more than 65000 items
                //              is not supported.
                //              This since indexes are kept in ushort vars
                //              However putting 65000 objects into a stream
                //              is probably not a good idea anyway.
                int i = 0, end = 0;
                while (i < compressed_list.Count)
                {
                    //Adds as many objects to a object stream as it can.
                    //This isn't a particulary smart way of doing it, also
                    //the specs recomend against putting too many objects
                    //in a stream.
                    //
                    //Using MAX_GEN_NR as a sensible limit to the number
                    //of objects.
                    //
                    //With more than 255 objects I see dimminishing returns
                    //on filesize. On a typical 2MB file one gain 6-10KB by 
                    //putting all objects in a single object stream
                    int max = Math.Min(MAX_GEN_NR, compressed_list.Count - i);

                    //Increases the size of the XRef table if needed. 
                    higest_gen_nr = Math.Max(higest_gen_nr, max);

                    end += max;
                    var wos = new WritableObjStream();
                    for (; i < end; i++)
                        wos.Add(compressed_list[i]);

                    //WritableObjStream does not really need to be a "PdfObject",
                    //but this way we slip it into the existing code.
                    var woref = new WritableReference(this, PdfObjID.Null, wos);
                    ((IRef)wos).Reference = woref;
                    xref_renum.Add(woref);
                    comp_obj_list.Add(woref);

                    //Records the position of the catalog so that it can be found
                    //and updated later.
                    if (cat_compressed && i == cat_index)
                    {   //For compressed catalogs the cat is of type "ObjStream"
                        wos.CatalogIndex = wos.Objects.Count - 1;
                    }
                }
            }

            #endregion

            #region Step 3. Renumbers
            xref_list.Sort();

            //Gets the last item in the list since it has the
            //higest object number. Then adds xref_renum.Count
            //to make space for the compressed streams
            int last_id;
            if (xref_list.Count > 0)
                last_id = xref_list[xref_list.Count - 1].ObjectNr;
            else last_id = 0;

            //Need +1 for the catalog, and +1 on count for the zero pos
            int pos_array_size = Math.Max(xref_list.Count + xref_renum.Count +
                ((cat_compressed) ? 0 : 1), last_id + xref_renum.Count) + 1;

            //Sanity check. Must use another datastructure if ids get into the millions.
            //Here we simply use an array with one to one mapping to the id. 
            if (pos_array_size > 1000000)
                throw new PdfInternalException("Can't renumber ids over 1,000,000");

            var xref_pos_ar = new WritableReference[pos_array_size];
            for (int c = 0; c < xref_list.Count; c++)
            {
                var wref = xref_list[c];
                int id = wref.Id.Nr;

                //Tries to put the ref at it's desired location.
                if (xref_pos_ar[id] == null)
                    xref_pos_ar[id] = wref;
                else //But if it can't it will have to be renumbered
                    xref_renum.Add(wref);

                //Implementation note: Since id starts counting from 1, the first
                //position will always be left alone
            }

            Renumber(xref_pos_ar, xref_renum);

            if (PaddMode != PM.Verbose || cat_compressed && xref_pos_ar[xref_pos_ar.Length - 1] != null)
            {   //Todo: Test/Fix this:
                //Important: Not doing this can/will expose bugs
                //           in the current handeling of subsections
                //           and in the assumption that ids are never
                //           bigger than the length of the file.
                Compact(xref_pos_ar);
            }

            //Puts the catalog last
            int cat_pos = -1;
            if (!cat_compressed)
            {
                Debug.Assert(xref_pos_ar[xref_pos_ar.Length - 1] == null);
                if (cat.GenerationNr > MAX_GEN_NR)
                {
                    //Nothing needs to be done as the Generation numbers are
                    //simply capped later.
                }
                
                higest_gen_nr = Math.Max(higest_gen_nr, cat.GenerationNr);
                cat_pos = ArrayHelper.LastIndexOfNotNull(xref_pos_ar) + 1;
                xref_pos_ar[cat_pos] = cat;
            }

            //Sets the strings that will be used for saving.
            //Note that this function also caps the generation numbers.
            SetStringsCap(xref_pos_ar);

            //Replace references with index references. An index reference is
            //a reference into a compressed stream.
            for (int c = 0; c < comp_obj_list.Count; c++)
            {
                var woref = comp_obj_list[c];
                int id = WritableReference.GetId(woref.SaveString);

                //What happens is bluntly that a child will
                //reference a "type 2" reference in the Xref
                //table. This Type 2 reference is not a offset
                //to a position in the file, but a reference
                //to a Type 1 reference (that is a objstream)
                //and a index into that reference.
                var wos = ((WritableObjStream)woref.Deref()).Objects;

                for (int i = 0; i < wos.Count; i++)
                {
                    var child = wos[i];
                    int child_id = WritableReference.GetId(child.SaveString);

                    //I don't want to create a new object for this, so
                    //making creative use of WritableReference and
                    //PdfObjID. Note that owner is null. That's how
                    //"IndexedReference" is detected later.
                    //Holds the id to the ObjStream and the index into it.
                    var index_ref = new WritableReference(null, new PdfObjID(id, (ushort)i), null);

                    //We place this new reference in place of the original.
                    //The PdfObjID is not used during saving so it matters not
                    //that this replaced reference has a different one than the
                    //original.

                    //Note that while compressed objects are in the xref table,
                    //they are not written to the outer PDF file. When spotting
                    //and index ref the write algorithm will simply ignore it.
                    xref_pos_ar[child_id] = index_ref;
                }
            }

            XRefTable ret = new XRefTable();
            ret.CatalogPos = cat_pos; //Will be -1 for compressed catalogs.
            ret.Table = xref_pos_ar;
            ret.Offsets = new long[xref_pos_ar.Length];
            higest_id = xref_pos_ar.Length; //<-- Not correct but never smaller.

            #endregion

            return ret;
        }

        internal XRefTable CreateXRefTable(PM PaddMode)
        {
            #region Step 1. Create list of references
            //Turns the dictionary into a list
            var xref_list = new List<WritableReference>(_resources.Count);
            WritableReference cat = null;
            foreach (var kp in _resources)
            {
                var xref = kp.Value;
                if (xref.RefCount > 0)
                {
                    if (xref.Type == PdfType.Catalog)
                    {
                        if (cat != null) throw new PdfInternalException("Two catalogs?");
                        cat = xref;

                        continue;
                    }

                    switch (xref.SaveMode)
                    {
                        case SM.Auto:
                            if (xref.RefCount > 1)
                                goto default;
                            else
                                xref.SaveString = null;
                            break;
                        case SM.Direct:
                            xref.SaveString = null;
                            break;
                        case SM.Ignore:
                            break;
                        default:
                            xref_list.Add(xref);
                            break;
                    }
                }
            }
            #endregion

            #region Step 2. Renumbers

            xref_list.Sort();

            //Gets the last item in the list since it has
            //the higest object number
            int last_id;
            if (xref_list.Count > 0)
                last_id = xref_list[xref_list.Count - 1].ObjectNr;
            else last_id = 0;

            //Need +1 for the catalog, and +1 on count for the zero pos
            int pos_array_size = Math.Max(xref_list.Count + 1, last_id) + 1;

            //Sanity check. Must use another datastructure if ids get into the millions.
            if (pos_array_size > 1000000) 
                throw new PdfInternalException("Can't renumber ids over 1,000,000");

            var xref_pos_ar = new WritableReference[pos_array_size];
            var xref_renum = new List<WritableReference>();
            for (int c = 0; c < xref_list.Count; c++)
            {
                var wref = xref_list[c];
                int id = wref.Id.Nr;

                //Tries to put the ref at it's desired location.
                if (xref_pos_ar[id] == null)
                    xref_pos_ar[id] = wref;
                else //But if it can't it will have to be renumbered
                    xref_renum.Add(wref);
            }

            Renumber(xref_pos_ar, xref_renum);

            if (PaddMode != PM.Verbose || xref_pos_ar[xref_pos_ar.Length-1] != null)
            {
                Compact(xref_pos_ar);
            }

            //Puts the catalog last
            Debug.Assert(xref_pos_ar[xref_pos_ar.Length - 1] == null && cat != null);
            int cat_pos = ArrayHelper.LastIndexOfNotNull(xref_pos_ar) + 1;
            xref_pos_ar[cat_pos] = cat;

            SetStrings(xref_pos_ar);

            #endregion

            XRefTable ret = new XRefTable();
            ret.CatalogPos = cat_pos;
            ret.Table = xref_pos_ar;
            ret.Offsets = new long[xref_pos_ar.Length];

            return ret;
        }

        /// <summary>
        /// Renumbers references by finding some place for them in
        /// the position array. This assumes that the possition array
        /// is big enough to hold all references.
        /// </summary>
        private int Renumber(WritableReference[] xref_pos_ar, List<WritableReference> xref_renum)
        {
            int next = 1; //<- id 0 is reserved.
            Debug.Assert(xref_pos_ar[0] == null);
            for (int c = 0; c < xref_renum.Count; c++)
            {
                var to_renum = xref_renum[c];
                while (xref_pos_ar[next] != null)
                    next++;
                xref_pos_ar[next] = to_renum;
            }
            return next;
        }

        /// <summary>
        /// Compacts the reference table
        /// </summary>
        private void Compact(WritableReference[] xref_pos_ar)
        {
            int move = 0;
            for (int c = 1; c < xref_pos_ar.Length; c++)
            {
                var xref = xref_pos_ar[c];
                if (xref == null) move++;
                else if (move != 0)
                    xref_pos_ar[c - move] = xref_pos_ar[c];
            }
            //Clears moved objects
            for (int c = xref_pos_ar.Length - move; c < xref_pos_ar.Length; c++)
                xref_pos_ar[c] = null;
        }

        /// <summary>
        /// Changes the dictionary into a list. Prunes unused references
        /// </summary>
        private void SetStrings(WritableReference[] xrefs)
        {
            for (int c = 1; c < xrefs.Length; c++)
            {
                var xref = xrefs[c];
                if (xref != null)
                    xref.SaveString = Lexer.GetBytes(string.Format("{0} {1} R", c, xref.GenerationNr));
            }
        }

        /// <summary>
        /// Changes the dictionary into a list. Prunes unused references
        /// and caps the gen numbers to MAX_GEN_NR
        /// </summary>
        /// <remarks>Capping gen numbers is harmless as PdfLib makes no use
        /// of them. It's done to make sure they'll fit the PdfStreamTable's
        /// [1 4 1] format. If gen numbers are to be supported then the 
        /// PdfStreamTable's format must be made big enough to hold the highest
        /// number
        /// 
        /// Bugfix: Force gen number to 0. This because indexed references must have gen 0, 
        ///         and this function is unaware of which reference is an index.
        ///         
        ///         Arguably this is better behavior anyway, as there are never
        ///         a previous trailer, so there are never references to overwrite.
        /// </remarks>
        private void SetStringsCap(WritableReference[] xrefs)
        {
            for (int c = 1; c < xrefs.Length; c++)
            {
                var xref = xrefs[c];
                if (xref != null)
                    xref.SaveString = Lexer.GetBytes(string.Format("{0} {1} R", c, 0/* Math.Min(MAX_GEN_NR, xref.GenerationNr)*/));
            }
        }

        #endregion

        #region Incement and decrement references

        /// <summary>
        /// Registers a object with respect to their store mode. Only intended
        /// for newly created objects that has the tracker as an owner already
        /// </summary>
        /// <param name="item">Item to register</param>
        /// <remarks>
        /// Just because an object has been registered with a reference does not
        /// mean that one can't ignore that and set it 
        /// </remarks>
        internal void Register(IKRef item)
        {
            if (!item.IsOwner(this))
                throw new PdfInternalException("Object not owned by tracker");

            switch (item.DefSaveMode)
            {
                case SM.Direct: return;
                case SM.Auto:
                case SM.Compressed:
                case SM.Indirect:
                    if (!item.HasReference)
                        item.Reference = CreateMyWRef((PdfItem)item);
                    return;
                default:
                    throw new PdfInternalException("Unkown save mode");
            }
        }

        /// <summary>
        /// Does reference counting (+1) on items. Will potentially
        /// make a copy of the item if it does not belong to
        /// this ResTracker
        /// </summary>
        /// <param name="itm">Item to refcount, may be replaced</param>
        /// <remarks>
        /// It would be nice to have some sort of item cache. While the 
        /// orphan mechanism solves situations like these:
        /// 
        ///  var cs = new ColorSpace();
        ///  image1.cs = cs;
        ///  image2.cs = cs;
        ///  
        /// (Here the ColorSpace will be adopted and we get the same
        /// colorspace on both images). If the same is done with a
        /// color space taken from another document, there will be two
        /// sepperate color spaces.
        /// 
        /// An item cache would let the tracker recognize those situation,
        /// however to be able to implement that, the ability to compare 
        /// objects is needed.
        /// 
        /// I.e.
        ///  image1.cs = cs; //cs owned by another document
        ///  cs.parameter = 2;
        ///  image2.cs = cs;
        ///  
        /// In this case a naive item cache would result in the parameter
        /// change not comming along. Though, the user probably expects
        /// the colorspace parameter to change for both images, but since
        /// a copy was made that is not happening for image1.
        /// 
        /// Todo: Better name on this function
        /// </remarks>
        internal void RefCountItem(ref PdfItem itm)
        {
            //First we find out what kind of item we're
            //dealing with.
            if (itm is PdfReference)
                RefCountWRef(ref itm);
            else if (itm is IKRef)
                RefCountKRef(ref itm);
            else if (itm is IEnumRef)
            {
                //Can't easily determine ownership with objects like these, so
                //assume that the object is immutable*. With that assumtion, it's
                //safe to leave be copying the object even if the owner isn't known.
                //
                //*Mutable objects are required to implement IKRef
                switch(OwnsDirectObject((IEnumRef)itm))
                {
                    case -1: //Don't own the object, so a copy must be made
                        itm = MakeCopy((ICRef)itm, true);
                        break;

                    case 1: //Owns the object, so inc refcount for child references
                        IncChildRefCounts((IEnumRef)itm);
                        break;

                    //Default. There's no references in the object. Since it's immutable
                    //one do not need to make a copy.
                }
            }
        }

        /// <summary>
        /// Whenever the tracker has anything in this item  it owns
        /// </summary>
        /// <param name="ienm">Item to check</param>
        /// <returns>True if it owns anything in the item</returns>
        internal bool OwnsEnumRef(IEnumRef ienm)
        {
            return OwnsDirectObject(ienm) == 1;
        }

        private int OwnsDirectObject(IEnumRef ienm)
        {
            foreach (IEnumRef reference in ienm.RefEnumerable)
            {
                //If the direct item contains a reference we can use it
                //to see if this tracker owns this object
                if (reference is PdfReference)
                {
                    if (reference is WritableReference && Owns((WritableReference)reference))
                    {
                        //We can assume that if one reference is owned, then all
                        //refereces is owned.
                        return 1;
                    }

                    //Any other type of refences must be copied
                    return -1;
                }

                var child_result = OwnsDirectObject(reference);
                if (child_result != 0)
                    return child_result;
            }

            return 0;
        }

        /// <summary>
        /// Increments the reference counter
        /// </summary>
        /// <remarks>
        /// This function is used when creating the XRef table and
        /// when fixing the res table. Other methods should use:
        /// RefCountItem
        /// 
        /// That method takes care of ownership, not just blind
        /// refcounting.
        /// </remarks>
        internal void IncRefCount(WritableReference wref)
        {
            if (wref.RefCount == int.MinValue)
            {
                //References with min value is assumed to
                //have it's children registered already.
                wref.RefCount = 1;
            }
            else
            {
                wref.RefCount++;

                //When a object gets added to the document (perhaps a user is moving
                //a page for instance) and it has a refcount of zero, one need to
                //not only increment this reference, but also all references within.

                if (wref.RefCount == 1)
                {
                    //Note that only the child references of this reference's
                    //value are to be incremented blindly. Child references of 
                    //child references are only to be incremented if they too are 
                    //zero.

                    if (wref.HasChildren)
                        IncChildRefCounts(wref);

                    //Registers the object again. This because the reference
                    //is removed from the table when refcount goes down to zero
                    if (!_resources.ContainsKey(wref.Id))
                        _resources[wref.Id] = wref;
                }
            }
        }

        /// <summary>
        /// Decrements the reference count
        /// </summary>
        /// <param name="itm"></param>
        internal void DecRefCount(PdfItem itm)
        {
            if (itm is PdfReference)
                DecRefCount((WritableReference)itm);
            else if (itm is IEnumRef)
                DecChildCount((IEnumRef)itm);
        }

        /// <summary>
        /// Convinience function that works like RefCountItem
        /// </summary>
        /// <param name="r">A reference</param>
        internal void RefCountRef(ref PdfReference r)
        {
            PdfItem itm = r;
            RefCountWRef(ref itm);
            r = (PdfReference)itm;
        }

        #region Repair functins

        /// <summary>
        /// Fetches the current reference's IDs
        /// </summary>
        /// <returns></returns>
        internal PdfObjID[] GetCurrentIDs()
        {
            var ret = new PdfObjID[_resources.Count];
            int c=0;
            foreach (var key in _resources.Keys)
                ret[c++] = key;
            return ret;
        }

        /// <summary>
        /// Fixes the resource table
        /// </summary>
        /// <param name="root">The root reference</param>
        /// <param name="keep">References to always keep</param>
        internal void FixResTable(WritableReference root,  PdfObjID[] keep)
        {
            //Sets all refcounts to zero
            NegateRefCounts();

            //Recounts
            IncRefCount((WritableReference)root);
            FixResTable((IEnumRef)root, new HashSet<IEnumRef>(), new HashSet<IEnumRef>(), new HashSet<PdfObjID>());

            //Force these references to stay. 
            if (keep != null)
            {
                for (int c = 0; c < keep.Length; c++)
                {
                    var wref = _resources[keep[c]];
                    if (wref.RefCount < 2)
                        wref.RefCount = 2;
                }
            }

            //Removes unused objects
            PruneNegativeRefs();

            //Resets the flag
            _must_fix_restable = false;
        }

        /// <summary>
        /// Fixes the resource table
        /// </summary>
        /// <param name="parent">The first reference</param>
        /// <param name="refs">Checked references</param>
        /// <param name="direct">Checked direct objects</param>
        /// <param name="indirect">Checked indirect objects</param>
        /// <remarks>
        /// While this fixes the refcounts errors that can normaly appear,
        /// it does not fix errors were references are wrongly removed
        /// from the resources. Those type of errors should never occur.
        /// 
        /// To fix such errors:
        ///  1. Check if the ref is in the _resources
        ///  2. If not, add it and set refcount to 1 regardles of what it is
        /// </remarks>
        private void FixResTable(IEnumRef parent, HashSet<IEnumRef> refs, HashSet<IEnumRef> direct, HashSet<PdfObjID> indirect)
        {
            foreach (var child in parent.RefEnumerable)
            {
                if (child is WritableReference)
                {
                    var wref = (WritableReference)child;
                    Debug.Assert(_resources.ContainsKey(wref.Id));
                    if (refs.Contains(child))
                        wref.RefCount++;
                    else
                    {
                        wref.RefCount = 1;
                        refs.Add(wref);

                        try
                        {
                            var dwref = wref.Deref();
                            if (dwref is IEnumRef)
                            {
                                if (!indirect.Contains(wref.Id))
                                {
                                    indirect.Add(wref.Id);
                                    FixResTable((IEnumRef)dwref, refs, new HashSet<IEnumRef>(), indirect);
                                }
                                else
                                {
                                    //Can this happen?
                                }
                            }
                        }
                        catch (PdfInternalException)
                        {
                            //Nothing can be done about missing objects
                            wref.SetNull();
                        }
                    }
                }
                else
                {
                    if (direct.Contains(child))
                        throw new PdfInternalException("Illigal circular reference detected");
                    else
                    {
                        direct.Add(child);
                        FixResTable(child, refs, direct, indirect);
                        direct.Remove(child);
                    }
                }
            }
        }

        /// <summary>
        /// Sets referecnes with positive refcount to zero
        /// </summary>
        /// <remarks>Used to prune data from the trailer</remarks>
        internal void ZeroRefcount()
        {
            foreach (var kp in _resources)
            {
                var r = kp.Value;
                if (r.RefCount > 0)
                {
                    //This assert should only happen if browsing the datastructure with the debugger
                    //before this function was called.
                    Debug.Assert(!r.HasValue);
                    r.RefCount = 0;
                }
            }
        }

        /// <summary>
        /// Sets all refcounts to a negative value
        /// </summary>
        /// <remarks>Used when repairing the xref table</remarks>
        internal void NegateRefCounts()
        {
            foreach (var kp in _resources)
                kp.Value.RefCount = int.MinValue;
        }

        /// <summary>
        /// Removes references with a int.min value.
        /// </summary>
        /// <remarks>Used when repairing the xref table</remarks>
        internal void PruneNegativeRefs()
        {
            List<PdfObjID> to_remove = new List<PdfObjID>();
            foreach (var kp in _resources)
                if (kp.Value.RefCount == int.MinValue)
                {
                    kp.Value.RefCount = 0;
                    to_remove.Add(kp.Key);
                }
            for (int c = 0; c < to_remove.Count; c++)
                _resources.Remove(to_remove[c]);
        }

        #endregion

        #region RefCount implementation methods

        /// <summary>
        /// Checks if this tracker possibly owns the object. Never gives false
        /// negatives, but may give false positives for imutable objects.
        /// </summary>
        /// <param name="elem">Elem to check</param>
        /// <returns>True if owns, false if it can't find out</returns>
        internal bool OwnsItem(PdfItem item)
        {
            if (item is IKRef)
            {
                //Mutable objects must be owned, no exceptions.
                return ((IKRef)item).IsOwner(this);
            }
            //Imutable objects are free to give "false positives"
            //Note, if a imutable has a reference that is owned by
            //another document, that's not a problem. 
            if (item is IRef && Owns((IRef)item))
                return true;
            if (item is WritableReference)
                return Owns((WritableReference)item);
            if (item is IEnumRef)
                return OwnsDirectObject((IEnumRef)item) != -1;
            return true;
        }

        /// <summary>
        /// Checks if this tracker possibly owns the object. Never gives false
        /// positives, but may give false negatives.
        /// </summary>
        /// <param name="elem">Elem to check</param>
        /// <returns>True if owns, false if it can't find out</returns>
        /// <remarks>
        /// This assumption works because:
        /// - If this item was attemted added to another document, the tracker
        ///   for that document will make a copy. This means that if we own the
        ///   reference, we also own the item.
        /// </remarks>
        private bool Owns(IRef elem)
        {
            return (elem.HasReference && elem.Reference.Tracker == this);
        }

        /// <summary>
        /// If this tracker owns this reference
        /// </summary>
        /// <param name="reference">Reference to check</param>
        /// <returns>True if owns, false of not</returns>
        internal bool Owns(WritableReference reference)
        {
            return (reference.Tracker == this);
        }

        /// <summary>
        /// Refcounts a "high level object"
        /// </summary>
        /// <param name="cref">The IKRef implementing object</param>
        private void RefCountKRef(ref PdfItem cref)
        {
            var ik = (IKRef)cref;
            if (!ik.IsOwner(this))
            {
                if (!ik.Adopt(this))
                {
                    //Adoption failed, so we make a copy.
                    cref = MakeCopy(ik, true);
                }
                else
                {
                    //Adoption handles refcounting of children
                }
            }
            else
            {
                //We own it, so child references must be incremented
                if (ik is IEnumRef)
                    IncChildRefCounts((IEnumRef)ik);
            }
        }

        /// <summary>
        /// Refcounts a reference
        /// </summary>
        /// <param name="wref">A writable reference that may be replaced</param>
        private void RefCountWRef(ref PdfItem wref)
        {
            //We deliberatly check for PdfReference instead
            //of WritableReference to cause a cast exceptions
            var wr = (WritableReference)wref;

            if (!this.Owns(wr))
            {
                //If the reference isn't owned by this tracker
                //a copy must be made.
                var child = wref.Deref();
                if (child is ICRef)
                {
                    if (child is IKRef)
                        RefCountKRef(ref child);
                    else
                        child = MakeCopy((ICRef)child, true);
                }

                //Then a new reference is created with a copy
                //of the value.
                wr = this.CreateMyWRef(child);

                //Implementation detail
                //CreateWRef creates references with a flag that
                //tells that this reference hasn't been refcounted
                //yet.
                wr.RefCount = 1;

                //Replaces the reference
                wref = wr;
            }
            else
            {
                IncRefCount(wr);
            }
        }

        /// <summary>
        /// Use this when setting a direct child or
        /// reference
        /// </summary>
        /// <remarks>
        /// For IRef objects are treated like direct items,
        /// use its supplied reference instead if that's
        /// not desired.
        /// </remarks>
        private void IncChildCount(PdfItem itm)
        {
            //A reference points at another object. Because of this
            //one only increase the ref count of the reference itself,
            //unless the reference's refcount is zero
            //
            //Note that when saving, and a reference has a refcount of
            //one, PdfLib will sometimes remove the reference. This
            //does not break refcounting however as while a direct
            //objects references should be incremented, in that special
            //case they should also be decremented as that ref's count
            //goes to zero.
            if (itm is PdfReference)
                //We deliberatly check for PdfReference instead
                //of WritableReference to cause a cast exceptions
                IncRefCount((WritableReference)itm);
            else if (itm is IEnumRef)
                //Any object that can contain references are required to
                //implement the IEnumRef interface. Since this is a
                //direct item we are required to increese the refcount of
                //those references.
                IncChildRefCounts((IEnumRef)itm);
        }

        /// <summary>
        /// Increment the refcount of the children of a reference
        /// </summary>
        /// <remarks>
        /// This method is also called when adding direct objects.
        /// Direct objects can have references, and they need to
        /// be counted.
        /// </remarks>
        private void IncChildRefCounts(IEnumRef refs)
        {
            foreach (var child in refs.RefEnumerable)
            {
                if (child is WritableReference)
                {
                    var reference = (WritableReference)child;

                    //Refcount first in case the child has a reference
                    //to the parent (i.e. this reference) so that this
                    //code won't follow it back.
                    IncRefCount(reference);
                }
                else
                {
                    //Direct objects can have child references too, so
                    //they need to be checked.
                    IncChildRefCounts(child);
                }
            }
        }

        private void DecRefCount(WritableReference wref)
        {
            //This may happen if one forgets to register an object
            Debug.Assert(wref.RefCount != int.MinValue);

#if DEBUG
            //if (wref.Id.Nr == 865)
            //{
            //    Debug.Assert(true);
            //}
#endif

            //Don't want references to dip into the negative
            if (wref.RefCount == 0) 
            { 
                Debug.Assert(false); 
                return; 
            }

            //Start with decrementing the count
            wref.RefCount--;

            //If the reference hits zero, it means this item is no longer
            //part of the document, and it's child references must be decremented.
            if (wref.RefCount == 0)
            {
                _resources.Remove(wref.Id);
                DecChildCount(wref);
            }
            else
            {
                var item = wref.Deref();
                if (item is ICRef)
                {
                    //Problem: Circular recursion
                    //
                    //References can point everywhere, and this causes
                    //problems that are hard to solve.
                    //
                    //Basically you can have a situation like this:
                    //
                    // A -> B -> C -|
                    //      ^ <-----/
                    //
                    // In this case the reference going to B has a refcount
                    // at 2. (A -> B, and C -> B) We remove the A -> B reference,
                    // and decrement the refcount by 1. This means the object B
                    // is no longer in use, yet at the same time, the refcount is
                    // 1. Meaning we have an orphaned object that will be written 
                    // out to the stream.
                    //
                    //Now it's possible to detect this particular situation and take
                    //height for it, but then you have:
                    //
                    // A => B -> C -|
                    // ^ <----------/
                    //
                    // Let's assume that A in this example holds two refs to
                    // object B, and that we again remove one ref from A to B
                    //
                    // In this case, B is not orphaned, but a quick detection
                    // algorithim might belive that to be the case. We'd have
                    // to add code to determine if other references in A goes to
                    // B.
                    //
                    //We can IOW handle this as well, but we can also keep making
                    //more and more complex datastructures to fool any quick
                    //loop check up.
                    //
                    //Bottom line: decrementing references is much harder than
                    //incrementing them. We therefore optimize for common cases
                    //and leave the rest to FixResTable
                    if (!_must_fix_restable)
                        //Looks four levels down for loops. 
                        _must_fix_restable = QuickLoopCheck((IEnumRef) item, new IEnumRef[4], 0);
                }
            }
        }

        /// <summary>
        /// Quickly checks references
        /// </summary>
        /// <param name="item">Non PdfReference item</param>
        /// <param name="checking">Limits the size of the search to one that fits in this array</param>
        /// <param name="depth">Always set this to 0</param>
        /// <returns>True if there may be loops, false if there's no loops</returns>
        private bool QuickLoopCheck(IEnumRef item, IEnumRef[] checking, int depth)
        {
            Debug.Assert(!(item is PdfReference), "Don't feed this function references");
            checking[depth++] = item;
            foreach (var en in item.RefEnumerable)
            {
                item = en;
                if (item is PdfReference)
                {
                    var target = ((PdfReference)en).Deref();
                    if (target is IEnumRef)
                        item = (IEnumRef)target;
                    else
                        continue;
                }

                //Technically there's no need to check "HasValue" on direct items
                //as well (i.e. this should be done inside the "if (item is PdfReference)"), 
                //but doing it on direct items just in in case we're dealing with an
                //invalid data structure.
                if (Util.ArrayHelper.HasValue(checking, item, 0, depth) ||
                    

                //Pdf files usualy don't have much nesting, so for complex
                //enough objects we just give up.
                  depth >= checking.Length ||
                    
                //Checks at the next depth level
                  QuickLoopCheck(item, checking, depth)) 
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Decrement the refcount of the children of a reference
        /// </summary>
        /// <remarks>
        /// This function avoids the problem of circular recursion by never
        /// following a reference that has a refcount. This works since
        /// when a refcount is zero there's no reference leading back to
        /// that object.
        /// 
        /// References leading to objects "behind" the reference we're
        /// removing will never have a refcount of zero, and won't be
        /// followed.
        /// 
        /// The drawback is of course that some circular references
        /// will not be decrementred, however a dirty flag is set to
        /// deal with this. 
        /// </remarks>
        private void DecChildCount(IEnumRef refs)
        {
            foreach (var child in refs.RefEnumerable)
            {
                if (child is WritableReference)
                {
                    var reference = (WritableReference)child;

                    //To prevent circular recursion, children
                    //with a count of zero is always ignored
                    if (reference.RefCount == 0) continue;

                    //Dec Refcount first, 
                    DecRefCount(reference);
                }
#if !PRUNE_INVALID
                else if (child is PdfReference)
                {
                    //Invalid datastructures, as those produced by retargeting, may
                    //have "normal" non-writable references. An alternative to this
                    //check would be to use a "SaveDecChildCount" from the retargeting
                    //method.
                }
#endif
                else
                {
                    //Direct objects can have child references, so
                    //they need to be checked too.
                    DecChildCount(child);
                }
            }
        }

        #endregion

        #endregion

        #region Functions for creating references

        /// <summary>
        /// Used when creating references from a file
        /// </summary>
        /// <param name="request_id">Desired ID</param>
        /// <returns>A PdfReference with a refcount of one</returns>
        internal RWReference CreateRWRef(PdfObjID request_id)
        {
            PdfObjID id = request_id;
            if (_resources.ContainsKey(request_id))
                id = CreateID();
            var ret = new RWReference(this, id, request_id);
            _resources.Add(id, ret);
            return ret;
        }

        /// <summary>
        /// Updates an existing reference.
        /// </summary>
        /// <remarks>Only for use before the trailer is constructed</remarks>
        internal void UpdateRWRef(RWReference rw)
        {
            _resources[rw.Id] = rw;
        }

        /// <summary>
        /// Updates an existing reference.
        /// </summary>
        /// <remarks>
        /// Only for compressing streams. I'm not sure if this
        /// is the right way to go about it. It's possible some
        /// other class now holds a reference to the old value.
        /// </remarks>
        /*internal void UpdateRWRef(IKRef new_value, IKRef old_value)
        {
            var rw = old_value.Reference as RWReference;
            if (rw != null)
            {
                rw.SetValue((PdfItem) new_value);
                new_value.Reference = rw;
            }
        }//*/

        /// <summary>
        /// Creates a new writable reference
        /// </summary>
        /// <param name="value">Value of the writable reference</param>
        /// <returns></returns>
        internal WritableReference CreateWRef(PdfItem value)
        {
            if (value == null) throw new ArgumentNullException();
            if (value is ICRef)
            {
                if (value is IKRef)
                {
                    var ik = (IKRef)value;
                    if (!ik.IsOwner(this))
                        return new WritableReference(null, new PdfObjID(), value);
                }
                else
                {
                    //ICref object may be owned by other documents, so we play it safe
                    return new WritableReference(null, new PdfObjID(), value);
                }
            }
            var ret = new WritableReference(this, CreateID(), value);
            _resources.Add(ret.Id, ret);
            return ret;
        }

        /// <summary>
        /// Creates a new writable reference that is owned by this tracker.
        /// Only use this method for objects that are already known to be
        /// owned by the tracker. 
        /// </summary>
        /// <param name="value">Value of the writable reference</param>
        private WritableReference CreateMyWRef(PdfItem value)
        {
            if (value == null) throw new ArgumentNullException();
            var ret = new WritableReference(this, CreateID(), value);
            _resources.Add(ret.Id, ret);
            return ret;
        }

        /// <summary>
        /// Creates a new temp writable reference
        /// </summary>
        /// <param name="value">Value of the temp writable reference</param>
        /// <returns>A temporary writable reference</returns>
        /// <remarks>Usefull if one need to update the value later</remarks>
        internal TempWRef CreateTWRef(PdfItem value)
        {
            var ret = new TempWRef(this, CreateID(), value);
            _resources.Add(ret.Id, ret);
            return ret;
        }

        /// <summary>
        /// Only for use by PdfXrefTable.Trim
        /// </summary>
        internal void Remove(PdfObjID id)
        { 
            _resources.Remove(id);
        }

        /// <summary>
        /// Creates an id. All ids will have generation number 0.
        /// </summary>
        /// <remarks>Only indirect objects can have a generation number
        /// other than zero, and it's really only usefull when updating
        /// existing files</remarks>
        private PdfObjID CreateID()
        {
            PdfObjID ret;
            do
            {
                ret = new PdfObjID(_next_id++, 0);
            } while (_resources.ContainsKey(ret));
            //if (ret.Nr == 76)
            //    Debug.Assert(true);
            return ret;
        }

        #endregion

        #region Other methods

        internal void RemoveSaveStrings()
        {
            foreach (var kp in _resources)
                kp.Value.SaveString = null;
        }

        #endregion

        #region Unused methods

        /// <summary>
        /// Creates a new store mode that is a combination
        /// of the two.
        /// </summary>
        /// <remarks>
        /// Priority:
        /// Indirect
        /// Compressed
        /// Direct
        /// Auto
        /// Unkown
        /// 
        /// This function isn't terribly usefull. All it really
        /// does in this solution is prevent one from changing 
        /// SM.indirect to SM.compressed in some cases. 
        /// 
        /// Perhaps "one" should be made to have priorety, and
        /// two be the save_mode one try to change to.
        /// 
        /// I.e. Auto and unknown can be changed 
        ///      Compressed can be set Indirect
        ///      Direct and Indirect can't be changed
        /// 
        /// Then expose some method to let a libary user change
        /// the def save mode. (In which case "one" must always
        /// be the item's (IRef) default save mode and "two" what 
        /// mode one wan't to change into from default*.)
        /// 
        /// Right now "one" if the store mode of a reference, and
        /// "two" is whatever. The sugested change is then to ignore
        /// the store mode of the reference, though it's that
        /// store mode that is ultimatly changed:
        /// 
        ///  Now: ref.StoreMode = ResolveSM(ref.StoreMode, whatever)
        ///  Sugested: ref.StoreMode = ResolveSM(IRef.DefStoreMode, whatever)
        /// </remarks>
        private SM ResolveSM(SM one, SM two)
        {
            if (one == two) return one;
            if (one == SM.Ignore || two == SM.Ignore)
            {
                Debug.Assert(false, "Tried to change ignore");
                return SM.Ignore;
            }
            if (one == SM.Indirect || two == SM.Indirect) 
                return SM.Indirect;
            if (one == SM.Compressed || two == SM.Compressed) 
                return SM.Compressed;
            if (one == SM.Unknown) return two;
            if (two == SM.Unknown) return one;

            //This code should never execute.
            if (one == SM.Auto) return two;
            return one;
        }

        #endregion

        #region IDoc

        internal void Notify(INotify note, bool register)
        {
            if (_owner != null)
                _owner.Notify(note, register);
        }

        /// <summary>
        /// Documents that wish to use the restracker must
        /// implement this interface
        /// </summary>
        public interface IDoc
        {
            /// <summary>
            /// The document's catalog
            /// </summary>
            PdfCatalog Catalog { get; }

            /// <summary>
            /// The document's file, if any
            /// </summary>
            PdfFile File { get; }

            /// <summary>
            /// The restracker will hand over targeting information to
            /// the owning document. This information can be used to
            /// bind up missing objects with equivalent objects
            /// </summary>
            /// <param name="target">Where to place the equivalent object</param>
            void Retarget(RetargetObject target);

            /// <summary>
            /// Objects that need notification before being saved
            /// </summary>
            /// <param name="obj">relevant object</param>
            void Notify(INotify obj, bool register);
        }

        #endregion

        #region Helper classes

        /// <summary>
        /// Used to cache items and their reference.
        /// </summary>
        private struct CacheObj
        {
            public readonly PdfItem Item;
            private WritableReference _ref;
            public WritableReference Reference
            {
                get
                {
                    if (_ref == null)
                    {
                        if (Item is IRef)
                        {
                            var iref = (IRef)Item;
                            if (iref.HasReference)
                                _ref = iref.Reference;
                        }
                    }
                    return _ref;
                }
                set
                {
                    if (Item is IRef)
                    {
                        var iref = (IRef)Item;
                        if (!iref.HasReference)
                            iref.Reference = value;
                    }
                    _ref = value;
                }
            }
            public CacheObj(PdfItem item)
            { Item = item; _ref = null; }
            public CacheObj(PdfItem item, WritableReference r)
            { Item = item; _ref = r; }
        }

        /// <summary>
        /// Just used to collect xref information during the
        /// save operation.
        /// </summary>
        internal class XRefTable
        {
            /// <summary>
            /// References in the tabble
            /// </summary>
            public WritableReference[] Table;

            /// <summary>
            /// Offsets get updated as the objects are written
            /// </summary>
            public long[] Offsets;

            /// <summary>
            /// The position of the catalog.
            /// </summary>
            /// <remarks>Is negative if not in use</remarks>
            public int CatalogPos;

            public XRefTable() { }

            public XRefTable(List<WritableReference> wrefs)
            {
                Table = wrefs.ToArray();
                Offsets = new long[Table.Length];
            }

            public override string ToString()
            {
                if (Table == null)
                    return "null";
                if (Table.Length == 0)
                    return "[ ]";
                var wr = Table[0];
                int id1 = wr.SaveString != null ? WritableReference.GetId(wr.SaveString) : -wr.Id.Nr;
                if (Table.Length == 1)
                    return "[ "+id1+" ]";
                wr = Table[Table.Length - 1];
                int id2 = wr.SaveString != null ? WritableReference.GetId(wr.SaveString) : -wr.Id.Nr;
                return "[ " + id1 + " -> " + id2 + " ]";
            }
        }

        /// <summary>
        /// Used for when saving partial xref tables. 
        /// </summary>
        internal class XRefState
        {
            internal XRefState(PM PaddMode, SaveMode SaveMode)
            {
                this.PaddMode = PaddMode;
                this.SaveMode = SaveMode;
                Resources = new HashSet<PdfObjID>();
            }

            internal PM PaddMode;
            internal SaveMode SaveMode;

            /// <summary>
            /// Used when one want to delay save references.
            /// </summary>
            internal void MakeSaveString(WritableReference wr)
            {
                wr.SaveString = Lexer.GetBytes(string.Format("{0} {1} R", last_id++, Math.Min(MAX_GEN_NR, wr.GenerationNr)));
            }

            /// <summary>
            /// Dictionary over resources that has been handled.
            /// </summary>
            /// <remarks>
            /// Used for detecting new resources that has
            /// yet to be written to disk.
            /// 
            /// In retrospect it would be better to modify the
            /// ResTracker to track new resources, i.e. have
            /// it put new resources into a list that can be
            /// cleared.
            /// </remarks>
            internal HashSet<PdfObjID> Resources;

            /// <summary>
            /// The highest generation number used
            /// </summary>
            internal int higest_gen_nr = 0;

            /// <summary>
            /// The last id used.
            /// </summary>
            /// <remarks>
            /// ID 0 is reserved, so starting the count from 1
            /// </remarks>
            internal int last_id = 1;

            /// <summary>
            /// Can be dropped in favor of if (extrarefs != null)
            /// </summary>
            internal bool flush = false;

            /// <summary>
            /// Extra references to renumber. These references need not
            /// be among the resources
            /// </summary>
            internal WritableReference[] extrarefs;

            /// <summary>
            /// The number of references. For this implementation this
            /// number will always equal last_id
            /// </summary>
            internal int size = 1;
        }

        #endregion
    }
}
