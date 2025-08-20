using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Encryption;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Read
{
    /// <summary>
    /// Parser that decrypts string data and streams.
    /// 
    /// This is basically just a copy&paste of parser.cs with stuff
    /// for decryption added.
    /// </summary>
    /// <remarks>
    /// There's two spots where it's naural to implement decryption
    ///  - The parser
    ///  - the PdfFile
    ///  
    /// The former is more flexible and require less code changes to
    /// other classes.
    /// 
    /// The latter is a bit more complex. The file would have to know
    /// when to decrypt streams (as they can have crypt filters for that
    /// instead) and would have to go through dictionaries and arrays
    /// so that it could decrypt the strings.
    /// 
    /// The only issue I could think of with using the parser is that
    /// it must use the object id to decrypt. When a document is corrupt 
    /// in such a way that a object's id is wrong, the decryprion won't
    /// work.
    /// 
    /// However my testing makes it clear the common viewers don't like
    /// this at all. Adobe X flat out refuse to open a document where 
    /// just one id was wrong or missing*.
    /// 
    /// * So while mistakes in the xref table and missing endobjet 
    ///   markers are tolerated, that is not the case with object ids.  
    ///   IOW depending on them is not such a bad idea.
    /// 
    /// For now I keep the "Decrypt" parser sepperate from the original 
    /// working code. That way bugs here won't affect unencrypted parsing.
    /// </remarks>
    internal sealed class DecryptParser : Parser
    {
        #region Variables and properties

        readonly StandarSecuretyHandler _sech;

        public PdfEncryption SecuretyHandler { get { return _sech; } }

        #endregion

        #region Init

        internal DecryptParser(Parser parser, StandarSecuretyHandler sech)
            : base(parser)
        { _sech = sech; }

        /// <summary>
        /// Constructor
        /// </summary>
        internal DecryptParser(Lexer lex, ResTracker tracker) 
            : base(lex, tracker)
        { }

        #endregion

        protected override PdfItem ReadObjImpl(Pdf.Internal.PdfType type)
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
                    return new SealedReference((PdfFile)_owner, id);
                }

                return pdf_ref;
            }

            //Checks if this is a object
            if (_lex.IsObj())
            {
                //Creates reference id.
                var id = new PdfObjID(((PdfInt)_num_next).Value, (ushort)num.Value);

                #region DEBUG
                //if (id.Nr == 117)
                //    Debug.Assert(true);
                #endregion

                //Stores the current key
                //
                //There's only one situation where this is needed, and that's
                //when reading the length property on streams. I've set save/restore
                //around that instead. Though setting it here would allow one to
                //read that data while debugging.
                //var old_key = _sech.Save();

                //Sets up encryption for this object
                if (_sech != null)
                    _sech.SetEncryptionFor(id);

                //Must clear the used value.
                _num_next = null;

                //Gets the object
                var val = ReadItem();

                //Checks that the object was ended properly
                SetNextKeyword(PdfKeyword.EndObj);

                //Restores the previous key
                //_sech.Restore(old_key);

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
                            _owner.RealPrecision = token.Length - i - 1;
                        return new PdfReal(double.Parse(token, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    return new PdfReal(_lex.GetReal());

                case PdfType.String:
                    return new PdfString(_sech != null ? _sech.Decrypt(Lexer.DecodePDFString(_lex.RawToken, 0, _lex.ByteRange.Length)) : Lexer.DecodePDFString(_lex.RawToken, 0, _lex.ByteRange.Length), false, false);
                case PdfType.HexString:
                    return new PdfString(_sech != null ? _sech.Decrypt(Lexer.DecodeHexString(_lex.RawToken, 0, _lex.ByteRange.Length)) : Lexer.DecodeHexString(_lex.RawToken, 0, _lex.ByteRange.Length), true, false);

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

        private PdfObject ReadDictImpl()
        {
            var cat = ReadCatalog();

            //Checks if there's a stream
            if (_lex.PeekNextKeyword(Chars.s, Chars.t, PdfKeyword.BeginStream))
            {
                PdfItem l;
                if (!cat.TryGetValue("Length", out l))
                    throw new PdfReadException(ErrSource.Parser, PdfType.Stream, ErrCode.Invalid);

                //Length can be a reference, in which case the encryption key will be changed.
                int length;
                if (_sech != null)
                {
                    var key = _sech.Save();
                    length = l.GetInteger();
                    _sech.Restore(key);
                }
                else
                    length = l.GetInteger();

                var range = _lex.GetStreamRange(length);
                SetNextKeyword(PdfKeyword.EndStream);


                if (_tracker != null)
                    return new PdfStream(cat, _sech != null ? _sech.CreateDecryptWrapper(_owner) : _owner, range, _tracker);
                return new PdfStream(cat, _sech != null ? _sech.CreateDecryptWrapper(_owner) : _owner, range);
            }

            if (_tracker != null)
                return new WritableDictionary(cat, _tracker);
            return new SealedDictionary(cat);
        }
    }
}
