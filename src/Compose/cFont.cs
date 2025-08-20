using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Compose.Font;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Compose
{
    /// <summary>
    /// cFont.
    /// 
    /// Usage:
    /// 
    /// 1. Load glyphs that will be used
    /// 
    ///    i.e. LoadGlyph("ABC");
    /// 
    ///    Alternatly oine can call "GetGlyphMetrix" for each induvidual glyph
    ///    
    /// 2. Encode string with the Encode(str) function
    /// </summary>
    public abstract class cFont
    {
        #region Variables and properties

        /// <summary>
        /// Name of this font
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// If this font is italic
        /// </summary>
        public abstract double ItalicAngle { get; }

        /// <summary>
        /// If this font's characters all have the same width
        /// </summary>
        public abstract bool IsFixedPitch { get; }

        /// <summary>
        /// When a font is closed it should not be futher modified,
        /// if it is modified then it will be reopened, but with
        /// the result that an entierly new font will be generated
        /// when it is then reused.
        /// </summary>
        /// <remarks>
        /// A font is usually closed when executing font commands,
        /// like when creating a contents stream
        /// </remarks>
        public abstract bool IsClosed { get; }

        /// <summary>
        /// The height from the baseline to the top
        /// of characters (discounting ascent)
        /// </summary>
        public abstract double CapHeight { get; }

        /// <summary>
        /// The height from the baseline to the top
        /// of the lower case X character (discounting ascent)
        /// </summary>
        public abstract double XHeight { get; }

        /// <summary>
        /// Height from baseline to baseline
        /// </summary>
        public abstract double LineHeight { get; }

        /// <summary>
        /// Height from baseline to highest point
        /// </summary>
        public abstract double Ascent { get; }

        /// <summary>
        /// Height from baseline down to lowest point
        /// </summary>
        public abstract double Descent { get; }

        /// <summary>
        /// The underline's position relative to the baseline
        /// </summary>
        public abstract double UnderlinePosition { get; }

        /// <summary>
        /// The thickness of the underline
        /// </summary>
        public abstract double UnderlineThickness { get; }

        /// <summary>
        /// Whenver this font is dual byte encoded or not
        /// </summary>
        public virtual bool IsCIDFont 
        {
            get { return false; }
            set { }
        }

        /// <summary>
        /// This font does not conform to Adobe Unicode character encoding
        /// </summary>
        public abstract bool IsSymbolic { get; }

        /// <summary>
        /// The font's bounding box
        /// </summary>
        public abstract PdfRectangle FontBBox { get; }

        /// <summary>
        /// The current widths of the glyphs, note that
        /// CID fonts will return null
        /// </summary>
        public abstract double[] Widths { get; }

        /// <summary>
        /// The first width character, meaningless for CID fonts
        /// </summary>
        public abstract int FirstChar { get; }

        /// <summary>
        /// The last width character, meaningless for CID fonts
        /// </summary>
        public abstract int LastChar { get; }

        /// <summary>
        /// Precision of the glyph width array. Should either
        /// be kept at zero or set equal to the precision of
        /// the document. 
        /// </summary>
        /// <remarks>
        /// A known precision is needed when laying out text. 
        /// Over long strings preicision inaccuracies build up,
        /// so the text layout algorithim need to know the
        /// precision the document will be saved at to 
        /// compensate for this buildup.
        /// 
        /// Precision 0 gives four digits of accuracy.
        /// </remarks>
        public uint Precision = 0;

        #endregion

        #region Init

        #endregion

        #region FontMetrics

        /// <summary>
        /// Get kerning between to glyphs
        /// </summary>
        /// <param name="from_gid">Left glyph</param>
        /// <param name="to_gid">Right glyph</param>
        /// <returns>How much to move the right glyph towards the left</returns>
        public abstract double GetKerning(int from_gid, int to_gid);

        /// <summary>
        /// Fetches both the gid and advance width of a glyph
        /// </summary>
        /// <param name="c">Character to get metrix from</param>
        public abstract chGlyph GetGlyphMetrix(char c);

        /// <summary>
        /// Fetches the enclosing bounding box for this font
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        public abstract chBox GetGlyphBox(char c);

        #endregion

        #region Create

        /// <summary>
        /// Loads all the glyphs in the string into the font
        /// </summary>
        /// <param name="str">Glyphs to load</param>
        public void LoadGlyph(string str)
        {
            for (int c = 0; c < str.Length; c++)
                LoadGlyph(str[c]);
        }

        /// <summary>
        /// Loads a character into the font
        /// </summary>
        /// <param name="ch">Charater to load</param>
        public virtual void LoadGlyph(char ch)
        {
            GetGlyphMetrix(ch);
        }

        /// <summary>
        /// Encodes a unicode string into the font's
        /// format
        /// </summary>
        public abstract byte[] Encode(string str);

        /// <summary>
        /// Returns a wrapper for making this into a PdfFont.
        /// </summary>
        public abstract PdfFont MakeWrapper();

        /// <summary>
        /// Creates a finished pdf font
        /// </summary>
        public PdfFont CompileFont()
        {
            var td = new TemporaryDictionary();
            CompileFont(td);
            return PdfFont.Create(td);
        }

        /// <summary>
        /// Serialize the font into a dictionary
        /// </summary>
        /// <param name="dict">The wrap font's dictionary</param>
        internal abstract void CompileFont(PdfDictionary dict);

        /// <summary>
        /// Write font data to a stream
        /// </summary>
        /// <param name="target">Stream to write data to, pass null to have a byte array returned</param>
        /// <returns>If target is null, return the data</returns>
        public abstract byte[] WriteTo(Stream target);

        public static cFont Create(Stream source)
        {
            //Assumes true type for now
            return new cTTFont(source);
        }

        /// <summary>
        /// Creates a built in font from an afm object
        /// </summary>
        /// <param name="afm">AFM describing the built in font</param>
        /// <returns>Built in cFont</returns>
        public static cFont Create(AdobeFontMetrixs afm)
        {
            return new cBuiltInFont(afm);
        }

        /// <summary>
        /// Creates a font
        /// </summary>
        /// <param name="name">Name of the built in font, throws if it dosn't exist</param>
        /// <returns>The built in compose font</returns>
        public static cFont Create(string name)
        {
            var match = AdobeFontMetrixs.IsPerfectFit(name);
            if (match.PerfectFit)
                return new cBuiltInFont(name);


            string filename = Util.Registery.FindFontFilename(name, match.Bold, match.Italic || match.Oblique);
            if (filename != null)
            {
                var font_file = Path.IsPathRooted(filename) ? filename : rFont.FontFolder + filename;
                if (File.Exists(font_file))
                    try { return Create(File.OpenRead(font_file)); }
                    catch (Exception) { }
            }

            if (match.Name != null)
                return new cBuiltInFont(name);

            throw new ArgumentException("Font not found");
        }

        /// <summary>
        /// Creates a font
        /// </summary>
        /// <param name="name">Name of the font</param>
        /// <param name="embed">if true, force embeding</param>
        /// <returns></returns>
        public static cFont Create(string name, bool embed)
        {
            if (!embed)
                return Create(name);
            var match = AdobeFontMetrixs.IsPerfectFit(name);

            string filename = Util.Registery.FindFontFilename(name, match.Bold, match.Italic || match.Oblique);
            if (filename != null)
            {
                var font_file = rFont.FontFolder + filename;
                if (File.Exists(font_file))
                    try { return Create(File.OpenRead(font_file)); }
                    catch (Exception) { }
            }
            
            if (match.PerfectFit)
            {
                name = AdobeFontMetrixs.Create(name, null, false).SubstituteFont;
                filename = Util.Registery.FindFontFilename(name, match.Bold, match.Italic || match.Oblique);
                var font_file = rFont.FontFolder + filename;
                if (File.Exists(font_file))
                    try { return Create(File.OpenRead(font_file)); }
                    catch (Exception) { }
            }

            throw new ArgumentException("Font not found");
        }

        /// <summary>
        /// Attemps to make a cFont out of a PdfFont
        /// </summary>
        /// <param name="font">Pdf font</param>
        /// <returns>A cFont or null if it failed</returns>
        public static cFont Create(PdfFont font)
        {
            if (font is PdfType0Font)
            {
                var t0 = (PdfType0Font)font;
                var cid = t0.DescendantFonts;
                var fd = cid.FontDescriptor;
                var ff2 = fd.FontFile2;

                if (ff2 != null)
                {
                    //CID TT fonts do not have cmap, so we create one. For now
                    //we only map the first 256 glyphs.

                    //Grabs the CID cmap
                    var cmap = t0.Encoding;

                    //Reads out character by character
                    byte[] two = new byte[2];
                    var glyfs = new rCMap.Glyph[256];
                    var map = cmap.CreateMapper();
                    for (int c = 0; c < 256; c++)
                    {
                        two[1] = (byte) c;
                        var glyf = map.Map(two);
                        glyfs[c] = glyf[0];
                    }

                    //Creates a build table with the mapping placed
                    //into one range. Inneffieient but this table won't
                    //be the one written to disk
                    var build = new Read.TrueType.CMapBuild(
                        new Read.TrueType.CMapBuild.CMapType[] {
                            fd.IsSymbolic ? Read.TrueType.CMapBuild.CMapType.cmap_3_0 
                                          : Read.TrueType.CMapBuild.CMapType.cmap_3_1
                        });
                    var ranges = new Read.TrueType.CMapBuild.GidRange[1];
                    var range = new Read.TrueType.CMapBuild.GidRange(0, 255, new int[256]);
                    for (int c = 0; c < 256; c++)
                        range.Gids[c] = glyfs[c].CodePoint;
                    build.Ranges = ranges;
                    ranges[0] = range;

                    //Then writes the table to a memorystream
                    var wf = new Read.TrueType.WriteFont();
                    build.Write(wf, 2);
                    var ms = new MemoryStream();
                    wf.WriteTo(ms);

                    //And then read it back in. The reason for being this convoluted
                    //is because build tables can't be used in place of the normal 
                    //tables, and one can't create normal tables from scratch.
                    var s = new Util.StreamReaderEx(ms);
                    ms.Position = 0;
                    var cmap_table = new Read.TrueType.CmapTable(s);

                    return new cTTFont(new MemoryStream(ff2.DecodedStream), cmap_table);
                }
            }
            if (font is PdfType1Font)
            {
                var t1 = (PdfType1Font)font;
                var fd = t1.FontDescriptor;
                if (fd != null)
                {
                    var ff2 = t1.FontDescriptor.FontFile2;
                    if (ff2 != null)
                        return Create(new MemoryStream(ff2.DecodedStream));
                }

                //Tries to make use of the font as-is.
                return new cBuiltInT1(t1);
            }
            return null;
        }

        #endregion

        #region Render

        internal abstract rGlyph GetGlyph(int char_code, bool is_space, int n_bytes);
        internal abstract int GetNBytes(byte b);

        internal virtual void InitRenderNoCache(IFontFactory factory) => InitRender(factory);
        internal abstract void InitRender(IFontFactory factory);
        internal abstract void DissmissRender();
        internal abstract void DisposeRender();

        #endregion

        #region Standar font

        private static cFont _times;

        public static cFont Times
        {
            get
            {
                if (_times == null)
                    _times = cFont.Create("Times New Roman");
                return _times;
            }
        }

        #endregion

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Measures for a single glyph
    /// </summary>
    public class chBox
    {
        public readonly int _Width, _XMin, _XMax, _YMin, _YMax;
        public readonly float UnitsPerEm;
        public float Width { get { return _Width / UnitsPerEm; } }
        public float XMin { get { return _XMin / UnitsPerEm; } }
        public float XMax { get { return _XMax / UnitsPerEm; } }
        public float YMin { get { return _YMin / UnitsPerEm; } }
        public float YMax { get { return _YMax / UnitsPerEm; } }
        public chBox(int units_per_em, int width, int xmin, int ymin, int xmax, int ymax)
        { UnitsPerEm = units_per_em; _Width = width; _XMin = xmin; _YMin = ymin; _XMax = xmax; _YMax = ymax; }
    }

    public class chGlyph
    {
        /// <summary>
        /// Coordinate system
        /// </summary>
        private readonly int _units_per_em;

        private readonly int _width, _lsb, _rsb, _ymax, _ymin;

        /// <summary>
        /// The glyph id of this glyph
        /// </summary>
        public readonly int GID;

        /// <summary>
        /// The width of this glyph
        /// </summary>
        public double Width { get { return _width / (double) _units_per_em; } }

        /// <summary>
        /// Right side bearings
        /// </summary>
        /// <remarks>This is the whitespace at the right of the glyph</remarks>
        public double RSB { get { return _rsb / (double)_units_per_em; } }

        /// <summary>
        /// Left side bearings
        /// </summary>
        /// <remarks>This is the whitespace at the left of the glyph</remarks>
        public double LSB { get { return _lsb / (double)_units_per_em; } }

        /// <summary>
        /// Extents of this glyf
        /// </summary>
        public double YMax { get { return _ymax / (double) _units_per_em; } }
        public double YMin { get { return _ymin / (double) _units_per_em; } }

        internal chGlyph(int gid, int units_per_em, int w, int YMin, int YMax, int lsb, int rsb)
        { GID = gid; _units_per_em = units_per_em; _width = w; _ymin = YMin; _ymax = YMax; _lsb = lsb; _rsb = rsb; }
        internal chGlyph(int gid, chGlyph g)
            : this(gid, g._units_per_em, g._width, g._ymin, g._ymax, g._lsb, g._rsb)
        { }
    }
}
