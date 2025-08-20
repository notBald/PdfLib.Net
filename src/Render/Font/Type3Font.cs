using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Util;
using PdfLib.Pdf.Font;
using PdfLib.PostScript;
using PdfLib.PostScript.Font;
using PdfLib.Render.Commands;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Render.Font
{
    /// <remarks>
    /// Todo: There should probably also be CIDType3 fonts
    /// </remarks>
    public sealed class Type3Font : rFont
    {
        #region Variables and properties

        public override bool Vertical { get { return false; } }

        readonly RenderCMD[][] _encoding;
        readonly string[] _names;

        public readonly PdfRectangle FontBBox;
        public readonly xMatrix FontMatrix;

        /// <summary>
        /// Font factory is currently irelevant for type 3 fonts.
        /// </summary>
        internal override IFontFactory FontFactory => null;

        #endregion

        #region Init

        internal Type3Font(double[] widths, PdfFontDescriptor desc, PdfEncoding enc, Dictionary<string, RenderCMD[]> glyphs, PdfRectangle BBox, xMatrix Matrix)
            : base(widths)
        {
            _names = enc.Differences;
            _encoding = new RenderCMD[256][];
            for (int c = 0; c < 256; c++)
            {
                var name = _names[c];
                RenderCMD[] glyph;
                if (glyphs.TryGetValue(name, out glyph))
                    _encoding[c] = glyph;
                else
                    _encoding[c] = new RenderCMD[0];
            }

            FontBBox = BBox;
            FontMatrix = Matrix;
        }

        /// <summary>
        /// Init is always called before a font is to be used. Note that init can be called
        /// after dispose when a font is to be reused
        /// </summary>
        protected sealed override void InitImpl()
        {

        }

        public override void Dismiss()
        {

        }

        public override void DisposeImpl()
        {

        }

        #endregion

        new public T3Glyph[] GetGlyphs(string str)
        {
            return GetGlyphs(Read.Lexer.GetBytes(str));
        }

        new public T3Glyph[] GetGlyphs(byte[] str)
        {
            var glyphs = new T3Glyph[str.Length];
            for (int c = 0; c < str.Length; c++)
            {
                int ch = str[c];
                if (ch > 255)
                {
                    //Todo: Log out of range character
                    ch = 0;
                }

                glyphs[c] = GetT3Glyph(ch);
            }

            return glyphs;
        }

        private T3Glyph GetT3Glyph(int ch)
        {
            var glyph_name = _names[ch];
            var glyph_width = _widths[ch];
            var glyph_cmds = _encoding[ch];

            //Todo: Perhaps use ch == 32 for detecting space instead?
            //Answer: Just drop space detection. It's handled by the caller
            return new T3Glyph(glyph_cmds, "space".Equals(glyph_name), glyph_width, 0);
        }

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            throw new NotSupportedException();
        }
    }

    public class T3Glyph
    {
        public readonly double MoveX, MoveY;
        public readonly bool IsSpace;
        public readonly RenderCMD[] Commands;
        internal T3Glyph(RenderCMD[] cmds, bool space, double move_x, double move_y)
        { Commands = cmds; IsSpace = space; MoveX = move_x; MoveY = move_y; }
    }
}
