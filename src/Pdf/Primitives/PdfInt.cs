using System;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Encapsulates a pdf ingteger value.
    /// </summary>
    [DebuggerDisplay("({Value})")]
    public sealed class PdfInt : PdfItem, INumber
    {
        #region Variables and properties

        /// <summary>
        /// The value of this integer
        /// </summary>
        public readonly int Value;

        /// <summary>
        /// PdfType.Integer
        /// </summary>
        internal override PdfType Type { get { return PdfType.Integer; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        public PdfInt(int val) { Value = val; }

        #endregion

        /// <summary>
        /// Gets integer as integer
        /// </summary>
        public override int GetInteger() { return Value; }

        /// <summary>
        /// Gets integer as double
        /// </summary>
        public override double GetReal() { return Value; }

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
            write.WriteInt(Value);
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null) return false;
            if (obj is PdfReal)
                return Value == ((PdfReal)obj).Value;
            if (obj is PdfInt)
                return Value == ((PdfInt)obj).Value;
            return false;
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            return Value.ToString();
        }
    }
}
