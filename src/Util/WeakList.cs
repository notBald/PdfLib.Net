using System;
using System.Collections.Generic;

namespace PdfLib.Util
{
    /// <summary>
    /// Lists over weakly references objects
    /// </summary>
    /// <remarks>
    /// This class is semi-thread safe. This since it may be used
    /// from a deconstructor, which resides on a sepperate thread.
    /// 
    /// locks on "this" instead of a private lock object since clients might 
    /// wish to lock the list and use one of the base methods.
    /// </remarks>
    public class WeakList<T> : List<KeyValuePair<int, WeakReference<T>>>
        where T:class
    {
        public delegate void Obj(T obj);

        public WeakList()
        { }

        public WeakList(int capacity)
            : base(capacity)
        { }

        public void Add(T obj)
        {
            lock (this)
            {
                Add(new KeyValuePair<int, WeakReference<T>>(obj.GetHashCode(), new WeakReference<T>(obj)));
            }
        }

        public void Remove(T obj)
        {
            int count = Count;
            int hash = obj.GetHashCode();
            lock (this)
            {
                for (int c = 0; c < count; c++)
                {
                    var kp = this[c];
                    var val = kp.Value.Target;
                    if (val == null || kp.Key == hash && ReferenceEquals(obj, val))
                    {
                        base.RemoveAt(c--);
                        count--;
                    }
                }
            }
        }

        public void Itterate(Obj func)
        {
            lock (this)
            {
                foreach (var kp in this)
                {
                    var v = kp.Value.Target;
                    if (v != null)
                        func(v);
                }
            }
        }
    }
}
