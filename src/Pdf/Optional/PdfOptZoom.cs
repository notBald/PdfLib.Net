using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Optional
{
    public class PdfOptZoom : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.ZoomDictionary; } }

        public double min { get { return _elems.GetReal("min", 0); } }

        public double max { get { return _elems.GetReal("max", double.MaxValue); } }

        #endregion

        #region Init

        internal PdfOptZoom(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOptZoom(elems);
        }

        #endregion
    }
}
