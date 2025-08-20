//#define DEBUG_XREF
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Pdf;
using PdfLib.Read;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Util;

namespace PdfLib.Write
{
    /// <summary>
    /// This document is for streaming pages straight onto the disk
    /// </summary>
    /// <remarks>
    /// Can for instance be used when one don't have enough memory to construct
    /// a full PDF document in memory before saving.
    /// 
    /// Saving isn't as efficient as "WritableDocument". Even with the object
    /// cache turned on one can't, for instance, strip away references in the
    /// manner WritableDocument does. 
    /// 
    /// Todo:
    /// - When saving as a XRefStream it stuffs from 1 to 64 objects into
    ///   the object streams, should make a cutof at say min 10 objects.
    /// 
    /// - Retargeted objects will not be saved in compressed streams, at
    ///   all. 
    /// </remarks>
    public class StreamDocument : ResTracker.IDoc, IDisposable
    {
        #region Variables and properties

        /// <summary>
        /// Stream we're saving to
        /// </summary>
        readonly Stream _target;

        /// <summary>
        /// Will do the actual writing
        /// </summary>
        PdfWriter _writer;

        /// <summary>
        /// Objects to notify on save
        /// </summary>
        private WeakList<INotify> _on_save;

        /// <summary>
        /// All the partial tables are collected here so that 
        /// the full table can be assembled later.
        /// </summary>
        /// <remarks>
        /// Only used when saving XRefStreams.
        /// 
        /// Done this way to reuse code in WritableDocument. The
        /// references are only used for their id, not their data.
        /// </remarks>
        List<ResTracker.XRefTable> _xref_table_chunks = new List<ResTracker.XRefTable>();

        /// <summary>
        /// XRef streams must also have a references to the trailer
        /// </summary>
        WritableReference _trailer_ref;

        /// <summary>
        /// Frequently accessed object
        /// </summary>
        readonly PdfCatalog _root;

        /// <summary>
        /// The root of the document
        /// </summary>
        readonly PdfReference _root_ref;

        /// <summary>
        /// All the pages in the document
        /// </summary>
        readonly PdfPages _pages;

        /// <summary>
        /// Keeps track of the resources in this document
        /// </summary>
        /// <remarks>The ResTracker is not idealy suited
        /// for this task. There's nothing preventing
        /// this document from doing its own resource
        /// tracking, just implement a "MakeCopy" rutine
        /// and do all the needed resource sorting.
        /// </remarks>
        readonly ResTracker _tracker;

        /// <summary>
        /// How the document is to be saved
        /// </summary>
        internal SaveMode _save_mode = SaveMode.Compressed;

        /// <summary>
        /// How the document is to be saved
        /// </summary>
        public SaveMode SaveMode
        {
            get { return _save_mode; }
        }

        /// <summary>
        /// How the document is to be padded
        /// </summary>
        PM PaddMode = PM.None;

        /// <summary>
        /// How the document is to be compressed
        /// </summary>
        internal CompressionMode _compression = CompressionMode.Normal;

        /// <summary>
        /// How the document will be compressed
        /// </summary>
        public CompressionMode Compression { get { return _compression; } }

        /// <summary>
        /// State object used by the restracker
        /// </summary>
        ResTracker.XRefState _state;

        /// <summary>
        /// Objects already on the disk or referenced by
        /// on disk objects.
        /// </summary>
        PdfObjID[] _on_disk = null;

        /// <summary>
        /// References that has already been given a savestring
        /// </summary>
        List<PdfObjID> _has_savestring = new List<PdfObjID>();

        /// <summary>
        /// These objects are waiting to be written to disk at
        /// first opertunity.
        /// </summary>
        List<WritableReference> _write_pending = new List<WritableReference>();

        /// <summary>
        /// Stores retargeting information
        /// </summary>
        Util.WeakKeyCache<PdfItem, WritableReference> _retarget_cache = new Util.WeakKeyCache<PdfItem, WritableReference>();

        /// <summary>
        /// Objects that are waiting to be retargeted.
        /// </summary>
        /// <remarks>Note, this cache holds two strong references to the "Index" object</remarks>
        Dictionary<PdfItem, ResTracker.RetargetObject[]> _retarget_later = new Dictionary<PdfItem, ResTracker.RetargetObject[]>();
        bool _must_fix_retargets = false;

