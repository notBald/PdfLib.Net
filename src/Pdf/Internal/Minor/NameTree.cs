using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal.Minor
{
    /// <summary>
    /// This serves as leaf and intermediate nodes of the tree
    /// </summary>
    [DebuggerDisplay("{Limits}")]
    internal sealed class TreeNode : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.TreeNode; } }

        /// <summary>
        /// The least and greatest keys included in this node
        /// </summary>
        public PdfLimit Limits { get { return (PdfLimit)_elems.GetPdfTypeEx("Limits", PdfType.Limit); } }

        /// <summary>
        /// An array of indirect references to the children
        /// </summary>
        /// <remarks>Only pressent if "Names" is null</remarks>
        private NodeArray Kids { get { return (NodeArray)_elems.GetPdfType("Kids", PdfType.TreeNodeArray); } }

        /// <summary>
        /// A dictionary over items
        /// </summary>
        private DictArray Names { get { return (DictArray)_elems.GetPdfType("Names", PdfType.DictArray); } }

        /// <summary>
        /// If this is a leaf or intermediate node
        /// </summary>
        public bool IsLeafNode { get { return Kids == null; } }

        #endregion

        #region Init

        internal TreeNode(PdfDictionary dict)
            : base(dict) { }

        #endregion

        #region Index

        public bool Contains(string str)
        {
            return Limits.Contains(str);
        }

        public PdfItem Get(string index, PdfType child)
        {
            var kids = Kids;
            if (kids != null)
                return kids.Get(index, child);
            var names = Names;
            if (names == null)
                throw new PdfReadException(ErrSource.Dictionary, PdfType.DictArray, ErrCode.Missing);
            return names.Get(index, child);
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new TreeNode(elems);
        }

        #endregion
    }

    internal class NodeArray : TypeArray<TreeNode>
    {
        #region Variables and properties

        /// <summary>
        /// Returns PdfType.TreeNodeArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.TreeNodeArray; } }

        #endregion

        #region Init

        /// <summary>
        /// Create array with a collection of items.
        /// </summary>
        /// <param name="items">Filter items</param>
        /// <remarks>This method creates an adoptable filter array</remarks>
        public NodeArray(PdfItem[] items)
            : base(new TemporaryArray(items), PdfType.TreeNode)
        { }

        /// <summary>
        /// Create array with multiple item.
        /// </summary>
        /// <param name="items">Filter items</param>
        public NodeArray(PdfArray item)
            : base(item, PdfType.TreeNode)
        { }

        public NodeArray()
            : base(new PdfItem[0], PdfType.TreeNode)
        { }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new NodeArray(array);
        }

        #endregion

        #region Index

        public PdfItem Get(string index, PdfType child)
        {
            for (int c = 0; c < _items.Length; c++)
            {
                var node = this[c];
                if (node.Contains(index))
                    return node.Get(index, child);
            }

            throw new PdfReadException(PdfType.Item, ErrCode.Missing);
        }

        #endregion
    }

    /// <summary>
    /// An array that functions like a dictionary
    /// </summary>
    internal class DictArray : ItemArray
    {
        #region Variables and properties

        /// <summary>
        /// Returns PdfType.DictArray
        /// </summary>
        internal override PdfType Type { get { return PdfType.DictArray; } }

        #endregion

        #region Init

        /// <summary>
        /// Create array with multiple item.
        /// </summary>
        /// <param name="items">Filter items</param>
        internal DictArray(PdfArray item)
            : base(item)
        {
            if (item.Length % 2 != 0)
                throw new PdfCastException(ErrSource.General, PdfType.Array, ErrCode.Invalid);
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new DictArray(array);
        }

        #endregion

        #region Index

        public PdfItem Get(string i, PdfType child)
        {
            for (int c = 0; c < _items.Length; c += 2)
            {
                var key = _items[c].GetString();
                if (string.CompareOrdinal(key, i) == 0)
                    return _items.GetPdfTypeEx(c + 1, child);
            }

            //throw new PdfReadException(PdfType.Item, ErrCode.Missing);

            //I figure it's better to return null than to error.
            return null;
        }

        #endregion
    }

    public class PdfLimit : ItemArray
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.Limit; } }

        /// <summary>
        /// Lower bounds
        /// </summary>
        public string Low { get { return _items[0].GetString(); } }

        /// <summary>
        /// Higher bounds
        /// </summary>
        public string High { get { return _items[1].GetString(); } }

        #endregion

        #region Init

        internal PdfLimit(PdfArray ar)
            : base(ar)
        {
            if (ar.Length != 2 || !(ar[0] is PdfString && ar[1] is PdfString))
                throw new PdfCastException(ErrSource.General, PdfType.Array, ErrCode.Invalid);
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new PdfLimit(array);
        }

        #endregion

        public bool Contains(string str)
        {
            int cmp = string.CompareOrdinal(str, Low);
            if (cmp < 0) return false;
            if (cmp == 0) return true;
            return string.CompareOrdinal(str, High) <= 0;
        }

        public override string ToString()
        {
            return Low + " - " + High;
        }
    }
}
