namespace PdfLib.Util
{
    /// <summary>
    /// Based on the example given in the PNG specs, Appendix 15
    /// </summary>
    internal class CRC32_Alt
    {
        /// <summary>
        /// Table of CRCs of all 8-bit messages
        /// </summary>
        private uint[] crc_table = new uint[256];

        private uint _crc = uint.MaxValue;

        public uint CRC { get { return _crc ^ uint.MaxValue; } }

        public CRC32_Alt()
        {
            for(int n = 0; n < crc_table.Length; n++)
            {
                uint c = unchecked((uint) n);
                for(int k = 0; k < 8; k++)
                {
                    if ((c & 1) != 0)
                        c = 0xedb88320u ^ (c >> 1);
                    else
                        c >>= 1;
                }
                crc_table[n] = c;
            }
        }

        public void Update(byte[] buffer, int offset, int count)
        {
            _crc = Update(_crc, buffer, offset, count);
        }

        public void Update(byte b)
        {
            _crc = crc_table[(_crc ^ b) & 0xFF] ^ (_crc >> 8);
        }

        public void Reset()
        {
            _crc = uint.MaxValue;
        }

        private uint Update(uint crc, byte[] buffer, int offset, int count)
        {
            for (int n = 0; n < count; n++)
                crc = crc_table[(crc ^ buffer[offset++]) & 0xFF] ^ (crc >> 8);

            return crc;
        }
    }
}
