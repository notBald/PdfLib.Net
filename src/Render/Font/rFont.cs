using System; //"using System.Windows" is not allowed in this class
using System.Collections.Generic;
using System.IO;
using PdfLib.Util;
using PdfLib.Pdf.Font;
using PdfLib.Read.CFF;
using PdfLib.Read.TrueType;
using System.Collections.Concurrent;


namespace PdfLib.Render.Font
{
    /// <summary>
    /// Render font. 
    /// </summary>
    /// <remarks>
    /// Notes:
    /// 
    ///  Terminology:
    ///    - Raw character: One or more bytes making up the character
    ///    - CodePoint: The raw bytes merged into a number
    ///    - CID: Character ID. We get this after sending the CodePoint
    ///           through the encoding dictionary. However CIDFonts
    ///           gets the CID by using a CMap
    ///    - GlyphId: The identity of the character inside a font. For Type1
    ///      fonts they are handed the CID, then they find the GID themselves.
    ///      For TrueType fonts one may need to use a supplied CIDtoGOD map to
    ///      get the GID
    ///  
    ///  To test if a character is space, test if the code point equals 32
    ///  Also use the code point to fetch font metrics, such as width and height, 
    ///  though I belive CID should be used for this in CIDFonts
    /// </remarks>
    public abstract class rFont : IDisposable
    {
        #region Variables

        ///// <summary>
        ///// A cache over glyphs
        ///// </summary>
        private IGlyphCache _glyph_cache;

        /// <summary>
        /// Width of each character from 0 to 255. 
        /// </summary>
        protected readonly double[] _widths;

        /// <summary>
        /// The factory that created this render font
        /// </summary>
        internal abstract IFontFactory FontFactory { get; }

        #endregion

        #region Properties

        /// <summary>
        /// Tries to determine the system's font folder
        /// </summary>
        public static string FontFolder
        {
            get
            {
                //NET 4.0
                //string fontsfolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts);

                //Try getting it from the windows font folder
                // get parent of System folder to have Windows folder
                DirectoryInfo dirWindowsFolder = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.System));

