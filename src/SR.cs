using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib
{
    /// <summary>
    /// String resource class
    /// </summary>
    static class SR
    {
        //Should perhaps drop this as 
        public static readonly int CALL_DEPTH = 32;

        public static string Internal { get { return "Internal error"; } }
        public static string InvalidName { get { return "Name is not valid"; } }
        public static string ObjNotFound { get { return "Object not found"; } }
        public static string ItemNotFound { get { return "Item not found"; } }

        public static string SeekStream { get { return "Can only open seekable streams"; } }
        public static string WrongVersion { get { return "Can not read PDF document version: {0}"; } }
        public static string WrongArLength { get { return "Array is of the wrong length"; } }
        public static string CorruptName { get { return "The key name \"{0}\" is corrupt"; } }
        public static string CorruptNumber { get { return "The number \"{0}\" can not be parsed"; } }
        public static string CorruptString { get { return "The string can not be parsed"; } }
        public static string CorruptStream { get { return "The stream could not be read"; } }
        
        public static string UnexpectedEnd { get { return "Unexpected end of stream"; } }
        public static string UnexpectedNegative { get { return "Got negative number, expected positive"; } }

        public static string MissingArray { get { return "Missing required array"; } }
        public static string MissingDict { get { return "Missing required dictionary"; } }
        public static string MissingNumber { get { return "Number missing from dictionary"; } }
        public static string MissingItem { get { return "Item missing from dictionary"; } }

        public static string ExpectedArray { get { return "Expected an array"; } }
        public static string ExpectedDict { get { return "Expected a dictionary"; } }
        public static string ExpectedName { get { return "Expected a name"; } }
        public static string ExpectedNumber { get { return "Expected a number"; } }

        //Out of use
        public static string WrongType { get { return "Object if of the wrong type"; } }
        public static string NotIndirect { get { return "Object must be indirect"; } }
        public static string RealCast { get { return "Real can not be converted to int"; } }
        public static string CannotParseCRefStream { get { return "Cannot parse Cross Ref Stream. See PDF specs 7.5.8."; } }
        public static string UnexpectedToken { get { return "Encountered a unexpected token during parsing"; } }
        public static string UnexpectedObject { get { return "Encountered a unexpected object during parsing"; } }
        public static string NotObject { get { return "Expected object token"; } }
        public static string InvalidCast { get { return "Can't change the type of this item"; } }

        //Debugging aid
        internal static string HexDump(byte[] data) { return PdfLib.Pdf.Filter.PdfHexFilter.HexDump(data); }
    }
}
