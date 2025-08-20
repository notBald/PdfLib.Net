using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Read;


namespace PdfLib.Pdf
{
    /// <summary>
    /// Represents the pages structure in a PDF document
    /// </summary>
    public sealed class PdfPages : Elements, IEnumerable<PdfPage>
    {
        #region Properties

        /// <summary>
        /// Whenever these pages were imported or not.
        /// </summary>
        /// <remarks>
        /// When creating new documents there are a number
        /// of properties that are inherited from PdfPages.
        /// 
        /// For user friendliness these properties need not
        /// be set, instead reasonable defaults are created
        /// as needed.
        /// 
        /// This is controlled by this flag. I don't want
        /// situations where a imported document gets defaults
        /// from here, as it should have all the settings it
        /// needs already.
        /// 
        /// The reason these dedaults are not created straight
        /// away is because they may not be needed. This can
        /// not be known until the document is finished, so
        /// that one can check if any child needs these
        /// settings or not. 
        /// </remarks>
        readonly bool IsImported;

        /// <summary>
        /// Used for saving default resources and cleaning them
        /// up afterwards.
        /// </summary>
        List<string> _remove_list;

        /// <summary>
        /// PdfType.Pages
        /// </summary>
        internal override PdfType Type { get { return PdfType.Pages; } }

        /// <summary>
        /// Required to be indirect
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        /// <summary>
        /// To avoid loading all the pages in a document.
        /// </summary>
        protected override bool Follow { get { return false; } }

        /// <summary>
        /// The PageTree class wants direct access to get at the references
        /// </summary>
        /// <remarks>Only for use by the PageTree</remarks>
        internal PdfDictionary Elements { get { return _elems; } }

        /// <summary>
        /// The parent of this node
        /// </summary>
        /// <remarks>Required, except on the root node</remarks>
        public PdfPages Parent
        { get { return (PdfPages)_elems.GetPdfType("Parent", PdfType.Pages, IntMsg.NoMessage, null); } }

        /// <summary>
        /// Kids of this node
        /// </summary>
        public PageTree Kids 
        { get { return (PageTree)_elems.GetPdfTypeEx("Kids", PdfType.PageTree, IntMsg.NoMessage, this); } }

        /// <summary>
        /// Total number of leaf nodes
        /// </summary>
        public int Leaf_Count { get { return _elems.GetUIntEx("Count"); } }

        // <summary>
        // If this is the root pages object or not.
        // </summary>
        // <remarks>This method isn't reliable. Parent could be "PdfNull"</remarks>
        //public bool IsRoot { get { return !_elems.Contains("Parent"); } }

        #endregion

        #region Inherited properties

        /// <summary>
        /// Resource dictionary
        /// </summary>
        public PdfResources Resources 
        { 
            get 
            { 
                return (PdfResources)Inherit("Resources", PdfType.Resources); 
            } 
        }

        #endregion

        #region Init

        /// <summary>
        /// Used when creating editable documents.
        /// </summary>
        internal PdfPages(bool writable, ResTracker tracker)
            : base(writable, tracker)
        {
            //Type is required on pages
            _elems.SetType("Pages");

            var ptree = new PageTree(writable, tracker, this);
            _elems.SetNewItem("Kids", ptree, false);
            _elems.SetInt("Count", 0);

            IsImported = false;
        }

        /// <summary>
        /// Creates the pages from imported data
        /// </summary>
        internal PdfPages(PdfDictionary dict)
            : base(dict) 
        {
            _elems.CheckTypeEx("Pages");
            IsImported = true;
        }

        #endregion

        #region Dealing with page creation

        /// <summary>
        /// Adds a page
        /// </summary>
        /// <remarks>
        /// As of right now there's no way to disceern
        /// between an imported page and a newly created
        /// page. This means pages created for RWDocument
        /// will not get the "default value" system.
        /// 
        /// I'm thinking making a compromise by creating
        /// a new "PdfPages" object for RW documents and 
        /// tucking it at the end of the existing page 
        /// tree.
        /// <remarks>
        internal void AddPage(PdfPage page)
        {
            // !page.HasReference should be safe to remove, but isn't
            // tested as orphaned pages ain't supported (yet?).
            if (!IsWritable || !page.HasReference)
                throw new NotSupportedException();

            //Add to kids.
            Kids.AddPage(page);

            //Adding parrent last as it prevents the Kids from erroneously 
            //incrementing the refcount
            page.Parent = this;
            UpdateCount();
        }

