using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// The catalog's name dictionary
    /// </summary>
    /// <remarks>See 7.7.4 in the specs</remarks>
    [PdfVersion("1.2")]
    public class PdfNameDictionary : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.NameDictionary; } }

        /// <summary>
        /// Mapping names to destinations
        /// </summary>
        public PdfNamedDests Dests { get { return (PdfNamedDests)_elems.GetPdfType("Dests", PdfType.NamedDests); } }

        /// <summary>
        /// Mapping names to embeded files
        /// </summary>
        [PdfVersion("1.4")]
        public PdfNamedFiles EmbeddedFiles { get { return (PdfNamedFiles)_elems.GetPdfType("EmbeddedFiles", PdfType.NamedFiles); } }

        #endregion

        #region Init

        internal PdfNameDictionary(PdfDictionary dict)
            : base(dict) { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfNameDictionary(elems);
        }

        #endregion
    }

    [PdfVersion("1.1")]
    public class PdfDestDictionary : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.DestDictionary; } }

        internal override SM DefSaveMode { get { return SM.Compressed; } }

        #endregion

        #region Init

        internal PdfDestDictionary(PdfDictionary dict)
            : base(dict) { }

        #endregion

        #region Indexing

        /// <summary>
        /// Retrive PdfObject from dictionary
        /// </summary>
        /// <param name="key">Key of the object</param>
        /// <returns>The PdfObject</returns>
        public PdfDestination this[string key]
        {
            get 
            { 
                var item =_elems.GetPdfType(key, PdfType.Destination);
                if (item == null) return null;
                if (item is PdfDestination)
                    return (PdfDestination)item;
                return ((PdfDestinationDict)item).D;
            }
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfDestDictionary(elems);
        }

        #endregion
    }
}
