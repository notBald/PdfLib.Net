using System;
using PdfLib.Pdf.Font;
using PdfLib.Render;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Render.Font
{
    /// <summary>
    /// This inteface is intended to allow for implementing font rendering. The interface
    /// was never finished.
    /// </summary>
    public interface IFontFactory
    {
        // RenderEngine Engine { get; }

        /// <summary>
        /// A font cache, can be null.
        /// </summary>
        FontCache FontCache { get; }

        /// <summary>
        /// Creates a cache for glyphs
        /// </summary>
        /// <returns>A glyph cache or null</returns>
        IGlyphCache CreateGlyphCache();

        /// <summary>
        /// Create a built in font
        /// </summary>
        /// <param name="font">The PdfFont</param>
        /// <param name="widths">Widths, if known</param>
        /// <param name="afm">The metrixs of the font</param>
        /// <param name="substituted">If the AFM is a slot in subsitute</param>
        /// <returns>A render font</returns>
        rFont CreateBuiltInFont(PdfType1Font font, double[] widths, AdobeFontMetrixs afm, bool substituted);
        rFont CreateBuiltInFont(PdfType0Font font, AdobeFontMetrixs afm, bool substituted);
        rFont CreateMemTTFont(PdfLib.Read.TrueType.TableDirectory td, bool symbolic, ushort[] cid_to_gid);
    }

    /// <summary>
    /// An alternate approach was tacked on, the "NEW_IFontFactory", where instead of
    /// rendering the fonts one just need to implement the IGlyph and IGlyphDraw
    /// interfaces. 
    /// </summary>
    /// <typeparam name="Path">Path to render</typeparam>
    public interface IFontFactory<Path> : IFontFactory, IGlyphFactory<Path>, IDisposable
    {
        
    }

    public interface IGlyphCache
    {
        bool TryGetGlyph(int ch, out rGlyph glyph);

        void AddGlyph(int ch, rGlyph glyph);
    }

    public interface IGlyphFactory<Path>
    {
        /// <summary>
        /// Attach a Matrix transform to the path
        /// </summary>
        void SetTransform(Path path, xMatrix transform);

        /// <summary>
        /// Creates a path without anything in it
        /// </summary>
        Path CreateEmptyPath();

        rGlyph CreateGlyph(Path[] outline, xPoint advance, xPoint origin, xMatrix mt, bool freeze, bool is_space);

        IGlyph<Path> GlyphRenderer();
    }

    public interface IGlyphFactoryCache<Path> : IGlyphFactory<Path>
    {
        IGlyphCache CreateGlyphCache();
    }

    public interface IGlyph<Path>
    {
        IGlyphDraw Open();
        Path GetPath();
    }

    public interface IGlyphDraw : IDisposable
    {
        void BeginFigure(xPoint startPoint, bool isFilled, bool isClosed);
        void QuadraticBezierTo(xPoint point1, xPoint point2, bool isStroked, bool isSmoothJoin);
        void LineTo(xPoint point, bool isStroked, bool isSmoothJoin);
        void BezierTo(xPoint point1, xPoint point2, xPoint point3, bool isStroked, bool isSmoothJoin);
    }
}