        /// <summary>
        /// Generates default objects if needed
        /// </summary>
        /// <remarks>Use reference in compatible mode</remarks>
        internal void BeginWrite(SaveMode save_mode)
        {
            //Todo:
            // 1. Make a propper tree
            // 2. Remove unused resource dictionaries (I.e. ColorSpace, etc)

            if (!IsImported)
            {
                //Since there's only two properties that
                //matter I make this simple.
                bool need_mb = _elems.Contains("MediaBox");
                bool need_res = _elems.Contains("Resources");

                //Queries every page to see if it
                //needs default objects.
                foreach (var page in this)
                {
                    if (!need_mb) need_mb = page.NeedDefMB();
                    if (!need_res) need_res = page.NeedDefRes();
                }

                _remove_list = new List<string>(2);

                if (need_mb && !_elems.Contains("MediaBox"))
                {
                    var itm = new PdfRectangle(0, 0, 595.22, 842);
                    _elems.SetNewItem("MediaBox", itm, false);
                    _remove_list.Add("MediaBox");
                }

                if (need_res && !_elems.Contains("Resources"))
                {
                    var itm = _elems.Tracker == null ? new PdfResources() : new PdfResources(new WritableDictionary(_elems.Tracker));
                    _elems.SetNewItem("Resources", itm, false);
                    _remove_list.Add("Resources");
                }

                if (_remove_list.Count == 0)
                    _remove_list = null;
            }
        }

        /// <summary>
        /// Destroys default objects
        /// </summary>
        internal void EndWrite()
        {
            if (_remove_list != null)
            {
                for (int c = 0; c < _remove_list.Count; c++)
                    _elems.Remove(_remove_list[c]);
                _remove_list = null;
            }
        }

        /// <summary>
        /// Updates its count value, as well as the
        /// parent's count value.
        /// </summary>
        private void UpdateCount()
        {
            _elems.SetInt("Count", Kids.Count);
            var parent = Parent;
            if (parent != null)
                parent.UpdateCount();
        }

        #endregion

        #region Inheritance methods

        /// <summary>
        /// A Page can inherit properties from the pages object
        /// </summary>
        /// <param name="key">Property key</param>
        /// <param name="type">Property type</param>
        /// <param name="replace">If the reference should be replaced</param>
        /// <returns>A PdfObject or null if not found</returns>
        internal PdfObject Inherit(string key, PdfType type)
        {
            var ret = _elems.GetPdfType(key, type, IntMsg.NoMessage, null);
            if (ret != null) return ret;
            var parent = Parent;
            if (parent != null) return parent.Inherit(key, type);

            return null;
        }

        /// <summary>
        /// A Page can inherit properties from the PdfPages object
        /// </summary>
        /// <param name="key">Property key</param>
        /// <param name="deref">Set false if you want to chage the type</param>
        /// <returns>A PdfItem or null if not found</returns>
        internal PdfItem Inherit(string key, bool deref)
        {
            var ret = _elems[key];
            if (ret != null) return (deref) ? ret.Deref() : ret;
            var parent = Parent;
            if (parent != null) return parent.Inherit(key, deref);
            return null;
        }

        #endregion

        #region Indexing

        /// <summary>
        /// Fetches a page from the pages
        /// tree
        /// </summary>
        public PdfPage this[int page_nr]
        {
            get
            {
                int count = Leaf_Count;
                if (page_nr < 0 || page_nr >= Leaf_Count)
                    throw new PdfInternalException("Page not found");
                return Kids[page_nr];
            }
        }

