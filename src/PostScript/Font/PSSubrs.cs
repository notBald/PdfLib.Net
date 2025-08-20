using System;
using System.Collections;
using System.Collections.Generic;
using PdfLib.PostScript.Primitives;
using PdfLib.PostScript.Internal;
using PdfLib.Read;

namespace PdfLib.PostScript.Font
{
    /// <summary>
    /// Post script charstrings. 
    /// </summary>
    public class PSSubrs : PSArray, IEnumerable<string>
    {
        #region Variables and properties

        int _lenIV;

        /// <summary>
        /// Must be set for decryption of charstrings to work
        /// </summary>
        public int lenIV { get { return _lenIV; } set { _lenIV = value; } }

        /// <summary>
        /// Fetch decryped CharStrings
        /// </summary>
        /// <param name="i">Index of subrutine</param>
        /// <returns>The decrypted subrutine</returns>
        new public byte[] this[int i]
        {
            get
            {
                var str = _items[i];
                if (!(str is PSString)) throw new PSCastException();

                return EexecStream.Decrypt(Lexer.GetBytes(((PSString) str).GetString()), 4330, _lenIV);
            }
        }

        #endregion

        #region Init

        internal PSSubrs(PSArray ar)
            : base(ar.GetAllItems())
        {

        }

        internal static PSSubrs Create(PSArray ar) { return new PSSubrs(ar); }

        #endregion

        #region IEnumerator

        public IEnumerator GetEnumerator() { return new Enumrer(this); }

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
        {
            return new Enumrer(this);
        }

        class Enumrer : IEnumerator<string>
        {
            readonly PSSubrs _l;
            int _pos = -1;
            public Enumrer(PSSubrs l) { _l = l; }
            object IEnumerator.Current 
            { get { return EexecStream.MakeReadable(_l[_pos]); } }
            public string Current
            { get { return (string)((IEnumerator)this).Current; } }
            public bool MoveNext() { return ++_pos < _l._items.Length; }
            public void Reset() { _pos = -1; }
            public void Dispose() { }
        }

        #endregion
    }
}
