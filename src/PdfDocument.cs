using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using System.IO;

namespace PdfLib
{
    /// <summary>
    /// A PDF document
    /// </summary>
    /// <remarks>
    /// A PDF document can either be created or imported. Imported
    /// documents can not be saved to disk. 
    /// </remarks>
    public abstract class PdfDocument : IEnumerable<PdfPage>, IDisposable
    {
        #region Variables and properties

        /// <summary>
        /// All the pages in the document
        /// </summary>
        protected PdfPages _pages;

        /// <summary>
        /// The root of the document
        /// </summary>
        protected PdfReference _root_ref;

        private struct FileStr
        {
            public Stream S;
            public string FilePath;
        }
        private FileStr[] _extra_streams;

        /// <summary>
        /// Whenever this document can be modified or not
        /// </summary>
        public abstract bool IsWritable { get; }

        /// <summary>
        /// Number of pages in the document.
        /// </summary>
        public int NumPages 
        { 
            get 
            {
                if (_pages == null) SetPages();
                return _pages.Kids.Count; 
            } 
        }

        /// <summary>
        /// Harddrive file for this document (if any)
        /// </summary>
        public virtual PdfFile File { get { return null; } }

        public abstract PdfInfo Info { get; }

        public PdfDocumentID ID
        {
            get
            {
                var file = File;
                if (file == null) return null;
                return file.ID;
            }
        }

        /// <summary>
        /// Document outline
        /// </summary>
        public PdfOutline Outlines 
        { 
            get { return Catalog.Outlines; }
            set { Catalog.Outlines = value; }
        }

        public PdfPageMode PageMode
        {
            get { return Catalog.PageMode; }
            set { Catalog.PageMode = value; }
        }

        public virtual PdfPageLayout PageLayout
        {
            get { return Catalog.PageLayout; }
            set { Catalog.PageLayout = value; }
        }

        public PdfDestDictionary Destinations
        {
            get { return Catalog.Dests; }
            set { Catalog.Dests = value; }
        }

        /// <summary>
        /// The name tree
        /// </summary>
        public PdfNameDictionary Names
        {
            get { return Catalog.Names; }
            set { Catalog.Names = value; }
        }

        /// <summary>
        /// Returns a list off all non-inline images in the document.
        /// </summary>
        /// <remarks>
        /// A speedier way of implementing this would be to simply
        /// iterate the document's xref or resource tables. However
        /// then one has to do the extra work of converting plain 
        /// dictionaries to images.
        /// </remarks>
        public PdfImage[] AllImages
        {
            get
            {
                var list = new List<PdfImage>();
                foreach (PdfPage page in this)
                {
                    var images = page.Resources.XObject.AllImages;
                    foreach (PdfImage image in images)
                        if (!list.Contains(image))
                            list.Add(image);
                }
                return list.ToArray();
            }
        }

        /// <summary>
        /// The root of the document
        /// </summary>
        internal PdfCatalog Catalog { get { return (PdfCatalog)_root_ref.ToType(PdfType.Catalog); } }

        #endregion

        #region Init and dispose

        /// <summary>
        /// Document constructor
        /// </summary>
        /// <param name="root_ref">Root node</param>
        internal PdfDocument(PdfReference root_ref)
        {
            _root_ref = root_ref;
        }

        internal PdfDocument()
        { }

        /// <summary>
        /// Disposes PDF file
        /// </summary>
        public void Dispose()
        {
            DisposeImpl();

            if (_extra_streams != null)
            {
                for (int c = 0; c < _extra_streams.Length; c++)
                {
                    if (_extra_streams[c].S != null)
                    {
                        _extra_streams[c].S.Dispose();
                        _extra_streams[c].S = null;
                    }
                }

                _extra_streams = null;
            }
        }

        protected abstract void DisposeImpl();

        /// <summary>
        /// Closes the Pdf file
        /// </summary>
        public void Close() { Dispose(); }

        #endregion

        #region Indexing and enum

        /// <summary>
        /// Retrive page by index
        /// </summary>
        /// <param name="index">Index of the page</param>
        /// <returns>The page at the index</returns>
        public PdfPage this[int index] 
        { 
            get 
            {
                if (_pages == null) SetPages();
                return _pages[index]; 
            } 
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        /// <summary>
        /// Enumerator for itterating over the pages in the document
        /// </summary>
        public IEnumerator<PdfPage> GetEnumerator() 
        { if (_pages == null) SetPages(); return _pages.GetEnumerator(); }

        #endregion

        /// <summary>
        /// Loads as much data as it can into the cache.
        /// </summary>
        /// <param name="load_streams">If stream data is to be loaded into memory</param>
        /// <returns>If there was no errors during the loading.</returns>
        /// <remarks>
        /// Since the function can not know if a resource
        /// is needed or not, it will not throw if a corrupt
        /// resource is encountered.
        /// 
        /// One can try using "FixXRefTable" to clear up the
        /// issue (assuming the failed resource is junk data)
        /// </remarks>
        public abstract bool LoadIntoMemory(bool load_streams);
        public bool LoadIntoMemory() { return LoadIntoMemory(true); }

        /// <summary>
        /// This function fixes issues in the XRef table
        /// </summary>
        /// <remarks>
        /// Set internal because it's a confusing function. Insteasd, use PdfDocument.Rebuild or PdfFile.open(..., force rebuild = true)
        /// </remarks>
        internal abstract void FixXRefTable();

        /// <summary>
        /// Updates the pages cache
        /// </summary>
        protected void SetPages()
        {
            _pages = Catalog.Pages;

            //To prevent eternal loops in the page structure we never follow a page
            //where the first parrent isn't null. (looking at the specs I see that
            //Parent is prohibited in the root node, probably with an eye towards
            //this issue)
            if (_pages.Parent != null)
            {
                //One could alternatly force the parent null, and that way be able to
                //safly open a corrupt document. 
                _pages = null;
                throw new PdfReadException(PdfType.Pages, ErrCode.IsCorrupt);
            }
        }

        internal void AddStreamToDispose(Stream stream, string _file_path = null)
        {
            if (_extra_streams == null)
                _extra_streams = new FileStr[1];
            else
                Array.Resize<FileStr>(ref _extra_streams, _extra_streams.Length + 1);
            _extra_streams[_extra_streams.Length - 1] = new FileStr() { FilePath = _file_path, S = stream };
        }

        protected static void MoveExtraStreams(PdfDocument from, PdfDocument to)
        {
            to._extra_streams = from._extra_streams;
        }


        /// <summary>
        /// Creates a new SealedDocument from a on disk file. Note, the document must be open. Afterwards, the
        /// document will be closed.
        /// </summary>
        /// <param name="doc">Source document</param>
        /// <returns>A new document</returns>
        public static PdfDocument Rebuild(PdfDocument doc)
        {
            var owner = doc.File;
            if (owner == null)
                throw new NotSupportedException("Document must be opened from a file.");
            return PdfFile.Rebuild(owner, !doc.IsWritable);
        }
    }
}
