//For quickly checking if the decrypt paser works on unencrypted documents
#define USE_ONLY_DECRYPT_PARSER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using PdfLib.Util;
using PdfLib.Pdf.Encryption;

//Namespace for common document objects
using PdfLib.Pdf.Primitives;

//Namespace for "high" level pdf objects
using PdfLib.Pdf;

//Namespace for objects typically not of interest to users of
//the libary.
using PdfLib.Pdf.Internal;

//Namespace for parser related objects,
//though parsing is also done elsewhere
using PdfLib.Read;
using PdfLib.Read.Parser;

//Namespace for writeable objects
using PdfLib.Write.Internal;
using PdfLib.Write;
using PdfLib.Img;
using PdfLib.Img.Png;

using PdfLib.Render.PDF;

namespace PdfLib
{
    /// <summary>
    /// Represents the PDF file.
    /// </summary>
    /// <remarks>
    /// A PDF file is structured as such:
    ///  - A header spesifying the version of the file.
    ///    (This version can be changed later in the file)
    ///  - A body (The PdfDocument)
    ///  - A cross reference table for the location of indirect objects
    ///  - A trailer (PdfTrailer)
    /// </remarks>
    public class PdfFile : IDisposable, ISource
    {
        #region Variables and properties

        /// <summary>
        /// The header information of the PDF file
        /// </summary>
        internal PdfHeader Header;

        /// <summary>
        /// Parser used to read data from the PDF file
        /// </summary>
        internal IIParser Parser;

        /// <summary>
        /// The reference table of objects in this file
        /// </summary>
        private readonly PdfXRefTable _refs;

        /// <summary>
        /// The trailer of the file
        /// </summary>
        private PdfTrailer _trailer;

        /// <summary>
        /// When the document is opened using a filepath, then the path is stored away here.
        /// </summary>
        /// <remarks>Usefull for when one wish to reopen closed PDF files</remarks>
        private string _original_file_path, _user_psw, _owner_psw;

        /// <summary>
        /// If the 
        /// </summary>
        private bool IsOpen => !Parser.IsClosed;

        /// <summary>
        /// When the "DetatchFromFile" function is called, this flag is set true.
        /// 
        /// PdfFile no longer assumes that the underlying filestream exists
        /// </summary>
        public bool FileExists { get { return Parser != null; } }

        /// <summary>
        /// The detected real precision of the document
        /// 
        /// Filled in by the parser.
        /// </summary>
        int _detected_precision;
        public int RealPrecision 
        { 
            get { return _detected_precision; }
            internal set
            {
                if (value > _detected_precision)
                    _detected_precision = value;
            }
        }

        /// <summary>
        /// Resource tracker
        /// </summary>
        internal ResTracker Tracker { get { return _refs.Tracker; } set { _refs.Tracker = value; } }

        /// <summary>
        /// Returns the filename and path of the document
        /// </summary>
        public string FullName { get { return _original_file_path; } }

        /// <summary>
        /// Returns the filename of the document
        /// </summary>
        public string FileName 
        { 
            get 
            {
                if (_original_file_path == null) return null;
                return new FileInfo(_original_file_path).Name; 
            } 
        }

        /// <summary>
        /// The document's ID
        /// </summary>
        public PdfDocumentID ID { get { return _trailer.ID; } }

        /// <summary>
        /// Information about the document
        /// </summary>
        public PdfInfo Info { get {  return _trailer.Info; } } 

        /// <summary>
        /// If this file is encrypted it must have a securety handle
        /// </summary>
        public PdfEncryption SecurityHandler
        {
            get
            {
                if (Parser is DecryptParser)
                    return ((DecryptParser)Parser).SecuretyHandler;
                return null;
            }
            internal set
            {
                Parser = new DecryptParser((Parser) Parser, (PdfStandarSecuretyHandler)value);
            }
        }

        /// <summary>
        /// If this document is closed
        /// </summary>
        public bool IsClosed { get { return !FileExists || Parser.IsClosed; } }

        #endregion

        #region Init and dispose

        /// <summary>
        /// Constructs the PDF file
        /// </summary>
        private PdfFile(PdfHeader header, IIParser parser, ResTracker track)
        {
            Header = header;
            Parser = parser;
            _refs = new PdfXRefTable(track, this);
        }
        ~PdfFile() { Dispose(false); }

        /// <summary>
        /// Disposes PDF file
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Closes the Pdf file
        /// </summary>
        public void Close() 
        { 
            if (Parser != null) Parser.Close();
        }

