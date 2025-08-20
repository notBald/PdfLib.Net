using System;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;

using System.Resources;
using System.Globalization;

namespace PdfLib.Res
{
    /// <summary>
    /// String resources
    /// </summary>
    public class StrRes : Dictionary<int, StrRes.Msg>
    {
        /// <summary>
        /// Loads a string resource file
        /// </summary>
        /// <param name="source">Source path</param>
        /// <param name="res_name">Relative path and name</param>
        /// <returns>The string resources</returns>
        public static StrRes Load(string res_name)
        {
            var ret = new StrRes();
            string name = ret.GetType().FullName;
            StreamReader sr = GetResourceImpl(name.Substring(0, name.LastIndexOf('.')) + ".Text." + res_name);
            StringBuilder sb = new StringBuilder();
            
            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine().TrimStart();
                int comment = line.IndexOf('#');
                if (comment != -1) line = line.Substring(0, comment).TrimEnd();
                if (line.Length == 0) continue;
            again:                

                //Assumes number.
                int str_pos = line.IndexOf(':');
                int num = int.Parse(line.Substring(0, str_pos), 
                    System.Globalization.NumberStyles.AllowHexSpecifier);
                string header = line.Substring(str_pos+1, line.Length - str_pos-1);

                //Body
                sb.Length = 0;
                while (!sr.EndOfStream)
                {
                    line = sr.ReadLine().TrimStart();
                    comment = line.IndexOf('#');
                    if (comment != -1) line = line.Substring(0, comment).TrimEnd();
                    if (line.Length == 0) continue;
                    char ch = line[0];
                    if (ch != '-')
                    {
                        ret.Add(num, new Msg(header, (sb.Length != 0) ? sb.ToString() : null));
                        goto again;
                    }

                    if (line[1] == '|') sb.AppendLine();
                    else if (sb.Length != 0) sb.Append(' ');
                    if (line.Length == 2) continue;
                    sb.Append(line.Substring(2).TrimEnd());
                }
            }

            sr.Close();
            //ret._res = dict;
            return ret;
        }

        [DebuggerDisplay("{Caption}")]
        public struct Msg
        {
            public readonly string Caption;
            public readonly string Text;
            public Msg(string caption, string text)
            { Caption = caption; Text = text; }
        }

        public static StreamReader GetResource(string name)
        {
            string full_name = new Fudge().GetType().FullName;
            string path = full_name.Substring(0, full_name.LastIndexOf('.')+1);
            return GetResourceImpl(path + name);
        }

        /// <summary>
        /// Loads a resource
        /// </summary>
        /// <param name="FullPath">The full path to the resource</param>
        /// <returns>A stream reader with the resource</returns>
        public static Stream GetBinResource(string name)
        {
            string full_name = new Fudge().GetType().FullName;
            string path = full_name.Substring(0, full_name.LastIndexOf('.') + 1);
            string FullPath = path + name;

            Assembly Asm = Assembly.GetExecutingAssembly();
            //string[] names = Asm.GetManifestResourceNames();
            return Asm.GetManifestResourceStream(FullPath);
        }

        /// <summary>
        /// Loads a resource
        /// </summary>
        /// <param name="FullPath">The full path to the resource</param>
        /// <returns>A stream reader with the resource</returns>
        private static StreamReader GetResourceImpl(string FullPath)
        {
            Assembly Asm = Assembly.GetExecutingAssembly();
            //string[] names = Asm.GetManifestResourceNames();
            Stream strm = Asm.GetManifestResourceStream(FullPath);
            return strm != null ? new StreamReader(strm) : null;
        }

        public static Uri MakeShaderUri(string relativeFile)
        {
            Assembly a = typeof(StrRes).Assembly;
            var assemblyShortName = a.ToString().Split(',')[0];

            //var all = a.GetManifestResourceNames();
            //var rs = new ResourceReader(a.GetManifestResourceStream("PdfLib.g.resources"));

            string uriString = "pack://application:,,,/" + assemblyShortName + ";component/Res/Shader/" + relativeFile;
            return new Uri(uriString);
        }
    }

    /// <summary>
    /// Until I find a better way to locate resources
    /// </summary>
    class Fudge
    { }
}
