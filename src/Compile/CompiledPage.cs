using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Font;
using PdfLib.Render;
using PdfLib.Render.PDF;
using PdfLib.Render.Commands;
using PdfLib.Pdf.Annotation;
using PdfLib.Pdf.Transparency;
using PdfLib.Compile.Analyze;

namespace PdfLib.Compile
{
    public class CompiledPage : IExecutableImpl
    {
        #region Variables and properties

        /// <summary>
        /// A flattened media box. Can be used without
        /// checking for references.
        /// </summary>
        public readonly PdfRectangle MediaBox;

        /// <summary>
        /// A flattened crop box. Can be "null".
        /// </summary>
        internal readonly PdfRectangle CropBox;

        /// <summary>
        /// How this page is rotated
        /// </summary>
        public readonly int Rotate;

//#if DEBUG
        public RenderCMD[] Commands;
//#else
//        internal readonly RenderCMD[] Commands;
//#endif

        internal readonly CompiledAnnotation[] Annotations;

        /// <summary>
        /// The preicision needed to save these commands without
        /// loss of precision.
        /// </summary>
        internal readonly int DetectedPresicion;

        #endregion

        #region Init

        internal CompiledPage(PdfPage page, RenderCMD[] cmds, CompiledAnnotation[] annots, int prec)
        {
            MediaBox = page.MediaBox.Flatten();
            CropBox = page.GetCropBox();
            if (CropBox != null) CropBox = CropBox.Flatten();
            Commands = cmds;
            Rotate = page.Rotate;
            Annotations = annots;
            DetectedPresicion = prec;
        }

        internal CompiledPage(CompiledPage cpage, RenderCMD[] cmds, bool include_annotations)
        {
            MediaBox = cpage.MediaBox;
            CropBox = cpage.CropBox;
            Commands = cmds;
            Rotate = cpage.Rotate;
            if (include_annotations)
                Annotations = cpage.Annotations;
            DetectedPresicion = cpage.DetectedPresicion;
        }

        #endregion

        #region Public methods and properties

        /// <summary>
        /// How many image draw commands there are. Does not
        /// include inline images.
        /// </summary>
        public int NrImageCommands
        {
            get
            {
                int nr = 0;
                foreach(RenderCMD cmd in Enumerator)
                    if (cmd is Do_CMD)
                        nr++;
                return nr;
            }
        }

        /// <summary>
        /// How many text strings are drawn
        /// </summary>
        public int NrTextCommands
        {
            get
            {
                int nr = 0;
                foreach (RenderCMD cmd in Enumerator)
                    if (cmd.Type == RenderCMD.CMDType.Text)
                        nr++;
                return nr;
            }
        }

        /// <summary>
        /// How many paths, forms and inline images are drawn
        /// </summary>
        public int NrDrawCommands
        {
            get
            {
                int nr = 0;
                foreach (RenderCMD cmd in Enumerator)
                    if (cmd.Type == RenderCMD.CMDType.Paint ||
                        cmd.Type == RenderCMD.CMDType.Form ||
                        cmd is BI_CMD)
                        nr++;
                return nr;
            }
        }

        /// <summary>
        /// Converts this Compiled page into a PdfPage
        /// </summary>
        /// <returns>PdfPage</returns>
        public PdfPage MakePage()
        {
            var page = new PdfPage();
            Write.WritableDocument.AddCompiledPage(page, this);
            return page;
        }

        #endregion

        #region Enumerator

        /// <summary>
        /// For enumerating commands in a foreach, taking into account marked
        /// contents so that one get a single stream of commands.
        /// </summary>
        private CMDEnumerator Enumerator { get { return new CMDEnumerator(Commands); } }

        class CMDEnumerator : IEnumerator<RenderCMD>, IEnumerable<RenderCMD>
        {
            int c; RenderCMD[] _c; CMDEnumerator _child = null;
            public CMDEnumerator(RenderCMD[] cmds) { _c = cmds; }
            object System.Collections.IEnumerator.Current 
            { get { return (_child == null) ? _c[c] : _child.Current; } }
            public RenderCMD Current
            { get { return (_child == null) ? _c[c] : _child.Current; } }
            public bool MoveNext() 
            {
                if (_child != null)
                {
                    if (_child.MoveNext()) return true;
                    _child = null;
                }
                if (++c >= _c.Length) return false;
                if (_c[c].Type == RenderCMD.CMDType.Special)
                {
                    _child = new CMDEnumerator(((BDC_CMD)_c[c])._cmds);
                    if (_child.MoveNext()) return true;
                    _child = null;
                }
                return true;
            }
            public void Reset() { c = 0; _child = null; }
            public void Dispose() { }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() 
            { return this; }
            public IEnumerator<RenderCMD> GetEnumerator() { return this; }
        }

