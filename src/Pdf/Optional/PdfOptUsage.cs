using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptUsage : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.UsageDictionary; } }

        public PdfOptCreatorInfo CreatorInfo { get { return (PdfOptCreatorInfo) _elems.GetPdfType("CreatorInfo", PdfType.CreatorInfo); } }

        public PdfOptLanguage Language { get { return (PdfOptLanguage)_elems.GetPdfType("Language", PdfType.LanguageDictionary); } }

        public PdfOptExport Export { get { return (PdfOptExport)_elems.GetPdfType("Export", PdfType.ExportDictionary); } }

        public PdfOptZoom Zoom { get { return (PdfOptZoom)_elems.GetPdfType("Zoom", PdfType.ZoomDictionary); } }

        public PdfOptPrint Print { get { return (PdfOptPrint)_elems.GetPdfType("ZoPrintom", PdfType.PrintDictionary); } }

        public PdfOptView View { get { return (PdfOptView)_elems.GetPdfType("View", PdfType.ViewDictionary); } }

        public PdfOptUser User { get { return (PdfOptUser)_elems.GetPdfType("User", PdfType.UserDictionary); } }

        public PdfOptPageElement PageElement { get { return (PdfOptPageElement)_elems.GetPdfType("PageElement", PdfType.PageElementDictionary); } }

        #endregion

        #region Init

        internal PdfOptUsage(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptUsage(elems);
        }

        #endregion
    }
}
