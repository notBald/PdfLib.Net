using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Read;
using PdfLib.Pdf.Annotation;
using PdfLib.Pdf.Transparency;


namespace PdfLib
{
    /// <summary>
    /// A page in the PDF document
    /// </summary>
    /// <remarks>
    /// A page is technically an "XObject" in some functionality, but
    /// has a type of "Page"
    /// </remarks>
    [System.Diagnostics.DebuggerDisplay("PdfPage {Index}")]
    public sealed class PdfPage : Elements, IWPage, IInherit
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Page
        /// </summary>
        internal override PdfType Type { get { return PdfType.Page; } }

        /// <summary>
        /// If false then this is a replacement page for a page that
        /// could not be parsed.
        /// </summary>
        /// <remarks>Only relevant when enumerating a document.</remarks>
        public bool Valid { get { return _elems.GetBool("ĬValid", true); } }

        /// <summary>
        /// While adobe reader can open direct pages, the specs is clear
        /// that the "Kids" array is to be indirect. 
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Compressed; } }

        /// <summary>
        /// The parent of this page
        /// </summary>
        /// <remarks>
        /// While changing a PdfPage's parent is O.K. manipulating a parent's 
        /// dictionary can have consequences for the page. Impl. some sort of 
        /// event system, making the PdfPages dictionary private as well.
        /// </remarks>
        public PdfPages Parent 
        { 
            get { return (PdfPages)_elems.GetPdfTypeEx("Parent", PdfType.Pages); }
            internal set 
            { 
                //For now this will be internal.
                _elems.SetItem("Parent", value, true);
            }
        }

        /// <summary>
        /// This document's index in the parent tree. -1 if no index
        /// </summary>
        public int Index
        {
            get
            {
                var parent = Parent;
                if (parent != null)
                {
                    while (parent.Parent != null)
                        parent = parent.Parent;
                    int index = 0;
                    foreach (var page in parent)
                    {
                        if (ReferenceEquals(page, this))
                            return index;
                        index++;
                    }
                }
                return -1;
            }
        }

        /// <summary>
        /// When the page's content was most recently modified
        /// </summary>
        public PdfDate LastModified { get { return (PdfDate)_elems.GetPdfType("LastModified", PdfType.Date); } }

        /// <summary>
        /// Resource dictionary
        /// </summary>
        public PdfResources Resources 
        {
            [DebuggerStepThrough()]
            get 
            { 
                var ret = (PdfResources)Inherit("Resources", PdfType.Resources, false);

                if (ret == null) 
                {
                    if (IsWritable)
                    {
                        var track = _elems.Tracker;
                        var dict = (track == null) ? 
                            (PdfDictionary) new TemporaryDictionary() : new WritableDictionary(track);
                        ret = new PdfResources(dict);
                        _elems.SetNewItem("Resources", ret, false);
                    }
                    else
                        ret = new PdfResources(new SealedDictionary());
                }

                return ret;
            }
            set { _elems.SetItem("Resources", value, true); }
        }

        /// <summary>
        /// Bounderies of the physical medium the page shall be
        /// rendered to.
        /// </summary>
        public PdfRectangle MediaBox 
        { 
            get 
            { 
                var ret = (PdfRectangle)Inherit("MediaBox", PdfType.Rectangle, false);
                if (ret == null) return new PdfRectangle(0, 0, 595.22, 842);
                return ret;
            }
            set { _elems.SetItem("MediaBox", (value != null) ? value.Flatten() : null, false); }
        }

        /// <summary>
        /// A rectangle that define the visible region of the default user space
        /// (See 14.12.2 Page Boundaries)
        /// </summary>
        /// <remarks>Default value is the MediaBox</remarks>
        public PdfRectangle CropBox 
        { 
            get 
            {
                var ret = (PdfRectangle)Inherit("CropBox", PdfType.Rectangle, false);
                if (ret == null) return MediaBox;
                return ret;
            }
            set { _elems.SetItem("CropBox", (value != null) ? value.Flatten() : null, false); }
        }

        /// <summary>
        /// A rectangle that define the intended dimensions of the finished page
        /// in a production enviroment
        /// </summary>
        /// <remarks>Default value is the CropBox</remarks>
        [PdfVersion("1.3")]
        public PdfRectangle BleedBox 
        { 
            get 
            {
                var ret = (PdfRectangle)Fetch("BleedBox", PdfType.Rectangle, false);
                if (ret == null) return CropBox;
                return ret;
            }
            set { _elems.SetItem("BleedBox", value.Flatten(), false); }
        }

        /// <summary>
        /// A rectangle that define the intended dimensions of the finished page
        /// (See 14.12.2 Page Boundaries)
        /// </summary>
        /// <remarks>Default value is the CropBox</remarks>
        [PdfVersion("1.3")]
        public PdfRectangle TrimBox 
        { 
            get 
            { 
                var ret = (PdfRectangle)Fetch("TrimBox", PdfType.Rectangle, false);
                if (ret == null) return CropBox;
                return ret;
            }
            set { _elems.SetItem("TrimBox", value.Flatten(), false); }
        }

        /// <summary>
        /// A rectangle that define the extent of the page's meaningful content
        /// (See 14.12.2 Page Boundaries)
        /// </summary>
        [PdfVersion("1.3")]
        public PdfRectangle ArtBox 
        { 
            get 
            {
                var ret = (PdfRectangle)Fetch("ArtBox", PdfType.Rectangle, false);
                return (ret == null) ? CropBox : ret;
            }
            set { _elems.SetItem("ArtBox", value.Flatten(), false); }
        }

        /// <summary>
        /// A box colour information dictionary. See 14.11.2.2, "Display of Page Boundaries".
        /// </summary>
        [PdfVersion("1.4")]
        public PdfDictionary BoxColorInfo { get { return (PdfDictionary)Fetch("BoxColorInfo", PdfType.Dictionary, false); } }

        /// <summary>
        /// The width of the page after cropping
        /// </summary>
        public double Width 
        { 
            get { return CropBox.Width; }
            set 
            {
                var md = (PdfRectangle) Inherit("CropBox", PdfType.Rectangle, false);
                if (md != null)
                {
                    CropBox = new PdfRectangle(md.LLx, md.LLy, md.LLx + value, md.URy);
                }
                else
                {
                    md = MediaBox;
                    MediaBox = new PdfRectangle(md.LLx, md.LLy, md.LLx + value, md.URy);
                }
            }
        }

        /// <summary>
        /// The height of the page after cropping
        /// </summary>
        public double Height 
        { 
            get { return CropBox.Height; }
            set
            {
                var md = (PdfRectangle) Inherit("CropBox", PdfType.Rectangle, false);
                if (md != null)
                {
                    CropBox = new PdfRectangle(md.LLx, md.LLy, md.URx, md.LLy + value);
                }
                else
                {
                    md = MediaBox;
                    MediaBox = new PdfRectangle(md.LLx, md.LLy,  md.URx, md.LLy + value);
                }
            }
        }

        /// <summary>
        /// A content stream that describe the contents of this page. 
        /// If this entry is absent, the page shall be empty.
        /// </summary>
        public PdfContentArray Contents
        {
            get
            {
                return (PdfContentArray)_elems.GetPdfType("Contents", 
                    PdfType.ContentArray, 
                    PdfType.Array, 
                    true, 
                    _elems.IsWritable ? IntMsg.ResTracker : IntMsg.NoMessage, 
                    _elems.Tracker);
            }
            set
            {
                _elems.SetItem("Contents", value, true);
            }
        }

        /// <summary>
        /// Page rotation. Must be in multiples of 90
        /// </summary>
        /// <remarks>
        /// Optional with a default value of 0. With that
        /// in mind I treat 0 and null the same. 
        /// </remarks>
        public int Rotate 
        { 
            get 
            { 
                var obj = Inherit("Rotate", false);
                if (obj == null) return 0;
                //Normalize values
                int val = (int) obj.GetReal();
                while (val < 0) val += 360;
                while (val > 360) val -= 360;
                return val;
            }
            set
            {
                if (value == 0)
                    _elems.Remove("Rotate");
                _elems.SetInt("Rotate", value);
            }
        }


        /// <summary>
        /// The overal orientation of the page, as set by the rotation property.
        /// 0, 180 is Portrait
        /// 90, 270 is Landscape
        /// </summary>
        public PdfPageOrientation Orientation
        {
            get { return Math.Abs((Rotate / 90)) % 2 == 1 ? PdfPageOrientation.Landscape : PdfPageOrientation.Portrait; }
        }

        /// <summary>
        /// A group attributes dictionary that shall specify the attributes of 
        /// the page’s page group for use in the transparent imaging model 
        /// (see 11.4.7, "Page Group" and 11.6.6, "Transparency Group XObjects")
        /// </summary>
        [PdfVersion("1.4")]
        public PdfTransparencyGroup Group 
        {
            get { return (PdfTransparencyGroup)_elems.GetPdfType("Group", PdfType.Group); }
            set { _elems.SetItem("Group", value, true); }
        }

        /// <summary>
        /// A stream object that shall define the page’s thumbnail image 
        /// (see 12.3.4, "Thumbnail Images")
        /// </summary>
        /// <remarks>
        /// Thumbnails are a bit different that other images. Therefore a 
        /// PdfBool.True is passed along to make it clear that this is
        /// a thumbnail.
        /// </remarks>
        public PdfImage Thumb 
        { 
            get 
            { 
                return (PdfImage)_elems.GetPdfType("Thumb", PdfType.XObject, IntMsg.Thumbnail, Resources); 
            } 
        }

        /// <summary>
        /// An array that shall contain indirect references to all article 
        /// beads appearing on the page (see 12.4.3, "Articles").
        /// </summary>
        [PdfVersion("1.1")]
        public PdfArray B { get { return _elems.GetArray("B"); } }

        /// <summary>
        /// The page’s display duration
        /// </summary>
        [PdfVersion("1.1")]
        public double? Dur { get { return _elems.GetNumber("Dur"); } }

        /// <summary>
        /// A transition dictionary describing the transition effect 
        /// </summary>
        [PdfVersion("1.1")]
        public PdfDictionary Trans { get { return _elems.GetDictionary("Trans"); } }

        /// <summary>
        /// An array of annotation dictionaries
        /// (see 12.5, "Annotations")
        /// </summary>
        public AnnotationElms Annots 
        { 
            get 
            { 
                var annot = (AnnotationElms)_elems.GetPdfType("Annots", PdfType.AnnotationElms, IntMsg.Owner, this);
                if (annot == null && IsWritable)
                {
                    _elems.InternalSet("Annots", PdfBool.True);
                    annot = new AnnotationElms(_elems.IsWritable, _elems.Tracker, this);
                    _elems.SetItem("Annots", annot, true);
                }
                return annot;
            }
            set
            {
                _elems.SetItem("Annots", value, true);
                _elems.InternalSet("Annots", PdfBool.False);
            }
        }

        /// <summary>
        /// An additional-actions dictionary that shall define actions
        /// to be performed when the page is opened or closed
        /// (see 12.6.3, "Trigger Events")
        /// </summary>
        [PdfVersion("1.2")]
        public PdfDictionary AA { get { return _elems.GetDictionary("AA"); } }

        /// <summary>
        /// A metadata stream that shall contain metadata for the page
        /// </summary>
        [PdfVersion("1.4")]
        public PdfStream Metadata { get { return (PdfStream)_elems.GetPdfType("Metadata", PdfType.Dictionary); } }

        /// <summary>
        /// The page’s preferred zoom (magnification) factor: the factor by 
        /// which it shall be scaled to achieve the natural display magnification
        /// </summary>
        [PdfVersion("1.3")]
        public double? PZ { get { return _elems.GetNumber("PZ"); } }

        /// <summary>
        /// A name specifying the tab order that shall be used for annotations on 
        /// the page. The possible values shall be R (row order), C (column order), 
        /// and S (structure order). See 12.5, "Annotations" for details.
        /// </summary>
        [PdfVersion("1.5")]
        public string Tabs { get { return _elems.GetName("Tabs"); } }

        #region Marked Contents variables

        /// <summary>
        /// The integer key of the form XObject’s entry in the structural parent tree
        /// </summary>
        /// <remarks>
        /// A object that has this set contains marked content. It can not be
        /// marked. (I.e. StructParent and StructParents is mutualy exlusive)
        /// </remarks>
        public PdfInt StructParents { get { return _elems.GetUIntObj("StructParents"); } }

        #endregion

        #endregion

        #region Init

        public PdfPage()
            : base(new TemporaryDictionary())
        {
            _elems.SetType("Page");
        }

        public PdfPage(double width, double height)
            : base(new TemporaryDictionary())
        {
            _elems.SetType("Page");
            MediaBox = new PdfRectangle(0, 0, width, height);
        }

        /// <summary>
        /// Constructor for pages created for a writable document
        /// </summary>
        internal PdfPage(bool writable, ResTracker tracer)
            : base(writable, tracer)
        {
            _elems.SetType("Page");
        }

        /// <summary>
        /// Constructor for pages imported from a file
        /// </summary>
        /// <param name="dict">Elements</param>
        internal PdfPage(SealedDictionary dict)
            : base (dict)
        {
            _elems.CheckTypeEx("Page");
        }

        /// <summary>
        /// Constructor for pages imported from a file
        /// </summary>
        /// <param name="dict">Elements</param>
        internal PdfPage(PdfDictionary dict)
            : base(dict)
        {
            _elems.CheckTypeEx("Page");
        }

        /// <summary>
        /// Creates a new PDF page with the invalid flag set
        /// </summary>
        internal static PdfPage CreateInvalidPage()
        {
            var td = new TemporaryDictionary();
            td.InternalSet("Valid", PdfBool.False);
            td.SetType("Page");
            td.DirectSet("Parent", new TempReference(new PdfPages(true, null), true));
            return new PdfPage(td);
        }

        #endregion

        #region Events