        #endregion

        #region Debug
#if DEBUG
        public void Trim(int n_commands)
        {
            var r = new RenderCMD[n_commands];
            Array.Copy(Commands, r, n_commands);
            Commands = r;
        }

        public void Trim(int from, int to)
        {
            var r = new RenderCMD[to - from];
            Array.Copy(Commands, from, r, 0, r.Length);
            Commands = r;
        }

        public void ReplaceTJ(int pos, params byte[] val)
        {
            Commands[pos] = new TJ_CMD(new object[] { new PdfString(val) });
        }

        /// <summary>
        /// Removes x number of q, including any commands ahead of it. Stops automatically
        /// at cm that is not wrapped in a q
        /// 
        /// If set to zero it will only remove commands ahead of q
        /// </summary>
        /// <returns>Removed commands</returns>
        public RenderCMD[] RemoveQ(int from, int nr)
        {
            if (nr == 0) return Remove<q_RND, q_RND>(from, -3);
            var r_cmds = new RenderCMD[0];
            for (int c = 0; c < nr; c++)
            {
                var rem = Remove<q_RND, Q_RND>(from, -4);
                int pos = r_cmds.Length;
                Array.Resize<RenderCMD>(ref r_cmds, pos + rem.Length);
                Array.Copy(rem, 0, r_cmds, pos, rem.Length);
            }
            return r_cmds;
        }

        public void RemoveToQ(int from)
        {
            int to = from;
            for (; to < Commands.Length; to++)
            {
                if (Commands[to] is q_RND)
                {
                    to--;
                    break;
                }
            }
            if (from <= to)
                Remove(from, to);
        }

        public void StripEarilerQ(int pos)
        {
            //1. Find the q level of position
            int q_level = 0;
            for(int c=0; c <= pos; c++)
            {
                var cmd = Commands[c];
                if (cmd is q_RND)
                    q_level++;
                else if (cmd is Q_RND)
                    q_level--;
            }

            //2. Strip all Q on a higer level
            int q_strip = q_level;
            q_level = 0;
            var cmds_l = Commands.Length;
            for(int c=0; c < pos; c++)
            {
                var cmd = Commands[c];
                if (cmd is q_RND)
                {
                    q_level++;

                    if (q_level == q_strip)
                    {
                        RemoveQ(c, 1);
                        pos -= cmds_l - Commands.Length;
                        q_level--;
                        cmds_l = Commands.Length;
                        c--;
                    }
                }
                else if (cmd is Q_RND)
                    q_level--;
            }
        }

        public void RemoveToBT(int from)
        {
            int to = from;
            for (; to < Commands.Length; to++)
            {
                if (Commands[to] is BT_CMD)
                {
                    to--;
                    break;
                }
            }
            if (from <= to)
                Remove(from, to);
        }
        public void RemoveBT(int from, int nr)
        {
            if (nr == 0) Remove<BT_CMD, BT_CMD>(from, -3);
            for (int c = 0; c < nr; c++)
                Remove<BT_CMD, ET_CMD>(from, -4);
        }

        public void Remove(int from, int to) { Remove<q_RND, Q_RND>(from, to); }

