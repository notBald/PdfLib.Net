using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Optional
{
    /// <summary>
    /// Either a Optional Content Group or Optional Content Membership Dictionary
    /// </summary>
    public abstract class OptionalContent : Elements
    {
        protected OptionalContent(PdfDictionary dict)
            : base(dict)
        { }
    }
}
