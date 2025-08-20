//#define DEBUG_XREF //Adds ASCIIHexDecode to stream XRef (which makes Adobe throw an error, but makes the XRef readable in a text editor)
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Read;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Filter;
using PdfLib.Pdf;
using PdfLib.Compile;
using PdfLib.Render;
using PdfLib.Render.PDF;
using PdfLib.Util;

namespace PdfLib.Write
{
    /// <summary>
    /// A editable pdf document
    /// </summary>
    /// <remarks>
    /// Editable pdf documents have their own "XRef table", in the ResTracker
    /// 
    /// When reading from a imported documents, the xref table is used to 
    /// find objects in the file stream. It's full of references that
    /// needs to be correct, and I don't want to mess with them.
    /// 
    /// So editable documents instead has it's own resource table format.
    /// Saves me some pain.
    /// 
    /// How to implement AppendChanges:
    /// 
    ///  Alt. 1. All objects that can be changed, must have a "has been changed" flag
    ///  
    ///          I don't like this solution. Sure, you can get most of the way by simply
    ///          modifying WritableDict and array, but minor bugs can easily slip in and
    ///          this code is unlikely to see much testing
    ///          
    ///  Alt. 2. Simply reopen the document in read only mode. Compare object by object,
    ///          which one has changed. There's no need to worry about circular loops, as
    ///          refererences will not be followed.
    ///         
    ///          Streams must also be checked.
    ///          
    ///  Alt. 3. IncrementalDocument.
    ///  
    ///          With some minor changes, the parser/restracker can be altered to call an 
    ///          interface for dictionary creation. That would allow for dictionaries that
    ///          track changes. This is similar to Alt. 1.
    ///          
    ///          When streams are altered, length is set, so there's no need to check streams.
    ///          
    ///          This solution has the advantage of issolating the incremental code, so bugs
    ///          will not touch the other code paths.
    ///          
    /// </remarks>
    public class WritableDocument : PdfDocument, ResTracker.IDoc
    {
        #region Variables and properties

        /// <summary>
        /// The default precision for reals
        /// </summary>
        int? _real_precision;

        /// <summary>
        /// Keeps track of the resources in this document
        /// </summary>
        readonly ResTracker _tracker;

        /// <summary>
        /// Frequently accessed object
        /// </summary>
        private PdfCatalog _root;

        /// <summary>
        /// File this document belongs to.
        /// </summary>
        readonly PdfFile _file;

        /// <summary>
        /// Metadata about the document
        /// </summary>
        private PdfInfo _info;

        /// <summary>
        /// Objects to notify on save
        /// </summary>
        private WeakList<INotify> _on_save;

        /// <summary>
        /// How the document is to be saved
        /// </summary>
        public SaveMode SaveMode = SaveMode.Compressed;

        /// <summary>
        /// If the document is text only or includes binary
        /// </summary>
        public OutputMode OutputMode = OutputMode.AssumeBinary;

        /// <summary>
        /// How the document is to be padded
        /// </summary>
        public PM PaddMode = PM.None;

        public override PdfInfo Info
        {
            get
            {
                if (_info == null)
                {
                    var file = File;
                    if (file == null) return null;
                    _info = file.Info;
                    if (_info == null)
                        _info = new PdfInfo();
                }
                return _info;
            }
        }

        /// <summary>
        /// Precision of real numbers when saving to disk.
        /// Set negative to reset to default.
        /// </summary>
        public int RealPrecision
        {
            get 
            {
                if (_real_precision.HasValue)
                    return _real_precision.Value;
                if (File != null)
                    return _file.RealPrecision;
                return PdfWriter.DefaultRealPrecision; 
            }
            set
            {
                if (value < 0)
                    _real_precision = null;
                else
                {
                    if (value > 15)
                        throw new NotSupportedException("Value must be from 0 to 15");
                    _real_precision = value;
                }
            }
        }

        /// <summary>
        /// How the document is to be compressed
        /// </summary>
        public CompressionMode Compression = CompressionMode.Normal;

        /// <summary>
        /// Whenever this document was created from an existing
        /// file or as a new document.
        /// </summary>
        internal bool CreatedNew { get { return _file == null; } }

        /// <summary>
        /// The on disk file for this document, if any.
        /// </summary>
        public override PdfFile File { get { return _file; } }

        /// <summary>
        /// If the document is to cache foreign objects so that
        /// it can recognize them if they're added to the document
        /// multiple times, and avoid making a copy then.
        /// </summary>
        public bool CacheObjects
        {
            get { return _tracker.CacheObjects; }
            set { _tracker.CacheObjects = value; }
        }

        public override bool IsWritable => true;

        #region IDoc

        /// <summary>
        /// The catalog of this document.
        /// </summary>
        /// <remarks>Catalog is currently set internal</remarks>
        PdfCatalog ResTracker.IDoc.Catalog { get { return Catalog; } }

