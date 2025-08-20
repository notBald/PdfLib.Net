using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Compose.Text;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// A text box that works in a similar style to "textarea" in html. If you need 
    /// more control over the text, use chTextBox. 
    /// </summary>
    /// <remarks>
    /// To get wordwrap working you got to:
    ///  1. Sett Style.WhiteSpace = pre-wrap
    ///  2. Sett width to either a number or %
    ///  3. Sett display on the cTextArea's style to Block
    /// This seems a bit convoluted, but it's like this so that you can use TextArea
    /// in place of "input" fields. 
    ///
    /// Weaknesses:
    ///  Only "pre" like text is supported.
    ///  Can't flow text outside it's size rectangle.
    /// 
    /// Use cText for situations where the above is a problem.
    /// </remarks>
    public class cTextArea : cBox
    {
        private chTextLayout _text = new chTextLayout(false);
        private bool /*_text_wrap = false,*/ _stop_event = false;
        private chParagraph.Alignement _align = chParagraph.Alignement.Left;

        /// <summary>
        /// Gap between paragraphs
        /// </summary>
        public double ParagraphGap
        {
            get { return _text.ParagraphGap; }
            set { _text.ParagraphGap = value; }
        }

        /// <summary>
        /// Whenever to wrap text or not
        /// </summary>
        public bool WrapText
        {
            get { return Style.WhiteSpace == eWhiteSpace.pre_wrap; }
            /*set
            {
                if (value != WrapText)
                {
                    Style.WhiteSpace = value ? eWhiteSpace.pre_wrap : eWhiteSpace.pre;
                    DoLayoutContent = true;
                }
            }*/
        }
                
        internal override bool VariableContentSize
        {
            get { return WrapText; }
        }

        public override xSize ContentSize
        {
            get { return new xSize(_text.Width, _text.Height); }
        }

        internal override bool ContainsText { get { return true; } }

        /// <summary>
        /// cTextArea functions like an "input" when there's no word wrapping,
        /// and a textarea when there is wordwrapping.
        /// </summary>
        internal override double MaxDescent => WrapText ? 0 : _text.LastLineDescent;

        #region Init

        /// <summary>
        /// Creates a text box without text. Note, text will have zero height.
        /// </summary>
        public cTextArea()
            : this(new cStyle(), null)
        { }

        /// <summary>
        /// Creates a textbox with text
        /// </summary>
        /// <param name="text">Text to display, note that an empty string will have the height of the default font</param>
        public cTextArea(string text)
            : this(new cStyle(), text)
        { Style.AddDefault("Display", eDisplay.Inline); }

        /// <summary>
        /// Creates a textbox with text and style
        /// </summary>
        /// <param name="text">Text to display, note that an empty string will have the height of the default font</param>
        /// <param name="text">How to style this box</param>
        public cTextArea(cStyle style, string text)
            : base(style)
        {
            _text.LayoutChanged += new chTextLayout.LayoutChangedFunc(_text_LayoutChanged);
            SetText(text);
        }

        private void SetText(string text)
        {
            if (text == null)
                _text.Document = null;
            else
            {
                text = text.Replace("\r\n", "\n");
                var paragraphs = text.Split(new string[] { "\n\n" }, StringSplitOptions.None);
                var doc = new chDocument();
                foreach (var par in paragraphs)
                    doc.AddParagraph(par);
                _text.Document = doc;
            }
        }

        #endregion

        protected override bool LayoutContent(cBox anchor)
        {
            _stop_event = true;
            var s = Style;
            _text.DefaultFontSize = _FontSize;
            _text.DefaultFont = _Font;
            _text.DefaultLineHeight = s.LineHeight;
            _text.CharacterSpacing = s.TextSpacing.CharacterSpacing;
            _text.WordSpacing = s.TextSpacing.WordSpacing;
            _text.HorizontalScale = s.TextSpacing.HorizontalScale;
            _text.TextRise = s.TextSpacing.TextRise;

            if (WrapText && HasWidth)
                _text.Width = VisContentWidth;
            else
                _text.RemoveWidth();
            _text.Layout();

            _stop_event = false;
            return true;
        }

        protected override void FlowContent(cBox anchor)
        {
            //Makes sure all paragraphs has the same alignement
            var align = (chParagraph.Alignement) Style.HorizontalAlignemt;
            if (_align != align)
            {
                _align = align;
                var doc = _text.Document;
                if (doc != null)
                {
                    foreach (var par in doc)
                        par.Alignment = _align;
                }
            }

            //Positions the text inside the box.
            //The basic problem is that text is PDF is draw downwards, while everything else is
            //drawn uppwards. This means the drawing point is at the bottom. The first line of
            //the text is still drawn above the drawing point, though.

            _text.OffsetX = VisBorderLeft + VisPaddingLeft;
            _text.OffsetY = VisBorderBottom + VisPaddingBottom
                //Must compensate for the fact that the first line is drawn above the position,
                //and all the next are drawn below the position.
                + (VisContentHeight - _text.GetLineHeight(0, 0));
            
            switch(Style.TextAlignement)
            {
                //We move the baseline up so that the text fit inside the box. 
                case eTextAlignement.Descent:
                    _text.OffsetY -= _text.LastLineDescent;
                    break;

                case eTextAlignement.Middle:
                    _text.OffsetY += (_text.FreeSpaceAbove - _text.LastLineDescent) / 2;
                    break;

                case eTextAlignement.Center:
                    _text.OffsetY += _text.FreeSpaceAbove / 2;
                    break;

                case eTextAlignement.Baseline:
                    //Already on baseline
                    break;
            }

            switch (Style.HorizontalAlignemt)
            {
                case eHorizontalPos.Right:
                    _text.OffsetX += VisContentWidth - _text.Width;
                    break;
                case eHorizontalPos.Center:
                    _text.OffsetX += (VisContentWidth - _text.Width) / 2;
                    break;
                //Note sure if anything needs doing in this case.
                //case eHorizontalPos.Column:
                  //  break;
            }

            switch (Style.VerticalAlignemt)
            {
                case eVerticalPos.Even:
                    throw new NotImplementedException("Must change line height or add line spacing somehow");
                case eVerticalPos.Center:
                    _text.OffsetY -= (VisContentHeight - _text.Height) / 2;
                    break;
                case eVerticalPos.Bottom:
                    _text.OffsetY -= (VisContentHeight - _text.Height);
                    break;
            }
        }

        protected override void DrawContent(IDraw draw, ref cRenderState state, cBox anchor)
        {
            _text.Render(draw, ref state);
        }

        protected override void StyleChanged(cStyle.ChangeType ct, string property)
        {
            if (ct == cStyle.ChangeType.Apperance && property == "Color")
                _text.DefaultFontColor = Style.Color ?? cColor.BLACK;
        }

        internal override bool AddListeners()
        {
            if (base.AddListeners())
            {
                _text.DefaultFontColor = Style.Color ?? cColor.BLACK;
                return true;
            }
            return false;
        }

        void _text_LayoutChanged()
        {
            if (!_stop_event)
                DoLayoutContent = true;
        }

        #region Required overrides

        protected override void BlockLayoutChanged(cBox child)
        {

        }

        protected override void RemoveChildImpl(cNode child)
        {
            
        }

        #endregion
    }
}
