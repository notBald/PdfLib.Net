using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Render.Font.TT
{
    /// <summary>
    /// The table contains offsets and checksums for all other
    /// tables in the font.
    /// </summary>
#if DEBUG
    public
#else
    internal 
#endif
    class TableDirectory : IDisposable
    {
        #region Variables

        StreamReaderEx _f;

        readonly double _version;

        /// <summary>
        /// All tabledescriptors are put in this dictionary
        /// </summary>
        private readonly Dictionary<Tag, TableDescriptor> _tables;

        #endregion

        #region Properties

        /// <summary>
        /// The version number of this font file
        /// </summary>
        public double Version { get { return _version; } }

        /// <summary>
        /// Streamreader to the font file
        /// </summary>
        internal StreamReaderEx Reader { get { return _f; } }

        /// <summary>
        /// Character To Glyph Index Mapping Table
        /// </summary>
        internal CmapTable Cmap
        {
            get
            {
                var td = GetTable(Tag.cmap);

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new CmapTable(this, _f);
                _f.Position = org;

                return ret;
            }
        }

        internal GlyfTable Glyf
        {
            get
            {
                var td = GetTable(Tag.glyf);

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new GlyfTable(this, Loca, td.Offset + td.Length);
                _f.Position = org;

                return ret;
            }
        }

        /// <summary>
        /// Header table
        /// </summary>
        internal HeadTable Head
        {
            get
            {
                var td = GetTable(Tag.head);

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new HeadTable(this, _f);
                _f.Position = org;

                return ret;
            }
        }

        /// <summary>
        /// Horizontal header
        /// </summary>
        internal HheaTable Hhea
        {
            get
            {
                var td = GetTable(Tag.hhea);

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new HheaTable(this, _f);
                _f.Position = org;

                return ret;
            }
        }

        /// <summary>
        /// Vertical header
        /// </summary>
        internal VheaTable Vhea
        {
            get
            {
                var td = GetTable(Tag.vhea);

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new VheaTable(this, _f);
                _f.Position = org;

                return ret;
            }
        }        

        /// <summary>
        /// Horizontal Metrics
        /// </summary>
        internal HmtxTable Hmtx
        {
            get
            {
                var td = GetTable(Tag.hmtx);

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new HmtxTable(this, _f, Hhea.NumberOfHMetrics, Maxp.NumGlyphs, td.Length);
                _f.Position = org;

                return ret;
            }
        }

        /// <summary>
        /// Vertical Metrics
        /// </summary>
        internal VmtxTable Vmtx
        {
            get
            {
                var td = GetOptTable(Tag.vmtx);
                if (td == null) return null;

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new VmtxTable(this, _f, Vhea.NumOfLongVerMetrics, Maxp.NumGlyphs, td.Length);
                _f.Position = org;

                return ret;
            }
        }

        /// <summary>
        /// Glyph location offset
        /// </summary>
        private LocaTable Loca
        {
            get
            {
                var td = GetTable(Tag.loca);
                var glyf_td = GetTable(Tag.glyf);

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new LocaTable(this, _f, Maxp.NumGlyphs, Head.IndexToLocFormat, glyf_td.Offset);
                _f.Position = org;

                return ret;
            }
        }

        /// <summary>
        ///  Memory requirements for the font
        /// </summary>
        internal MaxpTable Maxp
        {
            get
            {
                var td = GetTable(Tag.maxp);
                if (td.Table != null) return (MaxpTable)td.Table;

                long org = _f.Position;
                _f.Position = td.Offset;
                var ret = new MaxpTable(this, _f);
                _f.Position = org;
                td.Table = ret;

                return ret;
            }
        }

        #endregion

        #region Init

        public TableDirectory(Stream f)
        {
            _f = new StreamReaderEx(f);
            
            //Reads out the table directory from the start of the stream
            _version = ReadFixed(_f);
            int n_tables = _f.ReadUShort();

            //These enteries are to facilitate quick binary search
            //of the following table. They are for now ignored.
            ushort searchRange = _f.ReadUShort();
            ushort entrySelector = _f.ReadUShort();
            ushort rangeShift = _f.ReadUShort();

            //Loads all table descriptors into memory
            _tables = new Dictionary<Tag, TableDescriptor>(n_tables);
            for (int c = 0; c < n_tables; c++)
            {
                var td = new TableDescriptor(_f);
                _tables.Add(td.Tag, td);
            }

        }

        public void Dispose()
        {
            _f.Dispose();
        }

        #endregion

        private TableDescriptor GetTable(Tag tag)
        {
            TableDescriptor td;
            if (_tables.TryGetValue(tag, out td))
                return td;
            throw new TableMissingException(tag.ToString());
        }

        private TableDescriptor GetOptTable(Tag tag)
        {
            TableDescriptor td;
            if (_tables.TryGetValue(tag, out td))
                return td;
            return null;
        }

        internal bool IsValid(Tag tag)
        {
            var td = GetTable(tag);
            var org = _f.Position;
            _f.Position = td.Offset;

            //Mr. Green.pdf gets "invalid" on the head table
            uint end = td.Length / 4;
            uint sum = 0;
            for (int c = 0; c < end; c++)
                sum += _f.ReadUInt();
            end = td.Length - end * 4;
            for (int c = 0; c < end; c++)
                sum += (uint) _f.ReadByte() << (24 - c * 8);

            _f.Position = org;

            return sum == td.CheckSum;
        }

        /// <summary>
        /// Reads a fixed format number
        /// </summary>
        public static double ReadFixed(StreamReaderEx s)
        {
            return s.ReadShort() + s.ReadUShort() / 16384;
        }

        [DebuggerDisplay("({Tag})")]
        private class TableDescriptor
        {
            public readonly Tag Tag;
            public readonly uint CheckSum;

            /// <summary>
            /// Offset from the start of the stream
            /// </summary>
            public readonly uint Offset;

            /// <summary>
            /// Length of the table data
            /// </summary>
            public readonly uint Length;

            public Table Table = null;

            public TableDescriptor(StreamReaderEx r)
            {
                Tag = (Tag)r.ReadUInt();

                //For making new tags:
                /*var bytes = BitConverter.GetBytes((uint) Tag);
                Array.Reverse(bytes);
                var str = System.Text.Encoding.ASCII.GetString(bytes);//*/

                CheckSum = r.ReadUInt();
                Offset = r.ReadUInt();
                Length = r.ReadUInt();
            }
        }
    }

    /// <summary>
    /// Some TrueType and Opentype tags. Not all will be implemented.
    /// </summary>
    enum Tag : uint
    {
        /// <summary>
        /// Digital signature
        /// </summary>
        DSIG = 1146308935,
        /// <summary>
        /// Embedded bitmap data
        /// </summary>
        EBDT = 1161970772,
        /// <summary>
        /// Embedded bitmap location data
        /// </summary>
        EBLC = 1161972803,
        /// <summary>
        /// Glyph definition data
        /// </summary>
        GDEF = 1195656518,
        /// <summary>
        /// Glyph positioning data
        /// </summary>
        GPOS = 1196445523,
        /// <summary>
        /// Glyph substitution data
        /// </summary>
        GSUB = 1196643650,
        /// <summary>
        /// OS/2 and Windows metrix
        /// </summary>
        OS2 = 1330851634,
        /// <summary>
        /// Character map
        /// </summary>
        cmap = 1668112752,
        /// <summary>
        /// Control value table
        /// </summary>
        cvt = 1668707360,
        /// <summary>
        /// Font program
        /// </summary>
        fpgm = 1718642541,
        /// <summary>
        /// Grid-fitting and scan conversion procedure
        /// </summary>
        gasp = 1734439792,
        /// <summary>
        /// Glyph
        /// </summary>
        glyf = 1735162214,
        /// <summary>
        /// Header
        /// </summary>
        head = 1751474532,
        /// <summary>
        /// Horizontal header
        /// </summary>
        hhea = 1751672161,
        /// <summary>
        /// Horizontal metrix
        /// </summary>
        hmtx = 1752003704,
        /// <summary>
        ///  locations of the glyphs
        /// </summary>
        loca = 1819239265,
        /// <summary>
        /// Maximum profile
        /// </summary>
        maxp = 1835104368,
        /// <summary>
        /// Names/strings table
        /// </summary>
        name = 1851878757,
        /// <summary>
        /// PostScript
        /// </summary>
        post = 1886352244,
        /// <summary>
        /// PreProgram
        /// </summary>
        prep = 1886545264,
        /// <summary>
        /// Vertical Header Table
        /// </summary>
        vhea = 1986553185,
        /// <summary>
        /// Vertical Metrics Table
        /// </summary>
        vmtx = 1986884728,
    }
}
