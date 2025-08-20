using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Internal;
using System.Text;
using PdfLib.Compile;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Optional;

namespace PdfLib.Pdf.Annotation
{
    public abstract class PdfMarkupAnnotation : PdfAnnotation
    {
        #region Variables and properties

        /// <summary>
        /// Title of popup window
        /// </summary>
        [PdfVersion("1.1")]
        public string T
        {
            get { return _elems.GetUnicodeString("T"); }
            set { _elems.SetUnicodeString("T", value); }
        }

        /// <summary>
        /// Short description of the subject being addressed
        /// </summary>
        [PdfVersion("1.5")]
        public string Subj
        {
            get { return _elems.GetUnicodeString("Subj"); }
            set { _elems.SetUnicodeString("Subj", value); }
        }

        [PdfVersion("1.3")]
        public PdfDictionary Popup
        {//Must be indirect
            get { return _elems.GetDictionary("Popup"); }
        }

        /// <summary>
        /// Annotation opacity. Not used for Apperance Streams
        /// </summary>
        [PdfVersion("1.4")]
        public double CA
        {
            get { return _elems.GetReal("CA", 1); }
            set { _elems.SetReal("CA", value, 1); }
        }

        [PdfVersion("1.5")]
        public string RC
        {
            get { return _elems.GetTextStreamOrString("RC"); }
            set { _elems.SetUnicodeString("RC", value); }
        }

        [PdfVersion("1.5")]
        public PdfDate CreationDate
        {
            get { return _elems.GetDate("CreationDate"); }
            set { _elems.SetItem("CreationDate", value, false); }
        }

        /// <summary>
        /// In reply to
        /// </summary>
        [PdfVersion("1.5")]
        public PdfAnnotation IRT
        {
            get { return (PdfAnnotation)_elems.GetPdfType("IRT", PdfType.Annotation); }
            set { _elems.SetItem("IRT", value, true); }
        }

        [PdfVersion("1.6")]
        public string RT
        {
            get { var rt = _elems.GetName("RT"); return rt == null ? "R" : rt; }
        }

        [PdfVersion("1.6")]
        public string IT
        {
            get { return _elems.GetName("IT"); }
            set { _elems.SetName("IT", value); }
        }

        [PdfVersion("1.7")]
        public PdfDictionary ExData
        {
            get { return _elems.GetDictionary("ExData"); }
        }

        #endregion

        #region Init

        internal PdfMarkupAnnotation(PdfDictionary dict)
            : base(dict)
        {
            //This is just a reminder that "PdfAnnotation(string subtype, PdfRectangle bounds)" should be used when
            //creating new annotations
            System.Diagnostics.Debug.Assert(!(dict is TemporaryDictionary) || dict.Count > 0, "Use the other constructor");
            _elems.CheckType("Annot");
        }

        /// <summary>
        /// Constructor for creating new annotations
        /// </summary>
        /// <param name="dict"></param>
        /// <param name="subtype"></param>
        /// <param name="bounds"></param>
        internal PdfMarkupAnnotation(string subtype, PdfRectangle bounds)
            : base(new TemporaryDictionary())
        {
            _elems.SetName("Subtype", subtype);
            Rect = bounds;
        }

        internal static PdfMarkupAnnotation Create(PdfDictionary dict)
        {
            switch (dict.GetNameEx("Subtype"))
            {
                case "Text":
                    return new PdfTextAnnot(dict);

                case "FreeText":
                    return new PdfFreeTextAnnot(dict);

                case "FileAttachment":
                    return new PdfFileAttachmentAnnot(dict);

                case "Line":
                    return new PdfLineAnnot(dict);

                case "Ink":
                    return new PdfInkAnnot(dict);

                case "Square":
                    return new PdfSquareAnnot(dict);

                case "Circle":
                    return new PdfCircleAnnot(dict);

                case "Stamp":
                    return new PdfStampAnnot(dict);

                case "Caret":
                    return new PdfCaretAnnot(dict);

                case "Popup":
                    return new PdfPopupAnnot(dict);

                case "Polygon":
                    return new PdfPolygonAnnot(dict);

                case "PolyLine":
                    return new PdfPolylineAnnot(dict);

                default:
                    //Todo: Log this
                    return new PdfUnkownAnnot(dict);
            }
        }

        #endregion
    }

    /// <summary>
    /// A dictionary over annotations
    /// </summary>
    public sealed class AnnotationElms : TypeArray<PdfMarkupAnnotation>
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.AnnotationElms; } }

        /// <summary>
        /// The number of annotations
        /// </summary>
        public int Count { get { return _items.Length; } }

        /// <summary>
        /// Needed to verify that new annotations have the correct owner
        /// </summary>
        PdfPage _owner;

        #endregion

        #region Init

        /// <summary>
        /// Creates an AnnotationElms instance without owner.
        /// </summary>
        internal AnnotationElms(PdfMarkupAnnotation annot)
            : base(annot, PdfType.Annotation) { }

        internal AnnotationElms(PdfArray array, PdfPage owner)
            : base(array, PdfType.Annotation) { _owner = owner; }

        internal AnnotationElms(bool writable, ResTracker tracker, PdfPage owner)
            : base(writable, tracker, PdfType.Annotation) { _owner = owner; }

        #endregion

        #region Adding and setting

        public override void Set(int index, PdfMarkupAnnotation annot)
        {
            PdfItem p = annot.P;
            if (p != null && !ReferenceEquals(p, _owner))
                throw new PdfOwnerMissmatch("Annotation owned by different page.");

            _items.SetItem(index, annot, true);
        }

        public void Add(PdfMarkupAnnotation annot)
        {
            PdfItem p = annot.P;
            if (p != null && !ReferenceEquals(p, _owner))
                throw new PdfOwnerMissmatch("Annotation owned by different page.");
            _items.AddItem(annot, true);
        }

        /// <summary>
        /// Adds the annnotation and registers this page as its owner. 
        /// Only reqired for screen based annotations
        /// </summary>
        public void TakeOwnership(PdfMarkupAnnotation annot)
        {
            PdfItem p = annot.P;
            if (p != null && !ReferenceEquals(p, _owner))
                throw new PdfOwnerMissmatch("Annotation owned by different page.");
            annot.P = _owner;
            Add(annot);
        }

        public void RemoveOwnership(PdfMarkupAnnotation annot)
        {
            var p = annot.P;
            if (ReferenceEquals(annot.P, _owner))
            {
                _items.Remove(annot);
                annot.P = null;
            }
        }

        /// <summary>
        /// Removes all annotations
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new AnnotationElms(array, _owner);
        }

        #endregion
    }
}
