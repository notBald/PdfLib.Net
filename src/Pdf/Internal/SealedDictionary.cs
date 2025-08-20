using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Read;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// A PDF dictionary
    /// </summary>
    /// <remarks>
    /// This dictionary is created by the parser. It has
    /// the "ToType" method overriden so that it can be
    /// replaced by higher level objects.
    /// </remarks>
    public sealed class SealedDictionary : PdfDictionary
    {
        #region Variables and properties

        public override bool IsWritable { get { return false; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor used by parser
        /// </summary>
        internal SealedDictionary(Catalog cat)
            : base(cat) { }

        internal SealedDictionary()
            : base(new Catalog()) { }

        #endregion

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override PdfDictionary MakeCopy(Catalog data, ResTracker tracker)
        {
            return new WritableDictionary(data, tracker);
        }

        /// <summary>
        /// Creates a clone of this dictionary
        /// </summary>
        public override PdfItem Clone() { return new SealedDictionary(_catalog.Clone()); }

        #region Unsuported overrides

        internal override Write.Internal.ResTracker Tracker
        {  get { return null; } }
        public override void SetBool(string key, bool boolean)
        { throw new PdfNotWritableException(); }
        public override void SetInt(string key, int num)
        { throw new PdfNotWritableException(); }
        public override void SetReal(string key, double num)
        { throw new PdfNotWritableException(); }
        public override void SetType(string name)
        { throw new PdfNotWritableException(); }
        public override void SetName(string key, string name)
        { throw new PdfNotWritableException(); }
        /*public override void SetDirectItem(string key, PdfItem item)
        { throw new PdfNotWritableException(); }
        //public override void SetItem(string key, PdfItem item)
        //{ throw new NotSupportedException(); }
        public override void SetIndirectItem(string key, PdfReference item)
        { throw new PdfNotWritableException(); }//*/
        public override void SetItem(string key, PdfItem item, bool reference)
        { throw new PdfNotWritableException(); }
        public override void Remove(string key)
        { throw new PdfNotWritableException(); }
        //public override void SetItemIndirect(string key, PdfItem item)
        //{ throw new NotSupportedException(); }
        internal override void SetNewItem(string key, PdfItem item, bool reference)
        { throw new PdfNotWritableException(); }
        /*public override void SetNewItem(string key, Elements elem)
        { throw new PdfNotWritableException(); }//*/
        public override Dictionary<PdfItem, string> GetReverseDict()
        { throw new PdfNotWritableException(); }

        #endregion
    }
}
