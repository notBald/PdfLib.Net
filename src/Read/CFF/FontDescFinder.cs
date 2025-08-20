using System;
using System.Collections.Generic;

namespace PdfLib.Read.CFF
{
    class FontDescFinder : IDisposable
    {
        //Charcode -> font mapping
        readonly byte[] _fs;

        //Font descriptors
        readonly Index _fds;

        //Parsed descriptors
        Dictionary<int, FontDescriptor> _desc = new Dictionary<int, FontDescriptor>(4);

        readonly Util.StreamReaderEx _s;

        public FontDescFinder(byte[] FASelect, Index FDArray, Util.StreamReaderEx s)
        {
            _fs = FASelect;
            _s = s;
            _fds = FDArray;
        }

        public void Dispose()
        {
            _s.Dispose();
        }

        public FontDescriptor ReadFD(int cid)
        {
            //Gets the font
            int font_nr = _fs[cid];

            FontDescriptor FD;
            if (_desc.TryGetValue(font_nr, out FD))
                return FD;

            //Parses the font descriptor
            var fd = new TopDICT(_s, true);
            fd.Parse(_fds.GetSizePos(font_nr, _s));

            //Parses the private dict
            var pd = new PrivateDICT(_s);
            pd.Parse(fd.Private);

            //Parses sub rutines
            if (pd.Subrs != null)
            {
                //todo: Find a font to test with.
                throw new NotImplementedException("Subrutines in CID font");
            }

            FD = new FontDescriptor(fd, pd);
            _desc.Add(font_nr, FD);

            return FD;
        }
    }
}
