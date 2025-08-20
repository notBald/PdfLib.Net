#define SHOW_REFCOUNT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Used for writable documents.
    /// </summary>
#if !SHOW_REFCOUNT
    [DebuggerDisplay("{DebugString}")]
#endif
    internal class WritableReference : PdfReference
    {
        #region Variables and properties

        /// <summary>
        /// How many objects holds this reference.
        /// </summary>
        /// <remarks>Used for auto save mode, so 
        /// no disaster if it's incorect.
        /// 
        /// Initialized to min value to hint to
        /// the ResTracker that this reference
        /// has yet to be registered.
        /// 
        /// If one for some reason don't register
        /// child references when constructing the
        /// object, set this value zero.
        /// </remarks>
        internal protected int RefCount = int.MinValue;

        /// <summary>
        /// How this reference is to be saved.
        /// </summary>
        internal SM SaveMode;

        /// <summary>
        /// Used when writing the reference to the stream.
        /// </summary>
        /// <remarks>
        /// Id and gen of the saved reference may differ from what
        /// the reference curently holds. Though Pdflib will try to 
        /// keep them the the same.
        /// </remarks>
        internal byte[] SaveString;

        /// <summary>
        /// String representation of SaveString, for reading in the debugger.
        /// </summary>
        public string SaveDebugString { get { return SaveString != null ? Read.Lexer.GetString(SaveString) : null; } }

        /// <summary>
        /// The owning tracker of this writable reference. Used
        /// to check ownership of references
        /// </summary>
        /// <remarks>Can be null</remarks>
        readonly internal ResTracker Tracker;

        #endregion

        #region Init

        /// <summary>
        /// Note, savemode is expected to be set by the creator
        /// </summary>
        internal WritableReference(ResTracker owner, PdfObjID id, PdfItem value)
            : base(id)
        {
            if (value is PdfReference)
                throw new PdfNotSupportedException("Reference to reference");

#if DEBUG
            //if (id.Nr == 76)
            //{
            //    Debug.Assert(true);
            //}
#endif

            _value = value;
            if (value != null)
            {
                SaveMode = value.DefSaveMode;

                //Attaches the reference
                if (value is IRef)
                {
                    var iref = (IRef)value;
                    if (!iref.HasReference)
                        iref.Reference = this;
                }
            }
            Tracker = owner;
        }

        #endregion

        #region IEnumRef

        /// <summary>
        /// Tells if there are child references in need of counting. 
        /// Not whenever this reference has a value or not.
        /// </summary>
        internal bool HasChildren
        { 
            get 
            {
                var value = _value;
                return (value is IEnumRef) ? ((IEnumRef)value).HasChildren : false; 
            } 
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Gets the item this reference is pointing at.
        /// </summary>
        public override PdfItem Deref()
        {
            return _value;
        }

        /// <summary>
        /// Used to change the value into the spesified type.
        /// </summary>
        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            var value = _value;
            if (value.Type != type)
            {
                lock(this)
                {
                    if (value == _value)
                    {
                        value = _value = value.ToType(type, msg, obj);
                    }
                }
               
            }
            return (PdfObject)value;
        }

        /// <summary>
        /// Writes itself or its child to the stream
        /// </summary>
        internal override void Write(Write.Internal.PdfWriter write)
        {
            if (SaveString == null)
                Deref().Write(write);
            else
                write.Write(SaveString);
        }

        /// <summary>
        /// Get the id out of a save string
        /// </summary>
        internal static int GetId(byte[] str)
        {
            int num = 0;
            for (int c = 0; true; c++)
            {
                byte ch = str[c];
                if (str[c] == 32) break;
                num = num * 10 + (ch - 48);
            }
            return num;
        }

        /// <summary>
        /// Get the gen out of a save string
        /// </summary>
        internal static int GetGen(byte[] str)
        {
            int c = 1;
            while (str[c++] != 32) ;
            int num = 0;
            while (true)
            {
                byte ch = str[c];
                if (str[c] == 32) break;
                num = num * 10 + (ch - 48);
                c++;
            }
            return num;
        }


        /// <summary>
        /// Used when fixing xref tables. Missing objects are set to null objects
        /// </summary>
        /// <remarks>
        /// The PDF specs treats null as something to ignore, but it would be more
        /// improper to remove this reference fully. But we don't know where this
        /// reference is used, and while there is a "ignore" savemode it's not
        /// taken into account when saving dictionaries or arrays.
        /// 
        /// This means the "null" will be saved out to the file. Not ideal but
        /// not worth fixing either.
        /// 
        /// In case of dictionaries the specs say "Specifying the null object as 
        /// the value of a dictionary entry shall be equivalent to omitting the 
        /// entry entirely." IOW one could add code like this to 
        /// PdfWrite.WriteDictionary:
        ///  if value == pdfnull continue;
        /// </remarks>
        internal void SetNull()
        {
            _value = PdfNull.Value;
            SaveMode = _value.DefSaveMode;
        }

#if SHOW_REFCOUNT
        public override string ToString()
        {
            return string.Format("iref({0}, {1} : {2})", ObjectNr, GenerationNr, RefCount);
        }
#endif

        #endregion
    }

    /// <summary>
    /// Used for valueless writable references.
    /// </summary>
    internal class TempWRef : WritableReference
    {
        public TempWRef(ResTracker owner, PdfObjID id)
            : base(owner, id, null) { }
        public TempWRef(ResTracker owner, PdfObjID id, PdfItem value)
            : base(owner, id, value) { }

        public void SetValue(PdfItem val)
        {
            Debug.Assert(!(val is PdfReference), "Reference to reference Ohoy!");
            if (_value is IRef)
            {
                var iref = (IRef)_value;
                if (ReferenceEquals(iref.Reference, this))
                    iref.Reference = null;
            }

            //Attaches the reference
            if (val is IRef)
            {
                var iref = (IRef)val;
                if (!iref.HasReference)
                    iref.Reference = this;
            }

            _value = val;
            SaveMode = val.DefSaveMode;
        }
    }
}
