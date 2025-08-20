using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Write.Internal;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Font;

namespace PdfLib.Pdf
{
    /// <summary>
    /// Resource dictionary for PDf files
    /// </summary>
    /// <remarks>
    /// When PDF content streams need contents, the commands do so by
    /// refering to a resource dictionary. This way one do not have to
    /// know the id of a resource one use in a content stream, one 
    /// instead use a name and make a lookup in the resource dictionary
    /// 
    /// This makes the save rutine eaiser, as IDs do not have to be kept
    /// constant. PdfLib will renumber resources as it feels like, and
    /// this is the reason it can get away with doing that.
    /// 
    /// ------
    /// 
    /// PdfResources inherit from EventDictionary instead of Elements.
    /// The reason for this is because PdfResources is a inherited
    /// resource.
    /// 
    /// This means that a PdfPage object can seemingly have a res 
    /// dictionary, but in reality it doesn't. What it does instead
    /// is create a quick read only copy of the inherited dictionay.
    /// 
    /// Any changes made to the parent will be reflected in this read
    /// only copy until which point a user makes a change to the
    /// read only copy.
    /// 
    /// At that point the readonly copy will be cloned into a 
    /// writeable copy that now belongs to the page. 
    /// 
    /// ------
    /// </remarks>
    public sealed class PdfResources : Elements
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Resources
        /// </summary>
        internal override PdfType Type { get { return PdfType.Resources; } }

        /// <summary>
        /// If this dictionary has been modified, does not check for default objects.
        /// </summary>
        internal bool Modified { get { return _elems.Count != 0; } }

        /// <summary>
        /// A dictionary that maps font resource names to external objects
        /// </summary>
        public FontElms Font
        {
            get
            {
                return (FontElms) _elems.GetPdfType("Font", PdfType.FontElms, PdfType.Dictionary, true, 
                    IntMsg.NoMessage, null);
            }
        }

        public PdfColorSpaceElms ColorSpace
        {
            get
            {
                return (PdfColorSpaceElms)_elems.GetPdfType("ColorSpace", PdfType.ColorSpaceElms, PdfType.Dictionary, true,
                    IntMsg.NoMessage, null);
            }
        }

        public PatternElms Pattern
        {
            get
            {
                return (PatternElms)_elems.GetPdfType("Pattern", PdfType.PatternElms, PdfType.Dictionary, true,
                    IntMsg.NoMessage, null);
            }
        }

        [PdfVersion("1.3")]
        public ShadingElms Shading
        {
            get
            {
                return (ShadingElms)_elems.GetPdfType("Shading", PdfType.ShadingElms, PdfType.Dictionary, true,
                    IntMsg.NoMessage, null);
            }
        }

        //Todo: Don't have any PdfProperty class yet so keeping this a PdfDict for now.
        public PdfDictionary Properties
        {
            get 
            {
                return _elems.GetDictionary("Properties");
            }
        }

        /// <summary>
        /// Maps resource names to graphics state parameter dictionaries
        /// </summary>
        public GStateElms ExtGState
        {
            get
            {
                var ret = (GStateElms)_elems.GetPdfType("ExtGState", PdfType.GStateElms, IntMsg.NoMessage, null);
                if (ret == null)
                {
                    if (IsWritable)
                    {
                        //Creates a writable empty GS dictionary
                        var track = _elems.Tracker;
                        if (track != null)
                            ret = new GStateElms(new WritableDictionary(track));
                        else
                            ret = new GStateElms();

                        //We can use new item as we know we're not overwriting anything.
                        //Also, setting as reference since def savemode is auto
                        _elems.SetNewItem("ExtGState", ret, true);
                    }
                    else
                        //Creates a non-writable empty dictionary
                        ret = new GStateElms(new SealedDictionary());
                }
                return ret;
            }
        }

        /// <summary>
        /// A dictionary that maps XObject resource names to external objects
        /// </summary>
        public XObjectElms XObject 
        { 
            get 
            { 
                var ret = (XObjectElms)_elems.GetPdfType("XObject", PdfType.XObjectElms, IntMsg.NoMessage, null);
                if (ret == null)
                {
                    if (IsWritable)
                    {
                        var track = _elems.Tracker;
                        if (track != null)
                        {
                            //The reference to "this" is used to cast XObjects, but
                            //since there are no XObjects it could be set null.
                            ret = new XObjectElms(new WritableDictionary(track));
                        }
                        else
                        {
                            //Returns an empty container to avoid null pointer exceptions.
                            ret = new XObjectElms();
                        }
                        _elems.SetNewItem("XObject", ret, true);
                    }
                    else
                        //Returns an empty container to avoid null pointer exceptions.
                        ret = new XObjectElms(new SealedDictionary());
                }

                return ret;
            } 
        }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        internal PdfResources(PdfDictionary dict)
            : base(dict)
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        internal PdfResources()
            : this(new TemporaryDictionary())
        { }

        #endregion

        /// <summary>
        /// Used when moving the element to another document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfResources(elems);
        }

        /// <summary>
        /// PdfResources prunes away empty dictionaries before saving
        /// </summary>
        internal override void Write(PdfWriter write)
        {
            RemoveEmptyDicts();
            base.Write(write);
        }

        private void RemoveEmptyDicts()
        {
            if (_elems is WritableDictionary)
            {
                List<string> to_remove = new List<string>(8);
                foreach (var kp in _elems)
                {
                    if (kp.Value is WritableDictionary &&
                        ((WritableDictionary)kp.Value).Count == 0)
                        to_remove.Add(kp.Key);
                }
                for (int c = 0; c < to_remove.Count; c++)
                    _elems.Remove(to_remove[c]);
            }
        }
    }
}
