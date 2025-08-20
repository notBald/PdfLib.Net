using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Base object for PDF value types and PDF objects
    /// </summary>
    public abstract class PdfItem : ICloneable
    {
        #region Debug aid
#if DEBUG
        static int _item_count = 0;
        readonly int _item_nr;
        protected PdfItem() { _item_nr = _item_count++; }

        public override string ToString() { return "PdfItem: " + _item_nr; }
#endif
        #endregion

        #region Properties

        /// <summary>
        /// Gets the type of object in such a way that it
        /// can be dereferenced if needed.
        /// </summary>
        internal abstract PdfType Type { get; }

        /// <summary>
        /// Desired savemode of this item
        /// </summary>
        /// <remarks>
        /// For non-object primitives there's never a situation
        /// where they can't be direct. There are situations where
        /// they can't be indirect, but a direct item will never
        /// be set indirect.
        /// </remarks>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal virtual SM DefSaveMode { get { return SM.Auto; } }

        /// <summary>
        /// Returns what PdfVersion information
        /// this object has avalible right now.
        /// </summary>
        /// <remarks>Use GetPdfVersion for accurate versioning</remarks>
        public virtual PdfVersion PdfVersion { get { return PdfVersion.V10; } }

        /// <summary>
        /// Gets the culumative version of this object and it's children.
        /// </summary>
        /// <remarks>Using a HashSet to prevent circular references from being
        /// checked over and over again. There are quite likely better ways.</remarks>
        internal virtual PdfVersion GetPdfVersion(bool fetch_obj, HashSet<object> checked_items) 
        {
            checked_items.Add(this);
            return this.PdfVersion; 
        }

        /// <summary>
        /// Gets the version number of this object and it's children.
        /// Does not include content array on PdfPage.
        /// </summary>
        /// <param name="fetch">If to call GetPdfVersion on children too</param>
        /// <returns>Highest PdfVersion</returns>
        /// <remarks>
        /// No code should truly depend on this function as it may be
        /// dropped later.
        /// </remarks>
        public PdfVersion GetPdfVersion(bool fetch) { return GetPdfVersion(fetch, new HashSet<object>()); }

        #endregion

        #region Get methods

        /// <summary>
        /// Gets the string value, if any
        /// </summary>
        public abstract string GetString();

        /// <summary>
        /// Gets the integer value, if any
        /// </summary>
        public abstract int GetInteger();

        /// <summary>
        /// Gets the real value. if any
        /// </summary>
        public abstract double GetReal();

        /// <summary>
        /// Useful for dereferencing
        /// </summary>
        public abstract PdfItem Deref();

        #endregion

        #region Object methods

        /// <summary>
        /// Alternate method to "Equals". Does more work.
        /// </summary>
        internal virtual bool Equivalent(object obj)
        {
            return Equals(obj);
        }

        /// <summary>
        /// Makes the object write itself to the stream.
        /// </summary>
        internal abstract void Write(PdfWriter write);

        /// <summary>
        /// Could be made the only write method, but leaving
        /// that for later.
        /// </summary>
        internal virtual void Write(PdfWriter write, SM store_mode)
        {
            Write(write);
        }

        /// <summary>
        /// Method for casting this object into a different object
        /// </summary>
        /// <param name="type">Type of object to transform to</param>
        /// <returns>Transformed object</returns>
        /// <remarks>
        /// Since objects are cached, this function should not be called 
        /// directly on direct objects. Use GetPdfType, or equivalent
        /// 
        /// Notes for ToType replacement
        ///  - PostScript does ToType in a neater, perhaps better, way.
        ///  - GetPdfType(non Ex) should not throw PdfCastExceptions, it
        ///    should instead just return null. Look at PdfDestination.Dest
        ///    for more.
        /// </remarks>
        internal PdfObject ToType(PdfType type)
        {
            return ToType(type, IntMsg.NoMessage, null);
        }

        /// <summary>
        /// Method for casting this object into a different object
        /// </summary>
        /// <param name="type">Type to cast into</param>
        /// <param name="msg">Message</param>
        internal virtual PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            throw new PdfCastException(type, ErrCode.Invalid);
        }

        #endregion

        #region ICloneable Members

        /// <summary>
        /// Implements IClonable
        /// </summary>
        object ICloneable.Clone() { return Clone(); }

        /// <summary>
        /// Clones the PdfItem
        /// </summary>
        public abstract PdfItem Clone();

        #endregion
    }
}
