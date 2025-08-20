using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Render;
using PdfLib.Render.Font;

namespace PdfLib.Pdf.Font
{
    /// <summary>
    /// Base class for all fonts
    /// </summary>
    public abstract class PdfFont : Elements, IDisposable
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Font
        /// </summary>
        internal sealed override PdfType Type { get { return PdfType.Font; } }

        public FontType SubType
        {
            get
            {
                var sub_type = _elems.GetNameEx("Subtype");

                switch (sub_type)
                {
                    case "Type0": return FontType.Type0;
                    case "Type1": return FontType.Type1;
                    case "MMType1": return FontType.MMType1;
                    case "TrueType": return FontType.TrueType;
                    case "Type3": return FontType.Type3;
                    default: throw new PdfReadException(PdfType.Name, ErrCode.OutOfRange);
                }
            }
        }

        /// <summary>
        /// Name of the font, null if unknown
        /// </summary>
        public virtual string FontName { get { return null; } }

        /// <summary>
        /// Optional cmap for translating to unicode
        /// </summary>
        public PdfCmap ToUnicode { get { return (PdfCmap)_elems.GetPdfType("ToUnicode", PdfType.Cmap); } }

        /// <summary>
        /// Gets an encoding object, which can be a PSMap or a PdfEncoding object
        /// </summary>
        /// <returns>PSMap or a PdfEncoding object</returns>
        internal abstract PdfItem FetchEncoding(); 

        #endregion

        #region Init

        protected PdfFont(PdfDictionary dict)
            : base(dict) { }

        ~PdfFont() { Dispose(); }

        public void Dispose()
        {

        }

        #endregion

        #region Overrides

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(obj, this)) return true;
            if (obj == null) return false;
            if (obj.GetType() == GetType())
                return ((Equivalence)IsLike((PdfFont)obj)) == Equivalence.Identical;
            return false;
        }

        protected abstract int IsLike(PdfFont obj);

        #endregion

        public rFont Realize()
        {
            return Realize<InfoPath>(InfoFF.CreateFactory());
        }

        public rFont Realize<Path>(IFontFactory<Path> factory)
        {
            var fc = factory.FontCache;
            rFont cached_font;
            if (fc == null)
                cached_font = RealizeImpl<Path>(factory);
            else
            {
                if (!fc.TryGetValue(this, out cached_font))
                {
                    cached_font = RealizeImpl<Path>(factory);
                    fc.Add(this, cached_font);
                }
            }
            cached_font.Init();
            return cached_font;
        }

        protected abstract rFont RealizeImpl<Path>(IFontFactory<Path> factory);

        internal static PdfFont Create(PdfDictionary dict)
        {
            var sub_type = dict.GetName("Subtype");

            //Some files write "SubType" instead
            if (sub_type == null)
            {
                sub_type = dict.GetName("SubType");
                var repp = (IDictRepair)dict;
                if (sub_type == null)
                {
                    //If there's no subtype -> assume Type1
                    sub_type = "Type1";
                }
                else
                {
                    repp.RepairRemove("SubType");
                }

                repp.RepairSet("Subtype", new PdfName(sub_type));
            }

            switch (sub_type)
            {
                case "Type0": return new PdfType0Font(dict);
                case "MMType1":
                case "Type1": return new PdfType1Font(dict);
                case "TrueType": return new PdfType1Font(dict);
                case "Type3": return new PdfType3Font(dict);
                default: throw new PdfReadException(PdfType.Name, ErrCode.OutOfRange);
            }
        }
    }

    /// <summary>
    /// A dictionary over fonts
    /// </summary>
    public sealed class FontElms : TypeDict<PdfFont>
    {
        Dictionary<PdfItem, string> _reverse;

        internal override PdfType Type { get { return PdfType.FontElms; } }

        internal FontElms(PdfDictionary dict)
            : base(dict, PdfType.Font, null) { }

        protected override void SetT(string key, PdfFont item)
        {
            if (_reverse != null && _elems.Contains(key))
            {
                var font = this[key];
                _reverse.Remove(font);
                if (item != null)
                    _reverse.Add(item, key);
            }
            _elems.SetItem(key, item, true);
        }

        internal string Add(PdfFont font)
        {
            if (_reverse == null) _reverse = _elems.GetReverseDict();

            string id;
            PdfItem key = (font.HasReference) ? ((IRef)font).Reference : (PdfItem) font;

            if (!_reverse.TryGetValue(key, out id))
            {
                if (ReferenceEquals(key, font) || !_reverse.TryGetValue(font, out id))
                {
                    var name = GetNewName();
                    _elems.SetItem(name, font, true);
                    _reverse.Add(font, name);
                    return name;
                }
            }

            return id;
        }

        protected override void DictChanged()
        {
            _reverse = null;
        }

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override TypeDict<PdfFont> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg)
        {
            return new FontElms(elems);
        }

        string GetNewName()
        {
            int c = _reverse.Count;
            var sb = new StringBuilder(4);
            do
            {
                sb.Length = 0;
                sb.Append("f");
                sb.Append(++c);
            } while (_elems.Contains(sb.ToString()));
            return sb.ToString();
        }
    }
}
