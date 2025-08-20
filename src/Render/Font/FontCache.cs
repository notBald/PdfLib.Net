using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using PdfLib.Pdf.Font;
using PdfLib.Compile;

namespace PdfLib.Render.Font
{
    /// <summary>
    /// A cache for font caching, for use during rendering.
    /// </summary>
    /// <remarks>
    /// Font caching is something the user of the libary should not have to worry about. To this end,
    /// the font cache is a little sneaky. Some objects hold hidden references to the font cache,
    /// which is then used during rendering.
    /// </remarks>
    public class FontCache : IDisposable
    {
        private Dictionary<string, FCache[]> _fonts = new Dictionary<string, FCache[]>(16);

        internal bool TryGetValue(PdfFont font, out rFont rfont)
        {
            lock(_fonts)
            {
                FCache[] f;
                if (_fonts.TryGetValue(font.FontName, out f))
                {
                    foreach (FCache rf in f)
                    {
                        if (rf.ID == font) 
                        {
                            rfont = rf.Font;
                            return true;
                        }
                    }
                }

                rfont = null;
                return false;
            }
        }

        internal void Add(PdfFont font, rFont rfont)
        {
            lock (_fonts)
            {
                FCache[] f;
                if (_fonts.TryGetValue(font.FontName, out f))
                {
                    for (int c = 0; c < f.Length; c++)
                    {
                        if (f[c].ID == font)
                        {
                            f[c].Font = rfont;
                            return;
                        }
                    }

                    Array.Resize<FCache>(ref f, f.Length + 1);
                    f[f.Length - 1] = new FCache(font, rfont);
                    _fonts[font.FontName] = f;
                }
                else
                {
                    _fonts.Add(font.FontName, new FCache[] { new FCache(font, rfont) });
                }
            }
        }

        private struct FCache
        {
            public readonly object ID;
            public rFont Font;

            public FCache(object id, rFont font)
            {
                ID = id;
                Font = font;
            }
        }

        public void Dispose()
        {
            foreach(var kp in _fonts)
            {
                foreach (FCache fcache in kp.Value)
                {
                    fcache.Font.Dispose();
                }
            }
        }
    }


    //public class GlyphCache : ConcurrentDictionary<int, object>
    //{
    //    public GlyphCache(int size) : base(1, size) { }
    //}
}
