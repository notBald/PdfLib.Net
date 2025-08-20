using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Read;
using PdfLib.Write.Internal;
using PdfLib.Write;

namespace PdfLib.Read
{
    /// <summary>
    /// The owner is used by the parser and by PdfStream objects. Technically
    /// only a "PdfFile" can be an owner, so this interface may be redudant.
    /// </summary>
    /*public interface IOwner
    {
        PdfReference GetReference(PdfObjID id);

        bool HasTrailer { get; }

        int Read(byte[] buffer, int offset);
    }*/
}
