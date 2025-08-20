using PdfLib.Compile;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Transparency;
using PdfLib.Render;
using PdfLib.Render.PDF;
using PdfLib.Render.Commands;
using PdfLib.Write.Internal;
using System;

namespace PdfLib.Pdf
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Todo: Optional Content is not implemented. (8.11 in the specs)
    /// File with optional content that is only to be displayed while
    /// printing: MuPDF Issue 691528 - ib65-en.pdf
    ///  (This document is also encrypted)
    /// 
    /// </remarks>
    public sealed class PdfForm : PdfXObject, IWSPage, IForm
    {
        #region Variables and properties

        public int FormType { get { return _elems.GetUInt("FormType", 1); } }

        /// <summary>
        /// Bounding box, expressed in the coordinate system of the form
        /// </summary>
        public PdfRectangle BBox 
        { 
            get { return (PdfRectangle)_elems.GetPdfTypeEx("BBox", PdfType.Rectangle); }
            set { _elems.SetItemEx("BBox", value, false); }
        }

        /// <summary>
        /// Transform from the form's coordinate system, to the user coordinate system.
        /// </summary>
        public xMatrix Matrix
        {
            get
            {
                var ar = (RealArray) _elems.GetPdfType("Matrix", PdfType.RealArray);
                if (ar == null) return xMatrix.Identity;
                return new xMatrix(ar);
            }
            set
            {
                _elems.SetItem("Matrix", value.ToArray(), false);
            }
        }

        /// <summary>
        /// Transparency group
        /// </summary>
        public PdfTransparencyGroup Group
        {
            get { return (PdfTransparencyGroup)_elems.GetPdfType("Group", PdfType.Group); }
            set { _elems.SetItem("Group", value, true); }
        }

        [PdfVersion("1.2")]
        public PdfResources Resources 
        { 
            get 
            { 
                var ret = (PdfResources)_elems.GetPdfType("Resources", PdfType.Resources);

                if (ret == null)
                {
                    if (IsWritable)
                    {
                        if (_elems.Tracker == null)
                            ret = new PdfResources();
                        else
                            ret = new PdfResources(new WritableDictionary(_elems.Tracker));
                        _elems.SetNewItem("Resources", ret, false);
                    }
                    else
                        ret = new PdfResources(new SealedDictionary());
                }

                return ret;
            } 
        }
        internal PdfResources GetResources() { return (PdfResources)_elems.GetPdfType("Resources", PdfType.Resources); }

        public PdfContent Contents
        {
            get { return new PdfContent((IWStream)_stream, _elems); }
        }

        #endregion

        #region Init

        public PdfForm(double width, double height)
            : this(new PdfRectangle(0, 0, width, height)) { }

        public PdfForm(PdfRectangle BoundingBox)
            : this(new TemporaryDictionary(), BoundingBox)
        { }

        internal PdfForm(TemporaryDictionary dict, PdfRectangle BBox)
            : base(new WritableStream(dict), dict)
        {
            dict.SetName("Subtype", "Form");
            dict.DirectSet("BBox", BBox);
        }

        internal PdfForm(IWStream stream)
            : base(stream) 
        {  }

        /// <summary>
        /// Used when moving the form to a different document
        /// </summary>
        /// <remarks>Don't truly need a separate constructor for this, but I want
        /// to stay consistent  with other classes</remarks>
        internal PdfForm(IStream stream, PdfDictionary dict)
            : base(stream, dict)
        { }

        /// <summary>
        /// Creates a form that can have it's contents written to.
        /// </summary>
        /// <remarks>Used by DrawPage</remarks>
        internal static PdfForm CreateWritableForm(ResTracker tracker, PdfRectangle BBox, xMatrix transform, PdfTransparencyGroup group)
        {
            var cat = new Catalog();
            cat.Add("Subtype", new PdfName("Form"));
            if (!transform.IsIdentity)
                cat.Add("Matrix", transform.ToArray());
            PdfDictionary wd;
            if (tracker == null)
            {
                wd = new TemporaryDictionary(cat);
                tracker = new ResTracker();
            }
            else
                wd = new WritableDictionary(cat, tracker);

            wd.SetNewItem("BBox", tracker.MakeCopy(BBox, false), false);
            if (group != null)
                wd.SetNewItem("Group", tracker.MakeCopy(group, true), false);

            return new PdfForm(new WritableStream(wd), wd);
        }

        #endregion

        #region IERef

        protected override int IsLike(PdfXObject obj)
        {
            return (int) (Equivalent(obj) ? Equivalence.Identical : Equivalence.Different);
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is PdfForm)
            {
                var form = (PdfForm) obj;
                if (_elems.Equivalent(form._elems))
                    return PdfStream.Equals(_stream, form._stream);
            }
            return false;
        }

        #endregion

        #region DEBUG

        /// <summary>
        /// Converts this form into a unmodifiable PdfPage
        /// </summary>
        /// <param name="source">
        /// Optional source page this form was to be drawn on. 
        /// </param>
        public PdfPage ConvertToPage(PdfPage source, bool ignore_matrix)
        {
            var temp = new TemporaryDictionary();

            temp.SetName("Type", "Page");
            temp.SetItem("Contents", new WritableStream(_stream.RawStream, _stream.Filter), true);
            var m = Matrix;
            if (!ignore_matrix)
                temp.SetItem("MediaBox", m.Transform(BBox), false);
            else
                temp.SetItem("MediaBox", BBox, false);
            var res =  _elems["Resources"];
            if (res == null)
            {
                if (source != null)
                {
                    res = source.Resources;
                    try
                    {
                        var cmds = new PdfCompiler().Compile(Contents, (PdfResources)res);
                        if (!RenderCMD.NeedRes(cmds))
                            res = null;
                    }
                    catch (Exception) { }
                }
            }
            else res = res.Deref();
               
            temp.SetItem("Resources", res, true);
            var g = _elems["Group"];
            if (g != null)
                temp.SetItem("Group", g.Deref(), true);

            var page = new PdfPage(temp);
            if (!ignore_matrix)
            {
                using (var draw = new DrawPage(page))
                {
                    draw.PrependCM(m);
                    draw.Commit(true);
                }
            }
            return page;
        }

        public PdfPage ConvertToPage()
        {
            return ConvertToPage(null, true);
        }

        public static PdfForm FromPage(PdfPage page)
        {
            if (page == null) return null;
            return page.ConveretToForm();
        }

        #endregion

        #region IWPage

        void IWSPage.SetContents(byte[] contents)
        {
            SetContents(contents);
        }

        internal void SetContents(byte[] contents)
        {
            if (!(_stream is NewStream))
                throw new PdfNotWritableException();
            ((NewStream)_stream).DecodedStream = contents;
        }

        #endregion

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override StreamElm MakeCopy(IStream stream, PdfDictionary dict)
        {
            return new PdfForm(stream, dict);
        }
    }
}
