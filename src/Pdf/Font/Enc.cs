//#define LIMIT_TO_SID
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLib.Res;

namespace PdfLib.Pdf.Font
{
    /// <summary>
    /// Contains a varity of tables and lookup dictionaries
    /// used when mapping charcodes to glyphs
    /// </summary>
    /// <remarks>
    /// Remeber that any array that may be changed by callers 
    /// must be cloned before sending them on their way.
    /// </remarks>
    public static class Enc
    {
        /// <summary>
        /// A cached copy of the glyph list.
        /// </summary>
        //private static Dictionary<int, char> _sid_glyph_list = null;
        private static Dictionary<string, char> _name_glyph_list = null;
        private static Dictionary<char, int> _unicode_to_ansi = null;
        private static Dictionary<char, string> _unicode_glyph_list = null;

        /// <summary>
        /// A cached copy of the sid names
        /// </summary>
        private static string[] _sid_names = null;

        /// <summary>
        /// A cached copy of the ansi names
        /// </summary>
        private static string[] _ansi_names = null;

        /// <summary>
        /// A cached copy of the mac roman names
        /// </summary>
        private static string[] _mac_names = null;

        /// <summary>
        /// A cached copy of the mac expert names
        /// </summary>
        private static string[] _mace_names = null;

        /// <summary>
        /// A cached copy of the standar names
        /// </summary>
        private static string[] _std_names = null;

        /// <summary>
        /// A cached copy of the sybol names
        /// </summary>
        private static string[] _sym_names = null;

        public static SIDNamePair[] ISOAdobe
        {
            get
            {
                return FillSID(229, "Font.ISOAdobe.txt");
            }
        }

        /// <summary>
        /// Not sure what Expert encoding is used for yet.
        /// (From the CFF specs)
        /// </summary>
        public static SIDNamePair[] Expert
        {
            get
            {
                return FillSID(166, "Font.Expert.txt");
                //return FillStrings(256, "Font.Expert.txt");
            }
        }

        public static SIDNamePair[] ExpertSubset
        {
            get
            {
                return FillSID(87, "Font.ExpertSubset.txt");
            }
        }

        /// <summary>
        /// Standar encoding
        /// </summary>
        public static string[] Standar
        {
            get
            {
                if (_std_names != null) return (string[]) _std_names.Clone();

                _std_names = FillStrings(256, "Font.StandarEnc.txt");
                return (string[]) _std_names.Clone();
            }
        }

        /// <summary>
        /// Standar encoding
        /// </summary>
        public static string[] Symbol
        {
            get
            {
                if (_sym_names != null) return _sym_names;

                _sym_names = FillStrings(256, "Font.SymbolEnc.txt");
                return _sym_names;
            }
        }

        /// <summary>
        /// Mac Roman encoding
        /// </summary>
        public static string[] MacRoman
        {
            get
            {
                if (_mac_names != null) return (string[]) _mac_names.Clone();

                _mac_names = FillStrings(256, "Font.MacRoman.txt");
                return (string[]) _mac_names.Clone();
            }
        }

        /// <summary>
        /// Mac Expert encoding
        /// </summary>
        /// <remarks>
        /// When using octal notation <xxx> one can, if the
        /// font supports it, index into the "Expert" character
        /// set. None of the Base14 fonts upports this though.
        /// </remarks>
        public static string[] MacExpert
        {
            get
            {
                if (_mace_names != null) return (string[]) _mace_names.Clone();

                _mace_names = FillStrings(256, "Font.MacExpert.txt");
                return (string[]) _mace_names.Clone();
            }
        }

        /// <summary>
        /// Win ANSI encoding
        /// </summary>
        public static string[] WinANSI
        {
            get
            {
                if (_ansi_names != null) return (string[]) _ansi_names.Clone();

                _ansi_names = FillStrings(256, "Font.WinANSI.txt");
                //For some reason, .not_defined gets treated as bullets in these cases. (Commented out since textfile has been updated with these changes)
                //_ansi_names[127] = _ansi_names[129] = _ansi_names[141] = _ansi_names[143] = _ansi_names[144] = _ansi_names[157] = "bullet";
                return (string[]) _ansi_names.Clone();
            }
        }

