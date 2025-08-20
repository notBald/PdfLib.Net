using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Transparency;
using PdfLib.Pdf.Annotation;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Function;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.Filter;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Internal.Minor;
using PdfLib.Pdf.Encryption;
using PdfLib.Pdf.Optional;
using PdfLib.Pdf;
using PdfLib.Pdf.Form;

namespace PdfLib.Read
{
    public class Parser : IDisposable
    {
        #region Variables

        /// <summary>
        /// Lexer used to read the stream
        /// </summary>
        protected readonly Lexer _lex;

        /// <summary>
        /// There is a need to check references up against
        /// the reference table in the PdfFile.
        /// </summary>
        /// <remarks>Change this into an interface.</remarks>
        protected PdfFile _owner;

        protected readonly ResTracker _tracker;

        /// <summary>
        /// References are defined as two positive numbers
        /// and a R (i.e. 12 0 R), so we need to look ahead
        /// at least three values
        /// </summary>
        protected PdfItem _num_next = null; //Either Integer or Real

        /// <summary>
        /// The position of the _num_next token in the stream.
        /// </summary>
#if LONGPDF
        protected long _num_pos;
#else
        protected int _num_pos;
#endif

        /// <summary>
        /// If the lexer's stream is closed.
        /// </summary>
        internal bool IsClosed { get { return _lex.IsClosed; } }

        #endregion

        #region Properties

        /// <summary>
        /// Position in the stream
        /// </summary>
#if LONGPDF
        public long Position
#else
        public int Position
#endif
        {
            get { return _num_next != null ? _num_pos : _lex.Position; }
            set { _lex.Position = value; _num_next = null; }
        }
        internal void LexPosChanged() { _num_next = null; }

        /// <summary>
        /// Gets the length of the PDF document.
        /// </summary>
        public int Length
        {
            get { return _lex.Length; }
        }

        /// <summary>
        /// Owner of the parser
        /// </summary>
        public PdfFile Owner 
        { 
            get { return _owner; }
            internal set
            {
                if (_owner != null)
                    throw new PdfInternalException("This parser has a owner");
                _owner = value;
            }
        }

        #endregion

        #region Init and dispose

        protected Parser(Parser p)
            : this(p.Owner, p._lex, p._tracker)
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        internal Parser(Lexer lex, ResTracker tracker) { _lex = lex; _tracker = tracker; }

        /// <summary>
        /// Constructor for creating a parser with a
        /// different lexer than the one used by the
        /// owner
        /// </summary>
        public Parser(PdfFile owner, Lexer lex, ResTracker tracker)
        { _lex = lex; _owner = owner; _tracker = tracker; }

        /// <summary>
        /// Constructor for creating a parser with a
        /// different lexer than the one used by the
        /// owner
        /// </summary>
        public Parser(PdfFile owner, System.IO.Stream stream)
        { _lex = new Lexer(stream); _owner = owner; _tracker = null; }

        /// <summary>
        /// Closes the lexer
        /// </summary>
        public void Dispose()
        {
            _lex.Close();
        }


        /// <summary>
        /// Closes the lexer
        /// </summary>
        public void Close()
        {
            _lex.Close();
        }

        /// <summary>
        /// Replaces the underlying stream, if it's null
        /// </summary>
        /// <param name="s">Stream to use from now on</param>
        internal void Reopen(System.IO.Stream s)
        {
            _lex.Reopen(s);
        }

        #endregion

        #region Public and internal read item methods

        /// <summary>
        /// Reads an item from the stream
        /// </summary>
        [DebuggerStepThrough]
        public PdfItem ReadItem() 
        { 
            return ReadObjImpl(SetNextToken()); 
        }

        /// <summary>
        /// ReadItem() works fine to read the trailer, but this method 
        /// avoids a Debug.Assert().
        /// </summary>
        internal PdfTrailer ReadTrailer()
        {
            if (SetNextToken() != PdfType.BeginDictionary)
                throw new PdfParseException(PdfType.BeginDictionary, ErrCode.UnexpectedToken);

            var cat = ReadCatalog();
            var dict = (_tracker != null) ? new WritableDictionary(cat, _tracker) : 
                (PdfDictionary) new SealedDictionary(cat);
            return (PdfTrailer)CreatePdfType(dict, PdfType.Trailer, IntMsg.NoMessage, null);
        }

