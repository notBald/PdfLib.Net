using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;
using PdfLib.Pdf.Font;
using PdfLib.Read.TrueType;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Render.Font
{
    /// <summary>
    /// TrueType font
    /// </summary>
    /// <remarks>
    /// A TrueType font file consists of a sequence of concatenated tables. The first
    /// table is special, but the rest can come in any order.
    /// 
    /// Each table is 32-bit aligned, with padding after the table if necessary.
    /// 
    /// When disposing, the stream used to construct this font will be closed.
    /// </remarks>
    public class TTFont<Path> : rFont
    {
        #region Variables

        /// <summary>
        /// The root of the TT font
        /// </summary>
        readonly TableDirectory _td;

        /// <summary>
        /// A cmap table for translating charcodes
        /// into glyph indexes
        /// </summary>
        CmapTable.CmapFormat _cmap;

        /// <summary>
        /// The function used for cmapping
        /// </summary>
        protected readonly CmapFunc _map;

        /// <summary>
        /// A table with glyphs
        /// </summary>
        GlyfTable _glyf;

        /// <summary>
        /// A table with glyph metrixs
        /// </summary>
        protected HmtxTable _hmtx;

        /// <summary>
        /// How big the measurments of
        /// the font is.
        /// </summary>
        protected readonly double _units_per_em;

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
        /// Used for creating the glyph geometry
        /// </summary>
        IFontFactory<Path> _factory;

        #endregion

        #region Properties

        public override bool Vertical
        {
            //Todo: support vertical fonts
            get { return false; }
        }

        public string FullName
        {
            get 
            { 
                return _td.Name.FullName; 
            }
        }

        internal sealed override IFontFactory FontFactory => _factory;

        #endregion

        #region Init

        internal TTFont(double[] widths, PdfType1Font font, IFontFactory<Path> factory)
            : this(new MemoryStream(font.FontDescriptor.FontFile2.DecodedStream), widths, font, factory)
        {
        }

        internal TTFont(Stream font_file, double[] widths, PdfType1Font font, IFontFactory<Path> factory)
            : base (widths == null ? new double[256] : widths)
        {
            _factory = factory;

            if (!font_file.CanRead || !font_file.CanSeek)
                throw new Exception();

            //The first of the tables is the font table directory, 
            //a special table that facilitates access to the other tables in the font. 
            _td = new TableDirectory(font_file);
            var fd = font.FontDescriptor;

            if (fd != null && (font.FontDescriptor.Flags & FontFlags.Symbolic) == FontFlags.Symbolic)
                _map = FetchCmap(true, font.Encoding);
            else
                _map = FetchCmap(false, font.Encoding);
            _glyf = _td.Glyf;
            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;

            //If the width array is null we fill it out using the font's width data.
            if (widths == null)
            {
                for (int c = 0; c < _widths.Length; c++)
                    _widths[c] = _hmtx.Lhmx[_map(c)].AdvanceWidth/* * 1000*/ / _units_per_em;
            }
        }

        internal TTFont(double[] widths, TableDirectory td, IFontFactory<Path> factory, bool is_symbolic, PdfEncoding enc)
            : base(widths == null ? new double[256] : widths)
        {
            _factory = factory;

            //The first of the tables is the font table directory, 
            //a special table that facilitates access to the other tables in the font. 
            _td = td;

            _map = FetchCmap(is_symbolic, enc);
            _glyf = _td.Glyf;
            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;

            //If the width array is null we fill it out using the font's width data.
            if (widths == null)
            {
                for (int c = 0; c < _widths.Length; c++)
                    _widths[c] = _hmtx.Lhmx[_map(c)].AdvanceWidth/* * 1000*/ / _units_per_em;
            }
        }

        protected TTFont(CIDFontType2 font, IFontFactory<Path> factory)
            : base(null)
        {
            _factory = factory;

            var fd = font.FontDescriptor;
            var font_file = new MemoryStream(font.FontDescriptor.FontFile2.DecodedStream);
            if (!font_file.CanRead || !font_file.CanSeek)
                throw new Exception();
            _td = new TableDirectory(font_file);
            //_map = FetchCmap((fd.Flags & FontFlags.Symbolic) == FontFlags.Symbolic, null);
            _glyf = _td.Glyf;
            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;
        }

        protected TTFont(Stream font_file, IFontFactory<Path> factory)
            : base(null)
        {
            _factory = factory;

            if (!font_file.CanRead || !font_file.CanSeek)
                throw new Exception();
            _td = new TableDirectory(font_file);
            //_map = FetchCmap((fd.Flags & FontFlags.Symbolic) == FontFlags.Symbolic, null);
            _glyf = _td.Glyf;
            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;

            Debug.Assert(_td.Vmtx == null, "We have vertical metrics");
        }

        protected TTFont(TableDirectory td, IFontFactory<Path> factory, bool symbolic)
            : base(null)
        {
            _factory = factory;

            _td = td;
            //_map = FetchCmap((fd.Flags & FontFlags.Symbolic) == FontFlags.Symbolic, null);
            _glyf = _td.Glyf;
            _hmtx = _td.Hmtx;
            _units_per_em = (double)_td.Head.UnitsPerEm;
            _map = FetchCmap(symbolic, null);

            Debug.Assert(_td.Vmtx == null, "We have vertical metrics");
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
            //System.Diagnostics.Debug.Assert(is_space == (_name_table != null && "space".Equals(_name_table[raw])), "Space in font, what to do?");
            // The file universalDeclarationOfHumanRights_french.pdf has this. 
            //Answer: Don't do anything. 

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
                    //Debug.Assert(scale_factor_w < 1.01 && scale_factor_w > 0.99, "Just testing scaling in TTFont.RenderGlyph");
                    //st = st.ScalePrepend(scale_factor_w, 1);
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
            var p = _glyf.GetGlyph(gid);
            
            if (p == null) return null;
            var r = new TTGlyphRender(_factory);
            if (!(p is CompositeGlyphs))
                return new Path[] { r.Render((SimpleGlyph)p) };
            else
            {
                //Composite glyps consists of several "simple" glyphs, with an equal
                //amount of transforms. This function renders the simple glyphs and
                //calculates the transform for each glyph
                var cgs = (CompositeGlyphs)p;
                var ret = new Path[cgs.Count];
                
                for (int i = 0; i < ret.Length; i++)
                {
                    var cg = cgs.Glyphs[i];

                    //Telling the renderer to fetch htmx/vtmx data from this
                    //glyph instead of the component glyph. (vtmx data is not
                    //currently used though so this has no impact on vertical
                    //fonts)
                    if ((cg.Flags & CompositeGlyphs.Flag.USE_MY_METRICS) != 0)
                        metrics_index = cg.GlyphIndex;

                    var gd = _glyf.GetGlyph(cg.GlyphIndex);
                    if (gd is SimpleGlyph gds)
                    {
                        var sg = r.Render(gds);

                        _factory.SetTransform(sg, cg.Transform);

                        ret[i] = sg;
                    }
                    else
                    {
                        //See example_018 - Nested composite.pdf for an example of a document with
                        //composite glyphs pointing at other composite glyphs. I'm not sure, but maybe
                        //this function below should be recursive? 
                        var comp2 = (CompositeGlyphs)gd;
                        Array.Resize(ref ret, ret.Length + comp2.Count - 1);
                        foreach(var simple in comp2.Glyphs)
                        {
                            gd = _glyf.GetGlyph(simple.GlyphIndex);
                            var sg = r.Render((SimpleGlyph) gd);

                            _factory.SetTransform(sg, cg.Transform);

                            ret[i++] = sg;
                        }

                        i--;
                    }
                }

                return ret;
            }
        }

        /// <summary>
        /// A TrueType glyph contour is made up of both straight line segments and quadratic Bézier curves.
        /// 
        /// It is basically a list of control points, with each point explicitly marked as either 
        ///  (1) lying on the curve 
        ///  (2) off-curve. 
        ///  Any combination of on and off points is acceptable when defining a curve. 
        ///  The path is always closed, meaning that the last point is implicitly connected to the first.
        ///  
        ///                                 *       # Point on curve
        ///                                         * Point off curve
        /// Fig1                   Fig2  __---__
        /// #-__                      _--       -_
        ///     --__                _-            -
        ///         --__           #               \
        ///             --__                        #
        ///                 -#
        ///                        Two 'on' points
        /// Two 'on' points        and one 'off' point
        ///                        between them
        ///
        /// Fig3         *
        /// #            __      Two 'on' points with two 'off'
        ///  \          -  -     points between them.  The point
        ///   \        /    \    marked '0' is the middle of the
        ///    -      0      \   'off' points, and is a 'virtual
        ///     -_  _-       #   on' point. It does not appear
        ///                      in the point list.
        ///       *              
        /// </summary>
        class TTGlyphRender
        {
            #region Variables

            IFontFactory<Path> _factory;
            IGlyphDraw sgc;
            
            /// <summary>
            /// First point that makes up the curve of the
            /// current segment being rendered.
            /// 
            /// This needs to be tracked for those instances
            /// where the last "line" curves back to the first 
            /// point.
            /// </summary>
            /// <remarks>
            /// There can be both be a first off and on point, 
            /// so they have to be tracked sepperatly.
            /// 
            /// This happens when the first in the list is an
            /// off point, i.e. when the first point is a 
            /// control point for the last curve to be drawn.
            /// </remarks>
            xPoint FirstOn, FirstOff;

            /// <summary>
            /// If the first point is on the curve
            /// </summary>
            bool HasFirstOn = false;

            /// <summary>
            /// If the first point is off the curve
            /// </summary>
            bool HasFirstOff = false;

            /// <summary>
            /// The previous point that was off the curve
            /// of the current segment
            /// </summary>
            xPoint PrevOff;

            /// <summary>
            /// If there is a previous point off the curve
            /// </summary>
            bool HasPrev = false;

            #endregion

            #region Init

            public TTGlyphRender(IFontFactory<Path> factory)
            {
                _factory = factory;
            }

            #endregion

            /// <summary>
            /// Renders a glyph into a StreamGeometry
            /// </summary>
            public Path Render(SimpleGlyph g)
            {
                if (g == null)
                    return _factory.CreateEmptyPath();
                var sg = _factory.GlyphRenderer();

                //Number of points
                int n_points = g.NumPoints;

                //Number of contours
                int n_contours = 0;

                //Where to stop and terminate a curve
                int next_end = g.EndPtsOfContours[n_contours];

                double pX = 0, pY = 0;

                using (sgc = sg.Open())
                {
                    //Itterates over every point
                    for (int c = 0; c < n_points; c++)
                    {
                        //Gets a point
                        pX += g.XCoords[c];
                        pY += g.YCoors[c];

                        //Adds the point depending on it
                        //being on or off the curve
                        if ((g.Flags[c] & SimpleGlyph.Flag.onCurve) != 0)
                            AddOn(new xPoint(pX, pY));
                        else
                            AddOff(new xPoint(pX, pY));

                        //check if the point is the end of the contour
                        if (c == next_end)
                        {
                            //In some cases the glyph may want to draw
                            //a curve back to the origin, using the first
                            //off curve point as a control point
                            if (HasFirstOff)
                                AddOff(FirstOff);

                            //There may be a control point so we close the
                            //figure "manually". (Otherwise StreamGeometry
                            //draws a straight line)
                            if (HasFirstOn)
                                AddOn(FirstOn);

                            //Resets flags (Note, TTGlyphRender may be reused
                            //so this must always be done. A corrupt glyph 
                            //could end the for loop early, but any error will
                            //only affect the character being rendered)
                            HasFirstOn = false;
                            HasFirstOff = false;
                            HasPrev = false;

                            //Finds the end point of the next segment
                            if (c + 1 < n_points)
                                next_end = g.EndPtsOfContours[++n_contours];
                        }
                    }

                    return sg.GetPath();
                }
            }

            /// <summary>
            /// Adds a point that is on the curve
            /// </summary>
            private void AddOn(xPoint p)
            {
                //We move to the first point of the segment
                if (!HasFirstOn)
                {
                    //The figure always starts with a "on curve" point, even if
                    //the first point given is off curve. 
                    FirstOn = p;
                    HasFirstOn = true;
                    sgc.BeginFigure(p, true, false);
                }
                else if (HasPrev)
                {
                    //If there is a control point, use it to draw
                    //a curved line.
                    sgc.QuadraticBezierTo(PrevOff, p, true, false);
                    HasPrev = false;
                }
                //If there's no control point, draw a straight line from
                //the current point to new point
                else
                    sgc.LineTo(p, true, false);
            }

            /// <summary>
            /// Adds a point that is off the curve
            /// </summary>
            private void AddOff(xPoint p)
            {
                if (HasPrev)
                {
                    //If there's a previous point already, interpolate
                    //a mid point between the two control points (see
                    //Fig3)
                    xPoint curve_end = new xPoint((p.X + PrevOff.X) / 2,
                            (p.Y + PrevOff.Y) / 2);

                    //And treat it like a point on the curve.
                    AddOn(curve_end);
                    //Note that if a glyph begins with two offset points
                    //the figure will begin at this interpolated point.
                }
                else if (!HasFirstOn)
                {
                    //In cases where the first point of the figure begins 
                    //with an offset point, said point must be tracked so 
                    //that it can be used when drawing the last curve.
                    FirstOff = p;
                    HasFirstOff = true;
                }

                //Keeps track of the last off curve point.
                PrevOff = p;
                HasPrev = true;
            }
        }

        #endregion

        #region Convert to unicode

        public override string GetUnicode(byte[] str)
        {
            //var cmap = _td.Cmap;
            //if (cmap != null)
            //{
            //    var map = cmap.GetCmap(Read.TrueType.PlatformID.Microsoft, 1);
            //    throw new NotImplementedException();
            //}

            //Last resort
            return Read.Lexer.GetString(str);
        }

        #endregion

        #region CMap

        //Used for cmapping as the various cmaps functions
        //in different ways.
        public delegate int CmapFunc(int ch);

        /// <summary>
        /// Function used to map symbolic fonts into
        /// a Microsoft cmap
        /// </summary>
        private int MapSymbol_30(int ch)
        {
            //We move the character into the first range
            //by slapping on a prefix
            var prefix = _cmap.Ranges[0].Prefix;
            return _cmap.GetGlyphIndex((prefix << 8) | ch);
        }

        /// <summary>
        /// Maps using the encoding array
        /// </summary>
        private int Map_enc(int ch)
        {
            return _cmap.GetGlyphIndex(_enc[ch]);
        }

        private CmapFunc FetchCmap(bool symbolic, PdfEncoding enc)
        {
            //Characters in the PDF file is in some unknown encoding.
            //We want to translate those code into characters the font
            //can understand.
            //
            //For that we use the font's internal character map (CMap)
            //
            //In case of symbolic fonts we can assume that the cmap 
            //understands the characters we're feeding it, not so with
            //non-symbolic where the text has been encoded into one of
            //four supported formats. In that case we first need to
            //translate the character into one the cmap understands.
            //
            //We treat symbolic fonts with a "encoding" as nonsymbolic

            if (symbolic)
            {
                //For symbolic fonts one ignore "Encoding". I.e. the font charcodes
                //in the PDF file mach 1 to 1 with the cmap.
                if (enc != null)
                {
                    //For symbolic fonts, one are to ignore the encoding. However, it may still be
                    //nessesary to use it.

                    //Check if the font understands unicode
                    _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Microsoft, (ushort)MicrosoftEncodingID.encodingUGL);
                    if (_cmap != null)
                    {
                        //This font understands unicode. We'll therefore treat it as non-symbolic
                        goto NonSymbolic; 
                    }
                }

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
            
            NonSymbolic:
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
                    _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Microsoft, (int)MicrosoftEncodingID.encodingUGL);
                    if (_cmap != null) return _cmap.GetGlyphIndex;

                    //Then a 1, 0 charmap
                    _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Macintosh, (int)MacintoshEncodingID.encodingRoman);
                    if (_cmap != null) return _cmap.GetGlyphIndex;

                    throw new NotImplementedException("Set up alternate symbolic cmap");
                }

                //When there's an encoding, we need to build an encoding
                //table.
                var base_enc = enc.GetBaseEncoding();

                if (base_enc == PdfBaseEncoding.WinAnsiEncoding || base_enc == PdfBaseEncoding.MacRomanEncoding || base_enc == PdfBaseEncoding.StandardEncoding)
                {
                    //We get the names of the glyphs
                    _name_table = enc.StdMergedEncoding;

                    //Check if the font understands unicode
                    _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Microsoft, (ushort)MicrosoftEncodingID.encodingUGL);
                    if (_cmap != null)
                    {
                        //Sets up the PDF charcode to Cmap charcode translation
                        //table. (CMap 3.1 needs to be fed unicode data)
                        _enc = PdfEncoding.CreateUnicodeEnc(_name_table);

                        return Map_enc;
                    }

                    //Checks if the font understands MacRoman. I presume MacRoman is
                    //checked last since it only supports 256 glyphs in its Cmap.
                    _cmap = _td.Cmap.GetCmap(PdfLib.Read.TrueType.PlatformID.Macintosh, (ushort)MacintoshEncodingID.encodingRoman);
                    if (_cmap != null)
                    {
                        //Sets up encoding table for charcode -> macroman
                        _enc = PdfEncoding.CreateXRef(_name_table, Enc.MacRoman);

                        return Map_enc;
                    }

                    _cmap = _td.Cmap.FirstCmap; ;
                    if (_cmap != null)
                    {
                        if (_cmap.Unicode)
                        {
                            //Sets up the PDF charcode to Cmap charcode translation
                            //table. (CMap 3.1 needs to be fed unicode data)
                            _enc = PdfEncoding.CreateUnicodeEnc(_name_table);

                            return Map_enc;
                        }
                    }

                    throw new TTException("No usable cmap found");
                }

                //Todo: Same for MacExpert? Need a test file
                throw new NotImplementedException();
            }
        }

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="Path">Type of geometry this class will make</typeparam>
    public sealed class MemTTFont<Path> : TTFont<Path>
    {
        private readonly ushort[] _cid_to_gid;

        public MemTTFont(TableDirectory td, IFontFactory<Path> factory, bool symbolic, ushort[] cid_to_gid)
            : base(td, factory, symbolic)
        {
            _cid_to_gid = cid_to_gid;
        }

        protected override rGlyph GetGlyph(int ch, bool is_space)
        {
            int g_index = ch;
            if (_cid_to_gid != null)
                g_index = _cid_to_gid[ch];
            else
                g_index = _map(ch);
            double w;
            if (_widths != null)
                w = _widths[ch];
            else
                w = _hmtx.Lhmx[g_index].AdvanceWidth / _units_per_em;

            return RenderGlyph(g_index, w, 0, 0, 0, is_space);
        }
    }
}
