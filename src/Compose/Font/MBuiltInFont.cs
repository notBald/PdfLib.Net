using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Measure built in font
    /// </summary>
    internal class MBuiltInFont : MeasureFont
    {
        private readonly AdobeFontMetrixs _afm;
        internal AdobeFontMetrixs AFM { get { return _afm; } }

        public override double Ascent { get { return _afm.Ascender; } }
        public override double Descent { get { return _afm.Descender; } }
        public override double CapHeight { get { return _afm.CapHeight; } }
        public double XHeight { get { return _afm.XHeight; } }
        public override double LineHeight { get { return _afm.Ascender - _afm.Descender; } }
        public override double UnderlinePosition { get { return _afm.UnderlinePosition; } }
        public override double UnderlineThickness { get { return _afm.UnderlineThickness; } }
        public bool IsFixedPitch { get { return _afm.IsFixedPitch; } }
        public bool IsSymbolic { get { return _afm.Symbolic; } }
        public xRect FontBBox { get { return _afm.FontBBox; } }
        public double ItalicAngle { get { return _afm.ItalicAngle; } }

        public MBuiltInFont(AdobeFontMetrixs afm)
        {
            if (afm == null) throw new ArgumentNullException();
            _afm = afm;
        }

        public override chGlyph GetGlyphInfo(int gid)
        {
            var g = _afm.GetGlyph(gid);
            return new chGlyph(gid, 1000, g.width, g.lly, g.ury, g.llx, g.width /*- g.llx*/ - g.urx);
        }

        public chGlyph GetGlyphInfo(string name)
        {
            return GetGlyphInfo(_afm.GetGID(name));
        }

        public chBox GetGlyphBox(int gid)
        {
            var g = _afm.GetGlyph(gid);
            return new chBox(1000, g.width, g.lly, g.llx, g.urx, g.ury);
        }

        public chBox GetGlyphBox(string name)
        {
            return GetGlyphBox(_afm.GetGID(name));
        }

        public override double GetKerning(int from_gid, int to_gid)
        {
            return 0;
        }

        public override double GetWidth(int gid)
        {
            return _afm.GetGlyph(gid).width / 1000d;
        }

        public double[] GetWidths(int first, int last, string[] encoding)
        {
            return _afm.GetWidths(first, last, encoding ?? _afm.Encoding);
        }
    }
}
