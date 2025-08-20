using System;
using System.Diagnostics;
using System.Globalization;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Encapsulates a Pdf floating point value.
    /// </summary>
    [DebuggerDisplay("({Value})")]
    public sealed class PdfReal : PdfItem, INumber
    {
        #region Variables and properties

        /// <summary>
        /// The value of this real
        /// </summary>
        public readonly double Value;

        /// <summary>
        /// PdfType.Real
        /// </summary>
        internal override PdfType Type { get { return PdfType.Real; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        public PdfReal(double val) { Value = val; }

        #endregion

        /// <summary>
        /// Gets integer as integer
        /// </summary>
        public override int GetInteger() { throw new PdfCastException(ErrSource.Numeric, PdfType.Integer, ErrCode.RealToInt); }

        /// <summary>
        /// Gets real as double
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
        /// Reals are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        /// <summary>
        /// Writes the real value to the stream
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            write.WriteDouble(Value);
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
            return Value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
