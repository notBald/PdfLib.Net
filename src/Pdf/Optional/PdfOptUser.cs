using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptUser : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.UserDictionary; } }

        public string UserType { get { return _elems.GetNameEx("Type"); } }

        public string[] Name
        {
            get
            {
                var name = _elems.GetItemEx("Name").Deref();
                if (name is PdfName)
                    return new string[] { name.GetString() };
                return ((NameArray)_elems.GetPdfTypeEx("Name", PdfType.NameArray)).ToArray();
            }
            set
            {
                if (value == null || value.Length == 0)
                    throw new ArgumentNullException("Can't be null");
                if (value.Length == 1)
                    _elems.SetName("Name", value[0]);
                else
                    _elems.SetItem("Name", new NameArray(value), false);
            }
        }

        #endregion

        #region Init

        internal PdfOptUser(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptUser(elems);
        }

        #endregion
    }
}
