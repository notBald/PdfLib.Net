using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using System.Text;
using PdfLib.Compile;
using PdfLib.Pdf.Transparency;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// Base class for images, forms and potentially other XObjects
    /// </summary>
    public abstract class PdfXObject : StreamElm
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.XObject
        /// </summary>
        internal sealed override PdfType Type { get { return PdfType.XObject; } }

        #region Marked Contents variables

        /// <summary>
        /// The integer key of the form XObject’s entry in the structural parent tree
        /// </summary>
        public PdfInt StructParent { get { return _elems.GetUIntObj("StructParent"); } }

        /// <summary>
        /// The integer key of the form XObject’s entry in the structural parent tree
        /// </summary>
        /// <remarks>
        /// A object that has this set contains marked content. It can not be
        /// marked. (I.e. StructParent and StructParents is mutualy exlusive)
        /// </remarks>
        public PdfInt StructParents { get { return _elems.GetUIntObj("StructParents"); } }

        #endregion

        #endregion

        #region Init

        internal PdfXObject(IWStream stream)
            : base(stream)
        {
            _elems.CheckType("XObject");
        }

        protected PdfXObject(IStream stream, PdfDictionary dict)
            : base(stream, dict)
        {  }

        #endregion

        #region IERef

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(obj, this)) return true;
            if (obj == null) return false;
            if (obj.GetType() == GetType())
                return ((Equivalence)IsLike((PdfXObject)obj)) == Equivalence.Identical;
            return false;
        }

        protected abstract int IsLike(PdfXObject obj);

        #endregion

        #region Create

        /// <summary>
        /// Creates a XObject
        /// </summary>
        /// <param name="stream">Stream to create the XObject from</param>
        /// <param name="msg">A message</param>
        /// <returns>The XObject</returns>
        /// <remarks>
        /// Since Thumbnails don't have a subtype, set the message to
        /// IntMsg.Thumbnail
        /// </remarks>
        internal static PdfXObject Create(IWStream stream, IntMsg msg, object obj)
        {
            string subtype = stream.Elements.GetName("Subtype");

            switch (subtype)
            {
                case "Image":
                    if (obj != null) //For PdfASDict.cs
                        break;
                    return new PdfImage(stream);

                case "Form":
                    return new PdfForm(stream);

                default:
                    //This is a thumbnail
                    if (msg == IntMsg.Thumbnail)
                        return new PdfImage(stream);
                    break;
            }

            if (msg == IntMsg.AssumeForm)
                return new PdfForm(stream);
            if (msg == IntMsg.AssumeImage)
                return new PdfImage(stream);

            throw new PdfCastException(ErrSource.General, PdfType.XObject, ErrCode.WrongType);
        }

        #endregion
    }

    /// <summary>
    /// A dictionary over XObjects
    /// </summary>
    public sealed class XObjectElms : TypeDict<PdfXObject>
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.XObjectElms; } }

        Dictionary<PdfItem, string> _reverse;

        /// <summary>
        /// Returns a listing of all images in this resource dictionary
        /// </summary>
        public PdfImage[] Images 
        { 
            get 
            { 
                var images = GetImages(false, new HashSet<XObjectElms>()); 
                var ret = new PdfImage[images.Count];
                for (int c = 0; c < ret.Length; c++)
                    ret[c] = images[c].Image;
                return ret;
            } 
        }

        /// <summary>
        /// Returns a listing of all images in this resource dictionary
        /// </summary>
        public ImagePtr[] ImagePtrs { get { return GetImages(false, new HashSet<XObjectElms>()).ToArray(); } }

        /// <summary>
        /// Returns all images in this dictionary and any child dictionary, except duplicated
        /// images.
        /// </summary>
        public PdfImage[] AllImages
        {
            get
            {
                var il = GetImages(true, new HashSet<XObjectElms>());
                var imgs = new List<PdfImage>(il.Count);
                foreach (var image_ptr in il)
                {
                    var image = image_ptr.Image;
                    if (!imgs.Contains(image))
                        imgs.Add(image);
                }

                return imgs.ToArray();
            }
        }

        /// <summary>
        /// Returns all images in this dictionary and any child dictionary, except duplicated
        /// images.
        /// </summary>
        public ImagePtr[] AllImagePtrs
        {
            get
            {
                var il = GetImages(true, new HashSet<XObjectElms>());
                var imgs = new List<ImagePtr>(il.Count);
                foreach (var image in il)
                {
                    if (!imgs.Contains(image))
                        imgs.Add(image);
                }

                return imgs.ToArray();
            }
        }

        #endregion

        #region Init

        internal XObjectElms()
            : this(new TemporaryDictionary()) { }

        internal XObjectElms(PdfDictionary dict)
            : base(dict, PdfType.XObject, null) { }

        #endregion

        #region Internal functions

        protected override void SetT(string key, PdfXObject item)
        {
            if (_reverse != null && _elems.Contains(key))
            {
                var font = this[key];
                _reverse.Remove(font);
                _reverse.Add(item, key);
            }
            _elems.SetItem(key, item, true);
        }

        internal string AddImg(PdfImage img)
        {
            if (_reverse == null) _reverse = _elems.GetReverseDict();
            PdfItem key = (img.HasReference) ? ((IRef)img).Reference : (PdfItem)img;

            string id;
            if (!_reverse.TryGetValue(img, out id))
            {
                if (ReferenceEquals(img, key) || !_reverse.TryGetValue(img, out id))
                {
                    var name = GetNewName("im");
                    _elems.SetItem(name, img, true);
                    _reverse.Add(img, name);
                    return name;
                }
            }

            return id;
        }

        internal string AddForm(PdfForm form)
        {
            if (_reverse == null) _reverse = _elems.GetReverseDict();
            PdfItem key = (form.HasReference) ? ((IRef)form).Reference : (PdfItem)form;

            string id;
            if (!_reverse.TryGetValue(form, out id))
            {
                if (ReferenceEquals(form, key) || !_reverse.TryGetValue(form, out id))
                {
                    var name = GetNewName("fo");
                    _elems.SetItem(name, form, true);
                    _reverse.Add(form, name);
                    return name;
                }
            }

            return id;
        }

        internal string CreateForm(PdfRectangle BBox, xMatrix matrix, PdfTransparencyGroup group, out PdfForm form)
        {
            if (!IsWritable)
                throw new PdfNotWritableException();
            if (_reverse == null) _reverse = _elems.GetReverseDict();
            form = PdfForm.CreateWritableForm(_elems.Tracker, BBox, matrix, group);
            var name = GetNewName("fo");
            _elems.SetNewItem(name, form, true);
            _reverse.Add(form, name);
            return name;
        }

        string GetNewName(string b)
        {
            int c = _reverse.Count;
            var sb = new StringBuilder(4);
            do
            {
                sb.Length = 0;
                sb.Append(b);
                sb.Append(++c);
            } while (_elems.Contains(sb.ToString()));
            return sb.ToString();
        }

        /// <summary>
        /// Returns a listing of all images in this resource dictionary
        /// and in that of any child. 
        /// </summary>
        private List<ImagePtr> GetImages(bool include_children, HashSet<XObjectElms> checking)
        {
            checking.Add(this);
            var list = new List<ImagePtr>(Count);
            foreach (var kv in _elems)
            {
                //Implementation note: All xObjects are streams, and by that the
                //dictionary we're enumerating will never be modified during this
                //enumeration.
                //
                //If a dictionary is modified while being enumerated one will get
                //an error. However since this never happens (if a non-stream is
                //enountered an exception will be thrown instead) we get away with it
                try
                {
                    var xobj = this[kv.Key];
                    if (xobj is PdfImage)
                        list.Add(new ImagePtr(kv.Key, this));
                    else if (include_children && xobj is PdfForm)
                    {
                        var xobjects = ((PdfForm)xobj).Resources.XObject;
                        if (!checking.Contains(xobjects))
                            list.AddRange(((PdfForm)xobj).Resources.XObject.GetImages(true, checking));
                    }
                }
                catch (PdfLibException) { /* If an xobject is corrupt it will simply not be added */ }
            }
            return list;
        }

        #endregion

        #region Required overrides

        protected override void DictChanged()
        {
            _reverse = null;
        }

        /// <summary>
        /// Used when moving the dictionary to another class.
        /// </summary>
        protected override TypeDict<PdfXObject> MakeCopy(PdfDictionary elems, PdfType type, PdfItem msg)
        {
            return new XObjectElms(elems);
        }

        #endregion

        public class ImagePtr
        {
            readonly XObjectElms _source;
            readonly string _key;

            /// <summary>
            /// Key to this image in the resources
            /// </summary>
            public string Key { get { return _key; } }

            /// <summary>
            /// Get/set the image from the resources
            /// </summary>
            public PdfImage Image
            { 
                get { return (PdfImage) _source[_key]; }
                set { _source[_key] = value; }
            }

            internal ImagePtr(string key, XObjectElms source)
            { _key = key; _source = source; }
        }
    }
}
