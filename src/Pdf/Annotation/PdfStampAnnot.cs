using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfStampAnnot : PdfMarkupAnnotation
    {
        #region Variables and properties

        /// <summary>
        /// The name of an icon that shall be used in displaying the 
        /// annotation.
        /// </summary>
        public string Name
        {
            get
            {
                var r = _elems.GetName("Name");
                if (r == null) return "Draft";
                return r;
            }
            set
            {
                if (value == null || value.Length == 0)
                    _elems.Remove("Name");
                else
                {
                    if (value == "Draft")
                        _elems.Remove("Name");
                    else
                        _elems.SetName("Name", value);
                }

            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Constructor that assumes you wish to position the
        /// annotation later. 
        /// </summary>
        public PdfStampAnnot()
            : base("Stamp", new PdfRectangle(0, 0, 10, 10))
        { }

        public PdfStampAnnot(PdfRectangle rect)
            : base("Stamp", rect)
        { }


        internal PdfStampAnnot(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfStampAnnot(elems);
        }

        #endregion
    }
}
