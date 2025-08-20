using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    public class PdfInfo : Elements
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Info
        /// </summary>
        internal override PdfType Type { get { return PdfType.Info; } }

        /// <summary>
        /// The document's title
        /// </summary>
        [PdfVersion("1.1")]
        public string Title { get { return _elems.GetString("Title"); } }

        /// <summary>
        /// Name of the document's creator
        /// </summary>
        public string Author { get { return _elems.GetString("Author"); } }

        /// <summary>
        /// The subject of the document
        /// </summary>
        [PdfVersion("1.1")]
        public string Subject { get { return _elems.GetString("Subject"); } }

        /// <summary>
        /// Keywords associated with the document.
        /// </summary>
        [PdfVersion("1.1")]
        public string Keywords { get { return _elems.GetString("Keywords"); } }

        /// <summary>
        /// The product that created the original document
        /// of which this PDF files was created
        /// </summary>
        public string Creator { get { return _elems.GetString("Creator"); } }

        /// <summary>
        /// The program that converted this document into a PDF file
        /// </summary>
        public string Producer { get { return _elems.GetString("Producer"); } }

        /// <summary>
        /// Time the document was created
        /// </summary>
        public PdfDate CreationDate { get { return (PdfDate) _elems.GetPdfType("CreationDate", PdfType.Date); } }

        /// <summary>
        /// Time the document was last modified
        /// </summary>
        public PdfDate ModDate { get { return (PdfDate)_elems.GetPdfType("ModDate", PdfType.Date); } }

        /// <summary>
        /// If this document has been trapped
        /// </summary>
        public string Trapped { get { return _elems.GetName("Trapped"); } }

        #endregion

        #region Init

        internal PdfInfo()
            : base(new TemporaryDictionary())
        { }

        internal PdfInfo(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfInfo(elems);
        }

        #endregion
    }
}
