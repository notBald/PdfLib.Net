using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Pdf.Transparency
{
    public sealed class PdfTransparencyGroup : PdfGroup
    {
        #region Properties

        /// <summary>
        /// Group color space
        /// 
        /// Valid color spaces include:
        ///  -DeviceGray/RHB/CMYK
        ///  -ICCBased, when the underlying CS is one of the above
        /// </summary>
        public IColorSpace[] CS
        {
            get
            {
                var itm = _elems["CS"];
                if (itm == null) return null;
                var type = itm.Type;
                if (type == PdfType.ColorSpace)
                    return new IColorSpace[] { (IColorSpace)itm.Deref() };
                if (type == PdfType.Array)
                {
                    var ar = (PdfArray) itm.Deref();
                    var cs_ar = new IColorSpace[ar.Length];
                    for (int c = 0; c < cs_ar.Length; c++)
                        cs_ar[c] = (IColorSpace)ar.GetPdfType(c, PdfType.ColorSpace, IntMsg.NoMessage, null);
                    return cs_ar;
                }
                return new IColorSpace[] { (IColorSpace)_elems.GetPdfType("CS", PdfType.ColorSpace) };
            }
            set
            {
                if (value == null || value.Length == 0)
                    _elems.Remove("CS");
                else
                {
                    var ar = new TemporaryArray(value.Length);
                    for (int c = 0; c < value.Length; c++)
                    {
                        var cs = (PdfItem) value[c];
                        if (cs is PdfColorSpace)
                            ar.AddItem(cs);
                        else if (cs is ICCBased)
                            ar.AddItem(cs, true);
                        else
                            throw new PdfNotSupportedException("Unsupported colorspace");
                    }
                }
            }
        }

        /// <summary>
        /// Returns the first known color space
        /// </summary>
        public IColorSpace ColorSpace
        {
            get
            {
                var itm = _elems["CS"];
                if (itm == null) return null;
                var type = itm.Type;
                if (type == PdfType.ColorSpace)
                    return (IColorSpace)itm.Deref();
                if (type == PdfType.Array)
                {
                    var ar = (PdfArray)itm.Deref();
                    int len = ar.Length;
                    if (len == 0) return null;
                    for (int c = 0; c < len; c++)
                    {
                        try
                        {
                            return (IColorSpace)ar.GetPdfType(c, PdfType.ColorSpace, IntMsg.NoMessage, null);
                        }
                        catch(Exception)
                        { }
                    }
                    throw new NotSupportedException();
                }
                return (IColorSpace)_elems.GetPdfType("CS", PdfType.ColorSpace);
            }
            set 
            {
                if (value is PdfColorSpace || value == null)
                    _elems.SetItem("CS", (PdfItem)value, value is IRef);
                else
                    CS = new IColorSpace[] { value };
            }
        }

        /// <summary>
        /// IsIsoloated
        /// 
        /// This flag means that the group isn't blended with
        /// the page. 
        /// </summary>
        public bool I 
        {
            get { return _elems.GetBool("I", false); }
            set { _elems.SetBool("I", value, false); }
        }

        /// <summary>
        /// IsKnockout
        /// 
        /// This means all transparency draw calls are blended
        /// with the "backdrop", but not each other.
        /// 
        /// If used in conjuction with "I", it will be little
        /// different from not using transparency at all.
        /// </summary>
        public bool K 
        { 
            get { return _elems.GetBool("K", false); }
            set { _elems.SetBool("K", value, false); }
        }

        #endregion

        #region Init

        public PdfTransparencyGroup()
           : this((IColorSpace) null)
        { }

        /// <summary>
        /// Valid color spaces include:
        ///  -DeviceGray/RHB/CMYK
        ///  -ICCBased, when the underlying CS is one of the above
        /// </summary>
        /// <param name="cs">DeviceGray/RHB/CMYK or ICC</param>
        public PdfTransparencyGroup(IColorSpace cs)
            : base(new TemporaryDictionary())
        {
            _elems.SetNameEx("S", "Transparency");
            if (cs != null)
                ColorSpace = cs;
        }

        internal PdfTransparencyGroup(PdfDictionary dict)
            : base(dict)
        {

        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfTransparencyGroup(elems);
        }

        #endregion
    }
}
