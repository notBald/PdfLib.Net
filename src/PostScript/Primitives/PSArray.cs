using System.Collections.Generic;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Implements a PostScript array
    /// </summary>
    /// <remarks>
    /// PostScript says that arrays are only equal if
    /// they have the same reference. So don't override
    /// "Equals"
    /// </remarks>
    public class PSArray : PSObject
    {
        #region Variables

        /// <summary>
        /// The items in the array
        /// </summary>
        protected PSItem[] _items;

        #endregion

        #region Properties

        public int Length { get { return _items.Length; } }

        /// <summary>
        /// Gets item from array. Don't use from PostScript as Access is not checked.
        /// </summary>
        public PSItem this[int i]
        {
            set { _items[i] = value; }
            get { return _items[i]; }
        }

        #endregion

        #region Init

        /// <summary>
        /// Create an array from a string array
        /// </summary>
        public PSArray(string[] items, bool name)
        {
            _items = new PSItem[items.Length];
            if (name)
            {
                for (int c = 0; c < items.Length; c++)
                {
                    var itm = items[c];
                    if (itm == null)
                        _items[c] = PSNull.Value;
                    else
                        _items[c] = new PSName(itm);
                }
            }
            else
            {
                for (int c = 0; c < items.Length; c++)
                {
                    var itm = items[c];
                    if (itm == null)
                        _items[c] = PSNull.Value;
                    else
                        _items[c] = new PSString(itm.ToCharArray());
                }
            }
        }

        /// <summary>
        /// Create array from list
        /// </summary>
        public PSArray(List<PSItem> items)
        {
            _items = items.ToArray();
        }

        /// <summary>
        /// Create array from array
        /// </summary>
        public PSArray(PSItem[] items)
        {
            _items = items;
        }

        public override PSItem ShallowClone()
        {
            var ar = new PSArray((PSItem[]) _items.Clone());
            ar.Executable = Executable;
            return ar;
        }

        internal override void Restore(PSItem obj)
        {
            var psa = (PSArray)obj;
            Access = psa.Access;
            _items = psa._items;
        }

        #endregion

        /// <summary>
        /// For PostScript use
        /// </summary>
        internal PSItem GetPS(int i)
        {
            if (Access > PSAccess.ReadOnly) throw new PSParseException(PSType.None, ErrCode.Illegal);
            return _items[i]; 
        }
        internal void SetPS(int i, PSItem val)
        {
            if (Access != PSAccess.Unlimited) throw new PSParseException(PSType.None, ErrCode.Illegal);
            _items[i] = val;
        }

        internal PSItem[] GetAllItems() { return _items; }

        /// <summary>
        /// Turns an array into an array of strings
        /// </summary>
        /// <param name="name">If the array consists of name objects</param>
        /// <returns></returns>
        internal string[] ToArray(bool name)
        {
            var ar = new string[_items.Length];
            if (name)
            {
                for (int c = 0; c < ar.Length; c++)
                    ar[c] = ((PSName)_items[c]).Value;
            }
            else
            {
                for (int c = 0; c < ar.Length; c++)
                    ar[c] = ((PSString)_items[c]).GetString();
            }
            return ar;
        }
    }
}
