using PdfLib.Read;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Token types for internal use.
    /// </summary>
    /// <remarks>
    /// Used by the parser, lexer, references and
    /// dictionary implementations.
    /// 
    /// The size (byte) is restricted by the error logging
    /// handeling.
    /// </remarks>
    public enum PdfType : byte
    {
        //Not a pdf type
        None,

        //Any pdf type except markers
        Item,

        //Numbers
        Integer, Long, Real,

        //Needed for when int/real is unknown
        Number,

        //Strings
        Comment, String, HexString,
        Name, Keyword, 
        
        //Types that are made out of 
        //primitive types
        Date, Rectangle, IntRectangle, 

        //Markers
        BeginArray, EndArray,
        BeginDictionary, EndDictionary,

        //Objects created from keywords
        Null, Bool, Obj,

        //Objects created from markers
        Array, Dictionary, 

        //High lever PdfObjects (in the Pdf namespace)
        Trailer, Catalog, Encrypt, CryptFilter,
        CryptFilters, Pages, PageTree, Page, 
        Resources, XObject, ColorSpace, Filter, 
        FilterParams, Content, XRefStream, ObjStream, 
        Font, Encoding, Pattern, Shading,
        Function, FontDescriptor, FontFile3, 
        GState, FontFile, Cmap, CIDSystemInfo,
        DNCSAttrib, ColorantsDict, ProcessDictionary,
        Outline, Annotation, NameDictionary, NameTree,
        TreeNode, Destination, EmbeddedFile, 
        DestDictionary, Action, Info,
        AppearanceDictionary, OptionalContentGroup,
        UsageDictionary, CreatorInfo, LanguageDictionary,
        ExportDictionary, ZoomDictionary, PrintDictionary,
        ViewDictionary, UserDictionary, 
        PageElementDictionary, OptionalContentMembership,
        InteractiveFormDictionary, FieldDictionary,
        BorderEffect, FontFile2, SoftMask, Group,

        //High level objects that don't really
        //exists in the pdf specs/file.
        XObjectElms, FilterArray, FilterParmsArray,
        ContentArray, IntArray, RealArray, FontElms,
        LabPoint, PatternElms, FunctionArray,
        GStateElms, ColorSpaceElms, CIDFont, 
        ShadingElms, AnnotationElms, TreeNodeArray, 
        Limit, DictArray, NamedDests, NamedFiles,
        ActionArray, DocumentID, ASDict, AnnotBorder,
        NameArray, FieldDictionaryAr, CalloutLine, 

        //Used exlusivly for error codes
        Stream, 

        // End of file (Note: Must always be the last one)
        EOF
    }
}
