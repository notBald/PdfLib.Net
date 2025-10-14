using System;
using System.Globalization;

public enum PdfRoundingMode
{
    /// <summary>
    /// Defer to document, or default to absolute tolerance 1e-4 when unresolved
    /// </summary>
    Auto = 0x100,

    /// <summary>
    /// Use N significant digits (no exponent emitted)
    /// </summary>
    Significant = 0x200,

    /// <summary>
    /// Use abs/rel tolerance → compute decimals
    /// </summary>
    Tolerance = 0x300,

    /// <summary>
    /// Shortest text that round-trips Value (expanded to non-exponent)
    /// </summary>
    RoundTrip = 0x400,
}


internal static class PdfNumberUtil
{
    private static readonly char[] InvalidChars = new[] { 'e', 'E', ',', ' ', '\t', '\r', '\n' };

    public static string FormatFixed(double x, int decimals, bool trimTrailingZeros)
    {
        string s = Math.Round(x, decimals, MidpointRounding.ToEven).ToString("F" + decimals, CultureInfo.InvariantCulture);

        if (trimTrailingZeros && s.IndexOf('.') >= 0)
        {
            int i = s.Length - 1;
            while (i >= 0 && s[i] == '0') i--;
            if (i >= 0 && s[i] == '.') i--;
            s = s.Substring(0, i + 1);
        }
        return NormalizeMinusZero(s);
    }

    public static string FormatSig(double x, int sigDigits)
    {
        double ax = Math.Abs(x);
        if (ax == 0) return "0";

        // Prefer fixed form for common magnitudes to avoid exponent
        if (ax >= 1e-4 && ax <= 1e6)
        {
            int exp = (int)Math.Floor(Math.Log10(ax));
            int decimals = Math.Max(0, sigDigits - 1 - exp);
            return FormatFixed(x, decimals, true);
        }

        // Else: round-trip digits then expand if scientific
        return ExpandExponent(x.ToString("G17", CultureInfo.InvariantCulture));
    }

    public static string FormatTol(double x, double absTol, double relTol)
    {
        double effTol = Math.Max(absTol, Math.Abs(x) * relTol);
        if (effTol < 1e-15) effTol = 1e-15;

        int decimals = (int)Math.Ceiling(-Math.Log10(effTol));
        if (decimals < 0) decimals = 0;
        if (decimals > 15) decimals = 15;

        return FormatFixed(x, decimals, true);
    }

    public static string ExpandExponent(string s)
    {
        int ePos = s.IndexOf('e');
        if (ePos < 0) ePos = s.IndexOf('E');
        if (ePos < 0) return NormalizeMinusZero(TrimZeros(s));

        bool negative = s.Length > 0 && s[0] == '-';
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
            s = s.Substring(1);

        string mant = s.Substring(0, ePos);
        int exp = int.Parse(s.Substring(ePos + 1), CultureInfo.InvariantCulture);

        int dot = mant.IndexOf('.');
        string digits = dot >= 0 ? mant.Remove(dot, 1) : mant;
        int pointIndex = dot >= 0 ? dot : digits.Length;
        long newPoint = (long)pointIndex + exp;

        string result;
        if (newPoint <= 0)
        {
            result = "0." + new string('0', (int)(-newPoint)) + digits;
        }
        else if (newPoint >= digits.Length)
        {
            result = digits + new string('0', (int)(newPoint - digits.Length));
        }
        else
        {
            result = digits.Insert((int)newPoint, ".");
        }

        if (negative && result != "0") result = "-" + result;
        return NormalizeMinusZero(TrimZeros(result));
    }

    private static string TrimZeros(string s)
    {
        int dot = s.IndexOf('.');
        if (dot >= 0)
        {
            int i = s.Length - 1;
            while (i > dot && s[i] == '0') i--;
            if (i == dot) i--;
            s = s.Substring(0, i + 1);
        }
        return s;
    }

    private static string NormalizeMinusZero(string s)
    {
        return s == "-0" ? "0" : s;
    }

    private static bool IsPdfNumber(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.IndexOfAny(InvalidChars) >= 0) return false;

        int dots = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i == 0 && (c == '+' || c == '-')) continue;

            if (c == '.')
            {
                dots++;
                if (dots > 1) return false;
                continue;
            }

            if (c < '0' || c > '9') return false;
        }
        return true;
    }
}