        /// <summary>
        /// Warning: Use with care as cached objects
        /// are written out to disk straight away, 
        /// so any later modification will not be 
        /// written to disk.
        /// </summary>
        public bool CacheObjects
        {
            get { return _tracker.CacheObjects; }
            set { _tracker.CacheObjects = value; }
        }

        /// <summary>
        /// Document outline, note: not flushed to disk
        /// </summary>
        public PdfOutline Outlines
        {
            get { return _root.Outlines; }
            set { _root.Outlines = value; }
        }

        /// <summary>
        /// How the document is to be opened
        /// </summary>
        public PdfPageMode PageMode
        {
            get { return _root.PageMode; }
            set { _root.PageMode = value; }
        }

        /// <summary>
        /// The name tree
        /// </summary>
        public PdfNameDictionary Names
        {
            get { return _root.Names; }
            set { _root.Names = value; }
        }

        #region IDoc

        /// <summary>
        /// Can safely return null as StreamDocument does not make
        /// use of RW references or anything similar.
        /// </summary>
        PdfFile ResTracker.IDoc.File { get { return null; } }

        /// <summary>
        /// The catalog of this stream document.
        /// </summary>
        PdfCatalog ResTracker.IDoc.Catalog { get { return _root; } }

        #endregion

        #endregion

        #region Init

        public StreamDocument(string filepath)
            : this(filepath, SaveMode.Compressed)
        { }
        public StreamDocument(string filepath, SaveMode savemode)
            : this(filepath, CompressionMode.Normal, savemode)
        { }
        public StreamDocument(string filepath, CompressionMode cm)
            : this(filepath, CompressionMode.Normal, SaveMode.Compatibility)
        { }
        public StreamDocument(string filepath, CompressionMode cm, SaveMode savemode)
            : this(File.Open(filepath, FileMode.Create), cm, savemode)
        { }

        /// <summary>
        /// Creates a stream document
        /// </summary>
        /// <param name="output">Target stream</param>
        /// <param name="cm">compression mode</param>
        /// <param name="savemode">
        /// Save mode, should be compability for now since compressed needs additonal work
        /// as it bloats the file size by quite a bit in some cases.
        /// (Just take any single image per page pdf and save using compressed to see the 
        ///  bloat)
        ///  
        /// To improve on this one need to be smarter when saving object streams. I.e. if
        /// a page only has a few objects going into a object stream, wait to save it
        /// until more objects have been collected.
        /// </param>
        /// <remarks>
        /// A way to implement "auto" DefSaveMode would reduce the number of indirect
        /// objects, say when saving images made with "jpeg.CompressPDF". One could for
        /// instance look at the refcount of the original document, if avalible, or simply
        /// always save smaller "auto" objects as direct.
        /// </remarks>
        public StreamDocument(Stream output, CompressionMode cm, SaveMode savemode)
        {
            if (!output.CanWrite)
                throw new PdfNotWritableException();
            _target = output;
            _tracker = new ResTracker();
            _tracker.Doc = this;
            _save_mode = savemode;
            _compression = cm;

            //The catalog contains all the pages in the document.
            _root = new PdfCatalog(true, _tracker);

            //Creates an empty root catalog with a high id number; it's
            //not actually needed as StreamDocument don't care about ids
            //at all. 
            _tracker.NextId += int.MaxValue - 1000;
            _tracker.Register(_root);
            _tracker.NextId -= int.MaxValue - 1001;

            //Catalogs are required to be indirect, so we can assume it got
            //a reference
            _root_ref = ((IRef)_root).Reference;

            //We increase the refcount to signal that someone is holding
            //on to this catalog. It's not terribly important to do as
            //it only matters for objects with "SaveMode.auto"
            _tracker.RefCountRef(ref _root_ref);

            //Direct link to the pages.
            _pages = _root.Pages;

            //For now we create the header straight away
            CreateHeader();
        }

        public void Dispose() 
        {
            EndDocument();
            _target.Close();
        }

        #endregion

        #region IDoc

        /// <summary>
        /// Registers a object to be notified of save events
        /// </summary>
        /// <param name="obj">Object to register</param>
        /// <param name="register">If false, the object is unregistered</param>
        void ResTracker.IDoc.Notify(INotify obj, bool register)
        {
            if (register)
            {
                if (_on_save == null)
                    _on_save = new WeakList<INotify>(16);
                _on_save.Add(obj);
            }
            else if (_on_save != null)
            {
                _on_save.Remove(obj);
                lock (_on_save)
                {
                    if (_on_save.Count == 0)
                        _on_save = null;
                }
            }
        }

