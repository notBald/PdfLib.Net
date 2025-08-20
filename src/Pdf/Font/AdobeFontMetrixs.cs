#define INCLUDE_BBOX
using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Res;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Util;

namespace PdfLib.Pdf.Font
{
    public class AdobeFontMetrixs
    {
        /// <summary>
        /// Note that AdobeFontMetrixs objects aren't usualy kept arround so
        /// it may be smarter to simply use a list.
        /// </summary>
        static WeakCache<string, AdobeFontMetrixs> _cache = new WeakCache<string, AdobeFontMetrixs>(0);

        private string _fontname;
        private bool _bold, _italic, _symbolic, _fixed_pitch;
        private xRect _fbbox;

        private double _ascender, _descender, _underline_position, _underline_thickness, _italic_angle;
        private double? _cap_height, _x_height;

        private Dictionary<string, AFMGlyphInfo> _glyfs;
        private string[] _encoding;
        private int[] _xref;

        /// <summary>
        /// This is a sugested substitute font, set to null
        /// if no suitable substitute is known.
        /// </summary>
        public readonly string SubstituteFont;

        /// <summary>
        /// TrueType font file name for built in fonts. Returns null if it don't know the name.
        /// </summary>
        public string SubstituteFile 
        { 
            get 
            {
                var ret = SubstituteFileImpl(FontName);
                if (ret == null) ret = SubstituteFont != null ? SubstituteFont.ToLower() : "";
                return ret;
            } 
        }

        private static string SubstituteFileImpl(string name)
        {
            switch (name.ToLower())
            {
                case "courier": return "cour";
                case "courier-bold": return "courbd";
                case "Courier-italic": return "couri";
                case "courier-boldoblique": return "courbi";
                case "helvetica": return "arial";
                case "helvetica-bold": return "arialbd";
                case "helvetica-oblique": return "ariali";
                case "helvetica-boldoblique": return "arialbi";
                case "times-roman":
                case "times": return "times";
                case "times-bold": return "timesbd";
                case "times-italic": return "timesi";
                case "times-bolditalic": return "timesbi";
                case "symbol": return "symbol";
                default: return null;
            }
        }

        public string[] Encoding { get { return _encoding; } }

        public double Ascender { get { return _ascender; } }
        public double Descender { get { return _descender; } }
        public double CapHeight 
        { 
            get 
            {
                if (_cap_height == null)
                    _cap_height = _symbolic ? _ascender : GetGlyph('H').ury;
                return _cap_height.Value; 
            } 
        }
        public double XHeight
        {
            get
            {
                if (_x_height == null)
                    _x_height = GetGlyph('x').ury;
                return _x_height.Value;
            } 
        }
        public string FontName { get { return _fontname; } }

        /// <summary>
        /// The ghost script name of this font, if any.
        /// </summary>
        public string GhostName
        {
            get
            {
            //+===============+========================+
            //| Base 14 name  | Ghostscript name       |
            //+===============+========================+
            //| Courier       |                        |
            //|    standard   | NimbusMonL-Regu        |
            //|    bold       | NimbusMonL-Bold        |
            //|    italic     | NimbusMonL-ReguObli    |
            //|    bolditalic | NimbusMonL-BoldObli    |
            //+---------------+------------------------+
            //| Helvetica     |                        |
            //|    standard   | NimbusSanL-Regu        |
            //|    bold       | NimbusSanL-Bold        |
            //|    italic     | NimbusSanL-ReguItal    |
            //|    bolditalic | NimbusSanL-BoldItal    |
            //+---------------+------------------------+
            //| Times-Roman   |                        |
            //|    standard   | NimbusRomNo9L-Regu     |
            //|    bold       | NimbusRomNo9L-Medi     |
            //|    italic     | NimbusRomNo9L-ReguItal |
            //|    bolditalic | NimbusRomNo9L-MediItal |
            //+---------------+------------------------+
            //| Symbol        | StandardSymL           |
            //+---------------+------------------------+
            //| ZapfDingbats  | Dingbats               |
            //+---------------+------------------------+
                switch (FontName)
                {
                    case "Courier": return "NimbusMonL-Regu";
                    case "Courier-Bold": return "NimbusMonL-Bold";
                    case "Courier-Italic": return "NimbusMonL-ReguObli";
                    case "Courier-BoldOblique": return "NimbusMonL-BoldObli";
                    case "Helvetica": return "NimbusSanL-Regu";
                    case "Helvetica-Bold": return "NimbusSanL-Bold";
                    case "Helvetica-Oblique": return "NimbusSanL-ReguItal";
                    case "Helvetica-BoldOblique": return "NimbusSanL-BoldItal";
                    case "Times-Roman":
                    case "Times": return "NimbusRomNo9L-Regu";
                    case "Times-Bold": return "NimbusRomNo9L-Medi";
                    case "Times-Italic": return "NimbusRomNo9L-ReguItal";
                    case "Times-BoldItalic": return "NimbusRomNo9L-MediItal";
                    case "Symbol": return "StandardSymL";
                    case "ZapfDingbats": return "Dingbats";
                    default: return "";
                }
            }
        }

