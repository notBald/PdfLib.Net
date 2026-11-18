using System;
using System.Collections.Generic;
using System.IO;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Render.Font
{
    public class FontFactory<Path> : IFontFactory<Path>
    {
        /// <summary>
        /// Cache over fonts and glyphs. 
        /// </summary>
        private readonly FontCache _font_cache = new FontCache();

        private readonly IGlyphFactoryCache<Path> _glyph_factory;

        public FontFactory(IGlyphFactoryCache<Path> glyph_factory)
        {
            _glyph_factory = glyph_factory;
        }

        #region IFontFactory

        //RenderEngine IFontFactory.Engine
        //{ get { return RenderEngine.Custom; } }

        IGlyphCache IFontFactory.CreateGlyphCache() => _glyph_factory.CreateGlyphCache();

        FontCache IFontFactory.FontCache { get { return _font_cache; } }

        public void Dispose() 
        {
            _font_cache.Dispose();
        }

        rFont IFontFactory.CreateMemTTFont(PdfLib.Read.TrueType.TableDirectory td, bool symbolic, ushort[] cid_to_gid)
        {
            return new MemTTFont<Path>(td, this, symbolic, cid_to_gid);
        }

        rFont IFontFactory.CreateBuiltInFont(PdfType1Font font, double[] widths, AdobeFontMetrixs afm, bool substituted)
        {
            //if (font.BaseFont.StartsWith("Times"))
            //    widths = widths;
            if (substituted)
            {
                string filename = PdfLib.Util.Registery.FindFontFilename(font.BaseFont, afm.Bold, afm.Italic);
                if (filename != null)
                {
                    var font_folder = rFont.FontFolder + filename;

                    if (File.Exists(font_folder))
                    {
                        var ttf = new MemoryStream(File.ReadAllBytes(font_folder));
                        return rFont.Create<Path>(ttf, widths, font, this);
                    }
                }
            }

            //First check if the font exists as a embeded resource.
            var gn = afm.ResourceFont;
            Stream cff = PdfLib.Res.StrRes.GetBinResource(gn.ResURL);
            if (cff == null && File.Exists(gn.DiskURL))
            {
                //Next we look into the "font" folder in the executing directory
                cff = new MemoryStream(File.ReadAllBytes(gn.DiskURL));
            }
            if (cff == null)
            {
                var font_folder = rFont.FontFolder;

                font_folder += afm.SubstituteFile + ".ttf";
                if (File.Exists(font_folder))
                {
                    cff = new MemoryStream(File.ReadAllBytes(font_folder));

                    return new TTFont<Path>(cff, widths, font, this);
                }
            }
            if (cff == null)
                throw new NotImplementedException();
            var fd = font.FontDescriptor;
            if (fd == null)
                fd = afm.CreateFontDescriptor();

            if (gn.ResourceType == "cff")
                return new Type1CFont<Path>(widths, fd, font.Encoding, this, cff, true);
            else
                //All otf files are cff, for now.
                return new CFFOTFont<Path>(widths, font, this, cff);
        }

        rFont IFontFactory.CreateBuiltInFont(PdfType0Font font, AdobeFontMetrixs afm, bool substituted)
        {
            if (substituted)
            {
                string filename = PdfLib.Util.Registery.FindFontFilename(font.BaseFont, afm.Bold, afm.Italic);
                if (filename != null)
                {
                    var font_folder = rFont.FontFolder + filename;

                    if (File.Exists(font_folder))
                    {
                        var ttf = new MemoryStream(File.ReadAllBytes(font_folder));

                        //The ttf file can be OpenType, CFF, or Truetype. Needs to return the appropriate CID font.
                        return new CIDTTFont<Path>((CIDFontType2)font.DescendantFonts, ttf, false, this);
                    }
                }
            }

            //First check if the font exists as a embeded resource.
            var gn = afm.ResourceFont;
            Stream cff = PdfLib.Res.StrRes.GetBinResource(gn.ResURL);
            if (cff == null && File.Exists(gn.DiskURL))
            {
                //Next we look into the "font" folder in the executing directory
                cff = new MemoryStream(File.ReadAllBytes(gn.DiskURL));
            }
            if (cff == null || afm.PreferTrueType)
            {
                var font_folder = rFont.FontFolder;

                font_folder += afm.SubstituteFile + ".ttf";
                if (File.Exists(font_folder))
                {
                    cff = new MemoryStream(File.ReadAllBytes(font_folder));

                    return new CIDTTFont<Path>((CIDFontType2)font.DescendantFonts, cff, false, this);
                }
            }
            if (cff == null)
                throw new NotImplementedException();

            if (gn.ResourceType == "cff")
                return new CIDT1CFont<Path>((CIDFontType2)font.DescendantFonts, this, font.Encoding.WMode, cff);
            else
                return new CIDOTFont<Path>((CIDFontType2)font.DescendantFonts, font.Encoding.WMode, this, cff);
        }

        /// <summary>
        /// Attach a Matrix transform to the path
        /// </summary>
        void IGlyphFactory<Path>.SetTransform(Path path, xMatrix transform)
        {
            _glyph_factory.SetTransform(path, transform);
        }

        /// <summary>
        /// Creates a path without anything in it
        /// </summary>
        Path IGlyphFactory<Path>.CreateEmptyPath()
        {
            return _glyph_factory.CreateEmptyPath();
        }

        rGlyph IGlyphFactory<Path>.CreateGlyph(Path[] outline, xPoint advance, xPoint origin, xMatrix mt, bool freeze, bool is_space_char)
        {
            return _glyph_factory.CreateGlyph(outline, advance, origin, mt, freeze, is_space_char);
        }

        IGlyph<Path> IGlyphFactory<Path>.GlyphRenderer()
        {
            return _glyph_factory.GlyphRenderer();
        }

        #endregion
    }
}
