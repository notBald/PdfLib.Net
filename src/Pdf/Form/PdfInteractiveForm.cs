using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Form
{
    public sealed class PdfInteractiveForm : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.InteractiveFormDictionary; } }

        /// <summary>
        /// If this dictionary has been modified from its default state
        /// </summary>
        internal bool Modified
        {
            get
            {
                return IsWritable && _elems.Count == 1 && _elems.Contains("Fields") && Fields.Length == 0;
            }
        }

        public PdfFormField[] Fields 
        {
            get { return ((PdfFormFieldAr)_elems.GetPdfTypeEx("Fields", PdfType.FieldDictionaryAr)).ToArray(); } 
        }

        public bool NeedAppearances { get { return _elems.GetBool("NeedAppearances", false); } }

        [PdfVersion("1.3")]
        public int SigFlags { get { return _elems.GetInt("SigFlags", 0); } }

        public PdfArray CO { get { return _elems.GetArray("CO"); } }

        public PdfResources DR 
        { 
            get 
            { 
                var res = (PdfResources)_elems.GetPdfType("DR", PdfType.Resources);
                if (res == null)
                {
                    _elems.InternalSet("DR", PdfBool.True);
                    res = new PdfResources();
                    _elems.SetItem("DR", res, true);
                }
                return res;
            }
        }

        public string DA { get { return _elems.GetString("DA"); } }

        /// <summary>
        /// A code specifying the form of quadding (justification) 
        /// that shall be used in displaying the annotation’s text
        /// </summary>
        public Justification Q { get { return (Justification)_elems.GetInt("Q", 0); } }

        #endregion

        #region Init

        internal PdfInteractiveForm(PdfDictionary dict)
            : base(dict)
        { }

        internal PdfInteractiveForm()
            : base(new TemporaryDictionary())
        {
            _elems.SetItem("Fields", new PdfFormFieldAr(), false);
        }

        #endregion

        #region saving

        internal override void Write(PdfWriter write)
        {
            if (_elems.InternalGetBool("DR") && DR.Modified)
            {
                var clone = _elems.TempClone();
                clone.Remove("DR");
                clone.Write(write);
            }
            else
                _elems.Write(write);
        }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfInteractiveForm(elems);
        }

        #endregion
    }
}
