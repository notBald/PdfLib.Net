using System;
using System.Diagnostics;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Encapsulates a null value
    /// </summary>
    /// <remarks>The libary typically converts this value to a null pointer</remarks>
    [DebuggerDisplay("PdfNull")]
    public sealed class PdfNull : PdfItem
    {
        /// <summary>
        /// Only one instance of this object is allowed
        /// </summary>
        public static readonly PdfNull Value = new PdfNull();

        /// <summary>
        /// This is an object of type null
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Null; }
        }

        /// <summary>
        /// Private constructor to prevent other instances
        /// </summary>
        private PdfNull() { }

        /// <summary>
        /// There's no integer representation for null
        /// </summary>
        public override int GetInteger()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// There's no real representation for null
        /// </summary>
        public override double GetReal()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Returns null
        /// </summary>
        public override string GetString()
        {
            return null;
        }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public override PdfItem Deref() { return this; }

        /// <summary>
        /// Nulls are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        /// <summary>
        /// Writes a null value to the stream
        /// </summary>
        internal override void Write(Write.Internal.PdfWriter write)
        {
            write.WriteNull();
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString() { return "null"; }
    }
}
