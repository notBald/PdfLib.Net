using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Form
{
    public sealed class PdfFormField : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.FieldDictionary; } }

        #endregion

        #region Init

        internal PdfFormField(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Boilerplate overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfFormField(elems);
        }

        #endregion
    }

    public sealed class PdfFormFieldAr : TypeArray<PdfFormField>
    {
        #region Properties

        internal override PdfType Type { get { return PdfType.FieldDictionaryAr; } }

        #endregion

        #region Init

        internal PdfFormFieldAr(PdfArray ar)
            : base(ar, PdfType.FieldDictionary)
        { }

        internal PdfFormFieldAr()
            : this(new TemporaryArray())
        { }

        #endregion

        #region Overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new PdfFormFieldAr(array);
        }

        #endregion
    }
}
