using System;
using System.Collections.Generic;

namespace PdfLib.Render.Font
{
    /// <summary>
    /// Pretty much lifted from BAUpload's PdfGlyph
    /// </summary>
    public abstract class rGlyph
    {
        //Code moved elswhere, because of NEW_IFontFactory
    }

    /// <summary>
    /// A glyph cache that dosn't cache glyphs
    /// </summary>
    internal class NullGlyphCache : IGlyphCache
    {
        bool IGlyphCache.TryGetGlyph(int ch, out rGlyph glyph)
        {
            glyph = null;
            return false;
        }

        void IGlyphCache.AddGlyph(int ch, rGlyph glyph)
        { }
    }

    public class StandardGlyphCache : IGlyphCache
    {
        protected Dictionary<int, rGlyph> _glyphs = new Dictionary<int, rGlyph>(32);

        bool IGlyphCache.TryGetGlyph(int ch, out rGlyph glyph)
        {
            return _glyphs.TryGetValue(ch, out glyph);
        }

        void IGlyphCache.AddGlyph(int ch, rGlyph glyph)
        {
            _glyphs.Add(ch, glyph);
        }
    }
}
