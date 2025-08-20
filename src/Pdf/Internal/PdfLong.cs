using System.Diagnostics;
using System;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Encapsulates a pdf ingteger value that resides outside bounds of C# integers.
    /// </summary>
    [DebuggerDisplay("({Value})")]
    public sealed class PdfLong : PdfItem, INumber
    {
        #region Variables and properties

        /// <summary>
        /// The value of this integer
        /// </summary>
        public readonly string Value;

        /// <summary>
        /// PdfType.Integer
        /// </summary>
        internal override PdfType Type { get { return PdfType.Long; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        public PdfLong(string val) { Value = val; }

        #endregion

        /// <summary>
        /// Gets integer as integer
        /// </summary>
        public override int GetInteger() { throw new OverflowException(); }

        /// <summary>
        /// Gets integer as double
        /// </summary>
        public override double GetReal() { return double.Parse(Value); }

        /// <summary>
        /// There's no string representation for integers
        /// </summary>
        public override string GetString() { throw new NotSupportedException(); }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public override PdfItem Deref() { return this; }

        /// <summary>
        /// Integers are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        /// <summary>
        /// Writes the int to the stream
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            write.WriteRaw(Value);
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null) return false;
            if (obj is PdfLong)
                return Value == ((PdfLong)obj).Value;
            if (obj is PdfReal)
                return GetReal() == ((PdfReal)obj).GetReal();
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