        /// <summary>
        /// The restracker will hand over targeting information to
        /// the owning document. This information can be used to
        /// bind up missing objects with equivalent objects
        /// </summary>
        /// <param name="target">Where to place the equivalent object</param>
        void ResTracker.IDoc.Retarget(ResTracker.RetargetObject target)
        {
            var index = target.Index.Deref();
            if (index is PdfDictionary)
            {
                //Unfortunatly one can't make this into a propper object just yet,
                //as it breaks the calling function.
                _must_fix_retargets = true;
            }

            var r = _retarget_cache[index];
            if (r != null)
            {
                //Does the retargeting straight away
                _tracker.IncRefCount(r);
                target.Value = r;
            }
            else
            {
                _must_fix_retargets = true;

                ResTracker.RetargetObject[] retargets;
                if (_retarget_later.TryGetValue(index, out retargets))
                {
                    //RetargetObject need not be an array, but keeping it
                    //that way for now.
                    //
                    //Basically one have to insure that all references to
                    //the same object are the same, and we do that here.
                    Debug.Assert(retargets[0].Value is TempWRef);
                    target.Value = retargets[0].Value;
                }
                else
                {
                    //Retargeting must be done after potentially writing the
                    //data out to disk. We therefore create a reference that 
                    //won't be written out to disk until it's safe to do so.
                    var tref = _tracker.CreateTWRef(PdfNull.Value);
                    target.Value = tref;
                    _state.Resources.Add(tref.Id);
                    _has_savestring.Add(tref.Id);
                    _write_pending.Add(tref);
                    _state.MakeSaveString(tref);
                    _retarget_later[index] = new ResTracker.RetargetObject[] { target };
                }
            }
        }

        private void PerformRetargets(PdfPage page, PdfPage new_page)
        {
            var to_add = new List<KeyValuePair<PdfItem, ResTracker.RetargetObject[]>>(_retarget_later.Count);
            foreach (var kp in _retarget_later)
            {
                var index = kp.Key;
                bool add = false;
                if (index is PdfDictionary)
                {
                    index = PdfPages.ParsePage((PdfDictionary)index);
                    if (index != null)
                        add = true;
                    else
                        index = kp.Key;
                }
                if (ReferenceEquals(index, page))
                {
                    var ra = kp.Value;
                    for (int c = 0; c < ra.Length; c++)
                    {
                        var tref = ra[c].Value as TempWRef;
                        if (tref != null)
                            tref.SetValue(new_page);
                    }
                }
                else
                {
                    if (add)
                        to_add.Add(new KeyValuePair<PdfItem, ResTracker.RetargetObject[]>(index, kp.Value));
                }
            }
            foreach (var kp in to_add)
            {
                _retarget_later.Remove(kp.Value[0].Index);
                _retarget_later.Add(kp.Key, kp.Value);
            }

            _must_fix_retargets = _retarget_later.Count != 0;
        }

        /// <summary>
        /// So that the unsaved data will be flushed to disk.
        /// </summary>
        private void RemoveRetargets()
        {
            _must_fix_retargets = false;
            _retarget_later.Clear();
        }

        #endregion

        #region Saving

        private void CreateHeader()
        {
            #region Determines version information

            //A bit on the simplistic side this is
            PdfVersion head_version;
            if (_save_mode == Internal.SaveMode.Compressed)
                head_version = PdfVersion.V15;
            else
                head_version = PdfVersion.V14;

            #endregion

            #region Writes the header to the stream

            _writer = new PdfWriter(_target, _save_mode, PaddMode, _compression, head_version);
            _writer.WriteHeader(head_version, true);

            #endregion

            #region Initializes for partial XRef table creation

            //This function creates a state object that will contain information needed
            //for creating a partial table
            _state = new ResTracker.XRefState(PaddMode, _save_mode);

            //Prevents the catalog from being written to disk.
            _state.Resources.Add(_root_ref.Id);

            //Prevents the pages array from being saved to disk. (Or one can set "SaveMode" on
            //the reference to Direct and forgo doing this)
            //
            //Because of this /one could probably forgo excluding the Catalog. 
            _state.Resources.Add(((IRef)_pages).Reference.Id);


            //Creates the trailer ref. Note that it can be used as a reference to the trailer by other objects.
            if (_save_mode == Internal.SaveMode.Compressed)
            {
                _trailer_ref = _tracker.CreateWRef(new WritableDictionary(_tracker));
                PdfReference reference = _trailer_ref;
                _tracker.RefCountRef(ref reference);
                //_tracker.CreateMyWRef is now private, and since WritableDictionary is a ICRef
                //object the tracker.CreateWRef will refuse to create an owned reference for it.
                _trailer_ref = (WritableReference) reference;

                //Prevents the trailer being written to disk prematurly by registering it
                //as "handeled" in the state.
                _state.Resources.Add(_trailer_ref.Id);
            }

            #endregion
        }

