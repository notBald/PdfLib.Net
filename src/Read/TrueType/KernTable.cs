using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Util;

namespace PdfLib.Read.TrueType
{
    internal class KernTable : Table
    {
        #region Variables and properties

        /// <summary>
        /// Should be zero
        /// </summary>
        public readonly ushort Version;

        /// <summary>
        /// If this table's checksum is correct
        /// </summary>
        public override bool Valid { get { return _td.IsValid(Tag.kern); } }

        /// <summary>
        /// Kern descriptors
        /// </summary>
        readonly KernDescriptor[] _kerns;

        #endregion

        #region Init

        public KernTable(TableDirectory td, StreamReaderEx r)
            : base(td)
        {
            Version = r.ReadUShort();
            int n_enc_tables = r.ReadUShort();

            //Reads in all the kern tables
            _kerns = new KernDescriptor[n_enc_tables];
            for (int c = 0; c < n_enc_tables; c++)
                _kerns[c] = new KernDescriptor(r);
        }

        #endregion

        /// <summary>
        /// Fetches a format 0 kern table
        /// </summary>
        /// <param name="Horizontal">
        /// If the table is to be vertical or horizontal
        /// </param>
        /// <param name="minimum">
        /// The mimimum table can be used to
        /// fit text into the smallest range possible. I.e the
        /// closets the glyphs can be placed together.
        /// </param>
        /// <param name="CrossStream">
        /// If tables with this bit set is to be returned
        /// </param>
        /// <param name="Override">
        /// If tables with this bit set is to be returned
        /// </param>
        /// <returns>Format0 kern table</returns>
        public KernFormat0 GetFormat0(bool Horizontal, bool Minimum, bool CrossStream, bool Override, double? units_per_em)
        {
            ushort pattern = 0;
            if (Horizontal)  pattern |= 1;
            if (Minimum)     pattern |= 2;
            if (CrossStream) pattern |= 4;
            if (Override)    pattern |= 8;

            for (int c = 0; c < _kerns.Length; c++)
            {
                var kern = _kerns[c];
                if (kern.Coverage == pattern)
                {
                    var r = _td.Reader;
                    var hold = r.Position;
                    var ret = new KernFormat0(r, kern, units_per_em, c + 1 == _kerns.Length);
                    r.Position = hold;
                    return ret;
                }
            }
            return null;
        }
    }

    [DebuggerDisplay("Format: {Format} - Hrz:{Horizontal} - Cross:{Crossstream} - Over:{Override}")]
    struct KernDescriptor
    {
        public readonly ushort Version;

        /// <summary>
        /// Length of the table data
        /// </summary>
        public readonly ushort Length;

        /// <summary>
        /// What type of information this kern
        /// table stores
        /// </summary>
        public readonly ushort Coverage;

        /// <summary>
        /// If this table has horizontal data (vertical if false)
        /// </summary>
        public bool Horizontal { get { return (Coverage & 0x01) != 0; } }

        /// <summary>
        /// If this table has minimum of kerning values
        /// </summary>
        public bool Minimum { get { return (Coverage & 0x02) != 0; } }

        /// <summary>
        /// If the kerning will be done in the up/down direction (assuming
        /// horizontal writing)
        /// 
        /// A value of 0x8000 in the kerning data resets this value
        /// </summary>
        public bool Crossstream { get { return (Coverage & 0x04) != 0; } }

        /// <summary>
        /// If set the value in this table should override the
        /// width value
        /// </summary>
        public bool Override { get { return (Coverage & 0x08) != 0; } }

        /// <summary>
        /// The format of this table
        /// </summary>
        public int Format { get { return Coverage >> 8; } }

        /// <summary>
        /// Offset from the beginning of the file
        /// to the kern table
        /// </summary>
        public int Offset;

        public KernDescriptor(StreamReaderEx r)
        {
            Version = r.ReadUShort();
            Length = (ushort) (r.ReadUShort() - 6);
            Coverage = r.ReadUShort();
            Offset = (int) r.Position;
            r.Position += Length;
        }
    }

    internal class KernFormat0
    {
        /// <summary>
        /// A dictionary containing either double or int values
        /// </summary>
        private readonly object _pairs;

        /// <summary>
        /// Whenver values in this table overrides widths
        /// </summary>
        public readonly bool Override;

        public Dictionary<uint, double> Pairs
        {
            get
            {
                if (_pairs is Dictionary<uint, double>)
                    return (Dictionary<uint, double>)_pairs;
                return null;
            }
        }

        public Dictionary<uint, int> IntPairs
        {
            get
            {
                if (_pairs is Dictionary<uint, int>)
                    return (Dictionary<uint, int>)_pairs;
                return null;
            }
        }

        public KernFormat0(StreamReaderEx r, KernDescriptor kern, double? units_per_em, bool is_last_table)
        {
            r.Position = kern.Offset;
            int nPairs = r.ReadUShort();
            r.Position += 6; //<-- skips over searchRange, entrySelector and rangeShift
            if (nPairs * 6 != kern.Length - 8)
            {
                //The Windows Calibri font has a kern table bigger than "allowed" length.
                //The basic problem is that the "length" field only allows tables up to
                //64k in length. Calibri needs more data than that, so it simply breaks
                //the specs.
                //
                // Other implementation seems to have this rule:
                // - If the kern table is the last kern table, ignore the length value
                if (!is_last_table)
                    throw new TTException("Wrong kern table length");
            }
            if (units_per_em != null)
            {
                var em = units_per_em.Value;
                var d = new Dictionary<uint, double>(nPairs);
                while (nPairs-- > 0)
                    d.Add(r.ReadUInt(), r.ReadShort() / em);
                _pairs = d;
            }
            else
            {
                var d = new Dictionary<uint, short>(nPairs);
                while (nPairs-- > 0)
                    d.Add(r.ReadUInt(), r.ReadShort());
                _pairs = d;
            }
            Override = kern.Override;
        }
    }
}
