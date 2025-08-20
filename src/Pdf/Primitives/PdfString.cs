//#define SAFE_TOSTRING //for when debuging the implementation
using System;
using System.IO;
using PdfLib.Read;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;
using System.Diagnostics;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Encapsulates a string in the PDF file
    /// </summary>
    /// <remarks>
    /// There are three string types (7.9.2.2)
    ///  -Byte string
    ///  -ASCII string
    ///  -Text string
    ///    -UTF-16BE
    ///    -PDFDocEncoding
    ///    
    /// What type of string one are dealing with has to be
    /// infered by context. 
    /// </remarks>
    public sealed class PdfString : PdfItem
    {
        #region Variables

        private readonly bool _hex;

        private readonly byte[] _raw_value;

        /// <summary>
        /// Returns "string" regardless of this being a hex string or
        /// literal string
        /// </summary>
        internal override PdfType Type { get { return PdfType.String; } }

        /// <summary>
        /// Gets the raw unconverted string data.
        /// </summary>
        public byte[] RawString { get { return _raw_value; } }

        /// <summary>
        /// Converts the string from unicode, if possible.
        /// </summary>
        /// <remarks>For use with strings that are unicode encoded in the source data</remarks>
        public string UnicodeString 
        { 
            get 
            {
                byte[] bytes;
                if (_hex)
                    bytes = Lexer.DecodeHexString(_raw_value, 0, _raw_value.Length);
                else
                    bytes = Lexer.DecodePDFString(_raw_value, 0, _raw_value.Length);
                return Lexer.GetUnicodeString(bytes); 
            } 
        }

        /// <summary>
        /// The string
        /// </summary>
        public string Value { get { return GetString(); } }

        /// <summary>
        /// Gets the string as bytes
        /// </summary>
        public byte[] ByteString
        {
            get 
            {
                if (_hex)
                    return Lexer.DecodeHexString(_raw_value, 0, _raw_value.Length);
                else
                    return Lexer.DecodePDFString(_raw_value, 0, _raw_value.Length);
            }
        }

        public bool IsHex { get { return _hex; } }

        #endregion

        #region Init

        /// <summary>
        /// Creates new string from a encoded byte array
        /// </summary>
        /// <remarks>Set internal since it's easy to use it by mistake</remarks>
        internal PdfString(byte[] value, bool hex)
        {
            _raw_value = value;
            _hex = hex;
        }

        /// <summary>
        /// Creates new string from a raw byte array
        /// </summary>
        public PdfString(byte[] value)
                : this(value, false, false)
        { }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value">Bytes that make up the string</param>
        /// <param name="hex">Whenever ot use hex decoding or not when decoding the data</param>
        /// <param name="is_encoded">If the bytes that make up the string already are encoded</param>
        public PdfString(byte[] value, bool hex, bool is_encoded)
        {
            _hex = hex;
            if (!is_encoded)
                _raw_value = (hex) ? EncodeHexString(value) : EncodeLiteralString(value);
            else
                _raw_value = value;
        }

        #endregion

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null) return false;
            if (obj is PdfString)
                return GetString() == ((PdfString) obj).GetString();
            return false;
        }

        /// <summary>
        /// Encodes a series of bytes into a hex encoded string
        /// </summary>
        /// <param name="bytes">Bytes to encode</param>
        /// <returns>A enclosed hex string</returns>
        public static byte[] EncodeHexString(byte[] bytes)
        {
            var ret = new byte[bytes.Length * 2 /*+ 2*/];
            //ret[0] = (byte)'<';
            //ret[ret.Length - 1] = (byte)'>';
            int write_pos = 0;// 1;
            bool jump = true;
            for (int c = 0; c < bytes.Length; c++)
            {
                byte b = bytes[c];
                int b1 = (b & 0xF0) >> 4;
            again:
                b1 += 48;
                if (b1 > 57) b1 += 7;

                ret[write_pos++] = (byte)b1;
                if (jump) { jump = false; b1 = b & 0x0F; goto again; }
                jump = true;
            }
            Debug.Assert(write_pos /*+ 1*/ == ret.Length);
            return ret;
        }

        /// <summary>
        /// Encodes a byte string. 
        /// </summary>
        public static byte[] EncodeLiteralString(byte[] bytes)
        {
            var ms = new MemoryStream(bytes.Length);
            int open = 0, last_open_pos = -1;
            for (int c = 0; c < bytes.Length; c++)
            {
                var b = bytes[c];
                if (b == 0x28)
                {
                    if (last_open_pos != -2)
                    {
                        last_open_pos = Util.ArrayHelper.IndexOf(bytes, last_open_pos + 1, bytes.Length, 0x29);
                        if (last_open_pos != -1)
                            open++;
                        else
                        {
                            ms.WriteByte(0x5C);
                            last_open_pos = -2;
                        }
                    }
                    else
                        ms.WriteByte(0x5C);
                    
                }
                else if (b == 0x29)
                {
                    if (open > 0)
                        open--;
                    else
                        ms.WriteByte(0x5C);

                    last_open_pos = c;
                }
                else if (b == 0x5C)
                    ms.WriteByte(b);

                ms.WriteByte(b);
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Encodes a non-unicode literal string
        /// </summary>
        public static byte[] EncodeLiteralString(char[] chrs)
        {
            var ms = new MemoryStream(chrs.Length);
            int open = 0, last_open_pos = -1;
            for (int c = 0; c < chrs.Length; c++)
            {
                ushort ch = (ushort)chrs[c];
                if (ch > byte.MaxValue)
                {
                    if (ch >= ushort.MaxValue)
                        throw new PdfNotSupportedException("Charcodes larger than 511");
                    ms.Write(Lexer.GetBytes(ch.ToString("###")), 0, 4);
                }
                else 
                {
                    if (ch == 0x28)
                    {
                        if (last_open_pos != -2)
                        {
                            last_open_pos = Util.ArrayHelper.IndexOf(chrs, last_open_pos + 1, ')');
                            if (last_open_pos != -1)
                                open++;
                            else
                            {
                                ms.WriteByte(0x5C);
                                last_open_pos = -2;
                            }
                        }
                        else
                            ms.WriteByte(0x5C);
                        
                    }
                    else if (ch == 0x29)
                    {
                        if (open > 0)
                            open--;
                        else
                            ms.WriteByte(0x5C);
                        
                        last_open_pos = c;
                    }
                    else if (ch == 0x5C)
                        ms.WriteByte(0x5C);

                    ms.WriteByte((byte)ch);
                }
            }

            return ms.ToArray();
        }

        /// <summary>
        /// There's no integer representation for strings
        /// </summary>
        public override int GetInteger() { throw new NotSupportedException(); }

        /// <summary>
        /// There's no real representation for strings
        /// </summary>
        public override double GetReal() { throw new NotSupportedException(); }

        /// <summary>
        /// Returns string value
        /// </summary>
        [DebuggerStepThrough]
        public override string GetString()
        {
            if (_hex)
                return Lexer.HexString(_raw_value, 0, _raw_value.Length);
            else
                return Lexer.PDFString(_raw_value, 0, _raw_value.Length);
        }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public override PdfItem Deref() { return this; }

        /// <summary>
        /// Strings are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        /// <summary>
        /// Strings can be cast into dates
        /// </summary>
        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            if (type == PdfType.Date)
                return new PdfDate(GetString());
            return base.ToType(type, msg, obj);
        }

        /// <summary>
        /// Writes itself to the stream
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            write.Write(((_hex) ? (byte)60 : (byte)40));
            write.WriteRaw(_raw_value);
            write.WriteRaw(((_hex) ? (byte)62 : (byte)41));
        }

        /// <summary>
        /// String representation of the string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
#if SAFE_TOSTRING
            return "PdfString with safe ToString";
#else
            return GetString();
#endif
        }
    }
}