                // Concatenate Fonts folder onto Windows folder.
                return Path.Combine(dirWindowsFolder.FullName, "Fonts") + "\\";
            }
        }

        /// <summary>
        /// If this is a vertical font
        /// </summary>
        public abstract bool Vertical { get; }

        #endregion

        #region Init and dispose

        protected rFont(double[] widths)
        {
            _widths = widths;
        }

        internal void SetNullGlyphCache()
        {
            if (_glyph_cache == null)
                _glyph_cache = new NullGlyphCache();
        }

        /// <summary>
        /// Init is always called before a font is to be used. Note that init can be called
        /// after dismiss when a font is to be reused
        /// </summary>
        public void Init()
        {
            if (_glyph_cache == null)
            {
                var ff = FontFactory;
                if (ff != null)
                    _glyph_cache = ff.CreateGlyphCache();

                if (_glyph_cache == null)
                    _glyph_cache = new NullGlyphCache();
            }

            InitImpl();
        }

        protected abstract void InitImpl();

        /// <summary>
        /// Called when a font is removed from rendering.
        /// </summary>
        public abstract void Dismiss();

        /// <summary>
        /// Tells the font to close all streams and other held resources
        /// </summary>
        public void Dispose()
        {
            DisposeImpl();

            if (_glyph_cache is IDisposable d)
                d.Dispose();
            _glyph_cache = null;
        }
        public abstract void DisposeImpl();

        #endregion

        #region Fetch glyphs

        /// <summary>
        /// Renders a single glyph
        /// </summary>
        /// <param name="ch">CID for type1 fonts, glyph index or character for true type*</param>
        /// <param name="is_space">Whenever this is a space character or not.</param>
        /// <returns>A render glyph</returns>
        /// <remarks>
        /// * For TrueType on give the glyph index for CID fonts and the character for normal fonts
        /// </remarks>
        protected abstract rGlyph GetGlyph(int ch, bool is_space);

        /// <summary>
        /// Fetches a single glyph, either from cache or from GetGlyph
        /// </summary>
        /// <param name="ch">Whatever value one wish to use as cache lookup</param>
        /// <param name="is_space">Whenever this glyph is a space character.</param>
        /// <returns>GetGlyphImpl(int ch, bool is_space)</returns>
        /// <remarks>Avoid calling this function directly. One most likly
        /// want to use GetGlyphs</remarks>
        internal rGlyph GetCachedGlyph(int ch, bool is_space)
        {
            rGlyph glyph;
            if (!_glyph_cache.TryGetGlyph(ch, out glyph))
            {
                glyph = GetGlyph(ch, is_space);
                _glyph_cache.AddGlyph(ch, glyph);
            }
            return glyph;
        }

        /// <summary>
        /// These functions is used by the unicode codepath
        /// </summary>
        /// <param name="cid"></param>
        /// <returns></returns>
        protected rGlyph GetCachedGlyph(int cid)
        {
            rGlyph glyph;
            _glyph_cache.TryGetGlyph(cid, out glyph);
            return glyph;
        }
        protected void AddCachedGlyph(int cid, rGlyph g)
        {
            _glyph_cache.AddGlyph(cid, g);
        }

        /// <summary>
        /// Takes a string and converts characters to render glyphs
        /// </summary>
        /// <param name="str">The string to convert</param>
        /// <returns>Renderable glyphs</returns>
        /// <remarks>
        /// This function has a defult implementation that assumes
        /// a 8-bit character set. CID fonts override this to
        /// get double or variable byte fonts.
        /// </remarks>
        internal virtual rGlyph[] GetGlyphs(string str) 
        {
            return GetGlyphs(Read.Lexer.GetBytes(str));
        }

        public rGlyph[] FetchGlyphs(byte[] str)
        {
            return GetGlyphs(str);
        }

        /// <summary>
        /// Takes a string and converts characters to render glyphs
        /// </summary>
        /// <param name="str">The string to convert</param>
        /// <returns>Renderable glyphs</returns>
        /// <remarks>
        /// This function has a defult implementation that assumes
        /// a 8-bit character set. CID fonts override this to
        /// get double or variable byte fonts.
        /// </remarks>
        internal virtual rGlyph[] GetGlyphs(byte[] str)
        {
            //Default implementation assumes an 8-bit font
            var glyphs = new rGlyph[str.Length];
            for (int c = 0; c < str.Length; c++)
            {
                int ch = str[c];
                glyphs[c] = GetCachedGlyph(ch, ch == 32);
            }
            return glyphs;
        }

        /// <summary>
        /// Converts a unicode string to glyphs
        /// </summary>
        /// <param name="unicode_string">A string of unicode characters in UE-16BE format</param>
        /// <param name="cid_codes">Cid codes for the characters. For now these corespond 1-1 with
        /// the unicode_string array, but if surrugate pairs are to be supported this will have to
        /// be changed. Cid codes are supplied as at least one font needs it to get the correct width
        /// of a character</param>
        /// <remarks>
        /// For the sake of simplicity unicode is given it's own function. This to ease support
        /// of surrugates and because I didn't at all concider unicode in the org. design.
        /// 
        /// With the exception of "outlines" (bookmarks) PDF does not have unicode strings in
        /// them (And bookmarks aren't rendered). This means the only time I want to use unicode is 
        /// when I don't have the propper font. In that case converting to unicode is a nice way
        /// to get a character than can then be mapped to a glyph in some substitute font. I.e. 
        /// unicode is a "when all else fails" codepath, so I think it's just as well to keep 
        /// it sepperate.
        /// 
        /// 
        /// Impl. note:
        /// 
        /// Currently CID fonts are handeled by "rCIDFont" whicb calls PdfCMap, which again calls 
        /// one of the CID*Font. Unicode is inserted into PdfCMap, so it's PdfCMap that calls
        /// this function (depending on whenever unicode is needed or not). 
        /// 
        /// An alternate approach that fits better with my original design is to create "unicode"
        /// fonts that overrides GetGlyphs(byte[] str). 
        /// 
        /// I.e. instead of rCIDFont, make a rUnicodeFont. Have that font call the PdfCMap and
        /// get the result in return. Then have it call this function. Then the "_to_unicode"
        /// bit could be removed from PdfCMap and placed into rUnicodeFont, making things a bit
        /// cleaner over there.
        /// </remarks>
        internal virtual rGlyph[] GetGlyphs(ushort[] unicode_string, Pdf.Font.rCMap.Glyph[] glyphs)
        {
            throw new NotImplementedException("This font can't handle unicode");
        }

        #endregion

        #region Convert to unicode

        public virtual string GetUnicode(byte[] str)
        {
            return "";
        }

        #endregion

        #region Create

        /// <summary>
        /// Creates a rFont.
        /// </summary>
        /// <typeparam name="Geometry">Type of gemoetry that will be created when making glyphs</typeparam>
        /// <param name="file">Stream that will be disposed with the font</param>
        /// <param name="widths">Width of the glyphs</param>
        /// <param name="font">Pdf data on the font</param>
        /// <param name="factory">Factory for creating glyphs</param>
        /// <returns>A render font</returns>
        public static rFont Create<Geometry>(Stream file, double[] widths, PdfType1Font font, IFontFactory<Geometry> factory)
        {
            //Snifs the file to determine what font this is.
            if (!file.CanRead || !file.CanSeek)
                return null;

            if (CFont.IsCFF(file))
                return new Type1CFont<Geometry>(widths, font.FontDescriptor, font.Encoding, factory);

            //Checks for font container
            if (TrueTypeCollection.IsTTC(file))
            {
                var ttc = new TrueTypeCollection(file, false);
                var fnt = ttc[font.BaseFont];
                if (fnt == null)
                    fnt = ttc[0];
                var fd = font.FontDescriptor;
                bool is_symbolic = fd != null && fd.IsSymbolic;
                if (fnt.IsCFF)
                    return new CFFOTFont<Geometry>(widths, fnt, factory, is_symbolic, font.Encoding);
                return new TTFont<Geometry>(widths, fnt, factory, is_symbolic, font.Encoding);
            }

            if (TableDirectory.IsTTF(file))
                return new TTFont<Geometry>(file, widths, font, factory);

            return null;
        }

        #endregion
    }
}
