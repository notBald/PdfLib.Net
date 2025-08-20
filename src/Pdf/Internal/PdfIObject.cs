using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// An indirect object
    /// </summary>
    /// <remarks>
    /// Used internaly in the parser and PdfXref classes, nowhere else.
    /// </remarks>
    internal sealed class PdfIObject : PdfItem
    {
        /// <summary>
        /// PdfType.Obj
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Obj; }
        }

        /// <summary>
        /// Id of object
        /// </summary>
        public readonly PdfObjID Id;

        /// <summary>
        /// Value of object
        /// </summary>
        public readonly PdfItem Value;

        /// <summary>
        /// Constructor
        /// </summary>
        public PdfIObject(PdfObjID id, PdfItem value) { Id = id; Value = value; }

        /// <summary>
        /// Dereferences integer
        /// </summary>
        public override int GetInteger()
        {
            return Value.GetInteger();
        }

        /// <summary>
        /// Dereferences real
        /// </summary>
        public override double GetReal()
        {
            return Value.GetReal();
        }

        /// <summary>
        /// Dereferences string
        /// </summary>
        public override string GetString()
        {
            return Value.GetString();
        }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public override PdfItem Deref() { return this; }

        public override PdfItem Clone()
        {
            //IDs are immutable
            return this;
        }

        /// <summary>
        /// Casts the value into the desired type
        /// </summary>
        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            System.Diagnostics.Debug.Assert(type == PdfType.XRefStream);
            return Value.ToType(type, msg, obj);
        }

        internal override void Write(Write.Internal.PdfWriter write)
        { throw new System.NotSupportedException(); }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            return string.Format("Obj {0}: {1}", Id, Value);
        }
    }
}
