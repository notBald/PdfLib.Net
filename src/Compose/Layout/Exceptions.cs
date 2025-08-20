using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLib.Compose.Layout
{
    public class cLayoutException : PdfLibException
    {
        public cLayoutException() : base() { }
        public cLayoutException(string msg) : base(msg) { }
    }
}
