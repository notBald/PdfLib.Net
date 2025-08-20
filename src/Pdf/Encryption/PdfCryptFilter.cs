using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Encryption
{
    public sealed class PdfCryptFilter : Elements
    {
        #region Properties

        /// <summary>
        /// PdfType.CryptFilter
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.CryptFilter; }
        }

        /// <summary>
        /// The method used for encryption
        /// </summary>
        public PdfCryptFilterMode CFM 
        { 
            get 
            {
                var mode = _elems.GetName("CFM");
                if (mode == null) return PdfCryptFilterMode.None;
                
                switch (mode)
                {
                    case "None": return PdfCryptFilterMode.None;
                    case "V2": return PdfCryptFilterMode.V2;
                    case "AESV2": return PdfCryptFilterMode.AESV2;
                    case "AESV3": return PdfCryptFilterMode.AESV3;
                    default: return PdfCryptFilterMode.Unsupported;
                }
            } 
        }

        /// <summary>
        /// The event to be used to trigger the authorization that is required to access encryption keys used by this filter
        /// </summary>
        public string AuthEvent {  get { return _elems.GetName("AuthEvent", "DocOpen"); } }

        /// <summary>
        /// The bit length of the encryption key. It shall be a multiple of 8 in the range of 40 to 128.
        /// </summary>
        public int? Length
        {
            get
            {
                var l = _elems.GetIntObj("Length");
                if (l == null) return null;
                return l.GetInteger();
            }
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        internal PdfCryptFilter(PdfDictionary dict)
            : base(dict)
        { dict.CheckType("CryptFilter"); }

        #endregion

        #region Boilerplate
        /// <summary>
        /// Used when moving the element to another document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfCryptFilter(elems);
        }
        #endregion
    }

    public sealed class PdfCryptFilters : TypeDict<PdfCryptFilter>
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.CryptFilters; } }

        #endregion

        #region Init

        internal PdfCryptFilters(PdfDictionary dict)
            : base(dict, PdfType.CryptFilter, null) { }

        #endregion

        #region Required overrides

        protected override void DictChanged()
        {  }

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override TypeDict<PdfCryptFilter> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg)
        {
            return new PdfCryptFilters(elems);
        }

        #endregion
    }
}
