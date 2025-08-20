using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    public sealed class SealedArray : PdfArray
    {
        public override bool IsWritable { get { return false; } }

        #region Init

        public SealedArray(List<PdfItem> items)
            : base(items) { }

        public SealedArray(PdfItem[] items)
            : base(items) { }

        public SealedArray(PdfItem item)
            : base(item) { }

        public SealedArray()
            : base() { }

        #endregion

        #region Overrides

        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            return PdfArray.ToType(type, msg, obj, this);
        }

        /// <summary>
        /// Used when moving the array to another class.
        /// </summary>
        protected override PdfArray MakeCopy(PdfItem[] data, ResTracker tracker)
        {
            return new WritableArray(data, tracker);
        }

        /// <summary>
        /// Makes a clone of this array
        /// </summary>
        public override PdfItem Clone()
        {
            return new SealedArray((PdfItem[]) _items.Clone());
        }

        #endregion
    }
}
