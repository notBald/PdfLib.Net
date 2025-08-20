//To be fully post script compliant one need to include a comment header
//There does not seem to be any issues with excluding this header for Adobe X
//but there could be issues with printing (and other/older readers may balk).
#define INCLUDE_HEADER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Helper class for creating a post script cmap
    /// </summary>
    /// <remarks>
    /// Based on: Adobe CMap and CIDFont Files Specification june 1993
    /// </remarks>
    public class PSCMap
    {
        #region Variables and properties

        readonly bool _unicode;
        string _name;

        /// <summary>
        /// Code ranges
        /// </summary>
        List<CRRange> _cr_ranges = new List<CRRange>(1);

        /// <summary>
        /// Character ranges
        /// </summary>
        List<CHRange> _ch_ranges = new List<CHRange>(256);

        /// <summary>
        /// The name of the cmap
        /// </summary>
        public string Name { get { return _name; } }

        #endregion

        #region Init

        public PSCMap(bool unicode)
        {
            _unicode = unicode;
            _name = _unicode ? "Adobe-Identity-UCS" : "Adobe-Identity-H";
        }

        #endregion

        /// <summary>
        /// How many bytes there are in the characters
        /// </summary>
        /// <param name="start">First character of range</param>
        /// <param name="end">Last character of range</param>
        /// <param name="nbytes">Number of bytes in the characters</param>
        public void AddCodespaceRange(int start, int end, int nbytes)
        {
            _cr_ranges.Add(new CRRange(start, end, nbytes));
        }

        /// <summary>
        /// Adds a single character. This character must come after any
        /// that came before
        /// </summary>
        /// <param name="from">From mapping</param>
        /// <param name="chr">The character to mapping value</param>
        /// <remarks>Usefull when building a large cmap from scratch</remarks>
        public void AddNextChar(int from, int chr, int nbytes)
        {
            if (_ch_ranges.Count > 0)
            {
                var last = _ch_ranges[_ch_ranges.Count - 1];
                if (from - 1 == last.End && last.EndDest + 1 == chr)
                {
                    //Merges the range
                    last.End = from;
                    return;
                }
            }
            _ch_ranges.Add(new CHRange(from, chr, nbytes * 2));
        }

        /// <summary>
        /// Adds a single character.
        /// </summary>
        /// <param name="from">From mapping</param>
        /// <param name="chr">The character to mapping value</param>
        public void AddChar(int from, int chr, int nbytes)
        {
            for (int c = 0; c < _ch_ranges.Count; c++)
            {
                var current = _ch_ranges[_ch_ranges.Count - 1];
                    
                //Merges the range
                if (from + 1 == current.Start && current.Dest - 1 == chr)
                {
                        
                    current.Start = from;
                    current.Dest = from;
                    return;
                }
                if (from - 1 == current.End && current.EndDest + 1 == chr)
                {
                    current.End = from;
                    return;
                }

                //Inserts before
                if (from < current.Start)
                {
                    _ch_ranges.Insert(c, new CHRange(from, chr, nbytes * 2));
                    return;
                }
            }

            //Adds after
            _ch_ranges.Add(new CHRange(from, chr, nbytes * 2));
        }

        /// <summary>
        /// Adds a uniform range
        /// </summary>
        public void AddRange(int from, int to, int start_ch, int nbytes)
        {
            _ch_ranges.Add(new CHRange(from, to, start_ch, nbytes * 2));
        }

        public byte[] Compile()
        {
            #region Writes out the header data

            var cmap = new StringBuilder(4096);
#if INCLUDE_HEADER
            //Cmaps are required to start with this line
            cmap.AppendLine("%!PS-Adobe-3.0 Resource-CMap");

            //We tell the post script interpreter what resources it needs to
            //load in before executing this script
            cmap.AppendLine("%%DocumentNeededResources: procset CIDInit");
            cmap.AppendLine("%%IncludeResource: procset CIDInit");

            //The documentation is depresingly vauge when it comes to setting the
            //name of the cmap. Identity indicates that the mapping is 1 to 1
            //(which it isn't) but I'm gambeling that embeded identity cmaps will
            //not be ignored, the alternate is to come up with some clever name on
            //my own (say Unnamed).
            cmap.Append("%%BeginResource: CMap ");
            cmap.AppendLine(_name);

            //Again here I'm gambeling that "Adobe Identity 0" will be a trouble.
            //free name.
            cmap.AppendFormat("%%Title: ({0} Adobe Identity 0)\n", _name);

            //This version is set in stone
            cmap.AppendLine("%%Version: 1");

            //And we're done with the header.
            cmap.AppendLine("%%EndComments");
#endif
            //Configures the post script engine so that it can execute cmap commands
            cmap.AppendLine("/CIDInit /ProcSet findresource begin");

            //Now we start constructing the cmap dictionary. This dictionary is 5 values
            //to large to accomidate implementation limits in old post script engines.
            cmap.AppendLine("12 dict begin");

            //Starts building the cmap
            cmap.AppendLine("begincmap");

            //Then we add the CIDSystemInfo dictionary
            cmap.AppendLine("/CIDSystemInfo 3 dict dup begin");
            cmap.AppendLine("/Registry (Adobe) def");
            cmap.AppendLine("/Ordering (Identity) def");
            cmap.AppendLine("/Supplement 0 def");
            cmap.AppendLine("end def");

            //The name of the cmap
            cmap.AppendFormat("/CMapName /{0} def\n", _name);

            //Then the version
            cmap.AppendLine("/CMapVersion 1 def");

            //And finally the type
            cmap.AppendFormat("/CMapType {0} def\n", _unicode ? 2 : 0);

            //We write horizonally (this is optional though, default value is 0)
            cmap.AppendLine("/WMode 0 def");

            #endregion

            #region Writes out the code space ranges

            //The begincodespacerange determines the amount of bytes to
            //fetch for each character. The 5411 technical note states
            //that to unicode mappings should have just 1 to byte range
            //for all characters, but AFAIK this isn't accurate since 
            //tounicode is not used on CIDs when working on PDF documents
            cmap.AppendFormat("{0} begincodespacerange\n", _cr_ranges.Count);
            foreach (var cr in _cr_ranges)
                cmap.AppendLine(cr.ToString());
            cmap.AppendLine("endcodespacerange");

            #endregion

            #region Writes out char mapping

            //We now write out the characters. We can only write out up to
            //100 characters at a time, so we must split up the writes thereafter
            var ch_ranges = new List<CHRange>(100);
            for (int c = 0, end = _ch_ranges.Count; c < end; c++)
            {
                if (ch_ranges.Count == 100)
                {
                    if (_unicode)
                        WriteBfCharacters(ch_ranges, cmap);
                    else
                        WriteCidCharacters(ch_ranges, cmap);
                }
                var range = _ch_ranges[c];
                if (range.SingleCharacter)
                    ch_ranges.Add(range);
            }
            if (ch_ranges.Count > 0)
            {
                if (_unicode)
                    WriteBfCharacters(ch_ranges, cmap);
                else
                    WriteCidCharacters(ch_ranges, cmap);
            }

            #endregion

            #region Writes out char ranges

            //We now write out the characters. We can only write out up to
            //100 characters at a time, so we must split up the writes thereafter
            for (int c = 0, end = _ch_ranges.Count; c < end; c++)
            {
                if (ch_ranges.Count == 100)
                {
                    if (_unicode)
                        WriteBfRanges(ch_ranges, cmap);
                    else
                        WriteCidRanges(ch_ranges, cmap);
                }
                var range = _ch_ranges[c];
                if (!range.SingleCharacter)
                    ch_ranges.Add(range);
            }
            if (ch_ranges.Count > 0)
            {
                if (_unicode)
                    WriteBfRanges(ch_ranges, cmap);
                else
                    WriteCidRanges(ch_ranges, cmap);
            }

            #endregion

            #region Ends the cmap

            cmap.AppendLine("endcmap");
            cmap.AppendLine("CMapName currentdict /CMap defineresource pop");
            cmap.AppendLine("end");
            cmap.AppendLine("end");
            cmap.AppendLine("%%EndResource");
            cmap.AppendLine("%%EOF");

            #endregion

            //var test = cmap.ToString();
            return Read.Lexer.GetBytes(cmap.ToString());
        }

        static void WriteCidRanges(List<CHRange> ranges, StringBuilder cmap)
        {
            cmap.AppendFormat("{0} begincidrange\n", ranges.Count);
            foreach (var range in ranges)
            {
                cmap.AppendFormat("<{0}> <{1}> {2}\n", 
                    CRRange.hex(range.Start, range.nDigits), 
                    CRRange.hex(range.End, range.nDigits), 
                    range.Dest.ToString());
            }
            cmap.AppendFormat("endcidrange\n");
            ranges.Clear();
        }

        static void WriteBfRanges(List<CHRange> ranges, StringBuilder cmap)
        {
            cmap.AppendFormat("{0} beginbfrange\n", ranges.Count);
            foreach (var range in ranges)
            {
                string end = range.Dest.ToString("X4");
                cmap.AppendFormat("<{0}> <{1}> <{2}>\n", CRRange.hex(range.Start, range.nDigits), CRRange.hex(range.End, range.nDigits), end);
            }
            cmap.AppendFormat("endbfrange\n");
            ranges.Clear();
        }

        static void WriteCidCharacters(List<CHRange> ranges, StringBuilder cmap)
        {
            cmap.AppendFormat("{0} begincidchar\n", ranges.Count);
            foreach (var range in ranges)
                cmap.AppendFormat("<{0}> {1}\n", CRRange.hex(range.Start, range.nDigits), range.Dest.ToString());
            cmap.AppendFormat("endcidchar\n");
            ranges.Clear();
        }

        static void WriteBfCharacters(List<CHRange> ranges, StringBuilder cmap)
        {
            cmap.AppendFormat("{0} beginbfchar\n", ranges.Count);
            foreach (var range in ranges)
            {
                string end = range.Dest.ToString("X4");
                cmap.AppendFormat("<{0}> <{1}>\n", CRRange.hex(range.Start, range.nDigits), end);
            }
            cmap.AppendFormat("endbfchar\n");
            ranges.Clear();
        }

        class CHRange
        {
            #region Variables and properties

            public int Start, End;
            public int Dest, nDigits;
            public bool SingleCharacter { get { return Start == End; } }
            public int EndDest { get { return Dest + End - Start; } }

            #endregion

            #region Init
            public CHRange(int start, int ch, int n_digits)
                : this(start, start, ch, n_digits)
            { }
            public CHRange(int start, int end, int ch, int n_digits)
            {
                Start = start; End = end;
                Dest = ch; nDigits = n_digits;
            }
            #endregion

            #region Other

            public override string ToString()
            {
                var sb = new StringBuilder(60);
                if (SingleCharacter) 
                    sb.AppendFormat("Character: {0} ({1})", (char) Start, End);
                else
                    sb.AppendFormat("Range: {0} - {1} ({2} - {3}", (char)Start, (char)End, Start, End);
                return sb.ToString();
            }

            #endregion
        }

        class CRRange
        {
            public readonly int Start, End, Digits;
            public CRRange(int start, int end, int bytes)
            { Start = start; End = end; Digits = bytes * 2; }
            public override string ToString()
            {
                return string.Format("<{0}> <{1}>", hex(Start, Digits), hex(End, Digits));
            }
            public static string hex(int val, int n)
            {
                string hex = val.ToString("X");
                while (hex.Length < n)
                    hex = "0" + hex;
                Debug.Assert(hex.Length == n);
                return hex;
            }
        }
    }
}