        /// <summary>
        /// Implements dispose
        /// </summary>
        private void Dispose(bool supress)
        {
            if (supress)
                GC.SuppressFinalize(this);
            if (Parser != null)
                Parser.Dispose();
        }

        #endregion

        #region ISource

        bool ISource.IsExternal { get { return true; } }
        object ISource.LockOn { get { return this; } }
        int ISource.Padding => 0;

        #endregion

        #region IOwner

        /// <summary>
        /// If this file have a trailer.
        /// </summary>
        public bool HasTrailer { get { return _trailer != null; } }

        /// <summary>
        /// Gets a reference object for the given id
        /// </summary>
        internal PdfReference GetReference(PdfObjID id)
        {
            var ret = _refs.GetReference(id);
            if (ret != null || HasTrailer) return ret;

            //Before the trailer is constructed one have
            //to register references that don't exist.
            RegisterObject(id, PdfXRefTable.NO_OFFSET);
            //When references are registered they get ref counted.
            //GetRef does not refcount, unlike GetReference.
            return _refs.GetRef(id);
        }

        /// <summary>
        /// Updates cached data in a reference.
        /// </summary>
        /// <param name="id">Identity of the reference</param>
        /// <param name="value">Value to insert into the reference</param>
        internal void UpdateCache(PdfObjID id, PdfItem value)
        {
            //This function is only to be used before the trailer is constructed
            if (HasTrailer)
                throw new PdfInternalException("Has trailer");

            //First check if there is a reference.
            if (!_refs.Contains(id))
            {
                //Should perhaps log this, but it mearly means that the
                //trailer is not in the xref table. So we'll silently
                //allow it
                return;
            }

            _refs.Update(id, value);
        }

        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset from the beginning of the stream</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        internal int Read(byte[] buffer, long offset)
        { return Parser.Read(buffer, offset); }
        int ISource.Read(byte[] buffer, long offset)
        { return Parser.Read(buffer, offset); }
#else
        internal int Read(byte[] buffer, int offset)
        { return Parser.Read(buffer, offset); }
        int ISource.Read(byte[] buffer, int offset)
        { return Parser.Read(buffer, offset); }
#endif

        #endregion

        #region Object methods

        /// <summary>
        /// Tells the xref table to increese its size
        /// </summary>
        /// <param name="space">How much space to add</param>
        /// <returns></returns>
        internal void SetXRefSpace(int space)
        {
            Debug.Assert(!HasTrailer);
            _refs.SetXRefSpace(space);
        }

        /// <summary>
        /// Checks if this file has a objected of
        /// the desired id.
        /// </summary>
        /// <param name="id">Id of object</param>
        /// <returns>True if the object exists</returns>
        internal bool HasObject(PdfObjID id)
        {
            return _refs.Contains(id);
        }

        /// <summary>
        /// Checks if this xref table has the location
        /// of the given id
        /// </summary>
        /// <param name="id">Id of object</param>
        /// <returns>True if the object exists</returns>
        /// <remarks>
        /// Used for creating the xref table, when one
        /// want to update an objects offset - but only
        /// when the xref table don't already have an 
        /// offset (from say, processing a newer xref
        /// table).
        /// </remarks>
        internal bool HasObjectLocation(PdfObjID id)
        {
            return _refs.HasOffset(id);
        }

        /// <summary>
        /// Registers an object as deleted
        /// </summary>
        /// <param name="id">The id of the object</param>
        internal void DeleteObject(PdfObjID id)
        {
            Debug.Assert(!HasTrailer);
            _refs.Del(id);
        }

        /// <summary>
        /// Registers an object that exists in the PdfFile's stream
        /// </summary>
        /// <param name="id">The id of the object</param>
        /// <param name="offset">Position from the beginning of the stream</param>
#if LONGPDF
        internal void RegisterObject(PdfObjID id, long offset)
#else
        internal void RegisterObject(PdfObjID id, int offset)
#endif
        {
            Debug.Assert(!HasTrailer);
            _refs.SetXRefSpace(id.Nr + 1);
            _refs.Add(id, offset);
        }

        /// <summary>
        /// Registers a compressed object
        /// </summary>
        /// <param name="id">The id of the object</param>
        /// <param name="offset">Position from the beginning of the stream</param>
        internal void RegisterObject(PdfObjID id, int parent_id, int index)
        {
            Debug.Assert(!HasTrailer);
            _refs.SetXRefSpace(id.Nr + 1);
            _refs.Add(id, parent_id, index);
        }

