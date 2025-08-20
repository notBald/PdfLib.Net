using System;
using PdfLib.Read;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using System.Security.Cryptography;
using PdfLib.Pdf.Encryption;
using PdfLib.Encryption;

namespace PdfLib.Pdf.Encryption
{
    /// <summary>
    /// Information about how a document is encrypted.
    /// </summary>
    [PdfVersion("1.1")]
    public abstract class PdfEncryption : Elements
    {
        #region Properties

        /// <summary>
        /// PdfType.Encrypt
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Encrypt; }
        }

        /// <summary>
        /// The name of the preferred security handler 
        /// for this document
        /// </summary>
        public string Filter { get { return _elems.GetNameEx("Filter"); } }

        /// <summary>
        /// A code specifying the algorithm to be used in 
        /// encrypting and decrypting the document 
        /// </summary>
        public PdfEncryptionModes V { get { return (PdfEncryptionModes) _elems.GetUInt("V", 0); } }

        /// <summary>
        ///  The length of the encryption key, in bits.
        /// </summary>
        public int Length { get { return _elems.GetUInt("Length", 40); } }

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        internal PdfEncryption(PdfDictionary dict)
            : base(dict)
        { }

        internal static PdfEncryption Create(PdfDictionary elems)
        {
            switch (elems.GetNameEx("Filter"))
            {
                case "Standard":
                    return new PdfStandarSecuretyHandler(elems);
                default:
                    throw new PdfUnsuportedEncryption(string.Format("Unkown filter: {0}", elems.GetNameEx("Filter")));
            }
        }

        #endregion
    }
}
