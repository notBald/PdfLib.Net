using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Pdf
{
    [DebuggerDisplay("{Title}")]
    public sealed class PdfOutlineItem : PdfOutline
    {
        #region Variables and properties

        /// <summary>
        /// The number of child outlines
        /// </summary>
        new public int Count { get { return Math.Abs(_elems.GetInt("Count", 0)); } }

        /// <summary>
        /// First child outline
        /// </summary>
        /// <remarks>Can be null</remarks>
        new public PdfOutlineItem First { get { return (PdfOutlineItem)_elems.GetPdfType("First", PdfType.Outline, IntMsg.NoMessage, _catalog); } }

        /// <summary>
        /// Last child outline
        /// </summary>
        /// <remarks>Can be null</remarks>
        new public PdfOutlineItem Last { get { return (PdfOutlineItem)_elems.GetPdfType("Last", PdfType.Outline, IntMsg.NoMessage, _catalog); } }

        /// <summary>
        /// Previous bookmark
        /// </summary>
        /// <remarks>Can be null</remarks>
        public PdfOutlineItem Prev 
        { 
            get { return (PdfOutlineItem)_elems.GetPdfType("Prev", PdfType.Outline, IntMsg.NoMessage, _catalog); }
            internal set { _elems.SetItem("Prev", value, true); }
        }

        /// <summary>
        /// Next bookmark
        /// </summary>
        /// <remarks>Can be null</remarks>
        public PdfOutlineItem Next 
        { 
            get { return (PdfOutlineItem)_elems.GetPdfType("Next", PdfType.Outline, IntMsg.NoMessage, _catalog); }
            internal set { _elems.SetItem("Next", value, true); }
        }

        /// <summary>
        /// Parent outline
        /// </summary>
        public PdfOutline Parent 
        { 
            get { return (PdfOutline)_elems.GetPdfTypeEx("Parent", PdfType.Outline, IntMsg.RootOutline, _catalog); }
            internal set { _elems.SetItem("Parent", value, true); }
        }

        /// <summary>
        /// Text that is to be displayed for this bookmark
        /// </summary>
        public string Title
        {
            get { return _elems.GetUnicodeStringEx("Title"); }
            set { _elems.SetUnicodeStringEx("Title", value); }
        }

        /// <summary>
        /// The destination this outline points at, if any. 
        /// </summary>
        public PdfDestination Destination
        {
            get
            {
                var dest = Dest;
                if (dest != null)
                    return FindDestination(dest, _elems, "Dest", _catalog);
                var a = A;
                if (a != null)
                    return FindDestAction(a, 0);                    
                return null;
            }
        }

        private PdfDestination FindDestAction(PdfActionArray ar, int call_depth)
        {
            foreach (var a in ar)
            {
                var dest = FindDestAction(a, call_depth);
                if (dest != null) return dest;
            }
            return null;
        }

        private PdfDestination FindDestAction(PdfAction a, int call_depth)
        {
            if (a is GoToAction)
                return ((GoToAction)a).Destination;

            //Breaks off the search in case the linked list is cyclic.
            if (call_depth >= SR.CALL_DEPTH)
                return null;

            var next_action = a.Next;
            if (next_action != null)
                return FindDestAction(next_action, ++call_depth);

            return null;
        }

        /// <summary>
        /// The page this outline points to, if any.
        /// </summary>
        public PdfPage Page 
        { 
            get 
            { 
                var dest = Destination;
                if (dest == null) return null;
                return dest.Page;
            }
        }

        public int PageNr
        {
            get
            {
                var page = Page;
                if (page == null || page.Parent == null || _catalog == null)
                    return -1;
                
                //It's possible to figure out the page number by using
                //page.Parent (as that is a link to the page tree) but
                //it's a non-tivial affair so we do a slow search instead.
                //
                //Note it's safe to do a ReferenceEquals comparison, as
                //the page object will always be taken out of the pages
                //array. It is posible to take a page out of the document
                //and then run a cache purge function, i.e. that one can 
                //have two instances of the same page, but that isn't a 
                //problem here as one take a page out of the pages and
                //ReferenceEquals that page with page objects in pages.

                return _catalog.Pages.FindPageIndex(page);
            }
        }

        /// <summary>
        /// Finds a destination object
        /// </summary>
        /// <param name="dest">The destination object, name or string</param>
        /// <param name="elems">A dictionary that may be updated</param>
        /// <param name="update">The entery in the dictionary that may be updated</param>
        /// <param name="catalog">A catlog that will be searched</param>
        /// <returns></returns>
        internal static PdfDestination FindDestination(PdfItem dest, PdfDictionary elems, string update, PdfCatalog catalog)
        {
            if (dest == null) return null;
            dest = dest.Deref();
            if (dest is PdfDestination)
                return (PdfDestination)dest;
            if (dest is PdfArray)
            {
                dest = elems.GetPdfType(update, PdfType.Destination);
                if (dest is PdfDestination)
                    return (PdfDestination)dest;

                //dest can potentially be a PdfDestinationDict
                if (dest != null)
                    throw new PdfCastException(PdfType.Destination, ErrCode.Wrong);
            }

            //If there's no catalog, then this outline is outside the document. Just return null then.
            if (catalog == null)
                return null;

            //The specs say this can either be a byte string or a name. Now,
            //with the way GetString() is implemented this should work regardless.
            //
            //Note that these strings can be in unicode, but since they are never
            //displayed there's no need to worry about that.
            var id = dest.GetString();

            //Looks into Names first
            var names = catalog.Names;
            if (names != null)
            {
                var d = names.Dests;
                if (d != null)
                {
                    var n = d[id];
                    if (n != null)
                        return n;
                }
            }

            //Then look into Dests
            var dests = catalog.Dests;
            if (dests != null)
                return dests[id];

            //Destination was not found.
            return null;
        }

        /// <summary>
        /// The destination to display when selected.
        /// </summary>
        internal PdfItem Dest { get { return _elems["Dest"]; } }

        /// <summary>
        /// Action to perform when clicked
        /// </summary>
        [PdfVersion("1.1")]
        internal PdfAction A { get { return (PdfAction) _elems.GetPdfType("A", PdfType.Action, IntMsg.NoMessage, _catalog); } }
        
        /// <summary>
        /// Indirect pointer to structured data (see 14.7.2 in the specs)
        /// </summary>
        [PdfVersion("1.3")]
        internal PdfDictionary SE { get { return _elems.GetDictionary("SE"); } }

        /// <summary>
        /// Title text color
        /// </summary>
        [PdfVersion("1.4")]
        public PdfColor C 
        { 
            get 
            {
                var color = (RealArray) _elems.GetPdfType("C", PdfType.RealArray);
                if (color == null)
                    return new DblColor(0, 0, 0);
                if (color.Length != 3)
                    throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return new DblColor(color[0], color[1], color[2]);
            }
            set
            {
                if (value == null || PdfColor.Equals(value, PdfColor.BLACK))
                    _elems.Remove("C");
                else
                    _elems.SetItem("C", new RealArray(value.ToDblColor().ToArray()), false);
            }
        }

        /// <summary>
        /// Text style for the title text
        /// </summary>
        [PdfVersion("1.4")]
        public TextStyle F 
        { 
            get { return (TextStyle) _elems.GetUInt("F", 0); }
            set { _elems.SetUInt("F", (uint)value, 0); }
        }

        #endregion

        #region Init

        internal PdfOutlineItem(PdfDictionary dict, PdfCatalog doc)
            : base(dict, doc) { }

        /// <summary>
        /// Creates a bookmark with a title and a page as destination
        /// </summary>
        /// <param name="title">The title of the bookmark</param>
        /// <param name="destination">The page the bookmark is to point towards</param>
        public PdfOutlineItem(string title, PdfPage destination)
        {
            Title = title;
            _elems.SetItem("Dest", new PdfDestination(destination, new PdfDestination.Fit()), false);
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOutlineItem(elems, ((WritableDictionary)elems).Tracker.Doc.Catalog);
        }

        #endregion

        [Flags()]
        public enum TextStyle
        {
            Normal,
            Italic,
            Bold,
        }
    }
}
