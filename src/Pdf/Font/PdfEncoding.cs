using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Font
{
    public class PdfEncoding : Elements
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Encoding
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Encoding; }
        }

        /// <summary>
        /// If there is a specified base encoding for this encoding.
        /// </summary>
        public string[] BaseEncoding 
        { 
            get 
            {
                var name = _elems.GetName("BaseEncoding");
                if (name == null) return Enc.Standar; //<-- Todo: Not correct for Symbolic fonts
                switch (name)
                {
                    case "WinAnsiEncoding":
                        return Enc.WinANSI;

                    case "MacRomanEncoding":
                        return Enc.MacRoman;

                    case "MacExpertEncoding":
                        return Enc.MacExpert;

                        //Todo: Look into this
                    case "Identity-H":
                    case "Identity-V":
                        goto default;

                    default:
                        throw new PdfReadException(PdfType.Name, ErrCode.OutOfRange);
                }
            } 
        }

        /// <summary>
        /// Gets the BaseArray merged with differences and
        /// standar encoding
        /// </summary>
        public string[] StdMergedEncoding
        {
            get
            {
                var diff = Differences;
                var std = Enc.Standar;
                for (int c = 0; c < diff.Length; c++)
                {
                    if (".notdef".Equals(diff[c]))
                        diff[c] = std[c];
                }
                return diff;
            }
        }

        /// <summary>
        /// The base encoding as a enum
        /// </summary>
        public PdfBaseEncoding GetBaseEncoding()
        {
            var name = _elems.GetName("BaseEncoding");
            if (name == null) return PdfBaseEncoding.StandardEncoding;
            return GetBaseEncoding(name);
        }

        static PdfBaseEncoding GetBaseEncoding(string name)
        {
            switch (name)
            {
                case "WinAnsiEncoding":
                    return PdfBaseEncoding.WinAnsiEncoding;

                case "MacRomanEncoding":
                    return PdfBaseEncoding.MacRomanEncoding;

                case "MacExpertEncoding":
                    return PdfBaseEncoding.MacExpertEncoding;

                default:
                    throw new PdfReadException(PdfType.Name, ErrCode.OutOfRange);
            }
        }

        static string GetBaseEncoding(PdfBaseEncoding name)
        {
            switch (name)
            {
                case PdfBaseEncoding.WinAnsiEncoding:
                    return "WinAnsiEncoding";

                case PdfBaseEncoding.MacRomanEncoding:
                    return "MacRomanEncoding";

                case PdfBaseEncoding.MacExpertEncoding:
                    return "MacExpertEncoding";

                default:
                    throw new PdfReadException(PdfType.Name, ErrCode.OutOfRange);
            }
        }

        /// <summary>
        /// Gets the base array merged with the differences
        /// array.
        /// </summary>
        public string[] Differences
        {
            get
            {
                return CreateDifferences(BaseEncoding);
            }
        }

        /// <summary>
        /// An array describing the differences from the encoding specified by BaseEncoding
        /// </summary>
        /// <remarks>
        /// Used by AdobeFontMetix
        /// </remarks>
        internal PdfArray DifferencesAr { get { return _elems.GetArray("Differences"); } }

        /// <summary>
        /// The differences without any standar base encoding, for use by
        /// symbolic fonts.
        /// </summary>
        /// <remarks>.notdef may be returned as null pointers</remarks>
        internal string[] SymbolicDifferences
        {
            get
            {
                if (_elems.Contains("BaseEncoding"))
                    return CreateDifferences(BaseEncoding);
                if (_elems.Contains("Differences"))
                    return CreateDifferences(new string[256]);
                else return null;
            }
        }

        #endregion

        #region Init

        public PdfEncoding(PdfBaseEncoding enc)
            : base(new TemporaryDictionary())
        { 
             if (enc != PdfBaseEncoding.StandardEncoding)
                _elems.SetName("BaseEncoding", GetBaseEncoding(enc));
        }

        internal PdfEncoding(string name)
            : base(new Catalog() { { "BaseEncoding", new PdfName(name) } })
        { }

        internal PdfEncoding(PdfDictionary dict)
            : base(dict)
        {
            _elems.CheckType("Encoding");
        }

        #endregion

        #region Required overrides

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(obj, this)) return true;
            if (obj != null && obj is PdfEncoding)
            {
                var enc = (PdfEncoding) obj;
                return 
                    PdfFile.Equivalent(_elems.GetName("BaseEncoding"), enc._elems.GetName("BaseEncoding")) &&
                    PdfFile.Equivalent(_elems.GetArray("Differences"), enc._elems.GetArray("Differences"));
            }
            return false;
        }

        internal override void Write(Write.Internal.PdfWriter write)
        {
            //Can't assume that there's base encoding, or Differences,
            //as they're both optional features.
            if (_elems.Contains("Differences") || 
                !_elems.Contains("BaseEncoding")) base.Write(write);
            else _elems.GetItem("BaseEncoding").Write(write);
        }

        /// <summary>
        /// Used when moving the object to different documents.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfEncoding(elems);
        }

        #endregion

        #region Helper methods

        internal string[] CreateDifferences(string[] original_table)
        {
            var diff = _elems.GetArray("Differences");
            if (diff != null)
            {
                int current_character = 0;
                int current_pos = 0;
                while (current_pos < diff.Length)
                {
                    var itm = diff[current_pos++];

                    if (itm.Type == PdfType.Integer)
                    {
                        current_character = itm.GetInteger();

                        //Sanity
                        if (current_character < 0) current_character = 0;
                        else if (current_character > 255) current_character = 255;
                    }
                    else
                    {
                        if (current_character >= original_table.Length)
                        {
                            // Todo: Log out of range character
                            System.Diagnostics.Debug.Assert(false, "Character out of range " + current_character );
                        }
                        else
                        {
                            original_table[current_character++] = itm.GetString();
                        }
                    }
                }
            }
            return original_table;
        }

        /// <summary>
        /// Creates a "ToUnicode" encoding.
        /// </summary>
        public static int[] CreateUnicodeEnc(string[] encode)
        {
            var unicodes = Enc.GlyphNames;
            var chrs = new int[encode.Length];
            for (int c = 0; c < encode.Length; c++)
            {
                char ch;
                if (unicodes.TryGetValue(encode[c], out ch))
                    chrs[c] = ch;
                else
                {
                    var str = encode[c];

                    //Some PDF creators encode the unicode into the name as uni<hex number of the unicode character>. It's not part of the PDF standard, but
                    //doesn't hurt to support. 
                    if (str.Length > 3 && str.StartsWith("uni"))
                    {
                        int uni_char;
                        if (int.TryParse(str.Substring(3).TrimStart('0'), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uni_char))
                        {
                            chrs[c] = uni_char;
                            continue;
                        }
                    }

                    //This means a character was not translated to Unicode
                    System.Diagnostics.Debug.Assert(".notdef".Equals(str), "Failed to map "+str+" to unicode.");
                }
            }
            return chrs;
        }

        /// <summary>
        /// Creates a "ToUnicode" encoding.
        /// </summary>
        public static ushort[] CreateUshortUnicodeEnc(string[] encode)
        {
            var unicodes = Enc.GlyphNames;
            var chrs = new ushort[encode.Length];
            for (int c = 0; c < encode.Length; c++)
            {
                char ch;
                if (unicodes.TryGetValue(encode[c], out ch))
                    chrs[c] = ch;
                else
                {
                    System.Diagnostics.Debug.Assert(".notdef".Equals(encode[c]));
                }
            }
            return chrs;
        }

        /// <summary>
        /// Encodes characters to their integer index values
        /// </summary>
        /// <remarks>
        /// Symbolic fonts does not follow the unicode standard,
        /// but as long as the indexes correspond with the indexes
        /// in the font, this function works fine.
        /// </remarks>
        public static int[] CreatePassEnc(string[] encode)
        {
            var chrs = new int[encode.Length];
            for (int c = 0; c < encode.Length; c++)
                chrs[c] = c;
            return chrs;
        }

        /// <summary>
        /// Creates a cross references dictionary for translating from
        /// one charcode value to another.
        /// </summary>
        internal static int[] CreateXRef(string[] from, string[] to)
        {
            var xref = new int[256];
            int ndef = -1;

            for (int i_from = 0; i_from < from.Length; i_from++)
            {
                var str = from[i_from];

                //First check if the charcodes are the same
                if (i_from < to.Length && str.Equals(to[i_from]))
                {
                    xref[i_from] = i_from;
                    continue;
                }

                //Search for an equal charcode
                int i = Array.IndexOf<string>(to, str);
                if (i != -1)
                {
                    xref[i_from] = i;
                    continue;
                }

                //Set .notdefined
                if (ndef == -1)
                {
                    ndef = Array.IndexOf<string>(to, ".notdef");
                    if (ndef == -1)
                        throw new NotSupportedException("Must have a undefined character");
                }
                xref[i_from] = ndef;
            }

            return xref;
        }

        internal Dictionary<int, int> CreateDiffDict(PdfDictionary diff, string[] names)
        {


            throw new NotImplementedException();
        }

        private Dictionary<int, int> CreateDiffDict(PdfArray diff, string[] names)
        {


            throw new NotImplementedException();
        }



        #endregion
    }

    public enum PdfBaseEncoding
    {
        StandardEncoding,
        WinAnsiEncoding,
        MacRomanEncoding,
        MacExpertEncoding
    }
}