/*
        /// <summary>
        /// Used to add items when they are changed.
        /// </summary>
        /// <remarks>Temporary, will use proper callback soon</remarks>
        internal void Notify(string key, Elements val, bool reference)
        {
            _elems.SetItem(key, val, reference);
        }
        internal void Notify(string key, BaseArray<PdfItem> val, bool reference)
        {
            if (reference)
                _elems.SetItemIndirect(key, val);
            else
                _elems.SetDirectItem(key, val);
        }
        */
        #endregion

        #region Cloning and construction methods

        /// <summary>
        /// Sets an item straight into the catalog of the dictionary.
        /// This can potentially set the object into an inconsitant state
        /// </summary>
        /// <remarks>
        /// Will throw if the item exists. Will also work on non-writable
        /// pages.
        /// 
        /// Used by the "de"compiler.
        /// </remarks>
        /*internal void DirectSet(string key, PdfItem item)
        {
            _elems.DirectSet(key, item);
        }*/

        /// <summary>
        /// Tests if an item is a page
        /// </summary>
        internal static bool IsPage(PdfItem obj)
        {
            return obj is PdfPage || (obj is PdfDictionary) && ((PdfDictionary)obj).IsType("Page");
        }

        /// <summary>
        /// For testing if this page has the same dictionary
        /// </summary>
        internal static bool IsSamePage(PdfDictionary obj, PdfPage page)
        {
            return ReferenceEquals(page._elems, obj);
        }

        #endregion

        #region saving

        internal override void Write(PdfWriter write)
        {
            if (_elems.InternalGetBool("Annots") && Annots.Count == 0)
            {
                var clone = _elems.TempClone();
                clone.Remove("Annots");
                clone.Write(write);
            }
            else
                _elems.Write(write);
        }

        #endregion

        /// <summary>
        /// Gets a media box
        /// </summary>
        /// <param name="box_name"></param>
        /// <returns></returns>
        public PdfRectangle GetSizeBox(string box_name)
        {
            switch (box_name.ToUpper())
            {
                case "MEDIABOX":
                    return MediaBox;
                case "CROPBOX":
                    return CropBox;
                case "TRIMBOX":
                    return TrimBox;
                case "BLEEDBOX":
                    return BleedBox;
                case "ARTBOX":
                    return ArtBox;
                default:
                    return CropBox;
            }
        }

        /// <summary>
        /// Sets the size of the page, taking into account inheritance
        /// </summary>
        /// <param name="llx">Lower left x</param>
        /// <param name="lly">Lower lieft y</param>
        /// <param name="width">Width</param>
        /// <param name="height">Height</param>
        /// <remarks>
        /// What about artbox, bleedbox, etc? Perhaps simply remove them.
        /// </remarks>
        public void SetSize(double llx, double lly, double width, double height)
        {
            var r = new PdfRectangle(llx, lly, width, height);
            var crop = (PdfRectangle)Fetch("CropBox", PdfType.Rectangle, false);
            if (crop != null)
                CropBox = r;
            else if (_elems.Contains("CropBox"))
                _elems.Remove("CropBox");
            MediaBox = r;
        }

        /// <summary>
        /// Gets the cropbox, if it exists.
        /// </summary>
        /// <returns>null or cropbox</returns>
        internal PdfRectangle GetCropBox()
        {
            var crop = (PdfRectangle)Inherit("CropBox", PdfType.Rectangle, false);
            if (crop != null)
            {
                var mb = MediaBox;
                if (mb != null)
                    return mb.Intersect(crop);
            }
            return crop;
        }

        internal PdfForm ConveretToForm()
        {
            var temp = new TemporaryDictionary();
            temp.SetName("Subtype", "Form");
            temp.SetItem("BBox", CropBox, false);
            var r = _elems["Resources"];
            if (r != null)
                temp.SetItem("Resources", r.Deref(), true);
            var g = _elems["Group"];
            if (g != null)
                temp.SetItem("Group", g.Deref(), true);
            var contents = Contents;
            IStream stream;
            if (contents.Length > 1)
                stream = new WrMemStream(temp, contents.Contents);
            else
                stream = new WrMemStream(temp, contents[0].Content);
            return new PdfForm(stream, temp);
        }

        /// <summary>
        /// Used when moving the element to another document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfPage(elems);
        }

        #region Inheritance helpers
        //Overengineered. Only used by a few properties. 

        /// <summary>
        /// Inherits a PdfObject from the parent
        /// </summary>
        /// <param name="key">The object's key</param>
        /// <param name="type">The object's type</param>
        /// <param name="require">If the object must exist</param>
        /// <param name="replace">If the reference should be replaced</param>
        /// <returns>A PdfObject</returns>
        private PdfObject Inherit(string key, PdfType type, bool require)
        {
            var ret = _elems.GetPdfType(key, type);
            if (ret != null) return ret;
            var parent = (PdfPages)_elems.GetPdfType("Parent", PdfType.Pages); ;
            if (parent != null) ret = parent.Inherit(key, type);
            if (ret != null || !require) return ret; //ObjNotFound
            throw new PdfReadException(ErrSource.Pages, type, ErrCode.Missing);
        }

        /// <summary>
        /// Inherits a PdfItem from the parent
        /// </summary>
        /// <param name="key">The object's key</param>
        /// <param name="type">The object's type</param>
        /// <param name="require">If the object must exist</param>
        /// <returns>A PdfItem</returns>
        private PdfItem Inherit(string key, bool require)
        {
            var ret = _elems[key];
            if (ret != null) return ret.Deref();
            var parent = (PdfPages)_elems.GetPdfType("Parent", PdfType.Pages);
            if (parent != null)
                ret = Parent.Inherit(key, true);
            if (ret != null) return ret; //ItemNotFound
            if (require) throw new PdfReadException(ErrSource.Pages, PdfType.Item, ErrCode.Missing);
            return null;
        }

        /// <summary>
        /// Gets a PdfObject from the dictionary
        /// </summary>
        /// <param name="key">The object's key</param>
        /// <param name="type">The object's type</param>
        /// <param name="require">If the object must exist</param>
        /// <returns>A PdfObject</returns>
        /// <remarks>Just to share the syntax with Inherit</remarks>
        private PdfObject Fetch(string key, PdfType type, bool require)
        {
            return (require) ? _elems.GetPdfTypeEx(key, type) :
                _elems.GetPdfType(key, type);
        }

        /// <summary>
        /// Used by the pages parent to check if this
        /// page needs these default values generated.
        /// </summary>
        internal bool NeedDefMB() { return !_elems.Contains("MediaBox"); }
        internal bool NeedDefCB() { return !_elems.Contains("Cropbox"); }
        internal bool NeedDefRes() { return !_elems.Contains("Resources"); }

        /// <summary>
        /// Adds MediaBox, CropBox and Resources to the page
        /// </summary>
        /// <param name="page">Page to add missing info to</param>
        /// <param name="source">Where to fetch this information</param>
        internal static PdfPage Inherit(PdfPage page, IInherit source)
        {
            //Moves missing resources.
            if (page.NeedDefRes())
                page.Resources = source.Resources;
            if (page.NeedDefMB())
                page.MediaBox = source.MediaBox;
            if (page.NeedDefCB())
            {
                var crop = source.CropBox;
                if (!crop.Equals(source.MediaBox))
                    page.CropBox = crop;
            }
            return page;
        }

        /// <summary>
        /// Creates a parentless copy of the page
        /// </summary>
        /// <param name="page">Page to copy</param>
        /// <returns>The copied page</returns>
        internal static PdfPage ShallowCopy(PdfPage page)
        {
            var icref = (ICRef)page;
            var org_catalog = ((Catalog)icref.GetChildren());
            Catalog catalog;
            lock (org_catalog) { catalog = org_catalog.Clone(); }
            catalog.Remove("Parent"); //<-- prevents the ref to the parent from being decremented
            page = (PdfPage)icref.MakeCopy(catalog, page._elems.Tracker);

            //Then we make a new reference for the page.
            page._elems.Tracker.Register(page);

            //Removes the "new flag" from the reference.
            ((IRef)page).Reference.RefCount = 0;

            return page;
        }

        #endregion
    }
}

