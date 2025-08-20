using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib.Util
{
    /// <summary>
    /// This is a helper class for dealing with (largely byte) arrays
    /// </summary>
    public class ArrayHelper
    {
        /// <summary>
        /// Combines byte arrays into one large byte array
        /// </summary>
        /// <remarks>Called concat instead of "Join" because of Linq</remarks>
        public static byte[] Concat(params byte[][] arrays)
        {
            int tot_size = 0;
            for (int c = 0; c < arrays.Length; c++)
                tot_size += arrays[c].Length;

            byte[] ret = new byte[tot_size];
            tot_size = 0;
            for (int c = 0; c < arrays.Length; c++)
            {
                var ar = arrays[c];
                Buffer.BlockCopy(ar, 0, ret, tot_size, ar.Length);
                tot_size += ar.Length;
            }
            return ret;
        }

        /// <summary>
        /// Combines byte arrays into one large byte array
        /// </summary>
        public static T[] Join<T>(params T[][] arrays)
        {
            int tot_size = 0;
            for (int c = 0; c < arrays.Length; c++)
            {
                var ar = arrays[c];
                if (ar != null)
                    tot_size += arrays[c].Length;
            }
            if (tot_size == 0) return null;

            T[] ret = new T[tot_size];
            tot_size = 0;
            for (int c = 0; c < arrays.Length; c++)
            {
                var ar = arrays[c];
                if (ar != null)
                {
                    Array.Copy(ar, 0, ret, tot_size, ar.Length);
                    tot_size += ar.Length;
                }
            }
            return ret;
        }

        /// <summary>
        /// Tests if the array has the value
        /// </summary>
        /// <param name="ar">The array to search</param>
        /// <param name="value">Value to seach for</param>
        /// <returns>True if the value is in the array</returns>
        public static bool HasValue(object[] ar, object value)
        {
            return IndexOf(ar, 0, value) != -1;
        }

        /// <summary>
        /// Tests if the array has the value
        /// </summary>
        /// <param name="ar">The array to search</param>
        /// <param name="value">Value to seach for</param>
        /// <returns>True if the value is in the array</returns>
        public static bool HasValue(string[] ar, string value)
        {
            return IndexOf(ar, 0, value) != -1;
        }

        /// <summary>
        /// Tests if the array has the value
        /// </summary>
        /// <param name="ar">The array to search</param>
        /// <param name="value">Value to seach for</param>
        /// <returns>True if the value is in the array</returns>
        public static bool HasValue(object[] ar, object value, int start, int length)
        {
            return IndexOf(ar, start, value, length) != -1;
        }

        /// <summary>
        /// Finds the last index of a byte in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="b">Character to search for</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int LastIndexOf(byte[] ar, params byte[] b)
        {
            for (int c = ar.Length - 1; c >= 0; c--)
            {
                byte a = ar[c];
                for (int i = 0; i < b.Length; i++)
                    if (a == b[i]) return c;
            }
            return -1;
        }

        /// <summary>
        /// Finds the last index of a byte in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="offset">From where to start the search</param>
        /// <param name="b">Character to search for</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int LastIndexOf(byte[] ar, int offset, params byte[] b)
        {
            for (int c = offset; c >= 0; c--)
            {
                byte a = ar[c];
                for (int i = 0; i < b.Length; i++)
                    if (a == b[i]) return c;
            }
            return -1;
        }

        /// <summary>
        /// Finds the index of a not null object in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <returns>-1 if an object isn't found</returns>
        internal static int LastIndexOfNotNull(object[] ar)
        {
            for (int c = ar.Length - 1; c >= 0; c--)
                if (ar[c] != null) return c;
            return -1;
        }

        /// <summary>
        /// Finds the index of a character in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="ch">Characters to search for</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int IndexOf(char[] ar, params char[] ch)
        {
            for (int c = 0; c < ar.Length; c++)
            {
                char a = ar[c];
                for (int i = 0; i < ch.Length; i++)
                    if (a == ch[i]) return c;
            }
            return -1;
        }

        /// <summary>
        /// Finds the index of a byte in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <param name="bs">Bytes to search for</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int IndexOf(byte[] ar, int offset, params byte[] bs)
        {
            for (int c = offset; c < ar.Length; c++)
            {
                byte b = ar[c];
                for (int i = 0; i < bs.Length; i++)
                    if (b == bs[i]) return c;
            }
            return -1;
        }

        /// <summary>
        /// Finds the index of a byte in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <param name="end">Where to stop searching</param>
        /// <param name="bs">Bytes to search for</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int IndexOf(byte[] ar, int offset, int end, params byte[] bs)
        {
            for (int c = offset; c < end; c++)
            {
                byte b = ar[c];
                for (int i = 0; i < bs.Length; i++)
                    if (b == bs[i]) return c;
            }
            return -1;
        }

        /// <summary>
        /// Finds the index of a character in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="ch">Character to search for</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int IndexOf(char[] ar, int offset, char ch)
        {
            for (int c = offset; c < ar.Length; c++)
                if (ar[c] == ch) return c;
            return -1;
        }

        /// <summary>
        /// Finds the index of a object in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="o">Object to search for</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int IndexOf(object[] ar, int offset, object o)
        {
            for (int c = offset; c < ar.Length; c++)
                if (ar[c] == o) return c;
            return -1;
        }

        /// <summary>
        /// Finds the index of a string in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="o">Object to search for</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int IndexOf(string[] ar, int offset, string o)
        {
            if (o == null) return IndexOf(ar, offset, (object) o);
            for (int c = offset; c < ar.Length; c++)
                if (o.Equals(ar[c])) return c;
            return -1;
        }

        /// <summary>
        /// Finds the index of a object in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="o">Object to search for</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <param name="length">How far to search in the array</param>
        /// <returns>-1 if the character isn't found</returns>
        public static int IndexOf(object[] ar, int offset, object o, int length)
        {
            for (int c = offset; c < length; c++)
                if (ar[c] == o) return c;
            return -1;
        }

        /// <summary>
        /// Finds the index of a null pointer in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <returns>ar.Length if the null isn't found</returns>
        internal static int IndexOfNull(object[] ar, int offset)
        {
            for (int c = offset; c < ar.Length; c++)
                if (ar[c] == null) return c;
            return ar.Length;
        }

        /// <summary>
        /// Finds the index of a not null object in an array
        /// </summary>
        /// <param name="ar">The array to search through</param>
        /// <param name="offset">How far out in the array to start searching</param>
        /// <returns>ar.Length if an object isn't found</returns>
        internal static int IndexOfNotNull(object[] ar, int offset)
        {
            for (int c = offset; c < ar.Length; c++)
                if (ar[c] != null) return c;
            return ar.Length;
        }

        /// <summary>
        /// Shifts characters in a array and fills it
        /// </summary>
        /// <param name="ar">Array to fill</param>
        /// <param name="pos">Position in the array</param>
        /// <param name="s">Source of characters</param>
        public static void ShiftFill(char[] ar, int pos, Stream s)
        {
            var buf = new byte[ar.Length];
            if (pos >= ar.Length)
            {
                s.Read(buf, 0, buf.Length);
                var src = Encoding.ASCII.GetChars(buf);
                Array.Copy(src, ar, src.Length);
            }
            else
            {
                int l = ar.Length - pos;
                Array.Copy(ar, pos, ar, 0, l);
                s.Read(buf, 0, pos);
                var src = Encoding.ASCII.GetChars(buf, 0, pos);
                Array.Copy(src, 0, ar, l, pos);
            }
        }

        public static bool Same(double[] ar, double val)
        {
            for (int c = 0; c < ar.Length; c++)
                if (ar[c] != val)
                    return false;
            return true;
        }

        public static bool Same(ushort[] ar, ushort val)
        {
            for (int c = 0; c < ar.Length; c++)
                if (ar[c] != val)
                    return false;
            return true;
        }

        public static bool Same(byte[] ar, byte val)
        {
            for (int c = 0; c < ar.Length; c++)
                if (ar[c] != val)
                    return false;
            return true;
        }

        public static ushort Sum(ushort[] ar)
        {
            ushort sum = 0;
            for (int c = 0; c < ar.Length; c++)
                sum += ar[c];
            return sum;
        }

        public static void Fill(ushort[] ar, ushort val)
        {
            for (int c = 0; c < ar.Length; c++)
                ar[c] = val;
        }

        public static void Fill(int[] ar, int val)
        {
            for (int c = 0; c < ar.Length; c++)
                ar[c] = val;
        }

        public static void Fill(ulong[] ar, ulong val)
        {
            for (int c = 0; c < ar.Length; c++)
                ar[c] = val;
        }

        public static void Fill(double[] ar, double val)
        {
            for (int c = 0; c < ar.Length; c++)
                ar[c] = val;
        }

        public static ushort Max(ushort[] ar)
        {
            ushort max = 0;
            for(int c=0; c < ar.Length; c++)
                max = Math.Max(max, ar[c]);
            return max;
        }

        public static long Max(long[] ar)
        {
            long max = 0;
            for (int c = 0; c < ar.Length; c++)
                max = Math.Max(max, ar[c]);
            return max;
        }

        public static ulong Max(ulong[] ar)
        {
            ulong max = 0;
            for (int c = 0; c < ar.Length; c++)
                max = Math.Max(max, ar[c]);
            return max;
        }

        public static long Length(Array[] ars)
        {
            long total = 0;
            for (int c = 0; c < ars.Length; c++)
                total += ars.Length;
            return total;
        }

        public static byte[] TransformToByte(int[] ar)
        {
            var us = new byte[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (byte)ar[c];
            return us;
        }

        public static ushort[] TransformToUshort(ulong[] ar)
        {
            var us = new ushort[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (ushort)ar[c];
            return us;
        }

        public static ushort[] TransformToUshort(uint[] ar)
        {
            var us = new ushort[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (ushort)ar[c];
            return us;
        }

        public static uint[] TransformToUInt(ulong[] ar)
        {
            var us = new uint[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (uint)ar[c];
            return us;
        }

        public static uint[] TransformToUInt(ushort[] ar)
        {
            var us = new uint[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (uint)ar[c];
            return us;
        }

        public static ulong[] TransformToULong(uint[] ar)
        {
            var us = new ulong[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (ulong)ar[c];
            return us;
        }

        public static ulong[] TransformToULong(ushort[] ar)
        {
            var us = new ulong[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (ulong)ar[c];
            return us;
        }

        public static ulong[] TransformToULong(byte[] ar)
        {
            var us = new ulong[ar.Length];
            for (int c = 0; c < us.Length; c++)
                us[c] = (ulong)ar[c];
            return us;
        }

        /// <summary>
        /// Compares two arrays for equality
        /// </summary>
        public static bool ArraysEqual<T>(T[] a1, T[] a2)
        {
            if (ReferenceEquals(a1, a2))
                return true;

            if (a1 == null || a2 == null)
                return false;

            if (a1.Length != a2.Length)
                return false;

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a1.Length; i++)
            {
                if (!comparer.Equals(a1[i], a2[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Compares two arrays for equality
        /// </summary>
        public static bool ArraysEqual<T>(T[] a1, T[] a2, int length)
        {
            if (ReferenceEquals(a1, a2))
                return true;

            if (a1 == null || a2 == null)
                return false;

            if (Math.Min(a1.Length, length) != Math.Min(a2.Length, length))
                return false;

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < length; i++)
            {
                if (!comparer.Equals(a1[i], a2[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Reverses the bytes in the encoded data. Use a lookup table if
        /// this function needs to be faster.
        /// </summary>
        /// <remarks>
        /// This function is used to support little endian tif files.
        /// </remarks>
        public static void ReverseData(byte[] bytes)
        {
            if (bytes == null) return;

            for (int c = 0; c < bytes.Length; c++)
            {
                byte val = bytes[c];
                byte ret = 0;
                for (byte i = 0; i < 8; i++)
                    ret = (byte)(ret << 1 | (val & (((byte)1) << i)) >> i);
                bytes[c] = ret;
            }
        }

        /// <summary>
        /// Creates a reverse lookup dictionary
        /// </summary>
        public static Dictionary<V, K> Reverse<K, V>(Dictionary<K, V> dict)
        {
            var r = new Dictionary<V, K>(dict.Count);
            foreach (var kp in dict)
                r.Add(kp.Value, kp.Key);
            return r;
        }
    }
}
