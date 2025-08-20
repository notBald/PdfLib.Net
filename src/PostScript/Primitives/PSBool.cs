using System.Diagnostics;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Holds a boolean value
    /// </summary>
    [DebuggerDisplay("({Value})")]
    public sealed class PSBool : PSItem
    {
        /// <summary>
        /// The boolean value
        /// </summary>
        public readonly bool Value;

        /// <summary>
        /// Private constructor
        /// </summary>
        public PSBool(bool val) { Value = val; }
        public PSBool(PSBool val) { Value = val.Value; Executable = val.Executable; }

        public override bool Equals(object obj)
        {
            if (this == obj) return true;
            if (!(obj is PSBool)) return false;
            return ((PSBool) obj).Value == Value;
        }

        public override int GetHashCode()
        {
            return (Value) ? 1 : 0;
        }

        public override PSItem ShallowClone()
        {
            return new PSBool(this);
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }
}