        /// <summary>
        /// Stores retargeting information
        /// </summary>
        WeakKeyCache<PdfItem, WritableReference> _retarget_cache = new WeakKeyCache<PdfItem, WritableReference>();

        /// <summary>
        /// Objects that are waiting to be retargeted.
        /// </summary>
        /// <remarks>Note, this cache holds two strong references to the "Index" object</remarks>
        Dictionary<PdfItem, ResTracker.RetargetObject[]> _retarget_later = new Dictionary<PdfItem, ResTracker.RetargetObject[]>();
        bool _must_fix_retargets = false;

        #endregion

        #endregion

        #region Init

        /// <summary>
        /// Creates a document with a single image page
        /// </summary>
        /// <param name="page">Page 1</param>
        public WritableDocument(PdfPage page)
            : this()
        {
            AddPage(page);
        }

        /// <summary>
        /// Creates a document with a single image page
        /// </summary>
        /// <param name="page">Page 1</param>
        public WritableDocument(CompiledPage page)
            : this()
        {
            AddPage(page);
        }

        /// <summary>
        /// Creates a document with a single image page
        /// </summary>
        /// <param name="img">Image that will be set as a page</param>
        public WritableDocument(PdfXObject img)
            : this()
        {
            AddPage(img);
        }

        /// <summary>
        /// Constructor for creating new documents
        /// </summary>
        public WritableDocument()
            : this(new ResTracker(), null, null)
        { base.SetPages(); }

        protected override void DisposeImpl()
        { if (_file != null) _file.Dispose(); }

        /// <summary>
        /// Constructor for creating rewritable document
        /// </summary>
        /// <param name="track">Tracker for the document's resource</param>
        /// <param name="file">File that owns the document</param>
        /// <param name="root_ref">The reference pointing to the catalog</param>
        internal WritableDocument(ResTracker track, PdfFile file, PdfReference root_ref)
        {
            //By default, start at the lowest version
            //_header = new PdfHeader(1, 0, 8);

            //Resources added and removed to the document
            //is needed to track details such as the version
            //number, irefs and such.
            _tracker = track;
            _file = file;

            //This is used for ReWritableReferences, Outline objects and
            //retargeting.
            _tracker.Doc = this;

            if (root_ref == null)
            {
                //The catalog contains all the pages in the document.
                _root = new PdfCatalog(true, _tracker);

                //Creates an empty root catalog with a high id number; it's
                //not strickly needed but gets the catalog out of the way so
                //that one don't end up renumbering every reference when saving.
                _tracker.NextId += int.MaxValue - 1000;
                _tracker.Register(_root);
                _tracker.NextId -= int.MaxValue - 1001;

                //Catalogs are required to be indirect, so we can assume it got
                //a reference
                _root_ref = ((IRef) _root).Reference;

                //We increase the refcount to signal that someone is holding
                //on to this catalog. It's not terribly important to do as
                //it only matters for objects with "SaveMode.auto"
                _tracker.RefCountRef(ref _root_ref);
            }
            else
            {                
                //Note that references created by the parser is already refcounted.
                _root_ref = root_ref;
            }

            //Can't be done before encryption is figured out, and encryption can't
            //be done before the document is created. 
            //SetPages();
        }

        internal void PostEncryptionInit()
        {
            base.SetPages();
            if (_root == null)
                _root = (PdfCatalog)_root_ref.ToType(PdfType.Catalog);
        }

        #endregion

        #region Compression

        /// <summary>
        /// Tells the writable document to compress itself in memory.
        /// </summary>
        /// <remarks>
        /// This differs from setting compression mode in that the various stream
        /// resources are compressed before saving, and that the document is modified
        /// for this new compression.
        /// 
        /// By setting compresion mode objects are compressed right before writing
        /// them to disk. In some cases this can leave orphaned arrays in the file,
        /// which is avoided by calling this method before saving.
        /// </remarks>
        public void Compress(CompressionMode mode)
        {
            if (mode == CompressionMode.None)
                return;

            var filters = new List<FilterArray>(256);
            foreach (var stream in _tracker.Streams)
            {
                stream.Compress(filters, mode);
            }
        }
        public void Compress() { Compress(CompressionMode.Maximum); }

        #endregion

        #region Memory mangaement

        /// <summary>
        /// Loads as much data as it can into the cache.
        /// </summary>
        /// <returns>True if there was no errors when
        /// loading the data into the cache</returns>
        public override bool LoadIntoMemory(bool load_streams)
        {
            if (_file == null) return true;
            lock (_file) { return _file.LoadAllResources(load_streams); }
        }

        #endregion

        #region Document repair

        internal override void FixXRefTable()
        {
            if (_file != null) 
                _file.FixXRefTable();
            FixResTable();
        }

