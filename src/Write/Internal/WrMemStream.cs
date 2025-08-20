using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Filter;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Implements a writable stream that uses either a MemoryStream or
    /// a System.Stream as its backend.
    /// 
    /// For raw byte arrays, use WritableStream. 
    /// </summary>
    /// <remarks>
    /// The purpose of this class is primarily to add images to the
    /// document as streams. (Without reading them into memory)
    /// </remarks>
    public sealed class WrMemStream : NewStream
    {
        #region Variables and properties

        /// <summary>
        /// Stream containing relevant data.
        /// </summary>
        Stream _stream;

        /// <summary>
        /// The location and size of the stream
        /// </summary>
        private Range _range;

        /// <summary>
        /// Length of compressed data
        /// </summary>
        public override int Length { get { return _range.Length; } }

        public override byte[] RawStream
        {
            get
            {
                lock (_stream)
                {
                    var buff = new byte[_range.Length];
                    _stream.Position = _range.Start;
                    for (int read = 0; read < _range.Length;)
                        read += _stream.Read(buff, 0, _range.Length);
                    return buff;
                }
            }
            set
            {
                var str = _stream;
                lock (str)
                {
                    _stream.Dispose();
                    _stream = new MemoryStream(value);
                    _range = new Range(0, value.Length);
                    _elems.SetInt("Length", _range.Length);
                }
            }
        }

        #endregion

        #region Init

        private WrMemStream(PdfDictionary dict, Stream stream, Range range)
            : base(dict)
        {
            _range = range;
            _stream = stream;
            _elems.SetInt("Length", _range.Length);
        }

        internal WrMemStream(PdfDictionary dict, Stream stream)
            : this(dict, stream, new Range(0, (int) stream.Length))
        {
        }

        public WrMemStream(Stream stream, Range range, FilterArray filters,  FilterParmsArray fparams)
            : base(filters, fparams)
        {
            _range = range;
            _stream = stream;
            _elems.SetInt("Length", _range.Length);
        }

        public WrMemStream(Stream stream, Range range, FilterArray filters)
            : base(filters)
        {
            _range = range;
            _stream = stream;
            _elems.SetInt("Length", _range.Length);
        }

        public WrMemStream(Stream stream, FilterArray filters)
            : this(stream, new Range(0, (int) stream.Length), filters)
        { }

        public WrMemStream(Stream stream)
            : this(stream, new Range(0, (int)stream.Length), null)
        { }

        #endregion

        #region Required overrides

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new WrMemStream(elems, _stream, _range);
        }

        /// <summary>
        /// Makes sure everything is in memory
        /// </summary>
        protected override void LoadResources()
        {
            RawStream = RawStream;
        }

        #endregion
    }
}
