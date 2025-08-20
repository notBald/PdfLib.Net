using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Font
{
    public sealed class PSCMap : PSDictionary
    {
        #region Variables and properties

        /// <summary>
        /// How the CMap is originized
        /// </summary>
        /// <remarks>0 = normal, 2 = unicode</remarks>
        public int CMapType { get { return GetUIntEx("CMapType"); } }

        public PSCIDSystemInfo CIDSystemInfo { get { return GetPSDictEx<PSCIDSystemInfo>("CIDSystemInfo", PSCIDSystemInfo.Create); } }

        /// <summary>
        /// Name of the CMap
        /// </summary>
        public string CMapName { get { return GetNameEx("CMapName"); } }

        /// <summary>
        /// Version number of this CMap.
        /// </summary>
        public double? CMapVersion { get { return GetNumber("CMapVersion"); } }

        /// <summary>
        /// The code map.
        /// </summary>
        /// <remarks>Implementation spesific</remarks>
        public PSCodeMap CodeMap { get { return GetPSObjEx<PSCodeMap, PSCodeMap>("CodeMap", PSCodeMap.Create); } }

        /// <summary>
        /// The write direction
        /// </summary>
        public WMode WMode { get { return (WMode) GetUInt("WMode", 0); } }

        #endregion

        #region Init

        internal PSCMap(PSDictionary org)
            : base(org)
        {

        }

        /// <summary>
        /// Creates a identity cmap
        /// </summary>
        internal PSCMap()
            : base(4)
        {
            Catalog["CMapType"] = new PSInt(0);
            Catalog["PSCIDSystemInfo"] = new PSCIDSystemInfo("Adobe", "Identity", 0u);
            Catalog["CMapName"] = new PSString("Identity".ToCharArray());

            var start = new byte[] { 0, 0 };
            var end = new byte[] { 255, 255 };
            PSCodeMap.Add(this, new CodeRange[] { new CodeRange(start,  end) });
            PSCodeMap.Add(this, new CharRange[] { new CharRange(start, end, start, null) });
            PSCodeMap.Add(this, new CidRange[] { new CidRange(start, end, 0) });
        }

        internal void Init()
        {
            //Sorts the CMap data
            CodeMap.Init();

            //ToUnicode cmaps need not have a CMapName defined, but sometimes it still adds the "CMapName currentdict /CMap defineresource pop"
            //boilerplate. Not sure how to best handle this, but for now we set a "Unknown" name to unicode cmaps. For a relevant testfile see
            //"CID font with default width.pdf"
            if (!Catalog.ContainsKey("CMapName") && CMapType == 2)
                Catalog.Add("CMapName", new PSName("Unknown"));
        }

        #endregion

        #region Boilerplate overrides

        public override PSItem ShallowClone()
        {
            return new PSCMap(MakeShallowCopy());
        }

        #endregion
    }

    public enum WMode
    {
        Horizontal,
        Vertical
    }
}