        /// <summary>
        /// Register an already parsed object
        /// </summary>
        /// <param name="id">Identity of the object</param>
        /// <param name="value">Value</param>
        internal void RegisterObject(PdfObjID id, PdfItem value)
        {
            Debug.Assert(!HasTrailer);
            _refs.SetXRefSpace(id.Nr + 1);
            _refs.Add(id, value);
        }

        private PdfItem ParseItem(PdfReference r)
        {
            return _refs.ParseItem(r, Parser);
        }

        /// <summary>
        /// Parses an object located in the stream
        /// </summary>
        /// <remarks>You should lock this object before calling this method</remarks>
        internal PdfItem FetchObject(PdfReference r)
        {
            return ParseItem(r);
        }

        /// <summary>
        /// Parses an object located in the stream as the
        /// desired type
        /// </summary>
        internal PdfItem FetchObject(PdfReference r, PdfType type, IntMsg msg, object obj)
        {
            //This is always the item itself, never a reference or a "PdfIObject"
            var itm = (r.HasValue) ? r.Deref() : ParseItem(r);

            //Not sure if the specs allow refs to refs, but if it does one
            //have to make sure they don't go back to themselves. (This is
            //non trivial. It's not just A->B->A, but A->B->C->D->B)
            if (itm is PdfReference)
                throw new PdfInternalException("Potential circular reference");
            itm = itm.ToType(type, msg, obj);

            //When importing files the references does not get attached
            //to objects by themselves. Not sure where it's best to do
            //this, but I think this might me a good spot.
            if (r is WritableReference)
            {
                var wr = (WritableReference)r;
                wr.SaveMode = itm.DefSaveMode;
                if (itm is IRef)
                {
                    var iref = ((IRef)itm);
                    iref.Reference = wr;
                }
            }

            //This ref tag is used by the resource tracker. It's not
            //needed on plain arrays, dictionaries and other such
            //objects. Their basic function is to allow users to
            //work with objects instead of references. I.e. a IRef
            //object hides whenever it's indirect or not, whereas a
            //plain array will only be indirect if it's wrapped in a
            //reference.

            return itm;
        }

        /// <summary>
        /// Loads resources into memory. 
        /// </summary>
        /// <returns>
        /// May fail at loading all resources, but since
        /// the function can not know if this failed resource
        /// is really needed it returns a bool flag for now.
        /// (Could perhaps return a list of failed ids)
        /// </returns>
        /// <remarks>You should lock this object before calling this method</remarks>
        internal bool LoadAllResources(bool load_stream)
        {
            return _refs.LoadAllRefs(load_stream);
        }


        /// <summary>
        /// Loads refcounted resources into memory. 
        /// </summary>
        /// <returns>
        /// May fail at loading all resources, but since
        /// the function can not know if this failed resource
        /// is really needed it returns a bool flag for now.
        /// (Could perhaps return a list of failed ids)
        /// </returns>
        /// <remarks>You should lock this object before calling this method</remarks>
        internal bool LoadResources()
        {
            return _refs.LoadReferences();
        }

        internal void FixXRefTable()
        {
            _refs.FixXRefTable();
        }

        #endregion

        #region pdf file creation methods

        /// <summary>
        /// Open file in writeable mode.
        /// </summary>
        /// <remarks>Remember to close the document</remarks>
        [DebuggerStepThrough]
        public static WritableDocument OpenWrite(string file_path)
        {
            return (WritableDocument)Open(File.OpenRead(file_path), true);
        }

        /// <summary>
        /// Open file in write mode.
        /// </summary>
        [DebuggerStepThrough]
        public static WritableDocument OpenWrite(Stream pdf_document)
        {
            return (WritableDocument)Open(pdf_document, true);
        }

        /// <summary>
        /// Open file in read only mode.
        /// </summary>
        [DebuggerStepThrough]
        public static SealedDocument OpenRead(byte[] pdf_document)
        {
            return OpenRead(new MemoryStream(pdf_document));
        }

        /// <summary>
        /// Open file in read only mode.
        /// </summary>
        /// <remarks>Remember to close the document</remarks>
        [DebuggerStepThrough]
        public static SealedDocument OpenRead(string file_path)
        {
            return (SealedDocument) Open(File.OpenRead(file_path), false, null, "", false, file_path);
        }

        /// <summary>
        /// Open file in read only mode.
        /// </summary>
        /// <remarks>Remember to close the document</remarks>
        [DebuggerStepThrough]
        public static SealedDocument OpenRead(Stream pdf_document)
        {
            return (SealedDocument)OpenRead(pdf_document, false);
        }

