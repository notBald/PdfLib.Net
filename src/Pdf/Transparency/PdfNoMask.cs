using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Transparency
{
    public class PdfNoMask : PdfObject, IMask
    {
        public const string Value = "None";

        internal override PdfType Type
        {
            get { return PdfType.SoftMask; }
        }

        //internal override SM DefSaveMode
        //{
        //    //The shortes reference is 5 characters long, so there's no benefit from
        //    //allowing this name to be indirect. However, it's is allowed by the specs.
        //    get { return SM.Direct; }
        //}

        public PdfNoMask()
        { }

        /// <summary>
        /// Names are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        internal override void Write(PdfWriter write)
        {
            write.WriteName(Value);
        }

        internal override bool Equivalent(object obj)
        {
            if (obj is PdfNoMask) return true;
            if (obj == null) return false;
            if (obj is PdfName)
                return Value == ((PdfName)obj).GetString();
            return false;
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            return Value;
        }
    }
}
