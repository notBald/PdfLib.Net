using System;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Read;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Primitives
{
    public sealed class PdfDate : PdfObject
    {
        #region Variables and properties

        /// <summary>
        /// PdfType.Date
        /// </summary>
        internal override PdfType Type
        {
            get { return PdfType.Date; }
        }

        int _year;
        int? _month, _day, _hours, _minuttes, _seconds, _timezone, _timezone_minuttes;

        public string Value { get { return MakeDateString(); } }

        #endregion

        #region Init

        /// <summary>
        /// Creates a date object from a string
        /// </summary>
        /// <param name="date">D:YYYYMMDDHHmmSSOHH'mm</param>
        public PdfDate(string date)
        {
            //The prefix D: shall be present, the year field (YYYY) shall be present.
            if (date == null || date.Length < 6 || date[0] != 'D' || date[1] != ':')
                throw new PdfReadException(PdfType.Date, ErrCode.Invalid);
            _year = int.Parse(date.Substring(2, 4));

            //The default values for MM and DD shall be both 01; all other numerical fields shall 
            //default to zero values. 
            _month = 1;
            _day = 1;

            //All other fields may be present but only if all of their preceding fields are also present. 
            if (date.Length < 8) return;
            _month = int.Parse(date.Substring(6, 2));
            if (date.Length < 10) return;
            _day = int.Parse(date.Substring(8, 2));
            if (date.Length < 12) return;
            _hours = int.Parse(date.Substring(10, 2));
            if (date.Length < 14) return;
            _minuttes = int.Parse(date.Substring(12, 2));
            if (date.Length < 16) return;
            _seconds = int.Parse(date.Substring(14, 2));

            //Timezone
            //A PLUS SIGN as the value of the O field signifies that local time is later than UT, 
            //a HYPHEN-MINUS signifies that local time is earlier than UT, and the LATIN CAPITAL 
            //LETTER Z signifies that local time is equal to UT.
            if (date.Length < 17) return;
            var zone = date[16];
            if (date.Length < 19) return;
            var time = int.Parse(date.Substring(17, 2));
            _timezone = zone == '-' ? -time : time;
            if (zone == 'Z') _timezone = 0;
            
            //The APOSTROPHE following the hour offset field (HH) shall only be present if the HH 
            //field is present.
            //
            //The minute offset field (mm) shall only be present if the APOSTROPHE following the hour 
            //offset field (HH) is present.
            if (date.Length < 20) return;
            if (date[19] != '\'')
                throw new PdfReadException(PdfType.Date, ErrCode.Invalid);
            if (date.Length < 22) return;
            _timezone_minuttes = int.Parse(date.Substring(20, 2));            
        }

        public PdfDate(int year, int? month, int? day, int? hours, int? minuttes, int? seconds, int? timezone, int? timezone_minuttes)
        {
            _year = Range(year, 0, 9999).Value;
            _month = Range(month, 1, 12);
            _day = Range(day, 1, 31);
            _hours = Range(hours, 0, 23);
            _minuttes = Range(minuttes, 0, 59);
            _seconds = Range(seconds, 0, 59);
            _timezone = Range(timezone, -23, 23);
            _timezone_minuttes = Range(timezone_minuttes, 0, 59);
        }

        public string MakeDateString()
        {
            StringBuilder sb = new StringBuilder(22);
            sb.Append("D:");
            padd(sb, _year, 4);
            if (_month != null)
            {
                padd(sb, _month.Value, 2);
                if (_day != null)
                {
                    padd(sb, _day.Value, 2);
                    if (_hours != null)
                    {
                        padd(sb, _hours.Value, 2);
                        if (_minuttes != null)
                        {
                            padd(sb, _minuttes.Value, 2);
                            if (_seconds != null)
                            {
                                padd(sb, _seconds.Value, 2);
                                if (_timezone != null)
                                {
                                    int tz = _timezone.Value;
                                    if (tz < 0)
                                    {
                                        sb.Append('-');
                                        tz *= -1;
                                    }
                                    else
                                        sb.Append('+');
                                    padd(sb, _timezone.Value, 2);
                                    sb.Append('\'');
                                    if (_timezone_minuttes != null)
                                        padd(sb, _timezone_minuttes.Value, 2);
                                }
                            }
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private int? Range(int? val, int min, int max)
        {
            if (val == null) return null;
            if (val < min || val > max)
                throw new PdfCreateException("Invalid date range");
            return val;
        }

        private void padd(StringBuilder sb, int val, int n)
        {
            var str = val.ToString();
            int c = str.Length, m = n - Math.Max(0, n - c);
            for ( ; c < n; c++)
                sb.Append("0");
            sb.Append(str.Substring(0, m));
        }

        #endregion

        public static PdfDate Now
        {
            get
            {
                var dt = DateTime.Now.ToUniversalTime();
                return new PdfDate(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, null, null);
            }
        }

        #region Overrides

        internal override bool Equivalent(object obj)
        {
            if (obj == null) return false;
            return MakeDateString() == obj.ToString();
        }

        /// <summary>
        /// A PdfDate is considered imutable
        /// </summary>
        public override PdfItem Clone()
        {
            return this;
        }

        internal override void Write(PdfWriter write)
        {
            new PdfString(Lexer.GetBytes(MakeDateString()), false, false).Write(write);            
        }

        public override string ToString()
        {
            return Value;
        }

        #endregion
    }
}
