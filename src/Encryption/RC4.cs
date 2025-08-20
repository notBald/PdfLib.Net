namespace PdfLib.Encryption
{
    //FOR AES consider the BSD code: FIPS-197 compliant AES implementation or AESManaged in the framework
    internal class RC4
    {
        /// <summary>
        /// Encrypts using the RC4 algorithim
        /// </summary>
        /// <param name="pwd">Encryption key</param>
        /// <param name="data">Data to decrypt</param>
        /// <returns>Encrypted or decrypted data</returns>
        /// <remarks>
        /// http://en.wikipedia.org/wiki/RC4
        /// </remarks>
        public static byte[] Encrypt(byte[] pwd, byte[] data)
        {
            var output = new byte[data.Length];
            Encrypt(pwd, data, output);
            return output;
        }

        /// <summary>
        /// Encrypts using the RC4 algorithim
        /// </summary>
        /// <param name="pwd">Encryption key</param>
        /// <param name="data">
        /// Data to decrypt
        /// </param>
        /// <param name="output">
        /// Output buffer, can be the same as the input buffer
        /// </param>
        /// <returns>Encrypted or decrypted data</returns>
        public static void Encrypt(byte[] pwd, byte[] data, byte[] output)
        {
            int keylength = pwd.Length;

            //The S array contains permutations of
            //all possible bytes
            var S = new int[256];

            //Fills out the S array
            for (int i = 0; i < S.Length; i++)
                S[i] = i;

            //Computes the key
            for (int i = 0, j = 0; i < S.Length; i++)
            {
                j = (j + S[i] + pwd[i % keylength]) % 256;
                var tmp = S[i];
                S[i] = S[j];
                S[j] = tmp;
            }

            //Encrypts
            for (int c = 0, i = 0, j = 0; c < data.Length; c++)
            {
                i = (i + 1) % 256;
                j = (j + S[i]) % 256;
                var tmp = S[i];
                S[i] = S[j];
                S[j] = tmp;
                int k = S[(S[i] + S[j]) % 256];
                output[c] = (byte)(data[c] ^ k);
            }
        }
    }
}