        /// <summary>
        /// Adobe SID Names
        /// </summary>
        public static string[] SIDNames
        {
            get
            {
                if (_sid_names != null) return _sid_names;

                _sid_names = FillStrings(391, "Font.SIDNames.txt");
                return _sid_names;
            }
        }



        /// <summary>
        /// Translates from SID code value to unicode
        /// </summary>
        /// <remarks>I only take the AdobeSIDNames for now</remarks>
        /*public static Dictionary<int, char> GlyphList
        {
            get
            {
                if (_sid_glyph_list != null) return _sid_glyph_list;

                StreamReader s = StrRes.GetResource("Font.glyphlist.txt");
                if (_sid_names == null) _sid_names = SIDNames;
                var d = new Dictionary<int, char>(378); //4281 for full list, 378 for std

                char[] split = new char[] { ' ' };
                while (!s.EndOfStream)
                {
                    string line = s.ReadLine();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int i = line.IndexOf(';');
                    string[] codes = line.Substring(i + 1).Split(split);
                    char[] icodes = new char[codes.Length];
                    for (int c = 0; c < codes.Length; c++)
                        icodes[c] = (char)int.Parse(codes[c], System.Globalization.NumberStyles.HexNumber);
                    var name = line.Substring(0, i);

                    int std_index = Array.IndexOf(_sid_names, name);
                    if (std_index != -1) //Skips over vales that are not reflected in AdobeSIDNames
                        d.Add(std_index, icodes[0]); //Note: Only the first index is kept.
                }
                _sid_glyph_list = d;
                return d;
            }
        }//*/

        /// <summary>
        /// A glyph list as defined by adobe to translate from PDF names
        /// to unicode values
        /// </summary>
        /// <remarks>I only take the AdobeSIDNames for now</remarks>
        public static Dictionary<string, char> GlyphNames
        {
            get
            {
                if (_name_glyph_list != null) return _name_glyph_list;

                using (StreamReader s = StrRes.GetResource("Font.glyphlist.txt"))
                {
#if LIMIT_TO_SID
                    if (_sid_names == null) _sid_names = SIDNames;
                    var d = new Dictionary<string, char>(378); 
#else
                    var d = new Dictionary<string, char>(4821);
#endif
                    char[] split = new char[] { ' ' };
                    while (!s.EndOfStream)
                    {
                        string line = s.ReadLine();
                        if (line.Length == 0 || line[0] == '#') continue;
                        int i = line.IndexOf(';');
                        string[] codes = line.Substring(i + 1).Split(split);
                        char[] icodes = new char[codes.Length];
                        for (int c = 0; c < codes.Length; c++)
                            icodes[c] = (char)int.Parse(codes[c], System.Globalization.NumberStyles.HexNumber);
                        var name = line.Substring(0, i);
#if LIMIT_TO_SID
                        int std_index = Array.IndexOf(_sid_names, name);
                        if (std_index != -1) //Skips over vales that are not reflected in AdobeSIDNames
#endif
                        d.Add(name, icodes[0]); //Note: Only the first index is kept.
                    }
                    _name_glyph_list = d;
                    return d;
                }
            }
        }

