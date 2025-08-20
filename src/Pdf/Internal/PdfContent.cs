#define CACHE_CONTENTS  //<-- Not relevant for SIMPLE_CONTENTSTREAM
#define SIMPLE_CONTENTSTREAM //Do note that the not-simple one is buggy
using System;
using System.Diagnostics;
using System.Text;
using System.IO;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Contents of a PdfPage
    /// </summary>
    [DebuggerDisplay("{ContentText}")]
    public sealed class PdfContent : StreamElm, ISize
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Content;
        /// </summary>
        internal override PdfType Type { get { return PdfType.Content; } }

        /// <summary>
        /// The content as a raw stream
        /// </summary>
        public MemoryStream Content { get { return new MemoryStream(_stream.DecodedStream); } }

        /// <summary>
        /// The content as text, only intended for debugging
        /// </summary>
        public string ContentText { get { return Encoding.ASCII.GetString(_stream.DecodedStream).Replace('\0', ' '); } }

        /// <summary>
        /// The content stream
        /// </summary>
        public IStream Stream { get { return _stream; } }

        #endregion

        #region Init

        public PdfContent(PdfStream stream)
            : base(stream)
        { }

        internal PdfContent(IWStream stream)
            : base(stream)
        { }

        internal PdfContent(IWStream stream, PdfDictionary dict)
            : base(stream, dict)
        { }

        public PdfContent(byte[] stream, ResTracker tracker)
            : base(stream, tracker)
        { }

        #endregion

        /// <summary>
        /// Fetches the raw contents
        /// </summary>
        internal byte[] GetContent() { return _stream.DecodedStream; }

        #region ISize

        /// <summary>
        /// The size of the stream contents
        /// </summary>
        public ISize Size { get { return (ISize)this; } }

        /// <summary>
        /// The compressed length of this contents
        /// </summary>
        int ISize.Compressed { get { return _stream.Length; } }

        /// <summary>
        /// The decompressed length of this contents
        /// </summary>
        int ISize.Uncompressed { get { return _stream.DecodedStream.Length; } }

        #endregion

        #region Required overrides

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override StreamElm MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfContent((IWStream) stream, dict);
        }

        #endregion
    }

    /// <summary>
    /// An array of PdfContent
    /// </summary>
    /// <remarks>
    /// Non-immutable classes must implement IKRef, for this
    /// reason PdfContentArray implements IKRef
    /// </remarks>
    public class PdfContentArray : TypeArray<PdfContent>, IKRef, ISize
    {
        #region Variables and properties

        /// <summary>
        /// Returns PdfType.ContentArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.ContentArray; } }

        public Stream Contents
        {
            get
            {
                if (_items.Length == 1)
                    return this[0].Content;
                if (_items.Length == 0)
                    return new MemoryStream(0);
#if SIMPLE_CONTENTSTREAM
                return new MemoryStream(ContentsStream.Create(this));
#else
                return new ContentsStream(this);
#endif
            }
        }

#if SIMPLE_CONTENTSTREAM
        public string ContentsText { get { return Encoding.ASCII.GetString(ContentsStream.Create(this)).Replace('\0', ' '); } }
#else
        public string ContentsText { get { return ((ContentsStream) Contents).ContentText; } }
#endif
        #endregion

        #region Init

        /// <summary>
        /// Create empty array
        /// </summary>
        /// <param name="items">Content items</param>
        internal PdfContentArray(bool writable, ResTracker tracker)
            : base(writable, tracker, PdfType.Content)
        { }

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Content items</param>
        internal PdfContentArray(PdfItem[] items, bool writable, ResTracker tracker)
            : base(items, PdfType.Content, writable, tracker)
        { }

        /// <summary>
        /// Create array with one item.
        /// </summary>
        /// <param name="items">Content items</param>
        internal PdfContentArray(PdfItem item, bool writable, ResTracker tracker)
            : this(new PdfItem[] { item }, writable, tracker)
        { }

        /// <summary>
        /// Create array
        /// </summary>
        /// <param name="items">Content items</param>
        internal PdfContentArray(PdfArray items)
            : base(items, PdfType.Content)
        { }

        #endregion

        #region Add methods

        public void PrependContents(byte[] content, int offset)
        {
            _items.InjectNewItem(new PdfContent(content, _items.Tracker), offset, true);
        }

        /// <summary>
        /// Add contents to a writable contents array
        /// </summary>
        public void AddContents(byte[] content)
        {
            _items.AddNewItem(new PdfContent(content, _items.Tracker), true);
        }

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
            return new PdfContentArray(array);
        }

        #endregion

        #region IKRef

        /// <summary>
        /// Default save mode
        /// </summary>
        SM IKRef.DefSaveMode { get { return DefSaveMode; } }

        /// <summary>
        /// If this object has a dummy tracker.
        /// </summary>
        bool IKRef.IsDummy { get { return _items.IsWritable && _items.Tracker == null; } }

        /// <summary>
        /// Adopt this object. Will return false if the adoption failed.
        /// </summary>
        /// <param name="tracker"></param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker)
        {
            if (_items is TemporaryArray)
                _items = (PdfArray) tracker.Adopt((ICRef)_items);
            return _items.Tracker == tracker;
        }

        /// <summary>
        /// Adobt this object. Will return false if the adobtion failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adobt this item</param>
        /// <param name="state">State information about the adoption</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker, object state)
        {
            if (_items is TemporaryArray)
                _items = (PdfArray)tracker.Adopt((ICRef)_items, state);
            return _items.Tracker == tracker;
        }

        /// <summary>
        /// Checks if a tracker owns this object
        /// </summary>
        /// <param name="tracker">Tracker</param>
        /// <returns>True if the tracker is the owner</returns>
        bool IKRef.IsOwner(ResTracker tracker)
        {
            return _items.Tracker == tracker;
        }

        #endregion

        #region ISize

        /// <summary>
        /// The size of the stream contents
        /// </summary>
        public ISize Size { get { return (ISize)this; } }

        /// <summary>
        /// The compressed length of this contents
        /// </summary>
        int ISize.Compressed 
        { 
            get 
            {
                int length = 0;
                foreach (ISize content in this)
                    length += content.Compressed;
                return length; 
            } 
        }

        /// <summary>
        /// The decompressed length of this contents
        /// </summary>
        int ISize.Uncompressed
        {
            get
            {
                int length = 0;
                foreach (ISize content in this)
                    length += content.Uncompressed;
                return length;
            }
        }

        #endregion

        #region Helper class