        /// <summary>
        /// Only for use by the compiler for reading out strings
        /// and other more complex items.
        /// </summary>
        /// <remarks>
        /// No reason why the compiler can't just call ReadType directly
        /// except for now I want to check if there is cached data. 
        /// (since the compiler use the lexer directly it will not
        ///  know about any cached data.)
        /// </remarks>
        internal PdfItem ReadItem(PdfType type)
        {
            var ret = ReadType(type);
            if (_num_next != null)
                throw new PdfInternalException();
            return ret;
        }

        #endregion

        #region Internal methods

        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset from the beginning of the stream</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        internal int Read(byte[] buffer, long offset)
#else
        internal int Read(byte[] buffer, int offset)
#endif
        {
            return _lex.Read(buffer, offset);
        }

        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="stream_off">Offset from the beginning of the stream</param>
        /// <param name="count">How many bytes to read</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        internal int Read(byte[] buffer, long stream_off, int count)
#else
        internal int Read(byte[] buffer, int stream_off, int count)
#endif
        {
            return _lex.Read(buffer, stream_off, count);
        }

        /// <summary>
        /// Gets a raw string.
        /// </summary>
        internal string GetDebugString(int start, int length)
        {
            return _lex.GetDebugString(start, length);
        }

        #endregion

        #region Set methods

        /// <summary>
        /// Used when parsing objects with ReadObjImpl
        /// </summary>
        /// <remarks>
        /// Makes sure the right type information is returned
        /// </remarks>
        [DebuggerStepThrough]
        private PdfType SetNextToken()
        {
            if (_num_next != null)
                return _num_next.Type;
            return _lex.SetNextToken();
        }

        protected void SetNextKeyword(PdfKeyword kw)
        {
            SetNextToken(PdfType.Keyword);
            if (_lex.Keyword != kw)
                throw new PdfParseException(PdfType.Keyword, ErrCode.Wrong);
        }

        /// <summary>
        /// Used when parsing objects with ReadObjImpl
        /// </summary>
        /// <remarks>
        /// Makes sure the right type information is returned
        /// </remarks>
        private bool SetNextToken(PdfType type, PdfType end)
        {
            if (_num_next != null)
            {
                if (type == _num_next.Type)
                    return true;
                if (end == _num_next.Type)
                    return false;
                throw new PdfParseException(type, ErrCode.UnexpectedToken);
            }
            var t = _lex.SetNextToken();
            if (t != type)
            {
                if (t == end) return false;
                throw new PdfParseException(type, ErrCode.UnexpectedToken);
            }
            return true;
        }

        /// <summary>
        /// Use this methods instead of _lex.SetNextToken();
        /// </summary>
        private void SetNextToken(PdfType type)
        {
            if (_num_next != null)
            {
                if (type != _num_next.Type)
                    throw new PdfParseException(type, ErrCode.UnexpectedToken);
                return;
            }
            if (_lex.SetNextToken() != type)
                throw new PdfParseException(type, ErrCode.UnexpectedToken);
        }

        #endregion

        #region Read methods

