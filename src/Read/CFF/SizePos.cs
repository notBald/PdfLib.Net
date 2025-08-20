namespace PdfLib.Read.CFF
{
    struct SizePos
    {
        public int start, length;
        public int End { get { return start + length; } }
    }
}
