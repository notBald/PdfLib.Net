using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfFileAttachmentAnnot : PdfMarkupAnnotation
    {
        #region Variables and properties

        public PdfItem FS { get { return _elems.GetItemEx("FS"); } }

        /// <summary>
        /// The name of an icon that shall be used in displaying the annotation.
        /// </summary>
        public string Name
        {
            get
            {
                var r = _elems.GetName("Name");
                if (r == null) return "PushPin";
                return r;
            }
        }

        #endregion

        #region Init

        internal PdfFileAttachmentAnnot(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfFileAttachmentAnnot(elems);
        }

        #endregion
    }
}