        /// <summary>
        /// A glyph list as defined by adobe to translate from unicode values
        /// to PDF names values
        /// </summary>
        /// <remarks>I only take the AdobeSIDNames for now</remarks>
        //#define LIMIT_CHTOUC_TO_SID
        //private static Dictionary<char, string> _unicode_glyph_list = null;
        public static Dictionary<char, string> UnicodeToNames
        {
            get
            {
                if (_unicode_glyph_list != null) return _unicode_glyph_list;

                using (StreamReader s = StrRes.GetResource("Font.glyphlist.txt"))
                {
#if LIMIT_CHTOUC_TO_SID
                    if (_sid_names == null) _sid_names = SIDNames;
                    var d = new Dictionary<char, string>(378); 
#else
                    var d = new Dictionary<char, string>(4821);
#endif
                    char[] split = new char[] { ' ' };
                    while (!s.EndOfStream)
                    {
                        string line = s.ReadLine();
                        if (line.Length == 0 || line[0] == '#') continue;
                        int i = line.IndexOf(';');
                        string[] codes = line.Substring(i + 1).Split(split);
                        char[] icodes = new char[codes.Length];
                        for (int c = 0; c < codes.Length; c++)
                            icodes[c] = (char)int.Parse(codes[c], System.Globalization.NumberStyles.HexNumber);
                        var name = line.Substring(0, i);
#if LIMIT_CHTOUC_TO_SID
                        int std_index = Array.IndexOf(_sid_names, name);
                        if (std_index != -1) //Skips over vales that are not reflected in AdobeSIDNames
#endif
                        for (int c = 0; c < icodes.Length; c++)
                        {
                            char ch = icodes[c];
                            //It appears some characters are defined twice.
                            if (!d.ContainsKey(ch))
                                d.Add(ch, name);
                        }
                    }
                    _unicode_glyph_list = d;
                    return d;
                }
            }
        }

        /// <summary>
        /// Sets up a dictionary for translating unicode
        /// characters to ansi
        /// </summary>
        public static Dictionary<char, int> UnicodeToANSI
        {
            get
            {
                if (_unicode_to_ansi != null) return _unicode_to_ansi;
                var d = GetUnicodeToNames(WinANSI);
                _unicode_to_ansi = d;
                return d;
            }
        }

        /// <summary>
        /// Sets up a dictionary for translating unicode
        /// characters to MacRoman
        /// </summary>
        public static Dictionary<char, int> UnicodeToMacRoman
        {
            get
            {
                var d = GetUnicodeToNames(MacRoman);
                return d;
            }
        }

        private static Dictionary<char, int> GetUnicodeToNames(string[] names)
        {
            var d = new Dictionary<char, int>(names.Length);
            var unicode = GlyphNames;
            for (int c = 32; c < names.Length; c++)
            {
                var name = names[c];
                if (name != ".notdef")
                {
                    var ch = unicode[name];
                    if (!d.ContainsKey(ch))
                        d.Add(ch, c);
                }
            }
            return d;
        }

        public static char GetUnicodeFromAnsi(byte ansi_character)
        {
            if (_ansi_names == null)
                _ansi_names = FillStrings(256, "Font.WinANSI.txt");
            var name = _ansi_names[ansi_character];
            return Enc.GlyphNames[name];
        }

        public static char GetUnicodeFromRoman(byte roman_character)
        {
            var roman_names = FillStrings(256, "Font.MacRoman.txt");
            var name = roman_names[roman_character];
            return Enc.GlyphNames[name];
        }

        private static string[] FillStrings(int num, string file)
        {
            var str_names = new string[num];
            using (StreamReader s = StrRes.GetResource(file))
            {
                s.ReadLine(); s.ReadLine();
                for (int c = 0; c < str_names.Length; c++)
                    str_names[c] = s.ReadLine();
                return str_names;
            }
        }

        private static SIDNamePair[] FillSID(int num, string file)
        {
            var snp = new SIDNamePair[num];
            using (StreamReader s = StrRes.GetResource(file))
            {
                s.ReadLine(); s.ReadLine();
                for (int c = 0; c < snp.Length; c++)
                    snp[c] = new SIDNamePair(int.Parse(s.ReadLine()), s.ReadLine());
                return snp;
            }
        }
    }

    public struct SIDNamePair
    {
        public readonly int SID;
        public readonly string Name;
        public SIDNamePair(int sid, string name)
        { SID = sid; Name = name; }
        public override string ToString()
        {
            return string.Format("{0} {1}",SID, Name);
        }
    }
}