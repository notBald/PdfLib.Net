namespace PdfLib.Read.CFF
{
    class FontDescriptor
    {
        public readonly TopDICT FD;
        public readonly PrivateDICT PD;
        public FontDescriptor(TopDICT fd, PrivateDICT pd)
        { FD = fd; PD = pd; }
    }
}