        /// <summary>
        /// Open file in read only mode.
        /// </summary>
        /// <param name="pdf_document">Open stream to the pdf document</param>
        /// <param name="file_path">
        /// Path for reopen functionality
        /// </param>
        /// <remarks>Remember to close the document</remarks>
        [DebuggerStepThrough]
        public static SealedDocument OpenRead(Stream pdf_document, string file_path)
        {
            return (SealedDocument)Open(pdf_document, false, null, "", false, file_path);
        }

        /// <summary>
        /// Open file in read only mode.
        /// </summary>
        /// <remarks>Remember to close the document</remarks>
        //[DebuggerStepThrough]
        internal static SealedDocument OpenRead(Stream pdf_document, bool mt_safe)
        {
            var doc = Open(pdf_document, false);
            if (doc is WritableDocument)
            {
                var wd = (WritableDocument)doc;
                var rt = new ResTracker();
                doc = new SealedDocument(new PdfFile(new PdfHeader(), new Parser(new Lexer(new byte[0]), rt), rt), new TempReference(wd.Catalog));
            }
            return (SealedDocument) doc;
        }

        public static PdfDocument Open(string file_path, bool open_write)
        {
            return Open(file_path, open_write, null);
        }

        /// <summary>
        /// Open file from file path.
        /// </summary>
        /// <remarks>Remember to close the document</remarks>
        [DebuggerStepThrough]
        public static PdfDocument Open(string file_path, bool open_write, string password)
        {
            //Note: OpenRead is correct in writemode too, as one don't write to the opened PDF document's file.
            return Open(File.OpenRead(file_path), open_write, password, password, false, file_path);
        }

        public static PdfDocument Open(Stream pdf_document, bool open_write)
        {
            return Open(pdf_document, open_write, null);
        }

        public static PdfDocument Open(Stream pdf_document, bool open_write, string owner_password, string user_password = "", bool force_rebuild = false)
        {
            return Open(pdf_document, open_write, owner_password, user_password, force_rebuild, null);
        }

