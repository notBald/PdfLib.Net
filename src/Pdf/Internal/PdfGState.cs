using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Function;
using System.Text;
using PdfLib.Pdf.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Transparency;

namespace PdfLib.Pdf.Internal
{
    public sealed class PdfGState : Elements
    {
        #region Properties

        internal override PdfType Type { get { return PdfType.GState; } }

        /// <summary>
        /// Line Width
        /// </summary>
        [PdfVersion("1.3")]
        public double? LW { get { return _elems.GetNumber("LW"); } }

        /// <summary>
        /// Line cap style
        /// </summary>
        [PdfVersion("1.3")]
        public xLineCap? LC
        {
            get
            {
                var ret = _elems.GetUIntObj("LC");
                if (ret == null) return null;
                return (xLineCap)ret.GetInteger();
            }
        }

        /// <summary>
        /// Line join style
        /// </summary>
        [PdfVersion("1.3")]
        public xLineJoin? LJ
        {
            get
            {
                var ret = _elems.GetUIntObj("LJ");
                if (ret == null) return null;
                return (xLineJoin)ret.GetInteger();
            }
        }

        /// <summary>
        /// Miter Limit
        /// </summary>
        [PdfVersion("1.3")]
        public double? ML { get { return _elems.GetNumber("ML"); } }

        /// <summary>
        /// The line dash pattern
        /// </summary>
        [PdfVersion("1.3")]
        public xDashStyle? D
        {
            get 
            {
                var ret = _elems.GetArray("D");
                if (ret == null) return null;
                if (ret.Length != 2) throw new PdfReadException(PdfType.Array, ErrCode.Invalid);
                var da = (RealArray)ret.GetPdfTypeEx(0, PdfType.RealArray);
                var ph = ret[1].GetInteger(); //<-- Specs say Integer here
                return new xDashStyle(ph, da.ToArray());
            }
        }

        /// <summary>
        /// Rendering Intent
        /// </summary>
        [PdfVersion("1.3")]
        public string RI { get { return _elems.GetName("RI"); } }

        /// <summary>
        /// Overprint for stroking
        /// </summary>
        public bool? OP { get { return _elems.GetBool("OP"); } }

        /// <summary>
        /// Overprint for other operations
        /// </summary>
        [PdfVersion("1.3")]
        public bool? op { get { return _elems.GetBool("op"); } }

        /// <summary>
        /// Overprint mode
        /// </summary>
        [PdfVersion("1.3")]
        public PdfItem OPM { get { return _elems["OPM"]; } }

        /// <summary>
        /// A font
        /// </summary>
        [PdfVersion("1.3")]
        public PdfFont Font { get { return (PdfFont) _elems.GetPdfType("Font", PdfType.Font); } }

        /// <summary>
        /// Black Generator for CMYK conversions
        /// </summary>
        public PdfItem BG { get { return _elems["BG"]; } }

        /// <summary>
        /// Black Generator for CMYK conversions
        /// </summary>
        [PdfVersion("1.3")]
        public PdfItem BG2 { get { return _elems["BG2"]; } }

        /// <summary>
        /// Undercolor removal for CMYK conversions
        /// </summary>
        public PdfItem UCR { get { return _elems["UCR"]; } }

        /// <summary>
        /// Undercolor removal for CMYK conversions
        /// </summary>
        [PdfVersion("1.3")]
        public PdfItem UCR2 { get { return _elems["UCR2"]; } }

        /// <summary>
        /// The transfer function. "Identity" is represented as an empty array
        /// </summary>
        public PdfFunctionArray TR 
        { 
            get 
            {
                var fa = _elems["TR"];
                if (fa == null) return null;
                var type = fa.Type;
                if (type == PdfType.Name)
                    return null;
                if (type == PdfType.FunctionArray)
                    return (PdfFunctionArray)fa.Deref();
                return (PdfFunctionArray)_elems.GetPdfType("TR", PdfType.FunctionArray, IntMsg.Special, null);
            }
            set { _elems.SetItem("TR", value, true); }
        }

        /// <summary>
        /// The transfer function
        /// </summary>
        [PdfVersion("1.3")]
        public PdfFunctionArray TR2 
        { 
            get 
            {
                var fa = _elems["TR2"];
                if (fa == null) return null;
                var type = fa.Type;
                if (type == PdfType.Name)
                    return null;
                if (type == PdfType.FunctionArray)
                    return (PdfFunctionArray)fa.Deref();
                return (PdfFunctionArray)_elems.GetPdfType("TR2", PdfType.FunctionArray, IntMsg.Special, null);
            }
            set 
            {
                _elems.SetItem("TR2", value, true); 
            }
        }

        /// <summary>
        /// If TR2 or TR (if no TR2) is the identity function
        /// </summary>
        public bool TR_Identity
        {
            get
            {
                var fa = _elems["TR2"];
                if (fa == null)
                    fa = _elems["TR"];
                if (fa == null || fa.Type != PdfType.Name) return false;
                return fa.GetString() == "Identity";
            }
            set
            {
                if (!value)
                {
                    _elems.Remove("TR");
                    _elems.Remove("TR2");
                }
                else
                    _elems.SetName("TR2", "Identity");
            }
        }

