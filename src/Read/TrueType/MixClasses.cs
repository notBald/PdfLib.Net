using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PdfLib.Read.TrueType
{
    public class TTException : Exception
    {
        public TTException() { }
        public TTException(string msg) : base(msg) { }
    }

    public class TableMissingException : TTException
    {
        public TableMissingException() { }
        public TableMissingException(string msg) : base(msg) { }
    }

    public class UnknownFormatException : TTException
    {
        public UnknownFormatException() { }
        public UnknownFormatException(string msg) : base(msg) { }
    }

    public class UnknownVersionException : TTException
    {
        public UnknownVersionException() { }
        public UnknownVersionException(string msg) : base(msg) { }
    }

    public class InvalidDataException : TTException
    {
        public InvalidDataException() { }
        public InvalidDataException(string msg) : base(msg) { }
    }

    enum PlatformID : ushort
    {
        AppleUnicode = 0,
        Macintosh = 1,
        ISO = 2,
        Microsoft = 3
    }

    enum MicrosoftEncodingID : ushort
    {
        encodingUndefined = 0, //Symbol in opentype
        encodingUGL = 1,       //Unicode BMP only in opentype

        //These are specified in the OpenType specs
        shiftJIS = 2,
        PRC = 3,
        Big5 = 4,
        Wansung = 5,
        Johab = 6,
        Unicode = 10
    }

    enum MacintoshEncodingID : short
    {
        encodingRoman = 0,
        encodingJapanese = 1,
        encodingChinese = 2,
        encodingKorean = 3,
        encodingArabic = 4,
        encodingHebrew = 5,
        encodingGreek = 6,
        encodingRussian = 7,
        encodingRSymbol = 8,
        encodingDevanagari = 9,
        encodingGurmukhi = 10,
        encodingGujarati = 11,
        encodingOriya = 12,
        encodingBengali = 13,
        encodingTamil = 14,
        encodingTelugu = 15,
        encodingKannada = 16,
        encodingMalayalam = 17,
        encodingSinhalese = 18,
        encodingBurmese = 19,
        encodingKhmer = 20,
        encodingThai = 21,
        encodingLaotian = 22,
        encodingGeorgian = 23,
        encodingArmenian = 24,
        encodingMaldivian = 25,
        encodingTibetan = 26,
        encodingMongolian = 27,
        encodingGeez = 28,
        encodingSlavic = 29,
        encodingVietnamese = 30,
        encodingSindhi = 31,
        encodingUninterp = 32,
    }
}