        /// <summary>
        /// Open file from stream.
        /// </summary>
        /// <remarks>
        /// Do not close this stream until you're finished
        /// working with the document.
        /// 
        /// Also, the stream will be closed when the PDF file
        /// is closed.
        /// </remarks>
        /// <param name="pdf_document">A stream containing a PDF document or supported image</param>
        /// <param name="open_write">Open the document in write mode</param>
        /// <param name="owner_password">Password for owner of a PDF document</param>
        /// <param name="user_password">Password for a user of a PDF doucment</param>
        /// <param name="force_rebuild">Forces the trailer to be rebuilt</param>
        /// <param name="org_filepath">The original filepath can be attached to the document. Needed for reopen / reclose to function.</param>
        /// <param name="no_extra_stream">
        /// When opening JPG and JPX images, a stream can be attached to the PdfDocument. 
        /// 
        /// For this to be usable, the caller must know when this has happened, so it can decide whenever 
        /// to close the stream or not. Alternativly, make it so wjhen this is set false, close streams when 
        /// right here in this function when we know they're no longer needed (for BMP, PPM, PNG, Tiff, etc.).
        /// </param>
        private static PdfDocument Open(Stream pdf_document, bool open_write, string owner_password, string user_password, bool force_rebuild, string org_filepath, bool no_extra_stream = true)
        {
            //Validates the file
            if (!pdf_document.CanSeek || !pdf_document.CanRead)
                throw new PdfInternalException(SR.SeekStream);

            Log.Info(ErrSource.File, LogEvent.Open, (pdf_document is FileStream) ? Path.GetFileName(((FileStream)pdf_document).Name) : "from a stream");

            //Parses the header
            var header = GetVersion(pdf_document);
            if (!header.IsValid)
            {
                bool put_stream_in_extra = false;
                int? height = null;

                var ii = new ImageInfo();
                ii.DetermineImageCount = true;
                if (org_filepath != null) {
                    var ext = Path.GetExtension(org_filepath).ToLower();
                    if (ext == ".tga")
                        ii.CheckIfTargaFormat = true;
#if CELESTE
                    else if (ext == ".data")
                        ii.CheckIfCelesteFormat = true;
#endif
                }
                ii.Stream = pdf_document;
                pdf_document.Position = 0;
                if (ii.Valid)
                {
                    PdfImage img = null;
                    PngImage png_file = null;
                    if (ii.Format == ImageInfo.FORMAT.JP2 || ii.Format == ImageInfo.FORMAT.J2K)
                    {
                        img = PdfImage.CreateFromJPXData(pdf_document, null, null);
                        put_stream_in_extra = true;
                    }
                    else if (ii.Format == ImageInfo.FORMAT.JPEG)
                    {
                        img = PdfImage.CreateFromJPGData(pdf_document, null);
                        put_stream_in_extra = true;
                    }
                    else if (ii.Format == ImageInfo.FORMAT.PPM ||
                             ii.Format == ImageInfo.FORMAT.PGM ||
                             ii.Format == ImageInfo.FORMAT.PAM)
                        img = PdfImage.CreateFromPPMData(pdf_document, null, null);
                    //TODO: There can be multiple images in these files. ImageInfo should detect these. In theory, run  
                    //      CreateFromPPMData again for each additional image.
                    else if (ii.Format == ImageInfo.FORMAT.PNG)
                    {
                        png_file = PngImage.Open(pdf_document);
                        img = PdfImage.CreateFromPngData(png_file, 0);
                    }
                    else if (ii.Format == ImageInfo.FORMAT.BMP || ii.Format == ImageInfo.FORMAT.BMP_OS2)
                        img = PdfImage.CreateFromBMPData(Bmp.Open(pdf_document));
                    else if (ii.Format == ImageInfo.FORMAT.TGA)
                        img = PdfImage.CreateFromTGAData(TGA.Open(pdf_document));
                    else if (ii.Format == ImageInfo.FORMAT.IFF)
                    {
                        var iff = IFF.Open(pdf_document);
                        img = PdfImage.CreateFromIFFData(iff);
                        height = (int)(iff.Height / iff.Aspect);
                    }
#if CELESTE
                    else if (ii.Format == ImageInfo.FORMAT.Celeste)
                        img = PdfImage.CreateFromCelesteData(Celeste.Open(pdf_document));
#endif

                    if (img != null)
                    {                       
                        var wd = new WritableDocument();
                        if (height == null) 
                            height = img.Height;

                        using (var draw = new DrawPage(wd.NewPage(img.Width, height.Value)))
                        {
                            if (png_file != null)
                            {
                                if (png_file.Gamma != null)
                                {
                                    var create_psf = new PdfLib.Pdf.Function.PdfPSFCreator();
                                    create_psf.Exp(1 / 2.2 / png_file.Gamma.Value);

                                    var gs = new PdfGState();
                                    gs.TR = new PdfLib.Pdf.Function.PdfFunctionArray();
                                    var func = create_psf.CreateFunction(xRange.Create(new double[] { 0, 1 }), xRange.Create(new double[] { 0, 1 }));
                                    for (int c = 0; c < 4; c++)
                                        gs.TR.Add(func);
                                    draw.SetGState(gs);
                                }
                                var back = png_file.BackgroundAr;
                                if (back != null)
                                {
                                    draw.SetFillCS(img.ColorSpace);
                                    draw.SetFillColor(back);
                                    draw.RectAt(0, 0, img.Width, height.Value);
                                    draw.DrawPathNZ(false, false, true);
                                }
                            }

                            draw.PrependCM(new xMatrix(img.Width, 0, 0, height.Value, 0, 0));
                            draw.DrawImage(img);
                        }

                        if (put_stream_in_extra)
                        {
                            if (no_extra_stream)
                            {
                                ((IWStream)img.Stream).LoadResources();
                            }
                            else
                            {
                                wd.AddStreamToDispose(pdf_document, org_filepath);
                            }
                        }

                        return open_write ? wd : (PdfDocument)WritableDocument.ConvertToSealed(wd);
                    }

                    if (ii.Format == ImageInfo.FORMAT.TIF || ii.Format == ImageInfo.FORMAT.BIG_TIF)
                    {
                        var wd = new WritableDocument();
                        pdf_document.Position = 0;
                        using (var tr = Img.Tiff.TiffReader.Open(pdf_document, true, false))
                        {
                            foreach(var image in tr)
                            {
                                try
                                {
                                    using (var draw = new DrawPage(wd.NewPage(image.Width, image.Height)))
                                    {
                                        img = (PdfImage) image.ConvertToXObject(true);
                                        draw.PrependCM(new xMatrix(img.Width, 0, 0, img.Height, 0, 0));
                                        draw.DrawImage(img);
                                    }
                                }
                                catch(Exception e)
                                {
                                    PdfLibException.ExceptionPage(wd[wd.NumPages - 1], e);
                                }
                            }
                        }

                        if (!open_write)
                            return WritableDocument.ConvertToSealed(wd);

                        return wd;
                    }
                }

                throw new PdfLogException(ErrSource.Header, PdfType.None, ErrCode.Missing);
            }
            if (header.IsUnknown)
                Log.GetLog().Add(ErrSource.Header, PdfType.None, ErrCode.Invalid);

            //Creates nessesary objects
            var lexer = new Lexer(pdf_document);
            var tracker = (open_write) ? new ResTracker() : null;
#if USE_ONLY_DECRYPT_PARSER
            IIParser parser = new DecryptParser(lexer, tracker);
#else
            IIParser parser = new Parser(lexer, tracker);
#endif
            var file = new PdfFile(header, parser, tracker);
            parser.Owner = file;
            file._owner_psw = owner_password;
            file._user_psw = user_password;

            //Constructs the PdfFile information
            PdfTrailer trailer;
            try 
            {
                if (force_rebuild)
                    throw new PdfInternalException("Could not build document.");
                trailer = PdfTrailer.CreateFromFile(file, lexer); 
            }
            catch (PdfLibException e)
            {
                //Rebuilds the xref table
                trailer = PdfTrailer.RebuildFromFile(file, parser, tracker, lexer);
                if (trailer == null)
                    throw e;
            }
            file._trailer = trailer;
            
            //Conformant readers shall ignore objectes with
            //id above Size. 
            file._refs.Trim(trailer.Size);


            //Now create PdfDocument
            PdfDocument doc;
            if (open_write)
            {
                //We don't want stuff from the trailer saved in the final document. We
                //therefore zero the refcount, so that stuff in the trailer is ignored.
                //If the "info" is to be saved after all, this must be done differently.
                //
                //One problem is that there may be references in info, so by simply reading
                //from info those references are refcounted. This is a problem as it's
                //natural to expose the info to the client, which isn't yet done.
                //
                //Perhaps make a copy of info it it's accsessed.
                tracker.ZeroRefcount();

                //Since "nothing" has been loaded beyond multiple trailers,
                //and since only the last such trailer is relevant, the
                //refcount for the catalog is 1. Not that it matters, as
                //the catalog is saved indirect, regardless.
                var root_ref = (WritableReference) trailer.RootRef;
                root_ref.RefCount = 1;

                doc = new WritableDocument(tracker, file, root_ref);
            }
            else
                doc = new SealedDocument(file, trailer.RootRef);

            if (trailer.IsEncrypted)
            {
                Log.Info(ErrSource.File, LogEvent.Encrypted);

                var standar = (PdfStandarSecuretyHandler)trailer.Encrypt;

                if (!standar.InitEncryption(trailer.ID, user_password ?? "", owner_password ?? ""))
                    throw new PdfPasswordProtected();

                file.SecurityHandler = standar;
            }

            //Updates the version. This information isn't actually used
            //for anything though.
            var root = trailer.Root;
            if (root.Version.IsValid &&
                root.Version.Minor > file.Header.Minor)
                file.Header = root.Version;

            //Has to be done after encryption is setup. 
            if (open_write)
                ((WritableDocument)doc).PostEncryptionInit();

            if (org_filepath != null)
                file._original_file_path = org_filepath;

            //And we're done
            return doc;
        }

#endregion

#region Special open functions

