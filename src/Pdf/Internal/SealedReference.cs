using System.Diagnostics;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Read;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Used by imported documents
    /// </summary>
    [DebuggerDisplay("{DebugString}")]
    internal sealed class SealedReference : PdfReference
    {
        #region Variables and properties

        /// <summary>
        /// Owner of this reference
        /// </summary>
        readonly PdfFile _owner;

        /// <summary>
        /// Value of the reference
        /// </summary>
        /// <remarks>Same as "Deref(), use that instead</remarks>
        public PdfItem Value
        {
            get { return Deref(); }
        }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        internal SealedReference(PdfFile owner, PdfObjID id) 
            : base(id)
        { _owner = owner; }

        /// <summary>
        /// Only for use before the trailer is constructed.
        /// </summary>
        internal SealedReference(SealedReference org, PdfItem value)
            : this(org._owner, org._id)
        { _value = value; }

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
                lock(_owner)
                {
                    //Value may have been updated while waiting for the lock.
                    if (_value == null)
                        _value = _owner.FetchObject(this);
                    
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
                lock (_owner)
                {
                    //Value may have been updated while waiting for the lock.
                    if (_value == null || _value.Type != type)
                        _value = _owner.FetchObject(this, type, msg, obj);
                    Debug.Assert(_value.Type == type);

                    value = _value;
                }
            }

            return (PdfObject)value;
        }

        internal override void Write(Write.Internal.PdfWriter write)
        { throw new System.NotSupportedException("Must use WritableReference"); }

        #endregion
    }

    /// <summary>
    /// Temporary reference. Does not require a owner or id.
    /// </summary>
    /// <remarks>Used by the PageTree class.</remarks>
    internal sealed class TempReference : PdfReference
    {
        #region Variables

        /// <summary>
        /// This refcount isn't like the refcount of WritableReference,
        /// instead it just serves as a hint. If a temporary reference
        /// is used multiple times this should be set true, same if the
        /// item the temp ref is pointing towards is used multiple times.
        /// 
        /// When the time comes to convert this to a WritableReference
        /// the tracker will do a search through it's resources if this
        /// flag is set.
        /// </summary>
        public bool RefCount = false;

        #endregion

        #region Init

        internal TempReference(PdfItem value, bool refcount)
            : this(value)
        { RefCount = refcount; }

        internal TempReference(PdfItem value)
            : base(PdfObjID.Null)
        { _value = value; }

        internal TempReference(PdfObjID id)
            : base(id)
        { }

        #endregion

        #region Overrides

        public override PdfItem Deref() { return _value; }

        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            var value = _value;
            if (value.Type != type)
            {
                lock(this)
                {
                    if (value == _value || _value.Type != type)
                        _value = _value.ToType(type, msg, obj);

                    value = _value;
                }
            }
            return (PdfObject)value;
        }

        internal override void Write(Write.Internal.PdfWriter write)
        { throw new System.NotSupportedException("Must use WritableReference"); }

        #endregion
    }
}
