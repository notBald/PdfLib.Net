using System;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using System.Security.Cryptography;
using PdfLib.Encryption;

namespace PdfLib.Pdf.Encryption
{
    /// <summary>
    /// Handles RC4 encrypted documents
    /// </summary>
    public sealed class PdfStandarSecuretyHandler : PdfEncryption
    {
        #region Variables and settings

        private BaseSecurityHandler _handler;

        /// <summary>
        /// If the owner password validated
        /// </summary>
        private bool _owner;

        /// <summary>
        /// If the owner password validated
        /// </summary>
        public bool OwnerAuthenticated => _owner;

        /// <summary>
        /// Securety handle revision
        /// </summary>
        public PdfEncryptionRevisions R { get { return (PdfEncryptionRevisions) _elems.GetUIntEx("R"); } }

        /// <summary>
        /// Data used to validate the owner password
        /// </summary>
        public byte[] O { get { return _elems.GetStringObjEx("O").ByteString; } }

        /// <summary>
        /// Data used to validate the user password
        /// </summary>
        public byte[] U { get { return _elems.GetStringObjEx("U").ByteString; } }

        /// <summary>
        /// A 32 byte string used to validate the owner password
        /// </summary>
        public byte[] OE { get { return _elems.GetStringObjEx("OE").ByteString; } }

        /// <summary>
        /// A 32 byte string  used to validate the user password
        /// </summary>
        public byte[] UE { get { return _elems.GetStringObjEx("UE").ByteString; } }

        /// <summary>
        /// Whenever metadata is encrypted
        /// </summary>
        public bool EncryptMetadata { get { return _elems.GetBool("EncryptMetadata", true); } }

        /// <summary>
        /// Object used for decryption
        /// </summary>
        internal BaseSecurityHandler Handler => _handler;

        /// <summary>
        /// Permissions
        /// </summary>
        public PdfUseAccessPermissions P { get { return (PdfUseAccessPermissions)((int)_elems.GetUIntEx("P") & 0xF3C); } }
        internal byte[] _P => _elems.GetRawLongEx("P");
        
        /// <summary>
        /// Encrypted permissions
        /// </summary>
        public byte[] Perms { get { return _elems.GetStringObjEx("Perms").ByteString; } }

        /// <summary>
        /// A dictionary over crypt filters
        /// </summary>
        public PdfCryptFilters CF { get { return (PdfCryptFilters)_elems.GetPdfType("CF", PdfType.CryptFilters); } }

        /// <summary>
        /// The name of the crypt filter that shall be used by default when decrypting streams.
        /// </summary>
        public string StmF { get { return _elems.GetName("StmF", "Identity"); } }

        #endregion

        #region Init

        internal PdfStandarSecuretyHandler(PdfDictionary elems)
            : base(elems)
        {
        }

        /// <summary>
        /// Must be run before this class can be used
        /// </summary>
        /// <param name="doc_id">The first ID of the document</param>
        /// <param name="user_pw">User password</param>
        /// <param name="owner_pw">Owner passoword</param>
        /// <returns>True if one of the passwords authenticated</returns>
        internal bool InitEncryption(PdfDocumentID doc_id, string user_pw, string owner_pw)
        {
            bool is_user;

            switch (V)
            {
                case PdfEncryptionModes.RC4_128:
                    var length = Length;
                    if (length < 40 || length > 128 || length % 8 != 0)
                        throw new PdfUnsuportedEncryption();
                    goto case PdfEncryptionModes.RC4_40;

                case PdfEncryptionModes.RC4_40:
                    _handler = new StandarSecurityHandlerRC40(doc_id, this, owner_pw, user_pw, out _owner, out is_user);
                    break;

                case PdfEncryptionModes.V3:
                case PdfEncryptionModes.V2:
                    var cfd = CF;
                    if (cfd == null) throw new PdfUnsuportedEncryption();
                    var cf = cfd["StdCF"];
                    if (cf == null) throw new PdfUnsuportedEncryption();

                    if (cf.CFM == PdfCryptFilterMode.AESV2)
                        _handler = new StandarSecurityHandlerAES128(doc_id, this, owner_pw, user_pw, out _owner, out is_user);
                    else if (cf.CFM == PdfCryptFilterMode.AESV3)
                        _handler = new StandarSecurityHandlerAES256(this, owner_pw, user_pw, out _owner, out is_user);
                    else if (cf.CFM == PdfCryptFilterMode.V2)
                        goto case PdfEncryptionModes.RC4_128;
                    else
                        throw new PdfUnsuportedEncryption();
                    break;


                default: throw new PdfUnsuportedEncryption();
            }

            return is_user;
        }

        #endregion

        #region Boilerplate
        /// <summary>
        /// Used when moving the element to another document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfStandarSecuretyHandler(elems);
        }
        #endregion
    }
}