        /// <summary>
        /// Truetype should be prefered
        /// </summary>
        public bool PreferTrueType { get; private set; }

        public double UnderlinePosition { get { return _underline_position; } }
        public double UnderlineThickness { get { return _underline_thickness; } }
        public bool IsFixedPitch { get { return _fixed_pitch; } }
        public xRect FontBBox { get { return _fbbox; } }

        /// <summary>
        /// Only relevant for symbolic fonts.
        /// </summary>
        public int[] XrefTable { get { return _xref; } }

        public bool Bold { get { return _bold; } }

        public bool Italic { get { return _italic; } }

        public bool Symbolic { get { return _symbolic; } }

        public double ItalicAngle { get { return _italic_angle; } }

        public Dictionary<string, AFMGlyphInfo> GlyfInfo { get { return _glyfs; } }

        #region Init

        private AdobeFontMetrixs(string substs, bool symbolic)
        { SubstituteFont = substs; _symbolic = symbolic; }

        /// <summary>
        /// Creates AFM from a string
        /// </summary>
        /// <param name="afm">AdobeFontMetrix string</param>
        public AdobeFontMetrixs(string afm)
        {
            Parse(new System.IO.StringReader(afm), this);
            _encoding = Enc.Standar;
        }

        #endregion

        public AFMGlyphInfo GetGlyph(char ch)
        {
            var name = _encoding[(int) ch];
            return _glyfs[name];
        }

        public AFMGlyphInfo GetGlyph(int ch)
        {
            var name = _encoding[ch];
            if (".notdef" == name)
                return new AFMGlyphInfo();
            return _glyfs[name];
        }

        public int GetGID(string name)
        {
            for (int c = 0; c < _encoding.Length; c++)
                if (name == _encoding[c])
                    return c;
            return 0;
        }

        /// <summary>
        /// Creates the widths array
        /// </summary>
        /// <param name="first">First character</param>
        /// <param name="after_last">After last charcter</param>
        /// <param name="missing_width">Set for characters outside of first and last</param>
        public double[] GetWidths(int first, int after_last, double missing_width)
        {
            var ret = new double[256];
            for (int c = 0; c < first; c++)
                ret[c] = missing_width;
            for (int c = first; c < after_last; c++)
            {
                var name = _encoding[c];
                AFMGlyphInfo glyph;
                if (_glyfs.TryGetValue(name, out glyph))
                    ret[c] = glyph.width;
                else
                    ret[c] = missing_width;
            }
            for (int c = after_last; c < 256; c++)
                ret[c] = missing_width;
            return ret;
        }

        internal double[] GetWidths(int first, int last)
        {
            return GetWidths(first, last, _encoding);
        }

        internal double[] GetWidths(int first, int last, string[] encoding)
        {
            var ret = new double[last - first + 1];
            for (int c = first; c <= last; c++)
            {
                var name = encoding[c];
                AFMGlyphInfo glyph;
                if (_glyfs.TryGetValue(name, out glyph))
                    ret[c - first] = glyph.width;
            }
            return ret;
        }

