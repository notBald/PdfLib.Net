using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Read.CFF
{
    /// <summary>
    /// Note: Recreate this class for every job
    /// </summary>
    class TopDICT : Parser
    {
        #region Variables

        public ushort? Version, Notice, FullName, FamilyName, Weight, Copyright;
        public ushort? PostScript, BaseFontName;
        public object[] FontBBox, XUID;
        public object UniqueID;

        /// <summary>
        /// Charset offset
        /// </summary>
        public int Charset = 0;

        /// <summary>
        /// Encoding offset
        /// </summary>
        public int Encoding = 0;

        public SizePos Private;

        public bool IsFixedPitch = false;
        public double ItalicAngle = 0;
        public double UnderlinePosition = -100;
        public double UnderlineThickness = 50;
        public double StrokeWidth = 0;
        public int PaintType = 0;
        public int CharstringType = 2;
        public xMatrix FontMatrix = new xMatrix(0.001, 0, 0, 0.001, 0, 0);

        public int? CharStrings;

        public CID_Font CID;

        #endregion

        #region Init

        public TopDICT(Util.StreamReaderEx r)
            : base(r, false) { }

        public TopDICT(Util.StreamReaderEx r, bool CID_font)
            : base(r, false)
        {
            if (CID_font) CID = new CID_Font();
        }

        #endregion

        #region Parse code

        protected override void ParseCMD1()
        {
            switch (_number)
            {
                case 0:
                    Version = (ushort)PopInt();
                    break;
                case 1:
                    Notice = (ushort)PopInt();
                    break;
                case 2:
                    FullName = (ushort)PopInt();
                    break;
                case 3:
                    FamilyName = (ushort)PopInt();
                    break;
                case 4:
                    Weight = (ushort)PopInt();
                    break;
                case 5: //Array with four numbers
                    FontBBox = StackToArray();
                    break;
                case 13:
                    UniqueID = Pop();
                    break;
                case 14:
                    XUID = StackToArray();
                    break;
                case 15:
                    Charset = PopInt();
                    break;
                case 16:
                    Encoding = PopInt();
                    break;
                case 17:
                    CharStrings = PopInt();
                    break;
                case 18: //Two numbers pointing to the Private DICT
                    Private = new SizePos
                    {
                        start = PopInt(),
                        length = PopInt()
                    };
                    break;

                default: //Unregonized command
                    //queue.Clear();
                    Debug.Assert(false, "Unimplimented command");
                    break;
            }

            Clear();
        }

        protected override void ParseCMD2()
        {
            switch (_number)
            {
                case 0:
                    Copyright = (ushort)PopInt();
                    break;
                case 1:
                    IsFixedPitch = (PopInt() != 0);
                    break;
                case 2:
                    ItalicAngle = PopDouble();
                    break;
                case 3:
                    UnderlinePosition = PopDouble();
                    break;
                case 4:
                    UnderlineThickness = PopDouble();
                    break;
                case 5:
                    PaintType = PopInt();
                    break;
                case 6:
                    CharstringType = PopInt();
                    break;
                case 7:
                    FontMatrix = CreateMatrix();
                    break;
                case 8:
                    StrokeWidth = PopDouble();
                    break;

                case 21:
                    PostScript = (ushort)PopInt();
                    break;

                case 22:
                    BaseFontName = (ushort)PopInt();
                    break;

                case 30:
                    CID = new CID_Font();
                    CID.Supplement = PopInt();
                    CID.Ordering = PopInt();
                    CID.Registry = PopInt();
                    break;

                case 31:
                    CID.CIDFontVersion = PopInt();
                    break;

                case 32:
                    CID.CIDFontRevision = PopInt();
                    break;

                case 33:
                    CID.CIDFontType = PopInt();
                    break;

                case 34:
                    CID.CIDCount = PopInt();
                    break;

                case 35:
                    CID.UIDBase = PopInt();
                    break;

                case 36:
                    CID.FDArray = PopInt();
                    break;

                case 37:
                    CID.FDSelect = PopInt();
                    break;

                case 38:
                    CID.FontName = PopInt();
                    break;

                default: //Unregognized command
                    //queue.Clear();
                    Debug.Assert(false, "Unimplimented command");
                    break;
            }

            Clear();
        }

        #endregion
    }
}