        /// <summary>
        /// Opens a raw xref table. 
        /// </summary>
        /// <param name="pdf_document">A PDF document</param>
        public static PdfReference[] OpenXRefTable(Stream pdf_document)
        {
            //Validates the file
            if (!pdf_document.CanSeek || !pdf_document.CanRead)
                throw new PdfInternalException(SR.SeekStream);

            //Creates nessesary objects
            var lexer = new Lexer(pdf_document);
            ResTracker tracker = null;
#if USE_ONLY_DECRYPT_PARSER
            IIParser parser = new DecryptParser(lexer, tracker);
#else
            IIParser parser = new Parser(lexer, tracker);
#endif
            var file = new PdfFile(new PdfHeader(), parser, tracker);
            parser.Owner = file;

            //Constructs the PdfFile information
            var trailer = PdfTrailer.CreateFromFile(file, lexer);
            file._trailer = trailer;

            //Conformant readers shall ignore objectes with
            //id above Size and references with no objects. 
            file._refs.Trim(trailer.Size);

            var refs = new List<PdfReference>(trailer.Size);
            foreach (var r in file._refs.EnumerateRefs())
            {
                refs.Add(r);
            }

            return refs.ToArray();
        }

#endregion

#region Locking functions

        /// <summary>
        /// Reopens a document, and aquires a lock. When a document is opened this way
        /// use "Reclose" afterwards.
        /// 
        /// Note: It's safe to call reopen after open, just to aquire that lock
        /// </summary>
        public void Reopen()
        {
            if (_original_file_path == null || !FileExists) return;

            Monitor.Enter(_original_file_path);
            if (Parser.IsClosed)
                Parser.Reopen(File.OpenRead(_original_file_path));
        }

