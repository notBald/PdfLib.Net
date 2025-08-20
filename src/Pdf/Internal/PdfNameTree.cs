using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Internal.Minor;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// A key value pare storage class that stores its data in a tree form
    /// </summary>
    /// <remarks>
    /// This serves as the root node of the tree
    /// </remarks>
    public abstract class PdfNameTree<T> : Elements
        where T:PdfItem
    {
        #region Variables and properties

        private readonly PdfType _child;

        internal override PdfType Type { get { return PdfType.NameTree; } }

        /// <summary>
        /// An array of indirect references to the children
        /// </summary>
        /// <remarks>Only pressent if "Names" is null</remarks>
        private NodeArray Kids { get { return (NodeArray)_elems.GetPdfType("Kids", PdfType.TreeNodeArray); } }

        /// <summary>
        /// A dictionary over items
        /// </summary>
        private DictArray Names { get { return (DictArray)_elems.GetPdfType("Names", PdfType.DictArray); } }

        #endregion

        #region Init

        internal PdfNameTree(PdfDictionary dict, PdfType child_type)
            : base(dict) { _child = child_type; }

        #endregion

        #region Required overrides

        #endregion

        #region Index

        public T this[string i]
        {

            get
            {
                var kids = Kids;
                PdfItem itm;
                if (kids != null)
                    itm = kids.Get(i, _child);
                else
                {
                    var names = Names;
                    if (names == null)
                        throw new PdfReadException(ErrSource.Dictionary, PdfType.DictArray, ErrCode.Missing);
                    itm = names.Get(i, _child);
                }
                if (itm == null) return null;
                return Cast(itm);
            }
        }

        protected abstract T Cast(PdfItem item);

        #endregion
    }
}
