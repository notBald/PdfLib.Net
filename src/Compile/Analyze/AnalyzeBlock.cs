using System;
using PdfLib.Pdf;
using PdfLib.Render;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Render.Commands;

namespace PdfLib.Compile.Analyze
{
    internal enum BTYPE
    {
        Unknown,
        SingleImage,
        ClippedSingleImage,
        Form,
        TextBlock
    }

    internal class AnalyzeBlock : RenderCMD
    {
        private bool _save, _pop = false;

        /// <summary>
        /// The commands that makes up the block, except save/restore
        /// </summary>
        /// <remarks>The intent is for this block to be editable, so exposing the internals.</remarks>
        internal RenderCMD[] Children = null;
        
        internal BTYPE BType = BTYPE.Unknown;
        internal object tag;

        /// <summary>
        /// Number of Do image commands
        /// </summary>
        internal int n_imgs = 0;

        /// <summary>
        /// Number of Do form commands
        /// </summary>
        internal int n_forms = 0;

        /// <summary>
        /// Number of "draw shape/clip" commands
        /// </summary>
        internal int n_shapes = 0;

        /// <summary>
        /// Number of BT commands
        /// </summary>
        internal int n_text = 0;

        internal bool Save
        {
            get { return _save; }
            set
            {
                if (_save || Children != null)
                    throw new NotSupportedException();
                _save = value;
            }
        }

        public AnalyzeBlock(bool save, RenderCMD[] children)
        {
            this._save = save;
            this.Children = children;
        }

        internal void Close(RenderCMD[] children, bool close)
        {
            if (Children != null) throw new NotSupportedException();
            Children = children;
            _pop = close;
        }

        internal override CMDType Type { get { return CMDType.Block; } }

        internal override void Execute(IDraw draw)
        {
            if (this.BType == BTYPE.Form)
            {
                var oform = (CompiledForm)tag;
                var cform = new CompiledForm(this.Children, new PdfRectangle(oform.BBox), oform.Matrix, oform.Group);
                draw.DrawForm(cform);
            }
            else if (BType == BTYPE.TextBlock)
            {
                if (this._save)
                    draw.BeginText();

                if (this.Children != null)
                    draw.Execute(this.Children);

                if (this._pop)
                    draw.EndText();
            }
            else
            {
                if (this._save)
                    draw.Save();

                if (this.Children != null)
                    draw.Execute(this.Children);

                if (this._pop)
                    draw.Restore();
            }
        }
    }

    public class AnalyzeCMD_Bounds : RenderCMD
    {
        protected RenderCMD _cmd;
        public readonly xRect Bounds;

        internal override CMDType Type { get { return _cmd.Type; } }

        internal AnalyzeCMD_Bounds(RenderCMD cmd, xRect bounds)
        {
            _cmd = cmd ?? throw new ArgumentNullException();
            Bounds = bounds;
        }

        internal override void Execute(IDraw draw)
        {
            _cmd.Execute(draw);
        }
    }

    public sealed class Text_Bounds : AnalyzeCMD_Bounds
    {
        private readonly AnalyzeBlock _block;
        private readonly AnalyzeTextState State;
        private PdfFont _new_font;

        /// <summary>
        /// The font used for this text, can be null. 
        /// </summary>
        public PdfFont Font { get { return State.OrgFont; } }

        public double[] Positions
        {
            get
            {
                return ((ToUnicode)_cmd).Execute();
            }
        }

        public string[] Text
        {
            get
            {
                //First we check if theres a ToUnicode map for us to use. Todo: Should we special case cWrapFont?
                PdfFont font = Font;
                var cmap = font.ToUnicode;
                if (cmap != null)
                {
                    rCMap map = cmap.CreateMapper();
                    return ((ToUnicode)_cmd).Execute(map);
                }

                //Next we check if the font has a known encoding. Note, symbolic fonts
                //has no encoding
                var enc = font.FetchEncoding();
                if (enc is PdfEncoding)
                {
                    var penc = (PdfEncoding)enc;
                    var names = penc.Differences;
                    int[] map = PdfEncoding.CreateUnicodeEnc(names);
                    return ((ToUnicode)_cmd).Execute(map);
                }

                if (enc is PdfCmap)
                {
                    cmap = (PdfCmap)enc;
                    rCMap map = cmap.CreateMapper();
                    if (map.Unicode)
                        return ((ToUnicode)_cmd).Execute(map);
                }

                //Last resort, ask the font to try translating to unicode.
                var f = rFont;
                if (f == null)
                {
                    //If we got no font, we just grab a default font and hope of the best.
                    f = Compose.cFont.Times.MakeWrapper().Realize();
                    f.Init();
                }
                return ((ToUnicode)_cmd).Execute(f);
            }
        }

        /// <summary>
        /// Creates a render font that gives information about the glyphs
        /// </summary>
        public rFont rFont
        {
            get
            {
                var f = Font;
                if (f == null) return null;
                var r = f.Realize();
                r.Init();

                return r;
            }
        }

        internal Text_Bounds(RenderCMD cmd, xRect bounds, AnalyzeTextState state, AnalyzeBlock block)
            : base(cmd, bounds)
        {
            _block = block;
            State = state;

            //This font belongs to the "renderer", and will be dismissed.
            State.Tf = null;
        }

        /// <summary>
        /// Replaces the underlying rendering command, note after doing this
        /// the other properties of the object will no longer be correct.
        /// </summary>
        /// <param name="cmd">Replacment CMD</param>
        public void ReplaceTxtCMD(RenderCMD cmd)
        {
            if (cmd == null) throw new ArgumentNullException();
            if (cmd.Type != CMDType.Text)
                throw new PdfNotSupportedException("Can't use non text commands in a text section");
            _cmd = cmd;
        }

        public void ReplaceTxtCMD(RenderCMD cmd, PdfFont font)
        {
            ReplaceTxtCMD(cmd);
            _new_font = font;
        }

        internal override void Execute(IDraw draw)
        {
            if (_new_font == null)
                base.Execute(draw);
            else
            {
                draw.SetFont(_new_font, State.Tfs);
                base.Execute(draw);
                draw.SetFont(Font, State.Tfs);
            }
        }
    }

    internal class Do_Analyze_CMD : Do_CMD
    {
        public readonly xSegCollection Bounds;

        public Do_Analyze_CMD(PdfImage img, xSegCollection bounds)
            :base(img)
        {
            Bounds = bounds;
        }
    }
}
