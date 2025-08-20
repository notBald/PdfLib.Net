using System;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using System.Security.Cryptography;
using PdfLib.Encryption;

namespace PdfLib.Pdf.Encryption
{
    internal abstract class BaseSecurityHandler
    {
        internal abstract void SetEncryptionFor(PdfObjID id);

        /// <summary>
        /// Decrypts data using the current decryption key
        /// </summary>
        /// <param name="bytes">Data to decrypt</param>
        /// <returns>Decrypted data</returns>
        internal abstract byte[] Decrypt(byte[] bytes);

        /// <summary>
        /// Creates a decryption wrapper for a stream
        /// </summary>
        /// <param name="source">Source for the stream</param>
        /// <returns>Wrapper</returns>
        internal abstract ISource CreateDecryptWrapper(ISource source);

        internal abstract object Save();
        internal abstract void Restore(object key);
    }

    internal abstract class StandarSecurityHandler : BaseSecurityHandler
    {
        /// <summary>
        /// Key to use for decrypting the document. 
        /// </summary>
        protected byte[] _global_encryption_key;

        /// <summary>
        /// This is the encryption key used for induvidual objects and
        /// is made from the object ID and the _global_encryption_key
        /// </summary>
        protected byte[] _encryption_key;

        private readonly MD5 _md5;
        private readonly bool _add_salt;
        private static readonly byte[] _salt = new byte[] { 0x73, 0x41, 0x6C, 0x54 };

        protected StandarSecurityHandler(PdfDocumentID id, PdfStandarSecuretyHandler std, bool add_salt, string owner_pw, string user_pw, out bool is_owner, out bool is_user)
        {
            var state = new EncryptionState();
            state.UserBytes = std.U;
            state.OwnerBytes = std.O;
            state.DocumentID = id.First;
            state.Permission = std._P;
            state.Revision = std.R;
            state.EncryptMetadata = std.EncryptMetadata;
            state.OwnerPassword = PadPassword(owner_pw);
            state.UserPassword = PadPassword(user_pw);
            state.KeyLength = std.Length / 8;
            _md5 = new MD5CryptoServiceProvider();

            is_owner = AuthenticateOwner(state, _md5);
            is_user = is_owner || AuthenticateUser(PadPassword(user_pw), state, _md5);
            

            _global_encryption_key = state.RC4Key;
            _encryption_key = new byte[Math.Min(_global_encryption_key.Length + 5, 16)];
            
            _add_salt = add_salt;
        }

        #region Save and restore key
        //Thought used if one need to decrypt parts of one
        //object, part of another and back again. Perhaps
        //the shuld be dropped.

        /// <summary>
        /// For saving the current key
        /// </summary>
        internal override object Save()
        {
            return _encryption_key;
        }

        /// <summary>
        /// Restores an old key
        /// </summary>
        internal override void Restore(object key)
        { _encryption_key = (byte[])key; }

        #endregion

        #region Compute encryption keys

        /// <summary>
        /// Implements algorithm 2 of the PDF specs
        /// </summary>
        /// <remarks>Used for computing a encryption key</remarks>
        private static void ComputeKey(byte[] password, EncryptionState state, MD5 md5)
        {
            md5.Initialize();

            //Step b. Pass the padded password to the hash function
            md5.TransformBlock(password, 0, password.Length, null, 0);

            //Step c. Pass the O entery to the hash function
            md5.TransformBlock(state.OwnerBytes, 0, state.OwnerBytes.Length, null, 0);

            //Step d. Pass the permissions to the hash
            md5.TransformBlock(state.Permission, 0, 4, null, 0);

            //Step e. Padd the id to the hash
            md5.TransformBlock(state.DocumentID, 0, state.DocumentID.Length, null, 0);

            //Step f. Add 0xFFFFFFFF
            if (state.Revision >= PdfEncryptionRevisions.AES_128 && !state.EncryptMetadata)
            {
                var ff = Read.Lexer.GetBytesLE(0xFFFFFFFF);
                md5.TransformBlock(ff, 0, 4, null, 0);
            }

            //Step g. Finalize the hash.
            md5.TransformFinalBlock(state.DocumentID, 0, 0);
            var hash = md5.Hash;

            //Step h. Rehash 50 times
            if (state.Revision >= PdfEncryptionRevisions.RC4_128)
            {
                for (int c = 0; c < 50; c++)
                {
                    md5.Initialize();
                    hash = md5.ComputeHash(hash, 0, state.KeyLength);
                }
            }

            //Step i.
            state.RC4Key = GetBytes(hash, state.KeyLength);
        }

