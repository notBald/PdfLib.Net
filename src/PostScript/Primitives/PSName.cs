using System;
using System.Diagnostics;
using System.Text;
using PdfLib.Read;

namespace PdfLib.PostScript.Primitives
{

    /// <summary>
    /// A name in accordance with 7.3.5 of the PDF specs
    /// 
    /// (Betting it's simmilar enough to PostScript)
    /// </summary>
    /// <remarks>
    /// Note, this class casts PdfExceptions (for now)
    /// </remarks>
    public class PSName : PSItem
    {
        #region Variables and properties

        /// <summary>
        /// Name value
        /// </summary>
        readonly string _name;

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
                    Chars ich = (Chars)ch;
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
        public PSName(string name)
        {
            if (name.IndexOf('\0') != -1)
                throw new PdfCastException(Pdf.Internal.PdfType.Name, ErrCode.Invalid);// SR.InvalidName);
            if (name.Length == 0) //<-- Technically lone '/' are ignored I belive.
                throw new PdfNotSupportedException("Empty name");

            //Names with a / is allowed by the specs, but is more likely a bug in the lib.
            Debug.Assert(name[0] != '/');
            _name = name;
        }

        //Names I imagine to always be executable. At least that's how they're treated by this implementation
        public PSName(PSName name) { _name = name._name; Executable = name.Executable; }

        #endregion

        public override PSItem ShallowClone()
        {
            return new PSName(this);
        }

        /// <summary>
        /// Specs say that names can be compared with strings
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is PSName)
                return ((PSName)obj)._name.Equals(_name);
            else if (obj is PSString)
                return ((PSString)obj).GetString().Equals(_name);
            return false;
        }

        public override int GetHashCode()
        {
            return _name.GetHashCode();
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            return string.Format("/{0}", _name);
        }
    }
}
