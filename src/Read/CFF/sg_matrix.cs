using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Read.CFF
{

    public struct sg_matrix<Path>
    {
        public Path SG;
        public xMatrix M;
    }
}
