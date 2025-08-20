namespace PdfLib.PostScript
{
    public enum PSType : byte
    {
        //Not a PS type
        None,

        Keyword, Name, 

        Number, Real, Integer, 

        String, HexString, 

        BeginDictionary, EndDictionary, 
        BeginArray, EndArray, 
        BeginProcedure, EndProcedure, 

        EOF
    }
}
