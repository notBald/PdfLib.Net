using System;
using System.Diagnostics;
using PdfLib.Read;
using PdfLib.Pdf.Internal;
using PdfLib.Compose;
using PdfLib.Compose.Text;

namespace PdfLib
{
    /// <summary>
    /// Base exception
    /// </summary>
    public abstract class PdfLibException : Exception
    {
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfLibException() : base() { /*Debug.Assert(false);*/ }

        /// <summary>
        /// Exception with message
        /// </summary>
        /// <param name="msg">Message</param>
        public PdfLibException(string msg) : base(msg) { /*Debug.Assert(false);*/ }

        internal static void ExceptionPage(PdfPage page, Exception e)
        {
            page.Contents = null;
            using (var draw = new cDraw(page))
            {
                draw.FillColor = cColor.RED;
                var msg = new chDocument(e.Message);
                msg.AddParagraph(e.StackTrace);
                msg[0].SetColor(cColor.RED, null);
                msg[0].SetFont(null, page.Height * 14.0 / 842);
                var tb = new chTextBox(page.Width, null)
                {
                    DefaultFont = cFont.Create("helvetica"),
                    DefaultFontSize = page.Height * 10.0 / 842,
                    Padding = (float) (page.Height * 5.0 / 842),
                    PositionOver = true,
                    ParagraphGap = 2.0,
                    DefaultLineHeight = 1.5,
                    Document = msg,

                    //Note, the constructor sets the "contents height". Should change things
                    // a bit so that it's possible to set the actual height instead of this
                    //hack. (Notice that it's set after padding)
                    Height = page.Height
                };

                draw.DrawTextBox(tb);
            }
        }
    }

    public class PdfPasswordProtected : PdfLibException
    {
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfPasswordProtected() : base() { /*Debug.Assert(false);*/ }
    }

    public class PdfUnsuportedEncryption : PdfLibException
    {
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfUnsuportedEncryption() : base() { /*Debug.Assert(false);*/ }

        /// <summary>
        /// Exception with message
        /// </summary>
        /// <param name="msg">Message</param>
        public PdfUnsuportedEncryption(string msg) : base(msg) { /*Debug.Assert(false);*/ }
    }

    public class PdfStreamClosedException : PdfLibException
    {
        public PdfStreamClosedException() { }
    }

    /// <summary>
    /// If the owners don't line up.
    /// </summary>
    public class PdfOwnerMissmatch : PdfLibException
    {
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfOwnerMissmatch() : base() { /*Debug.Assert(false);*/ }

        /// <summary>
        /// Exception with message
        /// </summary>
        /// <param name="msg">Message</param>
        public PdfOwnerMissmatch(string msg) : base(msg) { /*Debug.Assert(false);*/ }
    }

    /// <summary>
    /// Used internaly in the compiler
    /// </summary>
    internal class CompileException : Exception { }

    /// <summary>
    /// Thrown when writing to a non-writable document.
    /// </summary>
    public class PdfNotWritableException : Exception { }

    /// <summary>
    /// Exception only thrown during the creation of
    /// the document
    /// </summary>
    public class PdfLogException : PdfLibException
    {
        public PdfLogException(ErrSource source, PdfType type, ErrCode code) 
        {
            Log.GetLog().Add(source, type, code);
        }
    }

    /// <summary>
    /// Base class for error type exceptions
    /// </summary>
    public abstract class PdfErrException : PdfLibException
    {
        public PdfErrException(ErrSource source, PdfType type, ErrCode code)
        { Log.GetLog().Add(source, type, code); }
        public PdfErrException(ErrSource source, ErrCode code, string msg)
        { Log.Err(source, code, msg); }
    }

