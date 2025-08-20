using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Pdf.Encryption
{
    public enum PdfEncryptionModes
    {
        RC4_40 = 1,
        RC4_128,
        V2 = 4,
        V3 = 5
    }

    public enum PdfEncryptionRevisions
    {
        RC4_40 = 2,
        RC4_128,
        AES_128,
        AES_256
    }

    /// <summary>
    /// The method used, if any, by the conforming reader to decrypt data.
    /// </summary>
    public enum PdfCryptFilterMode
    {
        /// <summary>
        /// The application shall not decrypt data but shall direct the input stream to the security handler for decryption
        /// </summary>
        None,

        /// <summary>
        /// The application shall ask the security handler for the encryption key and shall implicitly decrypt data using the RC4 algorithm.
        /// </summary>
        V2,

        /// <summary>
        /// The application shall ask the security handler for the encryption key and shall implicitly decrypt data using the AES algorithm.
        /// </summary>
        AESV2,

        /// <summary>
        /// The application shall ask the security handler for the encryption key and shall implicitly decrypt data using the AES algorithm.
        /// </summary>
        AESV3,

        /// <summary>
        /// Unsupported encryption method.
        /// </summary>
        Unsupported
    }
}
