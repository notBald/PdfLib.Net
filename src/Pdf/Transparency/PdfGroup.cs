using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Transparency
{
    public abstract class PdfGroup : Elements
    {
        #region Variables and properties

        internal sealed override PdfType Type { get { return PdfType.Group; } }

        #endregion

        #region Init

        protected PdfGroup(PdfDictionary dict)
            : base(dict)
        {
            dict.CheckType("Group");
        }

        #endregion

        internal static PdfGroup Create(PdfDictionary dict)
        {
            //Fetches the subtype
            var s = dict.GetNameEx("S");
            if (s == "Transparency")
                return new PdfTransparencyGroup(dict);
            throw new NotSupportedException();
        }
    }
}
