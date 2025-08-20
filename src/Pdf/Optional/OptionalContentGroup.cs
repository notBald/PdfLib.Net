using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public sealed class OptionalContentGroup : OptionalContent
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.OptionalContentGroup; } }

        /// <summary>
        /// The name of the group, suitable to display in a reader's user interface
        /// </summary>
        public string Name 
        {
            //Should this be unicode, if so look at PdfOutlineItem.Title
            get { return _elems.GetUnicodeStringEx("Name"); }
            set { _elems.SetUnicodeStringEx("Name", value); }
        }

        public string[] Intent
        {
            get
            {
                var name = _elems["Intent"];
                if (name == null) return new string[] { "View" };
                name = name.Deref();
                if (name is PdfName)
                    return new string[] { name.GetString() };
                return ((NameArray) _elems.GetPdfType("Intent", PdfType.NameArray)).ToArray();
            }
            set
            {
                if (value == null || value.Length == 0)
                    _elems.Remove("Intent");
                else if (value.Length == 1)
                    _elems.SetName("Intent", value[0]);
                else
                    _elems.SetItem("Intent", new NameArray(value), false);
            }
        }

        public PdfOptUsage Usage
        {
            get { return (PdfOptUsage)_elems.GetPdfType("Usage", PdfType.UsageDictionary); }
            set { _elems.SetItem("Usage", value, true); }
        }

        #endregion

        #region Init

        internal OptionalContentGroup(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new OptionalContentGroup(elems);
        }

        #endregion
    }
}
