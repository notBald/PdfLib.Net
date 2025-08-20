using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Pdf.Transparency
{
    public sealed class PdfLuminosityMask : PdfSoftMask
    {
        #region Variables and properties

        /// <summary>
        /// Background Color
        /// </summary>
        public double[] BC
        {
            get
            {
                var color = (RealArray) _elems.GetPdfType("BC", PdfType.RealArray);
                if (color == null)
                {
                    var g = G.Group;
                    if (g == null)
                        throw new PdfReadException(PdfType.Group, ErrCode.Missing);
                    var cs = g.ColorSpace;
                    if (cs == null) return null;

                    return cs.DefaultColorAr;
                }
                return color.ToArray();
            }
            set
            {
                if (value == null)
                    _elems.Remove("BC");
                else
                    _elems.SetItem("BC", new RealArray(value), false);
            }
        }

        #endregion

        #region Init

        public PdfLuminosityMask(PdfForm group_xobject)
            : base(group_xobject, "Luminosity")
        {
            if (group_xobject.Group.CS == null)
                throw new PdfNotSupportedException("XObject.Group must have colorspace");
        }

        internal PdfLuminosityMask(PdfDictionary dict)
            : base(dict)
        {

        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfLuminosityMask(elems);
        }

        #endregion
    }
}