        internal RenderCMD[] Remove<FROM, TO>(int from, int to)
        {
            if (to >= Commands.Length) to = Commands.Length - 1;

            if (to < 0)
            {
                if (to == -2) //Kept For compability
                {
                    int q = 0;
                    for (to = from + 1; to < Commands.Length - 1; to++)
                    {
                        if (Commands[to] is FROM)
                            q++;
                        else if (Commands[to] is TO)
                        {
                            q--;
                            if (q < 0)
                            { to--; break; }
                        }
                        else if (q == 0 && Commands[to] is cm_RND)
                        { to--; break; }
                    }
                }
                else if (to == -3)
                {
                    for (to = from; to < Commands.Length - 1; to++)
                    {
                        if (Commands[to] is TO)
                        {
                            to--;
                            break;
                        }
                    }
                }
                else if (to == -4)
                {
                    int q = 0;
                    for (to = from; to < Commands.Length - 1; to++)
                    {
                        if (Commands[to] is FROM)
                            q++;
                        else if (Commands[to] is TO)
                        {
                            q--;
                            if (q <= 0)
                                break;
                        }
                        else if (q == 0 && Commands[to] is cm_RND)
                        { to--; break; }
                    }
                }

            }
            var r = new RenderCMD[Commands.Length - (to - from + 1)];
            var rem = new RenderCMD[to - from + 1];
            Array.Copy(Commands, 0, r, 0, from);
            Array.Copy(Commands, from, rem, 0, rem.Length);
            Array.Copy(Commands, to + 1, r, from, r.Length - from);
            Commands = r;
            return rem;
        }

        public void Flatten()
        {
            int true_size = 0;
            for (int c = 0; c < Commands.Length; c++)
            {
                if (Commands[c] is BDC_CMD)
                    true_size += ((BDC_CMD)Commands[c])._cmds.Length;
                else
                    true_size++;
            }
            var r = new RenderCMD[true_size];
            for (int c = 0, wc = 0; c < Commands.Length; c++)
            {
                if (Commands[c] is BDC_CMD)
                {
                    var cmds = ((BDC_CMD)Commands[c])._cmds;
                    Array.Copy(cmds, 0, r, wc, cmds.Length);
                    wc += cmds.Length;
                }
                else
                    r[wc++] = Commands[c];
            }
            if (true_size != Commands.Length)
            {
                Commands = r;
                Flatten();
            }
            else
            {
                Commands = r;
            }
        }

        /// <summary>
        /// Puts CM as the first commando
        /// </summary>
        public void InjectCM(double scale, double off_x, double off_y)
        {
            var r = new RenderCMD[Commands.Length + 1];
            Array.Copy(Commands, 0, r, 1, Commands.Length);
            r[0] = new cm_RND(new xMatrix(scale, 0, 0, scale, off_x, off_y));
            Commands = r;
        }

        public void Replace_m(double x, double y, int pos)
        {
            if (!(Commands[pos] is m_CMD))
                throw new Exception("Wrong command.");
            Console.Write(Commands[pos].ToString());
            Commands[pos] = new m_CMD(y, x);
        }

        public void Replace_l(double x, double y, int pos)
        {
            if (!(Commands[pos] is l_CMD))
                throw new Exception("Wrong command.");
            Console.Write(Commands[pos].ToString());
            Commands[pos] = new l_CMD(y, x);
        }

        public void Inject_l(double x, double y, int pos)
        {
            InjectCMD(new l_CMD(y, x), pos);
        }

        public void InjectS()
        {
            AppendCMD(new S_CMD());
        }

        public void InjectS(int pos)
        {
            InjectCMD(new S_CMD(), pos);
        }

        /// <summary>
        /// Sets the fillcolor
        /// </summary>
        /// <param name="pos">Position of the command</param>
        /// <param name="gray">Gray color</param>
        public void Injectg(int pos, double gray = 0)
        {
            InjectCMD(new g_CMD(gray), pos);
        }

        /// <summary>
        /// Sets the strokecolor
        /// </summary>
        /// <param name="pos">Position of the command</param>
        /// <param name="gray">Gray color</param>
        public void InjectG(int pos, double gray = 0)
        {
            InjectCMD(new G_CMD(gray), pos);
        }

        public void InjectET()
        {
            AppendCMD(new ET_CMD());
        }

        public void InjectCMD(RenderCMD cmd, int pos)
        {
            Array.Resize<RenderCMD>(ref Commands, Commands.Length + 1);
            Array.Copy(Commands, pos, Commands, pos + 1, Commands.Length - pos - 1);
            Commands[pos] = cmd;
        }

        private void AppendCMD(RenderCMD cmd)
        {
            Array.Resize<RenderCMD>(ref Commands, Commands.Length + 1);
            Commands[Commands.Length - 1] = cmd;
        }

