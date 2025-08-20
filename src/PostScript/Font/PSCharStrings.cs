using System;
using System.Collections;
using System.Collections.Generic;
using PdfLib.PostScript.Primitives;
using PdfLib.PostScript.Internal;
using PdfLib.Read;

namespace PdfLib.PostScript.Font
{
    /// <summary>
    /// The CharStrings dictionary holds a collection of name-procedure
    /// pairs. The procedures to which the names refer produce the font’s
    /// character outlines. Character procedures can also call subroutines
    /// (located in the Private dictionary) that produce similar parts of
    /// characters, thus reducing storage requirements. The charstring
    /// procedures also contain character level hints.
    /// </summary>
    public class PSCharStrings : PSDictionary, IEnumerable<string>
    {
        #region Variables and properties

        int _lenIV;

        /// <summary>
        /// Must be set for decryption of charstrings to work
        /// </summary>
        public int lenIV { get { return _lenIV; } set { _lenIV = value; } }

        new public byte[] this[String name]
        {
            get 
            {
                var data = GetStr(name);
                return EexecStream.Decrypt(Lexer.GetBytes(data ?? GetStrEx(".notdef")), 4330, _lenIV);
            }
        }

        #endregion

        #region Init

        public PSCharStrings(PSDictionary dict)
            : base(dict) { }

        internal static PSCharStrings Create(PSDictionary dict) { return new PSCharStrings(dict); }

        public override PSItem ShallowClone()
        {
            return new PSCharStrings(this);
        }

        #endregion

        #region IEnumerator

        public IEnumerator GetEnumerator() { return new Enumrer(this); }

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
        {
            return new Enumrer(this);
        }

        class Enumrer : IEnumerator<string>
        {
            readonly PSCharStrings _l;
            readonly IEnumerator _e;
            public Enumrer(PSCharStrings l) { _l = l; _e = l.Catalog.GetEnumerator(); }
            object IEnumerator.Current 
            { 
                get 
                { 
                    var kp = (KeyValuePair<string, PSItem>)_e.Current;
                    return kp.Key + ": "+EexecStream.MakeReadable(_l[kp.Key]); 
                } 
            }
            public string Current
            { get { return (string) ((IEnumerator)this).Current; } }
            public bool MoveNext() { return _e.MoveNext(); }
            public void Reset() { _e.Reset(); }
            public void Dispose() { }
        }

        #endregion
    }
}