        /// <summary>
        /// Reads a object from the stream, where the first token is of a known type.
        /// 
        /// For use with the "SetNextToken()" functions
        /// </summary>
        /// <param name="type">Note that this is not a "error checking" parameter</param>
        /// <remarks>
        /// The purpose of this function is to parse irefs. An iref consists of two
        /// numbers and the keyword 'R'. Because of this one need to either look ahead
        /// or look back as much as three values.
        /// 
        /// This function puts aside one value, and use the peek functionality in the
        /// lexer to "look" at three values simultaniously, but for this to work the 
        /// SetNextToken() function must be used in place of "_lex.SetNextToken()"
        /// </remarks>
        protected virtual PdfItem ReadObjImpl(PdfType type)
        {
            PdfItem ret;
            if (_num_next != null)
            {
                //The object is "read"
                ret = _num_next;
                _num_next = null;

                //Checks if it's the expected type
                Debug.Assert(ret.Type == type);

                if (type == PdfType.Integer)
                    goto check_if_ref;
                return ret;
            }
            
             //For parsing of iref objects one need two positive numbers followed by keyword R
            ret = ReadType(type);
            if (type != PdfType.Integer)
                return ret;

        check_if_ref:

            //Checks if this is a positive integer, if not we know it's not a reference parameter
            var num = (PdfInt)ret;
            if (num.Value < 0)
                return ret;

            //Checks the next token. If it's not a number
            //we can safly return the previous number
            if (_lex.PeekNextItem() != PdfType.Number)
                return ret;

            //Since we know it's a number we can call "SetNum" instad if SetNextToken
            _num_pos = _lex.Position;
            PdfType new_type = _lex.SetNumNext();
            ret = ReadType(new_type);
            if (new_type != PdfType.Integer)
            {
                //If it's not a integer, we can assume it's a real,
                //but we need to hold on to the Real for now.
                _num_next = ret;

                //Returning the first read integer
                return num;
            }

            //Putting aside the original integer for now.
            _num_next = num;

            //Casting the new integer into a integer object
            num = (PdfInt)ret;

            //We now have two integers in a row. This might therefore be
            //a reference. Check for positive, and that keyword is 'R'
            if (num.Value < 0 || _lex.PeekNextItem() != PdfType.Keyword)
                goto not_ref;

            //Checks if this is a reference
            if (_lex.IsR())
            {
                //We now know that this is a reference, guaranteed. 
                _lex.SetNextToken();
                Debug.Assert(_lex.Keyword == PdfKeyword.R);

                //Creates reference id.
                var id = new PdfObjID(((PdfInt)_num_next).Value, (ushort)num.Value);

                //Must clear the used value.
                _num_next = null;

                if (_owner == null)
                {
                    //This happens when, for instance, parsing contents streams
                    //and there is a reference in it (References are not allowed
                    //in content streams).
                    return new TempReference(id);
                }

                PdfReference pdf_ref = _owner.GetReference(id);

                //PDF Specs 3.2.9:
                //Indirect reference to an undefined object is to be treated as null
                if (pdf_ref == null)
                {
                    //Ignores this check during construction of the trailer, as the xref
                    //may be incomplete (Ref for CCITT G4.pdf is an example of this)
                    if (_owner.HasTrailer)
                    { //Note, PdfFile now always return references during the construction phase,
                      //      taking care to prune the table afterwards, so this check is redundant
                        Debug.WriteLine(string.Format("Parser.ReadObjImpl(PdfType type): Ignored object \"{0}\"", id));
                        //Debug.Assert(false, "More likly a bug in my code than the intent of the PDF file");
                        //Hmm, Green.pdf has deleted objects, use Green14 for testing instead.
                        return PdfNull.Value;
                    }

                    //Creates a new reference. Perhaps an idea to register this reference
                    //in the Xref table. But I think it's a good idea to support offset
                    //less references for now, and this way I get to test them
                    return new SealedReference((PdfFile) _owner, id);
                }
                
                return pdf_ref;
            }

            //Checks if this is a object
            if (_lex.IsObj())
            {
                //Creates reference id.
                var id = new PdfObjID(((PdfInt)_num_next).Value, (ushort)num.Value);

                //Must clear the used value.
                _num_next = null;

                //Gets the object
                var val = ReadItem();

                //Checks that the object was ended properly
                SetNextKeyword(PdfKeyword.EndObj);

                return new PdfIObject(id, val);
            }

        not_ref:

            //This is not a reference, so getting back the first read integer
            //and putting the second aside.
            ret = _num_next;
            _num_next = num;
            return ret;
        }

