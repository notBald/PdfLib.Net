using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Render.Font.TT
{
    /// <summary>
    /// Character To Glyph Index Mapping Table
    /// 
    /// This table defines the mapping of character codes to the glyph 
    /// index values used in the font. It may contain more than one 
    /// subtable, in order to support more than one character encoding 
    /// scheme. Character codes that do not correspond to any glyph in 
    /// the font should be mapped to glyph index 0. The glyph at this 
    /// location must be a special glyph representing a missing 
    /// character.
    /// </summary>
    internal class CmapTable : Table
    {
        #region Variables and properties

        /// <summary>
        /// Should be zero
        /// </summary>
        public readonly ushort Version;

        /// <summary>
        /// If this table's checksum is correct
        /// </summary>
        public override bool Valid { get { return _td.IsValid(Tag.cmap); } }

        /// <summary>
        /// Cmap descriptors
        /// </summary>
        readonly CmapDescriptor[] _cmaps;

        #endregion

        #region Init

        public CmapTable(TableDirectory td, StreamReaderEx r)
            : base(td)
        {
            uint pos = (uint) r.Position;
            Version = r.ReadUShort();
            int n_enc_tables = r.ReadUShort();

            //Reads in all encoding tables
            _cmaps = new CmapDescriptor[n_enc_tables];
            for (int c = 0; c < n_enc_tables; c++)
                _cmaps[c] = new CmapDescriptor(r, pos);

#if DEBUG
            TestCmaps();
#endif
        }

        #endregion

        /// <summary>
        /// Fetches a cmap with the desired encoding, if such exists
        /// </summary>
        /// <param name="pid">Platform ID</param>
        /// <param name="pse">Platform Specific Encoding</param>
        /// <remarks>Null if the cmap isn't found</remarks>
        public CmapFormat GetCmap(PlatformID pid, ushort pse)
        {
            CmapFormat ret = null;

            for (int c = 0; c < _cmaps.Length && ret == null; c++)
            {
                var cmap = _cmaps[c];
                if (cmap.PlatformID == pid && cmap.PlatSpecEnc == pse)
                {
                    //Gets the stream reader
                    var s = _td.Reader;
                    var pos = s.Position;
                    s.Position = cmap.Offset;

                    //Reads the type of Cmap format
                    ushort format = s.ReadUShort();

                    //Creates the specified table
                    switch (format)
                    {
                        case 0:
                            ret = new CmapFormat0(s);
                            break;

                        case 2:
                            var test2 = new CmapFormat2(s);
                            throw new NotImplementedException("CmapFormat2");

                        case 4:
                            ret = new CmapFormat4(s);
                            break;

                        case 6:
                            ret = new CmapFormat6(s);
                            break;

                        case 12:
                            ret = new CmapFormat12(s);
                            break;

                        case 13:
                            ret = new CmapFormat13(s);
                            break;

                        default:
                            throw new UnknownFormatException("Cmap");
                    }

                    s.Position = pos;
                }
            }
            return ret;
        }

        #region DEBUG

        internal void TestCmaps()
        {
            foreach (var cmap in _cmaps)
            {
                GetCmap(cmap.PlatformID, cmap.PlatSpecEnc);
            }
        }

        #endregion

        public abstract class CmapFormat
        {
            public abstract int GetGlyphIndex(int i);

            /// <summary>
            /// Code ranges in the cmap
            /// </summary>
            public abstract Range[] Ranges { get; }

            public struct Range
            {
                public readonly uint Start;
                public readonly uint End;
                public byte Prefix { get { return (byte)((Start >> 8) & 0xFF); } }
                public Range(uint start, uint end) { Start = start; End = end; }
                public bool Contains(uint ch) { return Start <= ch && ch <= End; }
                public override string ToString()
                {
                    return string.Format("{0} - {1}", Start, End);
                }
            }
        }

        /// <summary>
        /// This is a simple 1 to 1 mapping of character codes to glyph indices. The glyph set 
        /// is limited to 256. Note that if this format is used to index into a larger glyph set,
        /// only the first 256 glyphs will be accessible
        /// </summary>
        class CmapFormat0 : CmapFormat
        {
            readonly byte[] _glyphIDs = new byte[256];

            public CmapFormat0(StreamReaderEx s)
            {
                ushort length = s.ReadUShort();
                ushort version = s.ReadUShort(); //<-- Changed into language in OpenType
                //Issue127 has a font with "language = 1". Simply ignoring the "version"
                //lets that pdf render fine. I'm unsure how language should be used, though.
                //Is it possible to select a cmap based on laguage? 
                if (/*version != 0 ||*/ length != 262)
                    throw new UnknownVersionException("CmapFormat0");
                s.Read(_glyphIDs, 0, 256);
            }

            public override int GetGlyphIndex(int i)
            {
                if (i < 0 || i > 255) return 0;
                return _glyphIDs[i];
            }

            public override Range[] Ranges { get { return new Range[] { new Range(0, 255) }; } }
        }

        /// <summary>
        /// This subtable is useful for the national character code standards used for Japanese, 
        /// Chinese, and Korean characters. These code standards use a mixed 8/16-bit encoding, 
        /// in which certain byte values signal the first byte of a 2-byte character (but these 
        /// values are also legal as the second byte of a 2-byte character).  Character codes 
        /// are always 1-byte. The glyph set is limited to 256
        /// </summary>
        class CmapFormat2
        {
            /// <summary>
            /// Array that maps high bytes to subHeaders: value is subHeader index * 8
            /// </summary>
            readonly ushort[] subHeaderKeys = new ushort[256];

            readonly SubHeader[] SubHeaders;

            public CmapFormat2(StreamReaderEx s)
            {
                ushort length = s.ReadUShort();
                ushort version = s.ReadUShort();
                if (version != 0)
                    throw new UnknownVersionException("CmapFormat2");

                //Reads the subheader keys and computes the length
                //of the subtables
                int n_subs = 0;
                for (int c = 0; c < subHeaderKeys.Length; c++)
                {
                    var val = s.ReadUShort();
                    subHeaderKeys[c] = val;

                    //Value is expected to be a multiple of 8
                    if ((val & 7) != 0)
                        throw new InvalidDataException("CmapFormat2");

                    //The value subHeaderKeys[i], divided by 8, is the index k into the subHeader's 
                    //array. By dividing by 8 we find the higest index into the subheader arrays
                    val /= 8;
                    if (val > n_subs) n_subs = val;
                }

                SubHeaders = new SubHeader[n_subs];
                for (int c = 0; c < n_subs; c++)
                {
                    var sh = new SubHeader(s);

                    //Todo:
                    //Additional parsing based on the
                    //EntryCount, IdRangeOffset, etc
                    //Want a testfile before I look into this

                    
                }
                throw new NotImplementedException("CmapFormat2");
            }

            struct SubHeader
            {
                /// <summary>
                /// First valid low byte for this subHeader
                /// </summary>
                readonly ushort FirstCode;
                /// <summary>
                /// Number of valid low bytes for this subHeader
                /// </summary>
                readonly ushort EntryCount;

                readonly short IdDelta;
                readonly ushort IdRangeOffset;

                public SubHeader(StreamReaderEx s)
                {
                    FirstCode = s.ReadUShort();
                    EntryCount = s.ReadUShort();
                    IdDelta = s.ReadShort();
                    IdRangeOffset = s.ReadUShort();
                }
            }
        }

        /// <summary>
        /// This is the Microsoft standard character to glyph index mapping table
        /// </summary>
        class CmapFormat4 : CmapFormat
        {
            readonly ushort[] _endCount;
            readonly ushort[] _startCount;
            readonly short[] _idDelta;
            readonly ushort[] _idRangeOffset;
            readonly ushort[] _glyphIdArray;

            public CmapFormat4(StreamReaderEx s)
            {
                ushort length = s.ReadUShort();
                ushort version = s.ReadUShort();
                if (version != 0) throw new UnknownVersionException("CmapFormat4");

                //Segment count
                ushort segCountx2 = s.ReadUShort();

                //Not used/needed.
                ushort searchRange= s.ReadUShort();
                ushort entrySelector = s.ReadUShort();
                ushort rangeShift = s.ReadUShort();

                int seg_count = segCountx2 / 2;

                _endCount = new ushort[seg_count];
                for (int c = 0; c < seg_count; c++)
                    _endCount[c] = s.ReadUShort();
                Debug.Assert(_endCount[seg_count - 1] == 0xFFFF);

                //Reserved
                Debug.Assert(s.ReadUShort() == 0);

                _startCount = new ushort[seg_count];
                for (int c = 0; c < seg_count; c++)
                    _startCount[c] = s.ReadUShort();

                _idDelta = new short[seg_count];
                for (int c = 0; c < seg_count; c++)
                    _idDelta[c] = s.ReadShort();

                _idRangeOffset = new ushort[seg_count];
                for (int c = 0; c < seg_count; c++)
                    _idRangeOffset[c] = s.ReadUShort(); ;

                //glyphIdArray size can be calcualted by looking at 
                //idRangeOffset and startcodes, but it's easier to
                //just read the remaining shorts
                int remains = (length - 16 - (seg_count * 8)) / 2;
                _glyphIdArray = new ushort[remains];
                for (int c = 0; c < remains; c++)
                    _glyphIdArray[c] = s.ReadUShort();
            }

            public override int GetGlyphIndex(int ch)
            {
                //"EndCount" always ends with this character,
                //so we special case it
                if (ch == 0xFFFF) return 0;

                //Loops through all the segemnts
                for (int c = 0; c < _endCount.Length; c++)
                {
                    if (_endCount[c] >= ch)
                    {
                        if (_startCount[c] <= ch)
                        {
                            //Found the segment the character bellongs to

                            //If the idRangeOffset value for the segment is 
                            //not 0, the mapping of character codes relies on glyphIdArray. 
                            var ido = _idRangeOffset[c];
                            if (ido != 0) 
                            {
                                //This formula gives the number of ushorts from the current
                                //position in the _idRangeOffset table, and into the glyphIdArray
                                var nshorts = ido / 2 + (ch - _startCount[c]);

                                //Calcs the number of ushorts into the glyphIdArray by substracting
                                //the remaning ushorts in the idRangeOffset table
                                nshorts -= (_endCount.Length - c);

                                return _glyphIdArray[nshorts];
                            }

                            return (ch + _idDelta[c]) % 65536;
                        }
                        else
                        {
                            //The character is between segements, thus 0
                            return 0;
                        }
                    }
                }

                //The character is after the segments, thus 0
                return 0;
            }

            public override Range[] Ranges
            {
                get
                {
                    var ranges = new Range[_startCount.Length - 1];
                    for (int c = 0; c < ranges.Length; c++)
                        ranges[c] = new Range(_startCount[c], _endCount[c]);
                    return ranges;
                }
            }
        }

        /// <summary>
        /// Trimmed table mapping 
        /// </summary>
        class CmapFormat6 : CmapFormat
        {
            //First character in the range
            readonly ushort _first_code;

            //Number of enteries in the range
            readonly ushort _entery_count;

            readonly ushort[] _glyph_ids;

            public CmapFormat6(StreamReaderEx s)
            {
                ushort length = s.ReadUShort();
                ushort version = s.ReadUShort();
                if (version != 0) throw new UnknownVersionException("CmapFormat6");
                _first_code = s.ReadUShort();
                _entery_count = s.ReadUShort();
                _glyph_ids = new ushort[_entery_count];
                for (int c = 0; c < _entery_count; c++)
                    _glyph_ids[c] = s.ReadUShort();
            }

            public override int GetGlyphIndex(int i)
            {
                i -= _first_code;
                //Enteries outside the range maps to glyph id 0
                if (i < 0 || i >= _entery_count) return 0;
                return _glyph_ids[i];
            }

            public override Range[] Ranges { get { return new Range[] { new Range(0, ushort.MaxValue) }; } }
        }

        /// <summary>
        /// Segmented coverage
        /// </summary>
        /// <remarks>
        /// This is the Microsoft standard character to glyph index mapping table for fonts 
        /// supporting the UCS-4 characters in the Unicode Surrogates Area (U+D800 - U+DFFF).
        /// It is a bit like format 4, in that it defines segments for sparse representation in 
        /// 4-byte character space.
        /// </remarks>
        class CmapFormat12 : CmapFormat
        {
            protected readonly Group[] _groups;

            public override Range[] Ranges
            {
                get
                {
                    var ret = new Range[_groups.Length];
                    for (int c = 0; c < ret.Length; c++)
                        ret[c] = _groups[c].Range;
                    return ret;
                }
            }

            public CmapFormat12(StreamReaderEx s)
            {
                ushort reserved = s.ReadUShort();
                if (reserved != 0) throw new UnknownFormatException("CmapFormat12");

                //Length of this table, including the header (16 bytes)
                uint length = s.ReadUInt();

                //Should always be zero (called version in the TT specs)
                uint language = s.ReadUInt();
                //if (language != 0) throw new UnknownVersionException("CmapFormat12");
                //^ Vertical_text_2 has a cmap with this set. Not sure how to deal with
                //this

                //Number of groupings which follows
                uint nGroups = s.ReadUInt();

                if (length - 16 != nGroups * 12) throw new InvalidDataException("CmapFormat12");

                _groups = new Group[nGroups];
                var old_group = new Group(0, 0, 0);
                for (int c = 0; c < nGroups; c++)
                {
                    uint startCharCode = s.ReadUInt();
                    uint endCharCode = s.ReadUInt();
                    uint startGlyphID = s.ReadUInt();

                    if (old_group.Range.End >= startCharCode && c != 0 || startCharCode > endCharCode)
                        throw new InvalidDataException("CmapFormat12");

                    old_group = new Group(startCharCode, endCharCode, startGlyphID);
                    _groups[c] = old_group;
                }
            }

            public override int GetGlyphIndex(int i)
            {
                uint ch = unchecked((uint) i);
                for (int c = 0; c < _groups.Length; c++)
                {
                    var group = _groups[c];

                    if (group.Range.Contains(ch))
                        return (int) (group.startGlyphID + ch - group.Range.Start);
                }

                //Glyphid not found
                return 0;
            }

            internal class Group
            {
                public readonly CmapFormat.Range Range;

                /// <summary>
                /// Glyph index corresponding to the starting character code
                /// </summary>
                public readonly uint startGlyphID;

                public Group(uint start, uint end, uint startGlyph)
                { startGlyphID = startGlyph; Range = new Range(start, end); }

                public override string ToString()
                {
                    return string.Format("{0} ID: {1}", Range.ToString(), startGlyphID);
                }
            }
        }

        /// <summary>
        /// Similar to format 12, except that groups use the same
        /// character for the whole range
        /// </summary>
        class CmapFormat13 : CmapFormat12
        {
            public CmapFormat13(StreamReaderEx s) : base(s) { }

            public override int GetGlyphIndex(int i)
            {
                uint ch = unchecked((uint)i);
                for (int c = 0; c < _groups.Length; c++)
                {
                    var group = _groups[c];

                    if (group.Range.Contains(ch))
                        return (int)(group.startGlyphID);
                }

                //Glyphid not found
                return 0;
            }
        }

        [DebuggerDisplay("({PlatformID} - {PlatSpecEnc}")]
        struct CmapDescriptor
        {
            public readonly PlatformID PlatformID;
            public readonly ushort PlatSpecEnc;
            
            /// <summary>
            /// Offset from the beginning of the file
            /// to the Cmap table
            /// </summary>
            public uint Offset;

            public CmapDescriptor(StreamReaderEx r, uint pos)
            {
                PlatformID = (TT.PlatformID)r.ReadUShort();
                PlatSpecEnc = r.ReadUShort();
                Offset = pos + r.ReadUInt();
            }
        }
    }
}
