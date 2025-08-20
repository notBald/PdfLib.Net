using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Internal
{
    /// <summary>
    /// Postscript virtual memory
    /// </summary>
    /// <remarks>
    /// All arrays, dictionaries, files, and procedures are put in this list
    /// of objects. That way they can be saved and restored.
    /// 
    /// Using WeakReference so I don't have to bother with refcounting. Though
    /// the weakreferences should be pruned for long running programs.
    /// 
    /// Note that "high level objects" are not tracked by the VM. It is assumed
    /// one don't start interacting with such objects until after the script
    /// has executed. However, highlevel objects can be registred too if there's
    /// a need.
    /// </remarks>
    internal class PSVM
    {
        #region Variables and properties

        readonly List<WeakReference> _objects;

        internal PSObject this[PSObject index]
        {
            get
            {
                _objects.Add(new WeakReference(index));
                return index;
            }
        }

        internal PSArray this[PSArray index]
        {
            get
            {
                _objects.Add(new WeakReference(index));
                return index;
            }
        }

        internal PSDictionary this[PSDictionary index]
        {
            get
            {
                _objects.Add(new WeakReference(index));
                return index;
            }
        }

        internal PSProcedure this[PSProcedure index]
        {
            get
            {
                _objects.Add(new WeakReference(index));
                return index;
            }
        }

        #endregion

        #region Init

        public PSVM()
        {
            _objects = new List<WeakReference>(50);
        }

        public void Clear() { _objects.Clear(); }

        #endregion

        /// <summary>
        /// Add object to be tracked
        /// </summary>
        /// <param name="o">Object that will be saved and restored</param>
        public void Add(PSObject o) { _objects.Add(new WeakReference(o)); }

        /// <summary>
        /// Saves the current VM
        /// </summary>
        public PSSave Save()
        {
            var l = new List<saved_obj>(_objects.Count);
            for (int c = 0; c < _objects.Count; c++)
            {
                var obj = (PSObject) _objects[c].Target;
                if (obj != null)
                    l.Add(new saved_obj(obj, obj.ShallowClone()));
            }

            return new PSSave(l.ToArray());
        }

        struct saved_obj
        {
            public readonly PSObject obj;
            public readonly PSItem itm;
            public saved_obj(PSObject o, PSItem i) { obj = o; itm = i; }
        }
    }
}
