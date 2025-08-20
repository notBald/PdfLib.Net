using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Internal
{
    /// <summary>
    /// Delegate that knows how to create new objects
    /// </summary>
    public delegate T PSDictCreate<T>(PSDictionary dict);

    /// <summary>
    /// There's no type system in use in PS classes, but I prefere
    /// that similar classes have similar names.
    /// </summary>
    /// <typeparam name="T">Target class</typeparam>
    public class PSTypeDict<T> : PSDictionary
        where T : PSDictionary
    {
        #region Variables

        readonly PSDictCreate<T> _creator;

        #endregion

        #region Properties

        new public T this[String key]
        {
            get 
            {
                var itm = (PSDictionary) Catalog[key];
                if (itm is T) return (T) itm;
                var t = _creator(itm);
                Catalog[key] = t;
                return t;
            }
        }

        public IEnumerable<KeyValuePair<string, T>> Enum { get { return new EnumImpl(this); } }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        public PSTypeDict(PSDictionary dict, PSDictCreate<T> Creator)
            : base(dict.Catalog)
        { Access = dict.Access; _creator = Creator; Executable = dict.Executable; }

        #endregion

        class EnumImpl : IEnumerable<KeyValuePair<string, T>>
        {
            PSTypeDict<T> dict;

            public System.Collections.IEnumerator GetEnumerator()
            {
                return ((IEnumerable<KeyValuePair<string, T>>)this).GetEnumerator();
            }

            IEnumerator<KeyValuePair<string, T>> IEnumerable<KeyValuePair<string, T>>.GetEnumerator()
            {
                var keys = new string[dict.Catalog.Count];
                dict.Catalog.Keys.CopyTo(keys, 0);
                foreach (var key in keys)
                {
                    var item = dict[key];
                    yield return new KeyValuePair<string, T>(key, item);
                }
            }

            public EnumImpl(PSTypeDict<T> d)
            { dict = d; }
        }

        public bool TryGetValue(string key, out T val)
        {
            PSItem item;
            if (Catalog.TryGetValue(key, out item))
            {
                if (item is T)
                    val = (T)item;
                else
                {
                    var t = _creator((PSDictionary)item);
                    Catalog[key] = t;
                    val = t;
                }
                return true;
            }
            val = null;
            return false;
        }

        public override PSItem ShallowClone()
        {
            return new PSTypeDict<T>(this, _creator);
        }
    }
}
