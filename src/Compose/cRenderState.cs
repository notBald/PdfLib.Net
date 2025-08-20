using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Compose.Text;
using PdfLib.Render;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Compose
{
    public class cRenderState
    {
        #region Variables

        #region Internal

        /// <summary>
        /// Current graphic state. (Text mode, page mode, etc)
        /// </summary>
        internal GS GS;

        /// <summary>
        /// The width of lines being drawn
        /// </summary>
        internal double line_width;

        internal cBrush fill_color;
        internal cBrush stroke_color;

        internal xDashStyle? dash_style;

        internal xMatrix CTM;

        #region text

        /// <summary>
        /// If the RenderTarget has a font
        /// </summary>
        internal bool _has_font;

        /// <summary>
        /// Character spacing, i.e the distance between characters
        /// </summary>
        internal double Tc;

        /// <summary>
        /// Word spacing, i.e. the distance between words
        /// </summary>
        internal double Tw;

        /// <summary>
        /// Horizontal scaling
        /// </summary>
        internal double Th;

        /// <summary>
        /// Font
        /// </summary>
        public cFont Tf;

        /// <summary>
        /// Font size
        /// </summary>
        public double Tfs;

        /// <summary>
        /// Text Rise
        /// </summary>
        internal double Tr;

        /// <summary>
        /// Has text rise.
        /// </summary>
        /// <remarks>Note, a state can have text rise without this being set true.</remarks>
        internal bool HasTr;

        /// <summary>
        /// Text leading. 
        /// 
        /// The vertical distance between text baselines.
        /// </summary>
        /// <remarks>
        /// Only used by the T*, ', and " operators
        /// </remarks>
        internal double Tl;

        /// <summary>
        /// Used to determine text rendering mode
        /// </summary>
        internal bool fill;
        internal bool stroke;

        /// <summary>
        /// The text rendering mode can be set explicitly, otherwise
        /// it's infered from fill and stroke
        /// </summary>
        internal xTextRenderMode? render_mode;

        /// <summary>
        /// Text metrix
        /// </summary>
        /// <remarks>Not currently updated or paid head to or updated during 
        /// measurments, but may be usefull to do that for tab handeling</remarks>
        public xMatrix Tm;

        /// <summary>
        /// Text line metrix
        /// </summary>
        public xMatrix Tlm;

        #endregion

        #endregion

        #region Properties

        public double TextRise
        {
            get { return Tr; }
            set
            {
                Tr = value;
                HasTr = true;
            }
        }

        /// <summary>
        /// Current fill color
        /// </summary>
        public cBrush FillColor 
        { 
            get { return /*fill ? fill_color : null*/ fill_color; }
            set
            {
                fill_color = value;
                fill = value != null;
            }
        }

        /// <summary>
        /// Current fill color
        /// </summary>
        public cBrush StrokeColor
        {
            get { return /*stroke ? stroke_color : null*/ stroke_color; }
            set
            {
                stroke_color = value;
                stroke = value != null;
            }
        }

        #endregion

        #region Used by chLine and TextRenderer

        /// <summary>
        /// I've not yet decided how tabs should be calculated,
        /// but this simple scheme works for now.
        /// </summary>
        internal double _tab_stop = 4d;

        /// <summary>
        /// The text rendering direction
        /// </summary>
        public bool RightToLeft = false;

        /// <summary>
        /// Whenever to kern text
        /// </summary>
        public bool Kern = true;

        /// <summary>
        /// Words too long for the sentence is broken up
        /// </summary>
        /// <remarks>
        /// Note, this setting is ignored for the "first word" on a line.
        /// 
        /// Is this a good idea? It makes it tricky to calculate a line's
        /// "minimum" width if you don't want any words broken. 
        /// 
        /// Perhaps change BreakWord and SimpleBreakWord into a enumeration,
        /// with the options "never, break first, simple break word, break word,"
        /// or something to that effect. Maybe make break first a sepperate setting.
        /// </remarks>
        public bool BreakWord { get { return _breakword; } set { _breakword = value; } }
        private bool _breakword = true;

        /// <summary>
        /// Introduced to break words in the same manner html does it. When set, this
        /// oversteers BreakWord, so we set it false to get that accross. Setting it
        /// true again won't do anything. 
        /// 
        /// The simple algo only breaks words containing the '-' character, it also
        /// disables the "break the last word on the line" feature. Currently this
        /// is the only way to disable this feature. 
        /// </summary>
        public bool SimpleBreakWord
        {
            get { return _alt_breakword; }
            set
            {
                _alt_breakword = value;
                if (value && BreakWordCharacter == null)
                {
                    BreakWordCharacter = '-';
                    BreakWord = false;
                }
            }
        }
        private bool _alt_breakword = false;

        /// <summary>
        /// When words are broken, they are split in two halves. 
        /// </summary>
        public bool BreakWordSplit = true;

        /// <summary>
        /// Words smaller than this isn't broken up.
        /// </summary>
        public int BreakWordMinLength = 7;

        /// <summary>
        /// Optional character to us for breaking up words. Note, if BreakWord = false this is ignored.
        /// </summary>
        /// <remarks>
        /// This feature works, but isn't particulary user friendly. When set,
        /// chLine's measure function will make space for the '-' character
        /// when breaking words. Then one must call appropriate non-obvious 
        /// RenderText functions to get the '-' along when rendering the text:
        ///  - chLine.Render(TextRenderer text_renderer, int start, int stop, char append_character)
        ///  - LineHMetrics.Render(TextRenderer text_renderer)
        ///  - LineHMetrics.SetText(TextRenderer tr, int end)*
        ///  - LineHMetrics.SetRtlText(TextRenderer tr, int end)*
        ///    *(SetText is used in conjunction with Render methods that don't explicitly support append_character)
        ///  - LineMetrics.Render(TextRenderer text_render)
        /// All other Render methods will fail to render the append_character
        /// 
        /// Note that the break word character will get the same font and style
        /// proeprties as the word that was broken.
        /// 
        /// Also note, SimpleBreakWord makes use of the BreakWordCharacter to divide words, but
        /// does not ever append it since it never break words lacking this character. 
        /// </remarks>
        internal char? BreakWordCharacter = null;

        #endregion

        #region Public

        /// <summary>
        /// The width of strokes
        /// </summary>
        public double StrokeWidth
        {
            get { return line_width; }
            set { line_width = value; }
        }

        /// <summary>
        /// Font size, default 1
        /// </summary>
        public double FontSize 
        { 
            get { return Tfs; }
            set { Tfs = value; }
        }

        public cFont Font
        {
            get { return Tf; }
            set { Tf = value; }
        }

        /// <summary>
        /// How much to streach the font horizontally, default 100
        /// </summary>
        /// <remarks>
        /// Note that HorizontalScaling is not handled automatically by the TextRenderer.
        /// I.e. if you set a different HorizontalScaling, you must manually set this
        /// scaling before rendering the line(s).
        /// 
        /// Reason: HorizontalScaling is (currently) set on a per line basis. 
        /// </remarks>
        public double HorizontalScaling
        {
            get { return Th*100; }
            set { Th = value/100; }
        }

        /// <summary>
        /// How much space to put between characters (in unscaled text units). Default 0
        /// </summary>
        /// <remarks>
        /// Like HorizontalScaling, only set on a per line basis.
        /// </remarks>
        public double CharacterSpacing
        {
            get { return Tc; }
            set { Tc = value; }
        }

        /// <summary>
        /// How much space to put between words (in unscaled text units). Default 0
        /// </summary>
        /// <remarks>
        /// Like HorizontalScaling, only set on a per line basis.
        /// </remarks>
        public double WordSpacing
        {
            get { return Tw; }
            set { Tw = value; }
        }

        public GS GraphicState { get { return GS; } set { GS = value; } }

        #endregion

        #endregion

        #region Init

        public cRenderState()
        {
            Reset();
        }

        public cRenderState(cFont default_font, double font_size)
            : this(default_font)
        {
            Tfs = font_size;
        }

        public cRenderState(cFont default_font)
            :this()
        {
            Tf = default_font;
            fill_color = cColor.BLACK;
            stroke_color = fill_color;
            this.fill = true;
            this.stroke = false;
        }

        private void Reset()
        {
            //Text
            Tf = null;
            Tfs = 1;
            Th = 1;
            Tr = 0;
            Tc = 0;
            Tl = 0;
            line_width = 1;
            fill_color = cColor.BLACK;
            stroke_color = fill_color;
            Tw = 0;
            fill = true;
            stroke = false;
            render_mode = null;
            CTM = xMatrix.Identity;
            BeginText(); //<-- sets tm and tlm to identity
        }

        public void Set(cRenderState state, IDraw draw)
        {
            if (state.Tf != null)
            {
                if (!state.Tf.Equals(Tf))
                {
                    Tf = state.Tf;
                    Tfs = state.Tfs;
                    draw.SetFont(Tf, Tfs);
                }
            }

            if (!Util.Real.Same(Tfs, state.Tfs))
            {
                Tfs = state.Tfs;
                if (Tf != null)
                    draw.SetFont(Tf, Tfs);
            }

            if (!Util.Real.Same(Th, state.Th))
            {
                Th = state.Th;
                draw.SetHorizontalScaling(Th);
            }

            if (state.HasTr && !Util.Real.Same(Tr, state.Tr))
            {
                Tr = state.Tr;
                draw.SetTextRise(Tr);
            }

            if (!Util.Real.Same(Tc, state.Tc))
            {
                Tc = state.Tc;
                draw.SetCharacterSpacing(Tc);
            }

            //if (!Util.Real.Same(Tl, state.Tl))
            //{
            //    Tl = state.Tl;
            //    draw.SetTextLeading(Tl);
            //}

            if (!Util.Real.Same(Tw, state.Tw))
            {
                Tw = state.Tw;
                draw.SetWordSpacing(Tw);
            }

            if (!Util.Real.Same(line_width, state.line_width))
            {
                line_width = state.line_width;
                draw.StrokeWidth = line_width;
            }

            if (state.fill && state.fill_color != fill_color)
                state.fill_color.SetColor(this, true, draw);

            if (state.stroke && state.stroke_color != stroke_color)
                state.stroke_color.SetColor(this, false, draw);

            if (state.render_mode != null && state.render_mode != render_mode)
            {
                render_mode = state.render_mode;
                draw.SetTextMode(render_mode.Value);
            }

            BreakWordCharacter = state.BreakWordCharacter;
            RightToLeft = state.RightToLeft;
        }

        #endregion

        #region CTM

        internal void PrependCTM(xMatrix m, IDraw draw)
        {
            draw.PrependCM(m);
            CTM = CTM.Prepend(m);
        }

        #endregion

        #region TLM

        public void BeginText()
        {
            Tm = xMatrix.Identity;
            Tlm = xMatrix.Identity;
        }

        public void SetTM(xMatrix m)
        {
            Tm = m;
            Tlm = m;
        }

        public void TranslateTLM()
        {
            TranslateTLM(0, -Tl);
        }

        public void TranslateTLM(double x, double y)
        {
            Tlm = Tlm.TranslatePrepend(x, y);
            Tm = Tlm;
        }

        public void SetTlandTransTLM(double x, double y)
        {
            Tl = -y;
            TranslateTLM(x, y);
        }

        #endregion

        #region Save and restore

        Stack<cRenderState> _stack = new Stack<cRenderState>(1);

        /// <summary>
        /// Saves a copy of this state
        /// </summary>
        public void Save() { _stack.Push(Copy()); }
        internal void Save(IDraw draw) { Save(); draw.Save(); }

        /// <summary>
        /// Returns a earlier copy of this state.
        /// </summary>
        /// <remarks>The state itself is unaffected by this command</remarks>
        public cRenderState Restore() { return _stack.Pop(); }
        public cRenderState Restore(IDraw draw) { draw.Restore(); return Restore(); }

        /// <summary>
        /// Makes a copy of the state
        /// </summary>
        public cRenderState Copy()
        { return (cRenderState)MemberwiseClone(); }

        #endregion

        #region Rendering

        internal void SetStrokeWidth(double w, IDraw draw)
        {
            if (line_width != w)
            {
                line_width = w;
                draw.StrokeWidth = w;
            }
        }

        internal void SetStrokeDashAr(xDashStyle? dash, IDraw draw)
        {
            if (!xDashStyle.Equals(dash, dash_style))
            {
                dash_style = dash;
                if (dash == null)
                    draw.SetStrokeDashAr(new xDashStyle(0, new double[0]));
                else
                    draw.SetStrokeDashAr(dash.Value);
            }
        }

        internal void SetFont(cFont font, double? size, IDraw draw)
        {
            if (font != null)
            {
                if (!font.Equals(Tf))
                {
                    Tf = font;
                    if (size != null)
                        Tfs = size.Value;
                    draw.SetFont(Tf, Tfs);
                }
            }
            else if (size != null)
            {
                if (!Util.Real.Same(Tfs, size.Value))
                {
                    Tfs = size.Value;
                    if (Tf != null)
                        draw.SetFont(Tf, Tfs);
                }
            }
        }


        #endregion

        #region State

        internal void TranslateTLM(IDraw draw, double x, double y)
        {
            draw.TranslateTLM(x, y);
            TranslateTLM(x, y);
        }

        internal void BeginText(IDraw draw)
        {
            draw.BeginText();
            BeginText();
            GS = Render.GS.Text;
        }

        internal void EndText(IDraw draw)
        {
            draw.EndText();
            GS = Render.GS.Page;
        }


        #endregion
    }
}
