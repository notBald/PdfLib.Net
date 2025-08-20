using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Read.TrueType;
using PdfLib.Pdf.Font;
using PdfLib.Render.Font;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Filter;

namespace PdfLib.Compose.Font
{
    /// <summary>
    /// Creates a True Type or Type 0 font
    /// </summary>
    /// <remarks>
    /// There are currently three stratergies for font creating in play.
    ///  (Note that the compose TTFont class does most of the work)
    /// 
    /// 1. WinANSI
    /// 
    /// Characters are encoded to win ansi format when written into the file.
    /// The font's encoding table is set to WinANSI, and a unicode cmap is
    /// included inside the embeded font.
    /// 
    /// When a PDF reader then renders the font it will convert the ansi encoded
    /// characters to unicode and use the included unicode cmap to get the glyph
    /// 
    /// When a PDF reader copies text it too converts the ansi encoded characters
    /// to unicode, then places them straight on the clipboard.
    /// 
    /// This gives me some 220 commonly used characters that should cover most
    /// text needs. The encoding is compact and little space is wasted.
    /// 
    /// While not done by this implementation it's possible to extend the WinANSI 
    /// table to handle other characters. This could potentially render the
    /// "Single byte CID" approach redundant, 
    /// 
    /// 2. Single byte CID
    /// 
    /// If a character that's not represented in the WinANSI table needs to be
    /// encoded the current solution is to switch over into CID mode. This bloats
    /// the filesize quite a bit, as PdfLib includes two PS CMaps. To get back a
    /// little of that space a single byte CID encoding is used when there are
    /// less than 96 different characters to encode.
    /// 
    /// To keep the text readable in the source file the encoder tries to preserve
    /// the original character code, but will alter character codes as need arises.
    /// 
    /// The 96 limit comes from keeping within the ASCII range while excluding
    /// control characters.
    /// 
    /// In single byte mode characters are encoded into a range 32-127 when written
    /// to the file.
    /// 
    /// When then a PDF reader renders the text it will make use of a supplied PS CMap
    /// that maps from the char codes into gid values. No other character maps are used,
    /// the glyphs are fetched directly from the font.
    /// 
    /// For copying text a reader instead makes use of a supplied "to unicode" PS CMap.
    /// Instead of mapping to gid values it maps characters to unicode values.
    /// 
    /// Note that no cmap is supplied in the embeded font, as CID fonts have no need
    /// for it. 
    /// 
    /// 3. Multi byte CID
    /// 
    /// This method can map up to 65000 characters. Like with single byte CIDs it
    /// supplies two PS cmaps to facilitate rendering and text extraction.
    /// 
    /// When written to a file strings are converted into a BE unicode string, which
    /// means they can be read in a text editor.
    /// 
    /// Rendering happens like with single byte cids, the reader use a supplied cmap
    /// to map to gids. However one could also supply a CIDtoGID map here and forgo
    /// the cmap (set it as Identity-H), this should reduce the filesize with no loss 
    /// in functionality (this as PostScript cmaps are pretty bloated).
    /// 
    /// For text extraction a simple "to_unicode" PS cmap is supplied. It's basically
    /// a identity cmap, as the original values are already in unicode.
    /// 
    /// For small files this method will probably result in a smaller size than the
    /// single byte CID, this since the PS CMaps are smaller.
    /// </remarks>
    class CreateTTFont
    {
        #region Variables and properties

        static uint CAP_NAME = 0;

        #endregion

        #region Init

        public static PdfFont Create(cTTFont font, bool cid_font)
        {
            var dict = new TemporaryDictionary();
            Create(font, cid_font, dict);
            return PdfFont.Create(dict);
        }

        internal static void Create(cTTFont font, bool cid_font, PdfDictionary dict)
        {
            #region Sets the name

            if (!dict.Contains("Type"))
                dict.SetType("Font");

            var td = font.GetTD();
            var name = td.Name.Postscript;
            if (name == null)
                name = "Unkown";

            //The name must have 6 capital letters appened.
            //What capital letters are arbitrary, but they need
            //to be unique to the document.
            dict.SetName("BaseFont", CreateCapName(name));

            #endregion

            if (cid_font)
            {
                if (font.IsSymbolic)
                    throw new NotImplementedException("Symbolic CID fons is not yet supported");

                dict.SetName("Subtype", "Type0");

                //Embeded fonts can use a identity collection
                dict.SetItem("CIDSystemInfo", new PdfCIDSystemInfo("Adobe", "Identity", 0), false);

                //Note this also sets up the unicode->charcode mapping inside the font
                PdfCmap to_cid, to_unicode;
                var widths = font.CreateCMaps(out to_cid, out to_unicode);

                dict.SetItem("Encoding", to_cid, true);
                dict.SetItem("ToUnicode", to_unicode, true);

                //Now we build up the descendant font.
                var dfont = new TemporaryDictionary();
                dfont.SetType("Font");
                dfont.SetName("Subtype", "CIDFontType2");
                dfont.SetName("BaseFont", dict.GetName("BaseFont"));
                dfont.SetItem("CIDSystemInfo", dict["CIDSystemInfo"], false);
                GenerateFontDescriptor(dfont, font);
                dfont.SetItem("W", widths, false);
                var ta = new TemporaryArray();
                ta.AddItem(new CIDFontType2(dfont), true);
                dict.SetItem("DescendantFonts", ta, false);
            }
            else
            {
                dict.SetName("Subtype", "TrueType");

                //As long as this is a non-symbolic non-CID font we use plain ANSI encoding
                if (!font.IsSymbolic)
                    dict.SetItem("Encoding", new PdfEncoding("WinAnsiEncoding"), true);

                //Sets the width information
                var da = font.Widths;
                var widths = new PdfItem[da.Length];
                if (font.Precision == 0)
                {
                    for (int c = 0; c < da.Length; c++)
                        widths[c] = new PdfInt((int)(da[c] * 1000));
                }
                else
                {
                    var mul = Math.Pow(10, font.Precision);
                    for (int c = 0; c < da.Length; c++)
                        widths[c] = new PdfReal(Math.Truncate(da[c] * mul * 1000) / mul);
                }
                dict.SetItem("Widths", new RealArray(widths), false);
                dict.SetInt("FirstChar", font.FirstChar);
                dict.SetInt("LastChar", font.LastChar);

                GenerateFontDescriptor(dict, font);
            }
        }