        /// <summary>
        /// Takes the settings defined in the type1 font over its own
        /// </summary>
        /// <param name="font">Font to copy settings from</param>
        internal void MergeWith(PdfType1Font font)
        {
            //Copies widths
            var w = font.Widths;
            if (w != null)
            {
                System.Diagnostics.Debug.Assert(false, "Untested code");
                var e = font.Encoding;
                var glyph_names = e.Differences;
                System.Diagnostics.Debug.Assert(w.Length == glyph_names.Length);
                for (int c = 0; c < w.Length; c++)
                {
                    var name = glyph_names[c];
                    AFMGlyphInfo glyph;
                    if (_glyfs.TryGetValue(name, out glyph))
                    {
                        int width = (int)(w[c] * 1000);
                        glyph.width = width;
                    }
                }
            }

            var fd = font.FontDescriptor;
            if (fd != null)
            {
                System.Diagnostics.Debug.Assert(false, "Untested code");
                _ascender = fd.Ascent;
                _descender = fd.Descent;
                _cap_height = fd.CapHeight;
                _fixed_pitch = fd.IsMonospaced;
                _italic = fd.IsItalic;
                _bold = fd.IsBold;
                _italic_angle = fd.ItalicAngle;
                _symbolic = fd.IsSymbolic;
                //Todo: Font box
            }
        }

        /// <summary>
        /// Creates a font discriptor from this afm
        /// </summary>
        /// <returns>A Font descriptor</returns>
        public PdfFontDescriptor CreateFontDescriptor()
        {
            var fd = new PdfFontDescriptor(FontName, Ascender, Descender, CapHeight);
            FontFlags flags = Symbolic ? FontFlags.Symbolic : FontFlags.Nonsymbolic;
            if (IsFixedPitch)
                flags |= FontFlags.FixedPitch;
            if (Italic)
                flags |= FontFlags.Italic;
            fd.Flags = flags;
            if (Bold)
                fd.FontWeight = 400;
            fd.ItalicAngle = ItalicAngle;
            return fd;
        }

        internal static string SaneFontName(string font_name, out bool bold, out bool italic)
        {
            if (font_name == null)
            {
                bold = italic = false;
                return null;
            }
            font_name = RmovePSMT(font_name).Replace(" ", "").ToLower();
            int i = font_name.IndexOfAny(new char[] { ',', '-' });
            if (i == -1)
            {
                bold = italic = false;
                return font_name;
            }
            var style = font_name.Substring(i);
            font_name = font_name.Substring(0, i);
            bold = style.IndexOf("bold") != -1;
            italic = style.IndexOf("italic") != -1 || style.IndexOf("oblique") != -1;
            return font_name;
        }

        internal static string RmovePSMT(string name)
        {
            if (name == null || name.Length < 5) return name;
            if (name.EndsWith("PSMT"))
                return name.Substring(0, name.Length - 4);
            if (name.EndsWith("MT"))
                name = name.Substring(0, name.Length - 2);
            if (name.EndsWith("PS"))
                name = name.Substring(0, name.Length - 2);
            return name;
        }

        /// <summary>
        /// This method is for creating fonts that don't exists in the specs
        /// </summary>
        /// <param name="name">Name of the font</param>
        /// <param name="enc">How characters are encoded</param>
        /// <param name="fd">Descriptor of the font</param>
        /// <returns></returns>
        internal static AdobeFontMetrixs Create(string name, PdfEncoding enc, PdfFontDescriptor fd)
        {
            if (fd != null)
            {
                string substitute_sugestion = (fd.IsSerif) ? "Times" : "Helvetica";
                if (fd.IsMonospaced)
                    substitute_sugestion = "Courier";
                string style = "";
                bool bold, italic;
                SaneFontName(fd.FontName, out bold, out italic);
                if (fd.IsBold || bold)
                    style = "Bold"; 
                if (fd.IsItalic || italic)
                    style += "Italic";
                if (style != "")
                    substitute_sugestion += "-" + style;

                var afm = Create(substitute_sugestion, enc, true);
                afm._italic = italic || fd.IsItalic;
                afm._fixed_pitch = fd.IsMonospaced;
                afm._bold = bold || fd.IsBold;
                afm._cap_height = fd.CapHeight;
                afm._italic_angle = fd.ItalicAngle;

                return afm;
            }
            else
            {
                var afm = Create("Helvetica", enc, true);
                SaneFontName(name, out afm._bold, out afm._italic);
                return afm;
            }
        }

