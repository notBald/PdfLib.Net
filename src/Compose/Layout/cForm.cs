using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// Encapsulates a PdfForm
    /// </summary>
    public class cForm : cBox
    {
        private readonly PdfForm _form;
        bool _allow_mod = true;

        /// <summary>
        /// Content is sized to match the box
        /// </summary>
        public bool FitContentToSize { get; set; }

        /// <summary>
        /// Allow this object to modify the form object
        /// </summary>
        public bool AllowFormModification
        {
            get { return _allow_mod && _form.IsWritable; }
            set { _allow_mod = value; }
        }

        /// <summary>
        /// Size of the content
        /// </summary>
        public override xSize ContentSize
        {
            get 
            {
                var bbox = _form.BBox;
                var m = _form.Matrix;
                if (!m.IsIdentity)
                    bbox = m.Transform(bbox);
                return new xSize(bbox.Width, bbox.Height);
            }
        }

        internal override double MaxDescent => 0;

        #region Init

        public cForm(PdfForm form)
            : this(form, new cStyle())
        { }
        public cForm( PdfForm form, cStyle style)
            : base(style)
        {
            if (form == null)
                throw new ArgumentNullException();
            _form = form;
            FitContentToSize = true;
        }

        #endregion

        #region Layout

        /// <summary>
        /// If the size of the content depends on size of the partent.
        /// 
        /// In this case, content need to be layed out when size changes.
        /// </summary>
        internal override bool VariableContentSize  { get { return false; }  }

        protected override bool LayoutContent(cBox anchor) { return false; }

        protected override void BlockLayoutChanged(cBox child) { }

        protected override void RemoveChildImpl(cNode child) { }

        #endregion

        #region Position

        protected override void FlowContent(cBox anchor) { }

        #endregion

        #region Render

        protected override void DrawContent(IDraw draw, ref cRenderState state, cBox anchor)
        {
            if (FitContentToSize)
            {
                if (AllowFormModification && _form.Matrix.IsIdentity)
                {
                    var size = ContentSize;
                    if (size.Width == VisContentWidth && size.Height == VisContentHeight)
                        draw.DrawForm(_form);
                    else
                    {
                        _form.Matrix = new xMatrix(VisContentWidth / size.Width, 0, 0, VisContentHeight / size.Height, 0, 0);
                        draw.DrawForm(_form);
                    }
                }
                else
                {
                    var size = ContentSize;
                    if (size.Width == VisContentWidth && size.Height == VisContentHeight)
                        draw.DrawForm(_form);
                    else
                    {
                        state.Save(draw);
                        var mt = new xMatrix(1 / size.Width, 0, 0, 1 / size.Height, 0, 0);
                        var ms = new xMatrix(VisContentWidth, 0, 0, VisContentHeight, 0, 0);
                        draw.PrependCM(mt.Prepend(ms));
                        draw.DrawForm(_form);
                        state = state.Restore(draw);
                    }
                }
            }
            else
                draw.DrawForm(_form);
        }

        #endregion

        public override string ToString()
        {
            return "<form> { " + base.ToString() + " }";
        }
    }
}
