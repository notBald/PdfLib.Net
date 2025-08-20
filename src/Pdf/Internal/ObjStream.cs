using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Read;
using PdfLib.Read.Parser;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Contains a stream of objects
    /// </summary>
    /// <remarks>
    /// Object stream gets special treatment, so there's no
    /// need to inherit from "StreamElms" as these objects
    /// will never be adopted. (StreamElms was created after
    /// this class)
    /// </remarks>
    internal sealed class ObjStream : Elements
    {
        #region Variables and properties

        /// <summary>
        /// Stream containing the compressed objects
        /// </summary>
        private IWStream _stream;

        /// <summary>
        /// Used to read out the objects
        /// </summary>
        private IIParser _parser;

        /// <summary>
        /// File this stream resides in.
        /// </summary>
        private PdfFile _owner;

        /// <summary>
        /// Table over object references
        /// </summary>
        private ObjRef[] _table;

        /// <summary>
        /// PdfType.ObjStream
        /// </summary>
        internal override PdfType Type { get { return PdfType.ObjStream; } }

        /// <summary>
        /// Number of objects in this stream
        /// </summary>
        internal int N { get { return _elems.GetUIntEx("N"); } }

        /// <summary>
        /// Number of items in the colection.
        /// </summary>
        public int Count
        {
            get
            {
                var ext = Extends;
                if (ext == null) return N;
                return ext.GetCount(this) + N;
            }
        }

        /// <summary>
        /// A stream this stream extends
        /// </summary>
        private ObjStream Extends { get { return (ObjStream)_elems.GetPdfType("Extends", PdfType.ObjStream, IntMsg.Owner, _owner); } }

        /// <summary>
        /// The byte offset in the decoded stream of the first compressed object.
        /// </summary>
        private int First { get { return _elems.GetUIntEx("First"); } }

        /// <summary>
        /// Tells that the resource tracker should ignore this object.
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Ignore; } }

        #endregion

        #region Init

        public ObjStream(PdfFile owner, IWStream stream)
            :base(stream.Elements)
        {
            _stream = stream;
            _elems.CheckTypeEx("ObjStm");
            _owner = owner;
        }

        #endregion

        #region Indexing

        /// <summary>
        /// Casts object on the given index into the desired type
        /// </summary>
        /*public PdfObject ToType(int index, PdfType type, PdfItem msg)
        {
         * //Extends must be checked
            var ret = this[index];
            if (ret is PdfObject)
                return ((PdfObject)ret).ToType(_owner, type, msg);
            return ret.ToType(type, msg);
        }*/

        /// <summary>
        /// Get object at the given index
        /// </summary>
        public PdfIObject this[int index]
        {
            get
            {
                var ext = Extends;
                if (ext != null)
                {
                    //Not sure how right this is.
                    if (index >= N)
                    {
                        Debug.Assert(false, "Code not tested");
                        return ext[index - N];
                    }
                }

                if (_table == null)
                    _parser = new Parser(_owner, BuildTable(), _elems.Tracker);

                if (index < 0 || _table.Length <= index)
                    throw new PdfReadException(ErrSource.Stream, PdfType.Item, ErrCode.Missing);

                var off = _table[index];

                //Must hold in case the debugger wants a value.
                var hold = _parser.Position;
                try
                {
                    _parser.Position = off.Offset;

                    //Objects will be cached by the PdfXRefTable, so no need to bother here.
                    var item = _parser.ReadItem();
                    if (item is PdfIObject)
                        //This is wrong. Errcode should be something like "Object not allowed"
                        throw new PdfReadException(ErrSource.Parser, PdfType.Item, ErrCode.UnexpectedObject);

                    //Boxing into an IObject so that the caller can check if it's the right object.
                    return new PdfIObject(new PdfObjID(off.Id, 0), item);
                }
                finally
                {
                    _parser.Position = hold;
                }
            }
        }

        private int GetCount(ObjStream first)
        {
            if (this == first)
                throw new PdfInternalException("Cyclic reference");
            var ext = Extends;
            if (ext == null) return N;
            return N + ext.GetCount(first);
        }

        /// <summary>
        /// Builds the object reference table
        /// </summary>
        private Lexer BuildTable()
        {
            var data = _stream.DecodedStream;
            var first = First;

            if (first >= data.Length)
                throw new PdfReadException(ErrSource.Stream, PdfType.None, ErrCode.UnexpectedEOD);

            Lexer lex = new Lexer(data);
            int next_index = 0;
            _table = new ObjRef[N];

            while (lex.Position < first && next_index < _table.Length)
            {
                //Gets two integers and adds the reference to the table
                if (lex.SetNextToken() != PdfType.Integer)
                    throw new PdfReadException(ErrSource.Stream, PdfType.Integer, ErrCode.UnexpectedToken);
                var obj_id = lex.GetInteger();
                if (lex.SetNextToken() != PdfType.Integer)
                    throw new PdfReadException(ErrSource.Stream, PdfType.Integer, ErrCode.UnexpectedToken);
                var obj_offset = lex.GetInteger();
                var obj = new ObjRef(obj_id, first + obj_offset);

                _table[next_index++] = obj;
            }

            //Only decompressing once
            _stream = null;

            return lex;
        }

        #endregion

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            //PdfLib creates object streams as needed. They are never moved.
            throw new NotSupportedException();
        }

        #region debug aid

        internal override void Write(PdfWriter write)
        {
            throw new PdfInternalException("Tried to save a non-writable object stream");
        }

        /// <summary>
        /// The stream as text
        /// </summary>
        internal string StreamStr 
        { 
            get 
            {
                if (_stream != null)
                    return Lexer.GetString(_stream.DecodedStream);
                else if (_parser != null)
                    return "<Data has been decompressed: "+_parser.GetDebugString(0, _parser.Length);
                return string.Empty;
            } 
        }

        #endregion

        #region structs

        struct ObjRef
        {
            public readonly int Id;
            public readonly int Offset;
            public ObjRef(int id, int offset) { Id = id; Offset = offset; }
        }

        #endregion
    }
}
