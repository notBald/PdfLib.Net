using System;
using System.Reflection;
using System.ComponentModel;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Read;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Not to be confused with IObject, this is the base class for
    ///  - Arrays
    ///  - Dictionaries
    ///  - Other "high level objects"
    /// </summary>
    /// <remarks>
    /// PdfObject do not serve any true purpose beyond code clarity.
    /// 
    /// This is the inheritance tree:
    ///  PdfItem
    ///  |--PdfPrimitives
    ///  |--PdfReference
    ///  ---PdfObjects
    ///     |-DictCatalog
    ///     | |-PdfDictionary
    ///     etc
    /// 
    /// Note that when a method returns a value of the type PdfObject
    /// one know straight away that it's not a reference (I.e. one do 
    /// not need to dereference) and that it's not a primitive (so one
    /// can't use the Get methods)
    /// 
    /// Note that PdfDictionary call Copy on PdfObjects when cloning,
    /// but not PdfItems*
    /// 
    /// * I'm not sure PdfObject is needed anymore.
    /// 
    ///   The ICRef/Elements/ItemsArray combination now does all pdfObject
    ///   initially set out to do. The "Copy" mentioned in the comment above
    ///   is now gone, and all "is PdfObject" now sits in commented out code
    ///   
    ///   Though some methods accepts and returns "PdfObject" still.
    /// </remarks>
    public abstract class PdfObject : PdfItem
    {
        #region Sealed off overrides

        /// <summary>
        /// There's no integer representation for objects
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public sealed override int GetInteger()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// There's no real representation for objects
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public sealed override double GetReal()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// There's no string representation for objects
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public sealed override string GetString()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public sealed override PdfItem Deref() { return this; }

        /// <summary>
        /// Sealed off do to how caching is implemented.
        /// </summary>
        public sealed override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        [System.Diagnostics.DebuggerStepThrough()]
        public sealed override int GetHashCode()
        {
            return base.GetHashCode();
        }

        #endregion

        /// <remarks>
        /// Almost all objects, besides streams, can be saved in a
        /// object stream (a.k.a. compressed) with a few special
        /// case exceptions. This makes compressed a good default.
        /// 
        /// Don't use auto as some objects can't be saved directly,
        /// and there's no practical way of knowing which.
        /// </remarks>
        internal override SM DefSaveMode { get { return SM.Compressed; } }

        /// <summary>
        /// This method reads the version information from the
        /// attribute set on the class. It does not read from
        /// children of the class.
        /// </summary>
        public override sealed PdfVersion PdfVersion
        {
            get
            {
                var type = GetType();
                object[] attribs = type.GetCustomAttributes(typeof(PdfVersionAttribute), true);


                PdfVersion ver = PdfVersion.V10;
                for(int c=0; c < attribs.Length; c++)
                {
                    var v = ((PdfVersionAttribute)attribs[c]).Ver;
                    if (ver < v) ver = v;
                }

                return ver;
            }
        }

        /// <summary>
        /// Does a memberwise clone
        /// </summary>
        /// <remarks>
        /// Just about everything overrides this method, so perhaps it would
        /// be better to make it abstract
        /// </remarks>
        public override PdfItem Clone() { return (PdfItem) MemberwiseClone(); }

        /// <summary>
        /// Cast this object into a different type of PdfObject
        /// </summary>
        /// <param name="owner">Owner of the object</param>
        /// <param name="type">Type of the object</param>
        /// <param name="msg">Message</param>
        /// <returns>Object of the given type</returns>
        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            if (type != Type)
                throw new NotSupportedException("Can't change this object into a diffferent type");
            return this;
        }
    }
}
