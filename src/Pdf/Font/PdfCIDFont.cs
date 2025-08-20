using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Font
{
    /// <remarks>
    /// CID fonts inherit from Elements, and not PdfFont, as
    /// they can't be used in the same way PdfFonts are. I.e.
    /// one can't put a CID font into a font resource dictionary.
    /// </remarks>
    public abstract class PdfCIDFont : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.CIDFont; } }

        /// <summary>
        /// What type of cid font this is
        /// </summary>
        public string SubType { get { return _elems.GetNameEx("Subtype"); } }

        /// <summary>
        /// Postscript name of the font.
        /// </summary>
        public string BaseFont { get { return _elems.GetNameEx("BaseFont"); } }

        public PdfFontDescriptor FontDescriptor
        { get { return (PdfFontDescriptor)_elems.GetPdfTypeEx("FontDescriptor", PdfType.FontDescriptor); } }

        /// <summary>
        /// Default Width
        /// </summary>
        public int DW { get { return _elems.GetUInt("DW", 1000); } }

        /// <summary>
        /// The widths of the induvidual CIDs. 
        /// </summary>
        /// <remarks>
        /// The formating of this array is a little strange, see: rCIDfont.SetWidths(..)
        /// </remarks>
        internal PdfHMTX W { get { return new PdfHMTX(DW, _elems.GetArray("W")); } }

        /// <summary>
        /// Default vector for vertical fonts.
        /// </summary>
        internal xIntPoint DW2 
        { 
            get 
            {
                var ia = _elems.GetArray("W2");
                if (ia == null) return new xIntPoint(880, -1000);
                return new xIntPoint(ia); 
            } 
        }

        /// <summary>
        /// The verticals widths of the induvidual CIDs. 
        /// </summary>
        /// <remarks>
        /// The formating of this array is a little strange. I therefore convert it
        /// into something a bit easier to handle. 
        /// 
        /// The array is organized into groups of "two" or five
        /// The two group consists of a number and an array.
        /// The five group consists of five consecative numbers
        /// </remarks>
        public PdfVMTX W2 
        { 
            get 
            {
                //A bit inefficient in that it recreates the W object
                return new PdfVMTX(DW2, _elems.GetArray("W2"), W);
            } 
        }

        /// <summary>
        /// Defines the character collection of the CID font
        /// </summary>
        public PdfCIDSystemInfo CIDSystemInfo { get { return (PdfCIDSystemInfo)_elems.GetPdfTypeEx("CIDSystemInfo", PdfType.CIDSystemInfo); } }

        /// <summary>
        /// If this is an embeded or built in font
        /// </summary>
        public bool BuiltInFont
        {
            get
            {
                //Temp code. Haven't got font desc yet.
                var fd = FontDescriptor;
                if (fd == null) return true;
                return fd.BuiltInFont;
            }
        }

        #endregion

        #region Init

        protected PdfCIDFont(PdfDictionary dict) : base(dict) 
        {
            dict.CheckTypeEx("Font");
        }

        #endregion

        internal sealed override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null) return false;
            if (obj.GetType() == GetType())
            {
                var cid = (PdfCIDFont) obj;
                if (BaseFont == cid.BaseFont &&
                    DW == cid.DW && DW2 == cid.DW2 &&
                    PdfFile.Equivalent(_elems.GetArray("W"), cid._elems.GetArray("W")) &&
                    PdfFile.Equivalent(CIDSystemInfo, cid.CIDSystemInfo) &&
                    PdfFile.Equivalent(_elems["CIDToGIDMap"], cid._elems["CIDToGIDMap"]))
                    return true;

            }
            return false;
        }

        internal static PdfCIDFont Create(PdfDictionary dict)
        {
            var subtype = dict.GetName("Subtype");

            if ("CIDFontType0".Equals(subtype))
            {
                //These fonts can have CFF data packed into a truetype
                //container. We therefore need to look at the font description
                var fd = (PdfFontDescriptor)dict.GetPdfType("FontDescriptor", PdfType.FontDescriptor);
                if (fd != null && fd.FontFile2 != null)
                    return new CIDFontType2(dict);
                return new CIDFontType0(dict);
            }
            else if ("CIDFontType2".Equals(subtype))
                return new CIDFontType2(dict);

            throw new PdfReadException(PdfType.CIDFont, ErrCode.OutOfRange);
        }
    }

    /// <summary>
    /// This are basically CFF fonts, when embeded, and true type
    /// fonts when "built in".
    /// </summary>
    /// <remarks>
    /// I'm not yet sure on how PdfCIDSystemInfo should be used.
    /// Currently only built in CIDFontType0 makes active use of
    /// them.
    /// </remarks>
    public sealed class CIDFontType0 : PdfCIDFont
    {
        #region Init

        internal CIDFontType0(PdfDictionary dict) : base(dict) { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new CIDFontType0(elems);
        }

        #endregion
    }

    /// <summary>
    /// CIDFontType2 are in esence true type fonts. True type fonts have no native
    /// notion of CID (Character ID), so they use a CIDToGIDMap for mapping from
    /// CID to Glyph Index.
    /// </summary>
    public sealed class CIDFontType2 : PdfCIDFont
    {
        #region Variables and properties

        /// <summary>
        /// Note, returns null for identity
        /// </summary>
        public ushort[] CIDToGIDMap
        {
            get 
            { 
                //Can be both a name and a stream. However, only the name "identity" is allowed.
                var itm = _elems["CIDToGIDMap"];
                if (itm == null) return null;
                itm = itm.Deref();
                if (itm is PdfName && "Identity".Equals(itm.GetString()))
                    return null;
                if (itm is PdfStream)
                {
                    var bytes = ((PdfStream)itm).DecodedStream;
                    var shorts = new ushort[bytes.Length / 2];
                    for (int c = 0, k = 0; k < shorts.Length; k++)
                        shorts[k] = (ushort) ((bytes[c++] << 8) | bytes[c++]);
                    return shorts;
                }
                throw new PdfReadException(ErrSource.Font, PdfType.Stream, ErrCode.WrongType);
            }
        }

        #endregion

        #region Init

        internal CIDFontType2(PdfDictionary dict) : base(dict) { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new CIDFontType2(elems);
        }

        #endregion
    }

    /// <summary>
    /// Defines the character collection of the CID font
    /// </summary>
    [DebuggerDisplay("{CMapName}")]
    public class PdfCIDSystemInfo : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.CIDSystemInfo; } }

        /// <summary>
        /// The issuer of the character collection
        /// </summary>
        public string Registry { get { return _elems.GetStringEx("Registry"); } }

        /// <summary>
        /// A string that uniquely names the character collection within the 
        /// specified registry
        /// </summary>
        public string Ordering { get { return _elems.GetStringEx("Ordering"); } }

        /// <summary>
        ///  The supplement number of the character collection
        /// </summary>
        public int Supplement { get { return _elems.GetUIntEx("Supplement"); } }

        /// <summary>
        /// Returns the full name of this character collection
        /// </summary>
        internal string Name { get { return Registry + "-" + Ordering; } }

        /// <summary>
        /// True if this collection is Chinese/Japanese/Korean
        /// </summary>
        internal bool IsCJK
        {
            get
            {
                switch (Name)
                {
                    case "Adobe-CNS1":
                    case "Adobe-GB1":
                    case "Adobe-Japan1":
                    case "Adobe-Japan2":
                    case "Adobe-Korea1":
                        return true;
                }

                return false;
            }
        }

        private string CMapName { get { return Registry + "-" + Ordering + "-" + Supplement; } }
        private string CMapUnicodeName { get { return Registry + "-" + Ordering + "-UCS2"; } }

        /// <summary>
        /// Fetches the basic CMap
        /// </summary>
        /// <remarks>
        /// Note that this object is technically not in the PdfTree. It can be
        /// viewed as a "different" representation of PdfCIDSystemInfo.
        /// 
        /// However it is at the same time a propper "CMap" file. So it can be
        /// used to (say) set the cmap property of another font.
        /// </remarks>
        public PdfCmap CMap { get { return PdfCmap.Create(CMapName); } }

        /// <summary>
        /// Translates from CID to Unicode values
        /// </summary>
        /// <remarks>This is technically not a propper CMap</remarks>
        public PdfCmap UnicodeCMap { get { return PdfCmap.Create(CMapUnicodeName); } }

        #endregion

        #region Init

        public PdfCIDSystemInfo(string registry, string ordering, uint supplement)
            : base(new TemporaryDictionary())
        {
            if (registry == null || ordering == null)
                throw new ArgumentNullException();
            _elems.SetASCIIString("Registry", registry);
            _elems.SetASCIIString("Ordering", ordering);
            _elems.SetInt("Supplement", (int)supplement);
        }

        internal PdfCIDSystemInfo(PdfDictionary dict)
            : base(dict) 
        { }

        #endregion

        #region Required overrides

        internal override bool Equivalent(object obj)
        {
            if (obj is PdfCIDSystemInfo)
                return _elems.Equivalent(((PdfCIDSystemInfo)obj)._elems);
            return false;
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfCIDSystemInfo(elems);
        }

        #endregion
    }

    /// <summary>
    /// Contains vertical metrixs for a range of CID characters
    /// </summary>
    public class PdfVMTX
    {
        readonly xPoint _dw2;
        readonly PdfHMTX _hmtx;
        readonly iVMTX[] _vmtx;

        public VMTX this[int cid]
        {
            get
            {
                if (_vmtx != null)
                {
                    //doing a naive search. It would be better to sort
                    //the array and do a quicker search
                    for (int c = 0; c < _vmtx.Length; c++)
                    {
                        var v = _vmtx[c];
                        if (v.Low <= cid && cid <= v.High)
                            return new VMTX(v.Vx / 1000d, v.Vy / 1000d, v.W1 / 1000d);
                    }
                }

                return new VMTX(_hmtx[cid] / 2, _dw2.X, _dw2.Y);
            }
        }

        public PdfVMTX(xIntPoint dw2, PdfArray W2, PdfHMTX hmtx)
        {
            _dw2 = new xPoint(dw2.X / 1000d, dw2.Y / 1000d);
            _hmtx = hmtx;

            if (W2 != null)
                _vmtx = vmtx(W2);
        }

        public struct VMTX
        {
            public VMTX(double vx, double vy, double w1y)
            { Pos = new xPoint(vx, vy); W1y = w1y; }

            /// <summary>
            /// Position vector
            /// </summary>
            public readonly xPoint Pos;

            /// <summary>
            /// Y component of the displacement vector
            /// </summary>
            public readonly double W1y;
        }

        static iVMTX[] vmtx(PdfArray ar)
        {
            if (ar == null || ar.Length == 0) return null;
            Debug.Assert(false, "Untested code");
            if (ar.Length == 1)
                throw new PdfReadException(PdfType.Array, ErrCode.Invalid);
            var list = new List<iVMTX>();

            for (int c = 0; c < ar.Length; c++)
            {
                //First item is always an int
                int count_from = ar[c++].GetInteger();

                //Second item is an array or number
                if (c == ar.Length)
                    throw new PdfReadException(PdfType.Array, ErrCode.Invalid);
                PdfItem peek = ar[1].Deref();

                if (peek is PdfArray)
                {
                    //count_from is the starting CID

                    var car = (PdfArray)peek;
                    int start = 0;
                    do
                    {
                        if (start + 2 >= car.Length)
                            throw new PdfReadException(PdfType.Array, ErrCode.Invalid);

                        int w1y = car[start++].GetInteger();
                        int vx = car[start++].GetInteger();
                        int vy = car[start++].GetInteger();

                        list.Add(new iVMTX(count_from, count_from, vx, vy, w1y));

                        count_from++;
                    } while (start < car.Length);
                }
                else
                {
                    if (c + 3 >= ar.Length)
                        throw new PdfReadException(PdfType.Array, ErrCode.Invalid);

                    int count_to = ar[c++].GetInteger();
                    int w1y = ar[c++].GetInteger();
                    int vx = ar[c++].GetInteger();
                    int vy = ar[c++].GetInteger();

                    list.Add(new iVMTX(count_from, count_to, vx, vy, w1y));
                }
            }

            return list.ToArray();
        }


        struct iVMTX
        {
            public iVMTX(int high, int low, int vx, int vy, int w1)
            { High = (ushort) high; Low = (ushort) low; Vx = (short) vx; Vy = (short) vy; W1 = (short) w1; }

            /// <summary>
            /// CID range
            /// </summary>
            public readonly ushort High, Low;

            /// <summary>
            /// Position vector
            /// </summary>
            public readonly short Vx, Vy;

            /// <summary>
            /// Vertical displacement vector (w1 = (0, W1))
            /// </summary>
            public readonly short W1;
        }
    }

    /// <summary>
    /// Contains horizontal metrixs for a range of CID characters
    /// </summary>
    public class PdfHMTX
    {
        //May be inefficent for large fonts, as each character is
        //mapped into this dict
        private readonly Dictionary<int, double> _w;
        readonly double _dw;

        public double this[int cid]
        {
            get
            {
                double width;
                if (!_w.TryGetValue(cid, out width))
                    width = _dw;
                return width;
            }
        }

        public PdfHMTX(int default_width, PdfArray ar)
        {
            _dw = default_width / 1000d;
            _w = SetWidths(ar);
        }

        /// <summary>
        /// Creates a width dictionary. 
        /// </summary>
        /// <remarks>
        /// 9.7.4.3 Glyph Metrics in CIDFonts
        /// 
        /// c [ w1 w2 … wn ] 
        /// c first clast w
        /// 
        /// In the first format, c shall be an integer specifying a starting CID value; 
        /// it shall be followed by an array of n numbers that shall specify the widths 
        /// for n consecutive CIDs, starting with c. The second format shall define the 
        /// same width, w, for all CIDs in the range cfirst to clast .
        /// </remarks>
        static Dictionary<int, double> SetWidths(PdfArray w)
        {
            Dictionary<int, double> widths = new Dictionary<int, double>();
            if (w == null)
                return widths;

            for (int c = 0; c < w.Length; )
            {
                //First element is always an integer
                var first = w[c++].GetInteger();

                //Note: Not checking for "IndexOutOfRange"

                PdfItem second = w[c++].Deref();
                if (second is PdfArray)
                {
                    //First format.
                    var pa = (PdfArray)second;
                    for (int k = 0; k < pa.Length; k++)
                    {
                        Debug.Assert(!widths.ContainsKey(first + k), "This width array is corrupt");
                        widths[first + k] = pa[k].GetReal() / 1000d;
                    }
                }
                else
                {
                    //Second format
                    var si = (second as PdfInt).Value;
                    var wi = w[c++].GetReal() / 1000d;
                    for (; first <= si; first++)
                    {
                        Debug.Assert(!widths.ContainsKey(first), "This width array is corrupt");
                        widths[first] = wi;
                    }
                }
            }

            return widths;
        }
    }
}
