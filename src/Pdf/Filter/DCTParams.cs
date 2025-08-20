using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Filter
{
    internal sealed class DCTParams : PdfFilterParms
    {
        #region Variables and properties

        /// <summary>
        /// If the colors should be transformed.
        /// </summary>
        /// <remarks>If the Jpeg has an adobe marker, this entery is to be ignored</remarks>
        public bool ColorTransform { get { return _elems.GetUInt("ColorTransform", 1) == 1; } }

        #endregion

        #region Init

        public DCTParams(PdfDictionary dict)
            : base(dict) { }

        #endregion

        #region Boilerplate

        protected override Internal.Elements MakeCopy(PdfDictionary elems)
        {
            return new DCTParams(elems);
        }

        #endregion
    }
}
