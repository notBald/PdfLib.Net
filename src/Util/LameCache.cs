using System;
using System.Collections.Generic;

namespace PdfLib.Util
{
    /// <summary>
    /// Intended as a drop in replacment for the WeakCache.
    /// </summary>
    public class LameCache<Key, Obj>
        where Obj:class
    {
        private Dictionary<Key, Obj> _dict;

        public Obj this[Key index]
        {
            get
            {
                Obj obj;
                if (_dict.TryGetValue(index, out obj))
                    return obj;
                return null;
            }
            set
            {
                _dict[index] = value;
            }
        }

        public LameCache(int initial_capacity) { _dict = new Dictionary<Key, Obj>(initial_capacity); }

        public bool TryGetValue(Key key, out Obj value)
        {
            return _dict.TryGetValue(key, out value);
        }

        public void Clear() { _dict.Clear(); }
    }
}
