using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib.Pdf.Annotation
{
    [Flags()]
    public enum AnnotationFlags
    {
        None,

        /// <summary>
        /// If set, do not display the annotation if it does not belong to one of the 
        /// standard annotation types and no annotation handler is available. If 
        /// clear, display such an unknown annotation using an appearance 
        /// stream specified by its appearance dictionary, if any
        /// </summary>
        Invisible = 0x1,

        /// <summary>
        /// (PDF 1.2) If set, do not display or print the annotation or allow it to 
        /// interact with the user, regardless of its annotation type or whether an 
        /// annotation handler is available. 
        /// </summary>
        Hidden = 0x2,

        /// <summary>
        /// (PDF 1.2) If set, do not display or print the annotation or allow it to 
        /// interact with the user, regardless of its annotation type or whether an 
        /// annotation handler is available. 
        /// </summary>
        Print = 0x4,

        /// <summary>
        /// (PDF 1.3) If set, do not scale the annotation’s appearance to match the 
        /// magnification of the page. The location of the annotation on the page 
        /// (defined by the upper-left corner of its annotation rectangle) shall 
        /// remain fixed, regardless of the page magnification.
        /// </summary>
        NoZoom = 0x08,

        /// <summary>
        /// (PDF 1.3) If set, do not rotate the annotation’s appearance to match 
        /// the rotation of the page. The upper-left corner of the annotation 
        /// rectangle shall remain in a fixed location on the page, regardless of the 
        /// page rotation.
        /// </summary>
        NoRotate = 0x10,

        /// <summary>
        /// (PDF 1.3) If set, do not display the annotation on the screen or allow it 
        /// to interact with the user. The annotation may be printed (depending on 
        /// the setting of the Print flag) but should be considered hidden for 
        /// purposes of on-screen display and user interaction. 
        /// </summary>
        NoView = 0x20,

        /// <summary>
        /// (PDF 1.3) If set, do not allow the annotation to interact with the user. 
        /// The annotation may be displayed or printed (depending on the settings 
        /// of the NoView and Print flags) but should not respond to mouse clicks 
        /// or change its appearance in response to mouse motions.
        /// 
        /// This flag shall be ignored for widget annotations; its function is 
        /// subsumed by the ReadOnly flag of the associated form field
        /// </summary>
        ReadOnly = 0x40,

        /// <summary>
        /// (PDF 1.4) If set, do not allow the annotation to be deleted or its 
        /// properties (including position and size) to be modified by the user. 
        /// However, this flag does not restrict changes to the annotation’s 
        /// contents, such as the value of a form field. 
        /// </summary>
        Locked = 0x80,

        /// <summary>
        /// (PDF 1.5) If set, invert the interpretation of the NoView flag for certain 
        /// events. 
        /// </summary>
        ToggleNoView = 0x100,

        /// <summary>
        /// (PDF 1.7) If set, do not allow the contents of the annotation to be 
        /// modified by the user. This flag does not restrict deletion of the 
        /// annotation or changes to other annotation properties, such as position 
        /// and size. 
        /// </summary>
        LockedContents = 0x200,
    }
}
