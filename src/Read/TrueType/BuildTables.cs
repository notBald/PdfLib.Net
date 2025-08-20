#define ALIGN_GLYFS //<-- Aligns glyph data to 4 byte boundaries (specs recommends 2).
#define ALIGN_TABLES //<-- Tables will be aligned on 4 byte boundaries

//This code is slightly buggy in how it deals with composit glyphs,
//just intended for testing. Works fine with common fonts though.
//#define KEEP_GIDS //<-- Must match with TTfont.cs

//Todo: What is the correct cmap behavior regarding the 0xffff charcode?
//      The program I'm testing fails regardless, so this needs to be
//      tested with adobe.
//
//      If CMAP4_TRIM_FFFF, then the dummy 0xFFFF will be trimmed away from 
//      the table
//
//      If CMAP4_COPY_FFFF the dummy 0xFFFF gets the mapping of the "real"
//      0xFFFF
//
//      Both off means that there will always be a dummy 0xFFFF mapping 
//      to ndef, even when 0xFFFF is mapped to something else.
//#define CMAP4_TRIM_FFFF
//#define CMAP4_COPY_FFFF
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Read.TrueType
{
    class TTGlyphInfo
    {
        /// <summary>
        /// Used for the ndef character
        /// </summary>
        public const int UNDEFINED = -2;

        /// <summary>
        /// Used by composit glyphs without charcodes
        /// </summary>
        public const int UNKNOWN = -1;

        /// <summary>
        /// This is the final Gid, not the gid from
        /// the original file
        /// </summary>
        public int Gid;

        /// <summary>
        /// Allows for caching of data related to this glyph
        /// </summary>
        public object Tag;

        /// <summary>
        /// Unicode character
        /// </summary>
        public int CharCode;

        public TTGlyphInfo(int cc) { CharCode = cc; }
        public TTGlyphInfo(int cc, int gid) { CharCode = cc; Gid = gid; }
        public override string ToString()
        {
            return string.Format("{0} -> {1}", (char)CharCode, Gid);
        }
        /// <summary>
        /// Clone of TTGlyphInfo, without Tag data
        /// </summary>
        public TTGlyphInfo Clone()
        {
            return new TTGlyphInfo(CharCode, Gid);
        }
    }

    abstract class BuildTable : IComparable
    {
        public readonly Tag Tag;

        /// <summary>
        /// This is the offset to the start
        /// of the table directory for this
        /// table.
        /// </summary>
        internal int Offset;

        /// <summary>
        /// If the length is know, set it here
        /// </summary>
        internal int Length = -1;

        protected BuildTable(Tag tag)
        { Tag = tag; }

        public void WriteTableIndex(WriteFont s)
        {
            if (Length == -1)
            {
                Offset = s.Position;
                s.Write(Tag);
                s.Skip(12);
            }
            else
            {
                s.Skip(4);
                s.Write(CalcTableChecksum(s, Offset, Length));
                s.Write((uint)Offset);
                s.Write((uint)Length);
            }
        }

        internal static uint CalcTableChecksum(WriteFont s, int offset, int length)
        {
            var hold = s.Position;
            s.Position = offset;
            uint sum = 0;

#if ALIGN_TABLES
            int n_ints = (length + 3) / 4;
#else
            int n_ints = length / 4;
#endif
            while (n_ints-- > 0)
                sum += s.ReadUInt();

#if !ALIGN_TABLES
            int bytes = length - (length >> 2) * 4;
            for (int c = 0; c < bytes; c++)
                sum += (uint)s.ReadByte() << (24 - c * 8);
#endif

            s.Position = hold;

            return sum;
        }

        public void Write(WriteFont s, int write_cycle)
        {
#if ALIGN_TABLES
            s.Align();
#endif
            s.BeginWrite();
            if (WriteTable(s, write_cycle))
            {
                //We record the length and offset of the
                //written table
                Offset = s.Offset;
                Length = s.Length;
            }
        }

        public abstract bool WriteTable(WriteFont s, int write_cycle);

        public int CompareTo(object obj)
        {
            return (int)(Tag - ((BuildTable)obj).Tag);
        }
    }

    /// <summary>
    /// This is usefull when simply making a copy of an existing table
    /// </summary>
    class CopyTable : BuildTable
    {
        byte[] _data;
        public CopyTable(byte[] data, Tag tag)
            : base(tag)
        { _data = data; }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            //Copytables will write themselves to the bottom of the font
            if (write_cycle != 4) return false;
            s.Write(_data);
            return true;
        }
    }

    /// <summary>
    /// Builds the table directory
    /// </summary>
    class TDBuild
    {
        readonly List<BuildTable> _tables = new List<BuildTable>();
        readonly double _version;
        public TDBuild(double version) { _version = version; }
        public void Add(BuildTable table)
        { _tables.Add(table); }

        /// <summary>
        /// The font is written in four cycles
        ///  
        /// This is perhaps a bit stupid though. Instead one could 
        /// estimate the length of the tables, and with that the
        /// needed offsets, and write out the font in one go. However
        /// checksums, including one for the whole font, must still be
        /// calculated, IOW. buffering the whole font is quite easier.
        /// </summary>
        /// <param name="s">Stream to write the font it</param>
        /// <param name="head_table">
        /// Needs a ref to the head table to update it's checksum.
        /// It's of course possible to get this out of the tables
        /// list or somesuch.
        /// </param>
        public void WriteFont(WriteFont s, HeadBuild head_table)
        {
            #region Step 1. Sort tables

            //Sorts tables so that the lower tags comes first
            _tables.Sort();

            #endregion

            #region Step 2. Write header

            //Writes out the header
            s.WriteFixed(_version);
            int ntables = _tables.Count;

            //Writes number of tables
            s.Write((ushort)ntables);

            //Writes search range
            ushort max_pow = s.MaxPow(ntables);
            ushort srange = (ushort)(max_pow * 16);
            s.Write(srange);

            //Writes entery selector
            s.Write((ushort)Math.Log(max_pow, 2));

            //Writes range shift
            s.Write((ushort)(ntables * 16 - srange));

            //Cycle 0. Writes the table directory
            var table_directory = s.Position;
            foreach (BuildTable table in _tables)
                table.WriteTableIndex(s);

            #endregion

            #region Step 3. Write tables

            //Ideal table order for TrueType fonts:
            //
            //head, hhea, maxp, OS/2, hmtx, LTSH, VDMX, hdmx, 
            //cmap, fpgm, prep, cvt, loca, glyf, kern, name, 
            //post, gasp, PCLT, DSIG
            //
            //For CFF OpenType:
            //head, hhea, maxp, OS/2, name, cmap, post, CFF, 
            //(other tables, as convenient)
            //
            //My current order:
            //head, hhea, maxp, OS/2, cmap, hmtx, loca, glyf,
            //post, cvt, fpgm, gasp, name, prep
            //
            //The current way to write tables is a bit dumb,
            //so changing the order the tables are witten 
            //requires a little rewriting
            //
            //The only thing to keep in mind is that the loca
            //table must be written twice, once to make room
            //and once after the glyf table is written.

            //Cycle 1. Writes out tables that should be
            //         at the start of the font
            //         These are small headers which one
            //         may want to peek at before
            //         everything else.
            //
            //         Head, Hhea, Maxp, OS/2
            foreach (BuildTable table in _tables)
                table.Write(s, 1);

            //Cycle 2. Bigger tables
            //
            //         cmap, hmtx/vmtx and the loca table
            foreach (BuildTable table in _tables)
                table.Write(s, 2);

            //Cycle 3. Writes out the glyfs and
            //         post tables
            //
            //         glyf, post
            foreach (BuildTable table in _tables)
                table.Write(s, 3);

            //Cycle 4. Writes out the names,
            //         other copy tables and 
            //         completes the loca table
            //
            //         Name, GPOS, ...
            foreach (BuildTable table in _tables)
                table.Write(s, 4);

            #endregion

            #region Step 4. Update header with final data

#if ALIGN_TABLES

            var padd = s.Position % 4;
            if (padd != 0)
            {
                while (padd++ < 4)
                    s.Write((byte)0);
            }

#endif

            //Updates the TableDirectory with the offsets
            //to the table, and calculates checksums
            var hold = s.Position;
            s.Position = table_directory;
            foreach (BuildTable table in _tables)
                table.WriteTableIndex(s);
            s.Position = hold;

            #endregion

            #region Step 5. Calculate checksum for head table

            //Calculates the checksum of the entire font.
            hold = s.Position;
            uint checksum = BuildTable.CalcTableChecksum(s, 0, hold);
            s.Position = head_table.Offset;
            s.Skip(8);
            s.Write(checksum);
            s.Position = hold;

            #endregion
        }
    }

    /// <summary>
    /// For now we just dump all names as is. 
    /// </summary>
    /// <remarks>
    /// Should perhaps take a deeper look on the name table but
    /// AFAICT strings are standaraized, and with that one do not
    /// have to rewrite the table as all strings have the index/id
    /// they must have.
    /// 
    /// However one could probably get away with only the copyright 
    /// strings 
    /// </remarks>
    class NameBuild : BuildTable
    {
        readonly byte[] _data;
        public NameBuild(byte[] data)
            : base(Tag.name)
        {
            _data = data;
        }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            if (write_cycle != 4)
                return false;
            s.Write(_data);
            return true;
        }
    }

    class HmtxBuild : BuildTable
    {
        readonly byte[] _data;
        public readonly int NumOfLongHorMetrics;

        public HmtxBuild(Dictionary<int, TTGlyphInfo> glyphs, HmtxTable hmtx)
            : base(Tag.hmtx)
        {
            //Problem, not all glyphs have hmtx. Those glyphs
            //all share the last entery in the table. However since
            //gids are decided by the cmap, we must take some care
            //when deploying this feature.

            //This is the last original non-monospaced gid
            var last_gid = hmtx.Lhmx.NumOfLongHorMetrics;

            //Used to determine where we can stop adding width info
            int min_lsb_id = int.MaxValue, highest_non_lsb_id = int.MinValue;

            //Since we do not yet know how many hmtx this font
            //will include we place them in a dictionary
            var hmtxs = new Dictionary<int, LongHorMetrics>(glyphs.Count);
            var lsb = new Dictionary<int, short>(glyphs.Count);

            //Fetches all needed metrics
            foreach (var kv in glyphs)
            {
                int gid = kv.Key, new_gid = kv.Value.Gid;
                if (gid >= last_gid)
                {
                    lsb.Add(new_gid, hmtx.Lhmx[gid].Lsb);
                    min_lsb_id = Math.Min(min_lsb_id, new_gid);
                    continue;
                }
                else
                    highest_non_lsb_id = Math.Max(highest_non_lsb_id, gid);
                hmtxs.Add(new_gid, hmtx.Lhmx[gid]);
            }

            if (lsb.Count != 0)
            {
                //When there are monospaced glyphs we can give them all the same width,
                //but only when they are a continious series of gid
                var last_width = hmtx.Lhmx[last_gid].AdvanceWidth;

                //First we add hmtx for non continious monospaced glyphs.
                while (highest_non_lsb_id > min_lsb_id)
                {
                    //Note: Do to the way composite glyfs are handeled, that is by giving 
                    //their component glyphs trailing gids, I suspect highest_non_lsb_id 
                    //will always be over min if there's a composite glyf in the font.)

                    //Adds hmtx for this glyph
                    hmtxs.Add(min_lsb_id, new LongHorMetrics(last_width, lsb[min_lsb_id]));

                    //Removes any lsb data
                    lsb.Remove(min_lsb_id++);
                }

                //Then we add the last hmtx if needed
                if (lsb.Count > 0 && last_width != hmtxs[highest_non_lsb_id].AdvanceWidth)
                {
                    //We need to insert the advanceWidth for the first lsb (monospaced) glyph.
                    var min_lsb = lsb[min_lsb_id];
                    lsb.Remove(min_lsb_id);
                    hmtxs.Add(min_lsb_id, new LongHorMetrics(hmtx.Lhmx[last_gid].AdvanceWidth, min_lsb));
                }
            }

            //Creates the data. One could in theory take the original
            //data, but the TT parser doesn't support that.
            _data = new byte[hmtxs.Count * 4 + lsb.Count * 2];
            foreach (var kv in hmtxs)
            {
                int pos = kv.Key * 4;
                var h = kv.Value;
                WriteFont.Write(h.AdvanceWidth, _data, pos);
                WriteFont.Write(h.Lsb, _data, pos + 2);
            }

            //This value does not count monospaced lsbs, that way one can
            //find out what are just lsb and what is both
            NumOfLongHorMetrics = hmtxs.Count;

            //Writes lsb for monospaced fonts. To find the correct position
            //we first negate the size of the lsb, then we also negate the
            //size of the min_id so that we can find the spot by simply
            //multiplying the id with 2.
            int lsb_pos = _data.Length - lsb.Count * 2 - min_lsb_id * 2;
            foreach (var kv in lsb)
                WriteFont.Write(kv.Value, _data, lsb_pos + 2 * kv.Key);
        }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            //We wait until the cmap table is written before writing this.
            if (write_cycle == 2)
            {
                s.Write(_data);
                return true;
            }
            return false;
        }
    }

    class VmtxBuild : BuildTable
    {
        readonly byte[] _data;
        public readonly int NumOfLongVerMetrics;

        public VmtxBuild(Dictionary<int, TTGlyphInfo> glyphs, VmtxTable vmtx)
            : base(Tag.vmtx)
        {
            //Problem, not all glyphs have hmtx. Those glyphs
            //all share the last entery in the table.
            var last_gid = vmtx.Lvmx.NumOfLongVerMetrics - 1;
            bool has_last_hmtx = false;
            int min_lsb_id = int.MaxValue;

            //Since we do not yet know how many hmtx this font
            //we'll include we place them in a dictionary
            var hmtxs = new Dictionary<int, LongVerMetrics>(glyphs.Count);
            var lsb = new Dictionary<int, short>(glyphs.Count);

            //Fetches all needed metrics
            foreach (var kv in glyphs)
            {
                int gid = kv.Key, new_gid = kv.Value.Gid;
                if (gid >= last_gid)
                {
                    if (gid > last_gid)
                    {
                        lsb.Add(new_gid, vmtx.Lvmx[gid].Tsb);
                        min_lsb_id = Math.Min(min_lsb_id, new_gid);
                        continue;
                    }

                    has_last_hmtx = true;
                }
                hmtxs.Add(new_gid, vmtx.Lvmx[gid]);
            }

            if (lsb.Count != 0 && !has_last_hmtx)
            {
                //We need to insert the advanceWidth for
                //the first lsb glyph.
                var min_lsb = lsb[min_lsb_id];
                lsb.Remove(min_lsb_id);
                hmtxs.Add(min_lsb_id, new LongVerMetrics(vmtx.Lvmx[last_gid].AdvanceHeight, min_lsb));
            }

            //Creates the data. One could in theory take the original
            //data, but the TT parser doesn't support that.
            _data = new byte[hmtxs.Count * 4 + lsb.Count * 2];
            foreach (var kv in hmtxs)
            {
                int pos = kv.Key * 4;
                var h = kv.Value;
                WriteFont.Write(h.AdvanceHeight, _data, pos);
                WriteFont.Write(h.Tsb, _data, pos + 2);
            }

            //This value does not count monospaced lsbs, that way one can
            //find out what are just lsb and what is both
            NumOfLongVerMetrics = hmtxs.Count;

            //Writes lsb for monospaced fonts. To find the correct position
            //we first negate the size of the lsb, then we also negate the
            //size of the min_id so that we can find the spot by simply
            //multiplying the id with 2.
            int lsb_pos = _data.Length - lsb.Count * 2 - min_lsb_id * 2;
            foreach (var kv in lsb)
                WriteFont.Write(kv.Value, _data, lsb_pos + 2 * kv.Key);
        }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            //We wait until the cmap table is written before writing this.
            if (write_cycle == 2)
            {
                s.Write(_data);
                return true;
            }
            return false;
        }
    }

    class GlyfBuild : BuildTable
    {
        public readonly byte[][] GlyphData;
#if !ALIGN_GLYFS
        int _loc_format;
#endif
#if KEEP_GIDS
        //This is buggy test code, don't use.
        public GlyfBuild(Dictionary<int, TTGlyphInfo> glyphs, GlyfTable glyph_table, int nglyfs)
            : base(Tag.glyf)
        {
            GlyphData = new byte[nglyfs][];
            for (int c = 0; c < GlyphData.Length; c++)
                GlyphData[c] = new byte[0];

            var new_glyphs = new Dictionary<int, TTGlyphInfo>();

            foreach (var kv in glyphs)
            {
                int gid = kv.Key;

                SetGlyphData(glyph_table, gid, glyphs, new_glyphs);
            }
        }

        private void SetGlyphData(GlyfTable glyph_table, int gid, Dictionary<int, TTGlyphInfo> glyphs, Dictionary<int, TTGlyphInfo> new_glyphs)
        {
            //Fetches the original glyph
            var glyph = glyph_table.GetGlyph(gid);
            var data = glyph_table.GetGlyphData(gid);

            if (glyph is CompositeGlyphs)
            {
                var cg = (CompositeGlyphs)glyph;
                foreach (CompositeGlyphs.CGlyph g in cg.Glyphs)
                {
                    //First we look for the glyph among those that
                    //are already created.
                    TTGlyphInfo glyph_info;
                    int glyph_id;
                    if (glyphs.TryGetValue(g.GlyphIndex, out glyph_info))
                        glyph_id = glyph_info.Gid;
                    else
                    {
                        //Then we look among the newly created glyphs
                        if (new_glyphs.TryGetValue(g.GlyphIndex, out glyph_info))
                            glyph_id = glyph_info.Gid;
                        else
                        {
                            //Failing to find the glyf, we must fetch it
                            //from the original data
                            //Bug: composit composit glyfs not supported rigt now
                            var cdata = glyph_table.GetGlyphData(g.GlyphIndex);

                            //So that we kind find new glyphs if they are reused.
                            new_glyphs.Add(g.GlyphIndex, new TTGlyphInfo(TTGlyphInfo.UNKNOWN, g.GlyphIndex));

                            SetGlyphData(glyph_table, g.GlyphIndex, glyphs, new_glyphs);
                        }
                    }
                }
            }
            if (data != null)
                GlyphData[gid] = data;
        }
#else
        public GlyfBuild(Dictionary<int, TTGlyphInfo> glyphs, GlyfTable glyph_table, int next_gid, out int loc_format)
            : base(Tag.glyf)
        {
            //First we fetch the glyph data and any missing glyphs
            var new_glyphs = new Dictionary<int, TTGlyphInfo>();
            var glyph_data = new Dictionary<int, byte[]>(glyphs.Count);

            foreach (var kv in glyphs)
                SetGlyphData(kv.Key, kv.Value.Gid, ref next_gid, glyph_table, glyphs, new_glyphs, glyph_data, false);

            //We add all new glyphs. This couldn't be done in the foreach loop which is
            //why we do it now :)
            foreach (var kv in new_glyphs)
                glyphs.Add(kv.Key, kv.Value);

            //We now order the glyph data so that it's in the order it
            //needs to be when written out to the file. We can do this
            //since the cmap has calculated the needed GIDs for the 
            //glyphs with charcode, and SetGlyphData has done so for those
            //without.
            GlyphData = new byte[glyph_data.Count][];
            var length = 0;
            foreach (var kv in glyph_data)
            {
                var ba = kv.Value;

                //Glyphs without glyph data is allowed. (For instance
                //the space character usually has no data)
                if (ba == null) ba = new byte[0];
                GlyphData[kv.Key] = ba;
                length += ba.Length;
            }

            //Loc format 0 is more compact but can only address up to 128KB of data
            loc_format = (length > ushort.MaxValue * 2) ? 1 : 0;
#if !ALIGN_GLYFS
            _loc_format = loc_format;
#endif
        }

        private void SetGlyphData(
            int old_gid, int new_gid, ref int next_gid,
            GlyfTable glyph_table,
            Dictionary<int, TTGlyphInfo> glyphs,
            Dictionary<int, TTGlyphInfo> new_glyphs,
            Dictionary<int, byte[]> glyph_data,
            bool create_TTGlyphInfo)
        {
            //Fetches the original glyph
            var glyph = glyph_table.GetGlyph(old_gid);
            var data = glyph_table.GetGlyphData(old_gid);

            //Assumes that a composite glyph don't contain itself,
            //because if it does we'll end up in an endless loop.
            if (glyph is CompositeGlyphs)
            {
                //This is the position of the composit glyph, from
                //the start of the file. We need it to find the
                //actuall position of the CGlyph inside the
                //composit glyph's data
                //
                //We can do this since the TT parser has been modified
                //to collect the needed position for us (g.Offset),
                //except that g.Offset is from the beginning of the 
                //original file.
                int offset = (int)glyph_table.GetGlyphPos(old_gid);

                //We go through the list of component glyphs and add
                //anyone that's missing
                var cg = (CompositeGlyphs)glyph;
                foreach (CompositeGlyphs.CGlyph g in cg.Glyphs)
                {
                    //First we look for the glyph among those that
                    //are already created.
                    TTGlyphInfo glyph_info;
                    int glyph_id;
                    if (glyphs.TryGetValue(g.GlyphIndex, out glyph_info))
                        glyph_id = glyph_info.Gid;
                    else
                    {
                        //Then we look among the newly created glyphs
                        if (new_glyphs.TryGetValue(g.GlyphIndex, out glyph_info))
                            glyph_id = glyph_info.Gid;
                        else
                        {
                            //Failing to find the glyf, we must fetch it
                            //from the original data
                            glyph_id = next_gid;
                            SetGlyphData(g.GlyphIndex, next_gid++, ref next_gid, glyph_table, glyphs, new_glyphs, glyph_data, true);
                        }
                    }

                    //Updates the composit glyph's id.
                    int pos = g.Offset - offset;
                    data[pos] = (byte)((glyph_id >> 8) & 0xFF);
                    data[pos + 1] = (byte)(glyph_id & 0xFF);
                }
            }

            glyph_data.Add(new_gid, data);
            if (create_TTGlyphInfo)
            {
                //So that we will find new glyphs if they are reused.
                new_glyphs.Add(old_gid, new TTGlyphInfo(TTGlyphInfo.UNKNOWN, new_gid));
            }
        }
#endif

        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            //The glyf table is large, so it should wait until later
            //to write itself
            if (write_cycle != 3)
                return false;

            for (int c = 0; c < GlyphData.Length; c++)
            {
#if ALIGN_GLYFS
                s.Align();
#else
                if (_loc_format == 0)
                    s.ShortAlign();
#endif
                s.Write(GlyphData[c]);
            }
            return true;
        }
    }

    /// <summary>
    /// This table contains the location of the glyphs
    /// </summary>
    /// <remarks>
    /// Note that it is written after the glyf table,
    /// so to get this table higher up in the file we
    /// wait a bit with actually writing the glyfs
    /// </remarks>
    class LocaBuild : BuildTable
    {
        readonly GlyfBuild _glyphs;
        readonly int indexToLocFormat = 0;
        public LocaBuild(GlyfBuild glyphs, int indexToLocFormat)
            : base(Tag.loca)
        { _glyphs = glyphs; this.indexToLocFormat = indexToLocFormat; }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            //We wait until the cmap is written
            if (write_cycle == 2)
            {
                //We don't yet know the position of the
                //glyphs, so we make room for them.
                if (indexToLocFormat == 0)
                    Length = (_glyphs.GlyphData.Length + 1) * 2;
                else
                    Length = (_glyphs.GlyphData.Length + 1) * 4;
                s.Skip(Length);

                return true;
            }
            else if (write_cycle == 4)
            {
                //Updates the table with the glyf offsets
                var hold = s.Position;
                s.Position = Offset;
                var gd = _glyphs.GlyphData;

                if (indexToLocFormat == 0)
                {
                    int offset = _glyphs.Offset;
                    for (int c = 0; c < gd.Length; c++)
                    {
                        s.Write((ushort)((offset - _glyphs.Offset) / 2));
                        offset += gd[c].Length;

                        //Glyfs are written aligned. Not required by the specs, but
                        //recommended.
#if ALIGN_GLYFS
                        var adjust = offset % 4;
                        if (adjust != 0) offset += 4 - adjust;
#endif
                    }

                    //The loca table has one offset at the end pointing at the
                    //end of the glyf data
                    s.Write((ushort)((_glyphs.Length + 1) / 2));
                }
                else
                {
                    for (int c = 0, offset = _glyphs.Offset; c < gd.Length; c++)
                    {
                        s.Write((uint)(offset - _glyphs.Offset));
                        offset += gd[c].Length;

                        //Glyfs are written aligned. Not required by the specs, but
                        //recommended.
#if ALIGN_GLYFS
                        var adjust = offset % 4;
                        if (adjust != 0) offset += 4 - adjust;
#endif
                    }

                    //The loca table has one offset at the end pointing at the
                    //end of the glyf data
                    s.Write((uint)_glyphs.Length);
                }

                s.Position = hold;
            }
            return false;
        }
    }

    class CMapBuild : BuildTable
    {
        GidRange[] _ranges; int hold;
        readonly CMapEncoding[] Formats;
        public enum CMapType
        {
            /// <summary>
            /// Microsoft unicode cmap
            /// </summary>
            cmap_3_1,

            /// <summary>
            /// Microsoft symbolic cmap
            /// </summary>
            cmap_3_0
        }

        /// <summary>
        /// Creates CMap tables
        /// </summary>
        /// <param name="cmaps">What cmaps to create</param>
        public CMapBuild(
            CMapType[] cmaps
            ) : base(Tag.cmap) {
            Formats = new CMapEncoding[cmaps.Length];
            for(int c=0; c < Formats.Length; c++)
                switch (cmaps[c])
                {
                    case CMapType.cmap_3_1:
                        Formats[c] = new CMapEncoding(PlatformID.Microsoft, 1, 4);
                        break;

                    case CMapType.cmap_3_0:
                        Formats[c] = new CMapEncoding(PlatformID.Microsoft, 0, 4);
                        break;
                }
        }

        public GidRange[] Ranges 
        { 
            get { return _ranges; }
            set { _ranges = value; }
        }

        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            if (write_cycle != 2) return false;

            //This is also set by the caller, but we need the value now
            Offset = (int)s.Position;

            //Writes out the version number
            s.Write((ushort)0);

            //Number of CMap tables
            s.Write((ushort)Formats.Length);

            //Writes out the index
            var index_pos = s.Position;
            for (int c = 0; c < Formats.Length; c++)
            {
                var format = Formats[c];
                s.Write((ushort)format.Type);
                s.Write((ushort)format.PSE);
                s.Skip(4);
            }


            //Writes out the CMap tables
            uint[] offsets = new uint[Formats.Length];
            for (int c = 0; c < Formats.Length; c++)
            {
                s.Align();

                var format = Formats[c];
                offsets[c] = (uint)(s.Position - Offset);
                s.Write(format.Format);
                s.Skip(4); //<-- skips over length and language

                if (format.Type == Read.TrueType.PlatformID.Microsoft)
                    WriteCMap4(s);
                else
                {
                    //AFACT there's never any need for another cmap. Format
                    //4 support up to 65k glyphs, and that the implementation
                    //limit for PDF files (See Annex C in the specs)
                    throw new NotImplementedException();
                }

                //Updates length
                hold = s.Position;
                s.Position = (int)offsets[c] + Offset;
                s.Skip(2);
                s.Write((ushort)(hold - (int)(offsets[c] + Offset)));
                s.Position = hold;
            }

            //Updates the offsets
            hold = s.Position;
            s.Position = index_pos;
            for (int c = 0; c < offsets.Length; c++)
            {
                s.Skip(4);
                s.Write(offsets[c]);
            }
            s.Position = hold;

            return true;
        }

        /// <summary>
        /// Reformates the map into a set of ranges. This
        /// function also updates the glyph GIDs so that
        /// they are continuous.
        /// </summary>
        /// <returns>The next avalible gid</returns>
        /// <remarks>
        /// The ranges are uniform, so that characters ABC map to e.g. gid 1,2,3.
        /// No provision are made for special gids beyond gid 0
        /// </remarks>
        public int CreateRanges(Dictionary<int, TTGlyphInfo> glyphs, Dictionary<int, TTGlyphInfo> cidtogid)
        {
            var cid_ranges = new CidRange[32];
            int size = 0;
            bool has_unkown_glyfs = false;

            foreach (var kv in glyphs)
            {
                //Note kv.key == gid in source file, while kv..gid is
                //for the destination file and hasn't been set yet.
                var ch_code = kv.Value.CharCode;

                if (ch_code < 0)
                {
                    if (ch_code == TTGlyphInfo.UNKNOWN)
                    {
                        //Unkown glyfs can be in the table if one
                        //save a font again after having already
                        //done so. In that case we need to insure
                        //that there is no gap/overlap in the gids.
                        has_unkown_glyfs = true;
                    }
                    //We also skip over the UNDEFINED glyph. Its
                    //gid must always be zero regardless.
                    continue;
                }

                CidRange prev = null;
                for (int c = 0; c < cid_ranges.Length; c++)
                {
                    var range = cid_ranges[c];
                    if (range == null || ch_code < range.StartChar)
                    {
                        //We insert the range
                        if (prev != null)
                        {
                            //Test if adjacent
                            if (prev.EndChar + 1 == ch_code)
                            {
                                //Inserts the character into this range
                                prev.EndChar++;
                                prev.CidToGid.AddLast(new Map(kv.Key, ch_code));

                                if (range != null && ch_code + 1 == range.StartChar)
                                {
                                    //Mergees adjacent ranges
                                    prev.Merge(range);
                                    if (--size - c > 0)
                                        Array.Copy(cid_ranges, c + 1, cid_ranges, c, size - c);
                                    cid_ranges[size] = null;
                                }

                                break;
                            }
                        }

                        if (range != null)
                        {
                            //Test if adjacent.
                            if (range.StartChar - 1 == ch_code)
                            {
                                range.StartChar--;
                                range.CidToGid.AddFirst(new Map(kv.Key, ch_code));
                                //We know the character isn't adjacent
                                //with the previous range, as it would
                                //have been merged into it if it was.
                                break;
                            }

                            //Inserts the range
                            if (size == cid_ranges.Length)
                                Array.Resize<CidRange>(ref cid_ranges, size * 2);
                            Array.Copy(cid_ranges, c, cid_ranges, c + 1, size - c);
                        }

                        cid_ranges[c] = new CidRange(ch_code, kv.Key);
                        size++;

                        //cid_ranges must always be bigger than size
                        if (size == cid_ranges.Length)
                            Array.Resize<CidRange>(ref cid_ranges, size * 2);

                        break;
                    }
                    prev = range;
                }
            }

            //We want gids to be uniform within the ranges, i.e. A-C
            //gives gids from 1 to 3. We accomplish this by setting the
            //gids in the same order as the ranges.
            _ranges = new GidRange[size];
            int gid = 1;
            for (int c = 0; c < _ranges.Length; c++)
            {
                var range = cid_ranges[c];
                foreach (var map in range.CidToGid)
                    glyphs[map.Gid].Gid = gid++;
                _ranges[c] = new GidRange(range.StartChar, range.EndChar);
            }

            //Multiple characters can be mapped to the same gid. Now that
            //we know the final gids we can make sense of those characters
            //too.
            if (cidtogid != null)
            {
                List<int> char_codes = new List<int>(cidtogid.Count);
                foreach (int char_code in cidtogid.Keys)
                    char_codes.Add(char_code);
                char_codes.Sort();

                //We now have a list of charcodes in ascending order.

                var new_ranges = new List<GidRange>(char_codes.Count);
                var gids = new List<int>(char_codes.Count);

                //We create a list of new ranges
                int start = char_codes[0], end = start;
                gids.Add(cidtogid[start].Gid);
                for (int c = 1, cc_size = char_codes.Count; c < cc_size; c++)
                {
                    int char_code = char_codes[c];
                    if (char_code > end + 1)
                    {
                        //We create a new range
                        new_ranges.Add(new GidRange(start, end, gids.ToArray()));
                        gids.Clear();
                        start = char_code;
                        end = char_code;
                    }
                    else
                    {
                        //We enlarge the current range
                        end = char_code;
                    }
                    gids.Add(cidtogid[char_code].Gid);
                }
                //Creates the last range
                new_ranges.Add(new GidRange(start, end, gids.ToArray()));

                //We make room for the new ranges
                Array.Resize<GidRange>(ref _ranges, _ranges.Length + new_ranges.Count);

                //The ranges must now be inserted. There's little point
                //in trying to merge these ranges with the existing ones
                //as even if the characters sit next to eachother it's
                //unlikely that the gids do.
                foreach (var range in new_ranges)
                {
                    //We first test if a range is uniform, if so we
                    //shrink the gid data
                    int last = range.Gids[0];
                    for (int c = 1; c < range.Gids.Length; c++)
                    {
                        int cc_gid = range.Gids[c];
                        if (cc_gid == last + 1)
                            last = cc_gid;
                        else
                        {
                            //One could split up ranges into "uniform"
                            //and "not uniform" here.
                            last = -1;
                            break;
                        }
                    }
                    if (last != -1)
                        range.Gids = new int[] { range.Gids[0] };

                    //Now we insert the range into the table. (We assume
                    //that's impossible for ranges to overlap.)
                    for (int c = 0; c < _ranges.Length; c++)
                    {
                        var a_range = _ranges[c];
                        if (a_range == null)
                        {
                            _ranges[c] = range;
                            break;
                        }
                        else if (range.EndChar < a_range.StartChar)
                        {
                            Array.Copy(_ranges, c, _ranges, c + 1, size - c);
                            _ranges[c] = range;
                            break;
                        }
                    }

                    //We always insert one range per itteration
                    size++;
                }
            }

            if (has_unkown_glyfs)
            {
                //We give unkown glyfs in the table a new gid, as it's
                //possible that the gids overlap with those made for
                //the cmapped glyfs or for there to be a gap in the gids. 
                foreach (var kv in glyphs)
                    if (kv.Value.CharCode == TTGlyphInfo.UNKNOWN)
                        kv.Value.Gid = gid++;
            }

            return gid;
        }

        private void WriteCMap4(WriteFont s)
        {
#if CMAP4_TRIM_FFFF
            int segCount;

            //Checking for has_0xffff everywhere is a bit ugly. To fix this 
            //one could add a dummy entery into _ranges with the parameter:
            // "new GidRange(0xFFFF, 0xFFFF)"
            //
            //The loop "for (int c = 0, gid = 1; c < _ranges.Length; c++)"
            //will then balk things up, so it has to be stopped before it
            //cmaps the dummy entery.
            //
            //Having done that one can remove the "if (has_0xffff)" in most
            //places
            bool has_0xffff = _ranges[_ranges.Length - 1].EndChar == 0xFFFF;
                
            if (has_0xffff)
            {
                //The range must end with 0xFFFF, however that does not
                //prevent 0xFFFF from being a valid character. What we
                //do is to always add 0xFFFF (the + 1). However in this
                //case we already have a 0xFFFF code, so we do not need
                //to add it
                if (_ranges[_ranges.Length - 1].Length != 1)
                {
                    //But...
                    //Both the start code and the end code is required
                    //to be 0xFFFF. 
                    //When the last range has a different start code we
                    //rewrite the _ranges table a little. 
                    //
                    //We can do this since no other code depend on this 
                    //table (and since this is a Cmap4 quirk it should 
                    //not be done in the "CreateRanges" function)

                    //Fetches the last range, the one containing 0xFFFF
                    var r = _ranges[_ranges.Length - 1];

                    //Resizes the _ranges array to make room for the new
                    //entery
                    Array.Resize<GidRange>(ref _ranges, _ranges.Length + 1);
                    int[] gids = null, ff_gids = null;

                    //If the entery have gids, we have to remove one and
                    //add it to our new entery
                    if (r.Gids != null)
                    {
                        gids = r.Gids;
                        ff_gids = new int[] { gids[gids.Length - 1] };
                        Array.Resize(ref gids, gids.Length - 1);
                    }

                    //Adds the modified entery, now without 0xFFFF
                    _ranges[_ranges.Length - 2] = new GidRange(r.StartChar, r.EndChar - 1, gids);

                    //Adds the new 0xFFFF entery
                    _ranges[_ranges.Length - 1] = new GidRange(0xFFFF, 0xFFFF, ff_gids);
                }
                segCount = _ranges.Length;
            }
            else
                segCount = _ranges.Length + 1;
#else
            //The range must end with 0xFFFF, however that does not
            //prevent 0xFFFF from being a vaild character. What we
            //do is to always add 0xFFFF (the + 1) even if there's
            //already a 0xFFFF present. That means 0xFFFF will be
            //mapped twice, but at least to my own TT parser this
            //is unproblematic.
            int segCount = _ranges.Length + 1;
#endif

            #region Writes out the search information
            int max_power_of_2 = (int)Math.Pow(2, s.MaxPow(segCount));
            ushort segCountX2 = (ushort)(segCount * 2);
            s.Write(segCountX2);
            //searchRange have two formulas in the specs, using
            //the one from the example
            ushort searchRange = (ushort)(2 * max_power_of_2);
            s.Write(searchRange);

            ushort entrySelector = (ushort)Math.Log(searchRange / 2, 2);
            s.Write(entrySelector);

            ushort rangeShift = (ushort)(segCountX2 - searchRange);
            s.Write(rangeShift);
            #endregion

            #region Writes out the range boundaries

            //Writes the end codes
            for (int c = 0; c < _ranges.Length; c++)
                s.Write((ushort)_ranges[c].EndChar);
#if CMAP4_TRIM_FFFF
            if (!has_0xffff)
#endif
            s.Write((ushort)0xFFFF);

            //Reserved
            s.Skip(2);

            //Writes the start codes
            for (int c = 0; c < _ranges.Length; c++)
                s.Write((ushort)_ranges[c].StartChar);
#if CMAP4_TRIM_FFFF
            if (!has_0xffff)
#endif
            s.Write((ushort)0xFFFF);

            #endregion

            #region CharCode to GID mapping

            //For non-uniform ranges the offsets of the gid
            //values are put in this array 
            ushort[] idRangeOffs = null;

            //The GID data for non-uniform rnages are put here
            List<int[]> index_array = null;

            //Size in bytes of the gid data
            ushort glyf_index_size = 0;

            for (int c = 0, gid = 1; c < _ranges.Length; c++)
            {
                var range = _ranges[c];
                if (range.Gids != null)
                {
                    //This is a range that overlaps with another range.
                    //That means "gid" must not be incremented.
                    if (range.Gids.Length == 1)
                    {
                        //This is a uniform range (or a range with a single
                        //glyf).
                        short idDelta = (short)(range.Gids[0] % 65536 - range.StartChar);
                        s.Write(idDelta);
                    }
                    else
                    {
                        //A non uniform range must make use of the glyphIndexArray
                        if (idRangeOffs == null)
                        {
                            idRangeOffs = new ushort[_ranges.Length];
                            index_array = new List<int[]>(_ranges.Length);
                        }
                        //The offset is from the current position in idRangeOff, so we add
                        //what's left of to write of that table
                        idRangeOffs[c] = (ushort)(glyf_index_size + (segCount - c) * 2);
                        glyf_index_size += (ushort)(range.Gids.Length * 2);
                        index_array.Add(range.Gids);

                        //I'm not 100% sure what the idDelta value should be when using
                        //idRangeOffset, but I'm guessing 0
                        s.Write((ushort)0);
                    }
                }
                else
                {
                    //I know the specs say idDelta is ushort, but then 
                    //they put negative values into it in the example
                    short idDelta = (short)(gid % 65536 - range.StartChar);
                    s.Write(idDelta);
                    gid += range.Length;
                }
            }
#if CMAP4_TRIM_FFFF
            if (!has_0xffff)

                //I suspect that this value is 1 since (65535 + 1) % 65536 
                //yields a gid of 0, i.e. the ndef glyf
                s.Write((ushort)1); //<-- is 1 in the example
#else
#if CMAP4_COPY_FFFF
            bool has_0xffff = _ranges[_ranges.Length - 1].EndChar == 0xFFFF;
            if (has_0xffff)
            {
                //I copy the last value. The math should still be the
                //same as one substract this number from the charcode,
                //an it hasn't changed.
                s.Position -= 2;
                s.Write(s.ReadShort());
            }
            else
#endif
                s.Write((ushort)1); //<-- is 1 in the example
#endif
            if (idRangeOffs == null)
            {
                //Fills out idRangeOffset with zero as we don't use that functionality
                for (int c = 0; c < _ranges.Length; c++)
                    s.Write((ushort)0);
            }
            else
            {
                //Writes out the offsets into the glyf index table. 
                for (int c = 0; c < idRangeOffs.Length; c++)
                    s.Write(idRangeOffs[c]);
            }
#if CMAP4_TRIM_FFFF
            if (!has_0xffff)
                //By setting this 0 we say the 0xffff glyf does not have a glyf index.
                s.Write((ushort)0); //<-- is 0 in the example
#else
#if CMAP4_COPY_FFFF
            if (has_0xffff && idRangeOffs != null && idRangeOffs[idRangeOffs.Length - 1] != 0)
            {
                //Since we're to bytes ahead we write the last offset.
                //We also remove the size of that range.
                s.Write((ushort)(idRangeOffs[idRangeOffs.Length - 1]
                    - 4 + _ranges[_ranges.Length - 1].Length * 2));
            }
            else
#endif
                s.Write((ushort)0); //<-- is 0 in the example
#endif

            //Writes out glyfindexes, if any
            if (index_array != null)
            {
                foreach (var index in index_array)
                    for (int c = 0; c < index.Length; c++)
                        s.Write((ushort)index[c]);
            }

            #endregion
        }

        struct Map
        {
            public Map(int gid, int cid) { Gid = gid; CharCode = cid; }
            public readonly int Gid;
            public readonly int CharCode;
            public override string ToString()
            { return string.Format("{0}: {1} - {2}", (char)CharCode, CharCode, Gid); }
        }
        struct CMapEncoding
        {
            public readonly PdfLib.Read.TrueType.PlatformID Type;
            public readonly ushort PSE;
            public readonly ushort Format;
            public CMapEncoding(PdfLib.Read.TrueType.PlatformID t, ushort pse, int format)
            { Type = t; PSE = pse; Format = (ushort)format; }
        }
        /// <summary>
        /// Gids are assumed to be in the order the ranges
        /// are laid out
        /// </summary>
        internal class GidRange
        {
            public readonly int StartChar;
            public readonly int EndChar;
            public int[] Gids;
            public int Length { get { return EndChar - StartChar + 1; } }
            public GidRange(int start, int end)
            { StartChar = start; EndChar = end; }
            public GidRange(int start, int end, int[] gids)
            { StartChar = start; EndChar = end; Gids = gids; }
            public bool Inside(GidRange r)
            { return StartChar >= r.StartChar && EndChar <= r.EndChar; }
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0} - {1} : {2}", StartChar, EndChar, (char)StartChar);
                for (int sc = StartChar + 1; sc <= EndChar; sc++)
                    sb.AppendFormat(", {0}", (char)sc);
                return sb.ToString();
            }
        }
        /// <summary>
        /// Ranges that can map from cid to gid
        /// </summary>
        class CidRange
        {
            public int StartChar;
            public int EndChar;
            public LinkedList<Map> CidToGid = new LinkedList<Map>();
            public CidRange(int start, int gid)
            { StartChar = start; EndChar = start; CidToGid.AddFirst(new Map(gid, start)); }
            public void Merge(CidRange r)
            {
                EndChar = r.EndChar;
                //LinkedList does not support chaining, hmz.
                foreach (Map map in r.CidToGid)
                    CidToGid.AddLast(map);
            }
            public override string ToString()
            { return string.Format("{0} - {1}", StartChar, EndChar); }
        }
    }

    public enum PANOSE_Proportion : byte
    {
        Any,
        NoFit,
        OldStyle,
        Modern,
        EvenWidth,
        Expanded,
        Condensed,
        VeryExpanded,
        VeryCondensed,
        Monospaced
    }

    /// <summary>
    /// The OS/2 table
    /// </summary>
    class OS2Build : BuildTable
    {
        readonly byte[] _data;

        public ushort Version { get { return WriteFont.ReadUShort(_data, 0); } }
        public short XAvgCharWidth { get { return WriteFont.ReadShort(_data, 2); } }
        public ushort WeightClass { get { return WriteFont.ReadUShort(_data, 4); } }
        public ushort WidthClass { get { return WriteFont.ReadUShort(_data, 6); } }
        public Restriction Type { get { return (Restriction) WriteFont.ReadUShort(_data, 8); } }
        public short SubscriptXSize { get { return WriteFont.ReadShort(_data, 10); } }
        public short SubscriptYSize { get { return WriteFont.ReadShort(_data, 12); } }
        public short SubscriptXOffset { get { return WriteFont.ReadShort(_data, 14); } }
        public short SubscriptYOffset { get { return WriteFont.ReadShort(_data, 16); } }
        public short SuperscriptXSize { get { return WriteFont.ReadShort(_data, 18); } }
        public short SuperscriptYSize { get { return WriteFont.ReadShort(_data, 20); } }
        public short SuperscriptXOffset { get { return WriteFont.ReadShort(_data, 22); } }
        public short SuperscriptYOffset { get { return WriteFont.ReadShort(_data, 24); } }
        public short StrikeoutSize { get { return WriteFont.ReadShort(_data, 26); } }
        public short StrikeoutPosition { get { return WriteFont.ReadShort(_data, 28); } }
        public short FamilyClass { get { return WriteFont.ReadShort(_data, 30); } }
        public byte PANOSE_FamilyType { get { return _data[32]; } }
        public byte PANOSE_SerifStyle { get { return _data[33]; } }
        public byte PANOSE_Weight { get { return _data[34]; } }
        public PANOSE_Proportion PANOSE_Proportion { get { return (PANOSE_Proportion) _data[35]; } }
        public byte PANOSE_Contrast { get { return _data[36]; } }
        public byte PANOSE_StrokeVariation { get { return _data[37]; } }
        public byte PANOSE_ArmStyle { get { return _data[38]; } }
        public byte PANOSE_Letterform { get { return _data[39]; } }
        public byte PANOSE_Midline { get { return _data[40]; } }
        public byte PANOSE_XHeight { get { return _data[41]; } }

        //These should be modified with the ranges supported by MS cmap format 4
        public uint UnicodeRange1 { get { return WriteFont.ReadUInt(_data, 42); } }
        public uint UnicodeRange2 { get { return WriteFont.ReadUInt(_data, 46); } }
        public uint UnicodeRange3 { get { return WriteFont.ReadUInt(_data, 50); } }
        public uint UnicodeRange4 { get { return WriteFont.ReadUInt(_data, 54); } }

        public string AchVendID { get { return Read.Lexer.GetString(_data, 58, 4); } }
        public ushort Selection { get { return WriteFont.ReadUShort(_data, 62); } }

        //These should be modified with min/max charindex of MS cmap format 4
        public ushort FirstCharIndex { get { return WriteFont.ReadUShort(_data, 64); } }
        public ushort LastCharIndex { get { return WriteFont.ReadUShort(_data, 66); } }

        public short TypoAscender { get { return WriteFont.ReadShort(_data, 68); } }
        public short TypoDescender { get { return WriteFont.ReadShort(_data, 70); } }
        public short TypoLineGap { get { return WriteFont.ReadShort(_data, 72); } }

        public ushort WinAscent { get { return WriteFont.ReadUShort(_data, 74); } }
        public ushort WinDescent { get { return WriteFont.ReadUShort(_data, 76); } }

        //Format 1 properties: (Should be modified for the MS cmap)
        public uint CodePageRange1 
        { 
            get 
            {
                if (Version < 1) return 0;
                return WriteFont.ReadUInt(_data, 78); 
            } 
        }
        public uint CodePageRange2
        {
            get
            {
                if (Version < 1) return 0;
                return WriteFont.ReadUInt(_data, 82);
            }
        }

        //Format 2 properties:

        /// <summary>
        /// This is the height of the small 'x' character, and is
        /// intended for use for font substitution (i.e. the xHeight
        /// value of another font can be scaled approximatly to this
        /// height)
        /// </summary>
        public short XHeight  
        { 
            get 
            {
                if (Version < 2) return 0;
                return WriteFont.ReadShort(_data, 86); 
            } 
        }

        /// <summary>
        /// Height from baseline to top of uppercase letters
        /// </summary>
        public short CapHeight 
        {
            get
            {
                if (Version < 2) return 0;
                return WriteFont.ReadShort(_data, 88);
            }
        }

        /// <summary>
        /// Glyf to display when a glyf isn't found
        /// </summary>
        public ushort DefaultChar 
        {
            get
            {
                if (Version < 2) return 0;
                return WriteFont.ReadUShort(_data, 90);
            }
            set
            {
                if (Version < 2) throw new NotSupportedException();
                WriteFont.Write(value, _data, 90);
            }
        }

        /// <summary>
        /// This is the unicode encoding of the font's break
        /// character (cmap format 4)
        /// </summary>
        /// <remarks>
        /// This value should either be updated when writing fonts
        /// or one should insure that the break character comes along
        /// </remarks>
        public ushort BreakChar
        {
            get
            {
                if (Version < 2) return 0;
                return WriteFont.ReadUShort(_data, 92);
            }
            set
            {
                if (Version < 2) throw new NotSupportedException();
                WriteFont.Write(value, _data, 92);
            }
        }

        /// <summary>
        /// This is just a hint for layout engines.
        /// </summary>
        public ushort MaxContext
        {
            get
            {
                if (Version < 2) return 0;
                return WriteFont.ReadUShort(_data, 94);
            }
        }

        public OS2Build(byte[] data)
            : base(Tag.OS2)
        {
            _data = data;

            if (Version == 0)
            {
                if (_data.Length != 78)
                    throw new TTException("Wrong OS/2 table length");
            }
            else if (Version == 1)
            {
                if (_data.Length != 86)
                    throw new TTException("Wrong OS/2 table length");
            }
            else
            {
                if (_data.Length < 96)
                    throw new TTException("Wrong OS/2 table length");

                //DefaultChar is a GID valid for the full font, but
                //since we rewrite the gids we set this zero, i.e.
                //the ndef character. PDF viewers will in any case not 
                //use this value.
                DefaultChar = 0;

                //The difference between format 2 and 3 lays in how
                //the "Type" field is interpreted. In format 3 bits
                //0-3 are exlusive, so there must never have more 
                //than 1 bit set in that range.
                //
                //I don't know what Format 4 brings to the table. In
                //any case, we allow for bigger tables so that we
                //don't break on later versions.
            }
        }

        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            if (write_cycle != 1)
                return false;
            s.Write(_data);
            return true;
        }

        [Flags()]
        public enum Restriction : ushort
        {
            /// <summary>
            /// Fonts with this setting indicate that they may be 
            /// embedded and permanently installed on the remote 
            /// system
            /// </summary>
            Installable_Embedding = 0,

            /// <summary>
            /// Restricted License embedding: 
            /// Fonts that have only this bit set must not be 
            /// modified, embedded or exchanged in any manner 
            /// </summary>
            Restricted_License = 2,

            /// <summary>
            /// When this bit is set, the font may be embedded, 
            /// and temporarily loaded on the remote system.
            /// 
            /// No edits can be applied to the document
            /// </summary>
            Print = 4,

            /// <summary>
            /// Documents containing Editable fonts may be opened 
            /// for reading, editing is permitted, and changes 
            /// may be saved.
            /// </summary>
            Editable = 8,

            /// <summary>
            /// The font may not be subsetted prior to embedding
            /// </summary>
            No_subsetting = 256,

            /// <summary>
            /// Only bitmaps contained in the font may be embedded
            /// </summary>
            Bitmap_only = 512
        }
    }

    class PostBuild : BuildTable
    {
        readonly byte[] _data;

        public double Format
        {
            get { return WriteFont.ReadFixed(_data, 0); }
            set { WriteFont.WriteFixed(value, _data, 0); }
        }

        public double ItalicAngle
        {
            get { return WriteFont.ReadFixed(_data, 4); }
        }

        public short UnderlinePosition { get { return WriteFont.ReadShort(_data, 8); } }
        public short UnderlineThickness { get { return WriteFont.ReadShort(_data, 10); } }
        public bool IsFixedPitch { get { return WriteFont.ReadUInt(_data, 12) != 0; } }
        public uint MinMemType42 { get { return WriteFont.ReadUInt(_data, 16); } }
        public uint MaxMemType42 { get { return WriteFont.ReadUInt(_data, 20); } }
        public uint MinMemType1 { get { return WriteFont.ReadUInt(_data, 24); } }
        public uint MaxMemType1 { get { return WriteFont.ReadUInt(_data, 28); } }

        public PostBuild(byte[] data)
            : base(Tag.post)
        {
            if (data.Length < 32)
                throw new TTException("Invalid post table");
            if (data.Length > 32)
                Array.Resize<byte>(ref data, 32);
            _data = data;

            //By setting format 3 we can ignore the subtables
            Format = 3;
        }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            if (write_cycle != 3)
                return false;
            s.Write(_data);
            return true;
        }
    }

    class MaxpBuild : BuildTable
    {
        readonly byte[] _data;

        /// <summary>
        /// Should be 1.0
        /// </summary>
        public double Version
        {
            get { return WriteFont.ReadFixed(_data, 0); }
        }

        public ushort NumGlyphs
        {
            get { return WriteFont.ReadUShort(_data, 4); }
            set { WriteFont.Write(value, _data, 4); }
        }
        public ushort MaxPoints { get { return WriteFont.ReadUShort(_data, 6); } }
        public ushort MaxContours { get { return WriteFont.ReadUShort(_data, 8); } }
        public ushort MaxComponentPoints { get { return WriteFont.ReadUShort(_data, 10); } }
        public ushort MaxComponentContours { get { return WriteFont.ReadUShort(_data, 12); } }
        public ushort MaxZones { get { return WriteFont.ReadUShort(_data, 14); } }
        public ushort MaxTwilightPoints { get { return WriteFont.ReadUShort(_data, 16); } }
        public ushort MaxStorage { get { return WriteFont.ReadUShort(_data, 18); } }
        public ushort MaxFunctionDefs { get { return WriteFont.ReadUShort(_data, 20); } }
        public ushort MaxInstructionDefs { get { return WriteFont.ReadUShort(_data, 22); } }
        public ushort MaxStackElements { get { return WriteFont.ReadUShort(_data, 24); } }
        public ushort MaxSizeOfInstructions { get { return WriteFont.ReadUShort(_data, 26); } }
        public ushort MaxComponentElements { get { return WriteFont.ReadUShort(_data, 28); } }
        public ushort MaxComponentDepth { get { return WriteFont.ReadUShort(_data, 30); } }

        public MaxpBuild(byte[] data, int num_glyphs)
            : base(Tag.maxp)
        {
            if (data.Length != 32)
                throw new TTException("Maxp table is the wrong length");

            _data = data;
#if !KEEP_GIDS
            NumGlyphs = (ushort)num_glyphs;
#endif
        }

        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            if (write_cycle == 1)
            {
                s.Write(_data);
                return true;
            }
            return false;
        }
    }

    class HheaBuild : BuildTable
    {
        readonly byte[] _data;

        /// <summary>
        /// Should be 1.0
        /// </summary>
        public double Version
        {
            get { return WriteFont.ReadFixed(_data, 0); }
        }

        public short Ascent { get { return WriteFont.ReadShort(_data, 4); } }
        public short Descent { get { return WriteFont.ReadShort(_data, 6); } }
        public short LineGap { get { return WriteFont.ReadShort(_data, 8); } }
        public ushort AdvanceWidthMax { get { return WriteFont.ReadUShort(_data, 10); } }
        public short MinLeftSideBearing { get { return WriteFont.ReadShort(_data, 12); } }
        public short MinRightSideBearing { get { return WriteFont.ReadShort(_data, 14); } }
        public short XMaxExtent { get { return WriteFont.ReadShort(_data, 16); } }
        public short CaretSlopeRise { get { return WriteFont.ReadShort(_data, 18); } }
        public short CaretSlopeRun { get { return WriteFont.ReadShort(_data, 20); } }
        public short CaretOffset { get { return WriteFont.ReadShort(_data, 22); } }
        public short MetricDataFormat { get { return WriteFont.ReadShort(_data, 32); } }
        public ushort NumOfLongHorMetrics
        {
            get { return WriteFont.ReadUShort(_data, 34); }
            set { WriteFont.Write(value, _data, 34); }
        }

        public HheaBuild(byte[] data, int numOfLongHorMetrics)
            : base(Tag.hhea)
        {
            if (data.Length != 36)
                throw new TTException("Header table is wrong length");

            _data = data;
            NumOfLongHorMetrics = (ushort)numOfLongHorMetrics;
        }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            if (write_cycle == 1)
            {
                s.Write(_data);
                return true;
            }
            return false;
        }
    }

    class VheaBuild : BuildTable
    {
        readonly byte[] _data;

        /// <summary>
        /// Should be 1.1
        /// </summary>
        public double Version
        {
            get { return WriteFont.ReadFixed(_data, 0); }
        }

        public short VertTypoAscender { get { return WriteFont.ReadShort(_data, 4); } }
        public short VertTypoDescender { get { return WriteFont.ReadShort(_data, 6); } }
        public short VertTypoLineGap { get { return WriteFont.ReadShort(_data, 8); } }
        public short AdvanceHeightMax { get { return WriteFont.ReadShort(_data, 10); } }
        public short MinTopSideBearing { get { return WriteFont.ReadShort(_data, 12); } }
        public short MinBottomSideBearing { get { return WriteFont.ReadShort(_data, 14); } }
        public short YMaxExtent { get { return WriteFont.ReadShort(_data, 16); } }
        public short CaretSlopeRise { get { return WriteFont.ReadShort(_data, 18); } }
        public short CaretSlopeRun { get { return WriteFont.ReadShort(_data, 20); } }
        public short CaretOffset { get { return WriteFont.ReadShort(_data, 22); } }
        public short MetricDataFormat { get { return WriteFont.ReadShort(_data, 32); } }
        public ushort NumOfLongVerMetrics
        {
            get { return WriteFont.ReadUShort(_data, 34); }
            set { WriteFont.Write(value, _data, 34); }
        }

        public VheaBuild(byte[] data, int numOfLongVerMetrics)
            : base(Tag.vhea)
        {
            if (data.Length != 36)
                throw new TTException("Header table is wrong length");

            _data = data;
            NumOfLongVerMetrics = (ushort)numOfLongVerMetrics;
        }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Contains global information about the font
    /// </summary>
    /// <remarks>
    /// This table does not build itself from scratch,
    /// but instead modifies existing data
    /// </remarks>
    class HeadBuild : BuildTable
    {
        readonly byte[] _data;

        /// <summary>
        /// Should be 1.0
        /// </summary>
        public double Version
        {
            get { return WriteFont.ReadFixed(_data, 0); }
        }

        /// <summary>
        /// Set by the font writer
        /// </summary>
        public double FontRevision
        {
            get { return WriteFont.ReadFixed(_data, 4); }
        }

        public uint CheckSumAdjustment
        {
            get { return WriteFont.ReadUInt(_data, 8); }
            set { WriteFont.Write(value, _data, 8); }
        }

        public bool MagicNumber
        {
            get { return WriteFont.ReadUInt(_data, 12) == 0x5F0F3CF5; }
        }

        public ushort Flags
        {
            get { return WriteFont.ReadUShort(_data, 16); }
        }

        public ushort UnitsPerEm
        {
            get { return WriteFont.ReadUShort(_data, 18); }
        }

        /// <summary>
        /// When this font was created
        /// </summary>
        public DateTime Created
        {
            get
            {
                long date = WriteFont.ReadLong(_data, 20);
                var mac_date = new DateTime(1904, 1, 1);

                //FYI, this code will work up to at least the year 6000
                mac_date = mac_date.AddSeconds(0d + date);

                return mac_date;
            }
        }

        /// <summary>
        /// When this font was last modified
        /// </summary>
        public DateTime Modified
        {
            get
            {
                long date = WriteFont.ReadLong(_data, 28);
                var mac_date = new DateTime(1904, 1, 1);
                mac_date = mac_date.AddSeconds(0d + date);
                return mac_date;
            }
            set
            {
                var mac_date = new DateTime(1904, 1, 1);
                var date = (long)value.Subtract(mac_date).TotalSeconds;
                WriteFont.Write(date, _data, 28);
            }
        }

        public short XMin { get { return WriteFont.ReadShort(_data, 36); } }
        public short YMin { get { return WriteFont.ReadShort(_data, 38); } }
        public short XMax { get { return WriteFont.ReadShort(_data, 40); } }
        public short YMax { get { return WriteFont.ReadShort(_data, 42); } }
        public ushort MacStyle { get { return WriteFont.ReadUShort(_data, 44); } }
        public ushort LowestRecPPEM { get { return WriteFont.ReadUShort(_data, 46); } }
        public short FontDirectionHint { get { return WriteFont.ReadShort(_data, 48); } }

        public short IndexToLocFormat
        {
            get { return WriteFont.ReadShort(_data, 50); }
            set { WriteFont.Write(value, _data, 50); }
        }
        public short GlyphDataFormat { get { return WriteFont.ReadShort(_data, 52); } }

        public HeadBuild(byte[] data, int loc_format)
            : base(Tag.head)
        {
            if (data.Length != 54)
                throw new TTException("Header table is wrong length");

            _data = data;


            IndexToLocFormat = (short) loc_format;

            //The checksum of the font is calculated with this set zero
            CheckSumAdjustment = 0;
        }
        public override bool WriteTable(WriteFont s, int write_cycle)
        {
            //Being a small table, we put this at at the head of the font
            if (write_cycle == 1)
            {
                Modified = DateTime.Now;
                s.Write(_data);
                return true;
            }
            return false;
        }
    }
    
}
