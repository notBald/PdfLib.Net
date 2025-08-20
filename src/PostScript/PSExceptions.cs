using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.PostScript
{
    /// <summary>
    /// Base class for error type exceptions
    /// </summary>
    public abstract class PSErrException : PdfLibException
    {
        public PSErrException(ErrSource source, PSType type, ErrCode code)
        { /*Log.GetLog().Add(source, type, code);*/ }
    }

    /// <summary>
    /// Exceptions thrown by the Lexer.
    /// </summary>
    /// <remarks>
    /// These exceptions may put the lexer in an inconsitant state, in
    /// particular the "Unexpected Character" exception.
    /// </remarks>
    public class PSLexerException : PSErrException
    {
        public PSLexerException(PSType type, ErrCode code)
            : base(ErrSource.Lexer, type, code) { }
    }

    /// <summary>
    /// Exceptions thrown by the parser
    /// </summary>
    public class PSParseException : PSErrException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">
        /// Type of token that failed to parse
        /// * Unless otherwise spesified by the code
        /// </param>
        /// <param name="code">Error code</param>
        public PSParseException(PSType type, ErrCode code)
            : base(ErrSource.Parser, type, code) { }
    }

    /// <summary>
    /// Exception related to issues reading the contents
    /// in the file
    /// </summary>
    public class PSReadException : PSErrException
    {
        public PSReadException(PSType type, ErrCode code)
            : base(ErrSource.General, type, code) { }
        public PSReadException(ErrSource source, PSType type, ErrCode code)
            : base(source, type, code) { }
    }

    /// <summary>
    /// For when casting from one type to another fails
    /// </summary>
    public class PSCastException : PdfLibException
    {
        public PSCastException() { }
        public PSCastException(string msg) : base(msg) { }
    }
}
