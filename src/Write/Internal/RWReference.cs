using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Read / write reference. 
    /// </summary>
    internal class RWReference : WritableReference
    {
        #region Variables and properties

        /// <summary>
        /// The id this reference has in the file
        /// </summary>
        PdfObjID _file_id;

        #endregion

        #region Init

        internal RWReference(ResTracker owner, PdfObjID id, PdfObjID file_id)
            : base(owner, id, null)
        {
            _file_id = id;
        }

        internal RWReference(ResTracker owner, PdfObjID id, PdfObjID file_id, PdfItem value)
            : base(owner, id, value)
        {
            _file_id = id;
        }

        #endregion
        
        #region Overrides

        /// <summary>
        /// Gets the item this reference is pointing at.
        /// </summary>
        public override PdfItem Deref()
        {
            var value = _value;
            if (value == null)
            {
                var owner = Tracker.Doc.File;
                lock (owner)
                {
                    if (value == null)
                    {
                        var tmp = _id;
                        _id = _file_id;
                        try
                        {
                            value = owner.FetchObject(this);
                            //Since the value is parsed, no need to check if
                            //References is set already
                            if (value is IRef)
                                ((IRef)value).Reference = this;
                            _value = value;
                            SaveMode = value.DefSaveMode;
                        }
                        finally { _id = tmp; }
                    }
                    else
                        value = _value;
                }
            }

            return value;
        }

        /// <summary>
        /// Used to change the value into the spesified type.
        /// </summary>
        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            var value = _value;
            if (value == null || value.Type != type)
            {
                var owner = Tracker.Doc.File;
                lock (owner)
                {
                    if (_value == null || _value.Type != type)
                    {
                        var tmp = _id;
                        _id = _file_id;
                        try { value = owner.FetchObject(this, type, msg, obj); }
                        finally { _id = tmp; }
                        if (value is IRef)
                        {
                            var iref = (IRef)value;
                            if (!iref.HasReference)
                                iref.Reference = this;
                        }

                        Debug.Assert(value.Type == type);

                        _value = value;
                    }

                    return (PdfObject)_value;
                }
            }

            return (PdfObject)value;
        }

        internal override void Invalidate(bool negate)
        {
            base.Invalidate(negate);
            if (negate)
                _file_id = _file_id.GetAsInvalid();
        }

        #endregion
    }
}