        /// <summary>
        /// Closes the file and exits the file lock
        /// </summary>
        /// <param name="close_file">
        /// If true, the stream is closed, false it's not.
        /// </param>
        public void Reclose(bool close_file)
        {
            if (_original_file_path == null || !FileExists) return;

            try
            {
                if (close_file)
                    Close();
            }
            finally
            {
                Monitor.Exit(_original_file_path);
            }
        }

#endregion

#region Open multiple files

        /// <summary>
        /// Returns all PDF documents in a directory.
        /// </summary>
        /// <param name="dir">Directory</param>
        public static IEnumerable<PdfDocument> OpenFiles(string path)
        {
            return OpenFiles(path, true, false);
        }

        /// <summary>
        /// Returns all PDF documents in a directory.
        /// </summary>
        /// <param name="dir">Directory</param>
        /// <param name="include_subdirectories">Include files in the sub directories</param>
        /// <returns>All PDF documents</returns>
        public static IEnumerable<PdfDocument> OpenFiles(string path, bool include_subdirectories, bool write)
        {
            return OpenFiles(new DirectoryInfo(path), include_subdirectories, write);
        }

        /// <summary>
        /// Returns all PDF documents in a directory.
        /// </summary>
        /// <param name="dir">Directory</param>
        /// <param name="include_subdirectories">Include files in the sub directories</param>
        /// <returns>All PDF documents</returns>
        public static IEnumerable<PdfDocument> OpenFiles(DirectoryInfo dir, bool include_subdirectories, bool write)
        {
            if (dir == null) throw new ArgumentNullException();
            string ext = "*.pdf";

            IEnumerable<FileInfo> files; 

            try
            {
                files = dir.EnumerateFiles(ext);
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var file in files)
            {
                PdfDocument doc;
                try
                {
                    doc = Open(file.FullName, write);
                }
                catch (Exception)
                { doc = null; }

                if (doc != null)
                    yield return doc;
            }

            if (!include_subdirectories)
                yield break;

            IEnumerable<DirectoryInfo> directories;
            try
            {
                directories = dir.EnumerateDirectories();
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var sub_dir in directories)
            {
                foreach (var file in OpenFiles(sub_dir, true, write))
                    yield return file;

            }
        }

#endregion

        /// <summary>
        /// Loads data into memory
        /// </summary>
        public void DetatchFromFile()
        {
            if (!FileExists) return;

            if (IsClosed)
            {
                this.Reopen();
                _refs.LoadAllRefs(true);
                this.Reclose(true);
            }
            else
            {
                _refs.LoadAllRefs(true);
                Close();
            }

            if (Parser != null)
            {
                Parser.Dispose();
                Parser = null;
            }
        }

        /// <summary>
        /// Removes cached object from memory.
        /// </summary>
        /// <remarks>
        /// Except the trailer, root and root pages.
        /// </remarks>
        [DebuggerStepThrough]
        internal void FlushCache()
        {
            if (FileExists)
                _refs.FlushCache();
        }

#region Static helper functions

        internal static PdfFile CreateDummyFile()
        {
            var f = new PdfFile(new PdfHeader(), null, null);
            f._trailer = new PdfTrailer(new Catalog());
            return f;
        }

        internal static PdfDocument Rebuild(PdfFile file, bool open_read)
        {
            lock (file)
            {
                if (!file.IsOpen)
                    throw new NotSupportedException("The document must be open.");

                if (file._original_file_path == null)
                    throw new NotSupportedException("Document must be opened from a filepath");
                var fp = file._original_file_path;
                var up = file._user_psw;
                var op = file._owner_psw;

                //Terminates the PdfFile
                file.Close();
                file.Dispose();
                file.Parser = null;

                var doc = Open(File.OpenRead(fp), !open_read, file._owner_psw, file._user_psw, true);
                doc.File._original_file_path = fp;
                return doc;
            }
        }

