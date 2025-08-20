using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Pdf.Internal
{
    public sealed class SealedDocument : PdfDocument
    {
        #region Variables and properties

        /// <summary>
        /// The pdf file this document resides in
        /// </summary>
        private readonly PdfFile _owner;

        /// <summary>
        /// Disk file for this document, if any
        /// </summary>
        public override PdfFile File { get { return _owner; } }

        public override PdfInfo  Info
        {
            get
            {
                return _owner.Info;
            }
        }

        public override bool IsWritable => false;

        #endregion

        #region Init

        /// <summary>
        /// Document constructor
        /// </summary>
        /// <param name="owner">Owner of the document</param>
        /// <param name="root">Root node</param>
        internal SealedDocument(PdfFile owner, PdfReference root)
            : base(root)
        {
            _owner = owner;
        }

        /// <summary>
        /// Disposes PDF file
        /// </summary>
        protected override void DisposeImpl()
        {
            _owner.Dispose();
        }

        #endregion

        /// <summary>
        /// Removes cached object from memory.
        /// </summary>
        /// <remarks>
        /// Except the trailer, root and root pages.
        /// 
        /// Note that when the cache is flushed any object held
        /// outside the document will now be "on it's own." I.e.
        /// can't use ReferenceEquals() to determine equality.
        /// </remarks>
        [DebuggerStepThrough]
        public void FlushCache()
        {
            _owner.FlushCache();

            //Cached pages object must be dropped too.
            _pages = null;
        }

        [DebuggerStepThrough]
        internal override void FixXRefTable()
        {
            _owner.FixXRefTable();
        }

        /// <summary>
        /// Loads as much data as it can into the cache.
        /// </summary>
        /// <returns>True if there was no errors when
        /// loading the data into the cache</returns>
        public override bool LoadIntoMemory(bool load_streams)
        {
            lock (_owner) { return _owner.LoadAllResources(load_streams); }
        }
    }
}
