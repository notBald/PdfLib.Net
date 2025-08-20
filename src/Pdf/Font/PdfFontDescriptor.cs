using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Font
{
    public class PdfFontDescriptor : Elements
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.FontDescriptor
        /// </summary>
        internal override PdfType Type { get { return PdfType.FontDescriptor; } }

        /// <summary>
        /// If this is the descriptor of an embeded or built in font
        /// </summary>
        public bool BuiltInFont
        {
            get
            {
                if (_elems.Contains("FontFile"))
                    return false;
                if (_elems.Contains("FontFile2"))
                    return false;
                if (_elems.Contains("FontFile3"))
                    return false;
                return true;
            }
        }

        /// <summary>
        /// Height above the baseline
        /// </summary>
        public double Ascent { get { return _elems.GetRealEx("Ascent"); } set { _elems.SetReal("Ascent", value); } }

        /// <summary>
        /// Depth below the baseline
        /// </summary>
        public double Descent { get { return _elems.GetRealEx("Descent"); } set { _elems.SetReal("Descent", value); } }

        /// <summary>
        /// The vertical coordinate of the top of flat capital letters
        /// </summary>
        public double CapHeight { get { return _elems.GetRealEx("CapHeight"); } set { _elems.SetReal("CapHeight", value); } }

        /// <summary>
        /// The height of the letter x
        /// </summary>
        public double XHeight { get { return _elems.GetReal("XHeight", 0); } set { _elems.SetReal("XHeight", value, 0); } }

        /// <summary>
        /// The spacing between baselines
        /// </summary>
        public double Leading { get { return _elems.GetReal("Leading", 0); } set { _elems.SetReal("Leading", value, 0); } }

        /// <summary>
        /// Boldness of the font
        /// </summary>
        public double FontWeight { get { return _elems.GetReal("FontWeight", 400); } set { _elems.SetReal("FontWeight", value, 400); } }

        /// <summary>
        /// Bounding box of the font
        /// </summary>
        public PdfRectangle FontBBox { get { return (PdfRectangle)_elems.GetPdfType("FontBBox", PdfType.Rectangle); } }

        /// <summary>
        /// Angle of the font.
        /// </summary>
        /// <remarks>Required by the specs, but, eh.</remarks>
        public double ItalicAngle { get { return _elems.GetReal("ItalicAngle", 0); } set { _elems.SetReal("ItalicAngle", value, 0); } }

        /// <summary>
        /// The width to use for character codes whose widths are not 
        /// specified in a font dictionary’s Widths array.
        /// </summary>
        public double MissingWidth { get { return _elems.GetReal("MissingWidth", 0); } set { _elems.SetReal("MissingWidth", value, 0); } }

        /// <summary>
        /// Avarage width of glyphs
        /// </summary>
        public double AvgWidth { get { return _elems.GetReal("AvgWidth", 0); } set { _elems.SetReal("AvgWidth", value, 0); } }

        /// <summary>
        /// Max width of glyphs
        /// </summary>
        public double MaxWidth { get { return _elems.GetReal("MaxWidth", 0); } set { _elems.SetReal("MaxWidth", value, 0); } }

        /// <summary>
        /// Thickness of vertical stem
        /// </summary>
        public double StemV { get { return _elems.GetRealEx("StemV"); } set { _elems.SetReal("StemV", value); } }

        /// <summary>
        /// Thickness of horizontal stem
        /// </summary>
        public double StemH { get { return _elems.GetReal("StemH", 0); } set { _elems.SetReal("StemH", value, 0); } }

        /// <summary>
        /// A collection of flags defining various characteristics of the font
        /// </summary>
        public FontFlags Flags { get { return (FontFlags)_elems.GetUIntEx("Flags"); } set { _elems.SetUInt("Flags", (uint)value); } }

        /// <summary>
        /// If this font is symbolic or not
        /// </summary>
        public bool IsSymbolic { get { return (Flags & FontFlags.Symbolic) == FontFlags.Symbolic; } }

        /// <summary>
        /// If this font is serif or not
        /// </summary>
        public bool IsSerif { get { return (Flags & FontFlags.Serif) == FontFlags.Serif; } }

        /// <summary>
        /// If this font is serif or not
        /// </summary>
        public bool IsItalic { get { return (Flags & FontFlags.Italic) == FontFlags.Italic; } }

        /// <summary>
        /// If this font is bold or not
        /// </summary>
        /// <remarks>Don't look at the font weight. It's not reliable.</remarks>
        public bool IsBold { get { return (Flags & FontFlags.ForceBold) == FontFlags.ForceBold; } }

        /// <summary>
        /// If this font's characters all have the same width or not
        /// </summary>
        public bool IsMonospaced { get { return (Flags & FontFlags.FixedPitch) == FontFlags.FixedPitch; } }

        /// <summary>
        /// The PostScript name of the font.
        /// </summary>
        public string FontName { get { return _elems.GetNameEx("FontName"); } set { _elems.SetName("FontName", value); } }

        /// <summary>
        /// A type 1 font program
        /// </summary>
        public PdfFontFile FontFile { get { return (PdfFontFile) _elems.GetPdfType("FontFile", PdfType.FontFile); } }

        /// <summary>
        /// A true type font program
        /// </summary>
        [PdfVersion("1.1")]
        public PdfFontFile2 FontFile2 
        {
            get { return (PdfFontFile2) _elems.GetPdfType("FontFile2", PdfType.FontFile2); }
            set { _elems.SetItem("FontFile2", (PdfItem)value, true); }
        }

        /// <summary>
        /// A font program as specified by the "SubType" entery
        /// </summary>
        [PdfVersion("1.2")]
        public PdfFontFile3 FontFile3 { get { return (PdfFontFile3) _elems.GetPdfType("FontFile3", PdfType.FontFile3); } }

        #endregion

        #region Init

        internal PdfFontDescriptor(PdfDictionary dict)
            : base(dict)
        {
            //The file "Hard Sync without Aliasing.pdf" contains a FontDescriptor
            //without "type" set correctly, so using CheckType instead of CheckTypeEx
            _elems.CheckType("FontDescriptor");
        }

        public PdfFontDescriptor(string font_name, double ascent, double descent, double cap_height)
            : base(new TemporaryDictionary())
        {
            _elems.SetType("FontDescriptor");

            Ascent = ascent;
            Descent = descent;
            CapHeight = cap_height;
            StemV = 0;
            Flags = FontFlags.Nonsymbolic;
            FontName = font_name;
        }

        #endregion

        #region Required overrides

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || !(obj is PdfFontDescriptor)) return false;
            var fd = (PdfFontDescriptor) obj; 
            return //Todo: Quick and dirty hack
                MissingWidth == fd.MissingWidth &&
                Flags == fd.Flags &&
                FontName == fd.FontName;
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfFontDescriptor(elems);
        }

        #endregion
    }

    public sealed class PdfFontFile : StreamElm
    {
        #region Properties

        /// <summary>
        /// PdfType.FontDescriptor
        /// </summary>
        internal override PdfType Type { get { return PdfType.FontFile; } }

        /// <summary>
        /// font file data
        /// </summary>
        public IStream Stream { get { return _stream; } }

        /// <summary>
        /// The length in bytes of the clear-text portion of the Type 1 font program
        /// </summary>
        public int ClearTextLength { get { return _elems.GetUIntEx("Length1"); } }

        /// <summary>
        /// The length in bytes of the encrypted portion of the Type 1 font program
        /// </summary>
        public int EncryptedLength { get { return _elems.GetUIntEx("Length2"); } }

        /// <summary>
        /// The length in bytes of the fixed-content portion of the Type 1 font program
        /// </summary>
        public int FixedLength { get { return _elems.GetUIntEx("Length3"); } }

        #endregion

        #region Init

        internal PdfFontFile(IWStream stream)
            : base(stream)
        {
        
        }

        internal PdfFontFile(IStream stream, PdfDictionary dict)
            : base(stream, dict)
        {

        }

        #endregion

        #region Required overrides

        protected override StreamElm MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfFontFile(stream, dict);
        }

        #endregion
    }

    public sealed class PdfFontFile2 : StreamElm
    {
        #region Properties

        /// <summary>
        /// PdfType.FontDescriptor
        /// </summary>
        internal override PdfType Type { get { return PdfType.FontFile2; } }

        /// <summary>
        /// font file data
        /// </summary>
        public IStream Stream { get { return _stream; } }

        /// <summary>
        /// Length of the font after decoding
        /// </summary>
        public int Length1 { get { return _elems.GetIntEx("Length1"); } }

        /// <summary>
        /// Uncompressed font data
        /// </summary>
        public byte[] DecodedStream { get { return _stream.DecodedStream; } }

        #endregion

        #region Init

        public PdfFontFile2(byte[] font_data)
            : base(new WritableStream(font_data))
        {
            _elems.SetInt("Length1", font_data.Length);
        }

        public PdfFontFile2(System.IO.Stream font_data)
            : base(new WrMemStream(font_data))
        {
            _elems.SetInt("Length1", (int) font_data.Length);
        }

        internal PdfFontFile2(IWStream stream)
            : base(stream)
        {
        
        }

        internal PdfFontFile2(IStream stream, PdfDictionary dict)
            : base(stream, dict)
        {

        }

        #endregion

        #region Required overrides

        protected override StreamElm MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfFontFile2(stream, dict);
        }

        #endregion
    }

    public sealed class PdfFontFile3 : StreamElm
    {
        #region Properties

        /// <summary>
        /// PdfType.FontDescriptor
        /// </summary>
        internal override PdfType Type { get { return PdfType.FontFile3; } }

        /// <summary>
        /// Type of font program. Can be Type1C, CIDFontType0C or OpenType
        /// </summary>
        public string Subtype { get { return _elems.GetNameEx("Subtype"); }  }

        /// <summary>
        /// font file data
        /// </summary>
        public IStream Stream { get { return _stream; } }

        #endregion

        #region Init

        internal PdfFontFile3(IWStream stream)
            : base(stream)
        {
        
        }

        internal PdfFontFile3(IStream stream, PdfDictionary dict)
            : base(stream, dict)
        {

        }

        #endregion

        #region Required overrides

        protected override StreamElm MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfFontFile3(stream, dict);
        }

        #endregion
    }

    [Flags]
    public enum FontFlags : uint
    {
        None,

        /// <summary>
        /// All glyphs have the same width
        /// </summary>
        FixedPitch = 0x1,
        /// <summary>
        /// Glyphs have serifs
        /// </summary>
        Serif = 0x2,
        /// <summary>
        /// Font contains glyphs outside the Adobe standard Latin character set
        /// </summary>
        Symbolic = 0x4,
        /// <summary>
        /// Glyphs resemble cursive handwriting
        /// </summary>
        Script = 0x8,
        /// <summary>
        /// Font uses the Adobe standard Latin character set or a subset of it
        /// </summary>
        Nonsymbolic = 0x20,
        /// <summary>
        /// Glyphs have dominant vertical strokes that are slanted
        /// </summary>
        Italic = 0x40,
        /// <summary>
        /// Font contains no lowercase letters
        /// </summary>
        AllCap = 0x10000,
        /// <summary>
        /// Font contains both uppercase and lowercase letters
        /// </summary>
        SmallCap = 0x20000,
        /// <summary>
        /// Whether bold glyphs shall be painted with extra pixels even at very small text sizes
        /// </summary>
        ForceBold = 0x40000
    }
}
