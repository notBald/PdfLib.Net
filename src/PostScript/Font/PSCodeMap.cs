using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Font
{
    /// <summary>
    /// Handles PostScript CMap mapping. (From charcode to CID)
    /// </summary>
    /// <remarks>
    /// This isn't a propper PS object, but will sit inside PS dictionaries.
    /// 
    /// Implementation details:
    ///  - Poorly optimized
    ///  - A character map contains "ranges" and "mapping" information
    ///    a range tells how many bytes a character is made up from,
    ///    while a mapping translates from that character to the desired
    ///    CID code.
    ///    
    ///    In this implementation everything is kept in byte arrays. This
    ///    is inefficent and has no benifit. Charcodes are at most 4 bytes,
    ///    though I belive PDF is limited down to 2 bytes.
    ///    
    ///    So it would be better to store the byte arrays into integer.
    ///    i.e. {0xff, 0xfd} => 0xfffd. That will make compares quicker.
    ///    
    ///    Also note that "to unicode" cmap does things a bit differently
    ///    
    ///    
    /// </remarks>
    public sealed class PSCodeMap : PSObject
    {
        #region Variables and properties

        int _max_n_bytes;

        /// <summary>
        /// PostScript is not allowed to touch this object
        /// </summary>
        public override PSAccess Access { get { return PSAccess.None; } }

        /// <summary>
        /// Whenever this map translates to CID or Unicode
        /// </summary>
        public bool IsUnicode { get { return _cid_ranges.Length == 0; } }

        /// <summary>
        /// Character ranges
        /// </summary>
        CodeRange[] _ranges;

        /// <summary>
        /// CID ranges, must be a subset of the code ranges
        /// </summary>
        CidRange[] _cid_ranges = new CidRange[0], _ndef_ranges = new CidRange[0];

        /// <summary>
        /// Char ranges
        /// </summary>
        CharRange[] _char_ranges;

        ///// <summary>
        ///// CID characters
        ///// </summary>
        ///// <remarks>The order is important, so I've moved them into the ranges</remarks>
        //CidChar[] _cid_chars, _ndef_chars;

        /// <summary>
        /// The number of bytes to pull from the input string
        /// is determined by looking what range the bytes fit
        /// into.
        /// </summary>
        public CodeRange[] Ranges { get { return _ranges; } }

        /// <summary>
        /// The maximum number of bytes required by this cmap
        /// </summary>
        public int MaxNumberOfBytes { get { return _max_n_bytes; } }

        #endregion

        #region Init

        /// <summary>
        /// Must be created using the PostScript interpreter
        /// </summary>
        private PSCodeMap() { }

        /// <summary>
        /// This function sorts the ranges
        /// </summary>
        internal void Init()
        {
            //Sort so that low byte ranges comes first
            Array.Sort<CodeRange>(_ranges);

            //Not sure how to do the sort properly, but it does look like large
            //ranges trumphs short ranges. 
            //Array.Reverse(_ranges);
        }

        #endregion

        /// <summary>
        /// Does the reverse of Map
        /// </summary>
        /// <param name="ch">CID character</param>
        /// <returns>CharCode character</returns>
        internal byte[] CIDtoCharCode(byte[] ch)
        {
            //Unicode PSCode maps do not posses any CID ranges.
            if (IsUnicode)
            {
                var ba = new byte[ch.Length];
                Buffer.BlockCopy(ch, 0, ba, 0, ch.Length);
                Array.Reverse(ba);
                return ba;
            }

            int chi = 0;
            for (int c = ch.Length - 1; c >= 0; c--)
                chi = (chi << 8) + ch[c];

            //Potential bug: 1 byte ranges should be checked first

            foreach (var range in _cid_ranges)
            {
                //Translates the first character in the range, then checks if
                //the current character is bigger or equal
                int first = range.Num + range.GetDistance(range.Start);
                if (first <= chi)
                {
                    int last = range.Num + range.GetDistance(range.End);
                    if (chi <= last)
                    {
                        //Translatd to charcode
                        int cc = chi + range.Start - range.Num;
                        var ba = new byte[range.nBytes];
                        for (int c = ba.Length - 1; c >= 0; c--)
                        {
                            ba[c] = (byte)cc;
                            cc >>= 8;
                        }

                        return ba;
                    }
                }
            }

            foreach(var c in ch)
                if (c != 0)
                    return CIDtoCharCode(new byte[] { 0 });
            throw new NotImplementedException("What to do when the map does not support the ndef character");
        }

        /// <summary>
        /// Maps a character to a CID
        /// </summary>
        /// <param name="ch">integer with up to four character bytes</param>
        /// <param name="length">The length of the ch "byte array"</param>
        /// <returns>CID</returns>
        public int Map(int ch, int length)
        {
            //First try to find a range suited for the character. Searching
            //from the top since later ranges take priorety over earlier
            //ranges. Alternativly one could build a lookup table.

            for (int c = _cid_ranges.Length - 1; c >= 0; c--)
            {
                var range = _cid_ranges[c];

                if (range.nBytes == length && range.Contains(ch))
                {
                    //Translates to CID
                    return range.Num + range.GetDistance(ch);
                }
            }

            for (int c = _ndef_ranges.Length - 1; c >= 0; c--)
            {
                var range = _ndef_ranges[c];

                if (range.nBytes == length && range.Contains(ch))
                    return range.Num;
            }

            //Specs say that the default .ndef CID equals 0
            return 0;
        }

        /// <summary>
        /// Does the reverse of "UnicodeMap"
        /// </summary>
        /// <param name="ch">Character as raw bytes</param>
        /// <returns>Raw bytes of the CID</returns>
        internal byte[] UnicodeToCID(byte[] ch)
        {
            //Converts to ushort array
            var shorts = new ushort[(ch.Length + 1) / 2];
            for (int c = 0, u = 0; c < ch.Length; c += 2, u++)
                shorts[u] = ch[c];
            for (int c = 1, u = 0; c < ch.Length; c += 2, u++)
                shorts[u] |= (ushort) (ch[c] << 8);

            foreach (var range in _char_ranges)
            {
                //Translates the first character in the range, then checks if
                //the current character is bigger or equal
                ushort[] first = range.GetCharacterSequence(range.Start);
                bool bigger = true;
                for(int c=0; c < first.Length && c < shorts.Length; c++)
                {
                    if (shorts[c] < first[c])
                    {
                        bigger = false;
                        break;
                    }
                }

                if (bigger)
                {
                    //Translates the last character in the range, then checks
                    //if the current character is smaller of equal
                    ushort[] last = range.GetCharacterSequence(range.End);
                    bool smaller = true;
                    for(int c=last.Length - 1; c >= 0; c--)
                    {
                        ushort sh = last[c];
                        if (sh == 0)
                        {
                            //The input character must be 0 or shorter
                            if (c < shorts.Length && shorts[c] != 0)
                            {
                                smaller = false;
                                break;
                            }
                        }

                        //The input character must be smaller or shorter
                        if (c >= shorts.Length || shorts[c] < sh)
                            break;

                        //We know the shorts array is long enough.
                        if (shorts[c] > sh)
                        {
                            smaller = false;
                            break;
                        }
                    }

                    if (smaller)
                    {
                        //We found the correct range. Now we calculate the distance from the start of the range to the character in question.
                        //To prevent issues with underflow, we convert the number into an int
                        Array.Resize<ushort>(ref shorts, 2);
                        Array.Resize<ushort>(ref first, 2);
                        int unicode_value = (shorts[1] << 16) | shorts[0];
                        int first_unicode_value = (first[1] << 16) | first[0];
                        int distance_from_start = unicode_value - first_unicode_value;
                        Debug.Assert(distance_from_start >= 0);

                        //We add the distance to the start range
                        int CID = range.Start + distance_from_start;

                        //Then we convert the int to a byte array
                        var ba = new byte[] { (byte) CID, (byte) (CID >> 8), (byte) (CID >> 16), (byte) (CID >> 24) };
                        Array.Resize(ref ba, range.nBytes);
                        return ba;

                        //Old code:
                        ////We found the correct range, so we need to add the character's value to
                        ////the first character
                        //Array.Resize<ushort>(ref first, shorts.Length);
                        //for (int c = 0; c < shorts.Length; c++)
                        //    first[c] += shorts[c];

                        ////The we convert it to a byte array.
                        //byte[] ba = new byte[first.Length * 2];
                        //for(int c=0, bp = 0; c < first.Length; c++)
                        //{
                        //    var sh = first[c];
                        //    ba[bp++] = (byte)sh;
                        //    ba[bp++] = (byte)(sh >> 8);
                        //}
                        //Array.Resize(ref ba, range.nBytes);
                        //return ba;
                    }
                }
            }

            if (ch.Length == 1 && ch[0] == 0)
                throw new NotImplementedException("What to do when the map does not support the ndef character");
            return UnicodeToCID(new byte[] { 0 });
        }

        /// <summary>
        /// Maps a character to a unicode value.
        /// </summary>
        /// <param name="ch">integer with up to four character bytes</param>
        /// <param name="length">The length of the ch "byte array"</param>
        /// <returns>Unicode (can be made out of multiple "ushorts")</returns>
        public ushort[] UnicodeMap(int ch, int length)
        {
            for (int c = _char_ranges.Length - 1; c >= 0; c--)
            {
                var range = _char_ranges[c];

                if (range.nBytes >= length && range.Contains(ch))
                {
                    //Translates to unicoce value
                    if (range.Name != null)
                        throw new NotImplementedException("Unicode names not supported");
                    return range.GetCharacterSequence(ch);
                }
            }

            return new ushort[] { 0 };
        }

        #region Requires overrides

        public override PSItem ShallowClone()
        {
            throw new NotImplementedException();
        }

        internal override void Restore(PSItem obj)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Add functions

        public void Add(CodeRange[] ranges)
        {
            if (_ranges == null)
                _ranges = ranges;
            else
            {
                Array.Resize<CodeRange>(ref _ranges, _ranges.Length + ranges.Length);
                Array.Copy(ranges, 0, _ranges, _ranges.Length - ranges.Length, ranges.Length);
            }

            for (int c=0; c < ranges.Length; c++)
            {
                int n = ranges[c].NBytes;
                if (_max_n_bytes < n)
                    _max_n_bytes = n;
            }
        }

        public void Add(CidRange[] ranges)
        {
            if (_cid_ranges.Length == 0)
                _cid_ranges = ranges;
            else
            {
                Array.Resize<CidRange>(ref _cid_ranges, _cid_ranges.Length + ranges.Length);
                Array.Copy(ranges, 0, _cid_ranges, _cid_ranges.Length - ranges.Length, ranges.Length);
            }
        }

        public void Add(CharRange[] ranges)
        {
            if (_char_ranges == null)
                _char_ranges = ranges;
            else
            {
                Array.Resize<CharRange>(ref _char_ranges, _char_ranges.Length + ranges.Length);
                Array.Copy(ranges, 0, _char_ranges, _char_ranges.Length - ranges.Length, ranges.Length);
            }
        }

        public void Add(CidChar[] chars)
        {
            var cr = new CidRange[chars.Length];
            for (int c=0; c < chars.Length; c++)
            {
                var ch = chars[c];
                cr[c] = new CidRange(ch.CharBytes, ch.CharBytes, ch.Cid);
            }
            Add(cr);

            //if (_cid_ranges == null)
            //    _cid_chars = chars;
            //else
            //{
            //    Array.Resize<CidChar>(ref _cid_chars, _cid_chars.Length + chars.Length);
            //    Array.Copy(chars, 0, _cid_chars, _cid_chars.Length - chars.Length, chars.Length);
            //}
        }

        public void Add(CharToCharMap[] chars)
        {
            var cr = new CharRange[chars.Length];
            for (int c = 0; c < chars.Length; c++)
            {
                var ch = chars[c];
                cr[c] = new CharRange(ch.CharBytes, ch.CharBytes, ch.ToCharBytes, ch.Name);
            }
            Add(cr);
        }

        public void AddNDef(CidRange[] ranges)
        {
            if (_ndef_ranges.Length == 0)
                _ndef_ranges = ranges;
            else
            {
                Array.Resize<CidRange>(ref _ndef_ranges, _ranges.Length + ranges.Length);
                Array.Copy(ranges, 0, _ndef_ranges, _ndef_ranges.Length - ranges.Length, ranges.Length);
            }
        }

        public void AddNDef(CidChar[] chars)
        {
            var cr = new CidRange[chars.Length];
            for (int c = 0; c < chars.Length; c++)
            {
                var ch = chars[c];
                cr[c] = new CidRange(ch.CharBytes, ch.CharBytes, ch.Cid);
            }
            AddNDef(cr);

            //if (_cid_ranges == null)
            //    _ndef_chars = chars;
            //else
            //{
            //    Array.Resize<CidChar>(ref _ndef_chars, _ndef_chars.Length + chars.Length);
            //    Array.Copy(chars, 0, _ndef_chars, _ndef_chars.Length - chars.Length, chars.Length);
            //}
        }

        internal static void Add(PSDictionary dict, CidRange[] ranges)
        {
            if (!dict.Catalog.ContainsKey("CodeMap"))
                dict.Catalog.Add("CodeMap", new PSCodeMap());
            var psc = (PSCodeMap) dict.Catalog["CodeMap"];
            psc.Add(ranges);
        }

        internal static void Add(PSDictionary dict, CharRange[] ranges)
        {
            if (!dict.Catalog.ContainsKey("CodeMap"))
                dict.Catalog.Add("CodeMap", new PSCodeMap());
            var psc = (PSCodeMap)dict.Catalog["CodeMap"];
            psc.Add(ranges);
        }

        internal static void Add(PSDictionary dict, CidChar[] chars)
        {
            if (!dict.Catalog.ContainsKey("CodeMap"))
                dict.Catalog.Add("CodeMap", new PSCodeMap());
            var psc = (PSCodeMap)dict.Catalog["CodeMap"];
            psc.Add(chars);
        }

        internal static void Add(PSDictionary dict, CharToCharMap[] chars)
        {
            if (!dict.Catalog.ContainsKey("CodeMap"))
                dict.Catalog.Add("CodeMap", new PSCodeMap());
            var psc = (PSCodeMap)dict.Catalog["CodeMap"];
            psc.Add(chars);
        }

        internal static void AddNDef(PSDictionary dict, CidRange[] ranges)
        {
            if (!dict.Catalog.ContainsKey("CodeMap"))
                dict.Catalog.Add("CodeMap", new PSCodeMap());
            var psc = (PSCodeMap)dict.Catalog["CodeMap"];
            psc.AddNDef(ranges);
        }

        internal static void AddNDef(PSDictionary dict, CidChar[] chars)
        {
            if (!dict.Catalog.ContainsKey("CodeMap"))
                dict.Catalog.Add("CodeMap", new PSCodeMap());
            var psc = (PSCodeMap)dict.Catalog["CodeMap"];
            psc.AddNDef(chars);
        }

        internal static void Add(PSDictionary dict, CodeRange[] ranges)
        {
            if (!dict.Catalog.ContainsKey("CodeMap"))
                dict.Catalog.Add("CodeMap", new PSCodeMap());
            var psc = (PSCodeMap)dict.Catalog["CodeMap"];
            psc.Add(ranges);
        }

        internal static void Add(PSDictionary dict, PSCMap map)
        {
            if (dict.Catalog.ContainsKey("CodeMap"))
                throw new NotImplementedException("Can not merge CodeMaps");
            dict.Catalog.Add("CodeMap", map.CodeMap);
        }

        #endregion

        #region Helper functions

        /// <summary>
        /// Simply to let me use the existing ToType system on PSDictionaries.
        /// </summary>
        /// <remarks>This method is never actually called</remarks>
        internal static PSCodeMap Create(PSCodeMap o) { return o; }

        /// <summary>
        /// Converts a BE byte array to a number
        /// </summary>
        /// <param name="ba"></param>
        /// <returns></returns>
        internal static int BaToInt(byte[] ba)
        {
            Debug.Assert(ba.Length <= 4);
            int ret = 0;
            for (int c = 0; c < ba.Length; c++)
                ret = ret << 8 | ba[c];
            return ret;
        }

        #endregion
    }

    [DebuggerDisplay("{Start} - {End} - NBytes: {NBytes}")] 
    public class CodeRange : IComparable<CodeRange>
    {
        public readonly int Start, End, NBytes;

        public CodeRange(byte[] start, byte[] end)
        {
            Debug.Assert(start.Length == end.Length);

            Start = PSCodeMap.BaToInt(start); End = PSCodeMap.BaToInt(end); NBytes = start.Length;
        }
        public bool Contains(int raw)
        {
            return Start <= raw && raw <= End;
        }

        /// <summary>
        /// Sort ranges
        /// </summary>
        /// <remarks>
        /// //Perhaps the sort should be:
        /// If a range overlaps another range, push it back.
        /// If ranges don't overlapp, it does not matter
        /// If ranges partially overlap, I'm not sure.
        ///
        /// Before the sort, extend one byte ranges with 00 on the start and
        /// FF on the end. 
        /// </remarks>
        public int CompareTo(CodeRange other)
        {
            if (NBytes == other.NBytes)
            {
                if (Start == other.Start)
                {
                    if (End == other.End)
                        return 0;
                    return End - other.End;
                }
                return other.Start - Start;
            }
            else
            {
                if (other.NBytes > NBytes) 
                    return other.CompareTo(this) * -1;

                var start = other.Start << 8;
                var end = (other.End << 8) | 0xFF;

                //If start is the same, we look at end
                if (Start == start)
                {
                    //If end is the same, we put the shortest range first
                    if (End == end)
                        return 1;

                    //If this range overlaps the other one, End will be greater.
                    return End - end;
                }

                //If this range overlaps the other one, Start will be lower.
                return start - Start;
                //Note, we do not look at the End as I don't know what to do for
                //partial overlaps.
            }
        }
    }

    public class CidRange
    {
        public readonly int Start, End, nBytes;
        public readonly int Num;
        public CidRange(byte[] start, byte[] end, int num)
        { Start = PSCodeMap.BaToInt(start); End = PSCodeMap.BaToInt(end); Num = num; nBytes = start.Length; }
        public bool Contains(int raw)
        {
            return Start <= raw && raw <= End;
        }
        /// <summary>
        /// Distance from start
        /// </summary>
        public int GetDistance(int raw)
        {
            return raw - Start;
        }
    }

    /// <summary>
    /// Used for ToUnicode mapping.
    /// 
    /// There are four ways to range map from char codes to unicode
    /// 
    /// [range from] [range to] [a number]
    ///  - In this case the number is incremented by the value "to - from"
    ///  
    /// [range from] [size] [a number  a number  a number]
    ///  - In this case the range has to be the same length as the numbers.
    ///  - Hmm, where did I find this in the specs? Have I been confused by 
    ///    surrugates? (No worries though, surrugates are handled elswhere 
    ///    and it does not hurt to support this)
    ///    
    /// [range from] [range to] [a string]
    ///  - In this case the last byte of the string is incremented the same way
    ///    numbers are incremented. This incrementation must never overflow.
    /// 
    /// [range from] [size] [a string  a string  a string]
    ///  - Same as with numbers, just with strings now
    /// </summary>
    [DebuggerDisplay("FROM: {Start}")]
    public class CharRange
    {
        public readonly int Start, End, nBytes;

        //A unicode character can be made up by multiple bytes
        private readonly ushort[] Num;

        //If there's a string, increment the last byte by (raw - start) and return.
        public readonly string Name; //Can be null

        public CharRange(byte[] start, byte[] end, byte[] num, string name)
            : this(PSCodeMap.BaToInt(start), start.Length, PSCodeMap.BaToInt(end), num, name)
        { }
        public CharRange(int start, int n_bytes, int end, byte[] num, string name)
        {
            Start = start; End = end; Name = name;
            nBytes = n_bytes;

            //Unicode is in UTF-16BE format
            Num = new ushort[(num.Length + 1) / 2];
            for (int c = 0, p = 0; c < num.Length; c++)
            {
                ushort n = num[c++];
                if (c < num.Length)
                    n = (ushort)(n << 8 | num[c]);
                Num[p++] = n;
            }
        }

        public bool Contains(int raw)
        {
            return Start <= raw && raw <= End;
        }
        /// <summary>
        /// Distance from start, does not work with "string"
        /// </summary>
        public ushort[] GetCharacterSequence(int raw)
        {
            var ret = (ushort[]) Num.Clone();
            ret[ret.Length - 1] += (ushort) (raw - Start);
            return ret;
        }
    }

    /// <summary>
    /// Establishes a mapping from char code to CID
    /// </summary>
    public class CidChar
    {
        public readonly byte[] CharBytes;
        public readonly int Cid;
        public CidChar(byte[] cb, int cid)
        { CharBytes = cb; Cid = cid; }
    }

    /// <summary>
    /// Establishes a mapping to either another charcode
    /// or a character name
    /// </summary>
    [DebuggerDisplay("{SR.HexDump(CharBytes)} to {TO}")]
    public class CharToCharMap
    {
        /// <summary>
        /// The character's name. Can be null.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// From charbytes mapping
        /// </summary>
        public readonly byte[] CharBytes;

        /// <summary>
        /// To charbytes mapping. Can be null
        /// </summary>
        public readonly byte[] ToCharBytes;

        /// <summary>
        /// Intended for debuging aid
        /// </summary>
        private string TO { get { return Name == null ? SR.HexDump(ToCharBytes) : Name; } }

        public CharToCharMap(byte[] cb, string name)
        { CharBytes = cb; Name = name; ToCharBytes = null; }

        public CharToCharMap(byte[] cb, byte[] tocb)
        { CharBytes = cb; Name = null; ToCharBytes = tocb; }
    }
}
