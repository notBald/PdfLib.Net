using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Internal
{
    public class PdfEmbeddedFile : StreamElm
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.EmbeddedFile; } }

        #endregion

        #region Init

        internal PdfEmbeddedFile(IWStream stream)
            : base(stream)
        { }

        private PdfEmbeddedFile(IStream stream, PdfDictionary dict)
            : base(stream, dict)
        { }

        #endregion

        #region Required override

        protected override StreamElm MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfEmbeddedFile(stream, dict);
        }

        #endregion
    }

    public sealed class PdfNamedFiles : PdfNameTree<PdfEmbeddedFile>
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.NamedFiles; } }

        #endregion

        #region Init

        public PdfNamedFiles(PdfDictionary dict)
            : base(dict, PdfType.EmbeddedFile)
        { }

        #endregion

        #region Index

        protected override PdfEmbeddedFile Cast(PdfItem item)
        {
            return (PdfEmbeddedFile) item;
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfNamedFiles(elems);
        }

        #endregion
    }
}
