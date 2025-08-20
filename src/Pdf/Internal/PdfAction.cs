using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    public class PdfAction : Elements
    {
        #region Variables and properties

        internal sealed override PdfType Type { get { return PdfType.Action; } }

        protected PdfCatalog _cat;

        /// <summary>
        /// Type of action
        /// </summary>
        protected string S { get { return _elems.GetNameEx("S"); } }

        /// <summary>
        /// Additional actions to perform
        /// </summary>
        [PdfVersion("1.2")]
        public PdfActionArray Next
        {
            get
            {


                return (PdfActionArray)_elems.GetPdfType("Next",
                    PdfType.ActionArray,
                    PdfType.Action,
                    false,
                    _elems.IsWritable ? IntMsg.ResTracker : IntMsg.NoMessage,
                    new object[] { _cat, _elems.Tracker });
            }
        }

        #endregion

        #region Init

        protected PdfAction(PdfDictionary dict, PdfCatalog cat)
            : base(dict)
        { dict.CheckType("Action"); _cat = cat; }

        internal static PdfAction Create(PdfDictionary dict, PdfCatalog cat)
        {
            var s = dict.GetNameEx("S");

            switch (s)
            {
                case "GoTo":
                    return new GoToAction(dict, cat);

                default:
                    return new PdfAction(dict, cat);
            }
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfAction(elems, ((WritableDictionary)elems).Tracker.Doc.Catalog);
        }

        protected override void DictChanged()
        {
            _cat = ((WritableDictionary)_elems).Tracker.Doc.Catalog;
        }

        #endregion
    }

    public sealed class PdfActionArray : TypeArray<PdfAction>, IKRef
    {
        #region Variables and properties

        /// <summary>
        /// Returns PdfType.ActionArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.ActionArray; } }

        private PdfCatalog _cat;

        #endregion

        #region Init

        /// <summary>
        /// Create empty array
        /// </summary>
        internal PdfActionArray(PdfCatalog cat, bool writable, ResTracker tracker)
            : base(writable, tracker, PdfType.Content)
        { _cat = cat; }

        /// <summary>
        /// Create array with one item.
        /// </summary>
        /// <param name="items">Content items</param>
        internal PdfActionArray(PdfItem item, PdfCatalog cat, bool writable, ResTracker tracker)
            : base(new PdfItem[] { item }, PdfType.Content, writable, tracker)
        { _cat = cat; }

        /// <summary>
        /// Create array
        /// </summary>
        /// <param name="items">Action items</param>
        internal PdfActionArray(PdfArray items, PdfCatalog cat)
            : base(items, PdfType.Action)
        { _cat = cat; }

        #endregion

        #region Add methods

        /// <summary>
        /// Add contents to a writable contents array
        /// </summary>
        public void AddContents(PdfAction action)
        {
            _items.AddItem(action, action.HasReference);
        }

        #endregion     

        #region IKRef

        /// <summary>
        /// Default save mode
        /// </summary>
        SM IKRef.DefSaveMode { get { return DefSaveMode; } }

        /// <summary>
        /// If this object has a dummy tracker.
        /// </summary>
        bool IKRef.IsDummy { get { return _items.IsWritable && _items.Tracker == null; } }

        /// <summary>
        /// Adopt this object. Will return false if the adoption failed.
        /// </summary>
        /// <param name="tracker"></param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker)
        {
            if (_items is TemporaryArray)
            {
                _items = (PdfArray)tracker.Adopt((ICRef)_items);
                _cat = tracker.Doc.Catalog;
            }
            return _items.Tracker == tracker;
        }

        /// <summary>
        /// Adobt this object. Will return false if the adoption failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adopt this item</param>
        /// <param name="state">State information about the adoption</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker, object state)
        {
            if (_items is TemporaryArray)
            {
                _items = (PdfArray)tracker.Adopt((ICRef)_items, state);
                _cat = tracker.Doc.Catalog;
            }
            return _items.Tracker == tracker;
        }

        /// <summary>
        /// Checks if a tracker owns this object
        /// </summary>
        /// <param name="tracker">Tracker</param>
        /// <returns>True if the tracker is the owner</returns>
        bool IKRef.IsOwner(ResTracker tracker)
        {
            return _items.Tracker == tracker;
        }

        #endregion

        #region Required overrides

        protected override PdfItem GetParam(int index)
        {
            return _cat;
        }

        /// <summary>
        /// Content is optional, so it can be "null", 
        /// a plain action item or an array of actions.
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            if (_items.Length == 0)
                write.WriteNull();
            else if (_items.Length == 1)
                _items[0].Write(write);
            else
                _items.Write(write);
        }

        /// <summary>
        /// When being saved as an indirect object, don't
        /// strip the array.
        /// </summary>
        internal override void Write(PdfWriter write, SM store_mode)
        {
            _items.Write(write);
        }

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new PdfActionArray(array, tracker.Doc.Catalog);
        }

        #endregion
    }

    public sealed class GoToAction : PdfAction
    {
        #region Variables and properties

        /// <summary>
        /// The destination
        /// </summary>
        private PdfItem D { get { return _elems["D"]; } }

        /// <summary>
        /// The destination for this action
        /// </summary>
        public PdfDestination Destination { get { return PdfOutlineItem.FindDestination(D, _elems, "D", _cat); } }

        #endregion

        #region Init

        internal GoToAction(PdfDictionary dict, PdfCatalog cat)
            : base(dict, cat)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new GoToAction(elems, ((WritableDictionary) elems).Tracker.Doc.Catalog);
        }

        #endregion
    }
}
