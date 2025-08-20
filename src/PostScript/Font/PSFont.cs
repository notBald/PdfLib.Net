using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.PostScript.Primitives;
using PdfLib.PostScript.Internal;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.PostScript.Font
{
    public class PSFont : PSDictionary
    {
        #region Properties

        /// <summary>
        /// The Private dictionary contains hints that apply across all the
        /// characters in the font, subroutines, and several other items such
        /// as a password.
        /// </summary>
        public PSPrivDict PrivateDict { get { return GetPSDictEx<PSPrivDict>("Private", PSPrivDict.Create); } }

        /// <summary>
        /// The CharStrings dictionary holds a collection of name-procedure
        /// pairs.
        /// </summary>
        public PSCharStrings CharStrings { get { return GetPSDictEx<PSCharStrings>("CharStrings", PSCharStrings.Create); } }

        /// <summary>
        /// An array of character names to which character codes are mapped
        /// </summary>
        public PSValArray<string, PSName> Encoding { get { return GetPSObj<PSValArray<string, PSName>, PSArray>("Encoding", CreateEncAr); } }

        public xMatrix FontMatrix
        {
            get
            {
                var m = GetPSObj<PSValArray<double, PSItem>, PSArray>("FontMatrix", HlpMet.CrDblArray);
                if (m == null)
                    return new xMatrix(0.001, 0, 0, 0.001, 0, 0);

                return new xMatrix(m);
            }
        }

        /// <summary>
        /// Checks if this font has "Flex" curves.
        /// </summary>
        /// <remarks>
        /// Flex curves are curves that should be ignored at
        /// small font sizes.
        /// 
        /// Using EexecStream.MakeReadable for this is ofcourse wastefull
        /// </remarks>
        public bool FlexFont
        {
            get
            {
                var subs = PrivateDict.Subrs;
                if (subs == null || subs.Length < 4) return false;
                var sub = subs[0];
                if (EexecStream.MakeReadable(sub) != "3 0 callothersubr\npop\npop\nsetcurrentpoint\nreturn\n")
                    return false;
                sub = subs[1];
                if (EexecStream.MakeReadable(sub) != "0 1 callothersubr\nreturn\n")
                    return false;
                sub = subs[2];
                if (EexecStream.MakeReadable(sub) != "0 2 callothersubr\nreturn\n")
                    return false;
                sub = subs[3];
                if (EexecStream.MakeReadable(sub) != "return\n")
                    return false;

                return true;
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Constructor
        /// </summary>
        public PSFont(PSDictionary dict)
            : base(dict.Catalog)
        { Access = dict.Access; }

        public PSFont()
            : this(new PSDictionary()) { }

        public override PSItem ShallowClone()
        {
            return new PSFont(this);
        }

        internal static PSFont Create(PSDictionary font)
        {
            return new PSFont(font);
        }

        #endregion

        #region Callbacks

        static PSValArray<string, PSName> CreateEncAr(PSArray ar)
        {
            return new PSValArray<string, PSName>(ar,
                (str) => { return new PSName(str); },
                (str) => { return str.Value; });
        }

        #endregion
    }
}