    /// <summary>
    /// Exceptions thrown by the parser
    /// </summary>
    public class PdfParseException : PdfErrException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">
        /// Type of token that failed to parse
        /// * Unless otherwise spesified by the code
        /// </param>
        /// <param name="code">Error code</param>
        public PdfParseException(PdfType type, ErrCode code)
            : base(ErrSource.Parser, type, code) { }
    }

    /// <summary>
    /// Exceptions thrown by the Lexer.
    /// </summary>
    /// <remarks>
    /// These exceptions may put the lexer in an inconsitant state, in
    /// particular the "Unexpected Character" exception.
    /// </remarks>
    public class PdfLexerException : PdfErrException
    {
        public PdfLexerException(PdfType type, ErrCode code)
            : base(ErrSource.Lexer, type, code) { }
        public PdfLexerException(ErrCode code, string msg)
            : base(ErrSource.Lexer, code, msg) { }
    }

    /// <summary>
    /// Exception thrown by filters
    /// </summary>
    public class PdfFilterException : PdfErrException
    {
        public PdfFilterException(PdfType type, ErrCode code)
            : base(ErrSource.Filter, type, code) { }
        public PdfFilterException(ErrCode code)
            : base(ErrSource.Filter, PdfType.None, code) { }
        public PdfFilterException(ErrCode code, string msg)
            : base(ErrSource.Filter, code, msg) { }
    }

    /// <summary>
    /// For when casting from one type to another fails
    /// </summary>
    public class PdfCastException : PdfErrException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">Type to cast into</param>
        /// <param name="code">Error code</param>
        public PdfCastException(PdfType type, ErrCode code)
            : base(ErrSource.Cast, type, code) { }
        public PdfCastException(ErrSource source, PdfType type, ErrCode code)
            : base(source, type, code) { }
    }

    /// <summary>
    /// Exception cast when parsed objects discover errors not
    /// directly related to parsing
    /// </summary>
    public class PdfStateException : PdfReadException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">Self</param>
        public PdfStateException(PdfType type)
            : base(ErrSource.General, type, ErrCode.IsCorrupt) { }
        public PdfStateException(ErrSource source, PdfType type)
            : base(source, type, ErrCode.IsCorrupt) { }
    }

    /// <summary>
    /// Exception related to issues reading the contents
    /// in the file
    /// </summary>
    public class PdfReadException : PdfErrException
    {
        public PdfReadException(PdfType type, ErrCode code)
            : base(ErrSource.General, type, code) { }
        public PdfReadException(ErrSource source, PdfType type, ErrCode code)
            : base(source, type, code) { }
    }

    internal class PdfStreamCorruptException : PdfReadException
    {
        public PdfStreamCorruptException(ErrSource source)
            : base(source, PdfType.Stream, ErrCode.Invalid)
        { }
    }

    /// <summary>
    /// Exception related to issues reading the file
    /// </summary>
    public class PdfCreateException : PdfLibException
    {
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfCreateException() : base() { }

        /// <summary>
        /// Exception with message
        /// </summary>
        /// <param name="msg">Message</param>
        public PdfCreateException(string msg) : base(msg) { }
    }

    /// <summary>
    /// Place holder exception for when I prospone creating
    /// error codes
    /// </summary>
    public class PdfInternalException : PdfLibException
    {
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfInternalException() : base() { }

        /// <summary>
        /// Exception with message
        /// </summary>
        /// <param name="msg">Message</param>
        public PdfInternalException(string msg) : base(msg) { }
    }

    /// <summary>
    /// For when circular references are suspected
    /// </summary>
    public class PdfRecursionException : PdfInternalException
    {
        
    }

    /// <summary>
    /// For opperations that are not supported due to the PDF specs, opposed
    /// to features that this libary does not support.
    /// </summary>
    public class PdfNotSupportedException : PdfInternalException
    { 
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfNotSupportedException() : base() { }

        /// <summary>
        /// Exception with message
        /// </summary>
        /// <param name="msg">Message</param>
        public PdfNotSupportedException(string msg) : base(msg) { }    
    }

    /// <summary>
    /// When values are outside allowed ranges.
    /// </summary>
    public class PdfOutOfRangeException : PdfInternalException
    {
        /// <summary>
        /// Parameterless constructor
        /// </summary>
        public PdfOutOfRangeException() : base() { }

        /// <summary>
        /// Exception with message
        /// </summary>
        /// <param name="msg">Message</param>
        public PdfOutOfRangeException(string msg) : base(msg) { }
    }

    /// <summary>
    /// The source of the error message
    /// </summary>
    /// <remarks>
    /// Thought used to group errors together.
    /// But perhaps "Warning", "Critical", "Info"
    /// would be better.
    /// </remarks>
    public enum ErrSource : byte
    {
        General = 0,
        Stream = 1,
        Catalog = 2,
        Xref = 3,
        Pages = 4,
        Numeric = 5,
        File = 6,
        Header = 7,
        Lexer = 8,
        Parser = 9,
        Dictionary = 10,
        XObject = 11,
        Page = 12,
        Compiler = 13,
        Filter = 14,
        Cast = 15,
        ColorSpace = 16,
        Array = 17,
        Font = 18
    }

    public enum ErrCode : byte
    {
        General = 0,
        Invalid = 1,

        /// <summary>
        /// The type parameter must be the missings item's
        /// type.
        /// </summary>
        Missing = 2,
        Wrong = 3,

        /// <summary>
        /// The type parameter is the illegal type when used
        /// with the compiler as source
        /// </summary>
        Illegal = 4,

        /// <summary>
        /// When a item is an iligal value
        /// </summary>
        OutOfRange = 5,
        Unknown = 6,

        UnexpectedObject = 7,

        /// <summary>
        /// PdfType parameter must be the expected type, and
        /// if there's multiple expected type either use the
        /// most important one or use another ErrCode
        /// </summary>
        UnexpectedToken = 8,

        UnexpectedEOF = 9,
        UnexpectedEOD = 10,
        UnexpectedChar = 11,
        UnexpectedNegative = 12,

        CorruptToken = 13,

        /// <summary>
        /// This error code is only for when an already parsed
        /// object has internal corruption. Token parameter
        /// is then it's own type.
        /// </summary>
        IsCorrupt = 14,

        /// <summary>
        /// Type parameter is to be the right type.
        /// </summary>
        WrongType = 15,
        RealToInt = 16,
        NotObject = 17,

        /// <summary>
        /// For not implemented errors
        /// </summary>
        NotImpl1 = 255,
    }
}
