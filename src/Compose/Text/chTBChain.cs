using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// Compose Horizontal Text Box Chain
    /// 
    /// Allows a document to flow between text boxes
    /// </summary>
    /// <remarks>Under development</remarks>
    public class chTBChain
    {
        #region Variables and properties

        private List<chTextBox> _text_boxes = new List<chTextBox>(3);

        int _selected_textbox = -1;

        cRenderState _inital_state;

        chDocument _doc;

        private chTextBox GetTB() { return _text_boxes[_selected_textbox]; }
        private bool HasTB { get { return _selected_textbox != -1; } }

#if DEBUG
        public chTextBox this[int index] { get { return _text_boxes[index]; } }
#endif

        /// <summary>
        /// Number of textboxes in the chain
        /// </summary>
        public int Count { get { return _text_boxes.Count; } }

        /// <summary>
        /// Width of the selected textbox
        /// </summary>
        public double Width
        {
            get
            {
                if (HasTB)
                    return GetTB().Width;
                return 0;
            }
            set { if (HasTB) GetTB().Width = value; }
        }

        /// <summary>
        /// Height of the selected textbox
        /// </summary>
        public double Height
        {
            get
            {
                if (HasTB)
                    return GetTB().Height;
                return 0;
            }
            set { if (HasTB) GetTB().Height = value; }
        }

        /// <summary>
        /// Height of the selected textbox
        /// </summary>
        public float? BorderTichness
        {
            get
            {
                if (HasTB)
                    return GetTB().BorderTichness;
                return null;
            }
            set { if (HasTB) GetTB().BorderTichness = value; }
        }

        /// <summary>
        /// Padding of the selected textbox
        /// </summary>
        public float? Padding
        {
            get
            {
                if (HasTB)
                    return GetTB().Padding;
                return null;
            }
            set { if (HasTB) GetTB().Padding = value; }
        }

        /// <summary>
        /// Distance between paragraphs. The distance is multiplied
        /// with the lineheight of first line in the next paragraph.
        /// </summary>
        public double ParagraphGap
        {
            get
            {
                if (HasTB)
                    return GetTB().ParagraphGap;
                return 1;
            }
            set { if (HasTB) GetTB().ParagraphGap = value; }
        }

        public bool RightToLeft
        {
            get
            {
                if (HasTB)
                    return GetTB().RightToLeft;
                return false;
            }
            set { if (HasTB) GetTB().RightToLeft = value; }
        }

        /// <summary>
        /// Padding of the selected textbox
        /// </summary>
        public xPoint Position
        {
            get
            {
                if (HasTB)
                    return GetTB().Position;
                return new xPoint();
            }
            set { if (HasTB) GetTB().Position = value; }
        }

        /// <summary>
        /// How the selected textbox handles text that overflows its boundaries.
        /// </summary>
        public chTextBox.Overdraw SelectedOverflow
        {
            get
            {
                if (HasTB)
                    return GetTB().Overflow;
                return chTextBox.Overdraw.Hidden;
            }
            set { if (HasTB) GetTB().Overflow = value; }
        }

        #endregion

        #region Init

        public chTBChain(chDocument doc, cRenderState initial_state)
        {
            if (doc == null || initial_state == null)
                throw new ArgumentNullException();
            _inital_state = initial_state;
            _doc = doc;
        }

        #endregion

        /// <summary>
        /// Adds a textbox
        /// </summary>
        public void AddTextBox()
        {
            _selected_textbox = _text_boxes.Count;
            _text_boxes.Add(new chTextBox(null, null, _inital_state));
        }

        /// <summary>
        /// Adds a textbox
        /// </summary>
        public void AddTextBox(double x, double y, double width, double height)
        {
            _selected_textbox = _text_boxes.Count;
            _text_boxes.Add(new chTextBox(null, width, height, _inital_state));
            Position = new xPoint(x, y);
        }

        /// <summary>
        /// Adds a textbox
        /// </summary>
        public void AddTextBox(double x, double y, double width, double height, float padding)
        {
            _selected_textbox = _text_boxes.Count;
            _text_boxes.Add(new chTextBox(null, null, height - padding * 2, _inital_state));
            Padding = padding;
            Width = width;
            Position = new xPoint(x, y);
        }

        /// <summary>
        /// Adds a textbox
        /// </summary>
        public void AddTextBox(double x, double y, double width, double height, float padding, float border_width)
        {
            _selected_textbox = _text_boxes.Count;
            _text_boxes.Add(new chTextBox(null, null, null, _inital_state));
            Padding = padding;
            BorderTichness = border_width;
            Width = width;
            Height = height;
            Position = new xPoint(x, y);
        }

        public bool Layout()
        {
            var doc = _doc;
            var ir = new xIntRange(0, int.MaxValue);
            foreach (var tb in _text_boxes)
            {
                tb.DocumentRange = ir;
                tb.SetDocument(doc);
                tb.Layout();
                if (tb.LastDisplayedCharacter == -1)
                    //Quick way of preventing following text boxes from displaying the dcument
                    doc = null;
                else
                    //Adjusts the range beyond the text displayed in the last text box
                    ir.Min = tb.LastDisplayedCharacter + 1;
            }

            return doc == null;
        }

        /// <summary>
        /// Renders the text boxes. State will be saved and restored.
        /// </summary>
        /// <param name="draw">Render target</param>
        /// <param name="state">Render state</param>
        public void Render(IDraw draw, ref cRenderState state)
        {
            foreach(var tb in _text_boxes)
                tb.Render(draw, ref state);
        }
    }
}
