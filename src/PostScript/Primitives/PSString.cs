using System;
using PdfLib.Read;
using PdfLib.Write.Internal;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Represents a string in the PS file
    /// </summary>
    /// <remarks>
    /// There are three types of string in PostScript,
    /// currently only two are supported and I'm blindly
    /// using PDF string rules in each case. 
    /// </remarks>
    public class PSString : PSObject
    {
        #region Variables

        /// <summary>
        /// String data. Needs to be a char[] so that the characters can
        /// be updated. (C# strings are immutable)
        /// </summary>
        private readonly char[] _value;

        /// <summary>
        /// Gets the string as bytes
        /// </summary>
        public byte[] ByteString { get { return Lexer.GetBytes(GetString()); } }

        /// <summary>
        /// Length of the string
        /// </summary>
        public int Length { get { return _value.Length; } }

        public char this[int i]
        {
            get { return _value[i]; }
            set { _value[i] = value; }
        }

        #endregion

        #region Init

        /// <summary>
        /// Creates new string from data in the parser
        /// </summary>
        /// <param name="p">Parser to create string from</param>
        public PSString(byte[] value, bool hex)
        {
            if (hex)
                _value = Lexer.HexString(value, 0, value.Length).ToCharArray();
            else
                _value = Lexer.PDFString(value, 0, value.Length).ToCharArray();
        }

        internal PSString(char[] str)
        { _value = str; }

        internal PSString(PSString str)
            : base(str)
        { _value = str._value;}

        #endregion

        /// <summary>
        /// Returns string value
        /// </summary>
        public string GetString() { return new String(_value); }

        /// <remarks>
        /// If one set the readonly flag on a string, a shallow copy is made.
        /// If one then alter the original string, should one alter the read
        /// only version as well? For now I let the chips lay where they fall
        /// </remarks>
        public override PSItem ShallowClone()
        {
            return new PSString(this);
        }

        internal override void Restore(PSItem obj)
        {
            var s = (PSString)obj;
            Access = s.Access;
        }

        public int GreaterThan(PSString str)
        {
            var ca = str._value;
            for (int c = 0; c < ca.Length; c++)
            {
                //If this string is shorter, but otherwise identical,
                //then this is a lesser string
                if (c == _value.Length) break;

                var b1 = (byte)ca[c];
                var b2 = (byte)ca[c];
                if (b1 != b2) return b1 - b2;
            }

            //We know that _value.length is either longer
            //or equal. 
            return _value.Length - ca.Length;
        }

        /// <summary>
        /// Specs say that names can be compared with strings
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is PSName)
                return ((PSName)obj).Value.Equals(GetString());
            else if (obj is PSString)
                return ((PSString)obj).GetString().Equals(GetString());
            return false;
        }

        public override int GetHashCode()
        {
            return GetString().GetHashCode();
        }

        /// <summary>
        /// String representation of the string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return GetString().Replace('\0', ' ');
        }
    }
}