        #endregion

        static void GenerateFontDescriptor(PdfDictionary elems, cTTFont font)
        {
            #region Sets the name

            var wd = new TemporaryDictionary();
            wd.SetType("FontDescriptor");
            var td = font.GetTD();

            wd.SetName("FontName", elems.GetNameEx("BaseFont"));

            #endregion

            #region Sets the flags

            //todo
            //Currently the font is assumed to be non-symbolic. This because we make
            //use of the 3.1 cmap table. For symbolic fonts we must make use of
            //the 3.0 cmap table.
            FontFlags flags = font.IsSymbolic ? FontFlags.Symbolic : FontFlags.Nonsymbolic;

            var os2 = td.OS2;
            if (os2 != null)
            {
                if (os2.PANOSE_Proportion == PANOSE_Proportion.Monospaced)
                    flags |= FontFlags.FixedPitch;
            }

            //todo: Italic and other such info should be set in the flags

            wd.SetInt("Flags", (int) flags);

            #endregion

            #region Sets the font box

            var head = td.Head;
            double units_per_em = head.UnitsPerEm;
            double XMax = head.xMax / units_per_em * 1000d;
            double XMin = head.xMin / units_per_em * 1000d;
            double YMax = head.yMax / units_per_em * 1000d;
            double YMin = head.yMin / units_per_em * 1000d;
            wd.SetNewItem("FontBBox", new PdfRectangle((int) XMin, (int) YMin, (int) XMax, (int) YMax), false);
            
            #endregion

            #region Sets the italic angle

            var post = td.Post;
            var italic_angle = (post != null) ? post.ItalicAngle : 0d;
            wd.SetReal("ItalicAngle", italic_angle);

            #endregion

            #region Sets Asccent to StemV

            wd.SetInt("Ascent",(int) (font.Ascent * 1000));
            wd.SetReal("Descent", (int) (font.Descent * 1000));
            wd.SetReal("CapHeight", (int) (font.CapHeight * 1000));
            
            //Todo:
            //I've not found any trivial way to get this value. One will have to
            //build each glyph, find the dominant vertical stems, and measure their 
            //width. I assume characters such as A, S and C can be ignored, and one
            //can probably fudge it by looking for parallel vertical lines that go
            //for more than X % of the font.
            wd.SetInt("StemV", 0);

            #endregion

            #region Font File

            //Now comes the font file.
            using (var ms = new MemoryStream(1000000))
            {
                font.WriteTo(ms);
                var font_data = ms.ToArray();
                //File.WriteAllBytes("C:/temp/font.ttf", font_data);

                var ws = new WritableStream(font_data);
                ws.Elements.SetInt("Length1", font_data.Length);
                wd.SetItem("FontFile2", ws, true);
            }

            #endregion

            elems.SetItem("FontDescriptor", new PdfFontDescriptor(wd), true);
            //var test = ((PdfFontDescriptor)_elems["FontDescriptor"].Deref()).FontFile2.Length;
        }

        static string CreateCapName(string name)
        {
            byte[] buff = new byte[4];
            WriteFont.Write(CAP_NAME++, buff, 0);
            var sb = new StringBuilder(7+name.Length);
            sb.Append("PL");
            for (int c = 0; c < buff.Length; c++)
                sb.Append((char)(buff[c] + (int)'A'));
            sb.Append('+');
            sb.Append(name);
            return sb.ToString();
        }

    }

    /// <summary>
    /// Wraps a CIDFontType2
    /// </summary>
    class WrapTTCIDFont : PdfCIDFont
    {
        #region Variables



        #endregion

        #region Init

        private WrapTTCIDFont(PdfDictionary dict)
            : base(dict)
        { }

        public static WrapTTCIDFont Create(PdfFontDescriptor fd)
        {
            var td = new TemporaryDictionary();
            td.SetType("Font");
            td.SetName("Subtype", "CIDFontType2");
            td.SetItem("FontDescriptor", fd, true);

            return new WrapTTCIDFont(td);
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
