using System;
using PdfLib.Read;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Form;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// The root of the document
    /// </summary>
    /// <remarks>
    /// 7.7.2 in the specs
    /// </remarks>
    public sealed class PdfCatalog : Elements
    {
        #region Properties

        /// <summary>
        /// PdfType.Catalog
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Catalog; }
        }

        public PdfInteractiveForm AcroForm
        {
            get
            {
                var form = (PdfInteractiveForm)_elems.GetPdfType("AcroForm", PdfType.InteractiveFormDictionary);
                if (form == null && IsWritable)
                {
                    _elems.InternalSet("AcroForm", PdfBool.True);
                    form = new PdfInteractiveForm();
                    _elems.SetItem("AcroForm", form, true);
                }
                return form;
            }
        }

        /// <summary>
        /// The version of the PDF document
        /// </summary>
        /// <remarks>
        /// Optional. Will be 0.0 if not set
        /// </remarks>
        [PdfVersion("1.4")]
        public PdfHeader Version
        {
            get
            {
                var name = _elems.GetNameObj("Version");
                if (name == null)
                    return new PdfHeader();
                string str = name.Value;
                if (str.Length != 3 && str[1] != '.')
                    throw new PdfReadException(ErrSource.Header, PdfType.Name, ErrCode.Invalid);
                char major = str[0];
                char minor = str[2];
                if ('0' < major && major <= '9' && '0' <= minor && minor <= '9')
                    return new PdfHeader((byte)(major - '0'), (byte)(minor - '0'), 0);
                throw new PdfReadException(ErrSource.Header, PdfType.Name, ErrCode.Invalid);
            }
            set { _elems.SetName("Version", value.ToString()); }
        }

        /// <summary>
        /// Optional extension dictionary
        /// </summary>
        internal PdfDictionary Extensions { get { return _elems.GetDictionary("Extensions"); } }

        /// <summary>
        /// The page tree node that shall be the root of the 
        /// document’s page tree (see 7.7.3, "Page Tree")
        /// </summary>
        public PdfPages Pages { get { return (PdfPages)_elems.GetPdfTypeEx("Pages", PdfType.Pages, IntMsg.Owner, this); } }

        /// <summary>
        /// The document’s name dictionary (see 7.7.4, "Name Dictionary")
        /// </summary>
        [PdfVersion("1.2")]
        public PdfNameDictionary Names 
        { 
            get { return (PdfNameDictionary)_elems.GetPdfType("Names", PdfType.NameDictionary); }
            set { _elems.SetItem("Names", value, true); }
        }

        /// <summary>
        /// The document’s outline destination dictionary
        /// </summary>
        [PdfVersion("1.1")]
        public PdfDestDictionary Dests 
        { 
            get { return (PdfDestDictionary)_elems.GetPdfType("Dests", PdfType.DestDictionary); }
            set { _elems.SetItem("Dests", value, true); }
        }

        /// <summary>
        /// Structured documents have additional information for use in
        /// extracting data from the PDF document.
        /// </summary>
        [PdfVersion("1.3")]
        internal PdfDictionary StructTreeRoot { get { return _elems.GetDictionary("StructTreeRoot"); } }

        /// <summary>
        /// A flag indicating whether the document conforms to Tagged PDF conventions
        /// </summary>
        [PdfVersion("1.4")]
        internal PdfDictionary MarkInfo { get { return _elems.GetDictionary("MarkInfo"); } }

        /// <summary>
        /// How the document should be displayed when first opened.
        /// </summary>
        public PdfPageMode PageMode
        {
            get
            {
                var pm = _elems.GetName("PageMode");
                if (pm == null) return PdfPageMode.UseNone;
                switch (pm)
                {
                    case "UseNone":
                        return PdfPageMode.UseNone;
                    case "UseOutlines":
                        return PdfPageMode.UseOutlines;
                    case "UseThumbs":
                        return PdfPageMode.UseThumbs;
                    case "FullScreen":
                        return PdfPageMode.FullScreen;
                    case "UseOC":
                        return PdfPageMode.UseOC;
                    case "UseAttachments":
                        return PdfPageMode.UseAttachments;
                    default:
                        return PdfPageMode.Unrecognized;
                }
            }
            set
            {
                if (value == PdfPageMode.UseNone)
                    _elems.Remove("PageMode");
                else if(value == PdfPageMode.UseOutlines)
                    _elems.SetName("PageMode", "UseOutlines");
                else if (value == PdfPageMode.UseThumbs)
                    _elems.SetName("PageMode", "UseThumbs");
                else if (value == PdfPageMode.FullScreen)
                    _elems.SetName("PageMode", "FullScreen");
                else if (value == PdfPageMode.UseOC)
                    _elems.SetName("PageMode", "UseOC");
                else if (value == PdfPageMode.UseAttachments)
                    _elems.SetName("PageMode", "UseAttachments");
            }
        }

        public PdfPageLayout PageLayout
        {
            get { return PdfPageLayoutConv.Convert(_elems.GetName("PageLayout")); }
            set
            {
                if (value == PdfPageLayout.SinglePage)
                    _elems.Remove("PageLayout");
                else
                    _elems.SetName("PageLayout", PdfPageLayoutConv.Convert(value));
            }
        }

        /// <summary>
        /// Root of the document's outline hirarcy
        /// </summary>
        /// <remarks>Must be indirect</remarks>
        public PdfOutline Outlines 
        { 
            get { return (PdfOutline)_elems.GetPdfType("Outlines", PdfType.Outline, IntMsg.RootOutline, this); }
            set { _elems.SetItem("Outlines", value, true); }
        }

        /// <summary>
        /// Catalogs have to be indirect, but can be inside a object stream
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Compressed; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor for catalogs created for new documents
        /// </summary>
        /// <param name="tracker">The document's resource tracker</param>
        /// <param name="compressed">Whenever to compress this catalog or not</param>
        /// <param name="NextId">A hack to give this catalog a high id</param>
        internal PdfCatalog(bool writable, ResTracker tracker)
            : base(writable, tracker)
        {
            _elems.SetType("Catalog");
            _elems.SetNewItem("Pages", new PdfPages(writable, tracker), true);
        }

        /// <summary>
        /// Creates the catalog
        /// </summary>
        /// <remarks>Used for creating catalogs from parsed data</remarks>
        internal PdfCatalog(PdfDictionary dict)
            : base(dict) 
        {
            _elems.CheckTypeEx("Catalog");
        }

        #endregion

        #region Required overloads

        internal override void Write(PdfWriter write)
        {
            if (_elems.InternalGetBool("AcroForm") && AcroForm.Modified)
            {
                var clone = _elems.TempClone();
                clone.Remove("AcroForm");
                clone.Write(write);
            }
            else
                _elems.Write(write);
        }

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            //Catalogs are not to be moved. AFAIK no child holds a ref
            //to the catalog, but if that assumtion prooves false then
            //the ref must be changed to point at the catalog in the
            //new document.
            throw new NotSupportedException("Catalogs can not be moved.");
        }

        #endregion
    }
}