        /// <summary>
        /// Sanitize the name and reads out font properties from that name
        /// </summary>
        internal static AFMFont NameToFont(string name)
        {
            if (name == null) throw new ArgumentNullException();
            bool perfect_fit = true;
            name = name.Replace(" ", "");

            //Removes MT
            if (name.Length > 3 && name[name.Length - 2] == 'M' && name[name.Length - 1] == 'T' )
            {
                name = name.Substring(0, name.Length - 2);
            }

            //Removes PS
            if (name.Length > 3 && name[name.Length - 2] == 'P' && name[name.Length - 1] == 'S')
            {
                name = name.Substring(0, name.Length - 2);
            }

            name = name.ToLower();

            var chunks = name.Split(new char[] { ',', '-' }, StringSplitOptions.RemoveEmptyEntries);

            bool bold = false, italic = false, oblique = false;
            int cc = 0;
            for (int c = 1; c < chunks.Length; c++)
            {
                var cur = chunks[c];
                bool concat = true;
                if (cur.Contains("bold"))
                {
                    bold = true;
                    concat = false;
                }
                if (cur.Contains("italic"))
                {
                    italic = true;
                    concat = false;
                }
                if (cur.Contains("oblique"))
                {
                    oblique = true;
                    concat = false;
                }
                /*if (cur.Contains("narrow"))
                {
                    perfect_fit = false;
                    oblique = true;
                    concat = false;
                }*/
                if (concat) cc++;
            }
            name = chunks[0];
            for (int c = 1; c <= cc; c++)
                name = name + chunks[c];
            if (oblique && bold)
                perfect_fit = false;

            return new AFMFont(name, italic, bold, oblique, perfect_fit);
        }

        /// <summary>
        /// Checks if the font name maps to the built in fonts
        /// </summary>
        private static void FitFontName(AFMFont font)
        {
            var name = font.Name;
            switch (name)
            {
                case "helvetica":
                case "times":
                case "courier":
                case "symbol":
                case "zapfdingbats":
                    return;

                case "timesroman":
                case "timesnewroman":
                    font.Name = "times";
                    return;
            }

            font.PerfectFit = false;
            if (name.StartsWith("arial") || name.StartsWith("helvetica"))
            {
                font.Name = "helvetica";
                font.PreferTrueType = name.StartsWith("arial");
                return;
            }

            if (name.StartsWith("times"))
            {
                font.Name = "times";
                font.PreferTrueType = name.StartsWith("timesn");
                return;
            }

            if (name.StartsWith("Courier"))
            {
                font.Name = "courier";
                font.PreferTrueType = name.StartsWith("couriern");
                return;
            }
        }

        /// <summary>
        /// Checks how a font name matches the built in font
        /// </summary>
        /// <returns>
        /// Name perfectly matches with a built in font, fnt.PerfectFit.
        /// Name imperfectly matches with a built in font, !fnt.PerfectFit.
        /// Name does not match at all, fnt.Name == null.
        /// </returns>
        internal static AFMFont IsPerfectFit(string name)
        {
            var fnt = NameToFont(name);
            FitFontName(fnt);
            if (fnt.PerfectFit)
                return fnt;
            switch (fnt.Name)
            {
                case "helvetica":
                case "courier":
                case "times":
                case "symbol":
                case "zapfdingbats":
                    return fnt;
            }

            fnt.Name = null;
            return fnt;
        }

        internal static AdobeFontMetrixs CreateHelvetica(string name, PdfEncoding enc)
        {
            var fnt = NameToFont(name);
            if (fnt.Italic || fnt.Oblique)
            {
                if (fnt.Bold)
                    return CreateImpl("Font.AFM.Helvetica-BoldOblique.afm", null, enc, "Arial", false, false);
                return CreateImpl("Font.AFM.Helvetica-Oblique.afm", null, enc, "Arial", false, false);
            }
            if (fnt.Bold)
                return CreateImpl("Font.AFM.Helvetica-Bold.afm", null, enc, "Arial", false, false);
            return CreateImpl("Font.AFM.Helvetica.afm", null, enc, "Arial", false, false);
        }

