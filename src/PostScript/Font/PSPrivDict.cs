using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.PostScript.Internal;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Font
{
    /// <summary>
    /// Private dictionary of a PostScript font
    /// </summary>
    public class PSPrivDict : PSDictionary
    {
        #region Variables and properties

        /// <summary>
        /// The lenIV entry is an integer specifying the number of random
        /// bytes at the beginning of charstrings for charstring encryption.
        /// </summary>
        public int lenIV { get { return GetUInt("lenIV", 4); } }

        /// <summary>
        /// Subrutines
        /// </summary>
        public PSSubrs Subrs { get { return GetPSObj<PSSubrs, PSArray>("Subrs", PSSubrs.Create); } }

        /// <summary>
        /// PostScript subrutines
        /// </summary>
        public PSArray<PSProcedure> OtherSubrs 
        { get { return GetPSObj<PSArray<PSProcedure>, PSArray>("OtherSubrs", PSArray<PSProcedure>.Create); } }

        #endregion

        #region Init

        public PSPrivDict(PSDictionary dict)
            : base(dict) { }

        internal static PSPrivDict Create(PSDictionary dict) { return new PSPrivDict(dict); }

        public override PSItem ShallowClone()
        {
            return new PSPrivDict(this);
        }

        #endregion
    }
}
