using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Internal
{
    /// <summary>
    /// Callback for creating a container
    /// </summary>
    delegate C ContCreate<V, C>(V val);

    /// <summary>
    /// Callback for taking a value out of a container
    /// </summary>
    delegate V ValCreate<V, C>(C cont);

    /// <summary>
    /// Array class that can cast inner values straight into the desired form
    /// </summary>
    /// <typeparam name="C">Container type</typeparam>
    /// <typeparam name="V">Value type</typeparam>
    public sealed class PSValArray<V, C> : PSArray
        where C:PSItem
    {
        #region Variables

        readonly ContCreate<V, C> _cc;

        readonly ValCreate<V, C> _vc;

        #endregion

        #region Init

        /// <summary>
        /// Create array from array
        /// </summary>
        internal PSValArray(PSItem[] items, ContCreate<V, C> CC, ValCreate<V, C> VC)
            : base(items)
        {
            _cc = CC; _vc = VC;
        }

        internal PSValArray(PSArray ar, ContCreate<V, C> CC, ValCreate<V, C> VC)
            : base(ar.GetAllItems())
        {
            _cc = CC; _vc = VC;
            Access = ar.Access;
        }


        public override PSItem ShallowClone()
        {
            var ret = new PSValArray<V, C>(_items, _cc, _vc);
            ret.Access = Access;
            ret.Executable = Executable;
            return ret;
        }

        #endregion

        new public V this[int i]
        {
            set { _items[i] = _cc(value); }
            get { return _vc((C) _items[i]); }
        }
    }

    /// <summary>
    /// Array class that casts its inner values into the desired type
    /// </summary>
    /// <typeparam name="V">Type the items will be cast into when read</typeparam>
    public sealed class PSArray<V> : PSArray
        where V : PSItem
    {
        #region Properties

        new public V this[int i]
        {
            get
            {
                var ret = _items[i];
                if (!(ret is V)) throw new PSCastException();
                return (V)ret;
            }
        }

        #endregion

        #region Init

        public PSArray(PSArray ar)
            : base(ar.GetAllItems())
        { Access = ar.Access; Executable = ar.Executable; }

        public override PSItem ShallowClone()
        {
            return new PSArray<V>(this);
        }

        internal static PSArray<V> Create(PSArray ar)
        {
            return new PSArray<V>(ar);
        }

        #endregion
    }
}