        public void RemoveAllSh(int from, int to)
        {
            if (to < from) to = Commands.Length - 1;
            else to = Math.Min(to, Commands.Length - 1);
            for (int c = from; c <= to; c++)
            {
                if (Commands[c] is sh_CMD)
                {
                    var r = new RenderCMD[Commands.Length - 1];
                    Array.Copy(Commands, 0, r, 0, c);
                    Array.Copy(Commands, c+1, r, c, r.Length - c);
                    Commands = r; c--; to--;
                }
            }
        }

        public void RemoveAllImages()
        {
            for (int c=0; c < Commands.Length; c++)
            {
                if (Commands[c] is Do_CMD || Commands[c] is BI_CMD)
                {
                    //Finds q
                    int start = c;
                    while (start >= 0 && !(Commands[start] is q_RND))
                        start--;

                    int end = c;
                    while (end < Commands.Length && !(Commands[end] is Q_RND))
                        end++;
                    Remove(start, end);

                    c = start;
                }
            }
        }

        public void RemoveAllVectors()
        {
            for (int c = 0; c < Commands.Length; c++)
            {
                if (Commands[c].Type == RenderCMD.CMDType.Paint)
                {
                    //Finds q
                    int start = c;
                    while (start >= 0 && !(Commands[start] is q_RND))
                        start--;

                    if (start == -1)
                    {
                        Remove(c, c);
                        continue;
                    }

                    int end = c;
                    while (end < Commands.Length && !(Commands[end] is Q_RND))
                        end++;

                    if (Remove(start, end, RenderCMD.CMDType.Image))
                        c = start;
                }
            }
        }

        private bool Remove(int start, int end, RenderCMD.CMDType not_type)
        {
            for (int c = start; c <= end; c++)
                if (Commands[c].Type == not_type)
                    return false;

            Remove(start, end);

            return true;
        }

        public void RemoveAllText()
        {
            for (int c = 0; c < Commands.Length; c++)
            {
                if (Commands[c] is BT_CMD)
                {
                    //Finds BT
                    int start = c;

                    //Finds ET
                    int end = c;
                    while (end < Commands.Length && !(Commands[end] is ET_CMD))
                        end++;
                    Remove(start, end);

                    c = start;
                }
            }
        }
#endif
        #endregion

        #region IExecutable

        RenderCMD[] IExecutableImpl.Commands { get { return Commands; } }

        #endregion
    }

    public class CompiledForm : IForm
    {
        public readonly RenderCMD[] Commands;
        public readonly xRect BBox;
        public readonly xMatrix Matrix;
        internal readonly PdfTransparencyGroup Group;
        internal CompiledForm(RenderCMD[] cmds, PdfRectangle bbox, xMatrix transform, PdfTransparencyGroup group)
        {
            Commands = cmds;
            BBox = new xRect(bbox);
            Matrix = transform;
            Group = group;
        }
    }

    internal class CompiledAppearance
    {
        /// <summary>
        /// Normal appearance
        /// </summary>
        internal readonly CompiledForms Normal;

        /// <summary>
        /// Apperance when mouse hovers
        /// </summary>
        internal readonly CompiledForms Rollover;

        /// <summary>
        /// Apperance when pressed down
        /// </summary>
        internal readonly CompiledForms Down;

        /// <summary>
        /// Apperance to render.
        /// </summary>
        internal string SelectedApperance;

        internal CompiledAppearance(CompiledForms n, CompiledForms r, CompiledForms d, string selected_apperance)
        { Normal = n; Rollover = r; Down = d; SelectedApperance = selected_apperance; }
    }

    internal class CompiledAnnotation
    {
        /// <summary>
        /// These commands draws the annotation. To be ignored
        /// when there is an apperance stream.
        /// </summary>
        internal readonly RenderCMD[] Apperance;

        /// <summary>
        /// An apperance stream is commands embeded straight into the PDF document.
        /// </summary>
        internal readonly CompiledAppearance ApperanceStream;

        /// <summary>
        /// Annotation flags
        /// </summary>
        public readonly AFlags Flags;

        /// <summary>
        /// Size of the annotation
        /// </summary>
        public readonly xRect Clip, Rect;

        public readonly string Subtype;

        internal CompiledAnnotation(RenderCMD[] apperance, AnnotationFlags f, xRect clip, xRect rect, string subtype)
        {
            Apperance = apperance;
            Flags = new AFlags(f);
            Clip = clip;
            Rect = rect;
            Subtype = subtype;
        }

