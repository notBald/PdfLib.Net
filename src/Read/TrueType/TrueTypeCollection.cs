using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PdfLib.Util;

namespace PdfLib.Read.TrueType
{
    public class TrueTypeCollection : IDisposable, IEnumerable<TableDirectory>
    {
        const int TTC_TTCF = 0x74746366;
        const int TTC_DSIG = 0x44534947;

        public readonly float Version;
        private bool _dispose_stream;

        public int Length { get { return _fonts.Length; } }

        private int[] _fonts;
        Stream _s;

        public TableDirectory this[int i]
        {
            get
            {
                _s.Position = _fonts[i];
                return new TableDirectory(_s);
            }
        }

        public TableDirectory this[string post_script_name]
        {
            get
            {
                if (post_script_name != null)
                {
                    foreach (var td in this)
                    {
                        if (post_script_name.Equals(td.Name.Postscript))
                            return td;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Has Digital Signature
        /// </summary>
        public readonly bool HasDSIG;

        private int _dsig_length;

        private int _dsig_offset;

        public TrueTypeCollection(Stream s, bool dispose_stream)
            : this(new StreamReaderEx(s))
        { _s = s; _dispose_stream = dispose_stream; }
        private TrueTypeCollection(StreamReaderEx s)
        {
            if (s.ReadInt() != TTC_TTCF)
                throw new NotSupportedException("File not a True Type Collection");
            Version = (float) TableDirectory.ReadFixed(s);
            var number_of_fonts = s.ReadInt();
            _fonts = new int[number_of_fonts];
            for (int c = 0; c < _fonts.Length; c++) 
                _fonts[c] = s.ReadInt();

            if (Version > 1)
            {
                HasDSIG = s.ReadInt() == TTC_DSIG;
                _dsig_length = s.ReadInt();
                _dsig_offset = s.ReadInt();
            }
        }

        public void Dispose()
        {
            if (_dispose_stream)
                _s.Dispose();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<TableDirectory> GetEnumerator()
        {
            for (int c = 0; c < _fonts.Length; c++)
                yield return this[c];
        }

        public static bool IsTTC(Stream s)
        {
            if (s == null || !s.CanRead || !s.CanSeek)
                return false;
            try
            {
                var hold = s.Position;
                var size = s.Length - hold;
                try
                {
                    var ex = new Util.StreamReaderEx(s);
                    return ex.ReadInt() == TTC_TTCF;
                }
                finally { s.Position = hold; }
            }
            catch { return false; }            
        }
    }
}
