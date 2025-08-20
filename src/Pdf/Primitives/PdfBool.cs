using System;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Holds a boolean value
    /// </summary>
    /// <remarks>
    /// There's only one instance of each bool value,
    /// meaning you can test for true or false by
    /// testing against PdfBool.True and PdfBool.False
    /// </remarks>
    [DebuggerDisplay("({Value})")]
    public sealed class PdfBool : PdfItem
    {
        #region Variables and properties

        /// <summary>
        /// The boolean value
        /// </summary>
        public readonly bool Value;

        /// <summary>
        /// PdfType.Bool
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Bool; }
        }

        /// <summary>
        /// True value
        /// </summary>
        public static readonly PdfBool True = new PdfBool(true);

        /// <summary>
        /// False value
        /// </summary>
        public static readonly PdfBool False = new PdfBool(false);

        #endregion

        #region Init

        /// <summary>
        /// Private constructor
        /// </summary>
        private PdfBool(bool val) { Value = val; }

        #endregion

        /// <summary>
        /// Gets a boolean value.
        /// </summary>
        public static PdfBool GetBool(string str)
        {
            return (bool.Parse(str)) ? True : False;
        }

        /// <summary>
        /// Gets a boolean value.
        /// </summary>
        public static PdfBool GetBool(bool b)
        {
            return (b) ? True : False;
        }

        /// <summary>
        /// There's no integer representation for bool
        /// </summary>
        public override int GetInteger()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// There's no real representation for bool
        /// </summary>
        public override double GetReal()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// There's no string representation for bool
        /// </summary>
        public override string GetString()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public override PdfItem Deref() { return this; }

        /// <summary>
        /// Bools are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        /// <summary>
        /// Writes itself to the stream
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            write.Write(Value);
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