        /// <summary>
        /// Reads a type from the stream
        /// </summary>
        private PdfItem ReadType(PdfType type)
        {
            switch (type)
            {
                case PdfType.Keyword:
                    switch (_lex.Keyword)
                    {
                        case PdfKeyword.Null:
                            return PdfNull.Value;

                        case PdfKeyword.Boolean:
                            return PdfBool.GetBool(_lex.Token);

                        //More complex keywords must be handeled
                        //by the caller. 

                        default:
                            goto error;
                    }

                case PdfType.Integer:
                    return new PdfInt(_lex.GetInteger());

                case PdfType.Real:
                    if (_owner != null)
                    {
                        var token = _lex.Token;
                        var i = token.IndexOf('.');
                        if (i != -1)
                        {
                            _owner.RealPrecision = token.Length - i - 1;
                        }
                        return new PdfReal(double.Parse(token, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    return new PdfReal(_lex.GetReal());

                case PdfType.String:
                    return new PdfString(_lex.RawToken, false);
                case PdfType.HexString:
                    return new PdfString(_lex.RawToken, true);

                case PdfType.Name:
                    {
                        var name = _lex.GetName();
                        if (name.Length == 0)
                            return PdfNull.Value;
                        return new PdfName(name);
                    }

                case PdfType.BeginArray:
                    return ReadArrayImpl();

                case PdfType.BeginDictionary:
                    return ReadDictImpl();

                default:
                    goto error;
            }

        error:
            if (type == PdfType.EOF)
                throw new PdfParseException(PdfType.EOF, ErrCode.UnexpectedEOF);
            throw new PdfParseException(type, ErrCode.UnexpectedToken);
        }

        /// <summary>
        /// Reads an array from the stream
        /// </summary>
        protected PdfArray ReadArrayImpl()
        {
            var ret = new List<PdfItem>();
            PdfType t;
            while ((t = SetNextToken()) != PdfType.EndArray)
                ret.Add(ReadObjImpl(t));

            if (_tracker != null)
                return new WritableArray(ret, _tracker);
            return new SealedArray(ret);
        }

        private PdfObject ReadDictImpl()
        {
            var cat = ReadCatalog();

            //Checks if there's a stream
            if (_lex.PeekNextKeyword(Chars.s, Chars.t, PdfKeyword.BeginStream))
            {
                PdfItem l;
                if (!cat.TryGetValue("Length", out l))
                    throw new PdfStreamCorruptException(ErrSource.Parser);
                int length = l.GetInteger();
                var range = _lex.GetStreamRange(length);
                SetNextKeyword(PdfKeyword.EndStream);

                if (_tracker != null)
                    return new PdfStream(cat, _owner, range, _tracker);
                return new PdfStream(cat, _owner, range);
            }

            if (_tracker != null)
                return new WritableDictionary(cat, _tracker);
            return new SealedDictionary(cat);
        }

        /// <summary>
        /// Reads a dictionary catalog from the stream
        /// </summary>
        /// <remarks>
        /// The first element of each entry is the key and 
        /// the second element is the value. 
        /// 
        /// The key shall be a name The value may be any 
        /// kind of object, including another dictionary. 
        /// 
        /// A dictionary entry whose value is null shall be 
        /// treated the same as if the entry does not exist. 
        /// </remarks>
        internal Catalog ReadCatalog()
        {
            var ret = new Catalog();
            while (SetNextToken(PdfType.Name, PdfType.EndDictionary))
            {
                //First read the key
                string key = _lex.GetName();

                //Then the object or value. Note will throw a
                //unexpected token error if it's a EOF token
                PdfItem value = ReadObjImpl(SetNextToken());

                //Specifying the null object as the value of a dictionary entry 
                //shall be equivalent to omitting the entry entirely. 
                if (value != PdfNull.Value)
                    ret[key] = value;
                //Note, duplicated enteries are overwritten. Should perhaps log
                //a warning. 
            }
            return ret;
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// A number of properties can return two types: 
        ///  A plain object, or an array/dictionary.
        ///  
        /// To hide this complexity PdfLib will return a container even
        /// if there's not one in the document.
        /// 
        /// This leads to a change in the "in memory" structure of the PDF
        /// document, so care must be taken when preforming these operations
        /// so that the document don't break.
        /// </summary>
        internal static PdfObject CreateContainer(PdfType container_type, 
                                                  PdfItem optional_child, 
                                                  IntMsg msg,
                                                  object obj)
        {
            switch (container_type)
            {
                case PdfType.ContentArray:
                    return (optional_child != null) ? new PdfContentArray(optional_child, msg == IntMsg.ResTracker, (ResTracker) obj) :
                                                      new PdfContentArray(msg == IntMsg.ResTracker, (ResTracker) obj);

                case PdfType.FilterArray:
                    return (optional_child != null) ? new FilterArray(optional_child) :
                                                      new FilterArray();

                case PdfType.FilterParmsArray:
                    return (optional_child != null) ? new FilterParmsArray(new PdfItem[] { optional_child }, (FilterArray)obj) :
                                                      new FilterParmsArray();

                case PdfType.FontElms:
                    if (!(optional_child is PdfDictionary)) 
                        throw new PdfReadException(ErrSource.General, PdfType.Dictionary, ErrCode.WrongType);
                    return new FontElms((PdfDictionary)optional_child);

                case PdfType.PatternElms:
                    if (!(optional_child is PdfDictionary))
                        throw new PdfReadException(ErrSource.General, PdfType.Dictionary, ErrCode.WrongType);
                    return new PatternElms((PdfDictionary)optional_child);

                case PdfType.ColorSpaceElms:
                    if (!(optional_child is PdfDictionary))
                        throw new PdfReadException(ErrSource.General, PdfType.Dictionary, ErrCode.WrongType);
                    return new PdfColorSpaceElms((PdfDictionary)optional_child);

                case PdfType.FunctionArray:
                    return new PdfFunctionArray(optional_child, (msg == IntMsg.Special));

                case PdfType.ShadingElms:
                    if (!(optional_child is PdfDictionary))
                        throw new PdfReadException(ErrSource.General, PdfType.Dictionary, ErrCode.WrongType);
                    return new ShadingElms((PdfDictionary)optional_child);

                case PdfType.ActionArray:
                    var ar = (object[])obj;
                    return (optional_child != null) ? new PdfActionArray(optional_child, (PdfCatalog)ar[0], msg == IntMsg.ResTracker, (ResTracker)ar[1]) :
                                                      new PdfActionArray((PdfCatalog)ar[0], msg == IntMsg.ResTracker, (ResTracker)ar[1]);

                default:
                    throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Used for creating "higher" objects
        /// </summary>
        /// <remarks>
        /// The problem is that during parsing one can't
        /// reliably determine the type of an object. That
        /// has to be infered by context.
        /// 
        /// My solution is to allow objects to be (cast)
        /// into different objects.
        /// 
        /// * Object creation now also happens in PdfArray, 
        ///   PdfName, PdfString and possibly other places 
        ///   too.
        /// </remarks>
        internal static PdfObject CreatePdfType(PdfDictionary dict, PdfType t, IntMsg msg, object obj)
        {
            switch (t)
            {
                case PdfType.Trailer:
                    return new PdfTrailer(dict);

                case PdfType.Catalog:
                    return new PdfCatalog(dict);

                case PdfType.Encrypt:
                    return PdfEncryption.Create(dict);

                case PdfType.Pages:
                    return new PdfPages(dict);

                case PdfType.Page:
                    return new PdfPage(dict);

                case PdfType.Resources:
                    return new PdfResources(dict);

                case PdfType.ColorSpaceElms:
                    return new PdfColorSpaceElms(dict);

                case PdfType.XObjectElms:
                    return new XObjectElms(dict);

                case PdfType.FontElms:
                    return new FontElms(dict);

                case PdfType.Font:
                    return PdfFont.Create(dict);

                case PdfType.FontDescriptor:
                    return new PdfFontDescriptor(dict);

                case PdfType.FilterParams:
                    return Pdf.Filter.PdfFilterParms.Create(dict, (Pdf.Filter.PdfFilter)obj);

                case PdfType.Encoding:
                    return new PdfEncoding(dict);

                case PdfType.PatternElms:
                    return new PatternElms(dict);

                case PdfType.Pattern:
                    return new PdfShadingPattern(dict);

                case PdfType.Shading:
                    return PdfShading.Create(dict);

                case PdfType.Function:
                    return PdfFunction.Create(dict);

                case PdfType.GStateElms:
                    return new GStateElms(dict);

                case PdfType.GState:
                    return new PdfGState(dict);

                case PdfType.FunctionArray:
                    if (msg == IntMsg.Special)
                        return new PdfFunctionArray(PdfFunction.Create(dict), true);
                    goto default;

                case PdfType.CIDFont:
                    return PdfCIDFont.Create(dict);

                case PdfType.ShadingElms:
                    return new ShadingElms(dict);

                case PdfType.CIDSystemInfo:
                    return new PdfCIDSystemInfo(dict);

                case PdfType.DNCSAttrib:
                    return new DeviceNCS.AttributesDictionary(dict);

                case PdfType.ColorantsDict:
                    return new DeviceNCS.ColorantsDictionary(dict);

                case PdfType.ProcessDictionary:
                    return new DeviceNCS.ProcessDictionary(dict);

                case PdfType.Outline:
                    if (!(obj is PdfCatalog))
                        goto default;
                    if (dict["Title"] != null)
                        return new PdfOutlineItem(dict, (PdfCatalog)obj);
                    if (msg != IntMsg.RootOutline)
                        goto default;
                    return new PdfOutline(dict, (PdfCatalog)obj);                  

                case PdfType.Annotation:
                    return PdfMarkupAnnotation.Create(dict);

                case PdfType.NameDictionary:
                    return new PdfNameDictionary(dict);

                case PdfType.NamedDests:
                    return new PdfNamedDests(dict);

                case PdfType.TreeNode:
                    return new TreeNode(dict);

                case PdfType.Destination:
                    return new PdfDestinationDict(dict);

                case PdfType.NamedFiles:
                    return new PdfNamedFiles(dict);

                case PdfType.Action:
                    return PdfAction.Create(dict, (PdfCatalog)obj);

                case PdfType.Info:
                    return new PdfInfo(dict);

                case PdfType.ASDict:
                    return new PdfASDict(dict);

                case PdfType.AppearanceDictionary:
                    return new PdfAppearanceDictionary(dict);

                case PdfType.UsageDictionary:
                    return new PdfOptUsage(dict);

                case PdfType.CreatorInfo:
                    return new PdfOptCreatorInfo(dict);

                case PdfType.LanguageDictionary:
                    return new PdfOptLanguage(dict);

                case PdfType.ExportDictionary:
                    return new PdfOptExport(dict);

                case PdfType.ZoomDictionary:
                    return new PdfOptZoom(dict);

                case PdfType.PrintDictionary:
                    return new PdfOptPrint(dict);

                case PdfType.ViewDictionary:
                    return new PdfOptView(dict);

                case PdfType.UserDictionary:
                    return new PdfOptUser(dict);

                case PdfType.PageElementDictionary:
                    return new PdfOptPageElement(dict);

                case PdfType.OptionalContentGroup:
                    return new OptionalContentGroup(dict);

                case PdfType.OptionalContentMembership:
                    return new OptionalContentMembership(dict);

                case PdfType.InteractiveFormDictionary:
                    return new PdfInteractiveForm(dict);

                case PdfType.FieldDictionary:
                    return new PdfFormField(dict);

                case PdfType.BorderEffect:
                    return new PdfBorderEffect(dict);

                case PdfType.SoftMask:
                    return PdfSoftMask.Create(dict);

                case PdfType.Group:
                    return PdfGroup.Create(dict);

                default:
                    throw new PdfCastException(t, ErrCode.Invalid);
            }
        }

        /// <summary>
        /// For creating high level objects based on arrays
        /// </summary>
        /// <param name="ar">Array for the object</param>
        /// <param name="t">Type</param>
        /// <returns>High level object</returns>
        /// <remarks>
        /// This method could/should be put in array's "ToType" method
        /// </remarks>
        internal static PdfObject CreatePdfType(PdfItem[] ar, PdfType t)
        {
            switch (t)
            {
                case PdfType.Rectangle:
                    return new PdfRectangle(ar);

                case PdfType.IntRectangle:
                    return new PdfIntRectangle(ar);

                default:
                    throw new NotSupportedException();
            }
        }

        #endregion
    }
}
