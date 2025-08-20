using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfAppearanceDictionary : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.AppearanceDictionary; } }

        /// <summary>
        /// Normal appearance
        /// </summary>
        public PdfForm N_Form 
        { 
            get 
            {
                var raw = _elems["N"];
                if (raw is PdfReference) raw = raw.Deref();
                if (raw is PdfForm) return (PdfForm) raw;
                if (raw is PdfDictionary || raw is PdfASDict) return null;
                return (PdfForm) _elems.GetPdfTypeEx("N", PdfType.XObject, IntMsg.AssumeForm, PdfNull.Value); 
            }
            set
            {
                _elems.SetItem("N", value, true);
            }
        }

        /// <summary>
        /// Normal appearance
        /// </summary>
        public PdfASDict N_Dict
        {
            get
            {
                var raw = _elems["N"];
                if (raw is PdfReference) raw = raw.Deref();
                if (raw is PdfASDict) return (PdfASDict)raw;
                if (raw is PdfForm || raw is PdfStream) return null;
                return (PdfASDict)_elems.GetPdfTypeEx("N", PdfType.ASDict);
            }
            set
            {
                _elems.SetItem("N", value, true);
            }
        }

        /// <summary>
        /// Rollover appearance
        /// </summary>
        public PdfForm R_Form
        {
            get
            {
                var raw = _elems["R"];
                if (raw is PdfReference) raw = raw.Deref();
                if (raw is PdfForm) return (PdfForm)raw;
                if (raw is PdfDictionary || raw is PdfASDict) return null;
                return (PdfForm)_elems.GetPdfType("R", PdfType.XObject, IntMsg.NoMessage, PdfNull.Value);
            }
            set
            {
                _elems.SetItem("R", value, true);
            }
        }

        /// <summary>
        /// Rollover appearance
        /// </summary>
        public PdfASDict R_Dict
        {
            get
            {
                var raw = _elems["R"];
                if (raw is PdfReference) raw = raw.Deref();
                if (raw is PdfASDict) return (PdfASDict)raw;
                if (raw is PdfForm || raw is PdfStream) return null;
                return (PdfASDict)_elems.GetPdfType("R", PdfType.ASDict);
            }
            set
            {
                _elems.SetItem("R", value, true);
            }
        }

        /// <summary>
        /// Down appearance
        /// </summary>
        public PdfForm D_Form
        {
            get
            {
                var raw = _elems["D"];
                if (raw is PdfReference) raw = raw.Deref();
                if (raw is PdfForm) return (PdfForm)raw;
                if (raw is PdfDictionary || raw is PdfASDict) return null;
                return (PdfForm)_elems.GetPdfType("D", PdfType.XObject, IntMsg.NoMessage, PdfNull.Value);
            }
            set
            {
                _elems.SetItem("D", value, true);
            }
        }

        /// <summary>
        /// Down appearance
        /// </summary>
        public PdfASDict D_Dict
        {
            get
            {
                var raw = _elems["D"];
                if (raw is PdfReference) raw = raw.Deref();
                if (raw is PdfASDict) return (PdfASDict)raw;
                if (raw is PdfForm || raw is PdfStream) return null;
                return (PdfASDict)_elems.GetPdfType("D", PdfType.ASDict);
            }
            set
            {
                _elems.SetItem("D", value, true);
            }
        }

        #endregion

        #region Init

        public PdfAppearanceDictionary()
            : this(new TemporaryDictionary())
        {}

        internal PdfAppearanceDictionary(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfAppearanceDictionary(elems);
        }

        #endregion
    }
}
