using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using PdfLib.Read;
using PdfLib.Pdf.Transparency;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Function;
using PdfLib.Pdf.Font;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// A name in accordance with 7.3.5 of the PDF specs
    /// </summary>
    public sealed class PdfName : PdfItem
    {
        #region Variables and properties

        /// <summary>
        /// Name value
        /// </summary>
        readonly string _name;

        /// <summary>
        /// PdfType.Name
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Name; }
        }

        /// <summary>
        /// The name value
        /// </summary>
        public string Value { get { return _name; } }

        /// <summary>
        /// The string as it appeares in a PDF file
        /// </summary>
        public string RawValue
        {
            get
            {
                StringBuilder sb = new StringBuilder(_name.Length);
                for (int c = 0; c < _name.Length; c++)
                {
                    char ch = _name[c];
                    Chars ich = (Chars) ch;
                    if (Lexer.IsWhiteSpace(ich) || Lexer.IsDelimiter(ich)
                        || ich == Chars.Hash)
                    {
                        sb.Append('#');
                        sb.Append(((byte)ich).ToString("X2"));
                    }
                    else
                        sb.Append(ch);
                }
                return sb.ToString();
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Creates a name. Can use any character up to 0xFF, except
        /// '\0'
        /// </summary>
        /// <param name="name"></param>
        public PdfName(string name)
        {
            //Lone / are allowed in the specs, but are to be ignored.
            if (name.Length == 0)
                throw new PdfNotSupportedException("Empty name");

            {
                //Names can not have a zero character, nor start with space
                if (name[0] == ' ' || name.IndexOf('\0') != -1)
                    throw new PdfReadException(PdfType.Name, ErrCode.Invalid);

                //Names with a / is allowed by the specs, but is more likely a bug in the lib.
                //Debug.Assert(name[0] != '/'); //See Issue 769 - 10.1.1.42.7749.pdf for a file that uses this (object id 550)
            }
            _name = name;
        }

        #endregion

        #region Override methods

        /// <summary>
        /// There's no integer representation for names
        /// </summary>
        public override int GetInteger() { throw new NotSupportedException(); }

        /// <summary>
        /// There's no real representation for names
        /// </summary>
        public override double GetReal() { throw new NotSupportedException(); }

        /// <summary>
        /// Returns string value
        /// </summary>
        public override string GetString() { return _name; }

        /// <summary>
        /// Gets the item itself.
        /// </summary>
        public override PdfItem Deref() { return this; }

        /// <summary>
        /// Names are immutable
        /// </summary>
        public override PdfItem Clone() { return this; }

        /// <summary>
        /// Writes itself to the stream
        /// </summary>
        /// <param name="write"></param>
        internal override void Write(Write.Internal.PdfWriter write)
        {
            write.WriteName(_name);
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null) return false;
            if (obj is PdfName)
                return GetString() == ((PdfName)obj).GetString();
            return false;
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            return string.Format("/{0}", _name);
        }

        #endregion

        #region ToType

        internal override PdfObject ToType(PdfType type, IntMsg msg, object obj)
        {
            switch(type)
            {
                case PdfType.ColorSpace:
                    return ColorSpace.PdfColorSpace.Create(_name, msg);

                case PdfType.Filter:
                    return Filter.PdfFilter.Create(_name);

                case PdfType.Encoding:
                    return new Font.PdfEncoding(_name);

                case PdfType.FunctionArray:
                    if (msg == IntMsg.Special && "Identity".Equals(_name))
                        return new PdfFunctionArray(new PdfIdentityFunction(1, 1), true);
                    goto default;

                case PdfType.Function:
                    if (msg == IntMsg.Message && "Identity".Equals(_name) && obj is PdfFunctionArray)
                        return new PdfIdentityFunction(1, 1);
                    goto default;

                case PdfType.Cmap:
                    return PdfCmap.Create(_name);

                case PdfType.SoftMask:
                    return new PdfNoMask();

                default:
                    return base.ToType(type, msg, obj);
            }
        }

        #endregion
    }
}
