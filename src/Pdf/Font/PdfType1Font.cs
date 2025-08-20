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
    /// <summary>
    /// Handles Type 1 and Type1C and TrueType. Both built in and embeded.
    /// </summary>
    public sealed class PdfType1Font : PdfFont
    {
        #region Variables and properties

        public PdfEncoding Encoding
        { get { return (PdfEncoding)_elems.GetPdfType("Encoding", PdfType.Encoding); } }
        internal override PdfItem FetchEncoding() { return Encoding; }

        public PdfFontDescriptor FontDescriptor
        { get { return (PdfFontDescriptor)_elems.GetPdfType("FontDescriptor", PdfType.FontDescriptor); } }

        /// <summary>
        /// First character in the width array
        /// </summary>
        public int FirstChar { get { return _elems.GetUInt("FirstChar", 0); } }

        /// <summary>
        /// Last character in the width array
        /// </summary>
        public int LastChar { get { return _elems.GetUInt("LastChar", 255); } }

        /// <summary>
        /// Postscript name of the font.
        /// </summary>
        public string BaseFont { get { return _elems.GetNameEx("BaseFont"); } set { _elems.SetName("BaseFont", value); } }

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

        /// <summary>
        /// Width if each character
        /// </summary>
        public double[] Widths
        {
            get
            {
                var ra = (IRealArray)_elems.GetPdfType("Widths", PdfType.RealArray);
                if (ra == null) return null;
                var da = ra.ToArray();
                if (da.Length >= 256) return da;
                var ret = new double[256];
                var fd = FontDescriptor;
                var missing_width = (fd != null) ? fd.MissingWidth : 0;
                var first = FirstChar;
                for (int c = 0; c < first; c++)
                    ret[c] = missing_width;
                Array.Copy(da, 0, ret, first, da.Length);
                for (int c = da.Length + first; c < 256; c++)
                    ret[c] = missing_width;
                return ret;
            }
        }

        /// <summary>
        /// If this is an embeded or built in font
        /// </summary>
        public bool BuiltInFont
        {
            get
            {
                //Temp code. Haven't got font desc yet.
                var fd = FontDescriptor;
                if (fd == null) return true;
                return fd.BuiltInFont;
            }
        }

        #endregion

        #region Init

        internal PdfType1Font(PdfDictionary dict) : base(dict) { }

        #endregion

        // Encodings start with a base encoding, which can come from
        // (in order of priority):
        //   1. FontDict.Encoding.BaseEncoding
        //        - MacRoman / MacExpert / WinAnsi / Standard
        //   2. default:
        //        - builtin --> builtin encoding
        //        - TrueType --> WinAnsiEncoding
        //        - others --> StandardEncoding
        // and then add a list of differences (if any) from
        // FontDict.Encoding.Differences.

        /// <summary>
        /// Before a font can be rendered, or font
        /// data can be extracted, one have to realize
        /// the font.
        /// </summary>
        protected override rFont RealizeImpl<Path>(IFontFactory<Path> factory)
        {
            rFont font;
            var fd = FontDescriptor;
            if (fd == null || fd.BuiltInFont)
            {
                var afm = AdobeFontMetrixs.Create(BaseFont, Encoding, false);
                var w = Widths;

                if (afm == null)
                {
                    //This is a font that's not embeded into the PDF document, and neither part of the
                    //PDF specs. One need to use heruclistics here.
                    var fnt = AdobeFontMetrixs.NameToFont(BaseFont);
                    var disk_font = Util.Registery.FindFontFilename(fnt.Name, fnt.Bold, fnt.Italic);
                //load_from_disk:
                    if (disk_font != null)
                        disk_font = rFont.FontFolder + disk_font;
                    font = null;
                    if (System.IO.File.Exists(disk_font))
                    {
                        try
                        {
                            var font_file = System.IO.File.OpenRead(disk_font);
                            if (w == null)
                            {
                                if (afm == null)
                                {
                                    afm = new AdobeFontMetrixs(AdobeFontMetrixs.Create(Compose.cFont.Create(font_file), Encoding));
                                    font_file.Position = 0;
                                }
                                w = afm.GetWidths(FirstChar, LastChar + 1, (fd != null) ? fd.MissingWidth : 0);
                            }


                            font = new TTFont<Path>(font_file, w, this, factory);
                            Debug.WriteLine("Substituted " + BaseFont + " with fontfile " + ((TTFont<Path>)font).FullName);
                        }
                        catch (Exception e)
                        {
                            Debug.Print(e.Message);
                        }
                    }
                    if (font == null)
                    {
                        var base_name = BaseFont;

                        ////Some fonts does not work well with auto substitution.
                        //if (base_name.StartsWith("HelveticaNeue-Medium"))
                        //{
                        //    //This font will be matched with Arial, but Arial-Narrow is a
                        //    //better fit.
                        //    disk_font = Util.Registery.FindFontFilename("Arial-Narrow", false, false);
                        //    if (disk_font != null)
                        //        goto load_from_disk;
                        //}

                        //We try creating a AdobeFontMetrixs object from the font descriptor
                        afm = AdobeFontMetrixs.Create(base_name, Encoding, FontDescriptor);
                        if (w == null)
                            w = afm.GetWidths(FirstChar, LastChar + 1, (fd != null) ? fd.MissingWidth : 0);

                        Debug.WriteLine("Substituted " + BaseFont + " with " + afm.FontName);
                        font = factory.CreateBuiltInFont(this, w, afm, true);
                    }
                }
                else
                {
                    if (w == null)
                        w = afm.GetWidths(FirstChar, LastChar + 1, (fd != null) ? fd.MissingWidth : 0);
                    font = factory.CreateBuiltInFont(this, w, afm, false);
                }
            }
            else
            {
                //Todo: Handle things when the "Widths" array is "null". (That will say use the font's internal widths or somesuch)
                switch (SubType)
                {
                    case FontType.MMType1:
                    case FontType.Type1:
                        if (fd.FontFile != null)
                        {
                            //Todo: Use factory?
                            font = new Type1Font<Path>(Widths, fd, Encoding, factory);


                        }
                        else
                        {
                            var ff3 = fd.FontFile3;
                            if (ff3 == null)
                            {
                                var ff2 = fd.FontFile2;
                                if (ff2 != null)
                                    return new CFFOTFont<Path>(Widths, this, factory);
                                throw new PdfReadException(PdfType.FontFile3, ErrCode.Missing);
                            }
                            if ("Type1C".Equals(ff3.Subtype))
                            {
                                //Todo: Use factory
                                font = new Type1CFont<Path>(Widths, fd, Encoding, factory);


                            }
                            else if ("OpenType".Equals(ff3.Subtype))
                            {
                                //The CFF file is embeded in a TrueType container
                                var tt_file = new PdfLib.Read.TrueType.TableDirectory(new System.IO.MemoryStream(ff3.Stream.DecodedStream));

                                //I don't think any of the TrueType tables are needed for anything, as the PDF already includes all the data
                                //needed. Only when dealing with a broken PDF file is that data perhaps relevant.
                                var cff_file = tt_file.CFF;

                                if (cff_file == null)
                                    throw new NotImplementedException();

                                //Todo: Use factory
                                return new Type1CFont<Path>(Widths, fd, Encoding, factory, new System.IO.MemoryStream(cff_file), true);
                            }
                            else //CID subtypes are handles elsewhere
                                throw new NotImplementedException(ff3.Subtype);
                        }
                        break;
                    case FontType.TrueType:
                        //Todo: Use factory
                        font = new TTFont<Path>(Widths, this, factory);

                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            font.Init();

            return font;
        }

        #region Required overrides

        protected override int IsLike(PdfFont obj)
        {
            var t1 = (PdfType1Font)obj;
            return (int) ((
                PdfFile.Equivalent(Encoding, t1.Encoding) &&
                PdfFile.Equivalent(FontDescriptor, t1.FontDescriptor) &&
                BaseFont == t1.BaseFont &&
                FirstChar == t1.FirstChar &&
                LastChar == t1.LastChar &&
                Util.ArrayHelper.ArraysEqual<double>(Widths, t1.Widths)
                ) ?  Equivalence.Identical : Equivalence.Different);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfType1Font(elems);
        }

        #endregion
    }
}
