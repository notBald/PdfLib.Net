using System;
using System.Diagnostics;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Represents a Pdf real value.
    /// </summary>
    [DebuggerDisplay("({Value})")]
    public class PSReal : PSItem, PdfLib.Pdf.Internal.INumber
    {
        /// <summary>
        /// The value of this real
        /// </summary>
        public readonly double Value;

            /// <summary>
        /// Constructor
        /// </summary>
        public PSReal(double val) { Value = val; }

        /// <summary>
        /// Gets real as double
        /// </summary>
        public double GetReal() { return Value; }

        public override PSItem ShallowClone()
        {
            //Can't imagine the Executable flag making sense with reals.
            return this;
        }

        public override bool Equals(object obj)
        {
            if (obj is PSInt)
                return ((PSInt)obj).Value == Value;
            else if (obj is PSReal)
                return ((PSReal)obj).Value == Value;
            return false;
        }

        public override int GetHashCode()
        {
            return (int) Value;
        }

        public override string ToString() { return Value.ToString(); }
    }
}