        /// <summary>
        /// Saves a page directly to disk
        /// </summary>
        /// <param name="page">Page to save</param>
        /// <remarks>
        /// The only known issue with this is with Variable Text and structural information,
        /// related data is not automatically copied to this document.
        /// </remarks>
        public void SavePage(PdfPage page)
        {
            #region Notifies listeners

            //Notifies listeners that saving has started
            WritableDocument.Notify(_on_save, NotifyMSG.PrepareForSave);

            #endregion

            #region Copies the page

            //Foreign pages are added to the retargeting cache
            var iref = (IRef)page;
            PdfPage retarget_page = null;
            if (!page.IsWritable || iref.HasReference && !_tracker.Owns(iref.Reference))
                retarget_page = page;

            //The cache in the restracker can cause trobule.
            var icref = (ICRef)page;
            _tracker.HasObject(ref icref);
            page = (PdfPage) icref;
            iref = (IRef)page;

            if (iref.HasReference && _tracker.Owns(iref.Reference))
            {
                //Makes a shallow copy.
                page = PdfPage.ShallowCopy(page);
            }
            else
            {
                //The tracker has a method for copying an object. We also use "Inherit" to
                //make sure the page has inherited resources. 
                page = PdfPage.Inherit((PdfPage)_tracker.MakeCopy((ICRef)page, new object[] { "Parent" }), page);
                iref = (IRef)page;
            }

            #endregion

            #region Registers and retargets the page

            if (retarget_page != null)
            {
                if (_must_fix_retargets)
                {
                    PerformRetargets(retarget_page, page);
                    if (!iref.HasReference)
                        _tracker.Register(page);
                }
                else
                {
                    _tracker.Register(page);
                }

                _retarget_cache[retarget_page] = ((IRef)page).Reference;
            }
            else
            {
                //By registering we give the page a reference.
                if (!iref.HasReference)
                    _tracker.Register(page);
            }

            //Then we add it to the pages list
            _pages.AddPage((PdfPage)page);

            #endregion

            #region Writes the page out to disk

            if (_tracker.IsDirty)
                _tracker.FixResTable((WritableReference)_root_ref, _on_disk);

            //CreateObjectList
            // This function passes a partial xref table state over to the ResTracker. The restracker compiles
            // a list of new objects to write out. This can be from 0 to many objects. It will automatically
            // pack objects into streams and such.
            ResTracker.XRefTable objects = _tracker.CreatePartialXRef(_state);
            WritableDocument.WriteObjects(_writer, 0, objects);

            //Writes additional objects
            ResTracker.XRefTable appended_objects = null;
            if (_write_pending.Count > 0)
            {
                var write = new List<WritableReference>(_write_pending.Count);
                foreach (var wr in _write_pending)
                    if (wr.Deref() != PdfNull.Value)
                        write.Add(wr);
                appended_objects = new ResTracker.XRefTable(write);
                WritableDocument.WriteObjects(_writer, 0, appended_objects);
                foreach (var wr in write)
                    _write_pending.Remove(wr);
                if (appended_objects.Table.Length > 0)
                {
                    _xref_table_chunks.Add(appended_objects);
                    _state.size += appended_objects.Table.Length;
                }
            }

            #endregion

            #region Flushes written data from memory

            if (_save_mode == Internal.SaveMode.Compressed)
            {
                for (var c = 0; c < objects.Table.Length; c++)
                    objects.Table[c].Flush();
                if (appended_objects != null)
                {
                    for (var c = 0; c < appended_objects.Table.Length; c++)
                        if (!(appended_objects.Table[c].Deref() is PdfPage))
                            appended_objects.Table[c].Flush();
                }
                //Note that not all objects are flushed, any object placed
                //in an object stream will remain in memory. This can be
                //considered a memory leak, but it should be acceptable
            }
            else
            {
                //By coincidence pages will not be flushed in compressed mode.
                //We don't want them flushed since they are used a tiny bit
                //when counting up the number of pages in the document. We could
                //fix this by altering PageTree.Count to count "null" as a page,
                //or by using another method for counting pages. 
                //
                //We can assume that a "page" objects will be a "PdfPage" object
                //and never a plain dictionary (so "is PdfPage" is safe).
                for (var c = 0; c < objects.Table.Length; c++)
                    if (!(objects.Table[c].Deref() is PdfPage))
                        objects.Table[c].Flush();
                if (appended_objects != null)
                {
                    for (var c = 0; c < appended_objects.Table.Length; c++)
                        if (!(appended_objects.Table[c].Deref() is PdfPage))
                            appended_objects.Table[c].Flush();
                }
            }

            //It's impossible to completly fix a reftable when data is flushed from
            //from memory, so we save a list of references that must not be removed
            //ever.
            _on_disk = _tracker.GetCurrentIDs();

            #endregion

            #region Creates the XRef table

            //Well, we now wait until the document is done. Allows for more code
            //sharing between compability and compressed. (Compressed mode need
            //to know the full size of the table so that it can size itself
            //apporpriatly, compability has one size, 10 digits, and that's it.)
            _xref_table_chunks.Add(objects);
            

            #endregion

            #region Notifies listeners

            //Notifies listeners that saving has started
            WritableDocument.Notify(_on_save, NotifyMSG.SaveComplete);

            #endregion
        }