        /// <summary>
        /// Fixes the resource table.
        /// </summary>
        /// <remarks>
        /// While idealy there will never be an error in the ref table,
        /// it's very difficult to do decremental refcounting right. This
        /// function helps out with that.
        /// 
        /// Also, documents opened straight into write mode may have
        /// junk and references pointing at invalid ids.
        /// </remarks>
        public void FixResTable()
        {
            try
            {
                //The tracker must be removed, as it does refcounting when
                //data is parsed. 
                if (_file != null)
                    _file.Tracker = null;

                _tracker.FixResTable((WritableReference) _root_ref, null);
            }
            finally
            {
                if (_file != null)
                    _file.Tracker = _tracker;
            }
        }

        #endregion

        #region Document analysis

        /// <summary>
        /// Analyzses a PDF file
        /// </summary>
        /// <param name="dump_file">Path where to dump</param>
        public void Analyze(string dump_file)
        {
            //Itterates through the pages to make sure all pages are parsed
            foreach (var page in this)
            {
                //Do nothing
            }

            //Updates all reference counts
            if (_tracker.IsDirty)
                FixResTable();
            _tracker.RemoveSaveStrings();

            using (var tw = System.IO.File.CreateText(dump_file))
            {
                Analyze(tw, Catalog, "Catalog", (WritableReference) _root_ref);
            }
        }

        private void Analyze(TextWriter tw, ICRef parent, string name, WritableReference r)
        {
            //Marking them to signal that it has been referenced
            r.SaveString = new byte[0];

            tw.Write(r.Id.Nr + " " + r.Id.Gen + " obj " + ((r.RefCount != int.MinValue) ? ""+r.RefCount : "min") + " " + name);
            if (parent is PdfPage)
            {
                int index = this._pages.FindPageIndex((PdfPage)parent) + 1;
                tw.Write(" (Page " + index + ")");
            }
            tw.WriteLine();
            var list = Analyze(tw, parent, "");

            foreach (var oa in list)
            {
                var wr = (WritableReference) oa[0];
                if (wr.SaveString == null)
                {
                    tw.WriteLine(); tw.WriteLine();
                    Analyze(tw, (ICRef)wr.Deref(), oa[1].ToString(), wr);
                }
            }
        }

        private List<object[]> Analyze(TextWriter tw, ICRef parent, string prepend)
        {
            var children = parent.GetChildren();
            var list = new List<object[]>(20);
            if (children is Catalog)
            {
                foreach (var kp in ((Catalog)children))
                {
                    var child = kp.Value;
                    if (child is ICRef)
                    {
                        tw.WriteLine(prepend + "\t/" + kp.Key);
                        list.AddRange(Analyze(tw, (ICRef)child, prepend + "\t   "));
                    }
                    else if (child is WritableReference)
                    {
                        var r = (WritableReference)child;
                        tw.Write(prepend + "\t/" + kp.Key + " -> " + r.Id.Nr + " " + r.Id.Gen + "; " + ((r.RefCount != int.MinValue) ? "" + r.RefCount : "min"));
                        if (!(r.Deref() is ICRef))
                            tw.WriteLine(" => " + r.Deref().ToString());
                        else
                        {
                            tw.WriteLine();
                            list.Add(new object[] { child, kp.Key });
                        }
                    }
                    else
                    {
                        tw.WriteLine(prepend + "\t/" + kp.Key + " : " + child.ToString());
                    }
                }
            }
            else
            {
                int c = 0; int ndigits = ((PdfItem[])children).Length.ToString().Length;
                foreach (var child in (PdfItem[])children)
                {
                    tw.Write(prepend+"[" + string.Format("{0, "+ndigits+"}", c) + "]");
                    if (child is ICRef)
                    {
                        tw.WriteLine();
                        list.AddRange(Analyze(tw, (ICRef)child, prepend + "\t"));
                    }
                    else if (child is WritableReference)
                    {
                        var r = (WritableReference)child;
                        tw.Write(" -> " + r.Id.Nr + " " + r.Id.Gen + "; " + ((r.RefCount != int.MinValue) ? "" + r.RefCount : "min"));
                        if (!(r.Deref() is ICRef))
                            tw.WriteLine(" => " + r.Deref().ToString());
                        else
                        {
                            tw.WriteLine();
                            list.Add(new object[] { child, "[" + string.Format("{0, " + ndigits + "}", c) + "]" });
                        }
                    }
                    else
                    {
                        tw.WriteLine(" : " + child.ToString());
                    }
                    c++;
                }
            }
            return list;
        }

        #endregion

        #region Working with pages

        /// <summary>
        /// Creates a new page owned by this document
        /// </summary>
        public PdfPage NewPage()
        {
            var page = new PdfPage(true, _tracker);
            _tracker.Register(page);
            _pages.AddPage(page);
            return page;
        }

