using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Write.Internal
{
    /// <summary>
    /// Used when saving documents with StreamXref table. 
    /// 
    /// Objects are gruped into WritableObjStreams in accordance with their
    /// relations. Fonts for one page, for instance
    /// 
    /// This is not currently done though. Need to think up a sensible way
    /// of collecting relationship data. Perhaps do a page by page oriented
    /// search, or generate the data while compiling documents (in fact it
    /// will be collected when compiling documents, but presumably a peek
    /// into a page's recource dictionary will provide enough information
    /// by itself.)
    /// 
    /// Objects that can't be put in streams include:
    ///  - Stream
    ///  - Trailer dictionary
    ///  - Encryption information
    ///  - The length entery of this object
    ///  - Objects with a gen other than zero*
    ///    * This is irrelevant for PdfLib as it
    ///      makes no use of object generations.
    /// </summary>
    /// <remarks>
    /// Note that WritableObjStream is not tracked as it will only
    /// exists for a short while during a save to disk. They have
    /// no purpose while the document is in memory and are never
    /// created by a user of the libary.
    /// </remarks>
    internal class WritableObjStream : PdfObject, IRef
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.ObjStream; } }

        internal readonly List<WritableReference> Objects;

        /// <summary>
        /// Number of objects in the stream
        /// </summary>
        public int N { get { return Objects.Count; } }

        /// <summary>
        /// Used for updating the version information
        /// on the catalog
        /// </summary>
        internal int CatalogIndex = -1;

        #region IRef
        //Arguably, there's no need for the WritableObjStream to implement IRef.
        //It's a short lived object that only exists during saving, but it implements
        //both PdfObject and IRef to slip into existing code.
        //
        //If / when saving gets rewritten, this can be changed. It's the only showstopper
        //preventing IRef from extending ICRef. 

        /// <summary>
        /// If this object is indirect
        /// </summary>
        public bool HasReference { get { return ((IRef)this).Reference != null; } }

        /// <summary>
        /// This object's reference.
        /// </summary>
        WritableReference IRef.Reference { get; set; }

        /// <summary>
        /// Not used. 
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Ignore; } }

        #endregion

        #endregion

        #region Init

        /// <summary>
        /// Creates a empty ObjStream.
        /// </summary>
        public WritableObjStream()
        {
            Objects = new List<WritableReference>();
        }

        #endregion

        internal void Add(WritableReference item) { Objects.Add(item); }

        internal override void Write(PdfWriter write)
        {
            Catalog cat = new Catalog();
            cat.Add("Type", new PdfName("ObjStm"));
            cat.Add("N", new PdfInt(N));

            //Write objects to buffer
            var objects = new MemoryStream();
            PdfWriter w = new PdfWriter(objects, write.SaveMode, write.PaddMode, write.Compression, write.HeaderVersion);
            w.Precision = write.Precision;
            var xrefs = new ResTracker.XRefTable();
            xrefs.CatalogPos = CatalogIndex;
            xrefs.Table = Objects.ToArray();
            xrefs.Offsets = new long[xrefs.Table.Length];
            WritableDocument.WritePlainObjs(w, 0, xrefs);

            //Write list of offsets to another buffer or in padding space of first buffer
            StringBuilder sb = new StringBuilder();
            for (int c = 0; c < xrefs.Table.Length; c++)
            {
                sb.Append(string.Format("{0} {1} ", WritableReference.GetId(xrefs.Table[c].SaveString),
                    xrefs.Offsets[c]));
            }
            var indexes = new MemoryStream(sb.Length);
            w = new PdfWriter(indexes, SaveMode.Compressed, PM.None, CompressionMode.None, PdfVersion.V00);
            if (sb.Length == 0)
            {
                //Will have to set First and Length for this to be okay, but
                //empty object streams are never to be created anyway.
                throw new PdfInternalException("No items in Writable obj stream");
            }
            sb[sb.Length - 1] = '\n';
            w.WriteRaw(sb.ToString());

            //Update first, length to start of object data
            cat.Add("First", new PdfInt((int)indexes.Length));

            //compress both buffers into a byte array
            byte[] data;
            var one_stream = new Util.MultiStream(indexes, objects);
            if (true) //No filters.
            {
                data = new byte[(int)(one_stream.Length)];
                one_stream.Read(data, 0, data.Length);
            }
            else
            {
                //Set filter on stream
            }

            //Update Length to length of stream data
            cat.Add("Length", new PdfInt(data.Length));

            //Write out the stream
            write.WriteStream(cat, data);
        }

        public override string ToString()
        {
            return "ObjStm: "+Objects.Count;
        }
    }
}