        /// <summary>
        /// Algorithm 3: Compute the O value (OwnerBytes)
        /// </summary>
        private static byte[] ComputeO(EncryptionState state, bool stop_after_d, MD5 md5)
        {
            md5.Initialize();

            //Step b. Input password to hash function
            var hash = md5.ComputeHash(state.OwnerPassword);

            if (state.Revision >= PdfEncryptionRevisions.RC4_128)
            {
                //Step c. Computes the hash 50 times
                for (int c = 0; c < 50; c++)
                {
                    md5.Initialize();
                    hash = md5.ComputeHash(hash);
                }
            }

            //Step d. Gets the RC4 key from the hash
            state.RC4Key = GetBytes(hash, state.KeyLength);

            if (stop_after_d) return null;

            //Step f.
            var o = RC4.Encrypt(state.RC4Key, state.UserPassword);

            //Step g.
            if (state.Revision >= PdfEncryptionRevisions.RC4_128)
            {
                var key = new byte[state.RC4Key.Length];
                for (int c = 1; c < 20; c++)
                {
                    for (int i = 0; i < state.RC4Key.Length; i++)
                        key[i] = (byte)(state.RC4Key[i] ^ c);
                    o = RC4.Encrypt(key, o);
                }
            }

            return o;
        }

        /// <summary>
        /// Algorithm 4 and 5
        /// </summary>
        private static byte[] ComputeU(byte[] password, EncryptionState state, MD5 md5)
        {
            //Step a. Creates an encryption key
            ComputeKey(password, state, md5);

            if (state.Revision == PdfEncryptionRevisions.RC4_40)
            {
                //Step b. Encrypt the password with the key
                return RC4.Encrypt(state.RC4Key, EncryptionState.Padding);
            }
            else
            {
                //Step b.
                md5.Initialize();
                md5.TransformBlock(EncryptionState.Padding, 0, password.Length, null, 0);

                //Step c.
                md5.TransformFinalBlock(state.DocumentID, 0, state.DocumentID.Length);

                //Step d.
                var hash = RC4.Encrypt(state.RC4Key, md5.Hash);

                //Step e.
                byte[] key = new byte[state.KeyLength];
                for (int c = 1; c <= 19; c++)
                {
                    for (int i = 0; i < key.Length; i++)
                        key[i] = (byte)(state.RC4Key[i] ^ c);
                    hash = RC4.Encrypt(key, hash);
                }

                //Step f. Arbitrary padding
                byte[] padded = new byte[32];
                Buffer.BlockCopy(hash, 0, padded, 0, 16);
                Buffer.BlockCopy(hash, 0, padded, 16, 16);

                return padded;
            }
        }

        #endregion

        #region Autenticate users

        /// <summary>
        /// Algorithm 6
        /// </summary>
        private static bool AuthenticateUser(byte[] password, EncryptionState state, MD5 md5)
        {
            var user = ComputeU(password, state, md5);

            if (state.Revision == PdfEncryptionRevisions.RC4_40)
                return Util.ArrayHelper.ArraysEqual<byte>(user, state.UserBytes);
            else
            {
                var u = state.UserBytes;
                for (int c = 0; c < 16; c++)
                    if (user[c] != u[c])
                        return false;
                return true;
            }
        }

