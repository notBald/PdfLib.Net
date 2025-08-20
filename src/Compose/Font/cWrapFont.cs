using PdfLib.Render.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Font;
using System.Text;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Wrapper that notifies a compose font when it's time to be
    /// written to disk, so that it can finish up it's encode tables
    /// and such.
    /// </summary>
    sealed class cWrapFont : PdfFont, INotify
    {
        private cFont _cfont;

        #region Only for external use
        //These are not for use by the wrap font. 

        internal rFont RenderFont;
        internal cFont CFont { get { return _cfont; } }
        internal override PdfItem FetchEncoding()
        {
            return _cfont.CompileFont().FetchEncoding();
        }
        public override string FontName => _cfont.Name;

        #endregion

        internal PdfDictionary Elements { get { return _elems; } }

        internal cWrapFont(cFont cfont, PdfDictionary dict)
            : base(dict)
        {
            _cfont = cfont;
        }

        ~cWrapFont()
        {
            _elems.Notify(this, false);
        }

        protected override rFont RealizeImpl<Path>(IFontFactory<Path> factory)
        {
            return new rWrapFont(_cfont, factory);
        }

        protected override int IsLike(PdfFont obj)
        {
            return (int) Equivalence.Different;
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            var wf = new cWrapFont(_cfont, elems);
            elems.Notify(wf, true);
            return wf;
        }

        protected override void DictChanged()
        {
            _elems.Notify(this, true);
        }

        internal override void Write(PdfWriter write)
        {
            base.Write(write);
        }

        public void Notify(NotifyMSG msg)
        {
            if (msg == NotifyMSG.PrepareForSave)
                _cfont.CompileFont(_elems);
        }

        class rWrapFont : rFont
        {
            cFont _font;
            IFontFactory _factory;

            public override bool Vertical { get { return false; } }

            /// <summary>
            /// Minor issue, rFont will create a glyph cache
            /// </summary>
            internal override IFontFactory FontFactory => _factory;

            internal rWrapFont(cFont font, IFontFactory factory)
                : base(null)
            {
                _font = font;
                _factory = factory;
            }

            protected sealed override void InitImpl()
            {
                //To avoid having two caches, we do a no cache init. Note, the cFont have to
                //support this feature, if it dosn't it will be a normal init instead. 
                _font.InitRenderNoCache(_factory);
            }

            protected override rGlyph GetGlyph(int ch, bool is_space)
            {
                return GetGlyph(ch, is_space, _font.GetNBytes((byte) (ch >> 8)));
            }

            private rGlyph GetGlyph(int ch, bool is_space, int n_bytes)
            {
                return _font.GetGlyph(ch, is_space, n_bytes);
            }

            internal override rGlyph[] GetGlyphs(byte[] str)
            {
                if (_font.IsCIDFont)
                {
                    var glyphs = new rGlyph[str.Length];
                    int pos = 0, n_glyphs = 0;
                    while(pos < str.Length)
                    {
                        var b = str[pos];
                        int byte_count = _font.GetNBytes(b);
                        if (pos + byte_count > str.Length)
                            break;

                        int raw_charcode = b;
                        for (int c = 1; c < byte_count; c++)
                            raw_charcode = (ushort)(raw_charcode << 8 | (byte)str[pos + c]);
                        glyphs[n_glyphs++] = GetGlyph(raw_charcode, raw_charcode == 32, byte_count);
                        pos += byte_count;
                    }
                    System.Array.Resize<rGlyph>(ref glyphs, n_glyphs);
                    return glyphs;
                }
                else
                    return base.GetGlyphs(str);
            }

            public override string GetUnicode(byte[] str)
            {
                if (_font.IsCIDFont)
                {
                    var chars = new StringBuilder(str.Length);
                    int pos = 0;
                    while (pos < str.Length)
                    {
                        var b = str[pos];
                        int byte_count = _font.GetNBytes(b);
                        if (pos + byte_count > str.Length)
                            break;

                        int raw_charcode = b;
                        for (int c = 1; c < byte_count; c++)
                            raw_charcode = (ushort)(raw_charcode << 8 | (byte)str[pos + c]);
                        chars.Append((char) raw_charcode);
                        pos += byte_count;
                    }
                    return chars.ToString();
                }
                return ASCIIEncoding.ASCII.GetString(str);
            }

            public override void Dismiss()
            {
                _font.DissmissRender();
            }

            public override void DisposeImpl()
            {
                _font.DisposeRender();
            }
        }
    }
}
