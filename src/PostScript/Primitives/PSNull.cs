using System;
using System.Diagnostics;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Represents a null value
    /// </summary>
    [DebuggerDisplay("PSNull")]
    public class PSNull : PSItem
    {
        /// <summary>
        /// Only one instance of this object is allowed
        /// </summary>
        public static readonly PSNull Value = new PSNull();

        /// <summary>
        /// Private constructor to prevent other instances
        /// </summary>
        private PSNull() { }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString() { return "null"; }

        public override PSItem ShallowClone()
        {
            //I belive we can ignore the Executable flag for null values
            return this;
        }
    }
}