        /// <summary>
        /// Finds the index of the page
        /// </summary>
        /// <param name="page">The page to search for</param>
        /// <returns>The index or -1 if not found</returns>
        public int FindPageIndex(PdfPage page)
        {
            int count = Leaf_Count;
            for (int c = 0; c < count; c++)
                if (ReferenceEquals(this[c], page))
                    return c;

            return -1;
        }

        /// <summary>
        /// Finds the index of the unparsed page
        /// </summary>
        /// <param name="page">The page to search for</param>
        /// <returns>The index or -1 if not found</returns>
        internal int FindIndexOf(PdfItem page)
        {
            return Kids.FindIndexOf(page);
        }

        #endregion

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        /// <summary>
        /// Enumerates the pages
        /// </summary>
        public IEnumerator<PdfPage> GetEnumerator() { return new PageTreeEnumerator(Kids, Leaf_Count); }

        /// <remarks>
        /// Todo:
        /// This class is currently not that useful when considering that it
        /// will totaly break if a page can't be parsed. 
        /// </remarks>
        class PageTreeEnumerator : IEnumerator<PdfPage>
        {
            int _i = -1;
            readonly int _end;
            readonly PageTree _tree;
            public PageTreeEnumerator(PageTree tree, int count) { _tree = tree; _end = count; }

            object IEnumerator.Current { get { return Current; } }
            public PdfPage Current { get { return _tree[_i]; } }
            public bool MoveNext() { return ++_i < _end; }
            public void Reset() { _i = -1; }
            public void Dispose() { }
        }

        #endregion

        #region Required overrides

        /// <summary>
        /// Pages does not let themselves be copied from a document to another document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            //Technically one can move pages to different documents, but it's quite rare
            //that one want to.. as one drags along pretty much the entire document.
            //throw new PdfInternalException("Can't move pages to different document");
            return null;
            //Note that "pages" is never a direct object, so when a clone is made of
            //a pdf page it will copy the reference here, and not this object itself.

            //If one want to change this behavior:
            // return new PdfPages(elems);
            //And remove the hack in WritableDocument.AddPage
        }

        #endregion

        #region helper methods

        /// <summary>
        /// This method will try at converting a dictionary into a page,
        /// while keeping all cached right.
        /// </summary>
        /// <param name="dict">Dictionary to convert</param>
        /// <returns>the page or null if it didn't manage</returns>
        internal static PdfPage ParsePage(PdfDictionary dict)
        {
            if (dict.IsType("Page"))
            {
                var root = FindRoot(dict);
                if (root != null)
                {
                    var index = root.FindIndexOf(dict);
                    if (index != -1)
                        return root[index];
                }
            }
            return null;
        }

        private static PdfPages FindRoot(PdfDictionary dict)
        {
            if (!dict.Contains("Parent"))
                return null;

            //Using the Tortoise and the Hare Algorithm by Robert W. Floyd
            PdfItem slow = dict, fast = dict;

            while (true)
            {
                //1 slow hop.
                slow = ((PdfDictionary)slow)["Parent"].Deref();

                //2 fast hops.
                fast = ((PdfDictionary)fast)["Parent"].Deref();

                if (fast is PdfPages)
                    return (PdfPages)fast;
                if (!(fast is PdfDictionary))
                    return null;
                var fast_dict = (PdfDictionary)fast;
                if (!fast_dict.Contains("Parent"))
                    return null;

                fast = fast_dict["Parent"].Deref();

                if (fast is PdfPages)
                    return (PdfPages)fast;
                if (!(fast is PdfDictionary))
                    return null;
                if (!((PdfDictionary)fast).Contains("Parent"))
                    return null;

                //If the two are equal, we have a loop.
                if (ReferenceEquals(slow, fast))
                    return null;
            }
        }

        #endregion

        #region Structs

        /// <summary>
        /// Used to send the count and res tracker to the kids array
        /// </summary>
        internal struct KidsMsg
        {
            public ResTracker Tracker;
            public int Count;
            public KidsMsg(int count, ResTracker tracker) { Count = count; Tracker = tracker; }
        }

        #endregion
    }
}
