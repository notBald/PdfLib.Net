using System;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    /// <summary>
    /// Base class for shaders (8.7.4.3)
    /// </summary>
    public abstract class PdfShading : Elements
    {
        #region Properties

        internal sealed override PdfType Type { get { return PdfType.Shading; } }

        public int ShadingType { get { return _elems.GetUIntEx("ShadingType"); } }

        /// <summary>
        /// A color space. Pattern and (in some cases) indexed color spaces
        /// are not allowed.
        /// </summary>
        public IColorSpace ColorSpace 
        { get { return (IColorSpace) _elems.GetPdfTypeEx("ColorSpace", PdfType.ColorSpace); } }

        public bool AntiAlias 
        { 
            get { return _elems.GetBool("AntiAlias", false); }
            set 
            {
                if (value) _elems.SetBool("AntiAlias", value);
                else _elems.Remove("AntiAlias");
            }
        }

        /// <summary>
        /// The backround color, as a DblColor.
        /// </summary>
        public PdfColor Background
        {
            get 
            {
                var col = BackgroundAr;
                if (col == null) return null;
                return ColorSpace.Converter.MakeColor(col); 
            }
            set 
            {
                if (value == null)
                    _elems.Remove("Background");
                else
                    BackgroundAr = value.ToArray();
            }
        }

        /// <summary>
        /// The backround color, as ColorSpace components.
        /// </summary>
        public double[] BackgroundAr
        {
            get
            {
                var ret = (RealArray)_elems.GetPdfType("Background", PdfType.RealArray);
                if (ret == null) return null;
                if (ret.Length != ColorSpace.NComponents)
                    throw new PdfLogException(ErrSource.Dictionary, PdfType.Array, ErrCode.Wrong);
                return ret.ToArray();
            }
            set
            {
                if (value == null)
                    _elems.Remove("Background");
                else
                {
                    if (value.Length != ColorSpace.NComponents)
                        throw new PdfLogException(ErrSource.Dictionary, PdfType.Array, ErrCode.Wrong);
                    _elems.SetItem("Background", new RealArray(value), false);
                }
            }
        }

        /// <summary>
        /// Bounding box of the shader
        /// </summary>
        public PdfRectangle BBox
        {
            get
            {
                return (PdfRectangle)_elems.GetPdfType("BBox", PdfType.Rectangle);
            }
            set
            {
                _elems.SetItem("BBox", value, false);
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Creates a temporary shading object that a user can modify before adding it
        /// to a document
        /// </summary>
        protected PdfShading(IColorSpace color_space, int shading_type)
            : this(color_space, shading_type, new TemporaryDictionary())
        {  }

        protected PdfShading(IColorSpace color_space, int shading_type, PdfDictionary dict)
            : base(dict)
        {
            _elems.SetInt("ShadingType", shading_type);
            if (color_space is PatternCS)
                throw new PdfNotSupportedException("Can't use a pattern colorspace with a shader");

            if (color_space == null)
                throw new ArgumentException("Invalid color space");

            _elems.SetItem("ColorSpace", (PdfItem)color_space, (color_space is IRef));
        }

        protected PdfShading(PdfDictionary dict) : base(dict) 
        { }
        #endregion

        internal sealed override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj != null && obj.GetType() == GetType())
            {
                var shade = (PdfShading)obj;
                if (ColorSpace.Equals(shade.ColorSpace) &&
                    AntiAlias == shade.AntiAlias &&
                    PdfColor.Equals(Background, shade.Background))
                {
                    var box = BBox; var shade_box = shade.BBox;
                    if (box == null)
                    {
                        if (shade_box != null)
                            return false;
                    }
                    else if (shade_box == null || !box.Equivalent(shade_box))
                        return false;
                    return Equivalent(shade);
                }
            }
            return false;
        }

        protected abstract bool Equivalent(PdfShading obj);

        internal static PdfShading Create(PdfDictionary dict)
        {
            //Can also be a stream
            switch (dict.GetUIntEx("ShadingType"))
            {
                case 1: return new PdfFunctionShading(dict);
                case 2: return new PdfAxialShading(dict);
                case 3: return new PdfRadialShading(dict);
            }

            throw new NotImplementedException("ShadingType");
        }

        internal static PdfShading Create(IWStream stream)
        {
            //Can also be a stream
            switch (stream.Elements.GetUIntEx("ShadingType"))
            {
                case 4: return new PdfGouraudShading(stream);
                case 6: return new PdfCoonsShading(stream);
                case 7: return new PdfTensorShading(stream);
            }

            throw new NotImplementedException("ShadingType");
        }
    }

    /// <summary>
    /// A dictionary over patterns
    /// </summary>
    public sealed class ShadingElms : TypeDict<PdfShading>
    {
        //Need to do reverse lookups for "render.DrawPage" to
        //function in a sensible manner
        Dictionary<PdfItem, string> _reverse;

        internal override PdfType Type { get { return PdfType.ShadingElms; } }

        internal ShadingElms(PdfDictionary dict)
            : base(dict, PdfType.Shading, null) { }

        //I'm thinking, make this method public and drop the (name, pat)
        //method.
        internal string Add(PdfShading shade)
        {
            if (_reverse == null) _reverse = _elems.GetReverseDict();

            PdfItem key = (shade.HasReference) ? ((IRef)shade).Reference : (PdfItem)shade;

            string id;
            if (!_reverse.TryGetValue(key, out id))
            {
                if (ReferenceEquals(key, shade) || !_reverse.TryGetValue(shade, out id))
                {
                    var name = GetNewName();
                    _elems.SetItem(name, shade, true);
                    _reverse.Add(shade, name);
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
                sb.Append("sh");
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
        protected override TypeDict<PdfShading> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg)
        {
            return new ShadingElms(elems);
        }
    }

    [DebuggerDisplay("{ExtendBefore} - {ExtendAfter}")]
    public struct ExtendShade
    {
        public readonly bool ExtendBefore;
        public readonly bool ExtendAfter;
        public ExtendShade(bool ext_before, bool ext_after)
        { ExtendBefore = ext_before; ExtendAfter = ext_after; }
        internal PdfArray ToArray()
        { return new SealedArray(new PdfItem[] { PdfBool.GetBool(ExtendBefore), PdfBool.GetBool(ExtendAfter) }); }
        public static bool operator ==(ExtendShade p1, ExtendShade p2)
        { return p1.ExtendBefore == p2.ExtendBefore && p1.ExtendAfter == p2.ExtendAfter; }
        public static bool operator !=(ExtendShade p1, ExtendShade p2)
        { return p1.ExtendBefore != p2.ExtendBefore || p1.ExtendAfter != p2.ExtendAfter; }
        public override bool Equals(object obj)
        {
            if (obj is ExtendShade)
            {
                var x = (ExtendShade)obj;
                return x == this;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return ExtendBefore.GetHashCode() + 23 * ExtendAfter.GetHashCode();
        }
    }
}