#if SIMPLE_CONTENTSTREAM
        /// <summary>
        /// Used to unify the content streams into a single stream.
        /// </summary>
        /// <remarks>
        /// The drawbacks of this stream over the more complicated
        /// one is that it must copy the data and it can't handle
        /// contents streams over 2GB. 
        /// 
        /// This implementation also use more memory when CACHE_CONTENTS
        /// isn't defined. Do note however that the ContentsStream itself
        /// is discarted after use, so this shouldn't be a big deal.
        /// </remarks>
        [DebuggerDisplay("{ContentText}")]
        internal class ContentsStream : Stream
        {
            #region Variables and properties

            //A cache of decompressed contents streams
            private readonly MemoryStream _contents;

            public MemoryStream Contents { get { return _contents; } }

            //Position in the total stream
            int _pos = 0;

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }

            public override long Length { get { return _contents.Length; } }

            public override long Position
            {
                get { return _pos; }
                set { Seek(value, SeekOrigin.Begin); }
            }

            public string ContentText
            {
                get
                {
                    return Encoding.ASCII.GetString(_contents.ToArray()).Replace('\0', ' ');
                }
            }

            #endregion

            #region Init

            public ContentsStream(PdfContentArray ar)
            {
                _contents = new MemoryStream(Create(ar));
            }

            public static byte[] Create(PdfContentArray ar)
            {
                byte[][] conts = new byte[ar.Length][];
                int count = 0, total_length = 0;
                foreach (var content in ar)
                {
                    var cont = content.GetContent();
                    conts[count++] = cont;
                    total_length += cont.Length;
                }
                total_length += ar.Length - 1;
                var all = new byte[total_length];
                int stop = conts.Length - 1, pos = 0;
                byte[] ba;
                for (count = 0; count < stop; count++)
                {
                    ba = conts[count];
                    Buffer.BlockCopy(ba, 0, all, pos, ba.Length);
                    pos += ba.Length;
                    all[pos++] = 32;
                }
                ba = conts[stop];
                Buffer.BlockCopy(ba, 0, all, pos, ba.Length);
                return all;
            }

            #endregion

            public override int ReadByte()
            {
                return _contents.ReadByte();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _contents.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _contents.Seek(offset, origin);
            }

            #region Required overrides

            public override void Flush() { }
            public override void SetLength(long value)
            { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            { throw new NotSupportedException(); }

            #endregion
        }
