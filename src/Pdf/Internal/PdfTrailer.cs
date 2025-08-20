using System;
using System.Collections.Generic;
using PdfLib.Read;
using PdfLib.Read.Parser;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Util;
using PdfLib.Pdf.Encryption;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// File Trailer
    /// </summary>
    /// <remarks>
    /// trailer 
    /// <<  key1 value1 
    ///     key2 value2 
    ///     … 
    ///     keyn valuen 
    /// >> 
    /// startxref 
    /// Byte_offset_of_last_cross-reference_section 
    /// %%EOF
    /// </remarks>
    internal class PdfTrailer : Elements
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Trailer
        /// </summary>
        internal override PdfType Type { get { return PdfType.Trailer; } }

        /// <summary>
        /// Trailers are generated anew during saveing. 
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Ignore; } }

        /// <summary>
        /// Total number of entries in the cross-reference table
        /// </summary>
        /// <remarks>Any object higher than this shall be ignored</remarks>
        public int Size { get { return _elems.GetUIntEx("Size"); } }

        /// <summary>
        /// Byte offset from start of stream to last cross
        /// reference section
        /// </summary>
        public PdfInt Prev { get { return _elems.GetUIntObj("Prev"); } }

        /// <summary>
        /// Byte offset from start of stream to last cross
        /// reference section
        /// </summary>
        [PdfVersion("1.5")]
        public PdfInt XRefStm { get { return _elems.GetUIntObj("XRefStm"); } }

        /// <summary>
        /// The catalog dictionary for the pdf document
        /// </summary>
        public PdfCatalog Root { get { return (PdfCatalog)_elems.GetPdfTypeEx("Root", PdfType.Catalog); } }

        /// <summary>
        /// The encryption used on this document
        /// </summary>
        [PdfVersion("1.1")]
        public PdfEncryption Encrypt { get { return (PdfEncryption)_elems.GetPdfType("Encrypt", PdfType.Encrypt); } }

        /// <summary>
        /// The identifyer of this document
        /// </summary>
        [PdfVersion("1.1")]
        public PdfDocumentID ID { get { return (PdfDocumentID)_elems.GetPdfType("ID", PdfType.DocumentID); } }

        /// <summary>
        /// Information about the document
        /// </summary>
        public PdfInfo Info { get { return (PdfInfo)_elems.GetPdfType("Info", PdfType.Info); } }

        /// <summary>
        /// True if this document is encrypted.
        /// </summary>
        public bool IsEncrypted { get { return _elems.Contains("Encrypt"); } }

        /// <summary>
        /// Used to fetch the root reference to hand over
        /// to the documents.
        /// </summary>
        /// <remarks>
        /// PdfLib does not (yet) offer any "low level" APIs for stuff
        /// like this. I figure that not having a LL API removes the 
        /// temtation to (ab)use it instead of getting the HL stuff right.
        /// (At the cost of hacks like this popping up here and there.)
        /// </remarks>
        internal PdfReference RootRef 
        { 
            get 
            {                
                var ret = _elems["Root"];
                if (!(ret is PdfReference))
                    throw new PdfReadException(ErrSource.General, PdfType.Trailer, ErrCode.IsCorrupt);
                return (PdfReference) ret; 
            } 
        }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        internal PdfTrailer(Catalog cat)
            : base(cat)
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        internal PdfTrailer(PdfDictionary dict)
            : base(dict/*, SM.Ignore*/)
        { }

        #endregion

        #region Create

        /// <summary>
        /// Builds a trailer without using the XRef table
        /// </summary>
        internal static PdfTrailer RebuildFromFile(PdfFile file, IIParser main_parser, ResTracker track, Lexer lex)
        {
            lex.Position = 0;
            //Using its own parser. Encryption and such isn't a problem. We're only interested in the unencrypted "top level" data.
            //Note, this parser will only produce "TempReference" objects. So no sealed nor writable references.
            IIParser parser = new Parser(lex, null);
            PdfItem trailer = null;
            PdfDictionary catalog = null;
            int catalog_id = 0, catalog_gen = 0;
            int max_id = 0;

            while (!lex.IsEOF)
            {
                long pos = lex.Position;
                var type = lex.trySetNextToken();

                //Objects has the signature <int> <int> "obj", so this is what we search for
                if (type == PdfType.Integer && lex.PeekNextItem() == PdfType.Number)
                {
                    int id = lex.GetInteger();
                    type = lex.trySetNextToken();
                    if (type == PdfType.Integer)
                    {
                        int gen = lex.GetInteger();

                        type = lex.trySetNextToken();
                        if (type == PdfType.Keyword && lex.Token == "obj")
                        {
                            var oid = new PdfObjID(id, (ushort)gen);
                            max_id = Math.Max(max_id, id);
                            PdfItem obj;
                            try { parser.LexPosChanged(); obj = parser.ReadItem(); }
                            catch (PdfStreamCorruptException err)
                            {
                                //Tries to repair the stream
                                if (lex.Keyword == PdfKeyword.BeginStream)
                                {
                                    var start = lex.Position + lex.GetLineFeedCount();

                                    var stop = start;
                                    while (!lex.PeekNextKeyword(Chars.e, Chars.n, PdfKeyword.EndStream))
                                    {
                                        lex.SetNextToken();
                                        stop = lex.Position;
                                    }
                                    int length = (int)(stop - start);
                                    var end = lex.Position;
                                    parser.Position = pos;
                                    lex.trySetNextToken();
                                    lex.trySetNextToken();
                                    lex.trySetNextToken();
                                    lex.trySetNextToken();
                                    var cat = parser.ReadCatalog();
                                    cat["Length"] = new PdfInt(length);
                                    var range = new rRange(start, length);

                                    //Creates a repaired stream, which must be placed into the
                                    //xref table
                                    obj = track == null ? new PdfStream(cat, file, range) : new PdfStream(cat, file, range, track);
                                    file.RegisterObject(oid, obj);

                                    parser.Position = end;

                                    continue;
                                }

                                throw err;
                            }

                            //Register object and skip stream
                            file.RegisterObject(oid, pos);

                            if (obj is PdfDictionary dict)
                            {
                                var cat = (Catalog)((ICRef)dict).GetChildren();

                                //if this is a catalog (there can be more than one, in which case we want the
                                // last for reconstrucing the trailer)
                                PdfItem item;
                                if (cat.TryGetValue("Type", out item)) 
                                {
                                    if (item is PdfName && item.GetString() == "Catalog")
                                    {
                                        catalog = dict;
                                        catalog_id = id;
                                        catalog_gen = gen;
                                    }
                                }
                            }

                            continue;
                        }
                    }
                }

                if (type == PdfType.Keyword && lex.Token == "trailer")
                {
                    //Note, we want the last trailer. 
                    main_parser.LexPosChanged();
                    trailer = main_parser.ReadTrailer();
                }
            }

            if (trailer is PdfTrailer)
            {
                var t = (PdfTrailer)trailer;
                try { var test = t.Size; }
                catch (PdfReadException)
                {
                    ((IDictRepair)t._elems).RepairSet("Size", new PdfInt(max_id + 1));
                }
                return t;
            } 
            else if (catalog != null)
            {
                if (catalog.Contains("Pages"))
                {
                    var cat = new Catalog();
                    cat.Add("Size", new PdfInt(max_id + 1));
                    cat.Add("Root", file.GetReference(new PdfObjID(catalog_id, (ushort)catalog_gen)));

                    return new PdfTrailer(cat);
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a trailer from a PDF file
        /// </summary>
        internal static PdfTrailer CreateFromFile(PdfFile file, Lexer lexer)
        {
            //Using the lexer directly for parsing the trailer.
            //The parser is more intended for creating dictionaries,
            //arrayes and such. Just want numbers and keywords for
            //now.


            //Finds xref table. This isn't entierly trivial, as a goof few files prides
            //themselves at making this difficult
            int xrefpos = FindOffset(lexer);
            if (xrefpos == -1)
            {
                //todo: Try rebuilding the xref table.
                throw new PdfLogException(ErrSource.Xref, PdfType.Trailer, ErrCode.Missing);
            }
            lexer.Position = xrefpos;

            //Checs for Cross ref or table or stream
            var type = lexer.PeekNextItem();
            if (type == PdfType.Number)
            {
                //Parses xref stream
                file.SetXRefSpace(2); //<-- Needed since parser will register references. 
                PdfIObject obj = file.Parser.ReadItem() as PdfIObject;
                if (obj == null) throw new PdfReadException(PdfType.Trailer, ErrCode.Missing);
                var xref = (XRefStream)obj.ToType(PdfType.XRefStream);
                xref.Init(file);

                //Parses other xref streams
                var xprev = xref.Prev;
                while (xprev != null)
                {
                    file.Parser.Position = xprev.Value;
                    var xstream = (XRefStream)file.Parser.ReadItem().ToType(PdfType.XRefStream);
                    if (xstream == xref)
                        throw new PdfInternalException("Cyclic reference");
                    xstream.Init(file);
                    xprev = xstream.Prev;
                }

                //Puts the trailer into the resources. The XRefStream trailer is the only object 
                //that's parsed before the XRef table is built, in hybrid and standar files there's
                //no need to do this as the trailer must be in the standar tables and they are
                //always parsed before the XRefStream trailers.
                //
                //Not doing this will result in an issue during saving. In essence, the saver does
                //not know not to save the trailer. By placing the parsed trailer object in the
                //cache, it will tell the saver not to save it and we avoid having the trailer 
                //written out.
                file.UpdateCache(obj.Id, xref);
                //^ In hybrid files the trailer will be in the standar XRef table, so no need
                //for this fudging.

                // pts_pdfsizeopt2009.psom.pdf tests this code path
                //System.Diagnostics.Debug.Assert(false, "Untested code path");
                return xref;
            }

            //How many times to retry finding the header, in those cases where the xrefpos
            //don't hit quite right.
            const int MAX_RETRIES = 2;
            int n_attempts = 0;
            retry:

            lexer.SetNextToken();
            if (type != PdfType.Keyword ||
                    lexer.Keyword != PdfKeyword.XRef)
            {
                if (n_attempts++ < MAX_RETRIES)
                    goto retry;
                throw new PdfLogException(ErrSource.Xref, PdfType.Keyword, ErrCode.UnexpectedToken);
                //Todo: log this
                //return RebuildXRefTable(file, lexer);
            }

            PdfTrailer first = CreateFromTable(file, lexer);
            PdfTrailer prev;

            //Checks if this is a hybrid-reference PDF file. Hybrid files contains a 
            //XRef stream. Priority is "Table->Stream->PrevTable->PrevStream->etc"
            if (first.XRefStm != null)
            {
                lexer.Position = first.XRefStm.Value;
                var xstream = (XRefStream)file.Parser.ReadItem().ToType(PdfType.XRefStream);
                xstream.Init(file);
            }

            //Initializes all XRef streams.
            prev = first;
            while (prev.Prev != null)
            {
                lexer.Position = prev.Prev.Value;
                type = lexer.SetNextToken();
                if (type != PdfType.Keyword &&
                    lexer.Keyword != PdfKeyword.XRef)
                {
                    throw new PdfLogException(ErrSource.Xref, PdfType.Keyword, ErrCode.UnexpectedToken);
                }
                prev = CreateFromTable(file, lexer);

                //Inits the stream
                if (prev.XRefStm != null)
                {
                    lexer.Position = prev.XRefStm.Value;
                    var xstream = (XRefStream)file.Parser.ReadItem().ToType(PdfType.XRefStream);
                    xstream.Init(file);
                }

                if (prev == first)
                    throw new PdfInternalException("Cyclic reference");
            }

            //Returns the first trailer that was read.
            return first;
        }

        /// <summary>
        /// Creates a xref table from a table structure
        /// and reads the trailer dictionary.
        /// </summary>
        /// <remarks>
        /// Quick note on the format
        /// 
        /// A object is registered by a format like this:
        /// 0000000000 00000\r\n
        /// 
        /// First number is the offset, seconed is the generation.
        /// The generation number is used for reusing object ids.
        ///   I.e. You have two xref tables and their ranges overlap,
        ///        then the writer that created the table will set
        ///        the second set of object ids with generation 1
        ///  
        ///        Why do their ranges overlap? Because the writer
        ///        likely wanted to replace one the the objects in
        ///        the overlapping range. That replaced object will
        ///        not have the gen set at 1, and since the last 
        ///        table is parsed first that object will be prefered
        ///
        /// The XRef table also has a concept of free object. The
        /// first object of the table (ID 0) will always bee free,
        /// and it's offset will point at the first free object ID
        ///        
        /// Objects that has been freed is to be treated like null.
        ///   I.e. any reference to that object is to be 
        ///        ignored/set null.
        ///   
        /// Each freed object's offset value will point at the next
        /// freed object, where the last one will point back at
        /// object zero.
        /// 
        /// All XRef tables start with a subsection. A subsection
        /// is just two ordinary numbers. The first is the start
        /// position, the next is the size. There can be multiple
        /// subsections in a table.
        /// </remarks>
        internal static PdfTrailer CreateFromTable(PdfFile file, Lexer l)
        {
            while (true)
            {
                //Have to check for "trailer" keyword.
                var type = l.SetNextToken();
                if (type == PdfType.Integer)
                {
                sub_section:
                    int start_id = l.GetInteger();
                    if (l.SetNextToken() != PdfType.Integer)
                        throw new PdfLogException(ErrSource.Xref, PdfType.Integer, ErrCode.UnexpectedToken);
                    int obj_count = l.GetInteger();
                    file.SetXRefSpace(start_id + obj_count);

                    //First entry shall always be free and shall have a
                    //generation number of 65,535;
                    //(The offset of free objects actually points at the next free object)
                    if (start_id == 0 && obj_count > 0)
                    {
                        //It's possible to have errors here*, but they
                        //are ignored for now. 
                        //*(For instance there might not be a first entery)
                        l.SetNextToken();
                        l.SetNextToken();
                        l.SetNextToken();

                        //Note, don't use start_id and obj_count for anything but
                        //the for loop or change this.
                        start_id++; obj_count--;
                    }

                    for (int id = start_id, end = start_id + obj_count;
                         id < end; id++)
                    {
                        if (l.SetNextToken() != PdfType.Integer)
                            throw new PdfLogException(ErrSource.Xref, PdfType.Integer, ErrCode.UnexpectedToken);

                        //Recognizes sub sections by the length of the first token.
                        if (l.TokenLength != 10)
                            goto sub_section;

                        int obj_pos = l.GetInteger();
                        if (l.SetNextToken() != PdfType.Integer)
                            throw new PdfLogException(ErrSource.Xref, PdfType.Integer, ErrCode.UnexpectedToken);
                        ushort obj_gen = (ushort) l.GetInteger();
                        if (l.SetNextToken() != PdfType.Keyword)
                            throw new PdfLogException(ErrSource.Xref, PdfType.Keyword, ErrCode.UnexpectedToken);

                        //n for in use, f for free
                        string key = l.Token;

                        if (key.Length != 1)
                            throw new PdfLogException(ErrSource.Xref, PdfType.Keyword, ErrCode.Wrong);
                        if (key[0] != 'n')
                        {
                            if (key[0] == 'f') { file.DeleteObject(new PdfObjID(id, obj_gen)); continue; }
                            else throw new PdfLogException(ErrSource.Xref, PdfType.Keyword, ErrCode.Invalid);
                        }
                            

                        var oid = new PdfObjID(id, obj_gen);

                        //An object can be described multiple times,
                        //if it's already registered the first must
                        //be kept.
                        if (!file.HasObjectLocation(oid))
                            file.RegisterObject(oid, obj_pos);
                    }
                }
                else if (type == PdfType.Keyword &&
                         l.Keyword == PdfKeyword.Trailer)
                {
                    //an xref table must end whith a "trailer" keyword
                    return (PdfTrailer)file.Parser.ReadTrailer();
                }
                else
                {
                    throw new PdfLogException(ErrSource.Xref, PdfType.Trailer, ErrCode.UnexpectedToken);
                }
            }
        }

        /// <summary>
        /// Finds a relative offset from the end of the stream to
        /// the XRef table
        /// </summary>
        private static int FindOffset(Lexer lex)
        {
            //Startxref is usualy at the very end of the PDF file. So
            //reads in the last 50 bytes and looks there
            var l = lex.Length;
            byte[] buff = new byte[Math.Min(l, 50)];
            var pos = l - buff.Length;
            lex.Read(buff, pos);

            //Startxref is probably after the last '>' symbol, so looks
            //for that first
            int i = ArrayHelper.LastIndexOf(buff, (byte)Chars.Greater);
            if (i < 0) i = 0;
            if (i < buff.Length)
            {
                //Converts the rest of the buffer into a string
                var str = Lexer.GetString(buff, i, buff.Length - i);

                //And searches for the "startxref" string
                // Bug: One must insure that this is truly the last
                //      startxref
                i = str.IndexOf("startxref");

                if (i != -1)
                {
                    //If we found the string, expect the
                    //next token to be the position
                    lex.Position = l - str.Length + i + 9;
                    if (lex.SetNextToken() == PdfType.Integer)
                        return lex.GetInteger();
                }
            }

            //In cases where it's not at the end, do a more thorught search.
            for (int attemp = 0; attemp < 10; attemp++)
            {
                const int SEARCH_WINDOW = 200, SEARCH_WINDOW2 = 150;
                int search_start_pos = l - SEARCH_WINDOW - SEARCH_WINDOW2 * attemp;
                int search_end_pos = SEARCH_WINDOW - 1;
                buff = new byte[SEARCH_WINDOW];

                while (search_start_pos > 0)
                {
                    lex.Read(buff, search_start_pos);

                    while (search_end_pos > 0)
                    {
                        //Looks for an '>'
                        i = ArrayHelper.LastIndexOf(buff, search_end_pos, (byte)Chars.Greater);

                        //Even if an '>' isn't found, there can still be a startxref in the buffer
                        if (i == -1) i = 0;

                        //Converts that section of the buffer into a string
                        var str = Lexer.GetString(buff, i, search_end_pos - i + 1);
                        search_end_pos = (search_end_pos == i) ? search_end_pos - 1 : i;

                        //And searches for the "startxref" string
                        i = str.IndexOf("startxref");

                        //Checks if we got a valid startxref, and returns in that case
                        if (i != -1)
                        {
                            lex.Position = search_start_pos + search_end_pos + i + 9;
                            if (lex.SetNextToken() == PdfType.Integer)
                                return lex.GetInteger();
                        }

                        //startxref either not found, or not valid. Tries again.
                    }

                    //Nothing found in this search window, moves to the next one.
                    search_start_pos -= SEARCH_WINDOW + 50;
                    search_end_pos = SEARCH_WINDOW - 1;
                }
            }
            return -1;
        }

        /// <summary>
        /// Experimental XRef table rebuilding code.
        /// </summary>
        /// <remarks>
        /// Currently it only handle normal xref files, and only if their
        /// fairly well formed.
        /// 
        /// The next step goes something like this.
        ///  1. Check for object id
        ///  2. If oid do a "ReadDict"
        ///  3. Look for "Length/Stream" keywords
        ///  4. If stream, determine the size of the stream and try fixing/decoding it
        ///  5. Look for new line
        ///  6. Go back to 1
        ///  ++ Finding trailers and xref tables
        ///  
        /// I think the "RebuildXRefTable" should be called from
        /// with a exception thrown from here instead.
        /// </remarks>
        private static PdfTrailer RebuildXRefTable(PdfFile file, Lexer lexer)
        {
            lexer.Position = 0;
            Console.WriteLine("Rebuilding XREF");
            var parser = new Parser(lexer, null);

            var xref = new Dictionary<PdfObjID, PdfItem>();

            var table_trailers = new List<PdfTrailer>();

            do
            {
                try
                {
                    var item = parser.ReadItem();

                    if (item is PdfIObject)
                    {
                        var iobj = (PdfIObject)item;
                        xref.Add(iobj.Id, iobj.Value);
                    }
                    else
                    {
                        lexer.AdvanceToNewLine();
                    }
                }
                catch (PdfParseException)
                {
                    if (lexer.Token == "xref")
                    {
                        //Try parsing the xref table
                        try
                        {
                            var trailer = CreateFromTable(file, lexer);
                            
                            //The trailer parsed. Yay!
                            table_trailers.Add(trailer);
                        }
                        catch (PdfErrException)
                        { }
                    }
                }
                catch (PdfErrException)
                {

                }

            } while (!lexer.IsEOF);

            if (table_trailers.Count > 0)
            {
                //Assumes that the last trailer is the valid one
                var trailer = table_trailers[table_trailers.Count - 1];

                //This sort of works, only one get a lot of debug asserts as
                //the various objects now sit with sealed references and not
                //temp references. I.e. one should not let the parser create
                //references.
                //
                //foreach (var kp in xref)
                //    file.RegisterObject(kp.Key, kp.Value);

                //Todo: Call FixResTable on WritableDocument
                return trailer;
            }

            throw new PdfInternalException("Unable to rebuild XRef table");
        }

        #endregion

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            //PdfLib creates the trailer when saving. It's never moved.
            throw new NotSupportedException();
        }
    }
}
