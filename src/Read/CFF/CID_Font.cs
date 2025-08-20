namespace PdfLib.Read.CFF
{
    class CID_Font
    {
        //String IDs
        public int Registry, Ordering, FontName;

        //Number
        public int Supplement, CIDFontVersion, CIDFontRevision,
            CIDFontType, CIDCount = 8720, UIDBase, FDArray,
            FDSelect;
    }
}
