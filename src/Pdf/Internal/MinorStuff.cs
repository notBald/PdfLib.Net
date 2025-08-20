/*
 * This file is used for some small (in code size) structs, classes and enums.
 * Saves me the time to create new files for 'em.
 */
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    public enum PdfAlphaInData
    {
        /// <summary>
        /// No alpha in data
        /// </summary>
        None = 0,

        /// <summary>
        /// Normal alpha
        /// </summary>
        Alpha = 1,

        /// <summary>
        /// Alpha preblended with a background
        /// </summary>
        PreblendedAlpha = 2
    }

    /// <summary>
    /// An array with two byte strings
    /// </summary>
    public class PdfDocumentID : ItemArray
    {
        /// <summary>
        /// PdfType.DocumentID
        /// </summary>
        internal override PdfType Type { get { return PdfType.DocumentID; } }

        /// <summary>
        /// Indirect isn't forbidden in unencrypted documents
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Direct; } }

        /// <summary>
        /// The first byte string
        /// </summary>
        public byte[] First { get { return _items.GetStringObjEx(0).ByteString; } }

        /// <summary>
        /// The second byte string
        /// </summary>
        public byte[] Second { get { return _items.GetStringObjEx(1).ByteString; } }

        #region Init

        internal PdfDocumentID(PdfArray ar)
            : base(ar)
        {
            if (ar.Length != 2)
                throw new PdfReadException(PdfType.Array, ErrCode.Invalid);
        }

        #endregion

        #region Boilerplate code

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new PdfDocumentID(array);
        }

        #endregion
    }

    /// <summary>
    /// Version number enum
    /// </summary>
    public enum PdfVersion
    {
        /// <summary>
        /// V00 is used by basic dictionaries, 
        /// references and arrays to denote they
        /// can't give the version number.
        /// 
        /// (No code depend on this feature, but
        /// it's nice for debugging)
        /// </summary>
        V00 = -1, 

        //Note, the numeric value is important.
        V10, V11, V12, V13, 
        V14, V15, V16, V17
    }

    public enum PdfPageMode
    {
        /// <summary>
        /// Show only the page
        /// </summary>
        UseNone,

        /// <summary>
        /// Show the page with bookmarks
        /// </summary>
        UseOutlines,

        /// <summary>
        /// Show the page with a thumbnail panel
        /// </summary>
        UseThumbs,

        /// <summary>
        /// Show the page full screen
        /// </summary>
        FullScreen,

        /// <summary>
        /// Show the page with the optinal contents panel
        /// </summary>
        UseOC,

        /// <summary>
        /// Show the page with the attachments panel
        /// </summary>
        UseAttachments,

        /// <summary>
        /// This mode wasn't regognized by PdfLib
        /// </summary>
        Unrecognized
    }

    public enum ImageFormat
    {
        /// <summary>
        /// The image data is in some raw format decided
        /// by the colorspace and bits per component paramters
        /// </summary>
        RAW,

        /// <summary>
        /// Fax encoding
        /// </summary>
        CCITT,

        /// <summary>
        /// JBIG2 encoding
        /// </summary>
        JBIG2,

        /// <summary>
        /// Jpeg encoding
        /// </summary>
        JPEG,

        /// <summary>
        /// Jpeg 2000/JPX encoding
        /// </summary>
        JPEG2000
    }

    /// <summary>
    /// Used to mark classes and properties with a version requirement.
    /// </summary>
    public class PdfVersionAttribute : Attribute
    {
        public readonly PdfVersion Ver;

        public PdfVersionAttribute(PdfVersion version)
        {
            Ver = version;
        }

        public PdfVersionAttribute(string version)
        {
            if (version.Length != 3 || version[0] != '1' || version[1] != '.')
                Ver = PdfVersion.V10; //<-- errors are ignored
            else
            {
                char num = version[2];
                Ver = (PdfVersion)(num - '0');
            }
        }
    }

    /// <summary>
    /// The orientation of the page, as reflected by the page's rotation property
    /// </summary>
    /// <remarks>
    /// PdfSharp displays a page orientation. It's usefull when one wish to know
    /// the orientation of the Width and Height values of the page.
    ///
    /// So it's not part of the specs. But usefull enough to emulate.
    /// </remarks>
    public enum PdfPageOrientation
    {
        /// <summary>
        /// Width is width and height is height
        /// </summary>
        Portrait,

        /// <summary>
        /// Width is height and height is width
        /// </summary>
        Landscape
    }

    /// <summary>
    /// Object id
    /// </summary>
    /// <remarks>
    /// Holds:
    ///  -information for identifying an object
    /// </remarks>
    public struct PdfObjID : IComparable
    {
        #region Variables and properties

        /// <summary>
        /// The number of this object
        /// </summary>
        readonly int _nr;

        /// <summary>
        /// The generation of this object
        /// </summary>
        /// <remarks>
        /// The reason this is a ushort is do to the original
        /// PDF trailer specs not needing more, but streams can
        /// go beyond this. TODO: Fix (It can be as easy as 
        /// changeing the variable, but one have to check other 
        /// code too)
        /// </remarks>
        readonly ushort _gen;

        /// <summary>
        /// If this is a null pointer
        /// </summary>
        public bool IsNull { get { return _nr == 0; } }

        /// <summary>
        /// The object's number
        /// </summary>
        public int Nr { get { return _nr; } }

        /// <summary>
        /// The object's generation
        /// </summary>
        public int Gen { get { return _gen; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        public PdfObjID(int id, ushort gen) { _nr = id; _gen = gen; }

        #endregion

        #region Methods

        /// <summary>
        /// Compares the objects for equality
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is PdfObjID)
            {
                var o = (PdfObjID)obj;
                return _nr == o._nr && _gen == o._gen;
            }
            return false;
        }

        #region IComparable Members

        /// <summary>
        /// Compares objects
        /// </summary>
        public int CompareTo(object obj)
        {
            //if (obj is PdfObjID)
            //{
                var o = (PdfObjID)obj;
                if (_nr == o._nr)
                    return _gen - o._gen;
                return _nr - o._nr;
            //}
            //return 1;
        }

        #endregion

        #region Overrides recommended for value types

        /// <summary>
        /// Creates a hash code
        /// </summary>
        [DebuggerStepThrough]
        public override int GetHashCode()
        {
            return _nr ^ _gen;
        }

        /// <summary>
        /// String representation of the object
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0:0000000000} {1:00000}", _nr, _gen);
        }

        #endregion

        #endregion

        #region Operators

        /// <summary>
        /// Checks for equality
        /// </summary>
        public static bool operator ==(PdfObjID l, PdfObjID r)
        {
            return l._nr == r._nr && l._gen == r._gen;
        }

        /// <summary>
        /// Checks for inequality
        /// </summary>
        public static bool operator !=(PdfObjID l, PdfObjID r)
        {
            return l._nr != r._nr || l._gen != r._gen;
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// When one deliberatly want a invalid id that remains "unique"
        /// </summary>
        public PdfObjID GetAsInvalid() { return new PdfObjID((_nr < 0) ? _nr : _nr * -1, _gen); }

        /// <summary>
        /// Returns a null id
        /// </summary>
        public static PdfObjID Null { get { return new PdfObjID(); } }

        #endregion
    }

    /// <summary>
    /// Describes a range.
    /// </summary>
    /// <remarks>
    /// Used internaly in string objects
    /// </remarks>
    [DebuggerDisplay("Range: {Start} - {Length}")]
    public struct Range
    {
        /// <summary>
        /// Start of the range
        /// </summary>
        public readonly int Start;

        /// <summary>
        /// Length of the range
        /// </summary>
        public readonly int Length;

        /// <summary>
        /// Constructor
        /// </summary>
        public Range(int start, int length) { Start = start; Length = length; }

        #region Overrides recommended for value types

        public override int GetHashCode() { return Start ^ Length; }
        public override bool Equals(object obj)
        {
            if (obj is Range)
            {
                var r = (Range)obj;
                return Start == r.Start && Length == r.Length;
            }
            return false;
        }
        public static bool operator ==(Range range1, Range range2)
        {
            return range1.Equals(range2);
        }
        public static bool operator !=(Range range1, Range range2)
        {
            return !range1.Equals(range2);
        }
        public override string ToString()
        {
            return string.Format("Range: {0} - {1}", Start, Length);
        }

        #endregion
    }

    /// <summary>
    /// Describes a range with explicit start and end.
    /// </summary>
    [DebuggerDisplay("iRange: {Start} : {End}")]
    public struct iRange
    {
        /// <summary>
        /// Start of the range
        /// </summary>
        public readonly int Start;

        /// <summary>
        /// Length of the range
        /// </summary>
        public readonly int End;

        /// <summary>
        /// Constructor
        /// </summary>
        public iRange(int start, int end) { Start = start; End = end; }

        #region Overrides recommended for value types

        public override int GetHashCode() { return Start ^ End; }
        public override bool Equals(object obj)
        {
            if (obj is iRange)
            {
                var r = (iRange)obj;
                return Start == r.Start && End == r.End;
            }
            return false;
        }
        public static bool operator ==(iRange range1, iRange range2)
        {
            return range1.Equals(range2);
        }
        public static bool operator !=(iRange range1, iRange range2)
        {
            return !range1.Equals(range2);
        }
        public override string ToString()
        {
            return string.Format("{0} -> {1}", Start, End);
        }

        #endregion
    }

    /// <summary>
    /// Encapsulates a version number
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PdfHeader
    {
        /// <summary>
        /// Major version number
        /// </summary>
        public readonly byte Major;

        /// <summary>
        /// Minor version number
        /// </summary>
        public readonly byte Minor;

        /// <summary>
        /// Offset into the stream to the PDF header
        /// </summary>
        internal readonly ushort Offset;

        /// <summary>
        /// If this version number is valid.
        /// </summary>
        /// <remarks>Valid for this libary</remarks>
        public bool IsValid { get { return Major != 0; } }

        /// <summary>
        /// For files with %PDF-, but no version information,
        /// minor is set to 255
        /// </summary>
        public bool IsUnknown { get { return Minor == 255; } }

        /// <summary>
        /// Inits a new version
        /// </summary>
        /// <param name="major">The major number</param>
        /// <param name="minor">The minor number</param>
        internal PdfHeader(byte major, byte minor, ushort offset) { Major = major; Minor = minor; Offset = offset; }

        /// <summary>
        /// For checking if a the PDf version is the
        /// given version or greater
        /// </summary>
        public bool IsAtLeast(int major, int minor)
        {
            if (major > Major) return false;
            if (major < Major) return true;
            return minor <= Minor;
        }

        /// <summary>
        /// For checking if a the PDf version is the
        /// given version or lesser
        /// </summary>
        public bool IsAtMost(int major, int minor)
        {
            if (major < Major) return false;
            if (major > Major) return true;
            return minor >= Minor;
        }

        #region Overrides recommended for value types

        public override int GetHashCode() { return Major << 8 | Minor; }
        public override bool Equals(object obj)
        {
            if (obj is PdfHeader)
            {
                var h = (PdfHeader)obj;
                return Major == h.Major && Minor == h.Minor;
            }
            return false;
        }
        public static bool operator ==(PdfHeader head1, PdfHeader head2)
        {
            return head1.Equals(head2);
        }
        public static bool operator !=(PdfHeader head1, PdfHeader head2)
        {
            return !head1.Equals(head2);
        }
        /// <summary>
        /// String representation
        /// </summary>
        /// <remarks>This code is in use</remarks>
        public override string ToString()
        {
            return string.Format("{0}.{1}", Major, Minor);
        }

        #endregion
    }

    /// <summary>
    /// This interface is implemented by the numeric PdfItems
    /// </summary>
    /// <remarks>
    /// This interface is for situations where one have to
    /// distinguish between "a number" and "null". 
    /// 
    /// Perhaps use "double?" instead
    /// </remarks>
    public interface INumber
    {
        /// <summary>
        /// Returns the number
        /// </summary>
        double GetReal();
    }

    /// <summary>
    /// Interface for any object that wish to expose it's stream's size
    /// </summary>
    public interface ISize
    {
        /// <summary>
        /// The compressed size of the stream
        /// </summary>
        int Compressed { get; }

        /// <summary>
        /// The uncompressed size of the stream
        /// </summary>
        int Uncompressed { get; }
    }

    /// <summary>
    /// Only used in special circumstances.
    /// </summary>
    /// <remarks>
    /// Intended to be use with enumerators creating
    /// invalid pdf objects. That way one don't need
    /// to check for exceptions but instead an "invalid"
    /// return value.
    /// </remarks>
    /*internal class InvalidPDF : PdfObject, IRef
    {
        #region Properties

        internal override PdfType Type { get { return PdfType.None; } }

        #endregion

        #region IRef

        /// <summary>
        /// This object is not intended to be saved
        /// </summary>
        SM IRef.DefSaveMode { get { return SM.Ignore; } }

        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference { get { return ((IRef)this).Reference != null; } }

        /// <summary>
        /// This object's reference.
        /// </summary>
        WritableReference IRef.Reference { get; set; }

        #endregion

        #region Required overrides

        internal override void Write(PdfWriter write)
        {
            throw new NotSupportedException();
        }

        #endregion
    }*/
}
