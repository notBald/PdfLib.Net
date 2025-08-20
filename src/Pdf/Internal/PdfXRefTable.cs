using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Read;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Contains object ids and cached objects
    /// </summary>
    /// <remarks>
    /// Reverse lookup: (When one want the object id of an indirect object)
    ///  While there's not yet any need for this, I've made object creation work in
    ///  such a way that if you have a reference to an indirect object, said object
    ///  will be cached in this table.
    ///  
    ///  Note that this implementation assumes that "high level" objects are unique,
    ///  and that the search is conducted using the "high level" representation of
    ///  the Dictionary or Array.
    ///  
    ///  In essense when a high level object is created the low level object is
    ///  dropped from the cache, so anyone with a reference to that object can no
    ///  longer do a reverse look up - but as fortune has it such objects tend to
    ///  be wrapped in a PdfReference, so one can use the Object ID instead.
    ///  
    /// Likely bug: Objects with a gen above 65535 will probably not be readable.
    ///             This since gen is stored in a ushort.
    /// </remarks>
    internal class PdfXRefTable
    {
        #region Variables and properties

        /// <summary>
        /// Array over references
        /// </summary>
        PdfObjRef[] _refs;

        /// <summary>
        /// Used for tracking resources
        /// </summary>
        private ResTracker _tracker;

        /// <summary>
        /// For references without offset
        /// </summary>
        internal const int NO_OFFSET = -1;

        /// <summary>
        /// As of right now the xref table and PdfFile
        /// is pretty much the same class, as they depend
        /// on eachother, oh well.
        /// </summary>
        readonly PdfFile _owner;

        /// <summary>
        /// Optional resource tracker.
        /// </summary>
        internal ResTracker Tracker { get { return _tracker; } set { _tracker = value; } }

        #endregion

        #region Init

        internal PdfXRefTable(ResTracker tracker, PdfFile owner) { _tracker = tracker; _owner = owner; }

        /// <summary>
        /// For ensuring that the XRef table has enough space
        /// </summary>
        /// <param name="space">The total amount of space needed</param>
        /// <remarks>Used while building the XRef table from a file</remarks>
        internal void SetXRefSpace(int space)
        {
            if (_refs == null)
                _refs = new PdfObjRef[space];
            else if (space > _refs.Length)
                Array.Resize<PdfObjRef>(ref _refs, space);
        }

        #endregion

        /// <summary>
        /// Removes cached data. Only to be used by SealedDocuments
        /// </summary>
        /// <remarks>
        /// For Writable documents there is no safe way to flush data, as
        /// one don't know which data the user "has changed".
        /// 
        /// Though one can probably flush plain RWReferences. 
        /// </remarks>
        internal void FlushCache()
        {
            foreach (var kp in _refs)
            {
                if (kp != null)
                {
                    var r = kp.Reference;
                    if (r is SealedReference)
                        ((SealedReference)r).Flush();
                }
            }
        }

        /// <summary>
        /// Check if this table contains the object
        /// </summary>
        public bool Contains(PdfObjID id) { return GetRef(id) != null; }

        /// <summary>
        /// Check if this table has the offset for a given object id
        /// </summary>
        public bool HasOffset(PdfObjID id) 
        {
            PdfObjRef ret = GetObj(id);
            if (ret != null)
                return ret.HasLocation;
            return false;
        }

        /// <summary>
        /// Gets a reference object for the given id, or returns null
        /// if no such reference exists
        /// </summary>
        /// <remarks>The returned reference is refcounted, so only for
        /// use when fetching a reference for keeps.</remarks>
        internal PdfReference GetReference(PdfObjID id)
        {
            //Note that deleted objects have no reference
            var ret = GetRef(id);
            if (_tracker != null && ret != null)
            {
                _tracker.IncRefCount((WritableReference) ret);
            }
            return ret;
        }

        /// <summary>
        /// Itterates over all references
        /// </summary>
        internal IEnumerable<PdfReference> EnumerateRefs()
        {
            foreach (var r in _refs)
            {
                if (r != null)
                {
                    var re = r.Reference;
                    if (re != null)
                        yield return re;
                }
            }
        }

        /// <summary>
        /// Gets a reference. No refcounting
        /// </summary>
        internal PdfReference GetRef(PdfObjID id)
        {
            //Note that deleted objects have no reference
            if (id.Nr >= _refs.Length)
                return null;
            PdfObjRef ret = _refs[id.Nr];
            if (ret != null && ret.Id == id)
                return ret.Reference;
            return null;
        }

        /// <summary>
        /// Gets a reference. No refcounting
        /// </summary>
        private PdfObjRef GetObj(PdfObjID id)
        {
            //Note that deleted objects have no reference
            if (id.Nr >= _refs.Length)
                return null;
            PdfObjRef ret = _refs[id.Nr];
            if (ret != null && ret.Id == id)
                return ret;
            return null;
        }

        /// <summary>
        /// Updates the cached value of a reference. The
        /// reference may potentially be replaced
        /// </summary>
        /// <param name="id">idetity of the reference</param>
        /// <param name="value">Cached value</param>
        internal void Update(PdfObjID id, PdfItem value)
        {
            OffsetRef r = _refs[id.Nr] as OffsetRef;
            if (r == null) throw new PdfInternalException("Only offet objects can be updated");

            var old_ref = r.Reference;
            if (old_ref is RWReference)
            {
                var rw = (RWReference)old_ref;
                rw = new RWReference(_tracker, rw.Id, id, value);
                rw.SaveMode = value.DefSaveMode;
                _refs[id.Nr] = new OffsetRef(id, r.Offset, rw);
                _tracker.UpdateRWRef(rw);
            }
            else if (old_ref is SealedReference)
                _refs[id.Nr] = new OffsetRef(id, r.Offset, new SealedReference((SealedReference)old_ref, value));
            else
                throw new PdfInternalException("Unkown reference type");
        }

        /// <summary>
        /// Adds an object to the table
        /// </summary>
        /// <param name="id">Id of object</param>
        /// <param name="offset">Location of object</param>
#if LONGPDF
        internal void Add(PdfObjID id, long offset)
#else
        internal void Add(PdfObjID id, int offset)
#endif
        {
            if (id.Nr >= _refs.Length) return;
            PdfObjRef ret = _refs[id.Nr];
            if (ret != null)
            {
                if (ret.Id == id)
                {
                    //Updates the offset.
                    //Debug.Assert(!ret.HasLocation);

                    //Deleted objects have no reference and are
                    //therefor not to be updated (not that it
                    //matters)
                    if (ret.Reference != null)
                        _refs[id.Nr] = new OffsetRef(id, offset, ret.Reference);
                }
            }
            else
                _refs[id.Nr] = new OffsetRef(id, offset, CreateReference(id));
        }

        /// <summary>
        /// Adds a compressed object to the table
        /// </summary>
        /// <param name="id">Id of object</param>
        internal void Add(PdfObjID id, int parent_id, int index)
        {
            //If id is to high, ignore the reference
            if (id.Nr >= _refs.Length)
                return;
            
            //If we already have the reference, ignore it
            var ret = _refs[id.Nr];
            if (ret != null && ret.HasLocation)
                return;
            
            //An index points at a parent
            var par_id = new PdfObjID(parent_id, 0);
            var parent = GetRef(par_id);

            //But we might not have the parent
            if (parent == null)
            {
                //Creates a parent without offset that substutites for now, and will possibly be
                //updated later
                Add(par_id, NO_OFFSET);
                parent = GetRef(par_id);
            }

            if (ret == null)
                _refs[id.Nr] = new IndexRef(parent, index, CreateReference(id));
            else
            {
                if (ret.Reference != null)
                {
                    _refs[id.Nr] = new IndexRef(parent, index, ret.Reference);
                }
                else if (_refs[parent_id].HasLocation)
                {
                    //Alt. is to do nothing. 
                    Debug.Assert(false, "Is this correct behavior?");
                    _refs[id.Nr] = new IndexRef(parent, index, CreateReference(id));
                }
                
                //if (ret.Reference == null)
                //{
                //    Debug.Assert(false, "Not sure if this is my bug or an error in the document."); //Investigate DG279-familie-Katalog.pdf, in particular page 2 (My bug)
                //    throw new PdfParseException(PdfType.XRefStream, ErrCode.IsCorrupt);
                //}
            }
        }

        /// <summary>
        /// Adds an object to the table that already has a value,
        /// overwriting any existing object.
        /// </summary>
        /// <remarks>Only for use when repairing documents</remarks>
        internal void Add(PdfObjID id, PdfItem value)
        {
            if (id.Nr >= _refs.Length)
                return;

            //Abusing the OffsetRef a little, by setting offset to 0
            //it will return true for "Has location".
            _refs[id.Nr] =  new OffsetRef(id, 0, new TempReference(value));
        }

        /// <summary>
        /// Deletes an object from the table
        /// </summary>
        internal void Del(PdfObjID id)
        {
            if (id.Nr >= _refs.Length)
                return;
            _refs[id.Nr] = new OffsetRef(id, NO_OFFSET, null);
        }

        /// <summary>
        /// Trims the cross references to the size and removes missing references
        /// </summary>
        /// <param name="size">A numbe one higher than the higest object number</param>
        /// <remarks>
        /// Any object in a cross-reference section whose number 
        /// is greater than this value shall be ignored and 
        /// defined to be missing by a conforming reader.
        /// </remarks>
        internal void Trim(int size)
        {
            System.Array.Resize<PdfObjRef>(ref _refs, size);
        }

        /// <summary>
        /// Gives all references a poke to get
        /// them into memory
        /// </summary>
        /// <param name="load_stream">Load stream data into memory</param>
        internal bool LoadAllRefs(bool load_stream)
        {
            bool no_error = true;
            foreach (var kp in _refs)
            {
                if (kp == null || !kp.HasLocation) continue;
                try
                {
                    var obj = kp.Reference.Deref();
                    //I don't think this can ever happen. 
                    //if (obj is PdfIObject)
                    //{
                    //    //Not good. The IObject should never be seen outside 
                    //    //the parser. It's used to encapsulate object id 
                    //    //information for error checking.
                    //    var iobj = (PdfIObject)obj;

                    //    //Naturaly we do the error checking (it's what it's 
                    //    //for after all)
                    //    var right_id = kp.Value.Reference.Id;

                    //    //Ids can be compared with !=, as it's overloaded.
                    //    if (right_id != iobj.Id)
                    //    {
                    //        //throw new PdfParseException(PdfType.Item, ErrCode.Wrong);
                    //        kp.Value.Reference.Invalidate(false);
                    //        no_error = false;
                    //    }
                    //    else
                    //    {
                    //        //The underlying object has a type, like "integer, array, etc"
                    //        //By doing a totype on the reference it will cast the iobj
                    //        //into this type, and strip away the iobj.
                    //        var newobj = kp.Value.Reference.ToType(iobj.Value.Type);
                    //        Debug.Assert(newobj.Type == iobj.Value.Type);
                    //    }
                    //}
                    //if (kp.Reference.Type == PdfType.Dictionary && obj is PdfDictionary)
                    //{
                    //    var dict = (PdfDictionary)obj;
                    //    if (dict.IsType("Page") && dict.Count == 1)
                    //        Console.WriteLine(""+kp.Reference);
                    //}
                    if (load_stream && obj is ICStream)
                        ((ICStream)obj).LoadResources();
                }
                catch (PdfErrException e)
                {
                    //There was some sort of error while loading the resource.
                    //However, can't tell if this resource is needed or not or
                    //how to correctly handle the exception. By invalidating 
                    //the reference the exception will be rethrown the next 
                    //time the resource is read.
                    kp.Reference.Invalidate(false);
                    no_error = false;
                    Debug.WriteLine(e);
                }
            }
            return no_error;
        }

        /// <summary>
        /// Gives Writable References a poke to get
        /// them into memory
        /// </summary>
        internal bool LoadReferences()
        {
            bool no_error = true;
            foreach (var kp in _refs)
            {
                if (kp == null || !kp.HasLocation) continue;
                try
                {
                    var wr = (WritableReference) kp.Reference;
                    if (wr.RefCount > 0 && !wr.HasValue)
                    {
                        //Goes through the whole reference tree, loading all data
                        //used by this reference
                        var obj = wr.Deref();
                        if (obj is IEnumRef)
                            LoadReferences((IEnumRef)obj);
                    }
                    
                }
                catch (PdfErrException e)
                {
                    //There was some sort of error while loading the resource.
                    //However, can't tell if this resource is needed or not or
                    //how to correctly handle the exception. By invalidating 
                    //the reference the exception will be rethrown the next 
                    //time the resource is read.
                    kp.Reference.Invalidate(false);
                    no_error = false;
                    Debug.WriteLine(e);
                }
            }
            return no_error;
        }

        /// <summary>
        /// Recursivly load all data
        /// </summary>
        /// <param name="iemn_in">References to load</param>
        /// <remarks>
        /// There's no need to check for circular references. This because
        /// references with value isn't followed, and references are given
        /// values before they are followed (A side effect of deref). 
        /// </remarks>
        private void LoadReferences(IEnumRef iemn_in)
        {
            foreach (var iemn in iemn_in.RefEnumerable)
            {
                if (iemn is PdfReference)
                {
                    var r = (PdfReference) iemn;
                    if (!r.HasValue)
                    {
                        var obj = r.Deref();
                        if (obj is IEnumRef)
                            LoadReferences((IEnumRef)obj);
                    }
                }
                else
                    LoadReferences(iemn);
            }
        }

        /// <summary>
        /// Gives all references a poke to get
        /// them into memory
        /// </summary>
        internal void FixXRefTable()
        {
            foreach (var kp in _refs)
            {
                if (kp == null) continue;
                try
                {
                    var obj = kp.Reference.Deref();
                    if (obj is PdfIObject)
                    {
                        //Not good. The IObject should never be seen outside 
                        //the parser. It's used to encapsulate object id 
                        //information for error checking.
                        var iobj = (PdfIObject)obj;

                        //Naturaly we do the error checking (it's what it's 
                        //for after all)
                        var right_id = kp.Reference.Id;

                        //Ids can be compared with !=, as it's overloaded.
                        if (right_id != iobj.Id)
                        {
                            //throw new PdfParseException(PdfType.Item, ErrCode.Wrong);
                            kp.Reference.Invalidate(false);
                        }
                        else
                        {
                            //The underlying object has a type, like "integer, array, etc"
                            //By doing a totype on the reference it will cast the iobj
                            //into this type, and strip away the iobj.
                            var newobj = kp.Reference.ToType(iobj.Value.Type);
                            Debug.Assert(newobj.Type == iobj.Value.Type);
                        }
                    }
                }
                catch (PdfErrException e)
                {
                    //Needless
                    kp.Reference.Invalidate(false);
                    Debug.WriteLine(e);

                    _refs[Util.ArrayHelper.IndexOf(_refs, 0, kp)] = null;
                }
            }
        }

        /// <summary>
        /// Parses an item out of the stream and caches a copy of the
        /// item.
        /// </summary>
        /// <param name="id">Id of the item</param>
        /// <param name="p">Parser to use to get the item</param>
        /// <returns>The item, null if no item exists</returns>
        internal PdfItem ParseItem(PdfReference r, IParser p)
        {
            //If the reference has a value already then simply
            //return it.
            if (r.HasValue)
            {
                //The debuger can trigger this condition easily.
                //Debug.Assert(false, "This should never happen");
                return r.Deref();
            }

            //if (r.Id.Nr == 124455)
            //    Debug.Assert(false);

            if (r.Id.Nr > _refs.Length)
                throw new PdfInternalException(SR.ObjNotFound);

            PdfObjRef oref = GetObj(r.Id);
            if (oref == null || !oref.HasLocation)
                throw new PdfInternalException(SR.ObjNotFound);

            if (r != oref.Reference && oref.Reference.HasValue)
            {
                Debug.Assert(false, "This may happen, but is likely a bug");
                return oref.Reference.Deref();
            }

            //Parses the item from the stream.
            PdfIObject obj;
            if (oref is OffsetRef)
            {

                var hold = p.Position;
                p.Position = ((OffsetRef)oref).Offset;
                try
                {
                    var itm = p.ReadItem();
                    if (!(itm is PdfIObject))
                        throw new PdfReadException(ErrSource.Xref, itm.Type, ErrCode.NotObject);
                    obj = (PdfIObject)itm;
                }
                catch (PdfParseException par)
                {
                    //Todo: Do something smarter. This is just to get "cas.tif.pdf" to parse, perhaps
                    //      trigger a "fix xref table" algo, similar to what other pdf libaries do. 
                    var itm = p.ReadItem();
                    if (!(itm is PdfIObject))
                        throw par;
                    obj = (PdfIObject)itm;
                }
                finally
                {
                    p.Position = hold;
                }
            }
            else
            {
                var index = (IndexRef)oref;
                var parent = (ObjStream) index.Parent.ToType(PdfType.ObjStream, IntMsg.Owner, _owner);
                obj = parent[index.Index];
            }

            //I've done some quick chekcing on what Sumatra does when encountering missmathced
            //ids. Basically it will not read the object, and that's that.
            //I.e. one don't need any "return the wrong item and hope for the best" schemes.
            if (obj.Id != r.Id)
                throw new PdfReadException(ErrSource.Xref, obj.Type, ErrCode.UnexpectedObject);

            //Note that the oref.Reference value is not updated.
            //This is a subtle feature that allows refererences
            //to hold different representation of a value.
            //Presumably there's no need for this functionality,
            Debug.Assert(r == oref.Reference, "Feature above used", "Was it on purpose?");
            // ^Note that for ResTracked documents this is not allowed. But at the same
            //  time the ResTracker handles the reference creation so no need to check here.

            return obj.Value;
        }

        private PdfReference CreateReference(PdfObjID id)
        {
            if (_tracker == null) return new SealedReference(_owner, id);

            return _tracker.CreateRWRef(id);
        }

        /// <summary>
        /// Object with offset and a cached reference
        /// </summary>
        /// <remarks>
        /// I've considered placing this value straight 
        /// into the ref objects, that is faster but has 
        /// the extra cost of tracking all ref objects 
        /// just in case their offset needs to be changed.
        /// 
        /// I.e. one can simply create a ref object
        /// and go, since the offset is here.
        /// 
        /// Off course if this feature isn't needed, then
        /// it's easy to change. Just add a offset field
        /// to the reference + parse code. But keep in mind 
        /// that references are often created before a offset
        /// is know, and that compressed objects will need
        /// two offsets (I think) and that offset really
        /// should cover 8 gigabytes - not 2
        /// </remarks>
        private abstract class PdfObjRef
        {
            /// <summary>
            /// Cached Reference
            /// </summary>
            public readonly PdfReference Reference;

            /// <summary>
            /// Identity
            /// </summary>
            public readonly PdfObjID Id;

            /// <summary>
            /// If this reference has the object's
            /// location
            /// </summary>
            public abstract bool HasLocation { get; }

            /// <summary>
            /// Constructor
            /// </summary>
            protected PdfObjRef(PdfObjID id, PdfReference reference)
            { Id = id; Reference = reference; }
        }

        /// <summary>
        /// For compressed objects
        /// </summary>
        private class IndexRef : PdfObjRef
        {
            public readonly PdfReference Parent;
            public readonly int Index;

            /// <remarks>
            /// The parent may be without a location though
            /// </remarks>
            public override bool HasLocation { get { return true; } }

            public IndexRef(PdfReference parent, int index, PdfReference pdfref)
                : base(pdfref.Id, pdfref)
            {
                Parent = parent;
                Index = index;
            }
        }

        /// <summary>
        /// Offset objects are used by XRef tables
        /// </summary>
        private class OffsetRef : PdfObjRef
        {
            /// <summary>
            /// Offset from the beginning of the stream
            /// to this object
            /// </summary>
#if LONGPDF
            public readonly long Offset;
#else
            public readonly int Offset;
#endif

            public override bool HasLocation
            {
                get { return Offset != NO_OFFSET; }
            }
#if LONGPDF
            public OffsetRef(PdfObjID id, long offset, PdfReference pdfref)
#else
            public OffsetRef(PdfObjID id, int offset, PdfReference pdfref)
#endif
                : base(id, pdfref)
            {
                Offset = offset;
            }

            /// <summary>
            /// Debug aid string
            /// </summary>
            public override string ToString()
            {
                return string.Format("File offset: {0}", Offset);
            }
        }

    }
}
