using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Filter;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Abstract implementation of a writable stream. Implementors need
    /// to implement MakeCopy, the RawStream property and the length
    /// property.
    /// </summary>
    /// <remarks>
    /// Note that this class (and PdfStream) inherits from 
    /// Elements instead of StreamElm. StreamElm is in essence a
    /// quick hack to sepperate between dictionaries and streams,
    /// but that does not make sense for this class.
    /// </remarks>
    public abstract class NewStream : Elements, IWStream
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.Stream; } }
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        /// <summary>
        /// Length of compressed data
        /// </summary>
        public abstract int Length { get; }

        /// <summary>
        /// Filter for decoding the stream.
        /// 
        /// Optional. Either PdfName or PdfArray of PdfNames
        /// </summary>
        public FilterArray Filter
        {
            get
            {
                return (FilterArray)_elems.GetPdfType("Filter",
                                             PdfType.FilterArray,
                                             PdfType.Array,
                                             false,
                                             IntMsg.NoMessage,
                                             null);
            }
            private set
            {
                _elems.SetItem("Filter", value, false);
            }
        }

        /// <summary>
        /// Decode parameters for decoding the stream.
        /// 
        /// Optional. Either a parameter PdfDictionary or 
        /// PdfArray of parameter PdfDictionarys
        /// </summary>
        public FilterParmsArray DecodeParms
        {
            get
            {
                return (FilterParmsArray)_elems.GetPdfType("DecodeParms",
                    PdfType.FilterParmsArray, PdfType.Array, false, IntMsg.Filter, Filter);
            }
            private set
            {
                _elems.SetItem("DecodeParms", value, false);
            }
        }

        public byte[] DecodedStream
        {
            get
            {
                byte[] stream = RawStream;
                if (stream == null) return null;
                var filter = Filter;
                if (filter == null) return stream;
                var decodeparms = DecodeParms;

                if (decodeparms != null)
                {
                    for (int c = 0; c < filter.Length; c++)
                        stream = filter[c].Decode(stream, decodeparms[c]);
                }
                else
                {
                    #region Filter bench code
                    /*if (filter[c] is Pdf85Filter)
                        {
                            //bench
                            var org_data = stream;
                            var src_data = new byte[org_data.Length];
                            var a85 = (Pdf85Filter)filter[0];
                            System.Windows.MessageBox.Show("Now!", "Start");
                            var time = System.DateTime.Now;
                            for (int k = 0; k < 500000; k++)
                            {
                                System.Buffer.BlockCopy(org_data, 0, src_data, 0, org_data.Length);
                                var bytes = a85.Decode(src_data, null);
                            }
                            var time_end = System.DateTime.Now;
                            System.Windows.MessageBox.Show(string.Format("{0:0.0} ms", (time_end - time).TotalMilliseconds), "Time");
                        }//*/
                    #endregion
                    for (int c = 0; c < filter.Length; c++)
                        stream = filter[c].Decode(stream, null);
                }
                return stream;
            }
            set
            {
                if (_elems.Contains("Filter"))
                    throw new NotImplementedException("Compression");

                RawStream = value;
            }
        }

        /// <summary>
        /// Streams are intended to share it's dictionary with other objects.
        /// </summary>
        internal PdfDictionary Elements { get { return (PdfDictionary)_elems; } }

        /// <summary>
        /// Unfiltered raw stream data
        /// </summary>
        public abstract byte[] RawStream
        {
            get;
            set;
        }

        /// <summary>
        /// Decodes a stream.
        /// </summary>
        /// <param name="EOD">Set true if decoding was terminated due to EOD</param>
        /// <returns>The decoded data</returns>
        /// <remarks>
        /// Used to work around a issue with inline images and CCITT decoding
        /// </remarks>
        internal byte[] DecodeStream(out bool EOD)
        {
            EOD = false;

            byte[] stream = RawStream;
            if (stream == null) return null;
            var filters = Filter;
            if (filters == null) return stream;
            var decodeparms = DecodeParms;

            for (int c = 0; c < filters.Length; c++)
            {
                var filter = filters[c];

                if (filter is PdfFaxFilter)
                    stream = ((PdfFaxFilter)filter).Decode(stream, (decodeparms != null) ? decodeparms[c] : null, out EOD);
                else
                    stream = filter.Decode(stream, (decodeparms != null) ? decodeparms[c] : null);
            }

            return stream;
        }

        #endregion

        #region Init

        internal NewStream(FilterArray filters, FilterParmsArray dparams)
            : base(new TemporaryDictionary())
        {
            if (filters != null && filters.Length > 0)
            {
                _elems.SetItem("Filter", filters, true);

                if (dparams != null && dparams.Length > 0)
                    _elems.SetItem("DecodeParms", dparams, true);
            }
        }

        internal NewStream(FilterArray filters)
            : this(filters, null)
        {
            
        }

        internal NewStream(PdfDictionary dict)
            : base(dict)
        {

        }

        internal NewStream() : this(new TemporaryDictionary()) { }

        #endregion

        #region Decode

        /// <summary>
        /// Decodes data up to the given filter type
        /// </summary>
        /// <typeparam name="T">The filter type to decode up to</typeparam>
        /// <returns>Data not decoded by that or later filters</returns>
        public byte[] DecodeTo<T>()
            where T : PdfFilter
        {
            var stream = RawStream;
            if (stream == null) return null;
            var filters = Filter;
            if (filters == null) return stream;
            var decodeparms = DecodeParms;

            if (decodeparms != null)
            {
                for (int c = 0; c < filters.Length; c++)
                {
                    var filter = filters[c];
                    if (filter is T) break;
                    stream = filter.Decode(stream, decodeparms[c]);
                }
            }
            else
            {
                for (int c = 0; c < filters.Length; c++)
                {
                    var filter = filters[c];
                    if (filter is T) break;
                    stream = filter.Decode(stream, null);
                }
            }
            return stream;
        }

        public bool HasFilter<T>()
            where T : PdfFilter
        {
            var filters = Filter;
            if (filters != null)
            {
                foreach (var filter in filters)
                    if (filter is T)
                        return true;
            }
            return false;
        }

        public bool IsLossy
        {
            get
            {
                var filters = Filter;
                if (filters != null)
                {
                    foreach (var filter in filters)
                        if (filter is PdfJPXFilter || filter is PdfDCTFilter)
                            return true;
                }
                return false;
            }
        }

        #endregion

        #region Compress

        /// <summary>
        /// For compressing the data of a stream futher.
        /// </summary>
        /// <param name="filter">The new filter</param>
        /// <param name="param">The filter's parameters</param>
        /// <param name="compressed_data">
        /// Compressed RawStream data. Note, don't use "DecodedStream" as the source of the data.
        /// </param>
        /// <param name="reject_bigger">If the filtered data is bigger, should it be rejected</param>
        public void AddFilter(PdfFilter filter, PdfFilterParms param, byte[] compressed_data, bool reject_bigger)
        {
            if (reject_bigger && compressed_data.Length > RawStream.Length)
                return;

            _elems.SetInt("Length", compressed_data.Length);
            RawStream = compressed_data;

            var my_filters = Filter;
            if (my_filters == null)
            {
                var fa = new FilterArray(filter);
                Filter = fa;
                DecodeParms = new FilterParmsArray(new PdfItem[] { param }, fa);
            }
            else
            {
                //A bit ugly, but adding filters to a "sealed array" is the way it's
                //implemented in the Compress function. It's not totally safe to do,
                //but there's currently no way to add an item to an array whenever it's
                //writable or not. So, yeah, not perfect but will have to do for now.

                var ia = (PdfItem[])((ICRef)my_filters).GetChildren();
                lock (ia)
                {
                    var na = new PdfItem[ia.Length + 1];
                    Array.Copy(ia, 0, na, 1, ia.Length);
                    na[0] = filter;
                    my_filters = new FilterArray(na);
                }
                Filter = my_filters;

                var decode = DecodeParms;
                if (decode != null)
                {
                    ia = (PdfItem[])((ICRef)decode).GetChildren();
                    lock (ia)
                    {
                        var na = new PdfItem[ia.Length + 1];
                        Array.Copy(ia, 0, na, 1, ia.Length);
                        na[0] = (param == null) ? PdfNull.Value : (PdfItem)param;
                        DecodeParms = new FilterParmsArray(na, my_filters);
                    }
                }
                else if (param != null)
                    DecodeParms = new FilterParmsArray(new PdfItem[] { param }, my_filters);
            }
        }

        void ICStream.Compress(List<FilterArray> filters, CompressionMode mode)
        {
            var my_filters = Filter;
            switch (mode)
            {
                #region Normal

                case CompressionMode.Normal:
                    if (my_filters == null)
                    {
                        var raw_data = RawStream;
                        var data = PdfFlateFilter.Encode(raw_data);
                        if (raw_data.Length - PdfFlateFilter.SKIP <= data.Length)
                            return;

                        _elems.SetInt("Length", data.Length);
                        var fa = new FilterArray(new PdfItem[] { new PdfFlateFilter() });

                        //Not bothering to reuse filter arrays with just
                        //one entery. 
                        _elems.SetItem("Filter", fa, false);

                        RawStream = data;
                    }
                    break;

                #endregion

                #region Maximum

                case CompressionMode.Maximum:
                    if (my_filters == null)
                        goto case CompressionMode.Normal;
                    else
                    {
                        if (my_filters.HasDeflate || my_filters.HasJ2K)
                            return;

                        var raw_data = RawStream;
                        var data = PdfFlateFilter.Encode(raw_data);
                        if (raw_data.Length - PdfFlateFilter.SKIP <= data.Length)
                            return;

                        _elems.SetInt("Length", data.Length);

                        var ia = (PdfItem[])((ICRef)my_filters).GetChildren();
                        lock (ia)
                        {
                            var na = new PdfItem[ia.Length + 1];
                            Array.Copy(ia, 0, na, 1, ia.Length);
                            na[0] = new PdfFlateFilter();
                            my_filters = new FilterArray(na);
                        }

                        //For this to make sense one must only do this when
                        //there's about 5 references to the filter array. I.e.
                        //set "SaveMode" to direct, and flip it to compressed
                        //if there's more than five references.
                        //my_filters = GetFilter(filters, my_filters);

                        _elems.SetItem("Filter", my_filters, true);

                        var decode = DecodeParms;
                        if (decode != null)
                        {
                            ia = (PdfItem[])((ICRef)decode).GetChildren();
                            lock (ia)
                            {
                                var na = new PdfItem[ia.Length + 1];
                                Array.Copy(ia, 0, na, 1, ia.Length);
                                na[0] = PdfNull.Value;
                                _elems.SetItem("DecodeParms", new FilterParmsArray(na, my_filters), false);
                            }
                        }

                        //Updates the data
                        RawStream = data;
                    }
                    break;

                #endregion

                #region Uncompressed

                case CompressionMode.Uncompressed:
                    if (my_filters != null && !my_filters.HasJ2K)
                    {
                        var raw_data = DecodedStream;
                        _elems.Remove("Filter");
                        _elems.Remove("Decode");
                        RawStream = raw_data;
                    }
                    break;

                #endregion
            }
        }

        PdfDictionary ICStream.Elements { get { return _elems; } }

        #endregion

        #region Write methods

        internal override void Write(PdfWriter write)
        {
            throw new PdfInternalException("Streams can't be saved as a direct object");
        }

        internal override void Write(PdfWriter write, SM store_mode)
        {
            if (store_mode != SM.Indirect)
                throw new PdfInternalException("Streams must be saved as indirect");

            write.WriteStream(_elems, RawStream);
        }

        void IWStream.Write(PdfWriter write)
        {
            write.WriteStream(_elems, RawStream);
        }

        #endregion

        #region Required overrides

        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            return PdfStream.ToType(this, type, msg, obj);
        }

        internal sealed override bool Equivalent(object obj)
        {
            return (obj is IStream) ? PdfStream.Equals(this, (IStream)obj) : false;
        }

        IWStream IWStream.MakeCopy(PdfDictionary elems)
        {
            return (IWStream) MakeCopy(elems);
        }

        void IWStream.SetDictionary(PdfDictionary elems)
        {
            _elems = elems;
        }

        /// <summary>
        /// Copies the stream's data into memory
        /// </summary>
        void ICStream.LoadResources()
        {
            LoadResources();
        }

        #endregion
    }
}