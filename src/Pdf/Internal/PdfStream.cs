using System;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Function;
using PdfLib.Pdf.Filter;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Read;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// A dictionary with a stream
    /// </summary>
    public sealed class PdfStream : Elements, IWStream
    {
        #region Variables and proeprties

        /// <summary>
        /// PdfType.Dictionary
        /// </summary>
        /// <remarks>
        /// PdfStreams are one of the few objects that gets created
        /// automatically. They are also "ordinary" dictionaries.
        /// </remarks>
        internal override PdfType Type { get { return PdfType.Dictionary; } }

        /// <summary>
        /// All streams must be indirect
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        /// <summary>
        /// The data source
        /// </summary>
        private ISource _source;

        /// <summary>
        /// The location and size of the stream
        /// </summary>
        private rRange _stream;

        /// <summary>
        /// Length of the stream
        /// 
        /// Required
        /// </summary>
        public int Length { get { return _elems.GetUIntEx("Length"); } }

        /// <summary>
        /// Filter for decoding the stream.
        /// 
        /// Optional. Either PdfName or PdfArray of PdfNames
        /// </summary>
        public FilterArray Filter 
        { 
            get 
            { 
                return (FilterArray) _elems.GetPdfType("Filter", 
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
        /// If this stream has a lossy filter. (Either DCT or J2K)
        /// </summary>
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
                return (FilterParmsArray) _elems.GetPdfType("DecodeParms", 
                    PdfType.FilterParmsArray, PdfType.Array, false, IntMsg.Filter, Filter); 
            }
            private set
            {
                _elems.SetItem("DecodeParms", value, false);
            }
        }

        /// <summary>
        /// Length of decoded filter data. 
        /// This is only to be used as a hint
        /// 
        /// Optional. Positive integer
        /// </summary>
        public PdfInt DecodeLength { get { return _elems.GetUIntObj("DL"); } }

        /// <summary>
        /// Gets the raw unfiltered stream
        /// </summary>
        public byte[] RawStream
        {
            get
            {
                var ret = new byte[_stream.Length - _source.Padding];
                lock (_source.LockOn)
                {
                    if (_source.Read(ret, _stream.Start) != ret.Length)
                        throw new PdfReadException(ErrSource.Stream, PdfType.None, ErrCode.UnexpectedEOD);
                }
                return ret;
            }
        }

        /// <summary>
        /// Gets the stream after filtering
        /// </summary>
        public byte[] DecodedStream
        {
            get
            {
                var stream = RawStream;
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
        }

        /// <summary>
        /// Streams are intended to share it's dictionary with other objects.
        /// </summary>
        internal PdfDictionary Elements { get { return (PdfDictionary)_elems; } }

        #endregion

        #region Init

        /// <summary>
        /// Creates a new PdfStream
        /// </summary>
        /// <param name="cat">Catalog</param>
        /// <param name="owner">Owner file</param>
        /// <param name="stream">A range for the stream</param>
        /// <remarks>
        /// Only used by the parser.
        /// </remarks>
        internal PdfStream(Catalog cat, ISource owner, rRange stream)
            : base(cat)
        {
            _source = owner;
            _stream = stream;
            //if (!cat.ContainsKey("Length"))
            //    throw new PdfReadException(SR.CorruptStream);
        }

        /// <remarks>
        /// Only used by the parser.
        /// </remarks>
        internal PdfStream(Catalog cat, ISource owner, rRange stream, ResTracker tracker)
            : base(new WritableDictionary(cat, tracker))
        {
            _source = owner;
            _stream = stream;
            //if (!cat.ContainsKey("Length"))
            //    throw new PdfReadException(SR.CorruptStream);
        }

        /// <summary>
        /// Creates a new PdfStream
        /// </summary>
        internal PdfStream(PdfDictionary dict, ISource owner, rRange stream)
            : base(dict)
        {
            _source = owner;
            _stream = stream;
            //if (!cat.ContainsKey("Length"))
            //    throw new PdfReadException(SR.CorruptStream);
        }

        /// <summary>
        /// Copies the stream's data into memory
        /// </summary>
        protected override void LoadResources()
        {
            LoadStream();
        }

        /// <summary>
        /// Copies the stream's data into memory
        /// </summary>
        void ICStream.LoadResources()
        {
            LoadStream();
        }

        /// <summary>
        /// Moves the stream's data into memory
        /// </summary>
        internal void LoadStream()
        {
            if (!_source.IsExternal) return;
            int padd = _source.Padding;
            _source = new ByteSource(this.RawStream);
            _stream = new rRange(0, _stream.Length - padd);
        }

        #endregion

        #region Decode

        /// <summary>
        /// Decodes data up to the given filter type
        /// </summary>
        /// <typeparam name="T">The filter type to decode up to</typeparam>
        /// <returns>Data not decoded by that or later filters</returns>
        public byte[] DecodeTo<T>()
            where T:PdfFilter
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

        /// <summary>
        /// Decodes data encoded with the given filter array and decode params
        /// </summary>
        internal static byte[] DecodStream(byte[] stream, FilterArray filter, FilterParmsArray decodeparms)
        {
            if (stream == null) return null;
            if (filter == null) return stream;

            if (decodeparms != null)
            {
                for (int c = 0; c < filter.Length; c++)
                    stream = filter[c].Decode(stream, decodeparms[c]);
            }
            else
            {
                for (int c = 0; c < filter.Length; c++)
                    stream = filter[c].Decode(stream, null);
            }
            return stream;
        }

        #endregion

        #region Compression

        PdfDictionary ICStream.Elements { get { return _elems; } }

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
            _source = new ByteSource(compressed_data);

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
            switch(mode)
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

                        //Updates the data by switching out the source
                        _source = new ByteSource(data);
                        _stream = new rRange(0, data.Length);
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
                        var na = new PdfItem[ia.Length + 1];
                        Array.Copy(ia, 0, na, 1, ia.Length);
                        na[0] = new PdfFlateFilter();
                        my_filters = new FilterArray(na);

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
                            na = new PdfItem[ia.Length + 1];
                            Array.Copy(ia, 0, na, 1, ia.Length);
                            na[0] = PdfNull.Value;
                            _elems.SetItem("DecodeParms", new FilterParmsArray(na, my_filters), false);
                        }

                        //Updates the data by switching out the source
                        _source = new ByteSource(data);
                        _stream = new rRange(0, data.Length);
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
                        _elems.SetInt("Length", raw_data.Length);
                        _source = new ByteSource(raw_data);
                        _stream = new rRange(0, raw_data.Length);
                    }
                    break;

                #endregion
            }
        }

        /// <summary>
        /// Not in use as of right now, need some more work
        /// </summary>
        internal static FilterArray GetFilter(List<FilterArray> filters, FilterArray filter)
        {
            foreach (var flt in filters)
                if (flt.Equals(filter))
                    return flt;
            filters.Add(filter);
            return filter;
        }

        #endregion

        #region Overrides

        internal override bool Equivalent(object obj)
        {
            return (obj is IStream) ? PdfStream.Equals(this, (IStream) obj) : false;
        }

        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            return ToType(this, type, msg, obj);
        }

        internal static PdfObject ToType(IWStream stream, PdfType type, IntMsg msg, object obj)
        {
            switch (type)
            {
                case PdfType.XObject:
                    return PdfXObject.Create(stream, msg, obj);

                case PdfType.Content:
                    return new PdfContent(stream);

                //case PdfType.ContentArray:
                //    return new PdfContentArray(this, obj as ResTracker);

                case PdfType.XRefStream:
                    return new XRefStream(stream);

                case PdfType.ObjStream:
                    //Problem: Streams can be compressed, and thus lose their owner.
                    //
                    //The solution is to pass the owner along the totype system, my
                    //only worry is a situation where a objstream expects a different 
                    //owner. But to my knowlege that can only happen if the object 
                    //stream is somehow moved to a different document, and that isn't
                    //possible (nor does it make sense to allow that). 
                    //Debug.Assert(msg == IntMsg.Owner && (!(_source is PdfFile) || ReferenceEquals(obj, _source)));
                    return new ObjStream((PdfFile)obj, stream);

                case PdfType.Pattern:
                    return new PdfTilingPattern(stream);

                case PdfType.FontFile:
                    return new Pdf.Font.PdfFontFile(stream);

                case PdfType.FontFile2:
                    return new Pdf.Font.PdfFontFile2(stream);

                case PdfType.FontFile3:
                    return new Pdf.Font.PdfFontFile3(stream);

                case PdfType.Function:
                    return PdfFunction.Create(stream); 

                case PdfType.FunctionArray:
                    if (msg == IntMsg.Special)
                        return new PdfFunctionArray(PdfFunction.Create(stream), true);
                    goto default;

                case PdfType.Shading:
                    return PdfShading.Create(stream);

                case PdfType.Cmap:
                    return Font.PdfCmap.Create(stream);

                case PdfType.EmbeddedFile:
                    return new PdfEmbeddedFile(stream);

                //Don't use the ToType system for streams. Will trigger an Debug.Assert(..)
                //case PdfType.Stream:
                //    return stream;

                default:
                    throw new NotSupportedException("Can't change this object into a diffferent type");
            }
        }

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

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfStream(elems, _source, _stream);
        }
        IWStream IWStream.MakeCopy(PdfDictionary elems)
        {
            return new PdfStream(elems, _source, _stream);
        }
        void IWStream.SetDictionary(PdfDictionary elems)
        {
            _elems = elems;
        }

        public override string ToString()
        {
            return string.Format("{0} Stream", base.ToString());
        }

        #endregion

        #region Debug aid

        /// <summary>
        /// Makes a hex dump of the unfiltered stream
        /// </summary>
        internal string HexDump()
        {
            return System.Text.Encoding.ASCII.GetString(PdfHexFilter.WhiteEncode(DecodedStream));
        }

        /// <summary>
        /// Makes a formated hex dump of the unfiltered stream
        /// </summary>
        internal string HexDump(int[] fields)
        {
            return System.Text.Encoding.ASCII.GetString(PdfHexFilter.FormatEncode(DecodedStream, fields, false));
        }

        #endregion

        #region Size

        /// <summary>
        /// Size metrics of this stream
        /// </summary>
        public ISize Size { get { return new MySize(this); } }

        class MySize : ISize
        {
            readonly PdfStream _me;
            public MySize(PdfStream me) { _me = me; }
            public int Compressed { get { return _me.Length; } }
            public int Uncompressed { get { return _me.DecodedStream.Length; } }
        }

        #endregion

        #region ISource

        public class ByteSource : ISource
        {
            public bool IsExternal { get { return false; } }
            public object LockOn { get { return this; } }
            int ISource.Padding => 0;
            readonly byte[] _data;
            public ByteSource(byte[] data) { _data = data; }
#if LONGPDF
            public int Read(byte[] buffer, long offset)
#else
            public int Read(byte[] buffer, int offset)
#endif
            {
                Buffer.BlockCopy(_data, (int) offset, buffer, 0, buffer.Length);
                return buffer.Length;
            }
        }

        #endregion

        #region Utility

        public static void LoadStream(IStream stream)
        {
            ((IWStream)stream).LoadResources();
        }

        public static bool Equals(IStream stream1, IStream stream2)
        {
            FilterArray filt1 = stream1.Filter, filt2 = stream2.Filter;
            if (filt1 != null && filt2 != null && filt1.Equals(filt2) || filt1 == filt2)
                //In theory, a file with the same filters and the same raw data could be different, extremly unlikely though
                return Util.ArrayHelper.ArraysEqual<byte>(stream1.RawStream, stream2.RawStream);
            else
                return Util.ArrayHelper.ArraysEqual<byte>(stream1.DecodedStream, stream2.DecodedStream);
        }

        #endregion
    }

    /// <summary>
    /// Interface all streams must implement
    /// </summary>
    public interface IStream
    {
        /// <summary>
        /// If this stream has lossy encoding (Jpeg or J2K)
        /// </summary>
        bool IsLossy { get; }

        byte[] DecodedStream { get; }
        byte[] RawStream { get; }
        FilterArray Filter { get; }
        FilterParmsArray DecodeParms { get; }
        int Length { get; }

        /// <summary>
        /// Decodes data up to the given filter type
        /// </summary>
        /// <typeparam name="T">The filter type to decode up to</typeparam>
        /// <returns>Data not decoded by that or later filters</returns>
        byte[] DecodeTo<T>() where T : PdfFilter;

        /// <summary>
        /// Check if a filter is used on this stream
        /// </summary>
        /// <typeparam name="T">The filter to check for</typeparam>
        /// <returns>True, if the filter is present</returns>
        bool HasFilter<T>() where T : PdfFilter;

        /// <summary>
        /// For compressing the data of a stream futher.
        /// </summary>
        /// <param name="filter">The new filter</param>
        /// <param name="param">The filter's parameters</param>
        /// <param name="compressed_data">
        /// Compressed RawStream data. Note, don't use "DecodedStream" as the source of the data.
        /// </param>
        /// <param name="reject_bigger">If the filtered data is bigger, should it be rejected</param>
        void AddFilter(PdfFilter filter, PdfFilterParms param, byte[] compressed_data, bool reject_bigger);
    }

    /// <summary>
    /// Interface for writable streams
    /// </summary>
    /// <remarks>
    /// Stream implementers need methods that I do not wish exposed
    /// publically. All streams are expected to be IWStreams, and
    /// IStream objects are blindly cast into IWStream internally.
    /// </remarks>
    internal interface IWStream : IRef, IStream, ICStream
    {
        /// <summary>
        /// Writes the stream to disk
        /// </summary>
        /// <param name="write"></param>
        void Write(PdfWriter write);

        /// <summary>
        /// Makes a clone of the stream object
        /// </summary>
        /// <param name="elems">
        /// A copy of the dictionary. Needed for
        /// when "write" is called.
        /// </param>
        IWStream MakeCopy(PdfDictionary elems);

        /// <summary>
        /// This method is only for use in conjuction with the "DictChange"
        /// method, used when adopting objects that deriver from Elements
        /// </summary>
        /// <remarks>
        /// This is ugly, think of a nicer way to go about this.
        /// 
        /// The problem is that Elements have a reference to the dictionary.
        /// When they are adopted they switch out this dictionary by one
        /// created by the tracker.
        /// 
        /// Elements implementers that need streams hold a reference to
        /// a stream object, who again also holds a reference to the 
        /// dictionary. The "stream" elements then override the "Write" 
        /// method of Elemens and call Write on the Stream instead when, 
        /// saving, and the IWStream then proceed to write out its 
        /// dictionary and stream data.
        /// 
        /// Because of this one must update the dictionary in the IWStream,
        /// also it's dumb to hold on to a discarded dictionary anyway so
        /// switching it out is the right thing to do.
        /// 
        /// Even so, don't like this.
        /// 
        /// Solutions
        ///  -StreamDictionary
        ///   I don't like this solutions as I prefere having only three
        ///   ditionary implementations. Then I can test for Sealed,Temp
        ///   Writable and go on as usual. Nor do I want these three sub
        ///   classed.
        ///  -Override the adopt method. This is in essence no different
        ///   than the current solution.
        ///  -Take PdfFunction (the sinners) of Elements. Say, have it 
        ///   implement everything itself. Not liking this idea at all. 
        ///   More code to mantain, already have that StreamElements puck.
        ///  -More subclassing. I already have more than enough.
        ///  -Another interface to address this. Will just complicate the
        ///   adoption code to remove one wart.
        ///   
        /// In any case the IWStream interface isn't public, so as long
        /// as I don't start abusing this method things will be fine.
        /// 
        /// Note that IWStream implementors can't ignore this method,
        /// as they must use the right dictionary when saving.
        /// </remarks>
        void SetDictionary(PdfDictionary dict);

        // /// <summary>
        // /// Loads the stream into memory
        // /// </summary>
        // void LoadResources();
    }

    /// <summary>
    /// Interface contains stream
    /// </summary>
    internal interface ICStream
    {
        /// <summary>
        /// Tells the stream to compress itself
        /// </summary>
        /// <param name="filters">
        /// A list of mutable filter arrays, which can
        /// be used instead of ones own. 
        /// </param>
        /// <remarks>
        /// Implementors can safly ignore this method, non IKRef
        /// objects must return null
        /// </remarks>
        void Compress(List<FilterArray> filters, CompressionMode mode);

        /// <summary>
        /// Streams are intended to share it's dictionary with other objects.
        /// </summary>
        PdfDictionary Elements { get; }

        /// <summary>
        /// Loads the stream into memory
        /// </summary>
        void LoadResources();
    }

    /// <summary>
    /// Interface for classes that act as a datasource for pdf stream objects
    /// </summary>
    internal interface ISource
    {
        /// <summary>
        /// Whenever this source stores it data externaly
        /// </summary>
        bool IsExternal { get; }

        /// <summary>
        /// Object used to lock on when reading from this source. 
        /// </summary>
        object LockOn { get; }

        /// <summary>
        /// AES encryption stores data in the stream itself, which means
        /// the length of the stream is effectivly padded. This padding
        /// must be taken into account when creating buffers.
        /// </summary>
        int Padding { get; }

        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset from the beginning of the stream</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        int Read(byte[] buffer, long offset);
#else        
        int Read(byte[] buffer, int offset);
#endif
    }
}