        /// <summary>
        /// Algorithm 7, for authentication of the owner password
        /// </summary>
        /// <returns>The user key</returns>
        private static bool AuthenticateOwner(EncryptionState state, MD5 md5)
        {
            //Step a-d of algorithm3
            ComputeO(state, true, md5);
            byte[] user_password;

            if (state.Revision == PdfEncryptionRevisions.RC4_128)
                user_password = RC4.Encrypt(state.RC4Key, state.OwnerBytes);
            else
            {
                user_password = state.OwnerBytes;
                byte[] key = new byte[state.RC4Key.Length];
                for (int c = 0; c < 20; c++)
                {
                    for (int i = 0; i < key.Length; i++)
                        key[i] = (byte)(state.RC4Key[i] ^ c);

                    user_password = RC4.Encrypt(key, user_password);
                }
            }

            return AuthenticateUser(user_password, state, md5);
        }

        #endregion

        #region Object decryption functions
        //Each object has it's own decryption key. It's generated
        //using the object's id, and must be set before decrypting
        //contents in a object or that object's stream

        /// <summary>
        /// Each object uses its own encryption key
        /// </summary>
        /// <param name="id">Object to set the key for</param>
        internal override void SetEncryptionFor(PdfObjID id)
        {
            //Copies the object id into an array
            byte[] bid = new byte[5];
            //id takes the first 3 bytes
            bid[0] = (byte)id.Nr;
            bid[1] = (byte)(id.Nr >> 8);
            bid[2] = (byte)(id.Nr >> 16);
            //gnr takes the last two bytes
            bid[3] = (byte)id.Gen;
            bid[4] = (byte)(id.Gen >> 8);

            //Hash it.
            _md5.Initialize();
            _md5.TransformBlock(_global_encryption_key, 0, _global_encryption_key.Length, null, 0);
            if (_add_salt)
            {
                _md5.TransformBlock(bid, 0, 5, null, 0);
                _md5.TransformFinalBlock(_salt, 0, 4);
            }
            else
                _md5.TransformFinalBlock(bid, 0, 5);


            //And set the result as the encryption key
            _encryption_key = new byte[_encryption_key.Length];
            Buffer.BlockCopy(_md5.Hash, 0, _encryption_key, 0, _encryption_key.Length);
        }


        #endregion

        #region helper functions

        private static byte[] GetBytes(byte[] b, int l)
        {
            var ret = new byte[l];
            Buffer.BlockCopy(b, 0, ret, 0, l);
            return ret;
        }

        /// <summary>
        /// The password is to be padded to 32 bytes (7.6.3.3)
        /// </summary>
        private byte[] PadPassword(string password)
        {
            if (password == null)
                password = "";
            var pw = Read.Lexer.GetBytes(password);
            var padded = new byte[32];
            var lenght = Math.Min(32, pw.Length);
            Buffer.BlockCopy(pw, 0, padded, 0, lenght);
            Buffer.BlockCopy(EncryptionState.Padding, 0, padded, lenght, 32 - lenght);
            return padded;
        }

        #endregion

        #region helper classes

        /// <summary>
        /// This class holds all data needed for the encryption functions
        /// </summary>
        private class EncryptionState
        {
            /// <summary>
            /// A string based on the user's password
            /// </summary>
            public byte[] UserBytes;

            /// <summary>
            /// A string based on the owner's password
            /// </summary>
            public byte[] OwnerBytes;

            /// <summary>
            /// The first ID of the document
            /// </summary>
            public byte[] DocumentID;

            /// <summary>
            /// The permission value as a little endian byte array
            /// </summary>
            public byte[] Permission;

            /// <summary>
            /// Securety handler revision
            /// </summary>
            public PdfEncryptionRevisions Revision;

            /// <summary>
            /// Whenever metadata is encrypted.
            /// </summary>
            public bool EncryptMetadata;

