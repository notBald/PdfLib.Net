using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Font;
using PdfLib.Read.CFF;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using System.Diagnostics;

namespace PdfLib.Render.Font
{
    /// <remarks>
    /// See: "The Compact Font Format Specification.pdf"
    /// And: "Type2 - Charstring format.pdf"
    /// Note that unlike T1Fonts, all paths are closed.
    /// 
    /// Todo:
    ///   - I belive there's some CID stuff missing
    ///   
    /// Note:
    ///   - The glyph building code is in Read.CFF.GlyphParser
    /// </remarks>
    public class Type1CFont<Path> : rFont
    {
        #region Variables

        protected readonly IFontFactory<Path> _factory;

        /// <summary>
        /// Function for creating a glyph
        /// </summary>
        protected CreateSG<Path> _create_sg;

        /// <summary>
        /// Parsed font
        /// </summary>
        protected CFont<Path> _cfont;

        /// <summary>
        /// The font descriptor of this font file
        /// </summary>
        /// <remarks>
        /// A font file can have multiple descriptors, however this is the
        /// render portion of the code so we treat those conditions as
        /// having entierly sepperate files (which consumes more memory but
        /// isn't a common scenario anyway)
        /// </remarks>
        private PdfFontDescriptor _font_desc;

        /// <summary>
        /// The name of the glyphs
        /// </summary>
        internal string[] GlyphNames { get { return _cfont.GlyphNames; } }

        #endregion

        #region Properties

        public override bool Vertical
        {
            //Todo: support vertical Type1C fonts
            get { return false; }
        }

        internal override IFontFactory FontFactory => _factory;

        #endregion

        #region Init and Dispose

        internal Type1CFont(double[] widths, PdfFontDescriptor desc, PdfEncoding enc, IFontFactory<Path> factory)
            : this(widths, desc, enc, factory, null, true) { }

        /// <summary>
        /// Creates a Type1C render font
        /// </summary>
        /// <param name="widths">Character widths</param>
        /// <param name="desc">The font's descriptor</param>
        /// <param name="enc">How the characters are encoded</param>
        /// <param name="factory">Used to create the glyphs</param>
        /// <param name="font_file">
        /// Normaly the font file is taken from the descriptor, 
        /// but if a file is supplied here it will be used instead.
        /// </param>
        /// <param name="use_font_encoding">
        /// If the encoding inside the font file is to be used
        /// </param>
        internal Type1CFont(double[] widths, PdfFontDescriptor desc, PdfEncoding enc, IFontFactory<Path> factory, Stream font_file, bool use_font_encoding)
            : base(widths)
        {
            _factory = factory;

            //Technically the init code could have been placed here and
            //the "PdfFontDescriptor" dropped and forgotten. But I
            //prefere having a link back to the parent for debugging
            //purposes.
            _font_desc = desc;

            //Keeps around a copy of the uncompressed font file in memory. Should perhaps be some way
            //to purge this after rendering a PDF file. 
            Util.StreamReaderEx srex;
            if (font_file == null)
                srex = new Util.StreamReaderEx(_font_desc.FontFile3.Stream.DecodedStream);
            else
                srex = new Util.StreamReaderEx(font_file);

            _cfont = new CFont<Path>(srex, use_font_encoding, out _create_sg, factory.GlyphRenderer());

            if (desc != null && desc.IsSymbolic)
            {
                if (enc != null)
                {
                    //For symbolic fonts one handles "base table" details
                    //differently. AFAICT the base table is identical with
                    //the font's embeded encoding table unless a base
                    //encoding is set. SymbolicDifferences handles this.
                    var diff = enc.SymbolicDifferences;
                    var names = GlyphNames;
                    if (diff != null)
                    {
                        var encoding = _cfont.Encoding;
                        for (int c = 0; c < diff.Length; c++)
                        {
                            var d = diff[c];
                            if (d != null)
                            {
                                int glyph_index = Array.IndexOf(names, d);
                                if (glyph_index >= 0)
                                    encoding[c] = glyph_index;
                                else
                                {
                                    //Todo: Numeric differences. 
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (enc != null)
                {
                    //This represents the encoding used by the source bytes
                    var diff = enc.StdMergedEncoding;

                    //Names of the glyphs in the Type1C file
                    var names = GlyphNames;

                    //Translates from source bytes to glyph indexes
                    _cfont.Encoding = PdfEncoding.CreateXRef(diff, names);
                }
            }
        }

        protected Type1CFont(CIDFontType0 font, IFontFactory<Path> factory)
            : this(null, font.FontDescriptor, null, factory, null, false)
        { }

        protected Type1CFont(CIDFontType2 font, IFontFactory<Path> factory, Stream cff_font)
            : this(null, font.FontDescriptor, null, factory, cff_font, false)
        { }

        /// <summary>
        /// Init is always called before a font is to be used. Note that init can be called
        /// after dismiss when a font is to be reused
        /// </summary>
        protected sealed override void InitImpl()
        {

        }

        public override void Dismiss()
        {

        }

        /// <summary>
        /// Closes the underlying stream
        /// </summary>
        public override void DisposeImpl()
        {
            _cfont.Dispose();
        }

        #endregion

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            //Creates the figure
            bool space;
            var sg = _create_sg(ch, out space);
            //Debug.Assert(space == is_space, "Space in font, what to do?");
            //Answer: Nothing.

            return _factory.CreateGlyph(new Path[] { sg.SG }, new xPoint(_widths[ch] / 1000, 0), new xPoint(0, 0), sg.M, true, is_space);
        }
    }
}
