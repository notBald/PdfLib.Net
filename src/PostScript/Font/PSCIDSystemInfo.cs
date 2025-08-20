using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Font
{
    public class PSCIDSystemInfo : PSDictionary
    {
        #region Variables and properties

        public string Registry { get { return GetStrEx("Registry"); } }

        public string Ordering { get { return GetStrEx("Ordering"); } }

        public int Supplement { get { return GetUIntEx("Supplement"); } }

        #endregion

        #region Init

        private PSCIDSystemInfo(PSDictionary dict) : base(dict) { }

        internal PSCIDSystemInfo(string registery, string ordering, uint supplement)
            : base(3)
        {
            Catalog["Registry"] = new PSString(registery.ToCharArray());
            Catalog["Ordering"] = new PSString(ordering.ToCharArray());
            Catalog["Supplement"] = new PSInt((int)supplement);
        }

        #endregion

        internal static PSCIDSystemInfo Create(PSDictionary dict)
        {
            return new PSCIDSystemInfo(dict);
        }

        public override PSItem ShallowClone()
        {
            return new PSCIDSystemInfo(MakeShallowCopy());
        }
    }
}
