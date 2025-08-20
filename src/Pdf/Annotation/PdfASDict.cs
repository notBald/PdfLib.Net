using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Annotation
{
    /// <summary>
    /// A dictionary of appearance streams
    /// </summary>
    /// <remarks>
    /// 12.5.5 Appearance Streams. Note that I have not managed
    /// to get these working in Adobe Reader XI, but they
    /// follow the specs, and work in other PDF viewers
    /// </remarks>
    public sealed class PdfASDict : TypeDict<PdfForm>
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.ASDict; } }

        #endregion

        #region Init

        public PdfASDict()
            : this(new TemporaryDictionary())
        { }

        internal PdfASDict(PdfDictionary dict)
            : base(dict, PdfType.XObject, PdfNull.Value)
        { }

        #endregion

        protected override void SetT(string key, PdfForm item)
        {
            _elems.SetItem(key, item, true);
        }

        #region Required overrides

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override TypeDict<PdfForm> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg)
        {
            return new PdfASDict(elems);
        }

        #endregion
    }
}
