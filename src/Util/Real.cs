using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Util
{
    /// <summary>
    /// Helper class for working with real numbers
    /// </summary>
    public static class Real
    {
        public static bool Same(double num1, double num2)
        {
            return Math.Abs(Math.Abs(num1) - Math.Abs(num2)) < 0.000000001;
        }

        public static bool Same(double? num1, double? num2)
        {
            if (num1 == null)
                return num2 == null;
            return (num2 != null) && Same(num1.Value, num2.Value);
        }

        public static bool Same(double num1, double num2, double delta)
        {
            return Math.Abs(Math.Abs(num1) - Math.Abs(num2)) < delta;
        }

        public static bool IsZero(double num)
        {
            return Math.Abs(num) < 0.000000001;
        }
    }
}
