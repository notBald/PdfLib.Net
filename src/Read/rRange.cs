using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Read
{
    /// <summary>
    /// Describes a range.
    /// </summary>
    /// <remarks>
    /// Used to describe ranges in the PDF file.
    /// </remarks>
    public struct rRange
    {
        /// <summary>
        /// Start of the range
        /// </summary>
#if LONGPDF
        public readonly long Start;
#else
        public readonly int Start;
#endif

        /// <summary>
        /// Length of the range
        /// </summary>
        /// <remarks>
        /// There's no need to support "long" ranges. The libary can't handle data that wont fit into an array.
        /// </remarks>
        public readonly int Length;

        /// <summary>
        /// Constructor
        /// </summary>
#if LONGPDF
        public rRange(long start, int length) { Start = start; Length = length; }
#else
        public rRange(int start, int length) { Start = start; Length = length; }
#endif

        #region Overrides recommended for value types

        public override int GetHashCode() { return ((int) Start) ^ Length; }
        public override bool Equals(object obj)
        {
            if (obj is rRange)
            {
                var r = (rRange)obj;
                return Start == r.Start && Length == r.Length;
            }
            return false;
        }
        public static bool operator ==(rRange range1, rRange range2)
        {
            return range1.Equals(range2);
        }
        public static bool operator !=(rRange range1, rRange range2)
        {
            return !range1.Equals(range2);
        }
        public override string ToString()
        {
            return string.Format("rRange: {0} - {1}", Start, Length);
        }

        #endregion
    }
}