        /// <summary>
        /// If TR2 or TR (if no TR2) is the default function
        /// </summary>
        public bool TR_Default
        {
            get
            {
                var fa = _elems["TR2"];
                if (fa == null)
                    fa = _elems["TR"];
                if (fa == null || fa.Type != PdfType.Name) return false;
                return fa.GetString() == "Default";
            }
            set
            {
                if (!value)
                {
                    _elems.Remove("TR");
                    _elems.Remove("TR2");
                }
                else
                    _elems.SetName("TR2", "Default");
            }
        }

        /// <summary>
        /// The halftone dictionary
        /// </summary>
        public PdfItem HT { get { return _elems["HT"]; } }

        /// <summary>
        /// The flatness tolerance
        /// </summary>
        [PdfVersion("1.3")]
        public double? FL { get { return _elems.GetNumber("FL"); } }

        /// <summary>
        /// The smoothness tolerance
        /// </summary>
        [PdfVersion("1.3")]
        public double? SM { get { return _elems.GetNumber("SM"); } }

        /// <summary>
        /// Automatic Stroke Adjustment
        /// </summary>
        public bool? SA { get { return _elems.GetBool("SA"); } }

        /// <summary>
        /// Blend Mode
        /// </summary>
        [PdfVersion("1.4")]
        public PdfBlendModes BM 
        { 
            get { return PdfBlendMode.Convert(_elems["BM"]); }
            set
            {
                if (value == PdfBlendModes.Normal)
                    _elems.Remove("BM");
                else
                    _elems.SetName("BM", PdfBlendMode.Convert(value));
            }
        }

        /// <summary>
        /// Soft mask
        /// </summary>
        [PdfVersion("1.4")]
        public IMask SMask 
        { 
            get { return (IMask) _elems.GetPdfType("SMask", PdfType.SoftMask); }
            set { _elems.SetItem("SMask", (PdfItem) value, !(value is PdfNoMask)); }
        }

        /// <summary>
        /// Current alpha for stroking operations
        /// </summary>
        [PdfVersion("1.4")]
        public double? CA 
        { 
            get { return _elems.GetNumber("CA"); }
            set { _elems.SetReal("CA", value); }
        }


        /// <summary>
        /// Current alpha for non-stroking operations
        /// </summary>
        [PdfVersion("1.4")]
        public double? ca 
        { 
            get { return _elems.GetNumber("ca"); }
            set { _elems.SetReal("ca", value); }
        }

        /// <summary>
        /// The alpha source flag
        /// </summary>
        [PdfVersion("1.4")]
        public bool? AIS { get { return _elems.GetBool("AIS"); } }

        /// <summary>
        /// Text knockout flag
        /// </summary>
        [PdfVersion("1.4")]
        public bool? TK { get { return _elems.GetBool("TK"); } }

        #endregion

        #region Init

        internal PdfGState(PdfDictionary dict)
            : base(dict) { _elems.CheckType("ExtGState"); }

        public PdfGState()
            : base(new TemporaryDictionary())
        {
            _elems.SetType("ExtGState");
        }

        #endregion

        #region Required overrides

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is PdfGState)
            {
                return _elems.Equivalent(((PdfGState)obj)._elems);
            }
            return false;
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfGState(elems);
        }

        #endregion
    }

    /// <summary>
    /// A dictionary over GStates
    /// </summary>
    public sealed class GStateElms : TypeDict<PdfGState>
    {
        internal override PdfType Type { get { return PdfType.GStateElms; } }

        Dictionary<PdfItem, string> _reverse;

        internal GStateElms()
            : this(new TemporaryDictionary()) { }

        internal GStateElms(PdfDictionary dict)
            : base(dict, PdfType.GState, null) { }

        protected override void SetT(string key, PdfGState item)
        {
            if (_reverse != null && _elems.Contains(key))
            {
                var font = this[key];
                _reverse.Remove(font);
                _reverse.Add(item, key);
            }
            _elems.SetItem(key, item, item.HasReference);
        }

        internal string Add(PdfGState gs)
        {
            if (_reverse == null) _reverse = _elems.GetReverseDict();
            PdfItem key = (gs.HasReference) ? ((IRef)gs).Reference : (PdfItem)gs;

            string id;
            if (!_reverse.TryGetValue(gs, out id))
            {
                if (ReferenceEquals(gs, key) || !_reverse.TryGetValue(gs, out id))
                {
                    var name = GetNewName();
                    _elems.SetItem(name, gs, true);
                    _reverse.Add(gs, name);
                    return name;
                }
            }

            return id;
        }

        string GetNewName()
        {
            int c = _reverse.Count;
            var sb = new StringBuilder(4);
            do
            {
                sb.Length = 0;
                sb.Append("gs");
                sb.Append(++c);
            } while (_elems.Contains(sb.ToString()));
            return sb.ToString();
        }

        protected override void DictChanged()
        {
            _reverse = null;
        }

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override TypeDict<PdfGState> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg)
        {
            return new GStateElms(elems);
        }
    }
}