        /// <summary>
        /// Creates an AdobeFontMetrix for the given font
        /// </summary>
        /// <param name="name">Name of the font</param>
        /// <param name="enc">Optional encoding</param>
        /// <param name="imperfect_fit">If AFM that fit imperfectly with the name is to be used</param>
        /// <returns></returns>
        internal static AdobeFontMetrixs Create(string name, PdfEncoding enc, bool imperfect_fit)
        {
            var fnt = NameToFont(name);
            FitFontName(fnt);
            if (!fnt.PerfectFit && !imperfect_fit)
                return null;

            switch (fnt.Name)
            {
                case "helvetica":
                    if (fnt.Italic || fnt.Oblique)
                    {
                        if (fnt.Bold)
                            return CreateImpl("Font.AFM.Helvetica-BoldOblique.afm", null, enc, "Arial", false, fnt.PreferTrueType);
                        return CreateImpl("Font.AFM.Helvetica-Oblique.afm", null, enc, "Arial", false, fnt.PreferTrueType);
                    }
                    if (fnt.Bold)
                        return CreateImpl("Font.AFM.Helvetica-Bold.afm", null, enc, "Arial", false, fnt.PreferTrueType);
                    return CreateImpl("Font.AFM.Helvetica.afm", null, enc, "Arial", false, fnt.PreferTrueType);
                    
                case "courier":
                    if (fnt.Italic || fnt.Oblique)
                    {
                        if (fnt.Bold)
                            return CreateImpl("Font.AFM.Courier-BoldOblique.afm", null, enc, "Courier New", false, fnt.PreferTrueType);
                        return CreateImpl("Font.AFM.Courier-Oblique.afm", null, enc, "Courier New", false, fnt.PreferTrueType);
                    }
                    if (fnt.Bold)
                        return CreateImpl("Font.AFM.Courier-Bold.afm", null, enc, "Courier New", false, fnt.PreferTrueType);
                    return CreateImpl("Font.AFM.Courier.afm", null, enc, "Courier New", false, fnt.PreferTrueType);

                case "times":
                    if (fnt.Italic || fnt.Oblique)
                    {
                        if (fnt.Bold)
                            return CreateImpl("Font.AFM.Times-BoldItalic.afm", null, enc, "Times New Roman", false, fnt.PreferTrueType);
                        return CreateImpl("Font.AFM.Times-Italic.afm", null, enc, "Times New Roman", false, fnt.PreferTrueType);
                    }
                    if (fnt.Bold)
                        return CreateImpl("Font.AFM.Times-Bold.afm", null, enc, "Times New Roman", false, fnt.PreferTrueType);
                    return CreateImpl("Font.AFM.Times-Roman.afm", null, enc, "Times New Roman", false, fnt.PreferTrueType);

                case "symbol":  //Ignores encoding, as the calling function does not nessesarly know that this is a symbolic font
                                //so they probably don't want encoding set.
                    return CreateImpl("Font.AFM.Symbol.afm", Enc.Symbol, /*enc*/ null, "Symbol", true, false);

                case "zapfdingbats":
                    return CreateImpl("Font.AFM.ZapfDingbats.afm", Enc.Symbol, /*enc*/ null, "Symbol", true, false);
            }

            return null;
        }

        /// <summary>
        /// Assumes that the AFM file is well formed and that the character info
        /// is layed out as in the standar afm files. (I.e. this is not usuable
        /// as a generic AFM parser)
        /// </summary>
        private static AdobeFontMetrixs CreateImpl(string res_name, string[] alt_table, PdfEncoding enc, string subst, bool symbolic, bool prefer_tt)
        {
            AdobeFontMetrixs afm = _cache[res_name];
            if (afm != null)
            {
                afm = (AdobeFontMetrixs)afm.MemberwiseClone();
                afm._xref = null;
            }
            else
            {
                afm = new AdobeFontMetrixs(subst, symbolic);
                Parse(StrRes.GetResource(res_name), afm);
                
                //Using the afm object for caching, though it contains some
                //non cacheable data that must be removed.
                _cache[res_name] = afm;
            }

            //Symbolic and non-symbolic fonts are treated differently.
            //I.e. for non-symbolic: charcode -> basetable -> unicode value -> Font
            //             symbolic: charcode -> Font table (Symbolic or Zpaf) -> Font 
            if (symbolic)
            {
                //Sets up the names (used for generating widths)
                afm._encoding = (enc != null) ? enc.CreateDifferences((string[])alt_table.Clone()) : alt_table;

                //Creates a cross reference table. (Used by the WPF codepath?)
                afm._xref = PdfEncoding.CreateXRef(afm._encoding, alt_table);

                //Fetches name data from the font
                foreach (var glyph in afm.GlyfInfo)
                {
                    int cc = glyph.Value.charcode;
                    if (cc >= 0 && cc < 256)
                        afm._encoding[cc] = glyph.Key;
                }
            }
            else
            {
                afm._encoding = (enc != null) ? enc.Differences : Enc.Standar;
            }

            afm.PreferTrueType = prefer_tt;
            return afm;
        }