#else
        /// <summary>
        /// Used to unify the content streams into a single stream.
        /// A more preformant approach would be to have the lexer
        /// do this stream shifting. 
        /// </summary>
        /// <remarks>
        /// Todo: This class does not padd contents streams with ' '
        /// 
        /// That noted this class seems rather too complex for what
        /// it does. I've replaced it with a simpler class.
        /// </remarks>
        [DebuggerDisplay("{ContentText}")]
        internal class ContentsStream : Stream
        {
            #region Variables and properties

            //Compressed contents
            private readonly PdfContentArray _ar;

            //Length of contents objects
            private readonly int[] _known_lengths;
            
#if CACHE_CONTENTS
            //A cache of decompressed contents streams
            private readonly MemoryStream[] _contents;
#endif

            //Position in the total stream
            int _pos = 0;
            
            //Start position of the current stream
            int _start;

            //Bytes left in the current stream
            int _left;

            //Index of the current stream
            int _i;

            /// <summary>
            /// Memory stream being read at this moment. Must be
            /// a memory stream since other stream may not cooperate
            /// with how this class use the read methods
            /// </summary>
            MemoryStream _current;

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }

            public override long Length 
            { 
                get 
                {
#if CACHE_CONTENTS
                    int len = 0;
                    for (int c = 0; c < _known_lengths.Length; c++)
                    {
                        int l = _known_lengths[c];
                        if (l == -1)
                        {
                            var cont = _ar[c].Content;
                            l = (int) cont.Length;
                            _known_lengths[c] = l;
                            _contents[c] = cont;
                        }
                        len += l;
                    }
                    return len;
#else
                    //This can be quite slow as all content is decompressed
                    long len = 0;
                    for (int c = 0; c < _ar.Length; c++)
                        len += _ar[c].Content.Length;
                    return len;                    
#endif
                } 
            }

            public override long Position
            {
                get { return _pos; }
                set { Seek(value, SeekOrigin.Begin); }
            }

            public string ContentText
            {
                get
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var cont in _ar)
                        sb.Append(cont.ContentText);
                    return sb.ToString();
                }
            }

            #endregion

            #region Init

            public ContentsStream(PdfContentArray ar)
            {
                _ar = ar;
                _known_lengths = new int[_ar.Length];
                for (int c = 0; c < _known_lengths.Length; c++)
                    _known_lengths[c] = -1;

#if CACHE_CONTENTS
                _contents = new MemoryStream[_known_lengths.Length];
#endif

                _start = int.MaxValue;
                Position = 0;
            }

            #endregion

            public override int ReadByte()
            {
                Debug.Assert(_start + _current.Position == _pos);
                if (_left > 0)
                {
                    //Reads the bytes straight out.
                    _left--;
                    _pos++;
                    return _current.ReadByte();
                }

                while (true)
                {
                    //Must shift buffer
                    _i++;
                    if (_i < _contents.Length)
                    {
                        _start += (int) _current.Length;
#if CACHE_CONTENTS
                        _current = _contents[_i];
                        if (_current == null)
                        {
                            _current = _ar[_i].Content;
                            _contents[_i] = _current;
                        }
                        _current.Position = 0;
#else
                        _current = _ar[_i].Content;
#endif
                        _left = (int)_current.Length;
                        _known_lengths[_i] = _left;

                        if (_left == 0) continue;
                        _pos++;
                        _left--;
                        return _current.ReadByte();
                    }

                    return -1;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                Debug.Assert(_start + _current.Position == _pos);
                int read, read_total;
                if (count <= _left)
                {
                    //Reads the bytes straight out.
                    read = _current.Read(buffer, offset, count);
                    _pos += read;
                    _left -= read;
                    return read;
                }

                //Must split up the reads.
                read_total = 0;
                if (_left > 0)
                {
                    do
                    {
                        read = _current.Read(buffer, offset, _left);
                        read_total += read;
                        offset += read;
                        _left -= read;
                        count -= read;
                    } while (_left != 0);
                }

                int total = _start + (int) _current.Length;
                for (_i += 1; _i < _known_lengths.Length; _i++)
                {
                    _start = total;
#if CACHE_CONTENTS
                    _current = _contents[_i];
                    if (_current == null)
                    {
                        _current = _ar[_i].Content;
                        _contents[_i] = _current;
                    }
                    _current.Position = 0;
#else
                    _current = _ar[_i].Content;
#endif
                    _left = (int)_current.Length;
                    _known_lengths[_i] = _left;

                    total += _left;

                    if (count <= _left)
                    {
                        _pos += read_total;
                        do
                        {
                            read = _current.Read(buffer, offset, count);
                            read_total += read;
                            _left -= read;
                            _pos += read;
                            count -= read;
                        } while (count != 0);
                        return read_total;
                    }
                    else
                    {
                        do
                        {
                            read = _current.Read(buffer, offset, _left);
                            offset += read;
                            count -= read;
                            read_total += read;
                            _left -= read;
                        } while (_left != 0);
                    }
                }

                _pos += read_total;
                return read_total;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        _pos = (int) offset;
                        break;
                    case SeekOrigin.Current:
                        _pos += (int) offset;
                        break;
                    case SeekOrigin.End:
                        _pos = (int) (Length - offset);
                        break;
                }
                if (_pos < 0)
                    _pos = 0;

                int mem_pos = _pos - _start ;
                if (_pos > _start && mem_pos < _current.Length)
                {
                    _current.Position = mem_pos;
                    _left = (int) _current.Length - mem_pos;
                    return _pos;
                }

                //Seeks up to the correct stream.
                int total = 0; _i = 0;
                foreach (var cont in _ar)
                {
                    _start = total;
                    int len = _known_lengths[_i];
                    _current = null;
                    if (len == -1)
                    {
                        _current = cont.Content;
                        #if CACHE_CONTENTS
                        _contents[_i] = _current;
                        #endif
                        len = (int) _current.Length;
                        _known_lengths[_i] = len;
                    }
                    total += len;
                    if (_pos < total)
                    {
                        if (_current == null)
                            //Can assume this never happens when CACHE_CONTENTS
                            _current = cont.Content;
                        _left = len;
                        _current.Position = _pos - _start;
                        return _pos;
                    }
                    _i++;
                }
                _i = _ar.Length - 1;
                _current = _ar[_i].Content;
                _left = 0;
                _current.Position = _current.Length;
                _pos = total;

                return _pos;
            }

            #region Required overrides

            public override void Flush() { }
            public override void SetLength(long value) 
            { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            { throw new NotSupportedException(); }

            #endregion
        }
#endif

        #endregion
    }
}
