using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Represents a page tree structure, flattening it for readers
    /// </summary>
    /// <remarks>
    /// A corrupt pageteee can give an "unlimited number of pages".
    /// 
    /// This is avoided by checking if the parent is set correctly 
    /// and bailing when it isn't. (The latter can be improved by 
    /// ignoring invalid pages instead.)
    /// 
    ///	A quick test of corrupt PDF pagetree:
    ///	- Reduced loop: Sumatra and Evince ignores affected pages, 
    ///	                Adobe X spins eternally, GSView and BAUpload stack overflows.
    ///	 (^root->pt1<->pt1 (pt1's parent also go to pt1))
    ///	- Larger loop: Sumatra ignores affected pages, Adobe X renders the document fine.
    ///	 (^root->pt1->pt2->root)
    ///	- Parent set wrong: Ignored by Sumatra/Evince. GSView loops eternally, Adobe X 
    ///	                    throws an error when rendering affected pages, but displays 
    ///	                    unaffected pages fine. BAUpload ignores as well.
    /// - Parent set on root (which is forbidden by the specs): Not yet tested.
    /// 
    /// Conclusion: Rejecting documents with corrupt pagetree is fine. Adobe manages the 
    ///             larger loop, but still fails on the small one so I assume this isn't 
    ///             something they've spent any time on.
    /// </remarks>
    public sealed class PageTree : ItemArray
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.PageTree
        /// </summary>
        //[DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal override PdfType Type { get { return PdfType.PageTree; } }

        /// <summary>
        /// Total number of pages
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public int Count
        { 
            get 
            {
                int count = 0;
                for (int c = 0; c < _items.Length; c++)
                {
                    var item = _items[c].Deref();
                again:
                    if (item is PdfPage)
                        count++;
                    else if (item is PdfPages)
                        count += ((PdfPages)item).Leaf_Count;
                    else
                    {
                        if (!(item is PdfDictionary))
                            throw new PdfReadException(ErrSource.Array, PdfType.Pages, ErrCode.WrongType);
                        var type = ((PdfDictionary)item).GetNameEx("Type");
                        if ("Page".Equals(type))
                            item = _items[c].ToType(PdfType.Page, IntMsg.NoMessage, null);
                        else if ("Pages".Equals(type))
                            item = _items[c].ToType(PdfType.Pages, IntMsg.NoMessage, null);
                        else throw new PdfCastException(ErrSource.Array, PdfType.Pages, ErrCode.WrongType);
                        goto again;
                    }
                }

                return count; 
            } 
        }

        /// <summary>
        /// Needed to check for loops
        /// </summary>
        /// <remarks>Now also used to feed pages a ref to the owning catalog</remarks>
        private readonly PdfPages _pages;

        #endregion

        #region Init

        /// <summary>
        /// Creates an empty page tree.
        /// </summary>
        internal PageTree(bool writable, ResTracker tracker, PdfPages pages)
            : base(writable, tracker)
        { _pages = pages; }

        /// <summary>
        /// Constructor for page trees made from existing documents
        /// </summary>
        /// <param name="kids">List of indirect references that make up the page tree</param>
        /// <param name="track">Optional ResTracker that can fix if kids are direct objects</param>
        /// <remarks>
        /// Note: The specs require that the kids to be indirect references, but both
        ///       sumatra and adobe reader opens both.
        ///       
        ///       However while opening both is okay, I'll stick to saving within the 
        ///       specs
        /// 
        /// Implementation is a bit weird and needs a rethink. In any case, the top of
        /// the page tree will always be parsed into nodes and pages. Would be better
        /// perhaps to let the node figure out if it's a node or a page (when there is
        /// a need).
        /// </remarks>
        private PageTree(PdfArray kids, PdfPages pages)
            :base(kids)
        { _pages = pages; }

        /// <summary>
        /// Creates a page tree
        /// </summary>
        /// <remarks>
        /// Not using the constructor so that the function can create
        /// a repaired PdfArray if needed.
        /// </remarks>
        internal static PageTree Create(PdfArray kids, PdfPages pages)
        {
            if (kids is WritableArray)
            {
                //Note that WritableArray never returns null for the tracker
                var tracker = kids.Tracker;

                for (int c = 0; c < kids.Length; c++)
                {
                    var aref = kids[c];
                    if (!(aref is WritableReference))
                    {
                        if (!(aref is IRef)) throw new PdfCastException(ErrSource.Pages, PdfType.Item, ErrCode.UnexpectedObject);
                        var iref = (IRef)aref;

                        //Creates a new writable reference, refcounts it
                        //and adds it to the array.
                        iref.Reference = tracker.CreateWRef(aref);
                        PdfReference r = iref.Reference;
                        tracker.RefCountRef(ref r);
                        kids[c] = r;
                    }
                }
            }
            else
            {
                for (int c = 0; c < kids.Length; c++)
                {
                    var kid = kids[c];
                    if (!(kid is PdfReference))
                    {
                        //Creates a new repaired PdfArray
                        var refs = new PdfItem[kids.Length];
                        for (c = 0; c < kids.Length; c++)
                        {
                            kid = kids[c];
                            refs[c] = (kid is PdfReference) ?  kid : new TempReference(kid);
                            //TempReference is not temporary when in "read" mode,
                            //but then it does not matter.
                        }
                        kids = new SealedArray(refs);
                        break;
                    }
                }
            }

            return new PageTree(kids, pages);
        }

        #endregion

        #region Indexing

        public PdfPage this[int index]
        {
            get
            {
                int count = 0;
                for (int c = 0; c < _items.Length; c++)
                {
                    var item = _items[c].Deref();
                again:
                    if (item is PdfPage)
                    {
                        if (index == count) return (PdfPage)item;
                        count++;
                    }
                    else if (item is PdfPages)
                    {
                        var pos = count;
                        var pages = (PdfPages)item;
                        count += pages.Leaf_Count;
                        if (index < count)
                        {
                            //This is to protect againts loops in the page tree structure.
                            //One can alternatly simply ignore pages where the parrent doesn't
                            //lead back here, and that way be able to open corrupt documents
                            //safely.
                            if (!ReferenceEquals(pages.Parent, _pages))
                                throw new PdfReadException(PdfType.Pages, ErrCode.IsCorrupt);

                            return pages[index - pos];
                        }
                    }
                    else 
                    {
                        if (!(item is PdfDictionary))
                            throw new PdfReadException(ErrSource.Array, PdfType.Pages, ErrCode.WrongType);
                        var type = ((PdfDictionary)item).GetNameEx("Type");
                        if ("Page".Equals(type))
                            item = _items.GetPdfTypeEx(c, PdfType.Page);
                        else if ("Pages".Equals(type))
                            item = _items.GetPdfTypeEx(c, PdfType.Pages);
                        else throw new PdfCastException(ErrSource.Array, PdfType.Pages, ErrCode.WrongType);
                        goto again;
                    }
                }

                throw new PdfInternalException("Page not found");
            }
        }

        #endregion

        /// <summary>
        /// This method can be used to find the index of a unparsed page
        /// </summary>
        /// <param name="page">Raw page item to search for</param>
        /// <returns>The page</returns>
        internal int FindIndexOf(PdfItem page)
        {
            int index = 0;
            for (int c = 0; c < _items.Length; c++)
            {
                var item = _items[c].Deref();
                if (ReferenceEquals(page, item))
                    return index;
                if (item is PdfDictionary)
                {
                    var dict = (PdfDictionary)item;
                    if (dict.IsType("Pages"))
                        item = _items.GetPdfTypeEx(c, PdfType.Pages);
                }
                if (item is PdfPages)
                {
                    var pages = (PdfPages)item;
                    if (!ReferenceEquals(pages.Parent, _pages))
                        continue;
                    int i = pages.FindIndexOf(page);
                    if (i != -1)
                        return index + i;
                    index += pages.Leaf_Count;
                }
                else
                {
                    //Helps when debugging, as browsing the page tree changes the object
                    if (item is PdfPage && PdfPage.IsSamePage(page as PdfDictionary, (PdfPage)item))
                        return index;
                    index++;
                }
            }
            return -1;
        }

        /// <summary>
        /// Adds a page to this node
        /// </summary>
        /// <param name="page">The page to add</param>
        internal void AddPage(PdfPage page)
        {
            //This check isn't needed anymore, can be safely removed. This
            //because a page without reference would simply be adopted.
            // (However orphaned pages ain't yet supported so...)
            if (!page.HasReference)
                throw new NotSupportedException("Page must be indirect");

            _items.AddItem(page, true);
        }

        /// <summary>
        /// Used when moving the array to a different document
        /// </summary>
        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            //throw new PdfInternalException("Can't move a pagetree into another document.");
            return null;
        }

        #region private classes and structs


        #endregion
    }
}
