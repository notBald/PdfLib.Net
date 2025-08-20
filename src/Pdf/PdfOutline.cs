using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf
{
    /// <summary>
    /// The root outline
    /// </summary>
    [DebuggerDisplay("{Count} outlines")]
    public class PdfOutline : Elements, IEnumerable<PdfOutlineItem>
    {
        #region Variables and properties

        /// <summary>
        /// The catalog is used to find destination and pages the outline is associated  with.
        /// </summary>
        /// <remarks>
        /// This is a conviniece feature. Otherwise callers would have to find this stuff
        /// themselves.
        /// </remarks>
        protected PdfCatalog _catalog;

        internal sealed override PdfType Type { get { return PdfType.Outline; } }

        /// <remarks>
        /// Specs require this to be indirect. (Compressed is also indirect)
        /// </remarks>
        internal sealed override SM DefSaveMode { get { return SM.Compressed; } }

        /// <summary>
        /// Whenever this outline is open or closed
        /// </summary>
        public bool Open
        {
            get { return _elems.GetInt("Count", 0) > 0; }
            set
            {
                if (Open == value) return;

                if (value)
                    Count = Count;
                else
                    Count = Count * -1;

                if (this is PdfOutlineItem)
                {
                    var parent = ((PdfOutlineItem) this).Parent;
                    if (parent != null)
                        parent.UpdateCount(_elems.GetInt("Count", 0));
                }
            }
        }

        /// <summary>
        /// The number of outlines
        /// </summary>
        public int Count 
        { 
            get { return Math.Abs(_elems.GetInt("Count", 0)); }
            protected set 
            { 
                if (value == 0) 
                    _elems.Remove("Count"); 
                else 
                    _elems.SetInt("Count", value); 
            } 
        }

        /// <summary>
        /// "Count" isn't reliable unless "RepairCount" is run, so this is a reliable alternative.
        /// </summary>
        public int ChildCount
        {
            get
            {
                int num = 0;
                foreach (var child in this) //<-- foreach handles cycles
                    num++;
                return num;
            }
        }

        /// <summary>
        /// First top level bookmark in the outline
        /// </summary>
        /// <remarks>Can be null</remarks>
        public PdfOutlineItem First 
        { 
            get { return (PdfOutlineItem) _elems.GetPdfType("First", PdfType.Outline, IntMsg.NoMessage, _catalog); }
            internal set { _elems.SetItem("First", value, true); }
        }

        /// <summary>
        /// Last top level bookmark in the outline
        /// </summary>
        /// <remarks>Can be null</remarks>
        public PdfOutlineItem Last 
        { 
            get { return (PdfOutlineItem)_elems.GetPdfType("Last", PdfType.Outline, IntMsg.NoMessage, _catalog); }
            internal set { _elems.SetItem("Last", value, true); }
        }

        #endregion

        #region Init

        internal PdfOutline(PdfDictionary dict, PdfCatalog doc)
            : base(dict) { dict.CheckType("Outlines"); _catalog = doc; }

        public PdfOutline()
            : base(new TemporaryDictionary())
        { /* An empty outline has nothing in it */ }

        #endregion

        /// <summary>
        /// Conviniece function for adding a bookmark
        /// </summary>
        /// <param name="title">Title of the bookmark</param>
        /// <param name="page">Page the bookmark is to point towards</param>
        public PdfOutlineItem AddChild(string title, PdfPage page)
        {
            var oi = new PdfOutlineItem(title, page);
            AddChild(oi);
            return oi;
        }

        /// <summary>
        /// Adds an outline at the last position
        /// </summary>
        /// <param name="outline">The outline to add</param>
        public void AddChild(PdfOutlineItem outline)
        {
            outline = (PdfOutlineItem) _elems.Adopt(outline);
            outline.Parent = this;
            var last = Last;
            if (last == null)
            {
                First = outline;
                Last = outline;

                //Single children does not have any Prev or Next
                //This if ( ... != null ) is here to avoid a debug.assert
                if (outline.Prev != null || outline.Next != null)
                {
                    outline.Prev = null;
                    outline.Next = null;
                }
            }
            else
            {                
                outline.Prev = last;
                last.Next = outline;
                Last = outline;
                if (outline.Next != null) //<-- avoids an assert
                    outline.Next = null;
            }

            UpdateCount(1 + outline.Count);
        }

        public void Add(PdfOutline root)
        {
            if (root == null) return;
            foreach (PdfOutlineItem child in root)
                AddChild(child);
        }

        internal void UpdateCount(int changed_count)
        {
            if (changed_count != 0)
            {
                int old_count = Count;
                int new_count = Math.Abs(Count) + changed_count;
                if (new_count < 0)
                {
                    Debug.Assert(false, "When there something ammis with count it should be repaird.");
                    RepairCount();
                    return;
                }
                if (old_count < 0) new_count *= -1;

                Count = new_count;

                if (this is PdfOutlineItem)
                    ((PdfOutlineItem)this).Parent.UpdateCount(changed_count);
            }
        }

        #region Repair

        /// <summary>
        /// Reparirs the "Count" values of outlines so that
        /// they reflect how man are open.
        /// 
        /// Note: Can't currently be used on sealed documents.
        /// </summary>
        /// <remarks>
        /// Perhaps an idea to run this before saving imported documents as improperly
        /// set count values aren't uncommon.
        /// </remarks>
        public void RepairCount()
        {
            //Starts from the root
            if (this is PdfOutlineItem)
                ((PdfOutlineItem)this).Parent.RepairCount();
            else
                this.Count = Recount();
        }
        private int Recount()
        {
            int true_count = 0;
            foreach (PdfOutlineItem outline in this)
            {
                true_count++;
                int child_count = outline.Recount();
                if (outline.Open)
                    true_count += child_count;
                else
                    child_count *= -1;
                ((PdfOutline)outline).Count = child_count;
            }

            return true_count;
        }

        /// <summary>
        /// For fixing full circles
        /// </summary>
        internal void SetPrevToNull()
        {
            _elems.DirectSet("Prev", null);
        }

        /// <summary>
        /// For fixing full circles
        /// </summary>
        internal void SetNextToNull()
        {
            _elems.DirectSet("Next", null);
        }

        #endregion

        #region Required overrides
        //Note: Assumes PdfDictionary is a WritableDictionary, however these functions
        //      are only used when moving an object into a writable document. 

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfOutline(elems, ((WritableDictionary)elems).Tracker.Doc.Catalog);
        }

        protected sealed override void DictChanged()
        {
            _catalog = ((WritableDictionary)_elems).Tracker.Doc.Catalog;
        }

        #endregion

        #region IEnumarable

        IEnumerator<PdfOutlineItem> IEnumerable<PdfOutlineItem>.GetEnumerator() { return new OutlineEnum(this); }
        public System.Collections.IEnumerator GetEnumerator() { return new OutlineEnum(this); }


        ///<remarks>
        /// Detecting malformed lists:
        ///  all nodes have a next and a last setting. If the last setting does not refere the
        ///  previous node we may have a cycle or different sort of error. This is silently
        ///  fixed
        ///</remarks> 
        class OutlineEnum : IEnumerator<PdfOutlineItem>
        {
            /// <summary>
            /// Keeps track of the first seen node
            /// </summary>
            readonly PdfOutlineItem _first;

            /// <summary>
            /// the node curently beeing viewed
            /// </summary>
            PdfOutlineItem _current;
            bool _done;

            PdfOutlineItem IEnumerator<PdfOutlineItem>.Current { get { return _current; } }
            public object Current { get { return _current; } }

            public OutlineEnum(PdfOutline parent)
            {
                _first = parent.First;
                if (_first == null)
                {
                    _done = true;
                    return;
                }

                //Loops are silently repaired.
                if (_first.Prev != null)
                    _first.SetPrevToNull();
            }

            public bool MoveNext()
            {
                if (_current == null)
                {
                    if (_done) return false;
                    _current = _first;
                }
                else
                {
                    var last = _current;
                    _current = _current.Next;
                    if (_current == null)
                    {
                        _done = true;
                        return false;
                    }

                    if (!ReferenceEquals(_current.Prev, last))
                    {
                        //We have a sircular liked list. Not sure what Adobe will
                        //do but I just ignore the situation.
                        last.SetNextToNull();
                        _done = true;
                        _current = null;

                        return false;
                    }
                }

                return true;
            }

            public void Reset()
            {
                _done = false;
                _current = null;
            }

            public void Dispose()
            { }
        }

        #endregion

    }
}
