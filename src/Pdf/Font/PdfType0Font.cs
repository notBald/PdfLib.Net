using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Render;
using PdfLib.Render.Font;

namespace PdfLib.Pdf.Font
{
    public class PdfType0Font : PdfFont
    {
        #region Variables and properties

        /// <summary>
        /// The name of a predefined CMap, or a stream containing a CMap 
        /// that maps character codes to font numbers and CIDs. If the 
        /// descendant is a Type 2 CIDFont whose associated TrueType font 
        /// program is not embedded in the PDF file, the Encoding entry 
        /// shall be a predefined CMap name
        /// </summary>
        public PdfCmap Encoding { get { return (PdfCmap)_elems.GetPdfTypeEx("Encoding", PdfType.Cmap); } }
        internal override PdfItem FetchEncoding() { return Encoding; }

        /// <summary>
        /// Postscript name of the font.
        /// </summary>
        public string BaseFont { get { return _elems.GetNameEx("BaseFont"); } }

        /// <summary>
        /// Name of this font
        /// </summary>
        public override string FontName
        {
            get
            {
                var pos = BaseFont.IndexOf('+');
                if (pos == -1) return BaseFont;
                return BaseFont.Substring(pos + 1);
            }
        }

        public PdfCIDFont DescendantFonts
        {
            get
            {
                //Only one descendant font is allowed.
                return (PdfCIDFont) _elems.GetArrayEx("DescendantFonts").GetPdfTypeEx(0, PdfType.CIDFont);;
            }
        }

        #endregion

        #region Init

        internal PdfType0Font(PdfDictionary dict) : base(dict) { }

        #endregion

