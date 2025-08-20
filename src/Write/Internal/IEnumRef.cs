using System;
using System.Collections;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Must be implemented by all writable objects with child
    /// references.
    /// </summary>
    /// <remarks>
    /// Implementation note:
    /// Objects that implements IEnumRef must also implement ICRef. The
    /// only exception are references, which must never implement ICRef
    /// 
    /// If one don't implement IKRef, the object is assumed to be immutable
    /// </remarks>
    internal interface IEnumRef
    {
        bool HasChildren { get; }

        /// <summary>
        /// Gets a reference enumerable with all child references
        /// </summary>
        /// <remarks>
        /// Debugger seems to be unable to enumerate this property 
        /// at times.  
        /// 
        /// If it gives an error about missing an assembly reference 
        /// there's not necessarily anything wrong.
        /// </remarks>
        IEnumRefEnumerable RefEnumerable { get; }
    }

    /// <summary>
    /// Problem is, implementors of IEnumRef may implement IEnumerator already.
    /// So using this glue interface.
    /// </summary>
    internal interface IEnumRefEnumerable : IEnumerator<IEnumRef>, IEnumerable<IEnumRef>
    { }

    /// <summary>
    /// Standar implementation
    /// </summary>
    /// <remarks>Interface Enumerable Array Implementation</remarks>
    internal class IEnumRefArImpl : IEnumRefEnumerable
    {
            int _i = -1;
            readonly IEnumRef[] _refs;
            public IEnumRefArImpl(IEnumRef[] refs) { _refs = refs; }
            public IEnumRefArImpl(IEnumRef refs) { _refs = new IEnumRef[] { refs }; }
            public IEnumRefArImpl() { _refs = new IEnumRef[0]; }
            object IEnumerator.Current { get { return _refs[_i]; } }
            public IEnumRef Current { get { return _refs[_i]; } }
            public bool MoveNext() { return ++_i < _refs.Length; }
            public void Reset() { _i = -1; }
            public void Dispose() { }
            IEnumerator IEnumerable.GetEnumerator() { return this; }
            IEnumerator<IEnumRef> IEnumerable<IEnumRef>.GetEnumerator()
            { return this; }
    }

    /// <summary>
    /// Standar implementation
    /// </summary>
    /// <remarks>Interface Enumerable Reference Enumerable Implementation</remarks>
    internal class IEnumRefEnImpl : IEnumRefEnumerable
    {
        IEnumRef _current;
        readonly IEnumerator _items;
        public IEnumRefEnImpl(IEnumerator items) { _items = items; }
        object IEnumerator.Current { get { return _current; } }
        public IEnumRef Current { get { return _current; } }
        public bool MoveNext()
        {
            while (_items.MoveNext())
            {
                var cur = _items.Current;
                if (cur is IEnumRef)
                {
                    _current = (IEnumRef)cur;
                    return true;
                }
            }
            return false;
        }
        public void Reset() { _items.Reset(); }
        public void Dispose() { }
        IEnumerator IEnumerable.GetEnumerator() { return this; }
        IEnumerator<IEnumRef> IEnumerable<IEnumRef>.GetEnumerator()
        { return this; }
    }
}
