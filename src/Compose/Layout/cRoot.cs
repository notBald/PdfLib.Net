using System;
using PdfLib.Render.PDF;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// This is the visual root of a layout document
    /// </summary>
    /// <remarks>
    /// This class exists primarily to help memory management.
    /// 
    /// The problem is that listeners are needed on the style objects, which again
    /// means that the style objects hold references to its listeners. One potential
    /// workaround is to use weak event listeners, but instead we have cBox only listen
    /// if it has a root or if it's "cRoot".
    /// 
    /// That way you can keep one cRoot, then add/remove children from the tree without
    /// worrying about cleaning up listeners.
    /// 
    /// This isn't a perfect solution, but it's not without advantages. You can create
    /// a complex tree this way, and then add it to a rooted tree. Only then will a
    /// layout pass be done.
    /// </remarks>
    public sealed class cRoot : cDiv
    {
        private int _precision_digits = 3;

        /// <summary>
        /// Number of decimal digits used when calculating the layout tolerance.
        /// Changing this does not require a new layout pass; it only affects
        /// rendering precision.
        /// </summary>
        public int PrecisionDigits
        {
            get { return _precision_digits; }
            set { _precision_digits = value; }
        }

        protected override double MinPrec { get { return 1 / Math.Pow(10, _precision_digits); } }

        public cRoot(cStyle style)
            : base(style)
        {
            Style.AddDefault("Position", ePosition.Relative);
            AddListeners();
        }
        public cRoot()
            : this(new cStyle() { Display = eDisplay.Inline })
        { }

        /// <summary>
        /// Determines the size and position of each block
        /// </summary>
        public void Layout()
        {
            base.Layout(this);
        }

        /// <summary>
        /// Creates a PDF page
        /// </summary>
        public PdfPage MakePage()
        {
            Layout();

            var page = new PdfPage(VisFullWidth, VisFullHeight);
            using (var draw = new DrawPage(page))
            {
                draw.Precision = _precision_digits;

                cRenderState state = new cRenderState();
                Render(draw, ref state, this);
            }
            return page;
        }
    }
}
