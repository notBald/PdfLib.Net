using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Render.Font.TT
{
    internal abstract class Table
    {
        #region Variables and properties

        /// <summary>
        /// Parent directory
        /// </summary>
        protected readonly TableDirectory _td;

        /// <summary>
        /// If this table's checksum is correct
        /// </summary>
        public abstract bool Valid { get; }

        #endregion

        #region Init

        public Table(TableDirectory td) { _td = td; }

        #endregion
    }
}
