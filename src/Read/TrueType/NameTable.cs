using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Read.TrueType
{
    internal class NameTable : Table, IEnumerable
    {
        #region Variables and properties

        public override bool Valid { get { return _td.IsValid(Tag.name); } }

        private readonly NameRecord[] _records;

        /// <summary>
        /// Copyright notice
        /// </summary>
        public string Copyright { get { return GetName(0); } }

        /// <summary>
        /// Font family name
        /// </summary>
        public string FontFamily { get { return GetName(1); } }

        /// <summary>
        /// Regular for normal fonts, italic, etc, for such fonts.
        /// </summary>
        public string Subfamily { get { return GetName(2); } }

        /// <summary>
        /// Usually a combination of fontfamily and name.
        /// </summary>
        public string FullName { get { return GetName(4); } }

        /// <summary>
        /// Postscript name of the font
        /// </summary>
        public string Postscript { get { return GetName(6); } }

        /// <summary>
        /// Trademark information
        /// </summary>
        public string Trademark { get { return GetName(7); } }

        #endregion

        #region Init

        public NameTable(TableDirectory td, StreamReaderEx s)
            : base(td)
        {
            ushort format = s.ReadUShort();
            if (format != 0)
                throw new TTException("Unknown name table format");

            //Number of records in this table
            ushort count = s.ReadUShort();

            ushort soffset = s.ReadUShort();
            int string_offset = (int)(s.Position + soffset - 6);

            _records = new NameRecord[count];
            for (int c = 0; c < _records.Length; c++)
                _records[c] = new NameRecord(s, string_offset);
            Debug.Assert(string_offset == s.Position);
        }

        #endregion

        string GetName(int id)
        {
            //Looks for MS name
            for (int c = 0; c < _records.Length; c++)
            {
                var r = _records[c];
                if (r.NameID == id && r.LanguageID == 1033 && r.PlatformID == PlatformID.Microsoft)
                    return r.ToString();
            }

            //Looks for the Apple name
            for (int c = 0; c < _records.Length; c++)
            {
                var r = _records[c];
                if (r.NameID == id && r.LanguageID == 0 && r.PlatformID == PlatformID.Macintosh)
                    return r.ToString();
            }

            return null;
        }

        #region IEnumerable

        public IEnumerator GetEnumerator()
        { return _records.GetEnumerator(); }

        #endregion
    }

    internal class NameRecord
    {
        public readonly PlatformID PlatformID;
        public readonly ushort PlatformSpecificID;
        public readonly ushort LanguageID;
        public readonly ushort NameID;
        public readonly byte[] Text;

        public NameRecord(StreamReaderEx s, int string_offset)
        {
            PlatformID = (PlatformID)s.ReadUShort();
            PlatformSpecificID = s.ReadUShort();
            LanguageID = s.ReadUShort();
            NameID = s.ReadUShort();
            var length = s.ReadUShort();
            var offset = s.ReadUShort();

            //Reads in the string
            var hold = s.Position;
            s.Position = string_offset + offset;
            Text = new byte[length];
            s.Read(Text, 0, length);
            s.Position = hold;
        }

        public override string ToString()
        {
            if (PlatformID == TrueType.PlatformID.AppleUnicode)
                return UnicodeEncoding.GetEncoding("UTF-16BE").GetString(Text, 0, Text.Length);
            if (PlatformID == TrueType.PlatformID.Microsoft)
                return UnicodeEncoding.GetEncoding("UTF-16BE").GetString(Text, 0, Text.Length);
            return ASCIIEncoding.ASCII.GetString(Text);
        }
    }
}
