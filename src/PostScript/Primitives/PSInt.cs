using System;
using System.Diagnostics;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Represents a PS Integer value.
    /// </summary>
    [DebuggerDisplay("({Value})")]
    public class PSInt : PSItem, PdfLib.Pdf.Internal.INumber
    {
        /// <summary>
        /// The value of this integer
        /// </summary>
        public readonly int Value;

        /// <summary>
        /// Constructor
        /// </summary>
        public PSInt(int val) { Value = val; }
        public PSInt(PSInt val) { Value = val.Value; Executable = val.Executable; }

        /// <summary>
        /// Gets integer as double
        /// </summary>
        public double GetReal() { return Value; }

        public override bool Equals(object obj)
        {
            if (obj is PSInt)
                return ((PSInt) obj).Value == Value;
            else if (obj is PSReal)
                return ((PSReal) obj).Value == Value;
            return false;
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override PSItem ShallowClone()
        {
            return new PSInt(this);
        }

        public override string ToString() { return Value.ToString(); }
    }
}