        private static void Parse(System.IO.TextReader afm_file, AdobeFontMetrixs afm)
        {
            while (afm_file.Peek() != -1)
            {
                var line = afm_file.ReadLine().Split(new char[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (line.Length > 0)
                {
                    switch (line[0])
                    {
                        case "FontName":
                            afm._fontname = line[1];
                            break;

                        case "Weight":
                            afm._bold = ("Bold".Equals(line[1]));
                            break;

                        case "ItalicAngle":
                            afm._italic_angle = gd(line[1]);
                            afm._italic = !("0".Equals(line[1]));
                            break;

                        case "FontBBox":
                            afm._fbbox = new xRect(gd(line[1]), gd(line[2]), gd(line[3]), gd(line[4]));
                            break;

                        case "Ascender":
                            afm._ascender = gd(line[1]);
                            break;

                        case "Descender":
                            afm._descender = gd(line[1]);
                            break;

                        case "CapHeight":
                            afm._cap_height = gd(line[1]);
                            break;

                        case "XHeight":
                            afm._x_height = gd(line[1]);
                            break;

                        case "UnderlinePosition":
                            afm._underline_position = gd(line[1]);
                            break;

                        case "UnderlineThickness":
                            afm._underline_thickness = gd(line[1]);
                            break;

                        case "IsFixedPitch":
                            afm._fixed_pitch = gb(line[1]);
                            break;

                        case "StartCharMetrics":
                            int num = int.Parse(line[1]);
                            var glyfs = new Dictionary<string, AFMGlyphInfo>(num); ;
                            afm._glyfs = glyfs;

                            for (int c = 0; c < num; )
                            {
                                line = afm_file.ReadLine().Split(new char[] { ' ' },
                                    StringSplitOptions.RemoveEmptyEntries);
                                if (line.Length > 0)
                                {
                                    c++;
                                    var name = line[7];

                                    var glyf = new AFMGlyphInfo();
                                    if (line[0] == "C")
                                        int.TryParse(line[1], out glyf.charcode);
                                    glyf.width = int.Parse(line[4]);
#if INCLUDE_BBOX
                                    glyf.llx = int.Parse(line[10]);
                                    glyf.lly = int.Parse(line[11]);
                                    glyf.urx = int.Parse(line[12]);
                                    glyf.ury = int.Parse(line[13]);
#endif
                                    glyfs[name] = glyf;
                                }
                            }

                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Creates an AFM file
        /// </summary>
        /// <param name="font">Font to retrive metrix from</param>
        /// <param name="enc">Optional additional encodings</param>
        /// <returns>AFM string</returns>
        public static string Create(PdfLib.Compose.cFont font, PdfEncoding enc)
        {
            var sb = new StringBuilder(16384);
            var name = font.Name;
            int ip = name.IndexOf('+');
            if (ip != -1)
                name = name.Substring(ip + 1);
            sb.AppendLine("StartFontMetrics 4.1");
            sb.Append("FontName "); sb.AppendLine(name.Replace(' ', '-'));
            sb.Append("FullName "); sb.AppendLine(name);
            sb.Append("FamilyName "); sb.AppendLine(name.Split(' ', '-')[0]);
            var fnt = NameToFont(name);
            sb.Append("Weight "); sb.AppendLine(fnt.Bold ? "Bold" : "Medium");
            var cult = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("ItalicAngle "); sb.AppendFormat(cult, "{0:0.##}\n", font.ItalicAngle);
            sb.Append("IsFixedPitch "); sb.AppendLine(font.IsFixedPitch ? "true" : "false");
            sb.Append("CharacterSet "); sb.AppendLine("WinANSI"); //Unsure about this one
            var fb = font.FontBBox;
            sb.AppendFormat("FontBBox {0:0} {1:0} {2:0} {3:0}\n", fb.LLx, fb.LLy, fb.URx, fb.URy); 
            sb.Append("UnderlinePosition "); sb.AppendFormat(cult, "{0:0.##}\n", font.UnderlinePosition * 1000);
            sb.Append("UnderlineThickness "); sb.AppendFormat(cult, "{0:0.##}\n", font.UnderlineThickness * 1000);
            sb.AppendLine("Version 001.000");
            sb.AppendLine("EncodingScheme AdobeStandardEncoding");
            sb.Append("CapHeight "); sb.AppendFormat("{0:0}\n", font.CapHeight * 1000);
            sb.Append("XHeight "); sb.AppendFormat("{0:0}\n", font.XHeight * 1000);
            sb.Append("Ascender "); sb.AppendFormat("{0:0}\n", font.Ascent * 1000);
            sb.Append("Descender "); sb.AppendFormat("{0:0}\n", font.Descent * 1000);
            var std_enc = Pdf.Font.Enc.Standar;
            int count = 149;
            //for (int c = 0; c < std_enc.Length; c++)
            //    if (std_enc[c] != ".notdef")
            //        count++;
            List<string> additional_encodings = null;
            if (enc != null)
            {
                var d_enc = enc.Differences;
                additional_encodings = new List<string>(64);
                for (int c = 0; c < d_enc.Length; c++)
                {
                    var glyph_name = d_enc[c];
                    if (glyph_name == ".notdef" || Util.ArrayHelper.HasValue(std_enc, glyph_name)) continue;
                    additional_encodings.Add(glyph_name);
                }
                count += additional_encodings.Count;
            }
            sb.Append("StartCharMetrics "); sb.AppendLine(count.ToString());
            var to_char = Pdf.Font.Enc.GlyphNames;
            for (int c = 0; c < std_enc.Length; c++)
            {
                var glyph_name = std_enc[c];
                if (glyph_name == ".notdef") continue;
                var ch = to_char[glyph_name];

                var glyph = font.GetGlyphBox(ch);
                sb.AppendFormat("C {0} ; WX {1:0} ; N {2} ; B {3:0} {4:0} {5:0} {6:0} ;\n", c, Math.Ceiling(glyph.Width * 1000), glyph_name, (int)(glyph.XMin * 1000), glyph.YMin * 1000, Math.Ceiling(glyph.XMax * 1000), glyph.YMax * 1000);
            }
            if (additional_encodings != null)
            {
                foreach (var glyph_name in additional_encodings)
                {
                    var ch = to_char[glyph_name];
                    var glyph = font.GetGlyphBox(ch);
                    sb.AppendFormat("C {0} ; WX {1:0} ; N {2} ; B {3:0} {4:0} {5:0} {6:0} ;\n", -1, Math.Ceiling(glyph.Width * 1000), glyph_name, (int)(glyph.XMin * 1000), glyph.YMin * 1000, Math.Ceiling(glyph.XMax * 1000), glyph.YMax * 1000);
                }
            }

            sb.AppendLine("EndCharMetrics");
            sb.AppendLine("EndFontMetrics");

            return sb.ToString();
        }

        private static double gd(string s)
        { return double.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }

        private static bool gb(string s)
        { return bool.Parse(s); }
    }

    /// <summary>
    /// Contains meta information about the glyph
    /// </summary>
    public struct AFMGlyphInfo
    {
        /// <summary>
        /// Total width, including whitespace
        /// </summary>
        public int width;

        public int charcode;

#if INCLUDE_BBOX
        /// <summary>
        /// The character's bounding box
        /// </summary>
        public int llx, lly, urx, ury;

        /// <summary>
        /// Start position of the colored area of the glyph
        /// </summary>
        public double X { get { return llx; } }

        /// <summary>
        /// Start position of the colored area of the glyph
        /// </summary>
        public double Y { get { return lly; } }

        /// <summary>
        /// The width of the colored area of the glyph
        /// </summary>
        public double Width { get { return urx - llx; } }

        /// <summary>
        /// Height of the colored area of the glyph
        /// </summary>
        public double Height { get { return ury - lly; } }
#endif
    }

    internal class AFMFont
    {
        public string Name;
        public readonly bool Italic, Oblique;
        public readonly bool Bold;

        /// <summary>
        /// When false, the built in font isn't an excact match.
        /// </summary>
        public bool PerfectFit, PreferTrueType;
        public AFMFont(string name, bool i, bool b, bool o, bool bf)
        { Name = name; Italic = i; Bold = b; Oblique = o; PerfectFit = bf; }
    }
}
