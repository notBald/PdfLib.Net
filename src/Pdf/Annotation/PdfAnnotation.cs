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
    /// <summary>
    /// Base class for annotations
    /// </summary>
    public abstract class PdfAnnotation : Elements
    {
        #region Variables and properties

        /// <summary>
        /// Whenever to strip the border or not while saving.
        /// </summary>
        /// <remarks>
        /// If this flag is set then it's safe to strip away default
        /// border objects when saving
        /// </remarks>
        private bool _strip_border = false;

        internal sealed override PdfType Type { get { return PdfType.Annotation; } }

        internal override SM DefSaveMode { get { return SM.Indirect; } }

        /// <summary>
        /// The type of annotation
        /// </summary>
        public string Subtype { get { return _elems.GetNameEx("Subtype"); } }

        /// <summary>
        /// The rectangle defining the location of this annotation in
        /// default user space units
        /// </summary>
        public PdfRectangle Rect
        {
            get { return (PdfRectangle)_elems.GetPdfTypeEx("Rect", PdfType.Rectangle); }
            set
            {
                if (value == null)
                    throw new PdfNotSupportedException("Annot must have bounds");
                _elems.SetItem("Rect", value, false);
            }
        }

        /// <summary>
        /// Text that is to be displayed for text annotations, or a description of this
        /// annotations contents
        /// </summary>
        public string Contents
        {
            get { return _elems.GetUnicodeString("Contents"); }
            set { _elems.SetUnicodeString("Contents", value); SetM(); }
        }

        /// <summary>
        /// The page of which this annotation is assosiated
        /// </summary>
        [PdfVersion("1.3")]
        public PdfPage P
        {
            get { return (PdfPage)_elems.GetPdfType("P", PdfType.Page); }
            internal set
            {
                _elems.SetItem("P", value, true);
            }
        }

        /// <summary>
        /// The name of this annotation. Must be unique
        /// </summary>
        [PdfVersion("1.4")]
        public string NM { get { return _elems.GetString("NM"); } }

        /// <summary>
        /// A date or text string for when this annotation was modified
        /// </summary>
        public PdfDate M { get { return _elems.GetDate("M"); } }
        protected void SetM() { _elems.SetItem("M", PdfDate.Now, false); }

        /// <summary>
        /// A set of flag specifying the characteristics of this annotation
        /// </summary>
        [PdfVersion("1.1")]
        public AnnotationFlags F
        {
            get { return (AnnotationFlags)_elems.GetInt("F", 0); }
            set { _elems.SetInt("F", (int)value); }
        }

        /// <summary>
        /// Flags for this annotation
        /// </summary>
        public AFlags Flags { get { return new AFlags(this); } }

        /// <summary>
        /// An appearance dictionary specifying how the annotation shall 
        /// be presented visually on the page
        /// </summary>
        [PdfVersion("1.2")]
        public PdfAppearanceDictionary AP
        {
            get { return (PdfAppearanceDictionary)_elems.GetPdfType("AP", PdfType.AppearanceDictionary); }
            set { _elems.SetItem("AP", value, true); }
        }

        /// <summary>
        /// The annotation’s appearance state, which 
        /// selects the applicable appearance stream from an appearance 
        /// subdictionary
        /// </summary>
        [PdfVersion("1.2")]
        public string AS
        {
            get { return _elems.GetName("AS"); }
            set { _elems.SetName("AS", value); }
        }

        /// <summary>
        /// How the border of the annotaion is to be drawn
        /// </summary>
        public PdfAnnotBorder Border
        {
            get
            {
                var b = (PdfAnnotBorder)_elems.GetPdfType("Border", PdfType.AnnotBorder);
                if (b == null)
                {
                    b = new PdfAnnotBorder(_elems.IsWritable, _elems.Tracker);
                    _strip_border = true;
                    if (IsWritable)
                        _elems.SetItem("Border", b, false);
                }
                return b;
            }
            set
            {
                if (value != null && value.IsDefault)
                    _elems.Remove("Border");
                else
                    _elems.SetItem("Border", value, false);
                _strip_border = false;
            }
        }

        /// <summary>
        /// An array of numbers in the range 0.0 to 1.0, 
        /// representing a colour used for the following purposes: 
        /// 1. The background of the annotation’s icon when closed 
        /// 2. The title bar of the annotation’s pop-up window 
        /// 3. The border of a link annotation
        /// </summary>
        [PdfVersion("1.1")]
        public double[] C
        {
            get
            {
                var r = (RealArray)_elems.GetPdfType("C", PdfType.RealArray);
                if (r == null) return null;
                if (r.Length == 2 || r.Length > 4)
                    throw new PdfReadException(PdfType.ColorSpace, ErrCode.Invalid);
                return r.ToArray();
            }
            set
            {
                if (value != null)
                {
                    if (value.Length > 4 || value.Length == 2)
                        throw new PdfNotSupportedException("Can't have two or more than four values");
                    foreach (var v in value)
                        if (v < 0 || v > 1)
                            throw new PdfOutOfRangeException("Value was outside supported range 0 - 1");
                }
                _elems.SetItem("C", new RealArray(value), false);
            }
        }

        /// <summary>
        /// This is the same property as "C", just using a color object instead
        /// </summary>
        public PdfColor C_Color
        {
            get
            {
                var col = C;
                if (col == null || col.Length == 0) return null;
                if (col.Length == 1)
                    return DeviceGray.Instance.Converter.MakeColor(col);
                if (col.Length == 3)
                    return DeviceRGB.Instance.Converter.MakeColor(col);
                if (col.Length == 4)
                    return DeviceCMYK.Instance.Converter.MakeColor(col);
                return null;
            }
            set
            {
                if (value == null)
                    C = null;
                {
                    if (value.Blue == value.Red && value.Blue == value.Green)
                        C = new double[] { value.Blue };
                    else
                        C = new double[] { value.Red, value.Green, value.Blue };
                }
            }
        }

        /// <summary>
        /// The integer key of the annotation’s entry in the structural parent tree
        /// </summary>
        public int? StructParent
        {
            get
            {
                var r = _elems.GetIntObj("StructParent");
                if (r == null) return null;
                return r.GetInteger();
            }
        }

        /// <summary>
        /// An optional content group or optional content membership dictionary 
        /// </summary>
        public OptionalContent OC
        {
            get
            {
                var oc = _elems["OC"];
                if (oc == null) return null;
                oc = oc.Deref();
                if (oc is OptionalContent)
                    return (OptionalContent)oc;
                if (!(oc is PdfDictionary))
                    throw new PdfCastException(ErrSource.General, PdfType.Dictionary, ErrCode.WrongType);
                var dict = (PdfDictionary)oc;
                if (dict.IsType("OCMD"))
                    return (OptionalContent)_elems.GetPdfType("OC", PdfType.OptionalContentMembership);
                return (OptionalContent)_elems.GetPdfType("OC", PdfType.OptionalContentGroup);
            }
        }

        #endregion

        #region Init

        protected PdfAnnotation(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Write

        internal sealed override void Write(PdfWriter write)
        {
            if (_strip_border && _elems.Contains("Border") && Border.IsDefault)
            {
                var el = _elems.TempClone();
                el.Remove("Border");
                el.Write(write);
            }
            else
                _elems.Write(write);
        }

        #endregion

        #region Helper classes

        public class AFlags
        {
            private PdfAnnotation A;
            /// <summary>
            /// Do not display "annotation missing" graphics when this annotation is
            /// rendered.
            /// </summary>
            public bool Invisible { get { return (A.F & AnnotationFlags.Invisible) != 0; } set { A.F = A.F | AnnotationFlags.Invisible; } }
            /// <summary>
            /// Do not display or print this annotation
            /// </summary>
            public bool Hidden { get { return (A.F & AnnotationFlags.Hidden) != 0; } set { A.F = A.F | AnnotationFlags.Hidden; } }
            /// <summary>
            /// Only print annotations with this flag set
            /// </summary>
            public bool Print { get { return (A.F & AnnotationFlags.Print) != 0; } set { A.F = A.F | AnnotationFlags.Print; } }
            /// <summary>
            /// Do not scale the annotation’s appearance to match the magnification of the page
            /// </summary>
            public bool NoZoom { get { return (A.F & AnnotationFlags.NoZoom) != 0; } set { A.F = A.F | AnnotationFlags.NoZoom; } }
            /// <summary>
            /// Do not rotate the annotation’s appearance to match the rotation of the page
            /// </summary>
            public bool NoRotate { get { return (A.F & AnnotationFlags.NoRotate) != 0; } set { A.F = A.F | AnnotationFlags.NoRotate; } }
            /// <summary>
            /// If set, do not show this annotation on screen (it can still be printed)
            /// </summary>
            public bool NoView { get { return (A.F & AnnotationFlags.NoView) != 0; } set { A.F = A.F | AnnotationFlags.NoView; } }
            /// <summary>
            /// The annotation may not be moved or modified
            /// </summary>
            public bool ReadOnly { get { return (A.F & AnnotationFlags.ReadOnly) != 0; } set { A.F = A.F | AnnotationFlags.ReadOnly; } }
            /// <summary>
            /// The annotation may not be moved or deleted, but its contents can still be modified
            /// </summary>
            public bool Locked { get { return (A.F & AnnotationFlags.Locked) != 0; } set { A.F = A.F | AnnotationFlags.Locked; } }
            /// <summary>
            /// Invert the interpretation of the NoView flag for certain events
            /// </summary>
            public bool ToggleNoView { get { return (A.F & AnnotationFlags.Locked) != 0; } set { A.F = A.F | AnnotationFlags.ToggleNoView; } }
            /// <summary>
            /// The annotation's contents may not be modified
            /// </summary>
            public bool LockedContents { get { return (A.F & AnnotationFlags.LockedContents) != 0; } set { A.F = A.F | AnnotationFlags.LockedContents; } }
            internal AFlags(PdfAnnotation a)
            { A = a; }
        }

        #endregion
    }
}
