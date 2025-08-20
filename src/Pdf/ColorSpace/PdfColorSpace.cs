using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// Base class for immutable PDF color spaces.
    /// </summary>
    public abstract class PdfColorSpace : PdfObject, IColorSpace
    {
        #region Properties

        /// <summary>
        /// PdfType.ColorSpace
        /// </summary>
        internal sealed override PdfType Type
        {
            get { return PdfType.ColorSpace; }
        }

        /// <summary>
        /// What type of colorspace this is
        /// </summary>
        public abstract PdfCSType CSType { get; }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public abstract int NComponents { get; }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public abstract double[] DefaultDecode { get; }

        /// <summary>
        /// Initial color
        /// </summary>
        public abstract PdfColor DefaultColor { get; }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public abstract double[] DefaultColorAr { get; }

        #endregion

        #region Overrides

        /// <summary>
        /// PdfColorSpace is immutable
        /// </summary>
        public sealed override PdfItem Clone() { return this; }

        #endregion

        #region IColorSpace

        /// <summary>
        /// Used to convert raw values into colors
        /// </summary>
        public abstract ColorConverter Converter { get; }

        /// <summary>
        /// Compares the color space
        /// </summary>
        /// <remarks>All inheritors are single instance classes</remarks>
        public bool Equals(IColorSpace cs)
        {
            return ReferenceEquals(this, cs);
        }

        #endregion

        #region Create

        /// <summary>
        /// Creates a colorpsace
        /// </summary>
        internal static PdfObject Create(string cs, IntMsg msg)
        {
            switch (cs)
            {
                case "DeviceRGB":
                    return DeviceRGB.Instance;
                case "DeviceGray":
                    return DeviceGray.Instance;
                case "DeviceCMYK":
                    return DeviceCMYK.Instance;
                case "Pattern":
                    return PatternCS.Instance;
            }
            /*var dcs = resources.Elements.GetDictionary("/ColorSpace");
            if (dcs != null)
            {
                var rcs = dcs.Elements.GetReference(cs);
                if (rcs != null && rcs.Value is PdfArray && (rcs.Value as PdfArray).Elements.Count == 2)
                {
                    var pda = rcs.Value as PdfArray;
                    if ("/ICCBased".Equals(pda.Elements.GetName(0)) && pda.Elements[1] is PdfReference)
                        return new ICCProfile((pda.Elements[1] as PdfReference).Value as PdfDictionary, owner);
                    else
                        throw new NotImplementedException("Probably a patern brush. Try gm2121 (Fra M191 Blackview).pdf and impl.");
                }
                throw new NotImplementedException("Probably a function brush. Try issue_1a.pdf and impl.");
            }
            throw new NotSupportedException();*/
            throw new NotImplementedException();
        }

        internal static string Create(IColorSpace cs)
        {
            if (cs == DeviceRGB.Instance)
                return "DeviceRGB";
            if (cs == DeviceGray.Instance)
                return "DeviceGray";
            if (cs == DeviceCMYK.Instance)
                return "DeviceCMYK";
            if (cs == PatternCS.Instance)
                return "Pattern";
            return null;
        }

        internal static PdfObject Create(PdfArray ar, IntMsg msg, object obj)
        {
            if (ar.Length < 2)
            {
                if (ar.Length == 1 && ar[0].Deref() is PdfName)
                    return Create(ar[0].GetString(), IntMsg.NoMessage);

                throw new PdfInternalException("corrupt object");
            }
            var name = ar[0];
            if (!(name is PdfName)) throw new PdfInternalException("corrupt object");

            switch (name.GetString())
            {
                case "Lab":
                    return new LabCS(ar);

                case "Indexed":
                    return new IndexedCS(ar);

                case "ICCBased":
                    return new ICCBased(ar);

                case "Pattern":
                    return new PatternCS(ar, (PdfColorSpaceElms) obj);

                case "Separation":
                    return new SeparationCS(ar);

                case "CalRGB":
                    return new CalRGBCS(ar);

                case "CalGray":
                    return new CalGrayCS(ar);

                case "DeviceN":
                    return DeviceNCS.Create(ar);
            }

            throw new NotImplementedException();
        }

        #endregion
    }

    public sealed class PdfColorSpaceElms : Elements, IEnumerable<KeyValuePair<string, IColorSpace>>
    {
        //Need to do reverse lookups for "render.DrawPage" to
        //function in a sensible manner
        Dictionary<PdfItem, string> _reverse;

        internal override PdfType Type { get { return PdfType.ColorSpaceElms; } }

        internal PdfColorSpaceElms(PdfDictionary dict)
            : base(dict) { }

        #region Indexing

        internal IColorSpace GetColorSpace(string name)
        {
            // Perhaps redundant. See PdfColorSpace.Create(...)
            // (Though, will remain for now due to impl. details)
            switch (name)
            {
                case "DeviceRGB":
                    return DeviceRGB.Instance;
                case "DeviceGray":
                    return DeviceGray.Instance;
                case "DeviceCMYK":
                    return DeviceCMYK.Instance;
                case "Pattern":
                    return PatternCS.Instance;
            }

            //This is not redudant, and is only done here. Never in PdfColorSpace.Create
            return this[name];
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public IEnumerator<KeyValuePair<string, IColorSpace>> GetEnumerator()
        {
            return new TypeDictEnumerator(_elems, this);
        }

        /// <summary>
        /// Retrive PdfObject from dictionary
        /// </summary>
        /// <param name="key">Key of the object</param>
        /// <returns>The PdfObject</returns>
        public IColorSpace this[string key] 
        { 
            get { return (IColorSpace)_elems.GetPdfType(key, PdfType.ColorSpace, IntMsg.PdfColorSpaceElms, this); }
            set
            {
                if (value != null && !(value is PdfItem))
                    throw new PdfNotSupportedException("Colorspace not supported in PDF documents");
                if (_reverse != null && _elems.Contains(key))
                {
                    var font = this[key];
                    _reverse.Remove((PdfItem) font);
                    _reverse.Add((PdfItem) value, key);
                }
                if (value is IRef)
                    _elems.SetItem(key, (PdfItem)value, ((IRef)value).HasReference);
                else
                    _elems.SetItem(key, (PdfItem)value, false);
            }
        }

        /// <summary>
        /// Enum wrapper
        /// </summary>
        /// <remarks>Based on TypeDict.TypeDictEnumerator</remarks>
        private class TypeDictEnumerator : IEnumerator<KeyValuePair<string, IColorSpace>>
        {
            readonly IEnumerator<KeyValuePair<string, PdfItem>> _enum;
            readonly PdfDictionary _parent;
            readonly PdfItem _msg;
            readonly List<KeyValuePair<string, IColorSpace>> _update;

            public TypeDictEnumerator(PdfDictionary parent, PdfItem msg)
            {
                _parent = parent;
                _update = new List<KeyValuePair<string, IColorSpace>>(_parent.Count);
                _msg = msg;
                _enum = _parent.GetEnumerator();
            }

            public bool MoveNext() { return _enum.MoveNext(); }
            object IEnumerator.Current { get { return Current; } }
            public KeyValuePair<string, IColorSpace> Current
            {
                get
                {
                    var kp = _enum.Current;
                    if (kp.Value.Type == PdfType.ColorSpace)
                        return new KeyValuePair<string, IColorSpace>(kp.Key, (IColorSpace)kp.Value.Deref());

                    //Transformd the object into the right type without modifying the collection
                    var child = kp.Value;
                    var new_kp = new KeyValuePair<string, IColorSpace>(kp.Key, (IColorSpace)child.ToType(PdfType.ColorSpace, IntMsg.Message, _msg));

                    //The newly created object must be put in the dictionary, but it will have to wait until
                    //the enumeration is done. 
                    _update.Add(new_kp);
                    return new_kp;
                }
            }
            public void Reset() { _enum.Reset(); }
            public void Dispose()
            {
                _enum.Dispose();

                //Updates dictionary objects
                foreach (var kp in _update)
                    _parent.InternalReplace(kp.Key, (PdfItem) kp.Value);
            }
        }

        #endregion

        /// <summary>
        /// Creates a pattern color space with an underlying
        /// color space
        /// </summary>
        /// <param name="under_cs">The underlying colorspace</param>
        public PatternCS CreatePatternCS(IColorSpace under_cs)
        {
            var items = new PdfItem[] { new PdfName("Pattern"), new PdfName(Add(under_cs)) };
            PdfArray ar;
            if (_elems.IsWritable)
            {
                var tracker = _elems.Tracker;
                if (tracker == null)
                    ar = new TemporaryArray(items);
                else
                    ar = new WritableArray(items, tracker);
            }
            else
                ar = new SealedArray(items);

            return new PatternCS(ar, this);
        }

        /// <summary>
        /// Only for use during execution
        /// </summary>
        /// <remarks>
        /// Used when executing compiled documents.
        /// 
        /// When executing a compiled document one builds up a new
        /// resource dictionary. This method assigns names to color
        /// spaces and use a reverse lookup to stop itself from
        /// assigning multiple names to the same color space.
        /// </remarks>
        internal string Add(IColorSpace cs)
        {
            if (cs is DeviceRGB) return "DeviceRGB";
            if (cs is DeviceCMYK) return "DeviceCMYK";
            if (cs is DeviceGray) return "DeviceGray";
            if (cs is PatternCS && ((PatternCS)cs).UnderCS == null)
                return "Pattern";

            if (_reverse == null) _reverse = _elems.GetReverseDict();
            var itm = (PdfObject)cs;

            string id;
            if (!_reverse.TryGetValue(itm, out id))
            {
                string name;
                if (itm is IRef)
                {
                    var iref = (IRef)itm;
                    if (iref.HasReference && _reverse.TryGetValue(iref.Reference, out id))
                        return id;
                    name = GetNewName();
                    if (itm is IKRef ikref)
                        // There should be a "iref.CanAdopt" property instead of abusing ikref.IsOwner(null)
                        // Also, this issue afflict all SetItem(name, itm, iref.HasReference) calls.
                        // Todo: solve this in an elegant way
                        _elems.SetItem(name, itm, iref.HasReference || ikref.IsOwner(null));
                    else
                        _elems.SetItem(name, itm, iref.HasReference);
                }
                else
                {
                    name = GetNewName();
                    _elems.SetItem(name, itm, false);
                }
                
                _reverse.Add(itm, name);
                return name;
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
                sb.Append("cs");
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
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfColorSpaceElms(elems);
        }
    }

    /// <summary>
    /// Type of colorspace
    /// </summary>
    public enum PdfCSType
    {
        Device,
        CIE,
        Special
    }
}