        /// <summary>
        /// Compares two items with the equivalence function
        /// </summary>
        internal static bool Equivalent(PdfItem itm1, PdfItem itm2)
        {
            if (itm1 == null) return itm2 == null;
            return itm1.Equivalent(itm2);
        }

        /// <summary>
        /// Compares two strings with the equals function
        /// </summary>
        internal static bool Equivalent(string itm1, string itm2)
        {
            if (itm1 == null) return itm2 == null;
            return itm1.Equals(itm2);
        }

        /// <summary>
        /// Tests if the file can be read by looking for
        /// the PDF version string
        /// </summary>
        /// <param name="pdf_file">PDF file to test</param>
        /// <returns>PdfFile can read the file</returns>
        public static bool CanRead(string file)
        {
            try
            {
                using (var fs = System.IO.File.OpenRead(file))
                    return CanRead(fs);
            }
            catch { return false; }
        }

        /// <summary>
        /// Tests if the file can be read by looking for
        /// the PDF version string
        /// </summary>
        /// <param name="pdf_file">PDF file to test</param>
        /// <returns>PdfFile can read the file</returns>
        public static bool CanRead(Stream pdf_file)
        {
            return GetVersion(pdf_file).Major == 1;
        }

        /// <summary>
        /// Checks the version number of the PDF file.
        /// </summary>
        /// <param name="pdf_file">The file to check</param>
        /// <returns>Version number</returns>
        public static PdfHeader GetVersion(Stream pdf_file)
        {
            long pos = -1;
            try
            {
                pos = pdf_file.Position;
                var b_buf = new byte[8]; //Must be at least 6 characters
                int max_loops = 64;
                pdf_file.Position = 0;

                while (pdf_file.Position != pdf_file.Length && max_loops-- > 0)
                {
                    pdf_file.Read(b_buf, 0, b_buf.Length);
                    var buffer = Encoding.ASCII.GetChars(b_buf);
                cont:
                    int i = ArrayHelper.IndexOf(buffer, '%');
                    if (i != -1)
                    {
                        //Offset to '%'
                        int offs = (int)pdf_file.Position - b_buf.Length + i;

                        ArrayHelper.ShiftFill(buffer, ++i, pdf_file);
                        while (pdf_file.Position != pdf_file.Length)
                        {
                            i = ArrayHelper.IndexOf(buffer, 'P');
                            if (i != -1)
                            {
                                int nl = ArrayHelper.IndexOf(buffer, '\n', '\r');
                                if (nl < i && nl != -1) 
                                {
                                    ArrayHelper.ShiftFill(buffer, ++i, pdf_file);
                                    goto cont; 
                                }
                                ArrayHelper.ShiftFill(buffer, ++i, pdf_file);
                                //Assumes the buffer is big enough to hold the whole string.
                                if (buffer[0] == 'D' && buffer[1] == 'F' && buffer[2] == '-')
                                {
                                    //This is enough for Adobe Acrobat to open a PDF document.
                                    
                                    //Tries to get the version number, if not found simply use
                                    //version 1.255
                                    char cur; char first; int m = 1, n = 255;
                                    while(char.IsWhiteSpace((cur = buffer[3])))
                                        ArrayHelper.ShiftFill(buffer, 1, pdf_file);
                                    if ('0' < cur && cur <= '9')
                                    {
                                        first = cur;
                                        while (char.IsWhiteSpace((cur = buffer[4])))
                                            ArrayHelper.ShiftFill(buffer, 1, pdf_file);
                                        if (cur == '.')
                                        {
                                            while (char.IsWhiteSpace((cur = buffer[5])))
                                                ArrayHelper.ShiftFill(buffer, 1, pdf_file);
                                            if ('0' <= cur && cur <= '9')
                                            {
                                                m = first - '0';
                                                n = cur - '0';
                                            }
                                        }
                                    }
                                    return new PdfHeader((byte)m, (byte)n, (ushort)offs);
                                }
                            }
                            else
                            {
                                // \r and \n is not allowed between % and P
                                if (ArrayHelper.IndexOf(buffer, '\n', '\r') != -1)
                                    break;
                                pdf_file.Read(b_buf, 0, b_buf.Length);
                                buffer = Encoding.ASCII.GetChars(b_buf);
                            }
                        }
                    }
                }
            }
            catch { } //Swallows all exceptions
            finally
            {
                if (pos != -1 && pdf_file.CanSeek)
                    try { pdf_file.Position = pos; }
                    catch { }
            }

            return new PdfHeader();
        }

#endregion
    }
}
