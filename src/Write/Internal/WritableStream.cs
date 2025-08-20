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
    /// Implements a stream that can be written, as in the stream itself can be written
    /// </summary>
    /// <remarks>
    /// Note that this class (and PdfStream) inherits from 
    /// Elements instead of StreamElm. StreamElm is in essence a
    /// quick hack to sepperate between dictionaries and streams,
    /// but that does not make sense for this class.
    public sealed class WritableStream : NewStream
    {
        #region Variables and properties

        private byte[] _data;

        /// <summary>
        /// Length of compressed data
        /// </summary>
        public override int Length { get { return _data.Length; } }

        /// <summary>
        /// Unfiltered raw stream data
        /// </summary>
        public override byte[] RawStream
        {
            get
            {
                return _data;
            }
            set
            {
                _data = value;
                _elems.SetInt("Length", _data.Length);
            }
        }

        #endregion

        #region Init

        internal static WritableStream SearchForEnd(int size, SealedDictionary dict, Catalog cat, Read.Lexer lex)
        {
            var ws = new WritableStream(null, dict, false);
            var filters = ws.Filter;
            if (filters == null)
            {
                //If there's no filter than finding the end is trivial.
                ws._data = lex.ReadRaw(size);
                cat.Add("Length", new PdfInt(ws._data.Length));
                return ws;
            }
            var decodeparms = ws.DecodeParms;

            //Some filters supports searching for the ending
            var hold = lex.Position;
            var data = filters[0].FindEnd(lex.ContentsStream, hold, (decodeparms != null) ? decodeparms[0] : null);
            if (data != null)
            {
                cat.Add("Length", new PdfInt(data.Length));
                ws._data = data;
                lex.Position = hold + data.Length;

                //Returns a writable stream with the compressed data
                return ws;
            }

            //Gives up. It is possible to set up a stream chain and then decoding data up to
            //size, but few filters supports streaming.
            return null;
        }

        public WritableStream(byte[] rawdata)
            : this(rawdata, new TemporaryDictionary())
        { }

        public WritableStream(byte[] rawdata, FilterArray filters)
            : this(rawdata, new TemporaryDictionary())
        {
            if (filters != null && filters.Length > 0)
            {
                _elems.SetItem("Filter", filters, true);
            }
        }

        public WritableStream(byte[] rawdata, PdfFilter filter, PdfFilterParms parms)
            : this(rawdata, new FilterArray(filter), parms)
        { }

        private WritableStream(byte[] rawdata, FilterArray filter, PdfFilterParms parms)
            : this(rawdata, filter, parms != null ? new FilterParmsArray(new PdfItem[] { parms }, filter) : null)
        { }

        //Internal do to FilterParmsArray issue, see todo comment
        internal WritableStream(byte[] rawdata, FilterArray filters, FilterParmsArray parms)
            : this(rawdata, new TemporaryDictionary())
        {
            if (filters != null && filters.Length > 0)
            {
                _elems.SetItem("Filter", filters, true);

                if (parms != null && parms.Length > 0)
                {
                    //Todo: Check if parms have a ref to "filters", as that
                    //is required to be set. At least before making this method public)
                    _elems.SetItem("DecodeParms", parms, true);
                }
            }
        }

        /// <summary>
        /// Creates a new Writable Stream
        /// </summary>
        /// <param name="bytes">Bytes in the stream</param>
        /// <param name="dict">The owner's own dictionary</param>
        internal WritableStream(byte[] bytes, PdfDictionary dict)
            : base(dict)
        {
            _data = bytes;
            _elems.SetInt("Length", _data.Length);
        }

        /// <summary>
        /// For creating a Writable Stream that can't actually be written
        /// </summary>
        /// <param name="bytes">Bytes in the stream</param>
        /// <param name="dict">The owner's own dictionary</param>
        /// <param name="set_length">The length of the binary data will not be updated</param>
        internal WritableStream(byte[] bytes, PdfDictionary dict, bool set_length)
            : base(dict)
        {
            _data = bytes;
            if (set_length)
                _elems.SetInt("Length", _data.Length);
        }

        internal WritableStream(PdfDictionary dict)
            : base(dict)
        {
            _data = new byte[0];
            _elems.SetInt("Length", 0);
        }

        internal WritableStream() : this(new TemporaryDictionary()) { }

        #endregion

        #region Required overrides

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new WritableStream(_data, elems);
        }

        #endregion
    }

    #region Old code

    /*
    internal class WritableStream : Elements, IWStream
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.Stream; } }
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        private byte[] _data;

        /// <summary>
        /// Length of compressed data
        /// </summary>
        public int Length { get { return _data.Length; } }

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
    /*
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

_data = value;
_elems.SetInt("Length", _data.Length);
}
}

/// <summary>
/// Streams are intended to share it's dictionary with other objects.
/// </summary>
internal PdfDictionary Elements { get { return (PdfDictionary)_elems; } }

/// <summary>
/// Unfiltered raw stream data
/// </summary>
public byte[] RawStream
{
get
{
return _data;
}
set
{
_data = value;
_elems.SetInt("Length", _data.Length);
}
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
stream = ((PdfFaxFilter) filter).Decode(stream, (decodeparms != null) ? decodeparms[c] : null, out EOD);
else
stream = filter.Decode(stream, (decodeparms != null) ? decodeparms[c] : null);
}

return stream;
}

#endregion

#region Init

internal static WritableStream SearchForEnd(int size, SealedDictionary dict, Catalog cat, Read.Lexer lex)
{
var ws = new WritableStream(null, dict, false);
var filters = ws.Filter;
if (filters == null)
{
//If there's no filter than finding the end is trivial.
ws._data = lex.ReadRaw(size);
cat.Add("Length", new PdfInt(ws._data.Length));
return ws;
}
var decodeparms = ws.DecodeParms;

//Some filters supports searching for the ending
var hold = lex.Position;
var data = filters[0].FindEnd(lex.ContentsStream, hold, (decodeparms != null) ? decodeparms[0] : null);
if (data != null)
{
cat.Add("Length", new PdfInt(data.Length));
ws._data = data;
lex.Position = hold + data.Length;

//Returns a writable stream with the compressed data
return ws;
}

//Gives up. It is possible to set up a stream chain and then decoding data up to
//size, but few filters supports streaming.
return null;
}

public WritableStream(byte[] rawdata)
: this(rawdata, new TemporaryDictionary())
{ }

public WritableStream(byte[] rawdata, FilterArray filters)
: this(rawdata, new TemporaryDictionary())
{
if (filters.Length > 0)
{
_elems.SetItem("Filter", filters, true);
}
}

/// <summary>
/// Creates a new Writable Stream
/// </summary>
/// <param name="bytes">Bytes in the stream</param>
/// <param name="dict">The owner's own dictionary</param>
internal WritableStream(byte[] bytes, PdfDictionary dict)
: base(dict)
{
_data = bytes;
_elems.SetInt("Length", _data.Length);
}

/// <summary>
/// For creating a Writable Stream that can't actually be written
/// </summary>
/// <param name="bytes">Bytes in the stream</param>
/// <param name="dict">The owner's own dictionary</param>
/// <param name="set_length">The length of the binary data will not be updated</param>
internal WritableStream(byte[] bytes, PdfDictionary dict, bool set_length)
: base(dict)
{
_data = bytes;
if (set_length)
_elems.SetInt("Length", _data.Length);
}

internal WritableStream(PdfDictionary dict)
: base(dict)
{
_data = new byte[0];
_elems.SetInt("Length", 0);
}

internal WritableStream() : this(new TemporaryDictionary()) { }

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

#endregion

#region Compress

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

_data = data;
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
_data = data;
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
_data = raw_data;
}
break;

#endregion
}
}

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

write.WriteStream(_elems, _data);
}

void IWStream.Write(PdfWriter write)
{
write.WriteStream(_elems, _data);
}

#endregion

#region Required overrides

/// <summary>
/// For moving the element to a different document.
/// </summary>
protected override Elements MakeCopy(PdfDictionary elems)
{
return new WritableStream(_data, elems);
}
IWStream IWStream.MakeCopy(PdfDictionary elems)
{
return new WritableStream(_data, elems);
}
void IWStream.SetDictionary(PdfDictionary elems)
{
_elems = elems;
}

#endregion
} 
*/

    #endregion
}
