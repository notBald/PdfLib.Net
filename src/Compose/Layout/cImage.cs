using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    public class cImage : cBox
    {
        private readonly PdfImage _image;

        /// <summary>
        /// Content is sized to match the box
        /// </summary>
        public bool FitContentToSize { get; set; }

        /// <summary>
        /// Size of the content
        /// </summary>
        public override xSize ContentSize
        {
            get
            {
                return new xSize(_image.Width, _image.Height);
            }
        }

        #region Init

        public cImage(PdfImage image)
            : this(image, new cStyle())
        { }
        public cImage(PdfImage image, cStyle style)
            : base(style)
        {
            if (image == null)
                throw new ArgumentNullException();
            _image = image;
            FitContentToSize = true;
        }

        #endregion

        #region Layout

        /// <summary>
        /// If the size of the content depends on size of the partent.
        /// 
        /// In this case, content need to be layed out when size changes.
        /// </summary>
        internal override bool VariableContentSize { get { return false; } }

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
            double width, height;
            if (FitContentToSize)
            {
                width = VisContentWidth;
                height = VisContentHeight;
            }
            else
            {
                width = _image.Width;
                height = _image.Height;
            }
            draw.Save();
            draw.PrependCM(new xMatrix(width, 0, 0, height, PosX, PosY));
            draw.DrawImage(_image);
            draw.Restore();
        }

        #endregion

        #region Text

        /// <summary>
        /// Images does not feature decent.
        /// </summary>
        internal override double MaxDescent => 0;

        #endregion

        public override string ToString()
        {
            return "<image> { " + base.ToString() + " }";
        }
    }
}
