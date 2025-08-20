using System;
using System.Collections.Generic;

namespace PdfLib.Util
{
    /// <summary>
    /// A weak cache does not hold onto the objects put into the cache, meaning
    /// they can be garbage collected when they're not used elsewhere
    /// </summary>
    /// <typeparam name="Key">A key object. Remeber to override "getHash" for best performance</typeparam>
    /// <typeparam name="Obj">Value object</typeparam>
    public class WeakCache<Key, Obj>
        where Obj:class
    {
        #region Variables and properties

        /// <summary>
        /// The cached items
        /// </summary>
        private Dictionary<Key, WeakReference> _dict;

        /// <summary>
        /// Fetches the cached object
        /// </summary>
        /// <param name="index">Key to the object</param>
        /// <returns>returns null if it's no longer in the cache</returns>
        public Obj this[Key index]
        {
            get
            {
                WeakReference wr;
                if (_dict.TryGetValue(index, out wr))
                {
                    var ret = wr.Target;
                    if (ret != null) return (Obj)ret;
                    _dict.Remove(index);
                }
                return null;
            }
            set
            {
                _dict[index] = new WeakReference(value);
            }
        }

        #endregion

        #region Init

        public WeakCache(int initial_capacity) { _dict = new Dictionary<Key, WeakReference>(initial_capacity); }

        #endregion

        public bool TryGetValue(Key key, out Obj value) 
        {
            WeakReference r;
            if (_dict.TryGetValue(key, out r))
            {
                value = (Obj) r.Target;
                return (value != null);
            }

            value = null;
            return false;
        }

        public void Clear() { _dict.Clear(); }
    }

    /// <summary>
    /// Similar to the weak cache, except here both
    /// keys and objects are weakly referenced.
    /// </summary>
    /// <typeparam name="Key"></typeparam>
    /// <typeparam name="Obj"></typeparam>
    public class WeakKeyCache<Key, Obj>
        where Key : class
        where Obj : class
    {
        #region Variables and properties

        /// <summary>
        /// The cached items
        /// </summary>
        private Dictionary<int, Bucket> _dict;

        /// <summary>
        /// Fetches the cached object
        /// </summary>
        /// <param name="index">Key to the object</param>
        /// <returns>returns null if it's no longer in the cache</returns>
        public Obj this[Key index]
        {
            get
            {
                int key = index.GetHashCode();
                Bucket bucket;
                if (_dict.TryGetValue(key, out bucket))
                {
                    var ret = bucket[index];
                    if (ret != null) return (Obj)ret;
                    _dict.Remove(key);
                }
                return null;
            }
            set
            {
                int key = index.GetHashCode();
                Bucket bucket;
                if (_dict.TryGetValue(key, out bucket))
                    bucket[index] = value;
                else
                {
                    bucket = new Bucket(1);
                    bucket.Add(index, value);
                    _dict.Add(key, bucket);
                }
            }
        }

        #endregion

        #region Init

        public WeakKeyCache() : this(10) { }
        public WeakKeyCache(int capacity) { _dict = new Dictionary<int, Bucket>(capacity); }

        #endregion

        public void Clear() { _dict.Clear(); }

        private class Bucket : List<KeyValuePair<WeakReference<Key>, WeakReference<Obj>>>
        {
            public Obj this[Key index]
            {
                get
                {
                    foreach (var kp in this)
                    {
                        var key = kp.Key.Target;
                        if (key == null)
                            Remove(kp);
                        else if (key == index)
                        {
                            var ret = kp.Value.Target;
                            if (ret == null)
                                Remove(kp);
                            return ret;
                        }
                    }
                    return null;
                }
                set { Set(index, value); }
            }

            #region Init

            public Bucket(int capacity)
                : base(capacity) { }

            #endregion

            #region Add

            public void Set(Key key, Obj obj)
            {
                foreach (var wr in this)
                    if (wr.Key == key)
                        throw new ArgumentException("A key with the same value already exists in the weak key cache");

                base.Add(new KeyValuePair<WeakReference<Key>, WeakReference<Obj>>(new WeakReference<Key>(key), new WeakReference<Obj>(obj)));
            }

            public void Add(Key key, Obj obj)
            {
                foreach (var wr in this)
                    if (wr.Key == key)
                        throw new ArgumentException("A key with the same value already exists in the weak key cache");
                    
                base.Add(new KeyValuePair<WeakReference<Key>, WeakReference<Obj>>(new WeakReference<Key>(key), new WeakReference<Obj>(obj)));
            }

            #endregion
        }
    }
#if DEBUG
    //Not fully implemented. Can't be used.
    public class WeakHashset<Obj>
        where Obj : class
    {
        private Dictionary<int, Bucket> _buckets;

        #region Init

        public WeakHashset() : this(10) { }

        public WeakHashset(int capacity)
        { _buckets = new Dictionary<int, Bucket>(capacity); }

        #endregion

        public void Add(Obj obj)
        {
            int key = obj.GetHashCode();
            if (!_buckets.TryGetValue(key, out _))
            {
                _ = new Bucket(1)
                {
                    obj
                };
            }
        }

        private class Bucket : List<WeakReference<Obj>>
        {
            public Bucket(int capacity)
                : base(capacity) { }

            public void Add(Obj obj)
            {
                foreach (var wr in this)
                    if (wr.Target == obj)
                        return;
                base.Add(new WeakReference<Obj>(obj));
            }
        }
    }
#endif
}
