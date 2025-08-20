using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace PdfLib.Util
{
    public static class Registery
    {
        #region Font finding code

        /// <summary>
        /// Finds the full filename of the font
        /// </summary>
        /// <param name="name">Basename</param>
        /// <param name="bold">Is this font bold</param>
        /// <param name="italic">Is this font italic</param>
        /// <returns>The font filename, if any.</returns>
        public static string FindFontFilename(string name, bool bold, bool italic)
        {
            if (name == null) return null;

            //Sanitizes the name
            int i;
            if (bold || italic)
            {
                i = name.IndexOfAny(new char[] { ',', '-' });
                if (i != -1)
                    name = name.Substring(0, i);
                name = Pdf.Font.AdobeFontMetrixs.RmovePSMT(name.Replace(" ", "").ToLower());
                i = name.Length;
                if (bold)
                    name += "bold";
                if (italic)
                    name += "italic";
            }
            else
            {
                name = Pdf.Font.AdobeFontMetrixs.RmovePSMT(name.Replace(" ", "").ToLower());
                name = name.Replace("-", "");
                i = name.Length;
            }

            bool NT = Environment.OSVersion.Platform == PlatformID.Win32NT;
            var fonts = Registry.LocalMachine.OpenSubKey(string.Format(@"Software\Microsoft\Windows{0}\CurrentVersion\Fonts", NT ? " NT" : ""));
            var all_fonts = fonts.GetValueNames();
            string best_match = null;
            foreach (var font_name in all_fonts)
            {
                var sane_name = font_name.Replace(" ", "").Replace("-", "").ToLower();
                int tt = sane_name.LastIndexOf("(truetype)");
                if (tt == -1) continue;
                sane_name = sane_name.Substring(0, tt);

                if (sane_name.StartsWith(name))
                {
                    sane_name = sane_name.Substring(i);
                    bool is_bold = sane_name.IndexOf("bold") != -1;
                    bool is_italic = sane_name.IndexOf("italic") != -1;
                    if (is_bold == bold && italic == is_italic)
                    {
                        int more = 0;
                        if (is_italic) more += 6;
                        if (is_bold) more += 4;
                        if (more != sane_name.Length)
                        {
                            if (best_match == null)
                                best_match = font_name;
                            continue;
                        }
                        return fonts.GetValue(font_name).ToString();
                    }
                }
            }

            if (best_match != null)
                return fonts.GetValue(best_match).ToString();

            //Could search for fonts without bold/italic. I.e. return FindFontFilename(.., false, false).
            return null;
        }

        #endregion
    }
}
