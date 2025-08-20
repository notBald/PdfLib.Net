using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Parameters for filters
    /// </summary>
    public abstract class PdfFilterParms : Elements
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Filter
        /// </summary>
        internal sealed override PdfType Type { get { return PdfType.FilterParams; } }

        #endregion

        #region Init

        protected PdfFilterParms(PdfDictionary dict)
            : base(dict) { }

        #endregion

        #region Create

        /// <summary>
        /// Creates a filterparam object for the type of
        /// filter supplied
        /// </summary>
        /// <param name="dict">Filter params catalog</param>
        /// <param name="filter">Filter to create params for</param>
        /// <returns>Filter params object for the filter</returns>
        internal static PdfFilterParms Create(PdfDictionary dict, PdfFilter filter)
        {
            if (filter is PdfFaxFilter)
                return new PdfFaxParms(dict);

            if (filter is PdfFlateFilter)
                return new FlateParams(dict);

            if (filter is PdfJBig2Filter)
                return new JBig2Params(dict);

            if (filter is PdfDCTFilter)
                return new DCTParams(dict);

            if (filter is PdfLZWFilter)
                return new FlateParams(dict);

            throw new NotSupportedException();
        }

        #endregion
    }

    /// <summary>
    /// An array of PdfFilterParams
    /// </summary>
    /// <remarks>
    /// Note that this class is assumed to be immutable
    /// 
    /// To change that assumtion, implement the IKRef interface
    /// </remarks>
    public class FilterParmsArray : TypeArray<PdfFilterParms>
    {
        #region Variables and properties

        /// <summary>
        /// Used as parameters for creating the
        /// FilterParams objects
        /// </summary>
        private readonly FilterArray _filters;

        /// <summary>
        /// Returns PdfType.FilterArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.FilterParmsArray; } }

        #endregion

        #region Init

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Filter items</param>
        public FilterParmsArray(FilterArray filters, PdfItem items)
            : base(items, PdfType.FilterParams)
        { _filters = filters; }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Filter items</param>
        public FilterParmsArray(PdfItem[] items, FilterArray filters)
            : base(items, PdfType.FilterParams)
        { _filters = filters; }

        /// <summary>
        /// Create array with one item.
        /// </summary>
        /// <param name="items">Filter items</param>
        public FilterParmsArray(PdfArray items, FilterArray filters)
            : base(items, PdfType.FilterParams)
        { _filters = filters; }

        /// <summary>
        /// Empty array
        /// </summary>
        public FilterParmsArray() : base() { _filters = new FilterArray(); }

        #endregion

        #region Overrides

        /// <summary>
        /// Content is optional, so it can be "null", 
        /// the plain contents item (must be a reference)
        /// or an array of contents streams.
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            //System.Diagnostics.Debug.Assert(false, "Untested code");
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
            if (store_mode == SM.Direct)
                Write(write);
            else
                _items.Write(write);
        }

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            //Note: Filters are only used as parameters. Should perhaps
            //      insure that they are loaded into memory, but other than
            //      that they are not used. 
            return new FilterParmsArray(array, _filters);
            //If the array must be copied, use this:
            //return new FilterParmsArray(data,(FilterArray) 
            //    _tracker.MakeCopy((PdfLib.Write.Internal.ICRef) _filters));
        }

        /// <summary>
        /// When creating a filter params object one need
        /// to know what kind of parameters the filter
        /// expects
        /// </summary>
        /// <param name="index">Filter index</param>
        /// <returns>The filter</returns>
        protected override PdfItem GetParam(int index)
        {
            if (_filters == null) return null;
            return _filters[index];
        }

        #endregion
    }
}
