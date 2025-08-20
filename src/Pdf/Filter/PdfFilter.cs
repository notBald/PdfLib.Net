using System;
using System.IO;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Util;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Filter
{
    /// <remarks>
    /// Implementors must put their full name in the ToString method. Used during
    /// some saving operations.
    /// </remarks>
    public abstract class PdfFilter : PdfObject
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Filter
        /// </summary>
        internal sealed override PdfType Type { get { return PdfType.Filter; } }

        /// <summary>
        /// The PDF name of this filter
        /// </summary>
        public abstract string Name { get; }

        #endregion

        /// <summary>
        /// Decodes the input data
        /// </summary>
        /// <remarks>
        /// Decode is expected to be either reentrant or
        /// use some sort of locking mechanism
        /// </remarks>
        public abstract byte[] Decode(byte[] data, PdfFilterParms fparams);

        /// <summary>
        /// Finds the end of the stream and returns the unfiltered data
        /// </summary>
        /// <param name="source">Stream to read from.</param>
        /// <param name="startpos">From where to start searching in the stream</param>
        /// <param name="fparams">Some filters require parameters to find the ending</param>
        /// <returns>The raw data or null if this filter can't find the end</returns>
        /// <remarks>Used for extracting inline images from a stream</remarks>
#if LONGPDF
        internal virtual byte[] FindEnd(Stream source, long startpos, PdfFilterParms fparams)
#else
        internal virtual byte[] FindEnd(Stream source, int startpos, PdfFilterParms fparams)
#endif
        {
            return null;
        }

        public abstract bool Equals(PdfFilter filter);

        #region Overrides

        /// <summary>
        /// Filters are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        internal sealed override void Write(PdfWriter write)
        {
            write.WriteName(Name);
        }

        #endregion

        #region Create

        /// <summary>
        /// Insures that all filters are the same. There
        /// is no real need for this, just experimenting
        /// with caching.
        /// </summary>
        private static WeakCache<string, PdfFilter> FilterCache = new WeakCache<string, PdfFilter>(8);

        /// <summary>
        /// Creates a filter with the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        internal static PdfFilter Create(string name)
        {
            var ret = FilterCache[name];
            if (ret != null) return ret;

            switch (name)
            {
                case "Fl":
                case "FlateDecode":
                    ret = new PdfFlateFilter();
                    break;

                case "DCT":
                case "DCTDecode":
                    ret = new PdfDCTFilter();
                    break;

                case "CCF":
                case "CCITTFaxDecode":
                    ret = new PdfFaxFilter();
                    break;

                case "LZW":
                case "LZWDecode":
                    ret = new PdfLZWFilter();
                    break;

                case "A85":
                case "ASCII85Decode":
                    ret = new Pdf85Filter();
                    break;

                case "AHx":
                case "ASCIIHexDecode":
                    ret = new PdfHexFilter();
                    break;

                case "JBIG2Decode":
                    ret = new PdfJBig2Filter();
                    break;

                case "RL":
                case "RunLengthDecode":
                    ret = new PdfRunLengthFilter();
                    break;

                case "JPXDecode":
                    ret = new PdfJPXFilter();
                    break;

                default:
                    throw new NotImplementedException(name);
            }

            FilterCache[name] = ret;
            return ret;
        }

        #endregion
    }

    /// <summary>
    /// An array of of PdfFilters
    /// </summary>
    public class FilterArray : TypeArray<PdfFilter>
    {
        #region Variables and properties

        /// <summary>
        /// Returns PdfType.FilterArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.FilterArray; } }

        internal bool IsBinary
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Whenever this filter array contains the deflate filter
        /// </summary>
        public bool HasDeflate
        {
            get
            {
               /* foreach (var filter in this)
                    if (filter is PdfFlateFilter)
                        return true;
                return false;//*/
                //Testing the first filter should be good enough.
                return _items.Length > 0 && this[0] is PdfFlateFilter;
            }
        }

        /// <summary>
        /// Whenever this filter array contains a J2K filter
        /// </summary>
        public bool HasJ2K
        {
            get
            {
                 foreach (var filter in this)
                     if (filter is PdfJPXFilter)
                         return true;
                 return false;
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Filter items</param>
        /// <remarks>This method creates an adoptable filter array</remarks>
        public FilterArray(PdfItem[] items)
            : base(new TemporaryArray(items), PdfType.Filter)
        { }

        /// <summary>
        /// Create array with multiple item.
        /// </summary>
        /// <param name="items">Filter items</param>
        public FilterArray(PdfArray item)
            : base(item, PdfType.Filter)
        { }

        /// <summary>
        /// Create array with one item.
        /// </summary>
        public FilterArray(PdfItem item)
            : base(item, PdfType.Filter)
        { }

        public FilterArray()
            : base(new PdfItem[0], PdfType.Filter)
        { }

        #endregion

        #region Required overrides

        /// <summary>
        /// Content is optional, so it can be "null", 
        /// the plain contents item (must be a reference)
        /// or an array of contents streams.
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
            return new FilterArray(array);
        }

        #endregion

        public bool Equals(FilterArray filters)
        {
            if (this._items.Length != filters._items.Length)
                return false;

            for (int c = 0; c < _items.Length; c++)
                if (!this[c].Equals(filters[c]))
                    return false;
            return true;
        }
    }
}