        //This code is a mess. 
        protected override rFont RealizeImpl<Path>(IFontFactory<Path> factory)
        {
            //Implementation detail:
            // DescendantFonts can return a CIDFontType2 with SubType CIDFontType0,
            // however it will never do this for built in fonts.
            // One should perhaps create a unique container for these fonts, to cut
            // down the chance of confusion.
            var fnt = DescendantFonts;

            if (fnt is CIDFontType2 t2)
            {
                if (fnt.BuiltInFont)
                {
                    var afm = AdobeFontMetrixs.Create(fnt.BaseFont, null, true);
                    bool substituted = false;
                    if (afm == null)
                    {
                        var name = fnt.BaseFont;
                        int i = name.IndexOf('+');
                        if (i != -1)
                            name = name.Substring(i + 1);
                        var cf = Compose.cFont.Create(name);

                        //Now create AFM for this font.
                        if (cf != null)
                        {
                            //Doing a blind guess for the encoding
                            afm = new AdobeFontMetrixs(AdobeFontMetrixs.Create(cf, new PdfEncoding(PdfBaseEncoding.WinAnsiEncoding)));
                        }
                        else
                        {
                            afm = AdobeFontMetrixs.Create("Helvetica", null, true);
                        }
                        substituted = true;
                    }

                    var bfnt = factory.CreateBuiltInFont(this, afm, substituted);
                    bfnt.Init();

                    if (ToUnicode == null)
                    {
                        //Todo: Make use of CIDtoGID map. I.e. the Encoding translates to CID, then the
                        //      font may supply a CIDtoGID map to get all the way. 
                        return new rCIDFont(Encoding.CreateMapper(), bfnt);
                    }

                    //How the glyph is found
                    // raw_charcode -> ToUnicode CMAP -> unicode to name -> name to glyph index -> glyph
                    return new rUnicodeFont(ToUnicode.CreateMapper(), bfnt);
                }

                if (fnt.SubType == "CIDFontType0")
                    //Todo: Use IFontFactory
                    return new rCIDFont(Encoding.CreateMapper(), new CIDOTFont<Path>(t2, Encoding.WMode, factory));
                else
                {
                    //Todo: Use IFontFactory
                    var rfnt = new CIDTTFont<Path>(t2, Encoding.WMode, factory);

                    //Todo: Use IFontFactory
                    return new rCIDFont(Encoding.CreateMapper(), rfnt);
                }
            }

            if (fnt is CIDFontType0 t1)
            {
                if (fnt.BuiltInFont)
                {
                    rFont bfnt = null;
                    var afm = AdobeFontMetrixs.Create(fnt.BaseFont, null, true);
                    if (afm == null)
                    {
                        //Debug.Assert(false, "Be cleverer here");

                        if (fnt.CIDSystemInfo.IsCJK)
                        {
                            //Problem: I do not have the requestet font, and it needs CJK characters. Some windows fonts may
                            //have CJK characters, however this does not appear to be the case for "Arial" on WinXP

                            //Solution:
                            //DroidSansFallback.ttf is a apache 2.0 licensed font with CJK characters. Alternativly one
                            //could give a try at having WPF show CJK charackters, but for now it's boxes there.
                            // (Just add || false to the if (fny.CID...) to test

                            //How the characters are mapped:
                            //  1. We start with the raw values.
                            //  2. Then we check how many bytes to read from the raw value using range checking on the font's cmap
                            //  3. We read out the needed number of bytes and converts to a 16-bit integer (CID) using the font's cmap
                            //  4. The CIDs are then converted to 16-BIT BE unicode characters by using the supplied CIDSystemInfo CMap
                            //  5. These unicode characters are then supplied to the font. The font use it's own internal CMap to
                            //     get glyph indexes
                            //  6. The CID (from step 3) is then used to look up the character's width, if widths have been supplied

                            var droid_font = new CIDTTFont<Path>(fnt, Res.StrRes.GetBinResource("Font.Droid.DroidSansFallback.ttf"), Encoding.WMode, factory);

                            //Note that CIDTTFont will close the stream when garbage collected.

                            bfnt = droid_font;
                        }
                        else
                        {
                            //For western fonts we'll go with Helvetica. 
                            afm = AdobeFontMetrixs.CreateHelvetica(fnt.BaseFont, null);
                        }
                    }
                    
                    //Todo: Use IFontFactory (I.e. don't directly touch WPF stuff. This really ruins cairo's day.)
                    if (bfnt == null)
                        bfnt = new CIDWPFFont(BaseFont, afm.SubstituteFont, afm, fnt, Encoding.WMode, factory);
                    

                    //Since we don't have the CFF font this "font" is based on we'll
                    //make use of the Unicode CMap.
                    var UnicodeCMap = fnt.CIDSystemInfo.UnicodeCMap;
                    if (UnicodeCMap == null)
                        throw new NotImplementedException("Must have Unicode CMap for CFF built in fonts");

                    bfnt.Init();
                    return new rCIDFont(Encoding.CreateMapper(UnicodeCMap as PdfPSCmap), bfnt);

                    //throw new NotImplementedException("CIDFontType0");
                }

                //Todo: Use IFontFactory
                var fd = t1.FontDescriptor;
                if (fd.FontFile != null)
                    return new rCIDFont(Encoding.CreateMapper(), new CIDT1Font<Path>(t1, factory, Encoding.WMode));
                else if (fd.FontFile3 != null)
                    //Todo: Use IFontFactory
                    return new rCIDFont(Encoding.CreateMapper(), new CIDT1CFont<Path>(t1, factory, Encoding.WMode));
                else
                {
                    //The only way to get here is if a font has FontFile2, but such fonts should
                    //always be contained in a CIDFontType2 container.
                    throw new PdfInternalException("Should never happen");
                }
            }

            throw new NotImplementedException();
        }

        #region Required overrides

        protected override int IsLike(PdfFont obj)
        {
            var t1 = (PdfType0Font)obj;
            return (int)(( //Todo Equivalent on PdfCmap and DescendantFonts
                PdfFile.Equivalent(Encoding, t1.Encoding) &&
                PdfFile.Equivalent(ToUnicode, t1.ToUnicode) &&
                BaseFont == t1.BaseFont &&
                DescendantFonts.Equivalent(t1.DescendantFonts)
                ) ? Equivalence.Identical : Equivalence.Different);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfType0Font(elems);
        }

        #endregion
    }
}
