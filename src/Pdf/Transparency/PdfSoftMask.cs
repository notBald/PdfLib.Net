using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Function;

namespace PdfLib.Pdf.Transparency
{
    public abstract class PdfSoftMask : Elements, IMask
    {
        #region Variables and properties

        internal sealed override PdfType Type { get { return PdfType.SoftMask; } }

        public PdfForm G
        {
            //There's only exists one PdfGroup subtype, so no need to ask for a spesific one.
            //If a new group subtype is added in the future, this libary will throw an
            //not supported exception regardless of what we do here.
            get { return (PdfForm)_elems.GetPdfTypeEx("G", PdfType.XObject); }
            set 
            {
                if (value == null || value.Group == null)
                    throw new PdfNotSupportedException("Form must have a group");
                _elems.SetItemEx("G", value, true); 
            }
        }

        /// <summary>
        /// Transfer function. Null is the identity function
        /// </summary>
        public PdfFunction TR
        {
            get
            {
                var itm = _elems["TR"];
                if (itm == null)
                    return null;
                var type = itm.Type;
                if (type == PdfType.Name)
                {
                    if (itm.GetString() == "Identity")
                        return null;
                    throw new PdfReadException(PdfType.Function, ErrCode.Illegal);
                }
                if (type == PdfType.Function)
                    return (PdfFunction)itm.Deref();
                return (PdfFunction) _elems.GetPdfType("TR", PdfType.Function);
            }
        }

        #endregion

        #region Init

        internal PdfSoftMask(PdfDictionary dict)
            : base(dict)
        {
            dict.CheckType("Mask");
        }

        protected PdfSoftMask(PdfForm group_xobject, string subtype)
            : base(new TemporaryDictionary())
        {
            G = group_xobject;
            _elems.SetNameEx("S", subtype);
        }

        #endregion

        internal static PdfSoftMask Create(PdfDictionary dict)
        {
            var s = dict.GetNameEx("S");
            if (s == "Alpha")
                return new PdfAlphaMask(dict);
            if (s == "Luminosity")
                return new PdfLuminosityMask(dict);
            throw new PdfNotSupportedException();
        }
    }
}
