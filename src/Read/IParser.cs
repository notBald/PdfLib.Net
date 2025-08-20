using System;
using System.IO;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Read
{
    internal interface IIParser : IParser
    {
        /// <summary>
        /// If the lexer's stream is closed.
        /// </summary>
        bool IsClosed { get; }

        /// <summary>
        /// Owner of the parser
        /// </summary>
        new PdfFile Owner { get; set; }

        /// <summary>
        /// ReadItem() works fine to read the trailer, but this method 
        /// avoids a Debug.Assert().
        /// </summary>
        PdfTrailer ReadTrailer();

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
        PdfItem ReadItem(PdfType type);

        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset from the beginning of the stream</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        int Read(byte[] buffer, long offset);
#else
        int Read(byte[] buffer, int offset);
#endif

        /// <summary>
        /// Reads raw data from the stream.
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="stream_off">Offset from the beginning of the stream</param>
        /// <param name="count">How many bytes to read</param>
        /// <returns>How many bytes were copied</returns>
#if LONGPDF
        int Read(byte[] buffer, long stream_off, int count);
#else
        int Read(byte[] buffer, int stream_off, int count);
#endif

        /// <summary>
        /// Gets a raw string.
        /// </summary>
        string GetDebugString(int start, int length);

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
        Catalog ReadCatalog();

        /// <summary>
        /// Replaces the underlying stream, if it's null
        /// </summary>
        /// <param name="s">Stream to use from now on</param>
        void Reopen(Stream s);

        void LexPosChanged();
    }

    public interface IParser : IDisposable
    {
        /// <summary>
        /// Position in the stream
        /// </summary>
#if LONGPDF
        long Position { get; set; }
#else
        int Position { get; set; }
#endif

        /// <summary>
        /// Gets the length of the PDF document.
        /// </summary>
        int Length { get; }

        /// <summary>
        /// Owner of the parser
        /// </summary>
        PdfFile Owner { get; }

        /// <summary>
        /// Reads an item from the stream
        /// </summary>
        PdfItem ReadItem();

        /// <summary>
        /// Closes the lexer
        /// </summary>
        void Close();
    }
}
