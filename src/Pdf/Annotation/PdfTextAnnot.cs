using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfTextAnnot : PdfMarkupAnnotation
    {
        #region Variables and properties

        /// <summary>
        /// Whenever the annotiation should be displayed in a open state
        /// </summary>
        public bool Open 
        { 
            get { return _elems.GetBool("Open", false); }
            set { _elems.SetBool("Open", value, false); }
        }

        /// <summary>
        /// The name of an icon that shall be used in displaying the 
        /// annotation.
        /// </summary>
        public string Name
        {
            get
            {
                var r = _elems.GetName("Name");
                if (r == null) return "Note";
                return r;
            }
            set
            {
                if (value == null || value.Length == 0)
                    _elems.Remove("Name");
                else
                {
                    var name = value.ToLower();
                    name = char.ToUpper(name[0]) + name.Substring(1);
                    if (name == "Note")
                        _elems.Remove("Name");
                    else
                        _elems.SetName("Name", name);
                }

            }
        }

        public string State
        {
            get
            {
                var r = _elems.GetString("State");
                if (r == null)
                {
                    var sm = StateModel;
                    //if (sm == null) throw new PdfInternalException("StateModel required");
                    if (sm == "Marked") return "Unmarked";
                    if (sm == "Review") return "None";
                }
                return r;
            }
        }

        public string StateModel { get { return _elems.GetString("StateModel"); } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor that assumes you wish to position the
        /// annotation later. 
        /// </summary>
        public PdfTextAnnot()
            : base("Text", new PdfRectangle(0, 0, 10, 10))
        { }

        public PdfTextAnnot(PdfRectangle rect)
            : base("Text", rect)
        { }

        internal PdfTextAnnot(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfTextAnnot(elems);
        }

        #endregion
    }
}
