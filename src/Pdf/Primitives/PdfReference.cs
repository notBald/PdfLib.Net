#define RefDebug //<-- Refs normaly add debugging info that may change its state
using System;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
using System.Collections.Generic;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Holds an object or a reference to an object
    /// </summary>
    [DebuggerDisplay("{DebugString}")]
    public abstract class PdfReference : PdfItem, IComparable, IEnumRef
    {
        #region Variables and properties

        /// <summary>
        /// Object this reference points to
        /// </summary>
        protected PdfObjID _id;

        /// <summary>
        /// The value this reference points at
        /// </summary>
        protected PdfItem _value;

        /// <summary>
        /// To check that there is a value without having
        /// the object being parsed.
        /// </summary>
        internal bool HasValue { get { return _value != null; } }

        /// <summary>
        /// Object number of this reference
        /// </summary>
        public int ObjectNr { get { return _id.Nr; } }

        /// <summary>
        /// Generation of this reference
        /// </summary>
        public int GenerationNr { get { return _id.Gen; } }

        /// <summary>
        /// Full Id of this reference
        /// </summary>
        internal PdfObjID Id { get { return _id; } }

        /// <summary>
        /// Dereferences type
        /// </summary>
        internal sealed override PdfType Type
        {
            get { return Deref().Type; }
        }

        /// <summary>
        /// In the current implementation save mode is never read from
        /// a reference. 
        /// </summary>
        /// <remarks>Later one can have this method read SM from the child</remarks>
        internal sealed override SM DefSaveMode
        {
            get { throw new NotSupportedException(); }
        }

        /// <summary>
        /// Gets version information from the child,
        /// but only if the child is avalible.
        /// </summary>
        public override sealed PdfVersion PdfVersion
        {
            get
            {
                if (_value != null)
                    return _value.PdfVersion;
                return PdfVersion.V00;
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        internal PdfReference(PdfObjID id) { _id = id; }

        #endregion

        #region IComparable

        int IComparable.CompareTo(object obj)
        {
            var other = (PdfReference)obj;

            return _id.CompareTo(other._id);
        }

        #endregion

        #region IEnumRef

        /// <summary>
        /// Tells if there are child references in need of counting. 
        /// Not whenever this reference has a value or not.
        /// </summary>
        bool IEnumRef.HasChildren
        { 
            get 
            {
                var value = Deref();
                return (value is IEnumRef) ? ((IEnumRef)value).HasChildren : false; 
            } 
        }

        /// <summary>
        /// The child's referecnes
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        {
            get
            {
                var value = Deref();
                if (value is IEnumRef)
                    return ((IEnumRef)value).RefEnumerable;
                return new IEnumRefArImpl();
            }
        }


        #endregion

        #region Overrides

        /// <summary>
        /// Dereferences integer
        /// </summary>
        public override int GetInteger()
        {
            return Deref().GetInteger();
        }

        /// <summary>
        /// Dereferences real
        /// </summary>
        public override double GetReal()
        {
            return Deref().GetReal();
        }

        /// <summary>
        /// Dereferences string
        /// </summary>
        public override string GetString()
        {
            return Deref().GetString();
        }

        internal override PdfVersion GetPdfVersion(bool fetch_obj, HashSet<object> set)
        {
            //References add themselves to the set too. No harm.
            set.Add(this);

            if (fetch_obj)
            {
                var kid = Deref();
                if (!set.Contains(kid))
                    return kid.GetPdfVersion(true, set);
            }
            return PdfVersion;
        }

        internal override void Write(Write.Internal.PdfWriter write, Write.Internal.SM store_mode)
        {
            throw new PdfInternalException("Tried to save reference as indirect");
        }

        #endregion

        #region Other methods

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            return _id.Equals(obj);
        }

        public override PdfItem Clone()
        {
            Debug.Assert(false, "Cloning a reference. Sure you know what you're doing?");
            
            //Not bothering even if they're mutalble
            return this;
        }

        /// <summary>
        /// Removes cached data.
        /// </summary>
        /// <remarks>Except if the savemode is ignore to avoid flushing the trailer</remarks>
        internal void Flush()
        {
            if (_value != null && _value.DefSaveMode != SM.Ignore)
                _value = null;
        }

        /// <summary>
        /// Ivalidates the id of the reference.
        /// </summary>
        /// <param name="negate">True if the id is to be set negative</param>
        /// <remarks>
        /// Used when a reference is removed from the xref table.
        /// 
        /// (To prevent situations where (by mistake) the id of a removed 
        /// reference gets used, introducing potentialy subtle bugs.)
        /// 
        /// Now also used when preloading objects. If an object can't be loaded,
        /// instead of handeling the exception the _value is set back to null.
        /// That way an exception can be thrown if the resource is actually needed.
        /// </remarks>
        internal virtual void Invalidate(bool negate)
        {
            if (negate)
                _id = _id.GetAsInvalid();
            _value = null;
        }

        /// <summary>
        /// Generates a hashcode for use in dictionaries
        /// </summary>
        /// <remarks>
        /// Not guaranteed to be unique across documents, however the
        /// Equals method is as it checks for reference equality.
        /// </remarks>
        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        #endregion

        #region Static methods

        /// <summary>
        /// Method for getting the object id of a reference
        /// </summary>
        /// <param name="obj">The reference</param>
        /// <returns>
        /// The object id of a reference or a empty
        /// PdfObjID.
        /// </returns>
        internal static PdfObjID GetRefId(PdfItem obj)
        {
            if (obj is PdfReference)
                return ((PdfReference)obj).Id;
            return PdfObjID.Null;
        }

        #endregion

        #region Debug aid

        /// <summary>
        /// String representation. Note, can't use the debug string since
        /// there's likely circular references.
        /// </summary>
        public override string ToString() { return string.Format("iref({0}, {1})", ObjectNr, GenerationNr); }

        /// <summary>
        /// String used in the debugger
        /// </summary>
        public string DebugString 
        { 
            get 
            {
#if RefDebug
                try { return ToString(); }
#else
                try { return string.Format("iref({0}, {1}): {2}", ObjectNr, GenerationNr, Deref().ToString()); }
#endif
                catch { return ToString(); }

            } 
        }

        #endregion
    }
}