            /// <summary>
            /// The padded owner password
            /// </summary>
            public byte[] OwnerPassword;

            /// <summary>
            /// The padded user password
            /// </summary>
            public byte[] UserPassword;

            /// <summary>
            /// Length of the key, in bytes
            /// </summary>
            public int KeyLength;

            /// <summary>
            /// A RC4 encryption key
            /// </summary>
            public byte[] RC4Key;

            /// <summary>
            /// 7.6.3.3
            /// </summary>
            /// <remarks>Used for padding short passwords</remarks>
            readonly public static byte[] Padding =
            {
              0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
              0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
              0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
              0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
            };
        }

        #endregion
    }



    internal sealed class StandarSecurityHandlerRC40 : StandarSecurityHandler
    {
        public StandarSecurityHandlerRC40(PdfDocumentID id, PdfStandarSecuretyHandler std, string owner_pw, string user_pw, out bool is_owner, out bool is_user)
            : base(id, std, false, owner_pw, user_pw, out is_owner, out is_user)
        {

        }

        internal override byte[] Decrypt(byte[] bytes)
        {
            return RC4.Encrypt(_encryption_key, bytes);
        }

        internal override ISource CreateDecryptWrapper(ISource source)
        {
            return new DecryptSource(source, _encryption_key);
        }

        /// <summary>
        /// This class decrypts stream data
        /// </summary>
        internal class DecryptSource : ISource
        {
            protected readonly ISource _source;
            protected readonly byte[] _key;

            int ISource.Padding => Padd;
            protected virtual int Padd { get { return 0; } }

            public object LockOn { get { return _source.LockOn; } }

            public bool IsExternal { get { return _source.IsExternal; } }
            public DecryptSource(ISource source, byte[] key)
            {
                _source = source;
                _key = new byte[key.Length];
                Buffer.BlockCopy(key, 0, _key, 0, key.Length);
            }
#if LONGPDF
            public int Read(byte[] buffer, long offset)
#else
            public int Read(byte[] buffer, int offset)
#endif
            {
                return ReadImpl(buffer, offset);
            }

            protected virtual int ReadImpl(byte[] buffer, long offset)
            {
                var read = _source.Read(buffer, offset);
                RC4.Encrypt(_key, buffer, buffer);
                return read;
            }
        }
    }

    internal sealed class StandarSecurityHandlerAES128 : StandarSecurityHandler
    {
        public StandarSecurityHandlerAES128(PdfDocumentID id, PdfStandarSecuretyHandler std, string owner_pw, string user_pw, out bool is_owner, out bool is_user)
            : base(id, std, true, owner_pw, user_pw, out is_owner, out is_user)
        {

        }

        internal override byte[] Decrypt(byte[] bytes)
        {
            //First 16 bytes is the initialization vector
            if (bytes.Length < 16)
                throw new PdfReadException(PdfType.String, ErrCode.UnexpectedEOD);
            var iv = new byte[16];
            Buffer.BlockCopy(bytes, 0, iv, 0, 16);

            using (var aes = Aes.Create())
            {
                aes.Key = _encryption_key;
                aes.IV = iv;

                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                // Create the streams used for decryption.
                using (var msDecrypt = new System.IO.MemoryStream(bytes))
                {
                    msDecrypt.Position = 16;
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        var ba = new byte[bytes.Length - 16];
                        csDecrypt.Read(ba, (int)0, ba.Length);
                        return ba;
                    }
                }
            }
        }

        internal override ISource CreateDecryptWrapper(ISource source)
        {
            return new AESSource(source, _encryption_key);
        }

        private class AESSource : StandarSecurityHandlerRC40.DecryptSource
        {
            byte[] _iv;
            protected override int Padd => 16;

            public AESSource(ISource source, byte[] key)
                : base(source, key)
            {

            }

