using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Filter;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Intended as an alternative to "PdfDictionary", with the catalog now
    /// hidden from callers
    /// </summary>
    public abstract class Elements : PdfObject, IEnumRef, IKRef
    {
        #region Variables

        /// <summary>
        /// The elements
        /// </summary>
        protected PdfDictionary _elems;

        #endregion

        #region Properties

        /// <summary>
        /// Most elements based classes can be saved by any mode.
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Auto; } }

        /// <summary>
        /// If this object can be written to
        /// </summary>
        public bool IsWritable { get { return _elems is WritableDictionary || _elems is TemporaryDictionary; } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor that creates a writable dictionary and
        /// registers itself with the tracker
        /// </summary>
        /// <param name="tracker">Tracker that will own this object</param>
        protected Elements(bool writable, ResTracker tracker)
        {
            if (!writable)
                _elems = new SealedDictionary();
            else
            {
                _elems = tracker == null ? new TemporaryDictionary() : (PdfDictionary) new WritableDictionary(tracker);
                /* tracker.Register(this); */
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        protected Elements(Catalog cat)
        {
            _elems = new SealedDictionary(cat);
        }

        /// <summary>
        /// Create from existing dictionary
        /// </summary>
        protected Elements(PdfDictionary dict)
        {
            _elems = dict;
        }

        /// <summary>
        /// Create from existing dictionary
        /// </summary>
        /*protected Elements(PdfDictionary dict)
            : this(dict, SM.Auto)
        { }*/

        #endregion

        #region IRef

        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference
        { get { return (_elems is IRef) && ((IRef)_elems).HasReference; } }

        WritableReference IRef.Reference
        {
            [DebuggerStepThrough]
            get { if (_elems is IRef) return ((IRef)_elems).Reference; return null; }
            set { if (_elems is IRef) ((IRef)_elems).Reference = value; }
        }

        #endregion

        #region ICRef

        object ICRef.GetChildren() { return ((ICRef)_elems).GetChildren(); }

        ICRef ICRef.MakeCopy(object data, ResTracker t)
        { return (ICRef)MakeCopy((WritableDictionary) ((ICRef)_elems).MakeCopy(data, t)); }

        protected abstract Elements MakeCopy(PdfDictionary elems);

        void ICRef.LoadResources(HashSet<object> check)
        {
            if (check.Contains(_elems))
                return;
            ((ICRef)_elems).LoadResources(check);
            LoadResources();
        }

        bool ICRef.Follow { get { return Follow; } }

        /// <summary>
        /// Flag true if you wish to load resources before disconnecting from a stream
        /// </summary>
        protected virtual bool Follow { get { return true; } }

        /// <summary>
        /// Do the actual loading here.
        /// </summary>
        protected virtual void LoadResources() { }

        #endregion

        #region IKRef

        /// <summary>
        /// Default save mode
        /// </summary>
        SM IKRef.DefSaveMode { get { return DefSaveMode; } }

        /// <summary>
        /// If this object has a dummy tracker.
        /// </summary>
        bool IKRef.IsDummy { get { return _elems.IsWritable && _elems.Tracker == null; } }

        /// <summary>
        /// Adobt this object. Will return false if the adobtion failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adobt this item</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker)
        {
            if (_elems is TemporaryDictionary)
            {
                _elems = (PdfDictionary)tracker.Adopt((ICRef)_elems);
                DictChanged();
            }
            return _elems.Tracker == tracker;
        }

        /// <summary>
        /// Adopt this object. Will return false if the adoption failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adopt this item</param>
        /// <param name="state">State information about the adoption</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker, object state)
        {
            if (_elems is TemporaryDictionary)
            {
                _elems = (PdfDictionary)tracker.Adopt((ICRef)_elems, state);
                DictChanged();
            }
            return _elems.Tracker == tracker;
        }

        /// <summary>
        /// Called every time a dictionary has changed. Usefull for
        /// classes that caches data from the dictionary
        /// </summary>
        /// <remarks>
        /// This is needed for stream based implementors of elements who can't
        /// inherit from "StreamElement"
        /// 
        /// In retrospect one could just have them call "SetDictionary" before
        /// writing instead of having this additional method, as implementors
        /// that needs to override this function have to override the write 
        /// methods anyway.
        /// 
        /// currently only Patterns and Functions make use of this.
        /// </remarks>
        protected virtual void DictChanged()
        { }

        /// <summary>
        /// Checks if a tracker owns this object
        /// </summary>
        /// <param name="tracker">Tracker</param>
        /// <returns>True if the tracker is the owner</returns>
        bool IKRef.IsOwner(ResTracker tracker)
        {
            return _elems.Tracker == tracker;
        }

        #endregion

        #region IEnumRef

        /// <summary>
        /// If there are child references in need of counting.
        /// </summary>
        bool IEnumRef.HasChildren
        { get { return ((IEnumRef)_elems).HasChildren; } }

        /// <summary>
        /// The child's referecnes
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        { get { return ((IEnumRef)_elems).RefEnumerable; } }

        #endregion

        #region Overrides

        /// <summary>
        /// Used to fetch version information
        /// </summary>
        internal override sealed PdfVersion GetPdfVersion(bool fetch, HashSet<object> set)
        {
            //First get the version nr for the class
            var ver = base.PdfVersion;
            set.Add(this);

            //Then the children and fields.
            var type = GetType();
            foreach (var kp in _elems)
            {
                if (!set.Contains(kp.Value))
                {
                    PdfVersion v = (fetch) ? kp.Value.GetPdfVersion(true, set) :  kp.Value.PdfVersion;
                    if (v > ver) ver = v;
                }

                //This class may have version information in the field
                //corresponding with the child's key.
                var field = type.GetProperty(kp.Key);
                if (field != null)
                {
                    object[] attrs = field.GetCustomAttributes(typeof(PdfVersionAttribute), false);
                    for (int c = 0; c < attrs.Length; c++)
                    {
                        PdfVersion v = ((PdfVersionAttribute)attrs[c]).Ver;
                        if (v > ver) ver = v;
                    }
                }
            }

            return ver;
        }

        /// <summary>
        /// General shallow copy method, for deep clone use ResTracker.MakeCopy
        /// </summary>
        public sealed override PdfItem Clone()
        {
            return MakeCopy((PdfDictionary) _elems.Clone());
        }

        //Writes itself to the stream
        internal override void Write(PdfWriter write)
        {
            _elems.Write(write);
        }

        public override string ToString()
        {
            return _elems.ToString();
        }

        #endregion

        #region Helper methods

        /*/// <summary>
        /// Gets a DictCatalog of the wanted type, throws
        /// if it does not exist
        /// </summary>
        internal PdfObject GetPdfTypeEx(string key, PdfType type, PdfMsg msg, object obj)
        {
            var ret = _elems.GetPdfType(key, type, msg, obj);
            if (ret == null)
                throw new PdfReadException(ErrSource.Dictionary, PdfType.Dictionary, ErrCode.Missing);
            return ret;
        }//*/

        #endregion
    }

    /// <summary>
    /// Base class for elements that has streams.
    /// </summary>
    /// <remarks>
    /// Stream objects (PdfStream and WritableStream) consists of a
    /// dictionary and a "stream". Elements only hold a reference to
    /// the dictionary, further when elements save it only saves the
    /// dictionary.
    /// 
    /// One solutions to this would be to modify the dictionary classes
    /// so that they can also hold streams. Then "elements" could forget
    /// all about the stream itself.
    /// 
    /// The current solution is to hold onto a reference to the
    /// stream object. When adopting objects elements call a 
    /// method (DictUpdate) so that implementors have a chance to
    /// update the dictionary in the stream. When saving the write 
    /// methods must be overriden so that one write the stream 
    /// instead of the dictionary.
    /// 
    /// This means a bunch of classes must implement their own
    /// write methods. In a few cases classes can inherit from
    /// "StreamElm" instead, which does all that already.
    /// 
    /// Todo: Get rid of this class. It's basically another Elements
    /// impl. along with PdfEmbededCmap in PdfCmap.cs
    /// </remarks>
    public abstract class StreamElm : PdfObject, IEnumRef, IKRef, ICStream
    {
        #region Variables and properties

        /// <summary>
        /// Stream based objects must be indirect
        /// </summary>
        internal sealed override SM DefSaveMode { get { return SM.Indirect; } }

        /// <summary>
        /// If this object can be written to
        /// </summary>
        public bool IsWritable { get { return _elems is WritableDictionary || _elems is TemporaryDictionary; } }

        /// <summary>
        /// The stream object
        /// </summary>
        protected readonly IStream _stream;

        /// <summary>
        /// The elements
        /// </summary>
        protected PdfDictionary _elems;

        #endregion

        #region Init

        /// <summary>
        /// Creates a writable stream elements from a writable stream
        /// </summary>
        /// <param name="istream"></param>
        internal StreamElm(IWStream istream)
            : this(istream, istream.Elements)
        { }

        /// <summary>
        /// Creates a writable stream elements from a writable stream
        /// </summary>
        /// <param name="istream"></param>
        protected StreamElm(WrMemStream istream)
            : this(istream, istream.Elements)
        { }

        /// <summary>
        /// Constructor that creates a writable dictionary
        /// </summary>
        /// <param name="tracker">Tracker that will own this object</param>
        protected StreamElm(ResTracker tracker)
        {
            if (tracker == null)
            {
                _elems = new TemporaryDictionary();
                _stream = new WritableStream(_elems);
            }
            else
            {
                _elems = new WritableDictionary(tracker);
                _stream = new WritableStream(_elems);
            }
        }

        /// <summary>
        /// Constructor that creates a writable dictionary
        /// </summary>
        /// <param name="data">Data to init the stream with</param>
        /// <param name="tracker">Tracker that will own this object</param>
        protected StreamElm(byte[] data, ResTracker tracker)
        {
            if (tracker == null)
            {
                _elems = new TemporaryDictionary();
                _stream = new WritableStream(data, _elems);
            }
            else
            {
                _elems = new WritableDictionary(tracker);
                _stream = new WritableStream(data, _elems);
            }
        }

        protected StreamElm(PdfStream stream)
        {
            _stream = stream;
            _elems = stream.Elements;
        }

        internal StreamElm(IStream stream, PdfDictionary dict)
        {
            _stream = stream;
            _elems = dict;
        }

        #endregion

        #region IRef

        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference
        { get { return (_elems is IRef) && ((IRef)_elems).HasReference; } }

        WritableReference IRef.Reference
        {
            [DebuggerStepThrough]
            get { if (_elems is IRef) return ((IRef)_elems).Reference; return null; }
            set { if (_elems is IRef) ((IRef)_elems).Reference = value; }
        }

        #endregion

        #region ICRef

        object ICRef.GetChildren() { return ((ICRef)_elems).GetChildren(); }

        ICRef ICRef.MakeCopy(object data, ResTracker t)
        {
            var wd = new WritableDictionary((Catalog)data, t);
            return MakeCopy(((IWStream)_stream).MakeCopy(wd), wd);
        }

        protected abstract StreamElm MakeCopy(IStream stream, PdfDictionary dict);

        void ICRef.LoadResources(HashSet<object> check)
        {
            if (check.Contains(_elems))
                return;
            ((ICRef)_elems).LoadResources(check);
            LoadResources();
        }

        bool ICRef.Follow { get { return Follow; } }

        /// <summary>
        /// Flag true if you wish to load resources before disconnecting from a stream
        /// </summary>
        protected virtual bool Follow { get { return true; } }

        /// <summary>
        /// Do the actual loading here.
        /// </summary>
        protected virtual void LoadResources() 
        {
            if (_stream is PdfStream)
                ((PdfStream)_stream).LoadStream();
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
        bool IKRef.IsDummy { get { return _elems.IsWritable && _elems.Tracker == null; } }

        /// <summary>
        /// Adobt this object. Will return false if the adoption failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adobt this item</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker)
        {
            if (_elems is TemporaryDictionary)
            {
                ((IKRef)_stream).Adopt(tracker);
                _elems = ((NewStream)_stream).Elements;
            }
            return _elems.Tracker == tracker;
        }

        /// <summary>
        /// Adobt this object. Will return false if the adobtion failed.
        /// </summary>
        /// <param name="tracker">Tracker that wish to adobt this item</param>
        /// <param name="state">State information about the adoption</param>
        /// <returns>True if adoption suceeded</returns>
        bool IKRef.Adopt(ResTracker tracker, object state)
        {
            if (_elems is TemporaryDictionary)
            {
                ((IKRef)_stream).Adopt(tracker, state);
                _elems = ((NewStream)_stream).Elements;
            }
            return _elems.Tracker == tracker;
        }

        /// <summary>
        /// Checks if a tracker owns this object
        /// </summary>
        /// <param name="tracker">Tracker</param>
        /// <returns>True if the tracker is the owner</returns>
        bool IKRef.IsOwner(ResTracker tracker)
        {
            return _elems.Tracker == tracker;
        }

        #endregion

        #region IEnumRef

        /// <summary>
        /// If there are child references in need of counting.
        /// </summary>
        bool IEnumRef.HasChildren
        { get { return ((IEnumRef)_elems).HasChildren; } }

        /// <summary>
        /// The child's referecnes
        /// </summary>
        IEnumRefEnumerable IEnumRef.RefEnumerable
        { get { return ((IEnumRef)_elems).RefEnumerable; } }

        #endregion

        #region ICStream

        void ICStream.Compress(List<FilterArray> filters, CompressionMode mode)
        {
            ((ICStream)_stream).Compress(filters, mode);
        }

        PdfDictionary ICStream.Elements { get { return _elems; } }

        void ICStream.LoadResources()
        {
            ((IWStream)_stream).LoadResources();
        }

        #endregion

        #region Write

        internal sealed override void Write(PdfWriter write)
        {
            throw new PdfInternalException("Stream objects can't be saved as a direct object");
        }

        internal override void Write(PdfWriter write, SM store_mode)
        {
            if (store_mode == SM.Direct)
                throw new PdfInternalException("Stream objects can't be saved as a direct object");
            ((IWStream)_stream).Write(write);
        }

        #endregion

        #region Overrides

        /// <summary>
        /// General copy method
        /// </summary>
        public sealed override PdfItem Clone()
        {
            var dict = (PdfDictionary)_elems.Clone();
            return MakeCopy(_stream, dict);
        }

        public override string ToString()
        {
            return _elems.ToString();
        }

        #endregion
    }
}