        /// <summary>
        /// Creates a new page owned by this document
        /// </summary>
        /// <param name="height">Height of the page</param>
        /// <param name="width">Width of the page</param>
        public PdfPage NewPage(double width, double height)
        {
            var page = new PdfPage(true, _tracker);
            page.MediaBox = new PdfRectangle(0, 0, width, height);
            _tracker.Register(page);
            _pages.AddPage(page);
            return page;
        }

        public PdfPage AddPage(PdfXObject xobject)
        {
            if (xobject is PdfImage)
            {
                var img = (PdfImage) xobject;
                var page = NewPage(img.Width, img.Height);
                using (var draw = new DrawPage(page))
                {
                    draw.PrependCM(new xMatrix(img.Width, 0, 0, img.Height, 0, 0));
                    draw.DrawImage(img);
                }
                return page;
            }
            else if (xobject is PdfForm)
            {
                var form = (PdfForm)xobject;
                return AddPage(form.ConvertToPage());
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds a page to the document
        /// </summary>
        /// <param name="page">Page to add</param>
        /// <param name="disconnect">Detaches it from streams</param>
        /// <returns>Cloned document</returns>
        public PdfPage AddPage(PdfPage page, bool disconnect)
        {
            page = AddPage(page);
            if (disconnect)
                ((ICRef)page).LoadResources(new HashSet<object>());
            return page;
        }

        /// <summary>
        /// For adding a page from another document.
        /// </summary>
        /// <remarks>
        /// This has the side effect of linking the two documents. 
        /// Changes in one document can be reflected in this document.
        /// 
        /// Rules for changing ownerships.
        /// 
        /// IRef objects must be copied, through the inner catalog 
        /// (inc. stream data) may stay as it is.
        /// 
        /// IEnumRef objects must be copied if they contain a ref not 
        /// owned by this document
        /// 
        /// --
        /// Note that any "ICRef" object that don't implement the 
        /// "CopyObject" glue interface will cause an exception if 
        /// the object isn't pruned before saving.
        /// 
        /// The reason for this is that the object will find its 
        /// way into the resource tracker with a ref count of 1, 
        /// then when the document is saved the "non-existant" 
        /// object gets save too, except for the issue of it likely 
        /// being non-writable (though if it's writable it will be 
        /// junk data instead)
        /// </remarks>
        public PdfPage AddPage(PdfPage page)
        {
            //Foreign pages are added to the retargetig cache
            var iref = (IRef)page;
            PdfPage retarget_page = null;
            if (!page.IsWritable || iref.HasReference && !_tracker.Owns(iref.Reference))
                retarget_page = page;

            var icref = (ICRef)page;

            //The cache in the restracker can cause trobule.
            _tracker.HasObject(ref icref);
            iref = (IRef)icref;
            page = (PdfPage) iref;

            //First check if this document already owns this page.
            if (iref.HasReference)
            {
                //In this case we can assume that the page is already in
                //the retarget cache

                if (iref.Reference.Tracker == _tracker)
                {
                    //This document already own this page. However
                    //Adobe will do not support pages appearing
                    //multiple times in a document. To fix that
                    //we make a shallow copy.
                    page = PdfPage.ShallowCopy(page);

                    //Todo:
                    //Copy over structural data from the page and it's XObjects (from the PdfCatalog)

                    //And add it as normal
                    _pages.AddPage(page);
                    return page;
                }
            }

            //The tracker has a method for copying an object. We also use "Inherit" to
            //make sure the page has inherited resources. 
            page = PdfPage.Inherit((PdfPage)_tracker.MakeCopy((ICRef)page, new object[] { "Parent" }), page);
            
            //Todo:
            //Copy over structural data from the page and it's XObjects (from the PdfCatalog)

            //By registering we give the page a reference. 
            _tracker.Register(page);

            _pages.AddPage(page);

            if (retarget_page != null)
            {
                _retarget_cache[retarget_page] = ((IRef)page).Reference;
                ResTracker.RetargetObject[] retargets;
                if (_retarget_later.TryGetValue(retarget_page, out retargets))
                {
                    _retarget_later.Remove(retarget_page);
                    for (int c = 0; c < retargets.Length; c++)
                        retargets[c].Value = ((IRef)page).Reference;
                }
            }

            return page;
        }

        public void AddPage(CompiledPage page)
        {
            AddCompiledPage(NewPage(), page);
        }
        internal static void AddCompiledPage(PdfPage npage, CompiledPage page)
        {
            if (page.Rotate != 0)
                npage.Rotate = page.Rotate;
            npage.MediaBox = page.MediaBox;
            if (page.CropBox != null)
                npage.CropBox = page.CropBox;
            if (page.Annotations == null || page.Annotations.Length == 0)
            {
                var draw = new DrawPage();
                draw.Precision = page.DetectedPresicion;
                draw.DrawPdfPage(npage, page);
            }
            else
            {
                //DrawPdfPage can't currently handle annotations, so drawing them
                //straight onto the page. 
                using (var draw = new DrawPage(npage))
                {
                    new PdfRender().RenderWithIDraw(page, draw, npage.Width, npage.Height, npage.Rotate);
                }
            }
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

            //Tries to do the retargeting straight away
            var r = _retarget_cache[index];
            if (r != null)
            {
                _tracker.IncRefCount(r);
                target.Value = r;
            }
            else
            {
                ResTracker.RetargetObject[] retargets;
                if (_retarget_later.TryGetValue(index, out retargets))
                {
                    Array.Resize<ResTracker.RetargetObject>(ref retargets, retargets.Length + 1);
                    retargets[retargets.Length - 1] = target;
                    _retarget_later[index] = retargets;
                }
                else
                    _retarget_later[index] = new ResTracker.RetargetObject[] { target };
            }
        }

        /// <summary>
        /// This method is called before saving and forces all targets
        /// to either PdfNull or some alternate object.
        /// </summary>
        /// <remarks>
        /// Might be an idea to make this method public, so that one can make
        /// retargeting happen without saving to disk first.
        /// </remarks>
        private void PerformRetargets()
        {
            foreach (var kp in _retarget_later)
            {
                var index = kp.Key;
                if (index is PdfDictionary)
                    index = PdfPages.ParsePage((PdfDictionary) index);
                if (index != null)
                {
                    var item = _retarget_cache[index];;
                    if (item != null)
                    {
                        var ra = kp.Value;
                        for (int c = 0; c < ra.Length; c++)
                            ra[c].Value = item;
                    }
                    else
                    {
                        //Todo: find alternate target for PdfPages
                        if (index is PdfPage)
                        {
                            int idx = ((PdfPage)index).Index;
                            if (idx != -1 && idx < NumPages)
                            {
                                var itm = this[idx];
                                var ra = kp.Value;
                                for (int c = 0; c < ra.Length; c++)
                                    ra[c].Value = itm;
                            }
                        }
                    }
                }
            }

            _retarget_later.Clear();
        }

        #endregion

        #region Loading into memory

        public void LoadAndCloseFile()
        {
            LoadIntoMemory();
            _file.Close();
        }

        #endregion

        #region Saving

        /// <summary>
        /// Saves the document to a file.
        /// </summary>
        [DebuggerStepThrough]
        public void WriteTo(string filename)
        {
            using (var file = System.IO.File.Create(filename))
                WriteTo(file);
        }

        /// <summary>
        /// Saves the document to a stream
        /// </summary>
        /// <remarks>
        /// There's high time this code got an overhaul.
        /// Weaknesses:
        ///  - Can't save documents larger than 2GB
        ///  - Does a poor job optimizing object streams
        ///  - Has no way of knowing how big an object/document is before it's written out to disk
        ///  - Can't encrypt while saving
        ///  - Limits xref stream documents to xref table constraints
        ///  - Saves a flat PageTree
        /// </remarks>
        public void WriteTo(Stream stream)
        {
            #region Step 0. Prep work

            if (_file != null)
                Monitor.Enter(_file);

            try
            {

                //Notifies listeners that saving has started
                Notify(_on_save, NotifyMSG.PrepareForSave);

                if (_retarget_later.Count != 0)
                    PerformRetargets();

                //The cache is automatically cleared. Presumably one are done adding
                //stuff to the document now.
                CacheObjects = false;

                //Documents created from disk may have incorrect reference counts.
                //By pre loading the resources we get the ref counts updated.
                if (!CreatedNew)
                    _file.LoadResources();

                if (_tracker.IsDirty)
                    FixResTable();

                //Pages may need to generate default (inherited) objects.
                _pages.BeginWrite(SaveMode);

            } 
            catch (Exception e)
            {
                if (_file != null)
                    Monitor.Exit(_file);

                throw e;
            }

            #endregion

            try
            {

                #region Step 1. Determine the version number

                PdfVersion head_version;
                if (SaveMode == Internal.SaveMode.Compressed)
                    head_version = PdfVersion.V17;
                else
                {
                    //AFAICT there's no point of setting anything else
                    head_version = PdfVersion.V17;
                    /*head_version = _root.PdfVersion;

                    if (head_version < PdfVersion.V14)
                        head_version = _root.GetPdfVersion();*/
                }
                #endregion

                #region Step 2. Sort objects
                //Step 2. Sort objects from the res tracker dictionary
                //     2b Group compressed objects
                //     2c Determine if there's binary data
                //     if  >= 1.4 set the catalog last
                ResTracker.XRefTable xref;
                int higest_generation_number = 0, higest_id = 0, trailer_pos = 0;
                WritableReference trailer_ref = null;
                if (SaveMode == Internal.SaveMode.Compressed)
                {
                    trailer_ref = new TempWRef(this._tracker, new PdfObjID());
                    xref = _tracker.CreateXRefStreamTable(out higest_generation_number, out higest_id, trailer_ref, PaddMode);

                    //Position the trailer ended up at
                    trailer_pos = WritableReference.GetId(trailer_ref.SaveString);

                    //Then we remove it from the table so that it won't be saved by the WriteObjects function
                    xref.Table[trailer_pos] = null;
                }
                else
                {
                    //var xref2 = _tracker.CreateXRefTable(PaddMode);
                    //FixResTable();
                    xref = _tracker.CreateXRefTable(PaddMode);
                    //Console.WriteLine("Not presemt in xref2:");
                    //foreach (var r in xref2.Table)
                    //{
                    //    if (Util.ArrayHelper.IndexOf(xref.Table, 0, r) == -1)
                    //        Console.WriteLine(r.ToString());
                    //}
                }

                #endregion

                #region Step 3. Write header
                bool is_binary;
                if (OutputMode == Internal.OutputMode.AssumeBinary)
                    is_binary = true;
                else if (OutputMode == Internal.OutputMode.AssumeText)
                    is_binary = false;
                else if (OutputMode == Internal.OutputMode.Auto)
                {
                    if (SaveMode == Internal.SaveMode.Compressed)
                        is_binary = true;
                    else
                    {
                        is_binary = false;
                        foreach (var reference in xref.Table)
                        {
                            if (reference != null)
                            {
                                var itm = reference.Deref() as IStream;
                                if (itm != null)
                                {
                                    var filter = itm.Filter;
                                    if (filter != null)
                                        is_binary = filter.IsBinary;
                                    else if (itm is PdfStream)
                                        is_binary = true; //Data is on disk, so we won't bother reading it in.
                                    else
                                    {
                                        //Not perfect, but zero is a common binary value.
                                        is_binary = Util.ArrayHelper.IndexOf(itm.RawStream, 0, 0) != -1;
                                    }

                                    if (is_binary)
                                        break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }

                var writer = new PdfWriter(stream, SaveMode, PaddMode, Compression, head_version);
                writer.Precision = RealPrecision;
                writer.WriteHeader(head_version, is_binary);

                #endregion

                #region Step 4. Write objects
                WriteObjects(writer, 1, xref);
                #endregion

                #region Step 5. Write XRef table

                int total_size = 0; long startxref = stream.Position;
                if (SaveMode != SaveMode.Compressed)
                {
                    writer.WriteRawLine("xref\n");

                    total_size = WriteXrefTable(writer, xref);
                }

                #endregion

                #region Step 6. Create trailer

                Catalog cat = new Catalog();
                cat.Add("Root", ((IRef)_root).Reference);

                if (SaveMode != SaveMode.Compressed)
                {
                    writer.WriteRawLine("trailer\n");
                    cat.Add("Size", new PdfInt(total_size));
                    writer.WriteDictionary(cat);
                }
                else
                {
                    cat.Add("Type", new PdfName("XRef"));

                    //We add back the trailer reference removed for the sake of the WriteObjects function
                    xref.Table[trailer_pos] = trailer_ref;
                    xref.Offsets[trailer_pos] = stream.Position;

                    //Must make sure the format is capable of holding the highest
                    //offset. stream.Position will be higher, but is never lower.
                    int n_offset_digits = (Math.Max(stream.Position, higest_id).ToString("X2").Length + 1) / 2;

                    //Makes sure we have enough room for generation numbers
                    int n_gen_digits = (higest_generation_number.ToString("X2").Length + 1) / 2;

                    //Format is Type - Offset - GenNr or ObjID
                    cat.Add("W", new IntArray(new int[] { 1, n_offset_digits, n_gen_digits }));

                    byte[] data = CreateStreamXrefTable(xref, cat);
                    //Compress, set filter, etc,

#if DEBUG_XREF
                    //Note: These files can't be opened by Adobe Reader. Note sure why, may be
                    //      an error in the filter or may be that HexDecode is dissallowed.
                    cat.Add("Filter", new PdfName("ASCIIHexDecode"));
                    data = PdfHexFilter.FormatEncode(data, ((IntArray)cat["W"]).ToArray(), true);
#endif

                    writer.WriteRawLine(string.Format("{0} 0 obj", trailer_pos));
                    cat.Add("Length", new PdfInt(data.Length));
                    writer.WriteStream(cat, data);
                    writer.WriteRawLine("endobj");
                }

                #endregion

                #region Step 7. Write StartXref and EOF

                writer.WriteRawLine("startxref\n");
                writer.WriteInt((int)startxref);
                writer.WriteRawLine("%%EOF");

                #endregion

            }
            finally
            {
                #region Step 8. Clean up

                //Destroys default objects
                _pages.EndWrite();

                if (_file != null)
                    Monitor.Exit(_file);

                #endregion
            }

            #region Notifies listeners

            //Notifies listeners that saving has started
            WritableDocument.Notify(_on_save, NotifyMSG.SaveComplete);

            #endregion
        }

        /// <summary>
        /// Must be kept in sych with WritePlainObjs or combined
        /// </summary>
        internal static void WriteObjects(PdfWriter writer, int offset, ResTracker.XRefTable xref)
        {
            byte[] obj = Lexer.GetBytes("obj\n");
            byte[] endobj = Lexer.GetBytes("\nendobj\n");

            for (int c = offset; c < xref.Table.Length; c++)
            {
                var wref = xref.Table[c];
                //Ignores null and index references
                if (wref == null || wref.Tracker == null) continue;

                //Padding
                if (writer.PaddMode > PM.None)
                {
                    //Break off long lines.
                    writer.WriteLine();
                }

                //Updates offsets
                xref.Offsets[c] = writer.Position;

                //Writes the object identifier.
                writer.Write(wref.SaveString, 0, wref.SaveString.Length - 1);
                writer.WriteRaw(obj);

                //Update version info.
                var item = wref.Deref(); //<-- gets the object into memory
                if (c == xref.CatalogPos)
                {
                    var ver = item.PdfVersion;
                    if (ver > writer.HeaderVersion)
                        ((PdfCatalog)item).Version = new PdfHeader(1, (byte)ver, 0);
                }

                //Writes the object
                item.Write(writer, SM.Indirect);

                //Ends the object
                writer.Write(endobj);
            }
        }

        /// <summary>
        /// This function is used by "Writable Object stream." Kept here
        /// so that it can be kept in synch with any changes made to 
        /// "WriteObjects". To combine the methods make it so that
        /// "WriteObjects" don't write obj/endobj
        /// </summary>
        internal static void WritePlainObjs(PdfWriter writer, int offset, ResTracker.XRefTable xref)
        {
            for (int c = offset; c < xref.Table.Length; c++)
            {
                var wref = xref.Table[c];
                //Ignores null and index references
                if (wref == null || wref.Tracker == null) continue;

                //Padding is always "none"

                //Updates offsets
                xref.Offsets[c] = writer.Position;

                //Update version info.
                var item = wref.Deref(); //<-- gets the object into memory
                if (c == xref.CatalogPos)
                {
                    var ver = item.PdfVersion;
                    if (ver > writer.HeaderVersion)
                        ((PdfCatalog)item).Version = new PdfHeader(1, (byte)ver, 0);
                }

                //Writes the object
                item.Write(writer, SM.Compressed);
            }
        }

        /// <summary>
        /// Creates a XRef Stream table
        /// </summary>
        /// <param name="xrefs">The table to create the stream from, first position is expected to be null</param>
        /// <param name="cat">The trailer's catalog</param>
        /// <returns>The stream data</returns>
        /// <remarks>
        /// Note that StreamDocument flushes the data in the reference, so don't ever dereference here.
        /// </remarks>
        internal static byte[] CreateStreamXrefTable(ResTracker.XRefTable xrefs, Catalog cat)
        {
            //Makes the format string.
            var W = ((IntArray) cat["W"]).ToArray();
            MemoryStream ms = new MemoryStream();
            
            //Inits some needed variables and get the
            //size of the sub section
            List<int> Index = new List<int>();
            int pos = 0;
            int i = ArrayHelper.IndexOfNull(xrefs.Table, 1); //<-- Higest id
            if (i == 0)
            {
                //Writes an empty table
                Index.Add(0); Index.Add(1);
                cat.Add("Size", new PdfInt(1));
                WriteBytes(ms, W, 0, 0, 0);
                return ms.ToArray();
            }

            //Writes section header.
            int size = i - pos;
            int total_size = size;
            Index.Add(pos); Index.Add(size);

            //Writes the linked free list.
            // - First num points at itself. Last prevents reuse.
            //The file I checked used 0, not F, for the gen (last number)
            WriteBytes(ms, W, 0, 0, 0);
            pos++;

            while (true)
            {
                //Writes items.
                for (int c = pos; c < i; c++)
                {
                    var write_ref = xrefs.Table[c];

                    if (write_ref.Tracker == null)
                    {
                        //This is really an IndexRef as WriteRef objects with
                        //tracer set null as IndexRefs. (And using GenNr as index)

                        //Type 2 - ObjectStream nr - Index in the object
                        WriteBytes(ms, W, 2, write_ref.ObjectNr, write_ref.GenerationNr);
                    }
                    else
                    {
                        //Type 1 - Offset from begining of file - gen number
                        int gen = WritableReference.GetGen(write_ref.SaveString);
                        WriteBytes(ms, W, 1, (int) xrefs.Offsets[c], gen);
                    }
                }

                if (i == xrefs.Table.Length) break;
                pos = i;
                i = ArrayHelper.IndexOfNull(xrefs.Table, i + 1);
                if (i == pos + 1) break;

                Debug.Assert(false, "Untested code");

                //Writes next section header.
                size = i - pos;
                total_size = i;
                Index.Add(pos); Index.Add(size);
            }

            //Adds subsections if required
            if (Index.Count > 2)
                cat.Add("Index", new IntArray(Index.ToArray()));
            cat.Add("Size", new PdfInt(total_size));

            return ms.ToArray();
        }

        /// <summary>
        /// Writes padded bytes to the stream.
        /// </summary>
        private static void WriteBytes(MemoryStream ms, int[] w, params int[] values)
        {
            for (int c = 0; c < w.Length; c++)
            {
                var num = GetBytes(values[c]);
                int padd = w[c] - num.Length;
                Debug.Assert(padd >= 0);
                for (int i = 0; i < padd; i++)
                    ms.WriteByte(0);
                ms.Write(num, 0, num.Length);
            }
        }

        /// <summary>
        /// Gets big endian bytes
        /// </summary>
        private static byte[] GetBytes(int num)
        {
            byte[] val;
            if ((num & 0xFF) == num)
            {
                val = new byte[1];
                val[0] = (byte)num;
            }
            else if ((num & 0xFFFF) == num)
            {
                val = new byte[2];
                val[1] = (byte)(num & 0xFF); num = num >> 8;
                val[0] = (byte)num;
            }
            else if ((num & 0xFFFFFF) == num)
            {
                val = new byte[3];
                val[2] = (byte)(num & 0xFF); num = num >> 8;
                val[1] = (byte)(num & 0xFF); num = num >> 8;
                val[0] = (byte)num;
            }
            else
            {
                val = new byte[4];
                val[3] = (byte)(num & 0xFF); num = num >> 8;
                val[2] = (byte)(num & 0xFF); num = num >> 8;
                val[1] = (byte)(num & 0xFF); num = num >> 8;
                val[0] = (byte) num;
            }
            
            return val;
        }

        internal static int WriteXrefTable(PdfWriter w, ResTracker.XRefTable xrefs)
        {
            //First we determine the size of the table
            int pos = 0;
            int i = ArrayHelper.IndexOfNull(xrefs.Table, 1);

            //This is an empty table
            if (i == 0)
            {
                //Writes an empty table
                w.WriteRaw("0 0\n");
                return 0;
            }

            int total_size = 0;
            while (pos < i)
            {
                //Size is the number of items in this section.
                int size = i - pos;

                //Total size is the highest id in the table
                if (size > 0)
                {
                    total_size = i;

                    //Writes section header
                    w.WriteRaw(string.Format("{0} {1}\n", pos, size));

                    if (pos == 0)
                    {
                        //Writes the linked free list.
                        // - First num points at itself. Last prevents reuse.
                        w.WriteRaw("0000000000 65535 f\r\n");
                        pos++;
                    }

                    //Writes out the item offsets
                    WriteXrefTableIds(w, xrefs, pos, i);

                    //If we are at the end of the table, we break off
                    if (i == xrefs.Table.Length) break;
                }

                //Otherwise we have to write a subsection

                //Finds the size of the subsection
                pos = ArrayHelper.IndexOfNotNull(xrefs.Table, i);
                i = ArrayHelper.IndexOfNull(xrefs.Table, pos + 1);
                if (i <= pos) break;

                Debug.Assert(false, "Untested code");

                total_size = i;
            }

            //Ret value used to set the size property in the dictionary. 
            //Must be one higher than the highest id.
            return total_size;
        }

        internal static void WriteXrefTableIds(PdfWriter w, ResTracker.XRefTable xrefs, int pos, int end)
        {
            //Writes items.
            for (int c = pos; c < end; c++)
                w.WriteRaw(string.Format("{0:0000000000} {1:00000} n\r\n", xrefs.Offsets[c], xrefs.Table[c].GenerationNr));
        }

        internal static void Notify(WeakList<INotify> listeners, NotifyMSG msg)
        {
            if (listeners != null)
                listeners.Itterate((obj) => { obj.Notify(msg); });
        }

        #endregion

        internal static SealedDocument ConvertToSealed(WritableDocument wd)
        {
            var sd = new SealedDocument(wd._file != null ? wd._file : PdfFile.CreateDummyFile(), wd._root_ref);
            MoveExtraStreams(wd, sd);
            return sd;
        }
    }
}
