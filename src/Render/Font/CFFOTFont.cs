using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;
using PdfLib.Pdf.Font;
using PdfLib.Read.CFF;
using PdfLib.Read.TrueType;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Render.Font
{
    /// <summary>
    /// OpenType font with compact font format glyfs
    /// </summary>
    /// <remarks>Based on TTFont</remarks>
    public class CFFOTFont<Path> : rFont
    {
        #region Variables
        protected IFontFactory<Path> _factory;

        /// <summary>
        /// The root of the TT font
        /// </summary>
        readonly TableDirectory _td;

        /// <summary>
        /// Parsed font
        /// </summary>
        protected CFont<Path> _cfont;

        /// <summary>
        /// A cmap table for translating charcodes
        /// into glyph indexes
        /// </summary>
        CmapTable.CmapFormat _cmap;

        /// <summary>
        /// The function used for cmapping
        /// </summary>
        readonly CmapFunc _map;

        /// <summary>
        /// A table with glyph metrixs
        /// </summary>
        HmtxTable _hmtx;

        /// <summary>
        /// How big the measurments of
        /// the font is.
        /// </summary>
        readonly double _units_per_em;

        /// <summary>
        /// A encoding table for translating charcodes
        /// from the PDF into something the cmap understands
        /// </summary>
        int[] _enc;

        /// <summary>
        /// Only used to check if a character is "space"
        /// </summary>
        string[] _name_table;

        /// <summary>
        /// Function for creating a glyph
        /// </summary>
        protected CreateSG<Path> _create_sg;

        #endregion

        #region Properties

        public override bool Vertical
        {
            //Todo: support vertical fonts
            get { return false; }
        }

        internal sealed override IFontFactory FontFactory => _factory;

        #endregion

        #region Init

        internal CFFOTFont(double[] widths, PdfType1Font font, IFontFactory<Path> factory)
            : base (widths)
        {
            _factory = factory;

            var fd = font.FontDescriptor;
            var font_file = new MemoryStream(fd.FontFile2.DecodedStream);

            //The first of the tables is the font table directory, 
            //a special table that facilitates access to the other tables in the font. 
            _td = new TableDirectory(font_file);

            if ((fd.Flags & FontFlags.Symbolic) == FontFlags.Symbolic)
                _map = FetchCmap(true, null);
            else
                _map = FetchCmap(false, font.Encoding);
            _cfont = new CFont<Path>(new StreamReaderEx(_td.GetTableData(Tag.CFF, true)), true, out _create_sg, factory.GlyphRenderer());

            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;
            
        }

        internal CFFOTFont(double[] widths, TableDirectory td, IFontFactory<Path> factory, bool symbolic, PdfEncoding enc)
            : base(widths)
        {

            _factory = factory;

            //The first of the tables is the font table directory, 
            //a special table that facilitates access to the other tables in the font. 
            _td = td;
            _map = FetchCmap(symbolic, enc);

            _cfont = new CFont<Path>(new StreamReaderEx(_td.GetTableData(Tag.CFF, true)), true, out _create_sg, factory.GlyphRenderer());

            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;

        }

        protected CFFOTFont(CIDFontType2 font, IFontFactory<Path> factory)
            : base(null)
        {
            _factory = factory;

            var fd = font.FontDescriptor;
            var font_file = new MemoryStream(font.FontDescriptor.FontFile2.DecodedStream);
            if (!font_file.CanRead || !font_file.CanSeek)
                throw new Exception();
            _td = new TableDirectory(font_file);
            //_map = FetchCmap((fd.Flags & FontFlags.Symbolic) == FontFlags.Symbolic, null);
            _cfont = new CFont<Path>(new StreamReaderEx(_td.GetTableData(Tag.CFF, true)), false, out _create_sg, factory.GlyphRenderer());

            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;
            _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Microsoft, 1);
        }

        /// <summary>
        /// Init is always called before a font is to be used. Note that init will be called
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
            _td.Dispose();
        }

        /// <summary>
        /// For internal use only. 
        /// </summary>
        protected object GetTD() { return _td; }

        #endregion

        #region Glyph rendering

        protected override rGlyph GetGlyph(int raw, bool is_space)
        {
            System.Diagnostics.Debug.Assert(is_space == (_name_table != null && "space".Equals(_name_table[raw])), "Space in font, what to do?");
            // The file universalDeclarationOfHumanRights_french.pdf has this. 

            //At this point the charcode is in some "unknown" format.
            //First thing we want to do is to translate it into a
            //known format.
            int cid = _map(raw);

            return RenderGlyph(cid, _widths[raw] / 1000, 0, 0, 0,
                is_space);
        }

        /// <summary>
        /// Renders a true type glyph
        /// </summary>
        /// <param name="glyph_index">The index of the glyph in the glyph table</param>
        /// <param name="width">Width of the glyph as given by the PDF file</param>
        /// <param name="space">Whenever this glyph is a space charcter or not</param>
        /// <returns>A rendered glyph</returns>
        protected rGlyph RenderGlyph(int glyph_index, double width, double height, double origin_x, double origin_y,  bool space)
        {
            //Renders the glyph
            int metrics_index;
            var path = CreateGeometry(glyph_index, out metrics_index);

            //Sets the transform
            if (path != null)
            {
                xMatrix st = xMatrix.Identity; //Scale Transform

                // Creates needed scale transform
                // Todo: Impelement Use_My_Metrixs

                var advanced_width = _hmtx.Lhmx[metrics_index].AdvanceWidth;
                if (advanced_width != 0 && width != 0)
                {
                    double advance = advanced_width / _units_per_em;

                    double scale_factor = 1 / _units_per_em;

                    //Scales the glyph down to 1x1
                    st = st.ScalePrepend(scale_factor, scale_factor);

                    //Scales the glyph up/down to width, if the glyph's internal
                    //width differs from what the PDF file supplied.
                    //
                    //Not sure if this is a good idea though
                    double scale_factor_w = width / advance;
                    Debug.Assert(scale_factor_w < 1.01 && scale_factor_w > 0.99, "Just testing scaling in TTFont.RenderGlyph");
                    st = st.ScalePrepend(scale_factor_w, 1);
                }
                else
                {
                    //If width or advanced_width with are zero we get NaN
                    //so I ignore the width scaling.
                    double scale_factor = 1 / _units_per_em;
                    st = st.ScalePrepend(scale_factor, scale_factor);
                }

                return _factory.CreateGlyph(path, new xPoint(width, height), new xPoint(origin_x, origin_y), st, true, space);
            }

            //Returns empty glyph
            return _factory.CreateGlyph(new Path[] { _factory.CreateEmptyPath() }, new xPoint(width, height), new xPoint(origin_x, origin_y), xMatrix.Identity, true, space);
        }

        /// <summary>
        /// Creates the geometry of the given character
        /// </summary>
        /// <param name="gid">Glyph to render</param>
        /// <param name="metrics_index">Where to get the metrics for this glyph</param>
        /// <returns>All geometry and transforms needed
        /// for rendering the character</returns>
        private Path[] CreateGeometry(int gid, out int metrics_index)
        {
            metrics_index = gid;
            bool space;
            var sg = _create_sg(gid, out space);

            return new Path[] { sg.SG };
        }

        #endregion

        #region CMap

        //Used for cmapping as the various cmaps functions
        //in different ways.
        delegate int CmapFunc(int ch);

        /// <summary>
        /// Function used to map symbolic fonts into
        /// a Microsoft cmap
        /// </summary>
        private int MapSymbol_30(int ch)
        {
            var prefix = _cmap.Ranges[0].Prefix;
            return _cmap.GetGlyphIndex((prefix << 8) | ch);
        }

        private int Map_enc(int ch)
        {
            return _cmap.GetGlyphIndex(_enc[ch]);
        }

        private CmapFunc FetchCmap(bool symbolic, PdfEncoding enc)
        {
            //Characters in the PDF file is in some unknown encoding.
            //We want to translate those code into characters the font
            //can understand.

            if (symbolic)
            {
                //For symbolic fonts one ignore "Encoding". I.e. the font charcodes
                //in the PDF file mach 1 to 1 with the cmap.
                //Todo: set up the _name_table somehow

                //First we look for a 3, 0 charmap
                _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Microsoft, (int)MicrosoftEncodingID.encodingUndefined);

                if (_cmap != null)
                {
                    //For this cmap, charcodes must be mapped into the correct range.
                    return MapSymbol_30;
                }

                //Then we look for a 1, 0 charmap
                _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Macintosh, (int)MacintoshEncodingID.encodingRoman);

                if (_cmap != null)
                    return _cmap.GetGlyphIndex;

                throw new NotImplementedException("Set up alternate symbolic cmap");
            }
            else
            {

                //First we determine the type of encoding used in the PDF file.
                if (enc == null)
                {
                    //If not encoding it set, one are to assume the font's
                    //built in encoding (when dealing with embeded fonts)
                    //
                    //I assume that means one can use the characters "as is",
                    //but what cmap to use?
                    //
                    //I'm guessing: 

                    //First we look for a 3, 1 charmap
                    //_cmap = _td.Cmap.GetCmap(TrueType.PlatformID.Microsoft, (int)TrueType.MicrosoftEncodingID.encodingUGL);
                    //if (_cmap != null) return _cmap.GetGlyphIndex;

                    ////Then a 1, 0 charmap
                    //_cmap = _td.Cmap.GetCmap(TrueType.PlatformID.Macintosh, (int)TT.MacintoshEncodingID.encodingRoman);
                    //if (_cmap != null) return _cmap.GetGlyphIndex;

                    throw new NotImplementedException("Set up alternate symbolic cmap");
                }

                //When there's an encoding, we need to build an encoding
                //table.
                var base_enc = enc.GetBaseEncoding();

                if (base_enc == PdfBaseEncoding.WinAnsiEncoding || base_enc == PdfBaseEncoding.MacRomanEncoding || base_enc == PdfBaseEncoding.StandardEncoding)
                {
                    //Names are needed to check if a character equals space
                    _name_table = enc.StdMergedEncoding;

                    //Check if the font understands WinAnsi
                    _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Microsoft, (ushort)MicrosoftEncodingID.encodingUGL);
                    if (_cmap != null)
                    {
                        //Sets up the PDF charcode to Cmap charcode translation
                        //table.
                        _enc = PdfEncoding.CreateUnicodeEnc(_name_table);

                        return Map_enc;
                    }

                    //Check if the font understands MacRoman. I presume MacRoman is
                    //checked last since it only supports 256 glyphs in its Cmap.
                    _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Macintosh, (ushort)MacintoshEncodingID.encodingRoman);
                    if (_cmap != null)
                    {
                        //Sets up encoding table for charcode -> macroman
                        _enc = PdfEncoding.CreateXRef(_name_table, Enc.MacRoman);

                        return Map_enc;
                    }

                    throw new TTException("No usable cmap found");
                }

                //Todo: Same for MacExpert?
                throw new NotImplementedException();
            }
        }

        #endregion
    }
}