            protected override int ReadImpl(byte[] buffer, long offset)
            {
                if (_iv == null)
                {
                    _iv = new byte[16];
                    if (_source.Read(_iv, offset) != 16)
                        throw new PdfReadException(PdfType.Stream, ErrCode.UnexpectedEOD);
                }

                offset += 16;
                using (var aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.IV = _iv;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        var encrypted_bytes = new byte[buffer.Length];
                        var read = _source.Read(encrypted_bytes, offset);

                        // Create the streams used for decryption.
                        using (var msDecrypt = new System.IO.MemoryStream(encrypted_bytes))
                        {
                            using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                            {
                                csDecrypt.Read(buffer, (int)0, buffer.Length);
                            }
                        }

                        return read;
                    }
                }
            }
        }
    }

    internal sealed class StandarSecurityHandlerAES256 : BaseSecurityHandler
    {
        /// <summary>
        /// Key to use for decrypting the document. 
        /// </summary>
        private byte[] _global_encryption_key;

        public StandarSecurityHandlerAES256(PdfStandarSecuretyHandler std, string owner_pw, string user_pw, out bool is_owner, out bool is_user)
        {
            var state = new EncryptionState();
            state.UserBytes = std.U;
            state.UserBytesExt = std.UE;
            state.OwnerBytes = std.O;
            state.OwnerBytesExt = std.OE;
            state.Permission = std._P;
            state.Perms = std.Perms;


            Authenticate(state, owner_pw, out is_owner, out is_user);
            _global_encryption_key = state.AesKey;
        }

        internal override ISource CreateDecryptWrapper(ISource source)
        {
            return new AESSource(source, _global_encryption_key);
        }

        internal override byte[] Decrypt(byte[] bytes)
        {
            //First 16 bytes is the initialization vector
            if (bytes.Length < 16)
                throw new PdfReadException(PdfType.String, ErrCode.UnexpectedEOD);
            var iv = new byte[16];
            Buffer.BlockCopy(bytes, 0, iv, 0, 16);

            using (var aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Key = _global_encryption_key;
                aes.IV = iv;

                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                // Create the streams used for decryption.
                using (var msDecrypt = new System.IO.MemoryStream(bytes))
                {
                    msDecrypt.Position = 16;
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        var ba = new byte[bytes.Length - 16];
                        csDecrypt.Read(ba, (int)0, ba.Length);
                        return ba;
                    }
                }
            }
        }

        internal override void SetEncryptionFor(PdfObjID id)
        {
            
        }

        internal override object Save()
        {
            return null;
        }

        internal override void Restore(object key)
        {
            
        }

        /// <summary>
        /// Computes an encryption key
        /// </summary>
        /// <remarks>Algorithm 3.2a</remarks>
        private static void Authenticate(EncryptionState state, string str_pw, out bool is_owner, out bool is_user)
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.IV = new byte[aes.BlockSize / 8];
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;

                //1. Normalize. For now I'll just do a utf8 transform
                byte[] pw = Encoding.UTF8.GetBytes(str_pw);  //UTF8 encoding

                //2. Trunctuate password
                if (pw.Length > 127)
                    Array.Resize(ref pw, 127);

                //3a. Test password against the owner key.
                // hash of pw + 8 last bytes of OwnerBytes + 48 bytes of user bytes 
                var hash = ComputeSha256Hash(pw, state.OwnerBytes, 32, state.UserBytes);
                is_owner = is_user = Util.ArrayHelper.ArraysEqual<byte>(hash, state.OwnerBytes, 32);

                //3b. Compute the decrytion key
                if (is_owner)
                {
                    aes.Key = ComputeSha256Hash(pw, state.OwnerBytes, 40, state.UserBytes);

                    state.AesKey = decrypt(aes, state.OwnerBytesExt);

                    if (DecryptPerms(state))
                        return;

                    is_owner = false;
                }

                //4a. Test password against user key
                // hash of pw + 8 last bytes of UserBytes
                hash = ComputeSha256Hash(pw, state.UserBytes, 32, null);
                is_user = Util.ArrayHelper.ArraysEqual<byte>(hash, state.UserBytes, 32);

                //4b. Compute the decrytion key
                if (is_user)
                {
                    aes.Key = ComputeSha256Hash(pw, state.UserBytes, 40, null);

                    state.AesKey = decrypt(aes, state.UserBytesExt);

                    is_user = DecryptPerms(state);
                }
            }
        }

        private static bool DecryptPerms(EncryptionState state)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = state.AesKey;
                aes.Mode = CipherMode.ECB;
                aes.IV = new byte[aes.BlockSize / 8];
                aes.Padding = PaddingMode.None;

                byte[] perms = decrypt(aes, state.Perms);

                if (perms[9] == (byte) 'a' && perms[10] == (byte) 'd' && perms[11] == (byte) 'b')
                {
                    state.Perms = perms;
                    return true;
                }
                return false;
            }
        }

        private static byte[] decrypt(Aes aes, byte[] encrypted_bytes)
        {
            using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
            {
                var buffer = new byte[encrypted_bytes.Length];

                // Create the streams used for decryption.
                using (var msDecrypt = new System.IO.MemoryStream(encrypted_bytes))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        csDecrypt.Read(buffer, (int)0, buffer.Length);
                    }
                }

                return buffer;
            }
        }

        private static byte[] ComputeSha256Hash(byte[] psw, byte[] salt, int salt_offset, byte[] salt2)
        {
            //https://www.programmingalgorithms.com/algorithm/sha256/
            //https://medium.com/bugbountywriteup/breaking-down-sha-256-algorithm-2ce61d86f7a3
            using (SHA256 sha = SHA256.Create())
            {
                sha.TransformBlock(psw, 0, psw.Length, null, 0);
                if (salt2 == null)
                    sha.TransformFinalBlock(salt, salt_offset, 8);
                else
                {
                    sha.TransformBlock(salt, salt_offset, 8, null, 0);
                    sha.TransformFinalBlock(salt2, 0, salt2.Length);
                }
                
                return sha.Hash;
            }
        }

        #region helper classes

        private class AESSource : StandarSecurityHandlerRC40.DecryptSource
        {
            byte[] _iv;
            protected override int Padd => 16;

            public AESSource(ISource source, byte[] key)
                : base(source, key)
            {

            }

            protected override int ReadImpl(byte[] buffer, long offset)
            {
                if (_iv == null)
                {
                    _iv = new byte[16];
                    if (_source.Read(_iv, offset) != 16)
                        throw new PdfReadException(PdfType.Stream, ErrCode.UnexpectedEOD);
                }

                offset += 16;
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Padding = PaddingMode.None;
                    aes.IV = _iv;
                    aes.Key = _key;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        var encrypted_bytes = new byte[buffer.Length];
                        var read = _source.Read(encrypted_bytes, offset);

                        // Create the streams used for decryption.
                        using (var msDecrypt = new System.IO.MemoryStream(encrypted_bytes))
                        {
                            using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                            {
                                csDecrypt.Read(buffer, (int)0, buffer.Length);
                            }
                        }

                        return read;
                    }
                }
            }
        }

        /// <summary>
        /// This class holds all data needed for the encryption functions
        /// </summary>
        private class EncryptionState
        {
            /// <summary>
            /// A string based on the user's password
            /// </summary>
            public byte[] UserBytes, UserBytesExt;

            /// <summary>
            /// A string based on the owner's password
            /// </summary>
            public byte[] OwnerBytes, OwnerBytesExt;

            /// <summary>
            /// The permission value as a little endian byte array
            /// </summary>
            public byte[] Permission, Perms;

            /// <summary>
            /// A Aes encryption key
            /// </summary>
            public byte[] AesKey;
        }

        #endregion
    }
}