        private void EndDocument()
        {
            #region Step 0. Clearing caches and prep

            if (_retarget_later.Count != 0)
                RemoveRetargets();

            _retarget_later.Clear();
            _retarget_cache.Clear();
            _tracker.CacheObjects = false;

            #endregion

            #region Step 1. Flushing

            //One could also remove the ids from _state.Resources but this way the ids will
            //be the last three, so we can find them again.
            _state.extrarefs = new WritableReference[_save_mode == SaveMode.Compressed ? 3 : 2];
            _state.extrarefs[0] = (WritableReference) ((IRef) _pages).Reference;
            _state.extrarefs[1] = (WritableReference)_root_ref;
            if (_save_mode == SaveMode.Compressed)
                _state.extrarefs[2] = _trailer_ref;

            //First write out any remaning objects (in object streams)
            //Then the catalog, as well as the trailer
            _state.flush = true;
            ResTracker.XRefTable objects = _tracker.CreatePartialXRef(_state);
            _state.extrarefs = null;

            //Writes additional objects
            if (_write_pending.Count > 0)
            {
                var appended_objects = new ResTracker.XRefTable(_write_pending);
                WritableDocument.WriteObjects(_writer, 0, appended_objects);
                _write_pending.Clear();
                _xref_table_chunks.Add(appended_objects);
                _state.size += appended_objects.Table.Length;
            }

            //By setting the trailer to null we prevent it from being written out to disk
            if (_save_mode == SaveMode.Compressed)
            {
                Debug.Assert(_trailer_ref == objects.Table[objects.Table.Length - 1]);
                objects.Table[objects.Table.Length - 1] = null;
            }
            _xref_table_chunks.Add(objects);

            WritableDocument.WriteObjects(_writer, 0, objects);

            #endregion

            #region Step 2. Writes XRef table

            long startxref = _target.Position;
            var w = _writer;

            //We create the full XRefTable in memory.
            var table = new ResTracker.XRefTable();
            table.Table = new WritableReference[_state.size];
            table.Offsets = new long[_state.size];
            int pos = 1; //<-- First position is reserved
            foreach (ResTracker.XRefTable chunk in _xref_table_chunks)
            {
                Array.Copy(chunk.Table, 0, table.Table, pos, chunk.Table.Length);
                Array.Copy(chunk.Offsets, 0, table.Offsets, pos, chunk.Table.Length);
                pos += chunk.Table.Length;
            }
            _xref_table_chunks = null;

            //Writes out the xref table
            if (_save_mode == SaveMode.Compatibility)
            {
                w.WriteRawLine("xref\n");
                if (_state.size == 1)
                {
                    //Empty table.
                    w.WriteRaw("0 0\n");
                }
                else
                {
                    //Sorts the table. This is only needed when retargeting is used, but
                    //we do it regardless
                    SortTable(table);

                    w.WriteRaw(string.Format("{0} {1}\n", 0, _state.size));
                    w.WriteRaw("0000000000 65535 f\r\n");
                    WritableDocument.WriteXrefTableIds(w, table, 1, table.Table.Length);
                }
            }

            #endregion

            #region Step 3. Writes out the trailer

            Catalog cat = new Catalog();
            cat.Add("Root", ((IRef)_root).Reference);

            if (_save_mode != SaveMode.Compressed)
            {
                w.WriteRawLine("trailer\n");
                cat.Add("Size", new PdfInt(_state.size));
                w.WriteDictionary(cat);
            }
            else
            {
                cat.Add("Type", new PdfName("XRef"));

                //We add back the trailer reference removed for the sake of the WriteObjects function
                var trailer_pos = _state.size - 1;
                Debug.Assert(table.Table[trailer_pos] == null);
                table.Table[trailer_pos] = _trailer_ref;
                table.Offsets[trailer_pos] = _target.Position;

                //Sorts the table. This is only needed when retargeting is used, but
                //we do it regardless
                SortTable(table);

                //Must make sure the format is capable of holding the highest
                //offset. stream.Position will be higher, but is never lower.
                int n_offset_digits = (Math.Max(_target.Position, _state.size - 1).ToString("X2").Length + 1) / 2;

                //Makes sure we have enough room for generation numbers
                int n_gen_digits = (_state.higest_gen_nr.ToString("X2").Length + 1) / 2;

                //Format is Type - Offset - GenNr or ObjID
                cat.Add("W", new IntArray(new int[] { 1, n_offset_digits, n_gen_digits }));

                byte[] data = WritableDocument.CreateStreamXrefTable(table, cat);
                //Compress, set filter, etc,

#if DEBUG_XREF
                    //Note: These files can't be opened by Adobe Reader. Note sure why, may be
                    //      an error in the filter or may be that HexDecode is dissallowed.
                    cat.Add("Filter", new PdfName("ASCIIHexDecode"));
                    data = PdfLib.Pdf.Filter.PdfHexFilter.FormatEncode(data, ((IntArray)cat["W"]).ToArray(), true);
#else
                var compressed_data = Pdf.Filter.PdfFlateFilter.Encode(data);
                if (compressed_data.Length + 19 < data.Length)
                {
                    data = compressed_data;
                    cat.Add("Filter", new Pdf.Filter.PdfFlateFilter());
                }
#endif

                cat.Add("Length", new PdfInt(data.Length));

                w.WriteRawLine(string.Format("{0} 0 obj", trailer_pos));
                w.WriteDictionary(cat);
                w.WriteRawLine("stream\n");
                w.WriteRaw(data);
                w.WriteRawLine("endstream");
                w.WriteRawLine("endobj");
            }

            #endregion

            #region Step 4. Write StartXref and EOF

            w.WriteRawLine("startxref\n");
            w.WriteInt((int)startxref);
            w.WriteRawLine("%%EOF");

            #endregion
        }

        private void SortTable(ResTracker.XRefTable table)
        {
            var t = table.Table;
            var o = table.Offsets;

            var sorted_table = new WritableReference[t.Length];
            var sorted_offsets = new long[t.Length];

            for (int c = 1; c < t.Length; c++)
            {
                var wr = t[c];
                if (wr != null)
                {
                    int id;
                    if (wr.Deref() == null && wr.Tracker == null)
                    {
                        //Index references. The true id is stored in
                        //the refcount.
                        id = wr.RefCount;

                        //Not sure if the later algo needs this or not.
                        wr.RefCount = int.MinValue;
                    }
                    else
                        id = WritableReference.GetId(wr.SaveString);
                    Debug.Assert(sorted_table[id] == null);
                    sorted_table[id] = wr;
                    sorted_offsets[id] = o[c];
                }
            }

            table.Table = sorted_table;
            table.Offsets = sorted_offsets;
        }

        #endregion
    }
}