        internal CompiledAnnotation(CompiledAppearance apperance_stream, AnnotationFlags f, xRect rect, string subtype)
        {
            ApperanceStream = apperance_stream;
            Rect = Clip = rect;
            Flags = new AFlags(f);
            Subtype = subtype;
        }

        public struct AFlags
        {
            private AnnotationFlags F;
            /// <summary>
            /// Do not display "annotation missing" graphics when this annotation is
            /// rendered.
            /// </summary>
            public bool Invisible { get { return (F & AnnotationFlags.Invisible) != 0; } }
            /// <summary>
            /// Do not display or print this annotation
            /// </summary>
            public bool Hidden { get { return (F & AnnotationFlags.Hidden) != 0; } }
            /// <summary>
            /// Only print annotations with this flag set
            /// </summary>
            public bool Print { get { return (F & AnnotationFlags.Print) != 0; } }
            /// <summary>
            /// Do not scale the annotation’s appearance to match the magnification of the page
            /// </summary>
            public bool NoZoom { get { return (F & AnnotationFlags.NoZoom) != 0; } }
            /// <summary>
            /// Do not rotate the annotation’s appearance to match the rotation of the page
            /// </summary>
            public bool NoRotate { get { return (F & AnnotationFlags.NoRotate) != 0; } }
            /// <summary>
            /// If set, do not show this annotation on screen (it can still be printed)
            /// </summary>
            public bool NoView { get { return (F & AnnotationFlags.NoView) != 0; } }
            /// <summary>
            /// The annotation may not be moved or modified
            /// </summary>
            public bool ReadOnly { get { return (F & AnnotationFlags.ReadOnly) != 0; } }
            /// <summary>
            /// The annotation may not be moved or deleted, but its contents can still be modified
            /// </summary>
            public bool Locked { get { return (F & AnnotationFlags.Locked) != 0; } }
            /// <summary>
            /// Invert the interpretation of the NoView flag for certain events
            /// </summary>
            public bool ToggleNoView { get { return (F & AnnotationFlags.Locked) != 0; } }
            /// <summary>
            /// The annotation's contents may not be modified
            /// </summary>
            public bool LockedContents { get { return (F & AnnotationFlags.LockedContents) != 0; } }
            public AFlags(AnnotationFlags f)
            { F = f; }
        }
    }

    internal class CompiledForms
    {
        public readonly CompiledForm[] Forms;
        public readonly string[] FormNames;
        internal CompiledForms(CompiledForm[] forms, string[] names)
        {
            Forms = forms;
            FormNames = names;
        }
        internal CompiledForms(CompiledForm form)
            : this(new CompiledForm[] { form }, null)
        { }
    }

    public class CompiledPattern : IForm
    {
        public readonly RenderCMD[] Commands;
        public readonly PdfRectangle BBox;
        public readonly xMatrix Matrix;
        public readonly double XStep;
        public readonly double YStep;
        public readonly PdfPaintType PaintType;
        public readonly PdfTilingType TilingType;
        internal CompiledPattern(RenderCMD[] cmds, PdfRectangle bbox, xMatrix transform,
            double xstep, double ystep, PdfPaintType pt, PdfTilingType tl)
        {
            Commands = cmds;
            BBox = bbox;
            Matrix = transform;
            XStep = xstep;
            YStep = ystep;
            PaintType = pt;
            TilingType = tl;
        }
        public PdfTilingPattern Make(int precision)
        {
            PdfTilingPattern pattern = new PdfTilingPattern(BBox,
                XStep, YStep, PaintType, TilingType);
            pattern.Matrix = Matrix;
            using (var form_draw = new DrawPage(pattern))
            {
                form_draw.Precision = precision;
                form_draw.Execute(Commands);
            }
            return pattern;
        }
    }

    public class CompiledFont : IForm
    {
        internal readonly PdfType3Font Font;
        internal readonly Dictionary<string, RenderCMD[]> Glyphs;
        internal CompiledFont(PdfType3Font font, Dictionary<string, RenderCMD[]> glyphs)
        { Font = font; Glyphs = glyphs; }
        public Render.Font.Type3Font Realize() { return Font.Realize(Glyphs); }
    }

    /// <summary>
    /// Not truly used beyond type checking.
    /// </summary>
    internal interface IForm { }
}
